using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vamoose.Api.Agents;
using Vamoose.Api.Data;
using Vamoose.Api.Dtos;
using Vamoose.Api.Services;

namespace Vamoose.Api.Tests;

// Runs the real API with an in-memory SQLite database and a fake agent client (requirements 6.1).
public partial class VamooseFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeAgentClient Agent { get; } = new();

    public VamooseFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Vamoose", "unused");
        builder.UseSetting("App:ExposeMagicLinks", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VamooseDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<VamooseDbContext>>();
            services.AddDbContext<VamooseDbContext>(o => o.UseSqlite(_connection));
            services.RemoveAll<IAgentClient>();
            services.AddSingleton<IAgentClient>(Agent);

            // Tests close response windows explicitly instead of waiting for the timer.
            var closer = services.Single(d => d.ImplementationType == typeof(ResponseWindowCloser));
            services.Remove(closer);

            // Tests run plan research explicitly; a background job would share the in-memory database connection.
            services.Remove(services.Single(d => d.ImplementationType == typeof(PlanResearcher)));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<VamooseDbContext>().Database.EnsureCreated();
        return host;
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    public async Task RunAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    public static string NewEmail(string name) => $"{name}-{Guid.NewGuid():N}@test.dev";

    public static string? MagicTokenFor(string email) =>
        DevEmailSender.Sent.Where(e => e.To == email).Select(e => MagicLink().Match(e.Body)).FirstOrDefault(m => m.Success)?.Groups[1].Value;

    // Signs a fresh client in through the member's emailed magic link.
    public async Task<HttpClient> OpenMagicLinkAsync(string email)
    {
        var token = MagicTokenFor(email) ?? throw new InvalidOperationException($"No magic link was emailed to {email}.");
        var client = NewClient();
        (await client.PostAsJsonAsync("/api/auth/magic", new MagicLinkRequest(token))).EnsureSuccessStatusCode();
        return client;
    }

    // Starts a trip through the agent the way the home page does, signed in as the organizer.
    public async Task<(HttpClient Organizer, Guid TripId)> StartTripAsync(string organizerEmail, params string[] travelers)
    {
        var client = NewClient();
        var intro = await (await client.PostAsJsonAsync("/api/start/intro", new StartIntroRequest("Lisbon"))).Content.ReadFromJsonAsync<StartIntroResponse>();
        var response = await client.PostAsJsonAsync("/api/start",
            new StartTripRequest("Lisbon in May", intro!.Destination, intro.TripName, organizerEmail, [.. travelers]));
        response.EnsureSuccessStatusCode();
        var started = await response.Content.ReadFromJsonAsync<StartTripResponse>();
        return (client, started!.TripId);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }

    [GeneratedRegex(@"/t/([A-Za-z0-9_-]+)")]
    private static partial Regex MagicLink();
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
