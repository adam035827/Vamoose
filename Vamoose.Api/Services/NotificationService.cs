using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;

namespace Vamoose.Api.Services;

public static class NotificationTypes
{
    public const string TripSent = "TripSent";
    public const string TripEdited = "TripEdited";
    public const string MemberExited = "MemberExited";
    public const string MemberAdded = "MemberAdded";
    public const string TripCancelled = "TripCancelled";
    public const string TripReopened = "TripReopened";
    public const string TripLocked = "TripLocked";
    public const string TripUnlocked = "TripUnlocked";
    public const string PlanDecided = "PlanDecided";
}

// Users join a group named after their user id; the server pushes notifications and trip refresh signals.
[Authorize]
public class NotificationHub : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is { } id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Guid.Parse(id)));
        }
        await base.OnConnectedAsync();
    }
}

public class NotificationService(VamooseDbContext db, IHubContext<NotificationHub> hub)
{
    private readonly List<Notification> _pending = [];
    private readonly HashSet<(Guid UserId, Guid TripId)> _tripChanges = [];

    // Queued until FlushAsync so nothing is pushed for a transaction that fails.
    public void Notify(IEnumerable<Guid> userIds, Guid? tripId, string type, string message)
    {
        foreach (var userId in userIds.Distinct())
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TripId = tripId,
                Type = type,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Notifications.Add(notification);
            _pending.Add(notification);
        }
    }

    public void Notify(Guid userId, Guid? tripId, string type, string message) => Notify([userId], tripId, type, message);

    // Tells open trip pages to reload without creating a notification.
    public void TripChanged(IEnumerable<Guid> userIds, Guid tripId)
    {
        foreach (var userId in userIds)
        {
            _tripChanges.Add((userId, tripId));
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        foreach (var n in _pending)
        {
            await hub.Clients.Group(NotificationHub.UserGroup(n.UserId)).SendAsync("notification", ToDto(n), ct);
            _tripChanges.Add((n.UserId, n.TripId ?? Guid.Empty));
        }
        foreach (var (userId, tripId) in _tripChanges.Where(c => c.TripId != Guid.Empty))
        {
            await hub.Clients.Group(NotificationHub.UserGroup(userId)).SendAsync("tripChanged", tripId, ct);
        }
        _pending.Clear();
        _tripChanges.Clear();
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.TripId, n.Type, n.Message, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);

    public async Task MarkReadAsync(Guid userId, Guid? notificationId, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null && (notificationId == null || n.Id == notificationId))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);

    private static NotificationDto ToDto(Notification n) => new(n.Id, n.TripId, n.Type, n.Message, n.CreatedAt, false);
}
