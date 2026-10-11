using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vamoose.Api.Dtos;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/trips")]
public class TripsController(
    TripService trips,
    PlanService plans,
    AgreementService agreement,
    LifecycleService lifecycle) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TripSummaryDto>> List(CancellationToken ct) => trips.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<TripDetailDto> Get(Guid id, CancellationToken ct) => trips.GetAsync(id, ct);

    [HttpGet("{id:guid}/magic-links")]
    public Task<IReadOnlyList<MagicLinkDto>> MagicLinks(Guid id, CancellationToken ct) => trips.MagicLinksAsync(id, ct);

    [HttpPost("{id:guid}/members")]
    public async Task<TripDetailDto> AddMember(Guid id, AddMemberRequest request, CancellationToken ct)
    {
        await trips.AddMemberAsync(id, request.Email, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpGet("{id:guid}/chat")]
    public Task<IReadOnlyList<ChatMessageDto>> GetChat(Guid id, CancellationToken ct) => plans.GetChatAsync(id, ct);

    [HttpPost("{id:guid}/chat")]
    public Task<ChatResponseDto> Chat(Guid id, ChatRequest request, CancellationToken ct) => plans.ChatAsync(id, request.Message.Trim(), ct);

    [HttpPut("{id:guid}/items/{itemId:guid}/selection")]
    public async Task<TripDetailDto> Select(Guid id, Guid itemId, SelectOptionRequest request, CancellationToken ct)
    {
        await plans.SelectOptionAsync(id, itemId, request.OptionId, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/send")]
    public async Task<TripDetailDto> Send(Guid id, CancellationToken ct)
    {
        await agreement.SendAsync(id, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/responses")]
    public async Task<TripDetailDto> Respond(Guid id, RespondRequest request, CancellationToken ct)
    {
        await agreement.RespondAsync(id, request.Choices, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/change-requests/{requestId:guid}/approve")]
    public async Task<TripDetailDto> ApproveChange(Guid id, Guid requestId, CancellationToken ct)
    {
        await agreement.DecideChangeAsync(id, requestId, approve: true, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/change-requests/{requestId:guid}/deny")]
    public async Task<TripDetailDto> DenyChange(Guid id, Guid requestId, CancellationToken ct)
    {
        await agreement.DecideChangeAsync(id, requestId, approve: false, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/exit/preview")]
    public Task<ExitPreviewDto> ExitPreview(Guid id, CancellationToken ct) => lifecycle.ExitPreviewAsync(id, ct);

    [HttpPost("{id:guid}/exit")]
    public async Task<IActionResult> Exit(Guid id, CancellationToken ct)
    {
        await lifecycle.ExitAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<TripDetailDto> Reopen(Guid id, CancellationToken ct)
    {
        await lifecycle.ReopenAsync(id, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/lock")]
    public async Task<TripDetailDto> Lock(Guid id, CancellationToken ct)
    {
        await lifecycle.LockAsync(id, ct);
        return await trips.GetAsync(id, ct);
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<TripDetailDto> Unlock(Guid id, CancellationToken ct)
    {
        await lifecycle.UnlockAsync(id, ct);
        return await trips.GetAsync(id, ct);
    }
}
