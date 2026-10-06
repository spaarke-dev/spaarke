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
//
// BUSINESS-UNIT STAMP + RESUME (unified-access-control-r2 task 165, owner rounds
// 35 / 41 / 49 — re-applied to H8-B at the batch-4 integration):
//   - BindRootContainerAsync stamps the container with its owning business unit
//     (custom property Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName
//     — the one C# constant the BFF authorizes against), reads it back, and
//     DELETES the container when the stamp did not land. H8 calls it once the
//     container is verified addressable.
//   - ActivateAsync re-activates a container H8 created and RECORDED whose
//     /activate failed, so a resume never creates a second container.
//   - Every fault after the container POST was sent is a CreateFailure saying
//     whether a container may exist (ContainerInDoubt) — never an exception.
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
    /// Domain failures do NOT throw. A fault before any Graph request is sent
    /// (the owning-app token exchange) MAY throw; every fault after the container
    /// POST was sent is returned (<see cref="SpeContainerProvisionOutcome.CreateFailure.ContainerInDoubt"/> /
    /// <see cref="SpeContainerProvisionOutcome.ActivateFailure"/>), so the handler can record what may exist.
    /// Successful outcome carries the new container's GUID.
    /// </summary>
    Task<SpeContainerProvisionOutcome> ProvisionAsync(
        SpeContainerProvisionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Activates a container H8 already created and recorded whose <c>/activate</c> failed (unified-access-control-r2
    /// task 165: a resume continues with the container it made — it never creates another). Returns
    /// <see cref="SpeContainerProvisionOutcome.Success"/> or <see cref="SpeContainerProvisionOutcome.ActivateFailure"/>
    /// only; a fault before any Graph request MAY throw.
    /// </summary>
    Task<SpeContainerProvisionOutcome> ActivateAsync(
        SpeContainerActivationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stamps a container H8 created with its owning business unit (unified-access-control-r2 task 165, owner round 35
    /// item 1 — every container-creation path stamps), reads the stamp back, and REMOVES the container when the stamp
    /// does not read back, so no unbound container is left behind. Called by the handler once the container is verified
    /// readable (an SPE container may be unaddressable for up to 24h after creation — stamping it earlier would remove
    /// healthy containers during that documented window). Domain failures do NOT throw; a fault before any Graph call
    /// MAY throw.
    /// </summary>
    Task<SpeContainerBindOutcome> BindRootContainerAsync(
        SpeContainerBindRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Inputs to <see cref="ISpeContainerProvisioner.ActivateAsync"/>.</summary>
/// <param name="CustomerId">Customer partition key — audit logs only.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5).</param>
/// <param name="OwningAppId">The container type's owning app — the identity that created the container.</param>
/// <param name="ContainerId">The recorded container to activate.</param>
public sealed record SpeContainerActivationRequest(
    string CustomerId,
    string TenantId,
    string OwningAppId,
    string ContainerId);

/// <summary>Inputs to <see cref="ISpeContainerProvisioner.BindRootContainerAsync"/>.</summary>
/// <param name="CustomerId">Customer partition key — audit logs only.</param>
/// <param name="TenantId">Customer Entra tenant id (§4D I1/I5).</param>
/// <param name="OwningAppId">The container type's owning app — the identity that created the container.</param>
/// <param name="ContainerId">The container to bind.</param>
/// <param name="BusinessUnitId">The owning business unit: the ROOT business unit of the customer's Dataverse environment.</param>
public sealed record SpeContainerBindRequest(
    string CustomerId,
    string TenantId,
    string OwningAppId,
    string ContainerId,
    Guid BusinessUnitId);

/// <summary>Discriminated result of <see cref="ISpeContainerProvisioner.BindRootContainerAsync"/>.</summary>
public abstract record SpeContainerBindOutcome
{
    private SpeContainerBindOutcome() { }

    /// <summary>The stamp was written and read back.</summary>
    public sealed record Bound : SpeContainerBindOutcome;

    /// <summary>
    /// The stamp did not land. <paramref name="Removed"/> is true when the container was then deleted (nothing unbound
    /// is left); false when that also failed and an UNBOUND container remains (no admin route reaches it until the
    /// backfill binds it with <c>-Bind</c>, an operator removes it, or a resume of H8 binds it).
    /// </summary>
    public sealed record NotBound(string Diagnostic, bool Removed) : SpeContainerBindOutcome;
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
    /// The container CREATE call failed. With <paramref name="ContainerInDoubt"/> false it is Graph's own answer (an
    /// <c>ODataError</c>) — nothing was created, Resumable at the handler level. With it true the POST got NO
    /// authoritative answer (a client timeout, a dropped connection, a 2xx without an id): a container may exist,
    /// unbound, that no one names — the handler records that and creates no container until an operator has checked
    /// (unified-access-control-r2 task 165, owner round 49 item 2).
    /// </summary>
    public sealed record CreateFailure(string Diagnostic, bool ContainerInDoubt = false) : SpeContainerProvisionOutcome;

    /// <summary>
    /// The container was created but the follow-up /activate call failed.
    /// <paramref name="ContainerId"/> carries the created-but-not-activated
    /// GUID (for audit / cleanup). Handler classifies as QuarantineRequired —
    /// a created-but-not-activated container is unusable per topology doc §6.
    /// </summary>
    /// <remarks><paramref name="NoAnswer"/>: the /activate call got no authoritative answer (transport fault, timeout).</remarks>
    public sealed record ActivateFailure(string ContainerId, string Diagnostic, bool NoAnswer = false) : SpeContainerProvisionOutcome;
}
