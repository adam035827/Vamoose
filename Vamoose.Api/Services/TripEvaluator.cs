using System.Threading.Channels;

namespace Vamoose.Api.Services;

public class TripEvaluationQueue
{
    private readonly Channel<Guid> _trips = Channel.CreateUnbounded<Guid>();

    public void Enqueue(Guid tripId) => _trips.Writer.TryWrite(tripId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _trips.Reader.ReadAllAsync(ct);
}

// Settles the group's vote after a traveler answers, so their request returns without waiting for the agent.
public class TripEvaluator(TripEvaluationQueue queue, IServiceScopeFactory scopes, ILogger<TripEvaluator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One at a time: two last answers arriving together must not settle the same plan version twice.
        await foreach (var tripId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgreementService>().EvaluateTripAsync(tripId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Settling the group's answers failed for trip {TripId}", tripId);
            }
        }
    }
}
