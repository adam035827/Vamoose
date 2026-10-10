using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vamoose.Api.Dtos;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/trips")]
public class TripsController(TripService trips) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TripSummaryDto>> List(CancellationToken ct) => trips.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<TripDetailDto>> Create(CreateTripRequest request, CancellationToken ct)
    {
        var id = await trips.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, await trips.GetAsync(id, ct));
    }

    [HttpGet("{id:guid}")]
    public Task<TripDetailDto> Get(Guid id, CancellationToken ct) => trips.GetAsync(id, ct);

    [HttpPost("{id:guid}/members")]
    public async Task<TripDetailDto> AddMember(Guid id, AddMemberRequest request, CancellationToken ct)
    {
        await trips.AddMemberAsync(id, request.Email, ct);
        return await trips.GetAsync(id, ct);
    }
}
