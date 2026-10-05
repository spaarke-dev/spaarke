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
// BUSINESS-UNIT STAMP (unified-access-control-r2 task 165, owner round 35 item 1):
//   Every SPE container carries its owning business unit as the custom property
//   Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName — the
//   BFF's admin plane reaches NO unbound container. H8's root container is
//   bound to the ROOT business unit of the customer's Dataverse environment
//   (H8 therefore runs after H5 — DagAdvancer.HandlerDependencies) through
//   BindRootContainerAsync, once the container is verified readable.
//   RESUME (owner round 41 item 1): H8 records what it created in the run
//   immediately; a re-entry never calls ProvisionAsync for a recorded root
//   container, and when only the type is recorded it passes
//   ExistingContainerTypeId so no second (undeletable) container type is made.
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

    /// <summary>
    /// Stamps the root container H8 created with its owning business unit (unified-access-control-r2 task 165, owner
    /// round 35 item 1 — every container-creation path stamps), reads the stamp back, and REMOVES the container when the
    /// stamp does not read back, so no unbound container is left behind. Called by the handler once the container is
    /// verified readable (an SPE container may be unaddressable for up to 24h after creation — stamping it earlier would
    /// remove healthy containers during that documented window). Domain failures do NOT throw; infra faults before any
    /// Graph call (cert load) MAY throw.
    /// </summary>
    Task<SpeContainerBindOutcome> BindRootContainerAsync(
        SpeContainerBindRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Inputs to <see cref="ISpeContainerTypeProvisioner.BindRootContainerAsync"/>.</summary>
/// <param name="CustomerId">Customer partition key — audit logs only.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5).</param>
/// <param name="OwningAppId">The owning app (H3 output) — the confidential-client identity that created the container.</param>
/// <param name="VaultName">Customer Key Vault holding the SPE owner cert (T6).</param>
/// <param name="CertSecretName">KV secret holding the base64 PFX SPE owner cert (T6).</param>
/// <param name="ContainerId">The root container to bind.</param>
/// <param name="BusinessUnitId">The owning business unit: the ROOT business unit of the customer's Dataverse environment.</param>
public sealed record SpeContainerBindRequest(
    string CustomerId,
    string TenantId,
    string OwningAppId,
    string VaultName,
    string CertSecretName,
    string ContainerId,
    Guid BusinessUnitId);

/// <summary>Discriminated result of <see cref="ISpeContainerTypeProvisioner.BindRootContainerAsync"/>.</summary>
public abstract record SpeContainerBindOutcome
{
    private SpeContainerBindOutcome() { }

    /// <summary>The stamp was written and read back.</summary>
    public sealed record Bound : SpeContainerBindOutcome;

    /// <summary>
    /// The stamp did not land. <paramref name="Removed"/> is true when the container was then deleted (nothing unbound
    /// is left); false when that also failed and an UNBOUND container remains (no admin route reaches it until the
    /// backfill binds it with <c>-Bind</c> or an operator removes it).
    /// </summary>
    public sealed record NotBound(string Diagnostic, bool Removed) : SpeContainerBindOutcome;
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
/// <param name="VaultName">Customer Key Vault name holding the SPE owner cert (§4D I4 tenant-scoped vault) — passed as the script's <c>-KeyVaultName</c>.</param>
/// <param name="CertSecretName">KV secret name holding the base64 PFX SPE owner cert (T6 cert bootstrap) — passed as the script's <c>-CertSecretName</c>.</param>
/// <param name="DisplayName">Container-type display name.</param>
/// <param name="ExistingContainerTypeId">
/// The container type THIS run's H8 already created (recorded in the run — unified-access-control-r2 task 165, owner
/// round 41 item 1), or null. When set, NO container type is created: only a new root container is created in it (the
/// recorded root container was removed after a failed bind, or creation stopped after the type). A container type is
/// durable customer data that cannot be deleted, so a resume never creates a second one.
/// </param>
public sealed record SpeContainerTypeProvisionRequest(
    string CustomerId,
    string TenantId,
    string OwningAppId,
    string SharePointDomain,
    string VaultName,
    string CertSecretName,
    string DisplayName,
    string? ExistingContainerTypeId = null);

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
    /// Resumable failure. <paramref name="CreatedContainerTypeId"/> names the container type this call DID create (or
    /// reused) before it failed, so the handler records it and a resume creates only the root container in it
    /// (task 165, owner round 41 item 1) — null when no container type exists yet.
    /// </summary>
    public sealed record Failure(string Diagnostic, bool IsDelegatedTokenTrap, string? CreatedContainerTypeId = null)
        : SpeContainerTypeProvisionOutcome;
}
