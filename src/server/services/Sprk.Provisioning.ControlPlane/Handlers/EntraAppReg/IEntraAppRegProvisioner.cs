// -----------------------------------------------------------------------------
// IEntraAppRegProvisioner.cs
//
// L2 abstraction over the actual Entra app-registration provisioning
// invocation. The production implementation
// (<see cref="RegisterEntraAppRegScriptProvisioner"/>) shells out to
// <c>scripts/Register-EntraAppRegistrations.ps1</c> (hardened by r1 task 010
// per commit fea66c023 — full Get-then-Add reconciler idempotency). Unit tests
// inject stubs to avoid pwsh + Graph round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="RegisterEntraAppRegScriptProvisioner"/> —
//       shells out to Register-EntraAppRegistrations.ps1 with parsed outputs.
//     - Test: stubs injected per unit test that construct
//       <see cref="EntraAppRegOutcome"/> directly (see H3EntraAppRegHandlerTests).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs Graph SDK re-implementation):
//   Same trade-off as H2a Bicep deploy (see
//   <c>Handlers/BicepInfraDeploy/IBicepDeployRunner.cs</c>): the PS script IS
//   the source-of-truth for the 5-step app-registration reconciler (create /
//   patch signInAudience / append identifierUri / append exposed scope /
//   reconcile requiredResourceAccess). Re-implementing in C# via Microsoft.
//   Graph SDK v6 duplicates a hardened surface with meaningful idempotency
//   subtleties (Get-then-Add per-permission diff, secret-only-when-missing).
//   H3 therefore WRAPS the script and adds:
//     (a) tenant-id guard (§4D I1 — script requires -TenantId mandatory)
//     (b) admin-consent verification (a separate concern the script does not
//         perform per task 010 SCOPE constraint)
//     (c) Dataverse-S2S structural guard (r3 task 060 dropped that app-reg;
//         MUST NOT be re-introduced per spec.md MUST rule).
//   The provisioner is admin-consent-agnostic — it either creates/reconciles
//   the BFF app-reg or it doesn't. Admin-consent verification is
//   <see cref="IAdminConsentVerifier"/>'s concern invoked BY the handler AFTER
//   this runner returns Success.
//
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Executes the per-customer Entra app-registration provisioning for handler
/// H3. Production impl shells out to <c>scripts/Register-EntraAppRegistrations.ps1</c>;
/// test impls return canned <see cref="EntraAppRegOutcome"/>s.
/// </summary>
public interface IEntraAppRegProvisioner
{
    /// <summary>
    /// Runs the app-registration provisioning. Returns a typed outcome —
    /// success carries the outputs consumed by downstream handlers (BFF
    /// appId + KV secret URI); failure carries a diagnostic. Domain
    /// failures do NOT throw (parity with <see cref="Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy.IBicepDeployRunner"/>);
    /// infra faults MAY throw.
    /// </summary>
    /// <param name="request">Provisioning inputs (customerId, tenantId, KV vault name).</param>
    /// <param name="cancellationToken">Cancellation token — a long-running script MUST honor it.</param>
    Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Entra app-registration provisioning invocation.
/// Immutable record; the caller (<see cref="H3EntraAppRegHandler"/>) constructs
/// one per run from <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — MUST be explicit, never default; passed as script's mandatory <c>-TenantId</c>).</param>
/// <param name="KeyVaultName">Target Key Vault name (e.g. <c>sprk-acme-prod-kv</c>) — passed as script's <c>-KeyVaultName</c>. Client secret is written here as <c>BFF-API-ClientSecret</c>.</param>
public sealed record EntraAppRegRequest(
    string CustomerId,
    string TenantId,
    string KeyVaultName);

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
