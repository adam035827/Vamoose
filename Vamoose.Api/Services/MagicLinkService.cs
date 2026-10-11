using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Vamoose.Api.Domain;

namespace Vamoose.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public record OutboxEmail(string To, string Subject, string Body, DateTimeOffset SentAt);

// No email provider is set up yet: emails are logged and kept in memory so developers can open magic links.
public class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    private static readonly ConcurrentQueue<OutboxEmail> Outbox = new();

    public static IReadOnlyList<OutboxEmail> Sent => [.. Outbox.Reverse()];

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        Outbox.Enqueue(new OutboxEmail(to, subject, body, DateTimeOffset.UtcNow));
        while (Outbox.Count > 200)
        {
            Outbox.TryDequeue(out _);
        }
        logger.LogInformation("Email to {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}

// Each member has a magic link per trip; it is the only access needed (requirements 3.2).
public class MagicLinkService(IEmailSender email, IConfiguration config, ILogger<MagicLinkService> logger)
{
    // Development shows each member's link to the organizer's browser; off on the server unless configured.
    public bool ExposeLinks => config.GetValue("App:ExposeMagicLinks", false);

    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string LinkFor(TripMember member) =>
        $"{(config["App:BaseUrl"] ?? "http://localhost:4200").TrimEnd('/')}/t/{member.AccessToken}";

    public async Task SendAsync(Trip trip, TripMember member, CancellationToken ct)
    {
        var subject = member.Role == MemberRole.Primary
            ? $"Your Vamoose link for \"{trip.Name}\""
            : $"{trip.PrimaryUser.Email} invited you to \"{trip.Name}\" on Vamoose";
        var body = $"""
            Open your trip: {LinkFor(member)}

            This link is your access to the trip. Don't share it.
            """;
        await email.SendAsync(member.User.Email, subject, body, ct);
        member.LinkSentAt = DateTimeOffset.UtcNow;

        // No email provider yet, so the link itself goes to the console.
        logger.LogInformation("Magic link for {Email} ({Role}) on \"{Trip}\": {Link}", member.User.Email, member.Role, trip.Name, LinkFor(member));
    }
}
