using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Dtos;
using Vamoose.Api.Services;

namespace Vamoose.Api.Tests;

public class TripFlowTests(VamooseFactory factory) : IClassFixture<VamooseFactory>
{
    [Fact]
    public async Task Starting_a_trip_creates_members_signs_in_organizer_and_emails_only_their_link()
    {
        var organizer = VamooseFactory.NewEmail("org");
        var traveler = VamooseFactory.NewEmail("ana");

        var (client, tripId) = await factory.StartTripAsync(organizer, traveler, organizer.ToUpperInvariant());

        var trip = await client.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.Equal("Lisbon, Portugal", trip!.Destination);
        Assert.Equal("Primary", trip.MyRole);
        Assert.Equal(2, trip.Members.Count);
        Assert.NotNull(VamooseFactory.MagicTokenFor(organizer));
        Assert.Null(VamooseFactory.MagicTokenFor(traveler));

        var chat = await client.GetFromJsonAsync<List<ChatMessageDto>>($"/api/trips/{tripId}/chat");
        Assert.Equal(["User", "Agent"], chat!.Select(m => m.Role));
    }

    [Fact]
    public async Task Start_rejects_invalid_traveler_email()
    {
        var client = factory.NewClient();

        var response = await client.PostAsJsonAsync("/api/start",
            new StartTripRequest("Lisbon", "Lisbon", "Lisbon trip", VamooseFactory.NewEmail("org"), ["bob@"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Magic_link_is_the_only_way_in()
    {
        var organizer = VamooseFactory.NewEmail("org");
        var (_, tripId) = await factory.StartTripAsync(organizer);

        var anonymous = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/trips/{tripId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/auth/magic", new MagicLinkRequest("wrong"))).StatusCode);

        var viaLink = await factory.OpenMagicLinkAsync(organizer);
        Assert.Equal(HttpStatusCode.OK, (await viaLink.GetAsync($"/api/trips/{tripId}")).StatusCode);
    }

    [Fact]
    public async Task Sending_emails_travelers_their_links_and_opens_a_24_hour_window()
    {
        var traveler = VamooseFactory.NewEmail("ben");
        var (organizer, tripId) = await PlannedTripAsync(traveler);
        var travelerClient = factory.NewClient();
        Assert.Null(VamooseFactory.MagicTokenFor(traveler));

        var sent = await Send(organizer, tripId);

        Assert.Equal("Agreement", sent.Status);
        Assert.InRange(sent.ResponseDeadline!.Value - DateTimeOffset.UtcNow, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24));
        travelerClient = await factory.OpenMagicLinkAsync(traveler);
        var seen = await travelerClient.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.True(seen!.Permissions.CanRespond);
        Assert.All(seen.Items, i => Assert.Equal($"OK with {i.Title}?", i.Question));
    }

    [Fact]
    public async Task After_the_window_closes_responses_are_rejected_and_only_responders_count()
    {
        var ana = VamooseFactory.NewEmail("ana");
        var ben = VamooseFactory.NewEmail("ben");
        var (organizer, tripId) = await PlannedTripAsync(ana, ben);
        var trip = await Send(organizer, tripId);
        var anaClient = await factory.OpenMagicLinkAsync(ana);
        var benClient = await factory.OpenMagicLinkAsync(ben);
        (await organizer.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)))).EnsureSuccessStatusCode();
        (await anaClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)))).EnsureSuccessStatusCode();

        await ExpireWindowAsync(tripId);
        await factory.RunAsync(sp => sp.GetRequiredService<AgreementService>().CloseExpiredWindowsAsync(CancellationToken.None));

        var late = await benClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var after = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.True(after!.Consensus);
        var notes = await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.Contains(notes!, n => n.Type == NotificationTypes.WindowClosed && n.Message.Contains("2 of 3"));
    }

    [Fact]
    public async Task Minority_traveler_is_nudged_then_nudged_once_more_in_chat_and_no_more()
    {
        var ana = VamooseFactory.NewEmail("ana");
        var ben = VamooseFactory.NewEmail("ben");
        var cleo = VamooseFactory.NewEmail("cleo");
        var (organizer, tripId) = await PlannedTripAsync(ana, ben, cleo);
        var trip = await Send(organizer, tripId);
        var lodging = trip.Items.Single(i => i.Key == "lodging-main");
        var hostel = lodging.Options.Single(o => o.Title == "Hostel").Id;
        var cleoClient = await factory.OpenMagicLinkAsync(cleo);

        foreach (var email in new[] { ana, ben })
        {
            var c = await factory.OpenMagicLinkAsync(email);
            (await c.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)))).EnsureSuccessStatusCode();
        }
        var cleoChoices = Current(trip).Select(c => c.ItemId == lodging.Id ? c with { OptionId = hostel } : c).ToList();
        (await cleoClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(cleoChoices))).EnsureSuccessStatusCode();

        var chat = await cleoClient.GetFromJsonAsync<List<ChatMessageDto>>($"/api/trips/{tripId}/chat");
        Assert.Single(chat!, m => m.Content == "Most chose the apartment.");

        var nudgesBefore = factory.Agent.Calls.Count(c => c.Nudge?.Kind == NudgeKinds.Majority);
        await cleoClient.PostAsJsonAsync($"/api/trips/{tripId}/chat", new ChatRequest("Why?"));
        await cleoClient.PostAsJsonAsync($"/api/trips/{tripId}/chat", new ChatRequest("Hmm"));
        var secondNudges = factory.Agent.Calls.Count(c => c.Nudge?.Kind == NudgeKinds.Majority) - nudgesBefore;
        Assert.Equal(1, secondNudges);
    }

    [Fact]
    public async Task Leaving_asks_the_agent_to_persuade_and_only_the_primary_user_hears_about_it()
    {
        var ana = VamooseFactory.NewEmail("ana");
        var ben = VamooseFactory.NewEmail("ben");
        var (organizer, tripId) = await PlannedTripAsync(ana, ben);
        await Send(organizer, tripId);
        var anaClient = await factory.OpenMagicLinkAsync(ana);
        var benClient = await factory.OpenMagicLinkAsync(ben);

        var preview = await (await anaClient.PostAsync($"/api/trips/{tripId}/exit/preview", null)).Content.ReadFromJsonAsync<ExitPreviewDto>();
        Assert.Equal("Please stay!", preview!.Message);
        Assert.Equal(HttpStatusCode.NoContent, (await anaClient.PostAsync($"/api/trips/{tripId}/exit", null)).StatusCode);

        Assert.Contains(await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications") ?? [], n => n.Type == NotificationTypes.MemberExited);
        Assert.DoesNotContain(await benClient.GetFromJsonAsync<List<NotificationDto>>("/api/notifications") ?? [], n => n.Type == NotificationTypes.MemberExited);
        Assert.Equal(HttpStatusCode.NotFound, (await anaClient.GetAsync($"/api/trips/{tripId}")).StatusCode);
    }

    [Fact]
    public async Task Trip_survives_when_the_agent_is_down_at_start()
    {
        factory.Agent.Down = true;
        try
        {
            var client = factory.NewClient();
            var response = await client.PostAsJsonAsync("/api/start",
                new StartTripRequest("Lisbon", "Lisbon", "Lisbon trip", VamooseFactory.NewEmail("org"), []));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            factory.Agent.Down = false;
        }
    }

    private async Task<(HttpClient Organizer, Guid TripId)> PlannedTripAsync(params string[] travelers)
    {
        var (organizer, tripId) = await factory.StartTripAsync(VamooseFactory.NewEmail("org"), travelers);
        var chat = await (await organizer.PostAsJsonAsync($"/api/trips/{tripId}/chat", new ChatRequest("We're 4 from NYC, mid budget"))).Content.ReadFromJsonAsync<ChatResponseDto>();
        Assert.True(chat!.PlanUpdated);
        return (organizer, tripId);
    }

    private static async Task<TripDetailDto> Send(HttpClient organizer, Guid tripId)
    {
        var response = await organizer.PostAsync($"/api/trips/{tripId}/send", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TripDetailDto>())!;
    }

    private static List<ChoiceDto> Current(TripDetailDto trip) =>
        [.. trip.Items.Select(i => new ChoiceDto(i.Id, i.SelectedOptionId!.Value))];

    private Task ExpireWindowAsync(Guid tripId) => factory.RunAsync(async sp =>
    {
        var db = sp.GetRequiredService<VamooseDbContext>();
        var trip = await db.Trips.SingleAsync(t => t.Id == tripId);
        trip.ResponseDeadline = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    });
}

public class AgentMapperTests
{
    [Fact]
    public void Agent_output_is_checked_before_it_is_saved()
    {
        var plan = new AgentTripPlan(
        [
            new AgentPlanSlot("bad", "Spaceship", "Not a category", 1, null, 0, [new AgentPlanOption("x")]),
            new AgentPlanSlot("lodging", "Lodging", "Stay", 1, null, 9,
                [new AgentPlanOption("Hotel", SourceUrl: "javascript:alert(1)", EnergyLevel: "Extreme"), new AgentPlanOption("Hostel", SourceUrl: "https://example.com")]),
        ]);

        var items = AgentMapper.ToItems(Guid.NewGuid(), plan);

        var item = Assert.Single(items);
        Assert.Null(item.Options[0].SourceUrl);
        Assert.Null(item.Options[0].EnergyLevel);
        Assert.Equal("https://example.com", item.Options[1].SourceUrl);
        Assert.Equal(item.Options[1].Id, item.SelectedOptionId);
    }
}
