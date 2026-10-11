using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

// Requirements section 4: sending, preference responses, change requests, approvals, majority nudging.
public class AgreementService(
    VamooseDbContext db,
    TripAccess access,
    IAgentClient agent,
    MagicLinkService magicLinks,
    NotificationService notifications,
    ILogger<AgreementService> logger)
{
    public static readonly TimeSpan ResponseWindow = TimeSpan.FromHours(24);
    private const int MaxNudges = 2;

    private record Tally(PlanItem Item, Dictionary<Guid, int> Counts, int Answered)
    {
        public (Guid OptionId, int Count) Top => Counts.OrderByDescending(c => c.Value).Select(c => (c.Key, c.Value)).FirstOrDefault();
        public bool IsTie => Counts.Count >= 2 && Counts.Values.OrderDescending().Take(2).Distinct().Count() == 1;
        public bool HasMajority => Top.Count * 2 > Answered;
    }

    public static bool WindowOpen(Trip trip) =>
        trip.Status == TripStatus.Agreement && (trip.ResponseDeadline is not { } deadline || DateTimeOffset.UtcNow <= deadline);

    // Everyone must approve; once the 24-hour window closes, only those who responded are considered.
    public static bool HasConsensus(Trip trip, IReadOnlySet<Guid> activeIds, IReadOnlyDictionary<Guid, bool> approvals)
    {
        var considered = WindowOpen(trip) ? activeIds : activeIds.Where(approvals.ContainsKey).ToHashSet();
        return considered.Count > 0 && considered.All(id => approvals.TryGetValue(id, out var ok) && ok);
    }

    // Finalizing and sending are the same action (requirements 3.5).
    public async Task SendAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement);
        var trip = ctx.Trip;
        if (trip.Items.Count == 0)
        {
            throw new ConflictException("Plan the trip with the agent before sending it.");
        }
        if (!ctx.OtherActiveMemberIds.Any())
        {
            throw new ConflictException("Add at least one traveler before sending the trip.");
        }

        var resend = trip.SentAt != null;
        trip.Status = TripStatus.Agreement;
        trip.SentAt = DateTimeOffset.UtcNow;
        await ResetApprovalsAsync(trip, ct);
        await GenerateQuestionsAsync(trip, ct);

        // Each traveler's magic link is their only way in (requirements 3.2).
        foreach (var member in ctx.ActiveMembers.Where(m => m.Role == MemberRole.Traveler && m.LinkSentAt == null))
        {
            await magicLinks.SendAsync(trip, member, ct);
        }

        notifications.Notify(
            ctx.OtherActiveMemberIds,
            tripId,
            NotificationTypes.TripSent,
            resend
                ? $"{trip.PrimaryUser.Email} sent \"{trip.Name}\" again. You have 24 hours to review the plan."
                : $"{trip.PrimaryUser.Email} sent you the trip \"{trip.Name}\". You have 24 hours to answer a few questions.");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // Called whenever the primary user changes the plan. Approvals are cleared (requirements 4.3).
    public async Task OnPlanEditedAsync(TripContext ctx, string what, bool regenerateQuestions, CancellationToken ct)
    {
        var trip = ctx.Trip;
        if (trip.Status != TripStatus.Agreement)
        {
            trip.PlanVersion++;
            return;
        }

        await ResetApprovalsAsync(trip, ct);
        await db.ChangeRequests
            .Where(r => r.TripId == trip.Id && r.Status == ChangeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChangeRequestStatus.Superseded), ct);
        if (regenerateQuestions)
        {
            await GenerateQuestionsAsync(trip, ct);
        }

        notifications.Notify(
            ctx.OtherActiveMemberIds,
            trip.Id,
            NotificationTypes.TripEdited,
            $"{what} on \"{trip.Name}\". Please review and approve the plan again within 24 hours.");
    }

    public async Task RespondAsync(Guid tripId, IReadOnlyList<ChoiceDto> choices, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequireActive(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Agreement);
        var trip = ctx.Trip;
        if (!WindowOpen(trip))
        {
            throw new ConflictException("The 24-hour response window for this plan has closed.");
        }

        var me = ctx.Me.UserId;
        var now = DateTimeOffset.UtcNow;
        var byItem = choices.GroupBy(c => c.ItemId).ToDictionary(g => g.Key, g => g.Last().OptionId);
        foreach (var (itemId, optionId) in byItem)
        {
            var item = trip.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new BadRequestException("A plan item in your answers no longer exists. Reload and try again.");
            if (item.Options.All(o => o.Id != optionId))
            {
                throw new BadRequestException($"That option isn't available for {item.Title}.");
            }
        }

        var existing = await db.ItemChoices.Where(c => c.TripId == tripId && c.UserId == me).ToDictionaryAsync(c => c.PlanItemId, ct);
        var myPending = await db.ChangeRequests
            .Where(r => r.TripId == tripId && r.RequestedByUserId == me && r.Status == ChangeRequestStatus.Pending)
            .ToListAsync(ct);

        var primaryEdits = new List<string>();
        foreach (var item in trip.Items)
        {
            // Unanswered items count as accepting the current selection.
            var chosen = byItem.TryGetValue(item.Id, out var optionId) ? optionId : item.SelectedOptionId ?? item.Options[0].Id;
            if (existing.TryGetValue(item.Id, out var choice))
            {
                choice.OptionId = chosen;
                choice.UpdatedAt = now;
            }
            else
            {
                db.ItemChoices.Add(new ItemChoice { TripId = tripId, PlanItemId = item.Id, UserId = me, OptionId = chosen, UpdatedAt = now });
            }

            var pendingForItem = myPending.Where(r => r.PlanItemId == item.Id).ToList();
            if (chosen == item.SelectedOptionId)
            {
                pendingForItem.ForEach(r => r.Status = ChangeRequestStatus.Superseded);
                continue;
            }

            var option = item.Options.First(o => o.Id == chosen);
            if (ctx.IsPrimary)
            {
                // The primary user's choices are the plan, so they apply directly.
                item.SelectedOptionId = chosen;
                primaryEdits.Add($"{item.Title} changed to {option.Title}");
                continue;
            }

            if (pendingForItem.Any(r => r.ToOptionId == chosen))
            {
                continue;
            }
            pendingForItem.ForEach(r => r.Status = ChangeRequestStatus.Superseded);
            db.ChangeRequests.Add(new ChangeRequest
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                PlanItemId = item.Id,
                RequestedByUserId = me,
                FromOptionId = item.SelectedOptionId ?? item.Options[0].Id,
                ToOptionId = chosen,
                CreatedAt = now,
            });
            notifications.Notify(
                trip.PrimaryUserId,
                tripId,
                NotificationTypes.ChangeRequested,
                $"{ctx.Me.User.Email} wants {item.Title} changed to {option.Title}. Approve or deny it.");
        }

        if (primaryEdits.Count > 0)
        {
            await OnPlanEditedAsync(ctx, string.Join("; ", primaryEdits), regenerateQuestions: false, ct);
        }

        var approved = trip.Items.All(i => (byItem.TryGetValue(i.Id, out var o) ? o : i.SelectedOptionId) == i.SelectedOptionId);
        await UpsertApprovalAsync(trip, me, approved, ct);
        await db.SaveChangesAsync(ct);

        await EvaluateAsync(ctx, ct);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    public async Task DecideChangeAsync(Guid tripId, Guid requestId, bool approve, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Agreement);

        var request = await db.ChangeRequests.Include(r => r.RequestedBy).FirstOrDefaultAsync(r => r.Id == requestId && r.TripId == tripId, ct)
            ?? throw new NotFoundException("Change request not found.");
        if (request.Status != ChangeRequestStatus.Pending)
        {
            throw new ConflictException("This change request was already handled.");
        }

        var item = ctx.Trip.Items.First(i => i.Id == request.PlanItemId);
        var from = item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId)?.Title ?? "the current option";
        var to = item.Options.First(o => o.Id == request.ToOptionId).Title;
        request.DecidedAt = DateTimeOffset.UtcNow;

        if (!approve)
        {
            request.Status = ChangeRequestStatus.Denied;
            notifications.Notify(request.RequestedByUserId, tripId, NotificationTypes.ChangeDenied,
                $"Your change to {item.Title} ({to}) on \"{ctx.Trip.Name}\" was denied.");
            notifications.TripChanged([ctx.Me.UserId], tripId);
            await db.SaveChangesAsync(ct);
            await notifications.FlushAsync(ct);
            return;
        }

        request.Status = ChangeRequestStatus.Approved;
        item.SelectedOptionId = request.ToOptionId;
        await db.ChangeRequests
            .Where(r => r.PlanItemId == item.Id && r.Id != request.Id && r.Status == ChangeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChangeRequestStatus.Superseded), ct);

        await ResetApprovalsAsync(ctx.Trip, ct);
        notifications.Notify(ctx.OtherActiveMemberIds, ctx.Trip.Id, NotificationTypes.ChangeApproved,
            $"Plan changed on \"{ctx.Trip.Name}\": {item.Title} is now {to} (was {from}). Please approve or deny the updated plan within 24 hours.");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // Closes response windows that have run out and evaluates them with whoever responded in time.
    public async Task CloseExpiredWindowsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.Trips
            .Where(t => t.Status == TripStatus.Agreement && t.ResponseDeadline < now && (t.ClosedVersion == null || t.ClosedVersion != t.PlanVersion))
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var tripId in due)
        {
            var ctx = await access.LoadForSystemAsync(tripId, ct);
            var trip = ctx.Trip;
            trip.ClosedVersion = trip.PlanVersion;

            var activeIds = ctx.ActiveMemberIds.ToHashSet();
            var responded = await db.PlanApprovals.CountAsync(a => a.TripId == tripId && a.PlanVersion == trip.PlanVersion && activeIds.Contains(a.UserId), ct);
            notifications.Notify(trip.PrimaryUserId, tripId, NotificationTypes.WindowClosed,
                $"The 24-hour response window for \"{trip.Name}\" closed. {responded} of {activeIds.Count} responded.");
            await EvaluateAsync(ctx, ct);
            await db.SaveChangesAsync(ct);
            await notifications.FlushAsync(ct);
        }
    }

    // Majority nudges, ties for the primary user, analysis when responses are in, consensus alert.
    public async Task EvaluateAsync(TripContext ctx, CancellationToken ct)
    {
        var trip = ctx.Trip;
        if (trip.Status != TripStatus.Agreement || trip.Items.Count == 0)
        {
            return;
        }

        var activeIds = ctx.ActiveMemberIds.ToHashSet();
        var tallies = await TallyAsync(trip, activeIds, ct);
        var choices = await db.ItemChoices.Where(c => c.TripId == trip.Id && activeIds.Contains(c.UserId)).ToListAsync(ct);
        var nudges = await db.Nudges.Where(n => n.TripId == trip.Id).ToListAsync(ct);
        var windowOpen = WindowOpen(trip);

        foreach (var tally in tallies.Where(t => t.Answered >= 2))
        {
            if (tally.IsTie)
            {
                await NotifyOnceAsync(trip.PrimaryUserId, trip.Id, NotificationTypes.Tie,
                    $"It's a tie on {tally.Item.Title} for \"{trip.Name}\". You have the deciding vote.", ct);
                continue;
            }
            if (!tally.HasMajority || !windowOpen)
            {
                continue;
            }

            var (majorityId, majorityCount) = tally.Top;

            // The primary user has the final word, so only travelers are nudged.
            foreach (var choice in choices.Where(c => c.PlanItemId == tally.Item.Id && c.OptionId != majorityId && c.UserId != trip.PrimaryUserId))
            {
                var nudge = nudges.FirstOrDefault(n => n.UserId == choice.UserId && n.PlanItemId == tally.Item.Id);
                if (nudge is { } existing && existing.PlanVersion == trip.PlanVersion && existing.Count > 0)
                {
                    continue;
                }
                await SendFirstNudgeAsync(trip, tally, choice, majorityId, majorityCount, nudge, ct);
            }
        }

        var approvals = await db.PlanApprovals
            .Where(a => a.TripId == trip.Id && a.PlanVersion == trip.PlanVersion && activeIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, a => a.Approved, ct);
        if (windowOpen && approvals.Count < activeIds.Count)
        {
            return;
        }

        if (HasConsensus(trip, activeIds, approvals))
        {
            await NotifyOnceAsync(trip.PrimaryUserId, trip.Id, NotificationTypes.Consensus,
                $"Everyone who responded approved the plan for \"{trip.Name}\". You can lock the trip.", ct);
        }
        await AnalyzeAsync(ctx, tallies, choices, approvals, ct);
    }

    // Returns context for the second nudge if the user is still in the minority after the first.
    public async Task<AgentNudgeContext?> TakeSecondNudgeAsync(TripContext ctx, CancellationToken ct)
    {
        var trip = ctx.Trip;
        if (!WindowOpen(trip))
        {
            return null;
        }

        var nudges = await db.Nudges
            .Where(n => n.TripId == trip.Id && n.UserId == ctx.Me.UserId && n.PlanVersion == trip.PlanVersion && n.Count == 1)
            .ToListAsync(ct);
        if (nudges.Count == 0)
        {
            return null;
        }

        var activeIds = ctx.ActiveMemberIds.ToHashSet();
        var tallies = await TallyAsync(trip, activeIds, ct);
        var mine = await db.ItemChoices.Where(c => c.TripId == trip.Id && c.UserId == ctx.Me.UserId).ToDictionaryAsync(c => c.PlanItemId, c => c.OptionId, ct);
        foreach (var nudge in nudges)
        {
            var tally = tallies.FirstOrDefault(t => t.Item.Id == nudge.PlanItemId);
            if (tally is null || !tally.HasMajority || !mine.TryGetValue(nudge.PlanItemId, out var myOption) || myOption == tally.Top.OptionId)
            {
                continue;
            }

            nudge.Count = MaxNudges;
            return BuildNudge(tally, myOption, tally.Top.OptionId, tally.Top.Count, MaxNudges);
        }
        return null;
    }

    private async Task SendFirstNudgeAsync(Trip trip, Tally tally, ItemChoice choice, Guid majorityId, int majorityCount, Nudge? nudge, CancellationToken ct)
    {
        var context = BuildNudge(tally, choice.OptionId, majorityId, majorityCount, 1);
        AgentResult result;
        try
        {
            result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(), AgentActions.Nudge, Trip: AgentMapper.ToContext(trip), Plan: AgentMapper.ToPlan(trip), Nudge: context, TripId: trip.Id), ct);
        }
        catch (AgentUnavailableException ex)
        {
            // A missed nudge shouldn't fail the traveler's response; it is retried on the next evaluation.
            logger.LogWarning(ex, "Nudge skipped for trip {TripId}", trip.Id);
            return;
        }

        if (nudge is null)
        {
            nudge = new Nudge { TripId = trip.Id, UserId = choice.UserId, PlanItemId = tally.Item.Id };
            db.Nudges.Add(nudge);
        }
        nudge.PlanVersion = trip.PlanVersion;
        nudge.Count = 1;

        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            UserId = choice.UserId,
            Role = ChatRole.Agent,
            Content = result.Reply ?? $"Most of the group chose {context.MajorityChoice} for {context.SlotTitle}.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        notifications.Notify(choice.UserId, trip.Id, NotificationTypes.Nudge,
            $"Most of the group chose {context.MajorityChoice} for {tally.Item.Title} on \"{trip.Name}\". The Vamoose agent left you a note.");
    }

    // Travelers are sent to the model as anonymous labels and mapped back to emails here.
    private async Task AnalyzeAsync(TripContext ctx, List<Tally> tallies, List<ItemChoice> choices, Dictionary<Guid, bool> approvals, CancellationToken ct)
    {
        var trip = ctx.Trip;
        var active = ctx.ActiveMembers.ToList();
        var labels = active.Select((m, i) => (m, Label: $"traveler-{i + 1}")).ToDictionary(x => x.m.UserId, x => x.Label);
        var emails = active.ToDictionary(m => labels[m.UserId], m => m.User.Email);

        var responses = new AgentResponses(
            [.. approvals.Where(a => a.Value).Select(a => labels[a.Key])],
            [.. approvals.Where(a => !a.Value).Select(a => labels[a.Key])],
            [.. active.Where(m => !approvals.ContainsKey(m.UserId)).Select(m => labels[m.UserId])],
            [.. tallies.Select(t => new AgentSlotResponses(
                t.Item.Key,
                t.Item.Title,
                t.Item.Options.FirstOrDefault(o => o.Id == t.Item.SelectedOptionId)?.Title ?? "",
                [.. t.Item.Options.Where(o => o.Id != t.Item.SelectedOptionId).Select(o => o.Title)],
                [.. choices.Where(c => c.PlanItemId == t.Item.Id).Select(c => new AgentParticipantChoice(labels[c.UserId], t.Item.Options.First(o => o.Id == c.OptionId).Title))]))]);

        AgentResult result;
        try
        {
            result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(), AgentActions.Analyze, Trip: AgentMapper.ToContext(trip), Plan: AgentMapper.ToPlan(trip), Responses: responses, TripId: trip.Id), ct);
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "Analysis skipped for trip {TripId}", trip.Id);
            return;
        }
        if (result.Analysis is not { } analysis)
        {
            return;
        }

        string Names(IEnumerable<string> participants) =>
            string.Join(", ", participants.Select(p => emails.GetValueOrDefault(p)).OfType<string>());
        var lines = (analysis.Items ?? [])
            .Where(i => i.Dissenters.Count > 0)
            .Select(i =>
            {
                var title = trip.Items.FirstOrDefault(x => x.Key == i.SlotKey)?.Title ?? i.SlotKey;
                var alternatives = i.Alternatives.Count > 0 ? $" Alternatives that fit: {string.Join(", ", i.Alternatives)}." : "";
                return $"• {title}: {i.Approvers.Count} approve; {Names(i.Dissenters)} prefer something else.{alternatives}";
            });

        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            UserId = trip.PrimaryUserId,
            Role = ChatRole.Agent,
            Content = string.Join("\n", new[] { $"Responses are in. {analysis.Summary}" }.Concat(lines)),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        notifications.Notify(trip.PrimaryUserId, trip.Id, NotificationTypes.Analysis,
            analysis.WorksForAll
                ? $"Responses are in for \"{trip.Name}\" and the plan works for all."
                : $"Responses are in for \"{trip.Name}\", but the plan doesn't work for everyone yet. See the agent's summary.");
    }

    private async Task GenerateQuestionsAsync(Trip trip, CancellationToken ct)
    {
        try
        {
            var result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(), AgentActions.Questions, Trip: AgentMapper.ToContext(trip), Plan: AgentMapper.ToPlan(trip), TripId: trip.Id), ct);

            // Questions are checked against their fixed structure before use (requirements 4.1).
            var byKey = (result.Questions ?? [])
                .Where(q => q.Type == "SingleChoice" && !string.IsNullOrWhiteSpace(q.Prompt))
                .GroupBy(q => q.SlotKey)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var item in trip.Items)
            {
                item.Question = byKey.TryGetValue(item.Key, out var q) && q.Options.Count == item.Options.Count
                    ? q.Prompt[..Math.Min(q.Prompt.Length, 400)]
                    : DefaultQuestion(item);
            }
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "Question generation fell back to defaults for trip {TripId}", trip.Id);
            trip.Items.ForEach(i => i.Question = DefaultQuestion(i));
        }
    }

    private async Task<List<Tally>> TallyAsync(Trip trip, HashSet<Guid> activeIds, CancellationToken ct)
    {
        var choices = await db.ItemChoices.Where(c => c.TripId == trip.Id).ToListAsync(ct);
        return [.. trip.Items.Select(item =>
        {
            var votes = choices.Where(c => c.PlanItemId == item.Id && activeIds.Contains(c.UserId)).ToList();
            return new Tally(item, votes.GroupBy(v => v.OptionId).ToDictionary(g => g.Key, g => g.Count()), votes.Count);
        })];
    }

    private static AgentNudgeContext BuildNudge(Tally tally, Guid userOption, Guid majorityOption, int majorityCount, int number) => new(
        NudgeKinds.Majority,
        tally.Item.Title,
        tally.Item.Options.First(o => o.Id == userOption).Title,
        tally.Item.Options.First(o => o.Id == majorityOption).Title,
        tally.Counts.GetValueOrDefault(userOption),
        majorityCount,
        tally.Answered,
        number);

    // Every new plan version starts a fresh 24-hour response window.
    private async Task ResetApprovalsAsync(Trip trip, CancellationToken ct)
    {
        trip.PlanVersion++;
        if (trip.Status == TripStatus.Agreement)
        {
            trip.ResponseDeadline = DateTimeOffset.UtcNow.Add(ResponseWindow);
        }
        await db.PlanApprovals.Where(a => a.TripId == trip.Id).ExecuteDeleteAsync(ct);
    }

    private async Task UpsertApprovalAsync(Trip trip, Guid userId, bool approved, CancellationToken ct)
    {
        var approval = await db.PlanApprovals.FirstOrDefaultAsync(a => a.TripId == trip.Id && a.UserId == userId, ct);
        if (approval is null)
        {
            db.PlanApprovals.Add(new PlanApproval { TripId = trip.Id, UserId = userId, PlanVersion = trip.PlanVersion, Approved = approved, At = DateTimeOffset.UtcNow });
            return;
        }
        approval.PlanVersion = trip.PlanVersion;
        approval.Approved = approved;
        approval.At = DateTimeOffset.UtcNow;
    }

    private async Task NotifyOnceAsync(Guid userId, Guid tripId, string type, string message, CancellationToken ct)
    {
        if (!await db.Notifications.AnyAsync(n => n.UserId == userId && n.TripId == tripId && n.Type == type && n.Message == message && n.ReadAt == null, ct))
        {
            notifications.Notify(userId, tripId, type, message);
        }
    }

    private static string DefaultQuestion(PlanItem item) =>
        $"Does {item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId)?.Title ?? "the current choice"} work for you for {item.Title}?";
}
