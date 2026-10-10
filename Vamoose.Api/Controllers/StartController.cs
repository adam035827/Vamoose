using Microsoft.AspNetCore.Mvc;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

// Starting a trip through the agent instead of a form (requirements 3.1). Open to anyone.
[ApiController]
[Route("api/start")]
public class StartController(TripService trips, PlanService plans, ILogger<StartController> logger) : ControllerBase
{
    [HttpPost("intro")]
    public Task<StartIntroResponse> Intro(StartIntroRequest request, CancellationToken ct) => trips.IntroAsync(request.Message.Trim(), ct);

    [HttpPost]
    public async Task<ActionResult<StartTripResponse>> Start(StartTripRequest request, CancellationToken ct)
    {
        var (trip, organizer) = await trips.StartAsync(request, ct);
        await AuthController.SignInAsync(HttpContext, organizer);

        // The agent follows up with questions about the group; the trip exists even if the agent is down.
        try
        {
            await plans.ChatAsync(trip.Id, request.Prompt.Trim(), ct);
        }
        catch (AgentUnavailableException ex)
        {
            logger.LogWarning(ex, "First agent message skipped for trip {TripId}", trip.Id);
        }

        return CreatedAtAction(nameof(TripsController.Get), "Trips", new { id = trip.Id }, new StartTripResponse(trip.Id, new UserDto(organizer.Id, organizer.Email)));
    }
}
