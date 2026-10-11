using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

public class PlanService(
    VamooseDbContext db,
    TripAccess access,
    IAgentClient agent,
    AgreementService agreement,
    NotificationService notifications,
    PlanResearchQueue research,
    ILogger<PlanService> logger)
{
    private const int HistoryLimit = 20;

    public async Task<IReadOnlyList<ChatMessageDto>> GetChatAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct);
        return await db.ChatMessages
            .Where(m => m.TripId == tripId && m.UserId == ctx.Me.UserId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto(m.Id, m.Role.ToString(), m.Content, m.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ChatResponseDto> ChatAsync(Guid tripId, string message, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequireActive(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement, TripStatus.Locked);
        if (!ctx.IsPrimary && ctx.Trip.SentAt == null)
        {
            throw new ConflictException("The organizer is still planning this trip. You can chat once it's sent.");
        }

        var trip = ctx.Trip;
        var canEdit = ctx.IsPrimary && trip.Status is TripStatus.Planning or TripStatus.Agreement;
        var history = await db.ChatMessages
            .Where(m => m.TripId == tripId && m.UserId == ctx.Me.UserId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);
        history.Reverse();

        var result = await agent.InvokeAsync(new AgentInvocation(
            AgentRequests.NewId(),
            AgentActions.Chat,
            Message: message,
            Trip: AgentMapper.ToContext(trip),
            Plan: trip.Items.Count > 0 ? AgentMapper.ToPlan(trip) : null,
            History: [.. history.Select(m => new AgentHistoryMessage(m.Role == ChatRole.User ? "user" : "agent", m.Content))],
            CanEditPlan: canEdit,
            TripId: tripId), ct);

        var now = DateTimeOffset.UtcNow;
        var userMessage = new ChatMessage { Id = Guid.NewGuid(), TripId = tripId, UserId = ctx.Me.UserId, Role = ChatRole.User, Content = message, CreatedAt = now };
        var agentMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            UserId = ctx.Me.UserId,
            Role = ChatRole.Agent,
            Content = string.IsNullOrWhiteSpace(result.Reply) ? "Done." : result.Reply,
            CreatedAt = now.AddMilliseconds(1),
        };
        db.ChatMessages.AddRange(userMessage, agentMessage);

        var planUpdated = false;
        var firstPlan = trip.Items.Count == 0;
        if (canEdit && result.Plan is { Slots.Count: > 0 } plan)
        {
            var items = AgentMapper.ToItems(tripId, plan);
            if (items.Count > 0)
            {
                db.PlanItems.RemoveRange(trip.Items);
                db.PlanItems.AddRange(items);
                trip.Items = items;
                planUpdated = true;
                await agreement.OnPlanEditedAsync(ctx, "The plan was updated", regenerateQuestions: true, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
        if (planUpdated && firstPlan && trip.Status == TripStatus.Planning)
        {
            research.Enqueue(tripId, trip.PlanVersion);
        }
        if (planUpdated)
        {
            agreement.QueueQuestions(trip);
        }
        return new ChatResponseDto([ToDto(userMessage), ToDto(agentMessage)], planUpdated);
    }

    // Second phase of a first plan (run by PlanResearcher): the agent adds real options and links to the quick draft.
    // Skipped if the organizer changed the plan in the meantime, so their edits always win.
    public async Task ApplyResearchAsync(Guid tripId, int planVersion, CancellationToken ct)
    {
        var ctx = await access.LoadForSystemAsync(tripId, ct);
        try
        {
            await ResearchAsync(ctx, planVersion, ct);
        }
        finally
        {
            // Cleared before notifying so refreshed pages see research as finished.
            research.Complete(tripId);
            notifications.TripChanged(ctx.ActiveMemberIds, tripId);
            await notifications.FlushAsync(CancellationToken.None);
        }
    }

    private async Task ResearchAsync(TripContext ctx, int planVersion, CancellationToken ct)
    {
        var trip = ctx.Trip;
        var tripId = trip.Id;
        if (!IsCurrent(trip, planVersion) || trip.Items.Count == 0)
        {
            return;
        }

        AgentResult result;
        try
        {
            result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(), AgentActions.Research, Trip: AgentMapper.ToContext(trip), Plan: AgentMapper.ToPlan(trip), TripId: tripId), ct);
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "Plan research skipped for trip {TripId}", tripId);
            return;
        }

        List<PlanItem> items = result.Plan is { } plan ? AgentMapper.ToItems(tripId, plan) : [];
        await db.Entry(trip).ReloadAsync(ct);
        if (items.Count == 0 || !IsCurrent(trip, planVersion))
        {
            return;
        }

        db.PlanItems.RemoveRange(trip.Items);
        db.PlanItems.AddRange(items);
        trip.Items = items;
        trip.PlanVersion++;
        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            UserId = trip.PrimaryUserId,
            Role = ChatRole.Agent,
            Content = string.IsNullOrWhiteSpace(result.Reply) ? "I added real options and links to the plan." : result.Reply,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static bool IsCurrent(Trip trip, int planVersion) => trip.Status == TripStatus.Planning && trip.PlanVersion == planVersion;

    public async Task SelectOptionAsync(Guid tripId, Guid itemId, Guid optionId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement);

        var item = ctx.Trip.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Plan item not found.");
        var option = item.Options.FirstOrDefault(o => o.Id == optionId) ?? throw new BadRequestException("That option isn't part of this item.");
        if (item.SelectedOptionId == option.Id)
        {
            return;
        }

        item.SelectedOptionId = option.Id;
        await agreement.OnPlanEditedAsync(ctx, $"{item.Title} changed to {option.Title}", regenerateQuestions: false, ct);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(m.Id, m.Role.ToString(), m.Content, m.CreatedAt);
}
