namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Background cron that keeps SPE change-notification subscriptions alive under the Graph
/// 4230-minute lifespan ceiling (spaarkeai-compose-r2 FR-26, design §2.5 + §6.2). On each
/// scan it asks <see cref="SpeSyncOrchestrator.RenewDueSubscriptionsAsync"/> to renew every
/// tracked subscription that has entered the <see cref="SpeSyncOrchestrator.RenewalMargin"/>.
///
/// <para>
/// <b>ADR-001</b> BackgroundService pattern (in-process async loop; NOT an Azure Function).
/// Modeled directly on <see cref="StaleCheckoutSweeperHostedService"/>: static
/// <see cref="ScanInterval"/>, <see cref="TimeProvider"/>-driven <c>Task.Delay</c>,
/// per-iteration try/catch, and a per-iteration <see cref="IServiceProvider.CreateScope"/>
/// to resolve the scoped <see cref="SpeSyncOrchestrator"/>.
/// </para>
///
/// <para>
/// <b>ADR-010</b> DI minimalism: registered once via
/// <c>services.AddHostedService&lt;SpeWebhookRenewalHostedService&gt;()</c> in
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.ComposeModule"/>.
/// </para>
///
/// <para>
/// <b>Resilience</b>: each scan iteration is try/catch-wrapped so a transient Graph or Redis
/// failure never crashes the host or stops the loop. Per-subscription renewal failures are
/// handled one level down (the orchestrator flags that subscription for delta-poll fallback
/// and continues), so one bad subscription never aborts the sweep.
/// </para>
///
/// <para>
/// <b>Cadence</b>: <see cref="ScanInterval"/> (30 min) is far below the 4230-minute lifespan
/// and comfortably below the 120-minute <see cref="SpeSyncOrchestrator.RenewalMargin"/>, so
/// the renewal window is never skipped between scans.
/// </para>
/// </summary>
public sealed class SpeWebhookRenewalHostedService : BackgroundService
{
    /// <summary>
    /// How often the renewal cron scans tracked subscriptions. Far under the 4230-minute
    /// subscription lifespan and below <see cref="SpeSyncOrchestrator.RenewalMargin"/>.
    /// </summary>
    internal static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpeWebhookRenewalHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public SpeWebhookRenewalHostedService(
        IServiceProvider serviceProvider,
        ILogger<SpeWebhookRenewalHostedService> logger,
        TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SpeWebhookRenewalHostedService started — scan every {ScanIntervalMinutes}min, renewal margin {RenewalMarginMinutes}min, subscription lifespan {LifespanMinutes}min",
            ScanInterval.TotalMinutes,
            SpeSyncOrchestrator.RenewalMargin.TotalMinutes,
            SpeSyncOrchestrator.SubscriptionLifetime.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RenewDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Per-iteration safety: never let a transient failure kill the host.
                _logger.LogError(ex, "SpeWebhookRenewalHostedService scan iteration failed; will retry next interval");
            }

            try
            {
                await Task.Delay(ScanInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("SpeWebhookRenewalHostedService stopped");
    }

    /// <summary>
    /// Single scan iteration: resolve the scoped orchestrator and renew due subscriptions.
    /// Internal for unit-test access via <see cref="RenewDueOnceAsync"/>.
    /// </summary>
    internal async Task RenewDueAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<SpeSyncOrchestrator>();
        await RenewDueOnceAsync(orchestrator, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure variant exposed for unit tests — takes the resolved
    /// <see cref="SpeSyncOrchestrator"/> directly so tests drive delegation without touching
    /// the DI container or the <see cref="ScanInterval"/> loop.
    /// </summary>
    internal Task RenewDueOnceAsync(SpeSyncOrchestrator orchestrator, CancellationToken ct)
        => orchestrator.RenewDueSubscriptionsAsync(ct);
}
