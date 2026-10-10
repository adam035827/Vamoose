using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Vamoose.Api.Data;
using Vamoose.Api.Domain;

namespace Vamoose.Api.Services;

public class UserService(VamooseDbContext db)
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) => email.Length <= 254 && EmailValidator.IsValid(email);

    // Users exist as soon as someone starts a trip or is added to one by email.
    public async Task<User> GetOrCreateAsync(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        var user = db.Users.Local.FirstOrDefault(u => u.Email == normalized)
            ?? await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
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
