using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Periodic sweep that enqueues a membership reconcile for every messaging thread as the eventual-consistency
/// safety net (messaging-communication-app-r1 task 041 / FR-07; design §8.4). Complements event-driven
/// reconcile: if an event was missed (fire-and-forget enqueue dropped, or a Dataverse change had no BFF hook),
/// the sweep repairs ACS↔Dataverse drift within one interval.
/// </summary>
/// <remarks>
/// Kill-switched via <c>Communication:MembershipReconcile:SweepEnabled</c> (default off) — the sweep is a
/// backstop, so the feature is fully operational (event-driven) without it. Best-effort throughout (NFR-02):
/// a sweep-pass failure is logged and the timer continues; individual enqueues are best-effort in the
/// enqueuer. Enumerates messaging threads via their <c>sprk_communicationchannelref</c> rows (Message channel,
/// non-empty external ref = an ACS chat thread exists to reconcile).
/// </remarks>
public sealed class MembershipReconcileSweepService : BackgroundService
{
    private const string ChannelRefEntity = "sprk_communicationchannelref";

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<MembershipReconcileOptions> _options;
    private readonly ILogger<MembershipReconcileSweepService> _logger;

    public MembershipReconcileSweepService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<MembershipReconcileOptions> options,
        ILogger<MembershipReconcileSweepService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = _options.CurrentValue.InitialDelay;
        if (initial > TimeSpan.Zero)
        {
            try { await Task.Delay(initial, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.CurrentValue.SweepEnabled)
            {
                try
                {
                    await RunSweepPassAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // NFR-02: a sweep-pass failure never crashes the host; the next pass retries.
                    _logger.LogWarning(ex, "Membership reconcile sweep pass failed; will retry next interval.");
                }
            }

            try { await Task.Delay(_options.CurrentValue.SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Enumerates messaging threads (Message channel-refs) and enqueues a reconcile per distinct thread.</summary>
    internal async Task RunSweepPassAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
        var enqueuer = scope.ServiceProvider.GetRequiredService<MembershipReconcileEnqueuer>();
        var max = Math.Max(1, _options.CurrentValue.MaxThreadsPerSweep);

        var query = new QueryExpression(ChannelRefEntity)
        {
            ColumnSet = new ColumnSet("sprk_thread", "sprk_externalref"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_channeltype", ConditionOperator.Equal, (int)CommunicationType.Message),
                    new ConditionExpression("sprk_externalref", ConditionOperator.NotNull),
                    new ConditionExpression("sprk_thread", ConditionOperator.NotNull),
                },
            },
            TopCount = max,
        };

        var result = await entityService.RetrieveMultipleAsync(query, ct);

        var threadIds = result.Entities
            .Select(e => e.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_thread")?.Id ?? Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        _logger.LogInformation("Membership reconcile sweep: enqueuing {Count} messaging thread(s).", threadIds.Count);

        foreach (var threadId in threadIds)
        {
            ct.ThrowIfCancellationRequested();
            await enqueuer.EnqueueAsync(threadId, MembershipReconcileTrigger.PeriodicSweep, "system", ct: ct);
        }
    }
}
