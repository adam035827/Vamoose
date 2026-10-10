namespace Vamoose.Api.Services;

// Checks once a minute for 24-hour response windows that have run out (requirements 1, 4.2).
public class ResponseWindowCloser(IServiceScopeFactory scopes, ILogger<ResponseWindowCloser> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgreementService>().CloseExpiredWindowsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Closing expired response windows failed");
            }
        }
    }
}
