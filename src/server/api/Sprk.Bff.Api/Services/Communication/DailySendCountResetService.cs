namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Background service that resets sprk_sendstoday to 0 for all communication accounts
/// at midnight UTC each day. Implements ADR-001 BackgroundService pattern.
///
/// Uses a simple loop with delay-until-midnight approach rather than PeriodicTimer,
/// since the interval is exactly once per day at a specific wall-clock time.
/// </summary>
public sealed class DailySendCountResetService : BackgroundService
{
    private readonly CommunicationAccountService _accountService;
    private readonly ILogger<DailySendCountResetService> _logger;

    public DailySendCountResetService(
        CommunicationAccountService accountService,
        ILogger<DailySendCountResetService> logger)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailySendCountResetService started — will reset send counts at midnight UTC each day");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delayUntilMidnight = CalculateDelayUntilMidnightUtc();

                _logger.LogDebug(
                    "DailySendCountResetService sleeping for {Hours:F1} hours until next midnight UTC",
                    delayUntilMidnight.TotalHours);

                await Task.Delay(delayUntilMidnight, stoppingToken);

                await ResetSendCountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DailySendCountResetService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during daily send count reset, will retry at next midnight");

                // Wait 1 minute before retrying to avoid tight error loops
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("DailySendCountResetService stopped");
    }

    private async Task ResetSendCountsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting daily send count reset at {Time:u}", DateTime.UtcNow);

        try
        {
            await _accountService.ResetAllSendCountsAsync(ct);
            _logger.LogInformation("Daily send count reset completed successfully at {Time:u}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily send count reset failed");
            throw; // Let outer loop handle retry
        }
    }

    /// <summary>
    /// Calculates the delay from now until the next midnight UTC.
    /// If it's exactly midnight, schedules for tomorrow's midnight.
    /// </summary>
    internal static TimeSpan CalculateDelayUntilMidnightUtc()
    {
        var now = DateTime.UtcNow;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;

        // Safety: ensure at least 1 second delay to avoid tight loops
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromDays(1) : delay;
    }
}
