// -----------------------------------------------------------------------------
// ISpeContainerVerifier.cs
//
// L2 abstraction over the H8 post-condition check: a FRESH app-only
// (confidential-client, cert-based) Graph token can GET the just-created +
// activated container. This is distinct from container CREATION + ACTIVATION
// succeeding — it proves the container is actually readable via the app-only
// identity path the BFF will use at runtime (§4D I4/I5).
//
// H8-B (task 214) NOTE: identical contract to the H8-A pre-rewrite version —
// the verifier's job is unchanged (verify a container is readable via app-only)
// even though H8's overall scope changed from container-TYPE creation to
// container CREATION. Namespace moved from SpeContainerType to SpeContainer.
//
// SEAM JUSTIFICATION (ADR-010):
//   >= 2 implementations exist from day 1: production
//   (GraphAppOnlyContainerVerifier — Graph SDK under ClientCertificateCredential)
//   + test stubs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Verifies a just-created SPE container is readable via a FRESH app-only
/// (confidential-client, cert-based) Graph token. Production impl calls Graph
/// SDK under ClientCertificateCredential.
/// </summary>
public interface ISpeContainerVerifier
{
    /// <summary>
    /// Performs the app-only GET verification. Domain outcomes do NOT throw;
    /// infra faults MAY throw.
    /// </summary>
    Task<SpeContainerVerificationResult> VerifyAsync(
        SpeContainerVerificationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Inputs to a single app-only container GET verification.</summary>
/// <param name="ContainerId">SPE container id to verify (H8's just-created container).</param>
/// <param name="OwningAppId">Container-type owning app-reg id — the confidential-client identity performing the GET.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5 — explicit, no default).</param>
/// <param name="VaultName">Customer Key Vault name holding the SPE owner cert.</param>
/// <param name="CertSecretName">KV secret name holding the base64 PFX SPE owner cert.</param>
public sealed record SpeContainerVerificationRequest(
    string ContainerId,
    string OwningAppId,
    string TenantId,
    string VaultName,
    string CertSecretName);

/// <summary>Discriminated result of <see cref="ISpeContainerVerifier.VerifyAsync"/>.</summary>
public abstract record SpeContainerVerificationResult
{
    private SpeContainerVerificationResult() { }

    /// <summary>GET succeeded (HTTP 200) via app-only token. <paramref name="Status"/> is the container's reported status (e.g. <c>active</c>).</summary>
    public sealed record Verified(string Status) : SpeContainerVerificationResult;

    /// <summary>
    /// GET did not verify (non-transient — 403, 400, unexpected Graph error).
    /// Handler maps to QuarantineRequired (container exists but is
    /// unverifiable). Note: H8-B does NOT participate in T6-trap detection
    /// (per task 214.4 Option A — H13's T6SpeConfidentialClientTrapProbe owns
    /// the T6 acceptance gate); a delegated-token error here is just a plain
    /// NotVerified with the raw error text propagated in the diagnostic.
    /// </summary>
    public sealed record NotVerified(string Diagnostic) : SpeContainerVerificationResult;

    /// <summary>
    /// The app-only GET returned 404 Not Found for a container H8 JUST
    /// created + activated — the documented signature of SPE's up-to-24h
    /// container-type replication window (design.md §4.1 H8 row + DS-4 §2:
    /// "the 24h SPE replication gate is a RUN-LEVEL external blocker, not a
    /// handler defect"). Distinct from <see cref="NotVerified"/> (a genuine,
    /// non-transient failure) — the handler maps THIS case to
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.RunStatus.WaitingOnGate"/>
    /// (a session-free run-level pause), never Resumable/QuarantineRequired.
    /// </summary>
    public sealed record ReplicationPending(string Diagnostic) : SpeContainerVerificationResult;
}
