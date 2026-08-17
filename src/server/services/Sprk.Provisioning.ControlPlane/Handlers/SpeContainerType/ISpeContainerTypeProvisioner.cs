// -----------------------------------------------------------------------------
// ISpeContainerTypeProvisioner.cs
//
// L2 abstraction over SPE container-type + root-container creation for handler
// H8. The production implementation (<see cref="CreateNewContainerTypeScriptProvisioner"/>)
// shells out to the T6-hardened <c>scripts/Create-NewContainerType.ps1</c>
// (post-task-011 confidential-client cert-based refactor) with
// <c>-CreateTestContainer</c> so BOTH the container-type AND a root container
// are created in a single invocation. Unit tests inject stubs to avoid pwsh +
// Graph round-trips.
//
// WHY -CreateTestContainer FOR THE "ROOT CONTAINER" (deviation from the POML's
// literal "invoke New-BusinessUnitContainer.ps1" wording — Path C pivot):
//   New-BusinessUnitContainer.ps1 requires an EXISTING Dataverse business-unit
//   row (-BusinessUnitId / -BusinessUnitName) and writes its output back to
//   that row's sprk_containerid column. Per design.md §4.1's handler DAG
//   ("H4 -> H3 -> { H8, H9 }"), H8 runs BEFORE H5 (Dataverse environment
//   creation) / H6 (solution import) — no customer Dataverse environment (and
//   therefore no business-unit row) exists yet at H8's point in the pipeline.
//   Create-NewContainerType.ps1's own -CreateTestContainer switch creates one
//   container immediately after the container-type, via Graph API only (zero
//   Dataverse dependency) — exactly what H8 needs for "container-type + root
//   container" without a chicken-and-egg dependency on H5/H6. Documented as a
//   deviation in projects/customer-provisioning-orchestration-r1/notes/
//   task-051-h8-deviations.md per CLAUDE.md §6.5 path C.
//
// SEAM JUSTIFICATION (ADR-010):
//   >= 2 implementations exist from day 1:
//     - Production: CreateNewContainerTypeScriptProvisioner — shells out to
//       Create-NewContainerType.ps1 with parsed outputs.
//     - Test: stubs injected per unit test.
//
// DESIGN CHOICE (shell-out vs Graph SDK re-implementation) — parity with H3's
// IEntraAppRegProvisioner / RegisterEntraAppRegScriptProvisioner rationale:
//   The PS script IS the source-of-truth for the T6 confidential-client
//   cert-based reconciler (Get-SpeConfidentialClientToken.ps1 helper). H8
//   WRAPS the script rather than re-implementing cert bootstrap + JWT
//   client-assertion signing in C# via Microsoft.Graph SDK v6 — that
//   complexity is already hardened + tested in the script (task 011).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Executes the per-customer SPE container-type + root-container provisioning
/// for handler H8. Production impl shells out to
/// <c>scripts/Create-NewContainerType.ps1 -CreateTestContainer</c>; test impls
/// return canned <see cref="SpeContainerTypeProvisionOutcome"/>s.
/// </summary>
public interface ISpeContainerTypeProvisioner
{
    /// <summary>
    /// Runs the container-type + root-container provisioning. Domain failures
    /// (including a detected T6 delegated-token trap) do NOT throw — infra
    /// faults (process launch failure, timeout) MAY throw.
    /// </summary>
    /// <param name="request">Provisioning inputs.</param>
    /// <param name="cancellationToken">Cancellation token — a long-running script MUST honor it.</param>
    Task<SpeContainerTypeProvisionOutcome> ProvisionAsync(
        SpeContainerTypeProvisionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single SPE container-type + root-container provisioning
/// invocation. Immutable record; the caller (<see cref="H8SpeContainerTypeHandler"/>)
/// constructs one per run from <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>
/// + <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.InterStepState"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key — carried for audit logs only.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5 — MUST be explicit, never default; passed as the script's mandatory <c>-TenantId</c>).</param>
/// <param name="OwningAppId">BFF API app registration id (H3 output, <c>InterStepState.BffAppRegId</c>) — the confidential-client identity that owns the container type.</param>
/// <param name="SharePointDomain">Customer SharePoint domain (e.g. <c>acme.sharepoint.com</c>) — passed as the script's <c>-SharePointDomain</c>.</param>
/// <param name="KeyVaultName">Customer Key Vault name holding the SPE owner cert (§4D I4 tenant-scoped vault) — passed as the script's <c>-KeyVaultName</c>.</param>
/// <param name="CertSecretName">KV secret name holding the base64 PFX SPE owner cert (T6 cert bootstrap) — passed as the script's <c>-CertSecretName</c>.</param>
/// <param name="DisplayName">Container-type display name.</param>
public sealed record SpeContainerTypeProvisionRequest(
    string CustomerId,
    string TenantId,
    string OwningAppId,
    string SharePointDomain,
    string KeyVaultName,
    string CertSecretName,
    string DisplayName);

/// <summary>
/// Outputs H8 needs to (a) populate <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.ContainerTypeId"/>,
/// (b) verify the root container via <see cref="ISpeContainerVerifier"/>, and
/// (c) persist the container-type id to KV via <see cref="ISpeContainerIdKvWriter"/>.
/// </summary>
/// <param name="ContainerTypeId">SPE container-type id (GUID) created + registered to the owning app.</param>
/// <param name="RootContainerId">SPE container id (GUID) of the root container created within the container type.</param>
public sealed record SpeContainerTypeProvisionOutputs(
    string ContainerTypeId,
    string RootContainerId);

/// <summary>
/// Discriminated result of <see cref="ISpeContainerTypeProvisioner.ProvisionAsync"/>.
/// </summary>
public abstract record SpeContainerTypeProvisionOutcome
{
    private SpeContainerTypeProvisionOutcome() { }

    /// <summary>Provisioning succeeded — outputs carry the container-type id + root container id.</summary>
    public sealed record Success(SpeContainerTypeProvisionOutputs Outputs) : SpeContainerTypeProvisionOutcome;

    /// <summary>
    /// Provisioning failed. <paramref name="IsDelegatedTokenTrap"/> is true when
    /// the script's output indicates the T6 silent-fail trap fired (a delegated
    /// token was used, or the script's confidential-client "T6 cleared" evidence
    /// markers are absent despite a claimed success) — the handler maps this to
    /// <see cref="Handlers.FailureClass.QuarantineRequired"/> + a distinct
    /// rejection code so operators never mistake a T6 regression for a routine
    /// Resumable failure.
    /// </summary>
    public sealed record Failure(string Diagnostic, bool IsDelegatedTokenTrap) : SpeContainerTypeProvisionOutcome;
}
