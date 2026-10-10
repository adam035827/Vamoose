using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Domain;
using Vamoose.Api.Services;

namespace Vamoose.Api.Data;

// A ready-to-send demo trip so the main flow can be shown from a clean start (requirements 6.2).
public static class DemoSeeder
{
    public const string OrganizerEmail = "demo-organizer@vamoose.dev";
    private static readonly string[] TravelerEmails = ["demo-ana@vamoose.dev", "demo-ben@vamoose.dev", "demo-cleo@vamoose.dev"];
    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<VamooseDbContext>();
        var magicLinks = services.GetRequiredService<MagicLinkService>();

        // The dev outbox is in memory, so re-send the presenter's link on every start.
        var existing = await db.TripMembers
            .Include(m => m.User)
            .Where(m => m.User.Email == OrganizerEmail && m.Role == MemberRole.Primary)
            .Join(db.Trips.Include(t => t.PrimaryUser), m => m.TripId, t => t.Id, (m, t) => new { Member = m, Trip = t })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            await magicLinks.SendAsync(existing.Trip, existing.Member, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        var users = services.GetRequiredService<UserService>();
        var now = DateTimeOffset.UtcNow;
        var organizer = await users.GetOrCreateAsync(OrganizerEmail, ct);
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            Name = "Demo: Lisbon long weekend",
            Destination = "Lisbon, Portugal",
            StartDate = new DateOnly(2027, 5, 14),
            EndDate = new DateOnly(2027, 5, 17),
            Notes = "Lisbon in May with 4 friends from New York, mid-range budget, big on food.",
            PrimaryUserId = organizer.Id,
            PrimaryUser = organizer,
            CreatedAt = now,
            Members = [new TripMember { UserId = organizer.Id, User = organizer, Role = MemberRole.Primary, AddedAt = now, AccessToken = MagicLinkService.NewToken() }],
        };
        foreach (var email in TravelerEmails)
        {
            var user = await users.GetOrCreateAsync(email, ct);
            trip.Members.Add(new TripMember { UserId = user.Id, User = user, Role = MemberRole.Traveler, AddedAt = now, AccessToken = MagicLinkService.NewToken() });
        }

        trip.Items =
        [
            Item(trip.Id, 0, "flight-outbound", PlanCategory.Flight, "Flight to Lisbon", 1,
                Option("Morning nonstop JFK → LIS", 540, "https://www.flytap.com", provider: "TAP Air Portugal", start: "08:10"),
                Option("Evening nonstop JFK → LIS", 610, "https://www.united.com", provider: "United", start: "18:45"),
                Option("One-stop saver via Madrid", 420, "https://www.iberia.com", provider: "Iberia", start: "06:30")),
            Item(trip.Id, 1, "lodging-main", PlanCategory.Lodging, "Where we stay", 1,
                Option("Alfama apartment, 3 bedrooms", 360, "https://www.airbnb.com", description: "Walkable old town with river views."),
                Option("Boutique hotel in Chiado", 480, "https://www.booking.com", description: "Breakfast included, central."),
                Option("Hostel private rooms in Bairro Alto", 190, "https://www.hostelworld.com", description: "Lively, further from the river.")),
            Item(trip.Id, 2, "dinner-day-1", PlanCategory.Dining, "Welcome dinner", 1,
                Option("Time Out Market", 35, "https://www.timeoutmarket.com/lisboa", start: "19:30", description: "Lots of choice, casual."),
                Option("Cervejaria Ramiro", 70, "https://www.cervejariaramiro.com", start: "20:00", description: "Classic seafood, expect a queue.")),
            Item(trip.Id, 3, "activity-day-2", PlanCategory.Activity, "Day 2 outing", 2,
                Option("Sintra day trip: Pena Palace", 55, "https://www.parquesdesintra.pt", start: "08:30", end: "17:30", energy: "High"),
                Option("Beach day in Cascais", 20, "https://www.visitcascais.com", start: "10:00", end: "17:00", energy: "Low"),
                Option("Belém walking tour", 30, "https://www.visitlisboa.com", start: "10:00", end: "15:00", energy: "Medium")),
        ];

        db.Trips.Add(trip);
        db.ChatMessages.AddRange(
            Message(trip, organizer.Id, ChatRole.User, "Lisbon in May with 4 friends from New York, mid-range budget, big on food.", now),
            Message(trip, organizer.Id, ChatRole.Agent, "Here's a first plan with alternatives for each item. Add or change anything, then finalize and send it to the group.", now.AddSeconds(1)));
        await db.SaveChangesAsync(ct);

        // Travelers get their links when the presenter sends the trip, as in the real flow.
        await magicLinks.SendAsync(trip, trip.Members[0], ct);
        await db.SaveChangesAsync(ct);
    }

    private static PlanItem Item(Guid tripId, int order, string key, PlanCategory category, string title, int day, params PlanOption[] options)
    {
        for (var i = 0; i < options.Length; i++)
        {
            options[i].SortOrder = i;
        }
        return new PlanItem
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            Key = key,
            Category = category,
            Title = title,
            Day = day,
            SortOrder = order,
            SelectedOptionId = options[0].Id,
            Options = [.. options],
        };
    }

    private static PlanOption Option(string title, decimal cost, string url, string? provider = null, string? description = null,
        string? start = null, string? end = null, string? energy = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = description,
        Provider = provider,
        EstimatedCost = cost,
        Currency = "USD",
        SourceUrl = url,
        PriceFetchedAt = FetchedAt,
        StartTime = start,
        EndTime = end,
        EnergyLevel = energy,
    };

    private static ChatMessage Message(Trip trip, Guid userId, ChatRole role, string content, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), TripId = trip.Id, UserId = userId, Role = role, Content = content, CreatedAt = at };
}
