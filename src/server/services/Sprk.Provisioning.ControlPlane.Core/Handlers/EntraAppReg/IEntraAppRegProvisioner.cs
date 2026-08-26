// -----------------------------------------------------------------------------
// IEntraAppRegProvisioner.cs
//
// L2 abstraction over Entra app-registration provisioning for handler H3.
//
// TASK 130 (Wave G-3) REWRITE: replaces the Wave-C4 shell-out design
// (<c>RegisterEntraAppRegScriptProvisioner</c>, RETIRED — see that file's
// retirement banner) with a pure Microsoft.Graph 6.x SDK port
// (<see cref="GraphAppRegistrationProvisioner"/>), per design.md §4.1's H3
// SDK-surface table + Option D's zero-shell-out invariant (spec.md MUST rule
// post-line-254 block). The interface now models BOTH tenancy-model branches
// (spec.md FR-39 + design.md §4.1 H3 row v3.5 split):
//   - <see cref="ProvisionAsync"/>   — Model 2 ONLY. Ensures/reconciles a
//     PER-CUSTOMER app-reg + service principal + client secret + FIC trusting
//     the shared BFF UAMI (auth-v4 §3.1 recipe).
//   - <see cref="VerifySharedAsync"/> — Model 1 ONLY. Read-only grant-currency
//     check against the PRE-EXISTING shared multitenant app-reg. Creates
//     NOTHING (I6-adjacent invariant: Model 1 MUST NOT create a new app-reg
//     or FIC object).
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations from day 1 — production
// GraphAppRegistrationProvisioner (real Graph SDK calls under a fake-transport
// test double per ADR-038) + per-unit-test stubs that construct outcomes
// directly (H3EntraAppRegHandlerTests).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Executes Entra app-registration provisioning (Model 2) and shared-app-reg
/// verification (Model 1) for handler H3. Production impl
/// (<see cref="GraphAppRegistrationProvisioner"/>) uses Microsoft.Graph 6.x;
/// test impls return canned outcomes.
/// </summary>
public interface IEntraAppRegProvisioner
{
    /// <summary>
    /// MODEL 2 ONLY. Ensures/reconciles the per-customer BFF app-reg + service
    /// principal + client secret + FIC (trusting the shared BFF UAMI). Returns
    /// a typed outcome — success carries the outputs consumed by downstream
    /// handlers; failure carries a diagnostic. Domain failures do NOT throw
    /// (parity with <see cref="Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy.IBicepDeployRunner"/>);
    /// infra faults MAY throw.
    /// </summary>
    /// <param name="request">Provisioning inputs (customerId, tenantId, KV vault name, UAMI principalId, profile).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// MODEL 1 ONLY. Read-only verification that the pre-existing shared
    /// multitenant app-reg's configuration (signInAudience, requiredResourceAccess,
    /// exposed scope) is current. Creates NOTHING — no app-reg, no service
    /// principal, no FIC. Domain outcomes do NOT throw; infra faults MAY throw.
    /// </summary>
    /// <param name="request">The shared app-reg's known appId.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EntraAppRegSharedVerifyOutcome> VerifySharedAsync(
        EntraAppRegSharedVerifyRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// MODEL 2 ONLY. Commits the KV secret writes <see cref="ProvisionAsync"/>
    /// staged as <see cref="EntraAppRegOutputs.PendingKvWrites"/>. The CALLER
    /// (<see cref="H3EntraAppRegHandler"/>) invokes this ONLY after
    /// <see cref="IAdminConsentVerifier"/> returns
    /// <see cref="AdminConsentVerificationResult.Verified"/> — DS-4 §3's
    /// BINDING recipe ordering ("writing KV secrets before consent
    /// verification would leak a functional secret before the consent gate
    /// has genuinely passed"). Returns null on success, or a diagnostic
    /// string on failure.
    /// </summary>
    Task<string?> CommitPendingSecretsAsync(
        IReadOnlyList<PendingKvSecretWrite> pendingWrites, CancellationToken cancellationToken);
}

/// <summary>
/// One deferred KV secret write staged by <see cref="IEntraAppRegProvisioner.ProvisionAsync"/>
/// and committed by <see cref="IEntraAppRegProvisioner.CommitPendingSecretsAsync"/>
/// ONLY after admin consent is verified (DS-4 §3 binding ordering).
/// <see cref="Value"/> is CLEARTEXT for <see cref="GraphAppRegistrationProvisioner.ClientSecretName"/>
/// entries — per ADR-028, this record MUST NEVER be logged or persisted to
/// Cosmos; it exists ONLY in memory for the duration of a single
/// <see cref="H3EntraAppRegHandler.HandleAsync"/> invocation, threaded
/// straight from the provisioner back to the provisioner.
/// </summary>
public sealed record PendingKvSecretWrite(string VaultName, string SecretName, string Value);

/// <summary>
/// Inputs to a single Model 2 Entra app-registration provisioning invocation.
/// Immutable record; the caller (<see cref="H3EntraAppRegHandler"/>) constructs
/// one per run from <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — MUST be explicit, never default).</param>
/// <param name="VaultName">Target Key Vault name (e.g. <c>sprk-acme-prod-kv</c>). Client secret + ClientId + Audience are all written here under their canonical §7.9 names.</param>
/// <param name="UamiPrincipalId">
/// The shared BFF UAMI's <c>principalId</c> (object id — NOT <c>clientId</c>,
/// per auth-v4 §3.1's documented most-common misconfiguration trap). This is
/// the FIC's <c>subject</c>. Sourced from <c>InterStepState.MiObjectId</c>
/// (H2a output).
/// </param>
/// <param name="Profile">
/// The run's environment profile (<c>spaarke-hosted-model2</c> or
/// <c>customer-owned-model2</c>) — determines the FIC <c>issuer</c> tenant per
/// auth-v4 §3.1 (Spaarke-hosted: Spaarke's own tenant; customer-owned: this
/// request's <see cref="TenantId"/>).
/// </param>
public sealed record EntraAppRegRequest(
    string CustomerId,
    string TenantId,
    string VaultName,
    string UamiPrincipalId,
    string Profile);

/// <summary>Inputs to a Model 1 shared-app-reg verification invocation.</summary>
/// <param name="SharedAppId">The shared multitenant BFF app-reg's Entra <c>appId</c> (from <see cref="EntraAppRegOptions.SharedBffAppRegistrationId"/>).</param>
public sealed record EntraAppRegSharedVerifyRequest(string SharedAppId);

/// <summary>Result of <see cref="IEntraAppRegProvisioner.VerifySharedAsync"/>. Exhaustive: <see cref="Current"/> | <see cref="Drifted"/> | <see cref="Failure"/>.</summary>
public abstract record EntraAppRegSharedVerifyOutcome
{
    private EntraAppRegSharedVerifyOutcome() { }

    /// <summary>Shared app-reg's configuration matches expected (signInAudience + requiredResourceAccess + exposed scope all current).</summary>
    public sealed record Current : EntraAppRegSharedVerifyOutcome;

    /// <summary>Shared app-reg exists but has drifted from expected configuration — operator must reconcile (out of scope for a per-customer handler to auto-fix a shared resource).</summary>
    public sealed record Drifted(string Diagnostic) : EntraAppRegSharedVerifyOutcome;

    /// <summary>Verification itself failed (Graph unreachable, shared app not found at all).</summary>
    public sealed record Failure(string Diagnostic) : EntraAppRegSharedVerifyOutcome;
}

/// <summary>
/// Deploy outputs H3 needs to (a) populate <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.BffAppRegId"/>
/// and (b) hand the KV secret URI reference (never the cleartext secret) to
/// downstream H4. All properties are REQUIRED — a null/blank value on any
/// field returns <see cref="EntraAppRegRejectionCodes.ProvisioningOutputsIncomplete"/>.
///
/// STRUCTURAL NOTE (S2S drop per r3 task 060):
///   There is intentionally NO <c>S2sAppRegId</c> property here. The Dataverse
///   S2S app-registration was retired 2026-01-07 (BFF app-reg is the single
///   Dataverse Application User); the type shape makes reintroducing S2S a
///   compile-time modification, catching the anti-pattern statically.
/// </summary>
public sealed class EntraAppRegOutputs
{
    /// <summary>
    /// Entra app registration <c>appId</c> (client id) for the BFF API app.
    /// Written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.BffAppRegId"/>.
    /// </summary>
    public required string BffAppRegId { get; init; }

    /// <summary>
    /// KV URI reference (<c>@Microsoft.KeyVault(SecretUri=...)</c>) for the
    /// BFF API client secret written by the script to <c>BFF-API-ClientSecret</c>.
    /// H4 consumes this to populate the App Service configuration.
    ///
    /// NEVER the cleartext secret — this is a KV URI reference literal only.
    /// The provisioner enforces the pattern in output construction.
    /// </summary>
    public required string BffClientSecretKvUri { get; init; }

    /// <summary>
    /// Deferred KV writes (task 130, DS-4 §3 binding ordering) — see
    /// <see cref="PendingKvSecretWrite"/> + <see cref="IEntraAppRegProvisioner.CommitPendingSecretsAsync"/>.
    /// Empty for Model 1 (no writes — Model 1 only REFERENCES pre-existing
    /// shared-vault entries, nothing to commit).
    /// </summary>
    public IReadOnlyList<PendingKvSecretWrite> PendingKvWrites { get; init; } = Array.Empty<PendingKvSecretWrite>();
}

/// <summary>
/// Discriminated result of <see cref="IEntraAppRegProvisioner.ProvisionAsync"/>.
/// Success carries the provisioning outputs; Failure carries a runner-side
/// diagnostic (later mapped to <see cref="EntraAppRegRejectionCodes.ProvisioningFailed"/>
/// by the handler).
/// </summary>
public abstract record EntraAppRegOutcome
{
    private EntraAppRegOutcome() { }

    /// <summary>Provisioning succeeded — outputs carry the BFF appId + KV secret URI reference.</summary>
    public sealed record Success(EntraAppRegOutputs Outputs) : EntraAppRegOutcome;

    /// <summary>
    /// Provisioning failed. <paramref name="Diagnostic"/> is the operator-facing
    /// message (e.g. "Register-EntraAppRegistrations.ps1 exit 1: Graph API 403").
    /// Handler wraps this in a <see cref="Handlers.FailureClass.Resumable"/>
    /// §4C classification — Entra app-reg failures are Resumable (operator
    /// resolves consent / permission / connectivity issue then POSTs
    /// /api/runs/{id}/resume).
    /// </summary>
    public sealed record Failure(string Diagnostic) : EntraAppRegOutcome;
}
