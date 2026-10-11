using System.Threading.Channels;

namespace Vamoose.Api.Services;

public class PlanResearchQueue
{
    private readonly Channel<(Guid TripId, int PlanVersion)> _jobs = Channel.CreateUnbounded<(Guid, int)>();

    public void Enqueue(Guid tripId, int planVersion) => _jobs.Writer.TryWrite((tripId, planVersion));

    public IAsyncEnumerable<(Guid TripId, int PlanVersion)> ReadAllAsync(CancellationToken ct) => _jobs.Reader.ReadAllAsync(ct);
}

// Researches real options and links for a fast first draft after the organizer already sees it.
public class PlanResearcher(PlanResearchQueue queue, IServiceScopeFactory scopes, ILogger<PlanResearcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (tripId, planVersion) in queue.ReadAllAsync(stoppingToken))
        {
            // Not awaited: one trip's research shouldn't hold up another's.
            _ = RunAsync(tripId, planVersion, stoppingToken);
        }
    }

    private async Task RunAsync(Guid tripId, int planVersion, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PlanService>().ApplyResearchAsync(tripId, planVersion, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Plan research failed for trip {TripId}", tripId);
        }
    }
}
