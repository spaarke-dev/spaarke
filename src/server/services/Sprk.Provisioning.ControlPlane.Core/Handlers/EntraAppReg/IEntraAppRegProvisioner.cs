// -----------------------------------------------------------------------------
// IEntraAppRegProvisioner.cs
//
// L2 abstraction over Entra app-registration provisioning for handler H3.
//
// TASK 130 (Wave G-3) REWRITE: replaces the Wave-C4 shell-out design
// (<c>RegisterEntraAppRegScriptProvisioner</c>, RETIRED and since deleted — its
// script was scripts/Register-EntraAppRegistrations.ps1) with a pure Microsoft.Graph 6.x SDK port
// (<see cref="GraphAppRegistrationProvisioner"/>), per design.md §4.1's H3
// SDK-surface table + Option D's zero-shell-out invariant (spec.md MUST rule
// post-line-254 block).
//
// TASK 222 REWRITE (2026-09-29, D-13 per INCOMING-D12-D13-REMEDIATION §5 Item 1):
// The two-branch interface was REDUCED to a single per-customer provisioning
// path — the shared-app-reg branch (<c>VerifySharedAsync</c>,
// <c>EntraAppRegSharedVerifyRequest</c>, <c>EntraAppRegSharedVerifyOutcome</c>)
// was DELETED because H3 now creates ONE app-reg per customer, UNCONDITIONALLY,
// in both models. See <see cref="H3EntraAppRegHandler"/> file header for the
// D-13 mechanism.
//
//   - <see cref="ProvisionAsync"/> — Ensures/reconciles a PER-CUSTOMER app-reg +
//     service principal + client secret + FIC trusting the BFF UAMI (auth-v4
//     §3.1 recipe). Called for BOTH tenancy models post-task-222.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations from day 1 — production
// GraphAppRegistrationProvisioner (real Graph SDK calls under a fake-transport
// test double per ADR-038) + per-unit-test stubs that construct outcomes
// directly (H3EntraAppRegHandlerTests).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Executes per-customer Entra app-registration provisioning for handler H3.
/// Post-task-222 (D-13): called for BOTH tenancy models — H3 provisions ONE
/// app-reg per customer, UNCONDITIONALLY. Production impl
/// (<see cref="GraphAppRegistrationProvisioner"/>) uses Microsoft.Graph 6.x;
/// test impls return canned outcomes.
/// </summary>
public interface IEntraAppRegProvisioner
{
    /// <summary>
    /// Ensures/reconciles the per-customer BFF app-reg + service
    /// principal + client secret + FIC (trusting the BFF UAMI). Returns
    /// a typed outcome — success carries the outputs consumed by downstream
    /// handlers; failure carries a diagnostic. Domain failures do NOT throw
    /// (parity with <see cref="Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy.IBicepDeployRunner"/>);
    /// infra faults MAY throw.
    /// </summary>
    /// <param name="request">Provisioning inputs (customerId, tenantId, KV vault name, UAMI principalId, profile).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the KV secret writes <see cref="ProvisionAsync"/> staged as
    /// <see cref="EntraAppRegOutputs.PendingKvWrites"/>. The CALLER
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
/// Inputs to a single per-customer Entra app-registration provisioning invocation.
/// Immutable record; the caller (<see cref="H3EntraAppRegHandler"/>) constructs
/// one per run from <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun"/>.
/// Post-task-222 (D-13): fires for BOTH tenancy models — every customer gets
/// their own BFF app-reg in both models.
/// </summary>
/// <param name="CustomerId">Customer partition key (customerId standard: 3-8 lowercase letters/digits, starts with a letter).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — MUST be explicit, never default).</param>
/// <param name="VaultName">Target Key Vault name (e.g. <c>sprk-acme-prod-kv</c>). Client secret + ClientId + Audience are all written here under their canonical §7.9 names.</param>
/// <param name="UamiPrincipalId">
/// The per-customer BFF UAMI's <c>principalId</c> (object id — NOT <c>clientId</c>,
/// per auth-v4 §3.1's documented most-common misconfiguration trap). This is
/// the FIC's <c>subject</c>. Sourced from <c>InterStepState.MiObjectId</c>
/// (H2a output).
/// </param>
/// <param name="Profile">
/// The run's environment profile — determines the FIC <c>issuer</c> tenant per
/// auth-v4 §3.1. The two values POST /api/runs accepts (task 225b, D-12 pairing):
/// (a) <c>spaarke-hosted-model2</c> — Model 1, the Spaarke-hosted dedicated
/// stamp (UAMI lives in Spaarke's subscription; issuer = Spaarke's own tenant,
/// intra-Spaarke-tenant), and (b) <c>customer-owned-model2</c> — Model 2, the
/// customer-hosted dedicated stamp (UAMI lives in the customer's subscription;
/// issuer = this request's <see cref="TenantId"/>). The profile names predate
/// the D-12 renumbering.
/// </param>
/// <param name="RequireSecretFreeIdentity">
/// Bucket B HIGH#3 (customer-provisioning-orchestration-r1 SESSION 18, adversarial
/// e2e verify workflow wepdcb8we) + <c>.claude/constraints/provisioning.md</c>
/// § KV credential lifecycle rule 1: when <c>true</c> (DEFAULT — secure by default),
/// <see cref="GraphAppRegistrationProvisioner.ProvisionAsync"/> MUST NOT mint a new
/// <c>BFF-API-ClientSecret</c> nor stage a <see cref="PendingKvSecretWrite"/> for it —
/// per ADR-028 A4 secret-free identity contract + auth-v4 task 033 (2026-08-24)
/// deletion of both KV copies. Post-task-222 (D-13) EVERY newly-provisioned
/// environment is a per-customer stamp — Model 1 (Spaarke-tenant) + Model 2
/// (Spaarke-hosted or customer-owned) — and ALL are secret-free by construction
/// per constraint rule 1, so H3's per-customer provisioning path
/// passes <c>true</c> unconditionally in both models. The parameter is threaded
/// through the request DTO (not read from an ambient option) so the intent is
/// visible at every call site + any future non-secret-free profile can opt IN
/// explicitly by passing <c>false</c>. A silent default of <c>false</c> is
/// FORBIDDEN — this is the load-bearing safety default; changing it re-opens
/// the exact silent-mint path the verify workflow surfaced.
/// </param>
/// <param name="SpaRedirectUris">
/// T240a: the app registration's exact <c>spa.redirectUris</c> — the customer's own Dataverse origin, where its code
/// pages sign in (<c>redirectUri = window.location.origin</c>). H3 sets exactly this list.
/// </param>
/// <param name="PreAuthorizedClientAppIds">
/// T240a: the shared client apps pre-authorized on the app's <c>user_impersonation</c> scope (H3 sets exactly this list).
/// </param>
public sealed record EntraAppRegRequest(
    string CustomerId,
    string TenantId,
    string VaultName,
    string UamiPrincipalId,
    string Profile,
    bool RequireSecretFreeIdentity = true,
    IReadOnlyList<string>? SpaRedirectUris = null,
    IReadOnlyList<string>? PreAuthorizedClientAppIds = null);

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
    /// Populated for both tenancy models post-task-222 (D-13), since both now
    /// provision a per-customer app-reg with its own client secret.
    /// </summary>
    public IReadOnlyList<PendingKvSecretWrite> PendingKvWrites { get; init; } = Array.Empty<PendingKvSecretWrite>();

    /// <summary>
    /// FIC verification state for this provisioning outcome — the C# exit-code
    /// equivalent of the <c>-FicOnly</c> script contract (task 205b row A42,
    /// SF-8). Defaults to <see cref="FicVerificationState.NotApplicable"/>
    /// (kept as the defensive default). Post-task-222 (D-13), the production
    /// provisioner sets <see cref="FicVerificationState.PendingPostAppServiceVerification"/>
    /// on every success for both tenancy models — L2 can NEVER produce
    /// <see cref="FicVerificationState.ExchangeVerified"/> at creation time
    /// (GOTCHA 2: it cannot mint the UAMI's assertion). The handler records
    /// the pending state into
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.FicPendingPostAppServiceVerification"/>
    /// so H13/T4 discharges the real exchange verification post-App-Service.
    /// </summary>
    public FicVerificationState FicVerification { get; init; } = FicVerificationState.NotApplicable;
}

/// <summary>
/// C# equivalent of the <c>-FicOnly</c> script's exit-code contract
/// (task 205b row A42 / FR-C4 parity — see
/// <c>projects/customer-provisioning-orchestration-r1/notes/decisions/205b-a42-fic-parity-contract.md</c>):
/// <list type="bullet">
/// <item>script exit 0 ↔ <see cref="ExchangeVerified"/> — created + verified by a REAL token exchange. NOT producible by L2 at creation time (GOTCHA 2); reserved for exchange-capable verifiers (H13/T4 post-App-Service, task-186 E2E runner, Q11 BFF warmup self-proof).</item>
/// <item>script exit 1 ↔ (no enum value) — a fault. Surfaces as <see cref="EntraAppRegOutcome.Failure"/> / a thrown <see cref="CrossTenantFicRefusedException"/>, never as a Success state.</item>
/// <item>script exit 2 ↔ <see cref="PendingPostAppServiceVerification"/> — persisted + structurally verified (independent re-GET confirms the (issuer, subject, audience) triple) but NOT exchange-verified from this host. The NORMAL off-Azure/L2 result. NEVER terminal success: it REQUIRES a recorded post-App-Service verification (SF-8), tracked via <c>InterStepState.FicPendingPostAppServiceVerification</c>.</item>
/// </list>
/// </summary>
public enum FicVerificationState
{
    /// <summary>No FIC was created or touched by this outcome. Defensive default; retained after task 222 (D-13) despite both tenancy models now provisioning a per-customer FIC on success, in case a future outcome shape needs to represent "FIC not applicable".</summary>
    NotApplicable = 0,

    /// <summary>The FIC was proven by a REAL OAuth2 token exchange (script exit-0 equivalent). Only an exchange-capable host can assert this — never L2 at creation time.</summary>
    ExchangeVerified = 1,

    /// <summary>The FIC persisted and its (issuer, subject, audience) triple was structurally confirmed by an independent re-GET, but no exchange proof exists from this host (script exit-2 equivalent). Requires recorded post-App-Service verification — never report as terminal success.</summary>
    PendingPostAppServiceVerification = 2,
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
