using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

// Requirements 4.5 and 4.6: exit, cancel, reopen, lock, unlock.
public class LifecycleService(
    VamooseDbContext db,
    TripAccess access,
    IAgentClient agent,
    AgreementService agreement,
    NotificationService notifications,
    ILogger<LifecycleService> logger)
{
    // Before a traveler leaves, the agent tries to persuade them to stay. Leaving as primary cancels the trip.
    public async Task<ExitPreviewDto> ExitPreviewAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequireActive(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement, TripStatus.Locked);

        if (ctx.IsPrimary)
        {
            return new ExitPreviewDto(
                "You're the primary user. If you leave, the trip is cancelled and everyone is notified. You can reopen it later.",
                CancelsTrip: true);
        }

        string message;
        try
        {
            var result = await agent.InvokeAsync(new AgentInvocation(
                AgentRequests.NewId(),
                AgentActions.Nudge,
                Trip: AgentMapper.ToContext(ctx.Trip),
                Plan: ctx.Trip.Items.Count > 0 ? AgentMapper.ToPlan(ctx.Trip) : null,
                Nudge: new AgentNudgeContext(NudgeKinds.Stay),
                TripId: tripId), ct);
            message = result.Reply ?? DefaultRetain(ctx.Trip);
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "Retention message fell back to default for trip {TripId}", tripId);
            message = DefaultRetain(ctx.Trip);
        }

        db.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            UserId = ctx.Me.UserId,
            Role = ChatRole.Agent,
            Content = message,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new ExitPreviewDto(message, CancelsTrip: false);
    }

    public async Task ExitAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        TripAccess.RequireActive(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement, TripStatus.Locked);
        var trip = ctx.Trip;

        if (ctx.IsPrimary)
        {
            trip.StatusBeforeCancel = trip.Status;
            trip.Status = TripStatus.Cancelled;
            if (trip.SentAt != null)
            {
                notifications.Notify(ctx.OtherActiveMemberIds, tripId, NotificationTypes.TripCancelled,
                    $"The trip \"{trip.Name}\" has been cancelled by {trip.PrimaryUser.Email}.");
            }
        }
        else
        {
            ctx.Me.Status = MemberStatus.Exited;
            ctx.Me.ExitedAt = DateTimeOffset.UtcNow;
            await db.PlanApprovals.Where(a => a.TripId == tripId && a.UserId == ctx.Me.UserId).ExecuteDeleteAsync(ct);
            await db.ChangeRequests
                .Where(r => r.TripId == tripId && r.RequestedByUserId == ctx.Me.UserId && r.Status == ChangeRequestStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChangeRequestStatus.Superseded), ct);

            // Only the primary user is told when someone leaves (requirements 4.5).
            notifications.Notify(trip.PrimaryUserId, tripId, NotificationTypes.MemberExited,
                $"{ctx.Me.User.Email} left the trip \"{trip.Name}\".");
            notifications.TripChanged(ctx.OtherActiveMemberIds, tripId);

            // Agreement is now measured among the remaining users.
            await agreement.EvaluateAsync(ctx, ct);
        }

        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    public async Task ReopenAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Cancelled);
        var trip = ctx.Trip;

        // Users who exited before the cancellation stay out until re-added.
        trip.Status = trip.StatusBeforeCancel ?? TripStatus.Planning;
        trip.StatusBeforeCancel = null;
        if (trip.SentAt != null)
        {
            notifications.Notify(ctx.OtherActiveMemberIds, tripId, NotificationTypes.TripReopened,
                $"The trip \"{trip.Name}\" has been reopened.");
        }
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // The primary user may lock even if some users still deny the plan; they can leave instead (requirements 4.6).
    public async Task LockAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Agreement);

        ctx.Trip.Status = TripStatus.Locked;
        await db.ChangeRequests
            .Where(r => r.TripId == tripId && r.Status == ChangeRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ChangeRequestStatus.Superseded), ct);
        notifications.Notify(ctx.OtherActiveMemberIds, tripId, NotificationTypes.TripLocked,
            $"\"{ctx.Trip.Name}\" is locked. The final plan is ready to view.");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    public async Task UnlockAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Locked);

        ctx.Trip.Status = TripStatus.Agreement;
        notifications.Notify(ctx.OtherActiveMemberIds, tripId, NotificationTypes.TripUnlocked,
            $"\"{ctx.Trip.Name}\" was unlocked. The plan may change again.");
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    private static string DefaultRetain(Trip trip) =>
        $"Sorry the plan isn't working for you. The group would miss you on \"{trip.Name}\", and you can still pick alternatives for anything you don't like. If you'd still rather leave, that's okay.";
}
