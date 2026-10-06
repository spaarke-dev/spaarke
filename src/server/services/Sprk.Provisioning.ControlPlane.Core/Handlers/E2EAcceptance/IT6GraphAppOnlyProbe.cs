// -----------------------------------------------------------------------------
// IT6GraphAppOnlyProbe.cs
//
// Per-probe seam abstracting the Graph half of T6SpeConfidentialClientTrapProbe
// (task 175, Wave G-7 pipelined with H8). See T6SpeConfidentialClientTrapProbe.cs's
// file header for the rationale: real Graph HTTP calls are not unit-tested in the
// CI suite; the seam keeps the T6 probe's orchestration + outcome mapping
// unit-testable end-to-end via fake seam impls returning canned outcomes.
//
// PRODUCTION IMPL (GraphContainersListAppOnlyProbe.cs — task 248) issues, app-only
// as the container type's OWNING app (Worker UAMI federated credential):
//   GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}
// and reports whether the run's container (H8 output) is in the result. A 2xx that
// lists it proves the app-only owning-app auth model is IN EFFECT for this stamp's
// container (H13's R7 "assert EFFECTS not intentions"). Task 248 replaced the
// app-only GET /storage/fileStorage/containerTypes, which Microsoft documents as
// returning 403 to app-only callers — that probe could never pass.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Graph half of the T6 confidential-client trap probe -- see file header.
/// </summary>
public interface IT6GraphAppOnlyProbe
{
    /// <summary>
    /// Lists the containers of <paramref name="containerTypeId"/> app-only as the owning app
    /// <paramref name="ownerAppId"/> in <paramref name="tenantId"/> and looks for
    /// <paramref name="containerId"/>. Never throws for expected fault modes -- returns a discriminated result.
    /// </summary>
    Task<T6GraphAppOnlyProbeResult> ProbeAsync(
        string tenantId, string ownerAppId, string containerTypeId, string containerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of a T6 Graph app-only probe. Discriminated union: Succeeded / ContainerAbsent /
/// DelegatedTokenTrapDetected / ReplicationPending / InfraFault.
/// </summary>
public abstract record T6GraphAppOnlyProbeResult
{
    private T6GraphAppOnlyProbeResult() { }

    /// <summary>The app-only containers listing succeeded and includes the run's container -- T6 satisfied.</summary>
    public sealed record SucceededResult : T6GraphAppOnlyProbeResult;

    /// <summary>The app-only containers listing succeeded but the run's container is not in it.</summary>
    public sealed record ContainerAbsentResult(int ListedCount, string Diagnostic) : T6GraphAppOnlyProbeResult;

    /// <summary>Graph returned an error whose body contains the delegated-token trap phrase -- T6 MANIFESTED.</summary>
    public sealed record DelegatedTokenTrapDetectedResult(int StatusCode, string Diagnostic) : T6GraphAppOnlyProbeResult;

    /// <summary>Graph returned 404 -- consistent with the up-to-24h SPE container-type replication window (verdict deferred).</summary>
    public sealed record ReplicationPendingResult(string Diagnostic) : T6GraphAppOnlyProbeResult;

    /// <summary>Graph could not be reached / refused the request for a reason UNRELATED to the T6 trap -- verdict deferred.</summary>
    public sealed record InfraFaultResult(string Diagnostic) : T6GraphAppOnlyProbeResult;
}

/// <summary>Convenience factory helpers keeping call sites terse and grep-able.</summary>
public static class T6GraphAppOnlyProbeResults
{
    /// <summary>Succeeded singleton -- reused across all Passed responses.</summary>
    public static readonly T6GraphAppOnlyProbeResult.SucceededResult Succeeded = new();
}
