// -----------------------------------------------------------------------------
// IDataverseHealthProbe.cs
//
// L2 abstraction over the Dataverse Web API check H5 performs on the environment the operator created (T228: H5
// adopts, never creates). Production impl
// (<see cref="DataverseWebApiHealthProbe"/>) issues an HTTP GET
// <c>{envUrl}/api/data/v9.2/WhoAmI</c> with a DefaultAzureCredential bearer
// token; unit tests inject stubs to avoid HTTP + auth.
//
// WHAT IT PROVES: the environment answers its Web API AND the L2 Worker identity may use it — 401/403 is its own
// result (AccessDenied), because the fix is an operator step (add the Worker as an application user), not a wait.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 impls (production HTTP + test stubs).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Verifies the Dataverse environment H5 adopts answers <c>GET /WhoAmI</c> for the L2 Worker identity.
/// </summary>
public interface IDataverseHealthProbe
{
    /// <summary>
    /// Probes Web API health at <paramref name="environmentUrl"/> by issuing
    /// <c>GET /api/data/v9.2/WhoAmI</c>. Returns typed
    /// <see cref="DataverseHealthProbeResult"/> — Reachable on 200, Unreachable
    /// on any non-200 or connection failure, InProgress when Dataverse
    /// reports the env is still replicating (retryable). Domain failures do
    /// NOT throw; infrastructure faults (e.g. auth chain broken) MAY throw.
    /// </summary>
    /// <param name="environmentUrl">The environment's canonical URL (intake <c>dataverseEnvUrl</c>).</param>
    /// <param name="tenantId">Entra tenant id for token acquisition scope (§4D I1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DataverseHealthProbeResult> CheckHealthAsync(
        string environmentUrl,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="IDataverseHealthProbe.CheckHealthAsync"/>.
/// Exhaustive: <see cref="Reachable"/> | <see cref="InProgress"/> | <see cref="AccessDenied"/> |
/// <see cref="Unreachable"/>.
/// </summary>
public abstract record DataverseHealthProbeResult
{
    private DataverseHealthProbeResult() { }

    /// <summary>WhoAmI returned 200 with a valid response body. Env is usable.</summary>
    public sealed record Reachable() : DataverseHealthProbeResult;

    /// <summary>
    /// WhoAmI returned a signal that env is still replicating (e.g. 503 with
    /// specific replication header, or 404 with the "environment is being
    /// prepared" body). Handler polls again after the configured interval.
    /// </summary>
    public sealed record InProgress(string Diagnostic) : DataverseHealthProbeResult;

    /// <summary>
    /// WhoAmI returned 401 or 403: the environment answers, but the calling identity is not an application user of it.
    /// Handler maps to <see cref="DataverseEnvAdoptionRejectionCodes.WorkerNotAppUser"/> (T228).
    /// </summary>
    public sealed record AccessDenied(string Diagnostic) : DataverseHealthProbeResult;

    /// <summary>
    /// WhoAmI returned another terminal failure (500-class or connection error). Handler maps to
    /// <see cref="DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed"/>.
    /// </summary>
    public sealed record Unreachable(string Diagnostic) : DataverseHealthProbeResult;
}
