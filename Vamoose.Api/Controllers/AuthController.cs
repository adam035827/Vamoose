using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;
using Vamoose.Api.Services;

namespace Vamoose.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserService users, VamooseDbContext db, CurrentUser currentUser) : ControllerBase
{
    // Email-only login with no verification, as agreed for the MVP (requirements 3.1).
    [HttpPost("login")]
    public async Task<UserDto> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.GetOrCreateAsync(request.Email, ct);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
        return new UserDto(user.Id, user.Email);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.Id, ct);
        return user is null ? Unauthorized() : new UserDto(user.Id, user.Email);
    }
}
