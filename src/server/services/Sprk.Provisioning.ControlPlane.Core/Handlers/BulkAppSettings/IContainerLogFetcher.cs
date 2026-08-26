// -----------------------------------------------------------------------------
// IContainerLogFetcher.cs
//
// Task 201 — seam over the Kudu SCM `/api/logs/docker` retrieval H4b performs
// when /healthz never turns green. On timeout, H4b fetches the container's
// docker logs, parses for `Unhandled exception. System.InvalidOperationException: X`
// to extract the failing IOptions module name, and returns
// Failure(QuarantineRequired, "h4b-healthz-timeout", "BFF fail-fast on {module}: ...")
// so the operator gets an actionable diagnostic in seconds instead of the
// previous 15-30 min manual triage cycle.
//
// TryParseFailFastModule (internal, exposed for test theory coverage) is the
// pure-function log parser — regex over the exception line.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Fetches an App Service container's docker logs via the Kudu SCM API.
/// Fake-substitutable so H4b unit tests can inject canned log samples.
/// </summary>
public interface IContainerLogFetcher
{
    /// <summary>
    /// Fetches the container docker log for the given App Service. Returns
    /// the raw log text (may be empty when the container hasn't produced
    /// output yet). Throws on infrastructure fault (401/403/5xx from Kudu,
    /// network error, cancellation).
    /// </summary>
    /// <param name="appServiceName">App Service name (e.g. "spaarke-bff-demo").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw docker log text (best-effort — may be truncated).</returns>
    Task<string> FetchDockerLogsAsync(string appServiceName, CancellationToken cancellationToken);
}
