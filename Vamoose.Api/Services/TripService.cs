using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

public class TripService(
    VamooseDbContext db,
    CurrentUser currentUser,
    TripAccess access,
    UserService users,
    NotificationService notifications)
{
    public async Task<IReadOnlyList<TripSummaryDto>> ListAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var trips = await db.Trips
            .Include(t => t.Members)
            .Include(t => t.PrimaryUser)
            .Where(t => t.Members.Any(m => m.UserId == me))
            .OrderByDescending(t => t.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

        return [.. trips
            .Select(t => (Trip: t, Me: t.Members.First(m => m.UserId == me)))
            .Where(x => TripAccess.CanSee(x.Trip, x.Me))
            .Select(x => new TripSummaryDto(
                x.Trip.Id,
                x.Trip.Name,
                x.Trip.Destination,
                x.Trip.StartDate,
                x.Trip.EndDate,
                x.Trip.Status.ToString(),
                x.Me.Role.ToString(),
                x.Trip.PrimaryUser.Email,
                x.Trip.Members.Count(m => m.Status == MemberStatus.Active)))];
    }

    public async Task<Guid> CreateAsync(CreateTripRequest request, CancellationToken ct)
    {
        if (request.StartDate is { } start && request.EndDate is { } end && end < start)
        {
            throw new BadRequestException("The end date can't be before the start date.");
        }

        var now = DateTimeOffset.UtcNow;
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Destination = request.Destination.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            PrimaryUserId = currentUser.Id,
            CreatedAt = now,
            Members = [new TripMember { UserId = currentUser.Id, Role = MemberRole.Primary, AddedAt = now }],
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);
        return trip.Id;
    }

    public async Task<TripDetailDto> GetAsync(Guid tripId, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct, withPlan: true);
        return await MapAsync(ctx, ct);
    }

    public async Task AddMemberAsync(Guid tripId, string email, CancellationToken ct)
    {
        var ctx = await access.LoadAsync(tripId, ct);
        TripAccess.RequirePrimary(ctx);
        TripAccess.RequireStatus(ctx, TripStatus.Planning, TripStatus.Agreement);

        var user = await users.GetOrCreateAsync(email, ct);
        var existing = ctx.Trip.Members.FirstOrDefault(m => m.UserId == user.Id);
        if (existing is { Status: MemberStatus.Active })
        {
            throw new ConflictException("That person is already on this trip.");
        }

        if (existing is null)
        {
            db.TripMembers.Add(new TripMember { TripId = tripId, UserId = user.Id, Role = MemberRole.Traveler, AddedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            // Re-added: they keep their earlier chat history and answers.
            existing.Status = MemberStatus.Active;
            existing.ExitedAt = null;
        }

        if (ctx.Trip.SentAt != null)
        {
            notifications.Notify(user.Id, tripId, NotificationTypes.MemberAdded, $"You were added to the trip \"{ctx.Trip.Name}\".");
        }
        notifications.TripChanged(ctx.ActiveMemberIds, tripId);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    private async Task<TripDetailDto> MapAsync(TripContext ctx, CancellationToken ct)
    {
        var trip = ctx.Trip;
        var activeIds = ctx.ActiveMemberIds.ToHashSet();

        var choices = await db.ItemChoices.Where(c => c.TripId == trip.Id).ToListAsync(ct);
        var approvals = await db.PlanApprovals
            .Where(a => a.TripId == trip.Id && a.PlanVersion == trip.PlanVersion)
            .ToDictionaryAsync(a => a.UserId, a => a.Approved, ct);
        var requests = await db.ChangeRequests
            .Include(r => r.PlanItem).ThenInclude(i => i.Options)
            .Include(r => r.RequestedBy)
            .Where(r => r.TripId == trip.Id && (ctx.IsPrimary || r.RequestedByUserId == ctx.Me.UserId))
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        string? ApprovalOf(Guid userId) => approvals.TryGetValue(userId, out var ok) ? (ok ? "Approved" : "Denied") : null;

        // Only the primary user learns about exits, so others see active members only.
        var members = trip.Members
            .Where(m => ctx.IsPrimary || m.Status == MemberStatus.Active)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.AddedAt)
            .Select(m => new MemberDto(m.UserId, m.User.Email, m.Role.ToString(), m.Status.ToString(), ApprovalOf(m.UserId)))
            .ToList();

        var items = trip.Items.Select(i =>
        {
            var votes = choices.Where(c => c.PlanItemId == i.Id && activeIds.Contains(c.UserId)).ToList();
            return new PlanItemDto(
                i.Id,
                i.Key,
                i.Category.ToString(),
                i.Title,
                i.Day,
                i.SelectedOptionId,
                i.Question,
                votes.FirstOrDefault(c => c.UserId == ctx.Me.UserId)?.OptionId,
                [.. i.Options.Select(o => new OptionDto(
                    o.Id, o.Title, o.Description, o.Provider, o.Location, o.Price, o.Currency, o.Url, o.StartTime, o.EndTime,
                    votes.Count(v => v.OptionId == o.Id)))]);
        }).ToList();

        var changeRequests = requests.Select(r => new ChangeRequestDto(
            r.Id,
            r.PlanItemId,
            r.PlanItem.Title,
            r.RequestedBy.Email,
            r.PlanItem.Options.FirstOrDefault(o => o.Id == r.FromOptionId)?.Title ?? "Previous option",
            r.PlanItem.Options.FirstOrDefault(o => o.Id == r.ToOptionId)?.Title ?? "Alternative",
            r.Status.ToString(),
            r.CreatedAt)).ToList();

        var status = trip.Status;
        var primary = ctx.IsPrimary;
        var active = ctx.Me.Status == MemberStatus.Active;
        var permissions = new PermissionsDto(
            CanChat: active && status != TripStatus.Cancelled,
            CanEditPlan: primary && status is TripStatus.Planning or TripStatus.Agreement,
            CanAddMembers: primary && status is TripStatus.Planning or TripStatus.Agreement,
            CanSend: primary && status is TripStatus.Planning or TripStatus.Agreement && trip.Items.Count > 0,
            CanRespond: active && status == TripStatus.Agreement && trip.Items.Count > 0,
            CanLock: primary && status == TripStatus.Agreement,
            CanUnlock: primary && status == TripStatus.Locked,
            CanReopen: primary && status == TripStatus.Cancelled,
            CanExit: active && status != TripStatus.Cancelled);

        return new TripDetailDto(
            trip.Id,
            trip.Name,
            trip.Destination,
            trip.StartDate,
            trip.EndDate,
            trip.Notes,
            status.ToString(),
            trip.PlanVersion,
            ctx.Me.Role.ToString(),
            trip.PrimaryUser.Email,
            ApprovalOf(ctx.Me.UserId),
            Consensus: activeIds.Count > 0 && activeIds.All(id => approvals.TryGetValue(id, out var ok) && ok),
            members,
            items,
            changeRequests,
            permissions);
    }
}
