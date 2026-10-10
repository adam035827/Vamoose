using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;

namespace Vamoose.Api.Services;

public class UserService(VamooseDbContext db)
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // Users exist as soon as someone logs in or is added to a trip by email.
    public async Task<User> GetOrCreateAsync(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is not null)
        {
            return user;
        }

        user = new User { Id = Guid.NewGuid(), Email = normalized, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
