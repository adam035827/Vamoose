using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Services;

public record TripContext(Trip Trip, TripMember Me)
{
    public bool IsPrimary => Me.Role == MemberRole.Primary;
    public IEnumerable<TripMember> ActiveMembers => Trip.Members.Where(m => m.Status == MemberStatus.Active);
    public IEnumerable<Guid> ActiveMemberIds => ActiveMembers.Select(m => m.UserId);
    public IEnumerable<Guid> OtherActiveMemberIds => ActiveMemberIds.Where(id => id != Me.UserId);
}

// Loads a trip for the current user and enforces who may see and change it.
public class TripAccess(VamooseDbContext db, CurrentUser currentUser)
{
    public async Task<TripContext> LoadAsync(Guid tripId, CancellationToken ct, bool withPlan = false)
    {
        var query = db.Trips.Include(t => t.Members).ThenInclude(m => m.User).Include(t => t.PrimaryUser).AsQueryable();
        if (withPlan)
        {
            query = query.Include(t => t.Items).ThenInclude(i => i.Options);
        }

        var trip = await query.AsSplitQuery().FirstOrDefaultAsync(t => t.Id == tripId, ct) ?? throw new NotFoundException("Trip not found.");
        var me = trip.Members.FirstOrDefault(m => m.UserId == currentUser.Id);
        if (me is null || !CanSee(trip, me))
        {
            throw new NotFoundException("Trip not found.");
        }

        if (withPlan)
        {
            trip.Items = [.. trip.Items.OrderBy(i => i.SortOrder)];
            foreach (var item in trip.Items)
            {
                item.Options = [.. item.Options.OrderBy(o => o.SortOrder)];
            }
        }
        return new TripContext(trip, me);
    }

    public static bool CanSee(Trip trip, TripMember me) =>
        me.Role == MemberRole.Primary
        || (me.Status == MemberStatus.Active && trip.SentAt != null && trip.Status != TripStatus.Cancelled);

    public static void RequirePrimary(TripContext ctx)
    {
        if (!ctx.IsPrimary)
        {
            throw new ForbiddenException("Only the primary user can do that.");
        }
    }

    public static void RequireStatus(TripContext ctx, params TripStatus[] allowed)
    {
        if (!allowed.Contains(ctx.Trip.Status))
        {
            throw new ConflictException(ctx.Trip.Status switch
            {
                TripStatus.Cancelled => "This trip is cancelled and read-only.",
                TripStatus.Locked => "This trip is locked. No further changes can be made.",
                TripStatus.Planning => "This trip hasn't been sent yet.",
                _ => "That isn't possible at this stage of the trip.",
            });
        }
    }

    public static void RequireActive(TripContext ctx)
    {
        if (ctx.Me.Status != MemberStatus.Active)
        {
            throw new ForbiddenException("You are no longer part of this trip.");
        }
    }
}
