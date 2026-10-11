using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Agents;
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
    IAgentClient agent,
    MagicLinkService magicLinks,
    NotificationService notifications,
    PlanResearchQueue research)
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

    // Step 1 of starting a trip (requirements 3.1): a short blurb about the destination.
    public async Task<StartIntroResponse> IntroAsync(string message, CancellationToken ct)
    {
        var result = await agent.InvokeAsync(new AgentInvocation(AgentRequests.NewId(), AgentActions.Chat, Message: message), ct);
        var destination = Clip(result.Destination, 200) ?? Clip(message, 200) ?? "Somewhere new";
        return new StartIntroResponse(
            result.Reply ?? $"{destination} sounds great.",
            destination,
            Clip(result.TripName, 120) ?? $"{destination.Split(',')[0]} trip");
    }

    // Step 2: the trip is created with the organizer and everyone going, each with their own magic link.
    public async Task<(Trip Trip, User Organizer)> StartAsync(StartTripRequest request, CancellationToken ct)
    {
        var organizer = await users.GetOrCreateAsync(request.OrganizerEmail, ct);
        var travelerEmails = request.TravelerEmails
            .Select(UserService.Normalize)
            .Where(e => e.Length > 0 && e != organizer.Email)
            .Distinct()
            .ToList();
        if (travelerEmails.FirstOrDefault(e => !UserService.IsValidEmail(e)) is { } invalid)
        {
            throw new BadRequestException($"\"{invalid}\" isn't a valid email address.");
        }

        var now = DateTimeOffset.UtcNow;
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            Name = request.TripName.Trim(),
            Destination = request.Destination.Trim(),
            Notes = request.Prompt.Trim(),
            PrimaryUserId = organizer.Id,
            PrimaryUser = organizer,
            CreatedAt = now,
            Members = [new TripMember { UserId = organizer.Id, User = organizer, Role = MemberRole.Primary, AddedAt = now, AccessToken = MagicLinkService.NewToken() }],
        };
        foreach (var email in travelerEmails)
        {
            var user = await users.GetOrCreateAsync(email, ct);
            trip.Members.Add(new TripMember { UserId = user.Id, User = user, Role = MemberRole.Traveler, AddedAt = now, AccessToken = MagicLinkService.NewToken() });
        }

        db.Trips.Add(trip);

        // Everyone gets their link as soon as they're added; travelers wait in the trip until it's sent.
        foreach (var member in trip.Members)
        {
            await magicLinks.SendAsync(trip, member, ct);
        }
        await db.SaveChangesAsync(ct);
        return (trip, organizer);
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
        var member = ctx.Trip.Members.FirstOrDefault(m => m.UserId == user.Id);
        if (member is { Status: MemberStatus.Active })
        {
            throw new ConflictException("That person is already on this trip.");
        }

        if (member is null)
        {
            member = new TripMember { TripId = tripId, UserId = user.Id, User = user, Role = MemberRole.Traveler, AddedAt = DateTimeOffset.UtcNow, AccessToken = MagicLinkService.NewToken() };
            db.TripMembers.Add(member);
        }
        else
        {
            // Re-added: they keep their earlier chat history and answers.
            member.Status = MemberStatus.Active;
            member.ExitedAt = null;
        }

        await magicLinks.SendAsync(ctx.Trip, member, ct);
        if (ctx.Trip.SentAt != null)
        {
            notifications.Notify(user.Id, tripId, NotificationTypes.MemberAdded, $"You were added to the trip \"{ctx.Trip.Name}\".");
        }
        notifications.TripChanged(ctx.ActiveMemberIds, tripId);
        await db.SaveChangesAsync(ct);
        await notifications.FlushAsync(ct);
    }

    // Each active member's magic link, for the organizer's browser console in development.
    public async Task<IReadOnlyList<MagicLinkDto>> MagicLinksAsync(Guid tripId, CancellationToken ct)
    {
        if (!magicLinks.ExposeLinks)
        {
            throw new NotFoundException();
        }
        var ctx = await access.LoadAsync(tripId, ct);
        TripAccess.RequirePrimary(ctx);
        return [.. ctx.ActiveMembers
            .OrderBy(m => m.Role)
            .ThenBy(m => m.AddedAt)
            .Select(m => new MagicLinkDto(m.User.Email, m.Role.ToString(), magicLinks.LinkFor(m)))];
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

        // Travelers wait until the organizer sends the trip; the draft plan stays private until then.
        var waiting = !ctx.IsPrimary && trip.SentAt == null;
        List<PlanItemDto> items = waiting ? [] : trip.Items.Select(i =>
        {
            var votes = choices.Where(c => c.PlanItemId == i.Id && activeIds.Contains(c.UserId)).ToList();
            return new PlanItemDto(
                i.Id,
                i.Key,
                i.Category.ToString(),
                i.Title,
                i.Day,
                i.Date,
                i.SelectedOptionId,
                i.Question,
                votes.FirstOrDefault(c => c.UserId == ctx.Me.UserId)?.OptionId,
                [.. i.Options.Select(o => new OptionDto(
                    o.Id, o.Title, o.Description, o.Provider, o.Location, o.EstimatedCost, o.Currency, o.SourceUrl, o.PriceFetchedAt,
                    o.StartTime, o.EndTime, o.EnergyLevel, votes.Count(v => v.OptionId == o.Id)))]);
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
        var windowOpen = AgreementService.WindowOpen(trip);
        var permissions = new PermissionsDto(
            CanChat: active && status != TripStatus.Cancelled && !waiting,
            CanEditPlan: primary && status is TripStatus.Planning or TripStatus.Agreement,
            CanAddMembers: primary && status is TripStatus.Planning or TripStatus.Agreement,
            CanSend: primary && status is TripStatus.Planning or TripStatus.Agreement && trip.Items.Count > 0,
            CanRespond: active && status == TripStatus.Agreement && trip.Items.Count > 0 && windowOpen,
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
            ResearchPending: status == TripStatus.Planning && research.IsPending(trip.Id),
            ctx.Me.Role.ToString(),
            trip.PrimaryUser.Email,
            ApprovalOf(ctx.Me.UserId),
            Consensus: AgreementService.HasConsensus(trip, activeIds, approvals),
            trip.ResponseDeadline,
            members,
            items,
            changeRequests,
            permissions);
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > max ? v[..max] : value.Trim();
}
