using System.Threading.Channels;

namespace Vamoose.Api.Services;

public class QuestionWritingQueue
{
    private readonly Channel<(Guid TripId, int PlanVersion)> _jobs = Channel.CreateUnbounded<(Guid, int)>();

    public void Enqueue(Guid tripId, int planVersion) => _jobs.Writer.TryWrite((tripId, planVersion));

    public IAsyncEnumerable<(Guid TripId, int PlanVersion)> ReadAllAsync(CancellationToken ct) => _jobs.Reader.ReadAllAsync(ct);
}

// Swaps the template questions for the agent's wording after the trip is sent or edited, so neither request waits for it.
public class QuestionWriter(QuestionWritingQueue queue, IServiceScopeFactory scopes, ILogger<QuestionWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (tripId, planVersion) in queue.ReadAllAsync(stoppingToken))
        {
            // Not awaited: one trip's questions shouldn't hold up another's.
            _ = RunAsync(tripId, planVersion, stoppingToken);
        }
    }

    private async Task RunAsync(Guid tripId, int planVersion, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgreementService>().WriteQuestionsAsync(tripId, planVersion, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Travelers keep the template questions.
            logger.LogError(ex, "Writing questions failed for trip {TripId}", tripId);
        }
    }
}
