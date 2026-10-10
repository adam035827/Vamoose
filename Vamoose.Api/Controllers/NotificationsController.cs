using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(NotificationService notifications, CurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> List(CancellationToken ct) => notifications.ListAsync(currentUser.Id, ct);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(currentUser.Id, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkReadAsync(currentUser.Id, null, ct);
        return NoContent();
    }
}
