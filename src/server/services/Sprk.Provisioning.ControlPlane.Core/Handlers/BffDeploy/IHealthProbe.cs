// -----------------------------------------------------------------------------
// IHealthProbe.cs
//
// L2 abstraction over the post-swap production /health smoke probe. The
// production impl (<see cref="HttpHealthProbe"/>) issues an HTTP GET against
// the BFF's /healthz endpoint (defaults to /healthz per Deploy-BffApi.ps1)
// with retry-until-timeout semantics. Unit tests inject stubs to avoid HTTP.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="HttpHealthProbe"/> — HttpClient GET with retry
//       parity to Deploy-BffApi.ps1's Test-HealthCheck helper (24 × 5s).
//     - Test: stubs injected per unit test that construct
//       <see cref="HealthProbeResult"/> directly (see H9BffDeployHandlerTests).
//   Interface earns its keep — no NIH.
//
// NFR-05 RATIONALE:
//   spec.md NFR-05: "Post-deploy: BFF /health returns 200 resolving all
//   Tier-1 IOptions KV refs (r3 task 061 ValidateOnStart)". The BFF's own
//   startup pipeline throws at boot on any Tier-1 KV-ref resolution failure
//   (r3 task 061 hardening) — the /healthz endpoint therefore CANNOT return
//   200 unless every Tier-1 KV ref has resolved. A single HTTP GET is
//   sufficient proof of the whole invariant; H9 does not need to enumerate
//   individual KV refs (that would duplicate ValidateOnStart's work).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Probes an HTTP /health endpoint until it returns 200 or the retry budget
/// exhausts. Production impl uses HttpClient; test impls return canned
/// <see cref="HealthProbeResult"/>s.
/// </summary>
public interface IHealthProbe
{
    /// <summary>
    /// Polls the target URL until a successful (200 OK) response is received
    /// or the configured retry budget is exhausted. Domain failures (non-200
    /// responses, timeouts) do NOT throw. Infrastructure faults (misconfigured
    /// URL, DNS resolution failure) MAY throw.
    /// </summary>
    /// <param name="request">Probe inputs (target URL, retry count, interval, per-request timeout).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<HealthProbeResult> ProbeAsync(HealthProbeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single health-probe pass.
/// </summary>
/// <param name="TargetUrl">Full HTTPS URL to GET (e.g. <c>https://spaarke-bff-acme.azurewebsites.net/healthz</c>).</param>
/// <param name="MaxRetries">Maximum number of GET attempts before returning Failure.</param>
/// <param name="Interval">Delay between retries.</param>
/// <param name="RequestTimeout">Per-request HTTP timeout.</param>
public sealed record HealthProbeRequest(
    string TargetUrl,
    int MaxRetries,
    TimeSpan Interval,
    TimeSpan RequestTimeout);

/// <summary>
/// Discriminated result of <see cref="IHealthProbe.ProbeAsync"/>. Success =
/// the endpoint returned 200 (potentially after retries); Failure = the
/// endpoint never returned 200 within the retry budget.
/// </summary>
public abstract record HealthProbeResult
{
    private HealthProbeResult() { }

    /// <summary>Endpoint returned 200 OK.</summary>
    /// <param name="AttemptsUsed">Number of GET attempts required (1 = first try).</param>
    /// <param name="Duration">Wall-clock time from first attempt to successful response.</param>
    public sealed record Success(int AttemptsUsed, TimeSpan Duration) : HealthProbeResult;

    /// <summary>Endpoint never returned 200 within the retry budget.</summary>
    /// <param name="AttemptsUsed">Number of GET attempts made (equals <see cref="HealthProbeRequest.MaxRetries"/>).</param>
    /// <param name="Diagnostic">Operator-facing message describing the last-observed failure mode.</param>
    public sealed record Failure(int AttemptsUsed, string Diagnostic) : HealthProbeResult;
}
