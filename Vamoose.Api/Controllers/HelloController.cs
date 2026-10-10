using Microsoft.AspNetCore.Mvc;

namespace Vamoose.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HelloController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "Hello, World from Vamoose API!" });
}
