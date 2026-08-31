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
//     - Production: GraphContainerProvisioner — Microsoft.Graph 6.5.0 under
//       ClientCertificateCredential (T6 confidential-client cert-based auth,
//       app-only). Calls Storage.FileStorage.Containers.PostAsync +
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
/// <param name="VaultName">Customer Key Vault name holding the SPE owner cert (§4D I4 tenant-scoped vault).</param>
/// <param name="CertSecretName">KV secret name holding the base64 PFX SPE owner cert (T6 cert bootstrap).</param>
/// <param name="OwningAppId">
/// The container-type's owning app-reg id. Used ONLY to construct the
/// ClientCertificateCredential (app-only Graph token) for the CREATE + ACTIVATE
/// calls — NOT registered anywhere. Sourced from run.InterStepState.BffAppRegId
/// (H3 output) for backward-compat with H8's existing DAG dependency on H3.
/// </param>
/// <param name="DisplayName">Human-readable container name (e.g. "Acme Corp").</param>
/// <param name="Description">Human-readable container description.</param>
public sealed record SpeContainerProvisionRequest(
    string CustomerId,
    string TenantId,
    string ContainerTypeId,
    string VaultName,
    string CertSecretName,
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
