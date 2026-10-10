using System.Security.Claims;

namespace Vamoose.Api.Infrastructure;

public class CurrentUser(IHttpContextAccessor accessor)
{
    public Guid Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException();
}
