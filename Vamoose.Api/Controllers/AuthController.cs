using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;
using Vamoose.Api.Dtos;
using Vamoose.Api.Infrastructure;

namespace Vamoose.Api.Controllers;

// There is no login: a member's magic link is their only access (requirements 3.2).
[ApiController]
[Route("api/auth")]
public class AuthController(VamooseDbContext db, CurrentUser currentUser) : ControllerBase
{
    [HttpPost("magic")]
    public async Task<ActionResult<MagicLinkResponse>> MagicLink(MagicLinkRequest request, CancellationToken ct)
    {
        var member = await db.TripMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.AccessToken == request.Token, ct);
        if (member is null)
        {
            throw new NotFoundException("This link isn't valid. Ask the trip organizer for a new one.");
        }

        await SignInAsync(HttpContext, member.User);
        return new MagicLinkResponse(new UserDto(member.User.Id, member.User.Email), member.TripId);
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

    // Also sets the request's user so the rest of the request runs as them.
    public static async Task SignInAsync(HttpContext context, User user)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email)],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(principal, new AuthenticationProperties { IsPersistent = true });
        context.User = principal;
    }
}
