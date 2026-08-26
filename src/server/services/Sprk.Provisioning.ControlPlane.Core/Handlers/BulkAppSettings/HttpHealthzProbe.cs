// -----------------------------------------------------------------------------
// HttpHealthzProbe.cs
//
// Task 201 — production impl of IHealthzProbe using HttpClient. Backoff
// schedule 30s / 60s / 90s / 120s / 180s per POML — 5 probes, ~8-min total.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <inheritdoc cref="IHealthzProbe"/>
public sealed class HttpHealthzProbe : IHealthzProbe
{
    /// <summary>
    /// Backoff schedule (delay BEFORE each probe attempt). 5 probes, 30 + 60 +
    /// 90 + 120 + 180 = 480 s ≈ 8-min total budget. Public/internal so the
    /// test suite can pin it and future task can extend without churning the
    /// interface.
    /// </summary>
    internal static readonly TimeSpan[] DefaultBackoffSchedule =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(180),
    ];

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHealthzProbe> _logger;
    private readonly TimeSpan[] _backoffSchedule;

    /// <summary>Constructs the probe. Uses the default backoff schedule (5 probes, ~8-min budget).</summary>
    public HttpHealthzProbe(HttpClient httpClient, ILogger<HttpHealthzProbe> logger)
        : this(httpClient, logger, DefaultBackoffSchedule)
    { }

    /// <summary>
    /// Test-only overload: supplies a custom backoff schedule (e.g. zero-delay
    /// for fast tests). Internal so it is not part of the public surface.
    /// </summary>
    internal HttpHealthzProbe(HttpClient httpClient, ILogger<HttpHealthzProbe> logger, TimeSpan[] backoffSchedule)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(backoffSchedule);
        _httpClient = httpClient;
        _logger = logger;
        _backoffSchedule = backoffSchedule;
    }

    /// <inheritdoc/>
    public async Task<HealthzResult> ProbeWithBackoffAsync(Uri healthzUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(healthzUrl);

        var stopwatch = Stopwatch.StartNew();
        var lastError = "no response";
        int lastStatusCode = 0;
        var attempt = 0;

        foreach (var delay in _backoffSchedule)
        {
            attempt++;
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            try
            {
                using var response = await _httpClient.GetAsync(healthzUrl, cancellationToken).ConfigureAwait(false);
                lastStatusCode = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "H4b HttpHealthzProbe: SUCCESS on attempt {Attempt}/{Total} — status {Status} in {ElapsedMs}ms",
                        attempt, _backoffSchedule.Length, lastStatusCode, stopwatch.ElapsedMilliseconds);
                    return new HealthzResult.Success(lastStatusCode, stopwatch.Elapsed);
                }
                lastError = $"HTTP {lastStatusCode}";
                _logger.LogDebug(
                    "H4b HttpHealthzProbe: attempt {Attempt}/{Total} returned {Status} — will retry",
                    attempt, _backoffSchedule.Length, lastStatusCode);
            }
            catch (HttpRequestException ex)
            {
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                _logger.LogDebug(
                    "H4b HttpHealthzProbe: attempt {Attempt}/{Total} threw {Kind}: {Message} — will retry",
                    attempt, _backoffSchedule.Length, ex.GetType().Name, ex.Message);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient's per-request timeout — not our cancellation.
                lastError = $"HTTP timeout: {ex.Message}";
                _logger.LogDebug(
                    "H4b HttpHealthzProbe: attempt {Attempt}/{Total} HTTP timeout — will retry",
                    attempt, _backoffSchedule.Length);
            }
        }

        stopwatch.Stop();
        var summary = $"last observed: {lastError} (elapsed {stopwatch.Elapsed.TotalSeconds:F0}s across {attempt} attempts)";
        _logger.LogWarning(
            "H4b HttpHealthzProbe: TIMEOUT — {Summary}",
            summary);
        return new HealthzResult.Timeout(summary, attempt);
    }
}
