// -----------------------------------------------------------------------------
// IDataverseHealthProbe.cs
//
// L2 abstraction over the Dataverse Web API health check that H5 performs
// after env creation reports success. Production impl
// (<see cref="DataverseWebApiHealthProbe"/>) issues an HTTP GET
// <c>{envUrl}/api/data/v9.2/WhoAmI</c> with a DefaultAzureCredential bearer
// token; unit tests inject stubs to avoid HTTP + auth.
//
// WHY A SEPARATE PROBE (not folded into IDataverseEnvCreator):
//   - The pac CLI's own success signal is that the env row exists in the
//     tenant — it does NOT verify Web API reachability from the L2 App
//     Service's network posture. Post-create env may still be replicating +
//     not yet WhoAmI-queryable.
//   - Splitting the seams matches the H2a pattern (IBicepDeployRunner ≠
//     IArmKeyVaultRefProbe). Each seam owns exactly one concern.
//   - Failure classification is different: env-creation timeout vs
//     health-check failure are distinct operator diagnoses.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 impls (production HTTP + test stubs).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Verifies Web API reachability of a freshly-created Dataverse environment.
/// H5 calls this AFTER <see cref="IDataverseEnvCreator"/> reports success to
/// gate the run's advance on the env actually being usable (POML acceptance
/// criterion #3 — GET /WhoAmI returns 200).
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
    /// <param name="environmentUrl">Base URL from <see cref="DataverseEnvCreationOutcome.Success.EnvironmentUrl"/>.</param>
    /// <param name="tenantId">Entra tenant id for token acquisition scope (§4D I1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DataverseHealthProbeResult> CheckHealthAsync(
        string environmentUrl,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="IDataverseHealthProbe.CheckHealthAsync"/>.
/// Exhaustive: <see cref="Reachable"/> | <see cref="InProgress"/> |
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
    /// WhoAmI returned a terminal failure (401/403/500-class or connection
    /// error). Handler maps to
    /// <see cref="DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed"/>.
    /// </summary>
    public sealed record Unreachable(string Diagnostic) : DataverseHealthProbeResult;
}
