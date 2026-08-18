// -----------------------------------------------------------------------------
// ISpeContainerVerifier.cs
//
// L2 abstraction over the H8 post-condition check: a FRESH app-only (confidential
// -client, cert-based) Graph token can GET the just-created root container. This
// is distinct from container CREATION succeeding — it proves the container is
// actually readable via the app-only identity path the BFF will use at runtime
// (§4D I4/I5), catching any subtlety where a delegated token slipped into the
// create call, or SPE-side propagation lags the create response.
//
// SEAM JUSTIFICATION (ADR-010):
//   >= 2 implementations exist from day 1: production
//   (SpeContainerAppOnlyVerifier, shells out to the T6-compliant
//   scripts/Get-SpeContainerMetadata-AppOnly.ps1) + test stubs.
//
// WHY A NEW SCRIPT (Get-SpeContainerMetadata-AppOnly.ps1) INSTEAD OF THE
// EXISTING scripts/Get-ContainerMetadata.ps1:
//   Get-ContainerMetadata.ps1 acquires its Graph token via
//   `az account get-access-token` — a DELEGATED token (the identity of
//   whoever ran `az login`). Using it here would defeat the entire purpose
//   of the T6 post-condition check (proving APP-ONLY access) and could even
//   mask a real T6 regression by succeeding via the operator's own delegated
//   session. A new, narrowly-scoped, cert-based script is required; modifying
//   the existing delegated script is out of scope (other operators may depend
//   on its current delegated behavior for ad-hoc troubleshooting).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Verifies a just-created SPE container is readable via a FRESH app-only
/// (confidential-client, cert-based) Graph token. Production impl shells out
/// to <c>scripts/Get-SpeContainerMetadata-AppOnly.ps1</c>.
/// </summary>
public interface ISpeContainerVerifier
{
    /// <summary>
    /// Performs the app-only GET verification. Domain failures (including a
    /// detected T6 delegated-token trap) do NOT throw — infra faults MAY throw.
    /// </summary>
    Task<SpeContainerVerificationResult> VerifyAsync(
        SpeContainerVerificationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Inputs to a single app-only container GET verification.</summary>
/// <param name="ContainerId">SPE container id to verify (H8's just-created root container).</param>
/// <param name="OwningAppId">BFF API app registration id — the confidential-client identity performing the GET.</param>
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
    /// GET did not verify. <paramref name="IsDelegatedTokenTrap"/> true when the
    /// script's output indicates a delegated-token 403 signature — mapped by the
    /// handler to a distinct T6 rejection code.
    /// </summary>
    public sealed record NotVerified(string Diagnostic, bool IsDelegatedTokenTrap) : SpeContainerVerificationResult;
}
