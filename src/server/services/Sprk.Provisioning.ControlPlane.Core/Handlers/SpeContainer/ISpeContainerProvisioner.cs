// -----------------------------------------------------------------------------
// ISpeContainerProvisioner.cs
//
// L2 abstraction over SPE CONTAINER creation (container CREATION, not
// container-TYPE creation) for handler H8 (H8-B semantics per task 214).
//
// SUPERSEDES ISpeContainerTypeProvisioner (deleted 2026-08-30). The old shape
// created a container-type + registration + root container in one call — all
// three steps required delegated auth for the container-type-creation call per
// topology doc §R5, so that flow could never succeed under L2's app-only
// runtime credential (verified 403 accessDenied on 2026-08-30 —
// runs/h8-live-test-2026-08-30.md). Container-type creation is now a one-time
// operator prereq (docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md); H8's
// remaining responsibility at customer dispatch is container CREATION only
// (app-only-OK per topology doc §6, plus the required /activate follow-up).
//
// SEAM JUSTIFICATION (ADR-010):
//   >= 2 implementations exist from day 1:
//     - Production: GraphContainerProvisioner — Microsoft.Graph 6.5.0 as the
//       container type's owning app, app-only, via the Worker UAMI's federated
//       identity credential (task 248). Calls Storage.FileStorage.Containers.PostAsync +
//       Containers[id].Activate.PostAsync per topology doc §6.
//     - Test: fake ISpeContainerProvisioner returning canned outcomes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Executes the per-customer SPE container creation for handler H8 (H8-B
/// semantics: CREATE + ACTIVATE only; container-type is a pre-existing prereq
/// per topology doc §R1 / §6 + SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md).
/// Production impl calls Graph SDK; test impls return canned
/// <see cref="SpeContainerProvisionOutcome"/>s.
/// </summary>
public interface ISpeContainerProvisioner
{
    /// <summary>
    /// Creates and activates a container of the specified container-type.
    /// Domain failures do NOT throw; infra faults (transport, timeout) MAY
    /// throw. Successful outcome carries the new container's GUID.
    /// </summary>
    Task<SpeContainerProvisionOutcome> ProvisionAsync(
        SpeContainerProvisionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Task 227b (G9): ensures each grant exists on the container type's registration, as the owning app —
    /// GET the grant, then create it (Graph v1.0 create is PUT) when missing or PATCH it when its permissions
    /// differ; nothing is written when it already matches. Domain failures do NOT throw; infra faults (token
    /// exchange, transport, timeout) MAY throw.
    /// </summary>
    Task<SpeContainerTypeGrantOutcome> EnsureGrantsAsync(
        SpeContainerTypeGrantRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One <c>applicationPermissionGrants</c> entry on a container-type registration (task 227b). Permission names
/// are Graph's <c>fileStorageContainerTypeAppPermission</c> values (e.g. <c>full</c>); an empty list means none.
/// </summary>
/// <param name="AppId">The app (client) id the grant is for — the stamp UAMI or the customer BFF app registration.</param>
/// <param name="ApplicationPermissions">Permissions for app-only tokens issued to <paramref name="AppId"/>.</param>
/// <param name="DelegatedPermissions">Permissions for delegated (OBO) tokens issued to <paramref name="AppId"/>.</param>
public sealed record SpeContainerTypeGrant(
    string AppId,
    IReadOnlyList<string> ApplicationPermissions,
    IReadOnlyList<string> DelegatedPermissions);

/// <summary>Inputs to <see cref="ISpeContainerProvisioner.EnsureGrantsAsync"/>.</summary>
/// <param name="TenantId">Customer Entra tenant id (§4D I5 — explicit, never default).</param>
/// <param name="ContainerTypeId">The container type whose registration carries the grants.</param>
/// <param name="OwningAppId">The container type's owning app — the only app allowed to change its registration (FileStorageContainerTypeReg.Selected).</param>
/// <param name="Grants">The grants to ensure, in order.</param>
public sealed record SpeContainerTypeGrantRequest(
    string TenantId,
    string ContainerTypeId,
    string OwningAppId,
    IReadOnlyList<SpeContainerTypeGrant> Grants);

/// <summary>Discriminated result of <see cref="ISpeContainerProvisioner.EnsureGrantsAsync"/>.</summary>
public abstract record SpeContainerTypeGrantOutcome
{
    private SpeContainerTypeGrantOutcome() { }

    /// <summary>Every grant is in place. <paramref name="WrittenAppIds"/> lists those created or updated by this call.</summary>
    public sealed record Success(IReadOnlyList<string> WrittenAppIds) : SpeContainerTypeGrantOutcome;

    /// <summary>Graph refused to read or write the grant for <paramref name="AppId"/>; later grants were not attempted.</summary>
    public sealed record Failure(string AppId, string Diagnostic) : SpeContainerTypeGrantOutcome;
}

/// <summary>
/// Inputs to a single SPE container creation invocation. Immutable record;
/// the caller (<see cref="H8SpeContainerHandler"/>) constructs one per run
/// from <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key — carried for audit logs + as description content.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5 — MUST be explicit, never default).</param>
/// <param name="ContainerTypeId">
/// The PRE-EXISTING container-type GUID this container will belong to.
/// Sourced from <c>spaarke-constants.yaml per_env_constants.&lt;env&gt;.containerTypeId</c>
/// (populated once by the operator per SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md
/// steps 3 + 7). NEVER created per-customer.
/// </param>
/// <param name="OwningAppId">
/// The container-type's owning app-reg id. Used ONLY to obtain the owning app's
/// app-only Graph token (Worker UAMI federated credential — task 248) for the
/// CREATE + ACTIVATE calls — NOT registered anywhere. From <see cref="SpeContainerOptions.ContainerTypeOwners"/> for the run's
/// container type (task 245b) — never the customer BFF app (InterStepState.BffAppRegId), which is a separate,
/// secret-free identity (topology §3A).
/// </param>
/// <param name="DisplayName">Human-readable container name (e.g. "Acme Corp").</param>
/// <param name="Description">Human-readable container description.</param>
public sealed record SpeContainerProvisionRequest(
    string CustomerId,
    string TenantId,
    string ContainerTypeId,
    string OwningAppId,
    string DisplayName,
    string Description);

/// <summary>
/// Outputs H8 needs to (a) populate <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.SpeContainerId"/>
/// (H7 reads this to write the Dataverse env-var), and (b) verify the
/// container via <see cref="ISpeContainerVerifier"/>.
/// </summary>
/// <param name="ContainerId">SPE container id (GUID) of the newly-created + activated container.</param>
public sealed record SpeContainerProvisionOutputs(
    string ContainerId);

/// <summary>
/// Discriminated result of <see cref="ISpeContainerProvisioner.ProvisionAsync"/>.
/// </summary>
public abstract record SpeContainerProvisionOutcome
{
    private SpeContainerProvisionOutcome() { }

    /// <summary>Container was created AND activated — outputs carry the new container id.</summary>
    public sealed record Success(SpeContainerProvisionOutputs Outputs) : SpeContainerProvisionOutcome;

    /// <summary>
    /// The container CREATE call failed (Graph API error). No confirmed
    /// external side effect — Resumable at the handler level.
    /// </summary>
    public sealed record CreateFailure(string Diagnostic) : SpeContainerProvisionOutcome;

    /// <summary>
    /// The container was created but the follow-up /activate call failed.
    /// <paramref name="ContainerId"/> carries the created-but-not-activated
    /// GUID (for audit / cleanup). Handler classifies as QuarantineRequired —
    /// a created-but-not-activated container is unusable per topology doc §6.
    /// </summary>
    public sealed record ActivateFailure(string ContainerId, string Diagnostic) : SpeContainerProvisionOutcome;
}
