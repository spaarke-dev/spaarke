using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Background service that keeps <see cref="CapabilityManifest"/> fresh via two mechanisms:
///
///   1. <b>Scheduled polling</b> — a <see cref="PeriodicTimer"/> fires every
///      <see cref="ManifestRefreshOptions.RefreshIntervalMinutes"/> minutes (default 15).
///      The timer never drifts: each interval starts after the previous tick's work
///      completes, so a slow Dataverse response does not cause overlapping refreshes.
///
///   2. <b>Webhook trigger</b> (<see cref="IManifestRefreshTrigger"/>) — the
///      <c>POST /api/ai/capabilities/refresh</c> endpoint calls <see cref="TriggerRefresh"/>
///      which posts a signal to a bounded channel. The background loop drains that channel
///      between scheduled ticks, enabling near-immediate refreshes after a Dataverse change.
///
/// Error policy (stale-on-error):
///   If <see cref="ICapabilityManifestLoader.LoadAsync"/> throws, the existing manifest is
///   left untouched and a warning is logged. The service never clears the manifest on a
///   failed refresh — capability routing continues with the previous (possibly stale) data.
///
/// OTEL:
///   Increments <c>ai_capability_manifest_refresh_total</c> on every attempt with
///   labels <c>trigger=scheduled|webhook</c> and <c>result=success|failure</c>.
///
/// ADR-001: Uses <see cref="BackgroundService"/> (no Azure Functions).
/// ADR-010: Registered as <see cref="IHostedService"/> in <see cref="Infrastructure.DI.AiCapabilitiesModule"/>.
/// </summary>
public sealed class ManifestRefreshService : BackgroundService, IManifestRefreshTrigger
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const string MeterName = "Sprk.Bff.Api.AiCapabilities";
    private const string CounterName = "ai_capability_manifest_refresh_total";

    // Capacity of 1: only one pending wake-up signal is retained at a time.
    // If the loop is busy and two webhook calls arrive the second signal is silently
    // dropped — the loop will still refresh exactly once after it finishes.
    private static readonly BoundedChannelOptions ChannelOptions = new(capacity: 1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    };

    // ── Dependencies ─────────────────────────────────────────────────────────

    private readonly CapabilityManifest _manifest;
    private readonly ICapabilityManifestLoader _loader;
    private readonly ILogger<ManifestRefreshService> _logger;
    private readonly ManifestRefreshOptions _options;

    // ── OTEL ─────────────────────────────────────────────────────────────────

    private readonly Meter _meter;
    private readonly Counter<long> _refreshTotal;

    // ── Webhook wake-up channel ───────────────────────────────────────────────

    private readonly Channel<bool> _triggerChannel;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ManifestRefreshService(
        CapabilityManifest manifest,
        ICapabilityManifestLoader loader,
        ILogger<ManifestRefreshService> logger,
        IOptions<ManifestRefreshOptions> options)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _triggerChannel = Channel.CreateBounded<bool>(ChannelOptions);

        _meter = new Meter(MeterName, "1.0.0");
        _refreshTotal = _meter.CreateCounter<long>(
            name: CounterName,
            unit: "{refresh}",
            description: "Total number of CapabilityManifest refresh attempts, by trigger and result.");
    }

    // ── IManifestRefreshTrigger ───────────────────────────────────────────────

    /// <inheritdoc/>
    public void TriggerRefresh()
    {
        // TryWrite is non-blocking. If the channel is full (one pending signal already)
        // this is a no-op — the existing signal will wake the loop shortly.
        _triggerChannel.Writer.TryWrite(true);

        _logger.LogDebug("ManifestRefreshService: webhook trigger received, signalling background loop");
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <summary>
    /// Main background loop. Runs until the host signals cancellation.
    ///
    /// The loop structure is:
    ///   - Await either a PeriodicTimer tick OR a webhook channel signal (whichever comes first).
    ///   - Perform the refresh.
    ///   - Repeat.
    ///
    /// Because PeriodicTimer.WaitForNextTickAsync is not cancellable mid-flight with a
    /// concurrent channel read, we use Task.WhenAny to race the two awaitables.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _options.RefreshIntervalMinutes > 0
            ? _options.RefreshIntervalMinutes
            : 15;

        _logger.LogInformation(
            "ManifestRefreshService starting — polling interval: {IntervalMinutes} minutes",
            intervalMinutes);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Race: scheduled tick vs. webhook trigger.
                // The scheduled tick happens every N minutes.
                // A webhook signal can arrive at any time via TriggerRefresh().
                var trigger = await WaitForNextRefreshAsync(timer, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await PerformRefreshAsync(trigger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown — do not log as an error.
                break;
            }
            catch (Exception ex)
            {
                // Guard: this catch is a last resort. PerformRefreshAsync already handles
                // loader exceptions and never re-throws. This handles any unexpected failure
                // in the loop infrastructure itself.
                _logger.LogError(ex,
                    "ManifestRefreshService: unexpected error in background loop — will retry on next tick");

                // Brief delay to avoid a tight error loop.
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("ManifestRefreshService stopped");
    }

    /// <summary>
    /// Awaits either the next PeriodicTimer tick or a webhook trigger signal,
    /// whichever arrives first.
    /// </summary>
    /// <returns><c>"scheduled"</c> or <c>"webhook"</c> — the trigger label for OTEL.</returns>
    private async Task<string> WaitForNextRefreshAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken)
    {
        // Build two tasks that race:
        //   timerTask: fires after the configured interval
        //   webhookTask: fires when TriggerRefresh() writes to the channel
        var timerTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
        var webhookTask = _triggerChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();

        var winner = await Task.WhenAny(timerTask, webhookTask).ConfigureAwait(false);

        if (winner == webhookTask && await webhookTask.ConfigureAwait(false))
        {
            // Drain the channel so the signal doesn't re-fire immediately on the next iteration.
            while (_triggerChannel.Reader.TryRead(out _)) { }

            _logger.LogDebug("ManifestRefreshService: woken by webhook trigger");
            return "webhook";
        }

        // Either the timer fired, or the webhook task completed with false (channel closed).
        return "scheduled";
    }

    /// <summary>
    /// Loads the manifest from Dataverse and atomically swaps the singleton.
    /// Logs but never re-throws on loader failure (stale-on-error policy).
    /// Records the OTEL counter regardless of outcome.
    /// </summary>
    private async Task PerformRefreshAsync(string trigger, CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ManifestRefreshService: refreshing capability manifest (trigger={Trigger})", trigger);

        try
        {
            var entries = await _loader.LoadAsync(stoppingToken).ConfigureAwait(false);
            _manifest.Refresh(entries);

            _refreshTotal.Add(1,
                new KeyValuePair<string, object?>("trigger", trigger),
                new KeyValuePair<string, object?>("result", "success"));

            _logger.LogInformation(
                "ManifestRefreshService: manifest refreshed — {Count} enabled capabilities (trigger={Trigger})",
                _manifest.GetAll().Count, trigger);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — do not log as a refresh failure.
            throw;
        }
        catch (Exception ex)
        {
            // Stale-on-error: the existing manifest is untouched.
            // Log as Warning (not Error) because this is a transient Dataverse issue,
            // not a code defect. The next scheduled tick will retry automatically.
            _refreshTotal.Add(1,
                new KeyValuePair<string, object?>("trigger", trigger),
                new KeyValuePair<string, object?>("result", "failure"));

            _logger.LogWarning(ex,
                "ManifestRefreshService: refresh failed (trigger={Trigger}) — " +
                "existing manifest retained (last refreshed: {LastRefreshed}). " +
                "Will retry on next tick.",
                trigger, _manifest.LastRefreshedUtc);
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the OTEL meter and the base BackgroundService resources.
    /// </summary>
    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
