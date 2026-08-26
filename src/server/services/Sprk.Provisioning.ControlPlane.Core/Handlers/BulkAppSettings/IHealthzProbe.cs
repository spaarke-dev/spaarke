// -----------------------------------------------------------------------------
// IHealthzProbe.cs
//
// Task 201 — seam over the HTTP /healthz backoff-poll H4b performs after the
// generated Configure-AppServiceSettings.generated.ps1 script writes app
// settings + Azure triggers ONE App Service restart cycle.
//
// CONTRACT:
//   ProbeWithBackoffAsync polls the target URL with an internal backoff
//   schedule (30s → 60s → 90s → 120s → 180s → ~8-min total budget) until it
//   observes HTTP 200 OR the budget is exhausted. Returns a discriminated
//   HealthzResult. Never throws on domain outcomes (non-200 / timeout);
//   throws only on OperationCanceledException + true infra faults.
//
// H4b maps Timeout → Failure(QuarantineRequired, "h4b-healthz-timeout", ...)
// and enriches the diagnostic by fetching + parsing container docker logs
// via IContainerLogFetcher.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Polls an HTTP /healthz endpoint with backoff until success OR budget
/// exhausted. Fake-substitutable so H4b unit tests can force Success /
/// Timeout without running the real backoff schedule.
/// </summary>
public interface IHealthzProbe
{
    /// <summary>
    /// Polls <paramref name="healthzUrl"/> until it returns HTTP 200 OR the
    /// internal backoff budget is exhausted. Backoff schedule:
    /// 30s / 60s / 90s / 120s / 180s (5 probes, ~8-min total).
    /// </summary>
    /// <param name="healthzUrl">The healthz URL (e.g. https://spaarke-bff-demo.azurewebsites.net/healthz).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="HealthzResult.Success"/> on first HTTP 200 observed;
    /// <see cref="HealthzResult.Timeout"/> when budget is exhausted without
    /// a successful probe.
    /// </returns>
    Task<HealthzResult> ProbeWithBackoffAsync(Uri healthzUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="IHealthzProbe.ProbeWithBackoffAsync"/>.
/// </summary>
public abstract record HealthzResult
{
    private HealthzResult() { }

    /// <summary>Probe observed HTTP 200 within the backoff budget.</summary>
    /// <param name="LastStatusCode">The successful status code (typically 200).</param>
    /// <param name="Elapsed">Wall-clock time from first probe attempt to success.</param>
    public sealed record Success(int LastStatusCode, TimeSpan Elapsed) : HealthzResult;

    /// <summary>Backoff budget exhausted before any probe returned HTTP 200.</summary>
    /// <param name="LastErrorSummary">Short summary of the final observed state (last status code + last exception message OR "no response").</param>
    /// <param name="AttemptsMade">Number of probe attempts before giving up.</param>
    public sealed record Timeout(string LastErrorSummary, int AttemptsMade) : HealthzResult;
}
