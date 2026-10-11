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
    public async Task Starting_a_trip_creates_members_signs_in_organizer_and_sends_everyone_a_link()
    {
        var organizer = VamooseFactory.NewEmail("org");
        var traveler = VamooseFactory.NewEmail("ana");

        var (client, tripId) = await factory.StartTripAsync(organizer, traveler, organizer.ToUpperInvariant());

        var trip = await client.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.Equal("Lisbon, Portugal", trip!.Destination);
        Assert.Equal("Primary", trip.MyRole);
        Assert.Equal(2, trip.Members.Count);
        Assert.NotNull(VamooseFactory.MagicTokenFor(organizer));
        Assert.NotNull(VamooseFactory.MagicTokenFor(traveler));

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
    public async Task Traveler_link_opens_a_waiting_trip_before_it_is_sent()
    {
        var traveler = VamooseFactory.NewEmail("ben");
        var (_, tripId) = await PlannedTripAsync(traveler);

        var travelerClient = await factory.OpenMagicLinkAsync(traveler);
        var seen = await travelerClient.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");

        Assert.Equal("Planning", seen!.Status);
        Assert.Empty(seen.Items);
        Assert.False(seen.Permissions.CanChat);
        Assert.False(seen.Permissions.CanRespond);
        var chat = await travelerClient.PostAsJsonAsync($"/api/trips/{tripId}/chat", new ChatRequest("Hi"));
        Assert.Equal(HttpStatusCode.Conflict, chat.StatusCode);
    }

    [Fact]
    public async Task Organizer_can_list_magic_links_only_when_enabled()
    {
        var traveler = VamooseFactory.NewEmail("ana");
        var (organizer, tripId) = await factory.StartTripAsync(VamooseFactory.NewEmail("org"), traveler);

        var links = await organizer.GetFromJsonAsync<List<MagicLinkDto>>($"/api/trips/{tripId}/magic-links");

        Assert.Equal(["Primary", "Traveler"], links!.Select(l => l.Role));
        Assert.EndsWith($"/t/{VamooseFactory.MagicTokenFor(traveler)}", links[1].Link);
        var travelerClient = await factory.OpenMagicLinkAsync(traveler);
        Assert.Equal(HttpStatusCode.Forbidden, (await travelerClient.GetAsync($"/api/trips/{tripId}/magic-links")).StatusCode);
    }

    [Fact]
    public async Task Sending_shows_travelers_the_questions_and_opens_a_24_hour_window()
    {
        var traveler = VamooseFactory.NewEmail("ben");
        var (organizer, tripId) = await PlannedTripAsync(traveler);

        var sent = await Send(organizer, tripId);

        Assert.Equal("Agreement", sent.Status);
        Assert.InRange(sent.ResponseDeadline!.Value - DateTimeOffset.UtcNow, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24));
        var travelerClient = await factory.OpenMagicLinkAsync(traveler);
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
        (await anaClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)))).EnsureSuccessStatusCode();

        await ExpireWindowAsync(tripId);
        await factory.RunAsync(sp => sp.GetRequiredService<AgreementService>().CloseExpiredWindowsAsync(CancellationToken.None));

        var late = await benClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var after = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.True(after!.Decided);
        Assert.False(after.Permissions.CanRespond);
        var notes = await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.Single(notes!, n => n.Type == NotificationTypes.PlanDecided && n.Message.Contains("1 of 2 travelers"));
    }

    [Fact]
    public async Task Answers_stay_silent_until_everyone_responds_then_the_majority_choice_is_applied()
    {
        var ana = VamooseFactory.NewEmail("ana");
        var ben = VamooseFactory.NewEmail("ben");
        var (organizer, tripId) = await PlannedTripAsync(ana, ben);
        var trip = await Send(organizer, tripId);
        var lodging = trip.Items.Single(i => i.Key == "lodging-main");
        var hostel = lodging.Options.Single(o => o.Title == "Hostel").Id;
        var wantsHostel = Current(trip).Select(c => c.ItemId == lodging.Id ? c with { OptionId = hostel } : c).ToList();
        var notesBefore = (await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!.Count;

        var anaClient = await factory.OpenMagicLinkAsync(ana);
        (await anaClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(wantsHostel))).EnsureSuccessStatusCode();

        var waiting = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.Equal(lodging.SelectedOptionId, waiting!.Items.Single(i => i.Id == lodging.Id).SelectedOptionId);
        Assert.False(waiting.Decided);
        Assert.Equal(notesBefore, (await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!.Count);

        var benClient = await factory.OpenMagicLinkAsync(ben);
        (await benClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(wantsHostel))).EnsureSuccessStatusCode();
        var answered = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.False(answered!.Decided);
        await SettleAsync(tripId);

        var decided = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.True(decided!.Decided);
        Assert.Equal(hostel, decided.Items.Single(i => i.Id == lodging.Id).SelectedOptionId);
        var notes = await organizer.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.Equal(notesBefore + 1, notes!.Count);
        Assert.Contains("changed 1 item(s)", Assert.Single(notes, n => n.Type == NotificationTypes.PlanDecided).Message);
        var chat = await organizer.GetFromJsonAsync<List<ChatMessageDto>>($"/api/trips/{tripId}/chat");
        Assert.Contains(chat!, m => m.Content.Contains("Where we stay: Hostel, was Apartment (2 of 3 votes)"));
    }

    [Fact]
    public async Task Ties_are_broken_by_the_agent_or_keep_the_current_pick_when_it_is_down()
    {
        foreach (var down in new[] { false, true })
        {
            var ana = VamooseFactory.NewEmail("ana");
            var (organizer, tripId) = await PlannedTripAsync(ana);
            var trip = await Send(organizer, tripId);
            var lodging = trip.Items.Single(i => i.Key == "lodging-main");
            var hostel = lodging.Options.Single(o => o.Title == "Hostel").Id;
            var anaClient = await factory.OpenMagicLinkAsync(ana);
            var own = await organizer.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(Current(trip)));
            Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
            Assert.False(trip.Permissions.CanRespond);

            factory.Agent.Down = down;
            try
            {
                var wantsHostel = Current(trip).Select(c => c.ItemId == lodging.Id ? c with { OptionId = hostel } : c).ToList();
                (await anaClient.PostAsJsonAsync($"/api/trips/{tripId}/responses", new RespondRequest(wantsHostel))).EnsureSuccessStatusCode();
                await SettleAsync(tripId);
            }
            finally
            {
                factory.Agent.Down = false;
            }

            var decided = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
            Assert.True(decided!.Decided);
            Assert.Equal(down ? lodging.SelectedOptionId : hostel, decided.Items.Single(i => i.Id == lodging.Id).SelectedOptionId);
            var chat = await organizer.GetFromJsonAsync<List<ChatMessageDto>>($"/api/trips/{tripId}/chat");
            Assert.Contains(chat!, m => m.Content.Contains(down ? "tie, so the current pick stayed" : "tie, so the agent picked it: Better value."));
        }
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

    [Fact]
    public async Task First_plan_gets_real_options_in_the_background_unless_the_plan_changed()
    {
        var (organizer, tripId) = await PlannedTripAsync();
        Assert.True((await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}"))!.ResearchPending);
        var version = 0;
        await factory.RunAsync(async sp => version = (await sp.GetRequiredService<VamooseDbContext>().Trips.SingleAsync(t => t.Id == tripId)).PlanVersion);

        await factory.RunAsync(sp => sp.GetRequiredService<PlanService>().ApplyResearchAsync(tripId, version, CancellationToken.None));

        var trip = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.False(trip!.ResearchPending);
        Assert.All(trip.Items, i => Assert.Contains(i.Options, o => o.Title == "Researched pick" && o.SourceUrl == "https://example.com/real"));
        var chat = await organizer.GetFromJsonAsync<List<ChatMessageDto>>($"/api/trips/{tripId}/chat");
        Assert.Equal("I found real options.", chat!.Last().Content);

        // The same job again is stale (the plan has moved on), so the agent isn't even called.
        var researchCalls = factory.Agent.Calls.Count(c => c.Action == AgentActions.Research);
        await factory.RunAsync(sp => sp.GetRequiredService<PlanService>().ApplyResearchAsync(tripId, version, CancellationToken.None));
        Assert.Equal(researchCalls, factory.Agent.Calls.Count(c => c.Action == AgentActions.Research));
    }

    [Fact]
    public async Task Research_is_no_longer_pending_when_the_agent_is_down()
    {
        var (organizer, tripId) = await PlannedTripAsync();
        var version = 0;
        await factory.RunAsync(async sp => version = (await sp.GetRequiredService<VamooseDbContext>().Trips.SingleAsync(t => t.Id == tripId)).PlanVersion);

        factory.Agent.Down = true;
        try
        {
            await factory.RunAsync(sp => sp.GetRequiredService<PlanService>().ApplyResearchAsync(tripId, version, CancellationToken.None));
        }
        finally
        {
            factory.Agent.Down = false;
        }

        var trip = await organizer.GetFromJsonAsync<TripDetailDto>($"/api/trips/{tripId}");
        Assert.False(trip!.ResearchPending);
        Assert.True(trip.Permissions.CanSend);
    }

    [Fact]
    public void Each_trip_reuses_its_agent_session_and_the_trip_id_is_never_sent_to_the_agent()
    {
        var tripId = Guid.NewGuid();
        var chat = new AgentInvocation("req-1", AgentActions.Chat, Message: "hi", TripId: tripId);

        var json = System.Text.Json.JsonSerializer.Serialize(chat, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.DoesNotContain("tripId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentCoreAgentClient.SessionId(chat), AgentCoreAgentClient.SessionId(chat with { RequestId = "req-2" }));
        Assert.NotEqual(AgentCoreAgentClient.SessionId(chat), AgentCoreAgentClient.SessionId(chat with { Action = AgentActions.Research }));
        Assert.True(AgentCoreAgentClient.SessionId(chat).Length >= 33);
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

    // What TripEvaluator does in the background after an answer.
    private Task SettleAsync(Guid tripId) =>
        factory.RunAsync(sp => sp.GetRequiredService<AgreementService>().EvaluateTripAsync(tripId, CancellationToken.None));

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
