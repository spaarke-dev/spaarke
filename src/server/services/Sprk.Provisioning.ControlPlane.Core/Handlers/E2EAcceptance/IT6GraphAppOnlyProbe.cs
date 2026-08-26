// -----------------------------------------------------------------------------
// IT6GraphAppOnlyProbe.cs
//
// Per-probe seam abstracting the Graph half of T6SpeConfidentialClientTrapProbe
// (task 175, Wave G-7 pipelined with H8). See T6SpeConfidentialClientTrapProbe.cs's
// file header "WHY THE GRAPH GET IS NOT MOCKED IN CI" + "TEST SEAM" sections
// for the rationale: parity with H8's GraphContainerTypeProvisioner /
// GraphAppOnlyContainerVerifier posture (real Graph HTTP calls are not
// unit-tested in the CI suite; the seam factoring keeps the T6 probe's
// orchestration + outcome-mapping unit-testable end-to-end via fake seam
// impls returning canned outcomes).
//
// PRODUCTION IMPL (see GraphContainerTypesListAppOnlyProbe.cs) issues:
//   GET /storage/fileStorage/containerTypes
// under a per-request ClientCertificateCredential constructed via
// SpeConfidentialClientGraphFactory.BuildGraphClient. A successful 2xx
// response (empty list or otherwise) proves the confidential-client
// (cert-based) auth model is genuinely IN EFFECT (T6 satisfied) -- H13's
// R7 "assert EFFECTS not intentions" principle. A response carrying the
// delegated-token trap signature proves the OPPOSITE (T6 manifested), a 404
// proves the container-type is still in the 24h SPE replication window
// (verdict deferred), any other error is InfraFault.
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Graph half of the T6 confidential-client trap probe -- see file header.
/// </summary>
public interface IT6GraphAppOnlyProbe
{
    /// <summary>
    /// Issues an app-only Graph GET against the SPE containerTypes endpoint
    /// under a <see cref="Azure.Identity.ClientCertificateCredential"/>
    /// constructed from <paramref name="tenantId"/>,
    /// <paramref name="clientAppId"/>, and <paramref name="cert"/>. Never
    /// throws for expected fault modes -- returns a discriminated result.
    /// </summary>
    Task<T6GraphAppOnlyProbeResult> ProbeAsync(
        string tenantId, string clientAppId, X509Certificate2 cert,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of a T6 Graph app-only probe. Discriminated union: Succeeded /
/// DelegatedTokenTrapDetected / ReplicationPending / InfraFault.
/// </summary>
public abstract record T6GraphAppOnlyProbeResult
{
    private T6GraphAppOnlyProbeResult() { }

    /// <summary>Graph GET returned 2xx under the ClientCertificateCredential -- T6 satisfied.</summary>
    public sealed record SucceededResult : T6GraphAppOnlyProbeResult;

    /// <summary>Graph returned an error whose body contains the delegated-token trap phrase -- T6 MANIFESTED.</summary>
    public sealed record DelegatedTokenTrapDetectedResult(int StatusCode, string Diagnostic) : T6GraphAppOnlyProbeResult;

    /// <summary>Graph returned 404 -- consistent with the up-to-24h SPE container-type replication window (verdict deferred).</summary>
    public sealed record ReplicationPendingResult(string Diagnostic) : T6GraphAppOnlyProbeResult;

    /// <summary>Graph could not be reached / rejected the request for a reason UNRELATED to the T6 trap -- verdict deferred.</summary>
    public sealed record InfraFaultResult(string Diagnostic) : T6GraphAppOnlyProbeResult;
}

/// <summary>Convenience factory helpers keeping call sites terse and grep-able.</summary>
public static class T6GraphAppOnlyProbeResults
{
    /// <summary>Succeeded singleton -- reused across all Passed responses.</summary>
    public static readonly T6GraphAppOnlyProbeResult.SucceededResult Succeeded = new();
}
