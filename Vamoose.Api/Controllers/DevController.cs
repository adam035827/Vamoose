using Microsoft.AspNetCore.Mvc;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

// Development only: shows the emails (magic links) the app would have sent.
[ApiController]
[Route("api/dev")]
public class DevController(IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("outbox")]
    public ActionResult<IReadOnlyList<OutboxEmail>> Outbox() =>
        env.IsDevelopment() ? Ok(DevEmailSender.Sent) : NotFound();
}
