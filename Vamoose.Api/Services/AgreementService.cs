using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

// Requirements section 4: sending, preference responses and the group's majority decision.
// Travelers answer silently; once all of them have answered (or the window closes) the agent applies the option
// most people chose on each item and breaks any ties. The organizer built the plan, so its picks are their vote.
public class AgreementService(
    VamooseDbContext db,
    TripAccess access,
    IAgentClient agent,
    MagicLinkService magicLinks,
    NotificationService notifications,
    TripEvaluationQueue evaluations,
    ILogger<AgreementService> logger)
{
    public static readonly TimeSpan ResponseWindow = TimeSpan.FromHours(24);

    // The current plan version has been settled by the group's answers.
    public static bool Decided(Trip trip) => trip.ClosedVersion == trip.PlanVersion;

    public static bool WindowOpen(Trip trip) =>
        trip.Status == TripStatus.Agreement && !Decided(trip) && !DeadlinePassed(trip);

    private static bool DeadlinePassed(Trip trip) => trip.ResponseDeadline is { } deadline && DateTimeOffset.UtcNow > deadline;

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
                ? $"{trip.PrimaryUser.Email} sent \"{trip.Name}\" again. You have 24 hours to answer a few questions."
                : $"{trip.PrimaryUser.Email} sent you the trip \"{trip.Name}\". You have 24 hours to answer a few questions.");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // Called whenever the primary user changes the plan. Answers are cleared and a new round starts (requirements 4.3).
    public async Task OnPlanEditedAsync(TripContext ctx, string what, bool regenerateQuestions, CancellationToken ct)
    {
        var trip = ctx.Trip;
        if (trip.Status != TripStatus.Agreement)
        {
            trip.PlanVersion++;
            return;
        }

        await ResetApprovalsAsync(trip, ct);
        if (regenerateQuestions)
        {
            await GenerateQuestionsAsync(trip, ct);
        }

        notifications.Notify(
            ctx.OtherActiveMemberIds,
            trip.Id,
            NotificationTypes.TripEdited,
            $"{what} on \"{trip.Name}\". Please answer the questions again within 24 hours.");
    }

    // Answers are recorded without notifying anyone; the plan only changes once every traveler has answered.
    public async Task RespondAsync(Guid tripId, IReadOnlyList<ChoiceDto> choices, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequireActive(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Agreement);
        if (ctx.IsPrimary)
        {
            throw new ConflictException("Your itinerary already counts as your vote.");
        }
        var trip = ctx.Trip;
        if (Decided(trip))
        {
            throw new ConflictException("Everyone has already answered, and the plan was updated with the group's choices.");
        }
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
        var keepsPlan = true;
        foreach (var item in trip.Items)
        {
            // Unanswered items count as a vote for the current selection.
            var chosen = byItem.TryGetValue(item.Id, out var optionId) ? optionId : item.SelectedOptionId ?? item.Options[0].Id;
            keepsPlan &= chosen == item.SelectedOptionId;
            if (existing.TryGetValue(item.Id, out var choice))
            {
                choice.OptionId = chosen;
                choice.UpdatedAt = now;
            }
            else
            {
                db.ItemChoices.Add(new ItemChoice { TripId = tripId, PlanItemId = item.Id, UserId = me, OptionId = chosen, UpdatedAt = now });
            }
        }

        // An approval row marks that this user answered the current version.
        await UpsertApprovalAsync(trip, me, keepsPlan, ct);
        await db.SaveChangesAsync(ct);

        // Settling the vote can call the agent, so the traveler doesn't wait for it; pages refresh when it lands.
        evaluations.Enqueue(tripId);
    }

    // Settles response windows that ran out with whoever answered in time.
    public async Task CloseExpiredWindowsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.Trips
            .Where(t => t.Status == TripStatus.Agreement && t.ResponseDeadline < now && (t.ClosedVersion == null || t.ClosedVersion != t.PlanVersion))
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var tripId in due)
        {
            await EvaluateTripAsync(tripId, ct);
        }
    }

    // Run by TripEvaluator after an answer, and by the window closer.
    public async Task EvaluateTripAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadForSystemAsync(tripId, ct);
        await EvaluateAsync(ctx, ct);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // Waits until every active traveler has answered (or the window closed), then applies the group's choices.
    public async Task EvaluateAsync(TripContext ctx, CancellationToken ct)
    {
        var trip = ctx.Trip;
        if (trip.Status != TripStatus.Agreement || trip.Items.Count == 0 || Decided(trip))
        {
            return;
        }

        var travelerIds = ctx.ActiveMemberIds.Where(id => id != trip.PrimaryUserId).ToHashSet();
        if (travelerIds.Count == 0)
        {
            return;
        }
        var approvals = await db.PlanApprovals
            .Where(a => a.TripId == trip.Id && a.PlanVersion == trip.PlanVersion && travelerIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, a => a.Approved, ct);
        if (!DeadlinePassed(trip) && approvals.Count < travelerIds.Count)
        {
            return;
        }

        trip.ClosedVersion = trip.PlanVersion;
        if (approvals.Count == 0)
        {
            notifications.Notify(trip.PrimaryUserId, trip.Id, NotificationTypes.PlanDecided,
                $"The 24-hour response window for \"{trip.Name}\" closed without any answers, so the plan stays as it is.");
            return;
        }

        // Earlier rounds' answers stay in the table, so only this round's responders count.
        var choices = await db.ItemChoices.Where(c => c.TripId == trip.Id && approvals.Keys.Contains(c.UserId)).ToListAsync(ct);
        // The organizer's vote is the itinerary they sent: one vote for each current pick.
        choices.AddRange(trip.Items.Select(i => new ItemChoice
        {
            TripId = trip.Id, PlanItemId = i.Id, UserId = trip.PrimaryUserId, OptionId = i.SelectedOptionId ?? i.Options[0].Id,
        }));
        await ApplyMajorityAsync(ctx, choices, approvals, travelerIds.Count, ct);
    }

    // The option most people chose wins each item. Ties go to the agent; if it is unreachable the current pick stays.
    // Members are sent to the model as anonymous labels.
    private async Task ApplyMajorityAsync(TripContext ctx, List<ItemChoice> choices, Dictionary<Guid, bool> approvals, int travelers, CancellationToken ct)
    {
        var trip = ctx.Trip;
        var active = ctx.ActiveMembers.ToList();
        var labels = active.Select((m, i) => (m, Label: $"traveler-{i + 1}")).ToDictionary(x => x.m.UserId, x => x.Label);

        var responses = new AgentResponses(
            [labels[trip.PrimaryUserId], .. approvals.Where(a => a.Value).Select(a => labels[a.Key])],
            [.. approvals.Where(a => !a.Value).Select(a => labels[a.Key])],
            [.. active.Where(m => m.UserId != trip.PrimaryUserId && !approvals.ContainsKey(m.UserId)).Select(m => labels[m.UserId])],
            [.. trip.Items.Select(item => new AgentSlotResponses(
                item.Key,
                item.Title,
                item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId)?.Title ?? "",
                [.. item.Options.Where(o => o.Id != item.SelectedOptionId).Select(o => o.Title)],
                [.. choices.Where(c => c.PlanItemId == item.Id).Select(c => new AgentParticipantChoice(labels[c.UserId], item.Options.First(o => o.Id == c.OptionId).Title))]))]);

        AgentAnalysis? analysis = null;
        try
        {
            var result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(), AgentActions.Analyze, Trip: AgentMapper.ToContext(trip), Plan: AgentMapper.ToPlan(trip), Responses: responses, TripId: trip.Id), ct);
            analysis = result.Analysis;
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "Tie breaks fell back to the current pick for trip {TripId}", trip.Id);
        }
        var decisions = (analysis?.Items ?? []).GroupBy(i => i.SlotKey).ToDictionary(g => g.Key, g => g.First());

        var lines = new List<string>();
        var changed = 0;
        foreach (var item in trip.Items)
        {
            var votes = choices.Where(c => c.PlanItemId == item.Id).ToList();
            var leaders = Leaders(item, votes);
            var current = item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId);
            var pick = leaders[0];
            var note = "";
            if (leaders.Count > 1)
            {
                // The agent may only choose among the tied options.
                var decision = decisions.GetValueOrDefault(item.Key);
                var agentPick = decision?.Decision is { } title ? leaders.FirstOrDefault(o => o.Title == title) : null;
                pick = agentPick ?? leaders.FirstOrDefault(o => o.Id == item.SelectedOptionId) ?? leaders[0];
                note = agentPick is null
                    ? " It was a tie, so the current pick stayed."
                    : $" It was a tie, so the agent picked it{(string.IsNullOrWhiteSpace(decision!.Reason) ? "." : $": {decision.Reason}")}";
            }

            var count = $"{votes.Count(v => v.OptionId == pick.Id)} of {votes.Count} votes";
            if (pick.Id == item.SelectedOptionId)
            {
                lines.Add($"• {item.Title}: kept {pick.Title} ({count}).{note}");
                continue;
            }
            item.SelectedOptionId = pick.Id;
            changed++;
            lines.Add($"• {item.Title}: {pick.Title}, was {current?.Title ?? "unset"} ({count}).{note}");
        }

        var who = approvals.Count == travelers
            ? "Everyone has answered"
            : $"The response window closed with {approvals.Count} of {travelers} travelers' answers";
        var summary = analysis?.Summary is { Length: > 0 } s ? $" {s}" : "";
        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            UserId = trip.PrimaryUserId,
            Role = ChatRole.Agent,
            Content = string.Join("\n", new[] { $"{who}, and I updated the plan with the group's choices.{summary}" }.Concat(lines)),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // The primary user hears about it once; everyone else's page just refreshes to the updated plan.
        notifications.Notify(trip.PrimaryUserId, trip.Id, NotificationTypes.PlanDecided,
            changed > 0
                ? $"{who} on \"{trip.Name}\". The agent changed {changed} item(s) to match what most people chose."
                : $"{who} on \"{trip.Name}\". The group went with the current plan.");
        notifications.TripChanged(ctx.ActiveMemberIds.Where(id => id != trip.PrimaryUserId), trip.Id);
    }

    // Options with the most votes, in plan order.
    private static List<PlanOption> Leaders(PlanItem item, List<ItemChoice> votes)
    {
        var counts = votes.GroupBy(v => v.OptionId).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0)
        {
            return [item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId) ?? item.Options[0]];
        }
        var top = counts.Values.Max();
        return [.. item.Options.Where(o => counts.GetValueOrDefault(o.Id) == top)];
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

    private static string DefaultQuestion(PlanItem item) =>
        $"Does {item.Options.FirstOrDefault(o => o.Id == item.SelectedOptionId)?.Title ?? "the current choice"} work for you for {item.Title}?";
}
