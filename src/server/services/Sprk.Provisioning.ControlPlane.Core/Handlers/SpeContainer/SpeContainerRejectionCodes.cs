// -----------------------------------------------------------------------------
// SpeContainerRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H8SpeContainerHandler (H8-B semantics
// per task 214 — container CREATION only, container-TYPE creation retired to
// operator prereq per docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md).
//
// SUPERSEDES SpeContainerTypeRejectionCodes (deleted 2026-08-30 task 214).
// Renaming was mechanical + also stripped codes that H8-B no longer produces
// (KV write codes, container-type-creation prerequisites), and added codes for
// the new required-and-activate container-creation flow:
//   ADDED  : MissingContainerTypeId (from constants; if missing, IntakeParameters
//            is malformed / SKILL Step 4.0 was not run against a populated
//            spaarke-constants.yaml)
//   ADDED  : ContainerActivationFailed / ContainerActivationInfraFault (the
//            required /activate follow-up per topology doc §6 has its own
//            failure modes distinct from CREATE — a created-but-not-activated
//            container is unusable, QuarantineRequired)
//   DROPPED: MissingKeyVaultName / MissingSubscriptionId / MissingSharePointDomain
//            / MissingOwningAppId (H8-B doesn't write KV / doesn't need
//            SharePoint-domain-scoped calls / gets container-type from constants
//            not from H3's InterStepState)
//   DROPPED: KvWriteFailed / KvWriteInfraFault (no per-customer KV write —
//            containerTypeId comes from spaarke-constants.yaml, not per-customer)
//   DROPPED: TrapT6DelegatedTokenDetected (H8-B doesn't participate in T6 trap
//            detection — H13's T6SpeConfidentialClientTrapProbe still owns T6
//            acceptance gate per topology doc §R5 + task 214.4 Option A)
//
//   ADDED (unified-access-control-r2 task 165, batch-4 integration — owner rounds
//            35 / 41 / 49 re-applied to H8-B): MissingDataverseEnvUrl,
//            RootBusinessUnitUnresolved, ContainerBindingFailed /
//            ...NotRemoved / ContainerBindingInfraFault (every container is stamped
//            with its owning business unit, read back, or removed),
//            CreationRecordNotPersisted and ContainerCreationInDoubt (H8 records
//            what it created and resumes with it — never a second container).
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegRejectionCodes.cs —
// one const per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Machine-stable rejection codes for <see cref="H8SpeContainerHandler"/>
/// failures (H8-B semantics: container CREATION only).
/// </summary>
public static class SpeContainerRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1/I5 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "spe-missing-tenant-id";

    /// <summary>
    /// Run parameter <c>containerTypeId</c> missing from IntakeParameters.
    /// Populated by SKILL Step 4.0 from
    /// <c>scripts/provisioning-prereqs/spaarke-constants.yaml per_env_constants.&lt;env&gt;.containerTypeId</c>
    /// (populated by operator after completing SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md steps 1-7).
    /// If missing at H8 dispatch time, the operator has not completed the
    /// runbook OR SKILL Step 4.0 payload construction was bypassed — Resumable.
    /// </summary>
    public const string MissingContainerTypeId = "spe-missing-container-type-id";

    /// <summary>
    /// Task 245b: this L2 deployment has no <see cref="SpeContainerOptions.ContainerTypeOwners"/> entry
    /// for the run's <c>containerTypeId</c>, so H8 has no owning-app credential to create the container
    /// with. Nothing has been created — Resumable once the entry is configured on the Worker.
    /// </summary>
    public const string ContainerTypeOwnerNotConfigured = "spe-container-type-owner-not-configured";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "spe-run-not-found";

    /// <summary>
    /// The provisioner reported a hard failure (Graph API error) creating the
    /// container. No confirmed external side effect — Resumable; operator
    /// resolves the precondition (connectivity, permissions, container-type
    /// replication lead-time) then POSTs /api/runs/{id}/resume.
    /// </summary>
    public const string ProvisioningFailed = "spe-provisioning-failed";

    /// <summary>
    /// Task 227b: <c>InterStepState.BffAppRegId</c> (H3) or <c>InterStepState.MiClientId</c> (H2a) is empty, so H8
    /// cannot grant the customer's BFF identities on the container type. Resumable — nothing was written.
    /// </summary>
    public const string GrantIdentityMissing = "spe-grant-identity-missing";

    /// <summary>
    /// Task 227b: Graph refused to read or write a grant on the container-type registration (the diagnostic names
    /// the app id). Resumable — no container was created; a grant already written is kept and re-checked on resume.
    /// </summary>
    public const string ContainerTypeGrantFailed = "spe-container-type-grant-failed";

    /// <summary>Task 227b: infrastructure fault (token exchange, transport, timeout) while ensuring the grants — Resumable.</summary>
    public const string ContainerTypeGrantInfraFault = "spe-container-type-grant-infra-fault";

    /// <summary>
    /// Task 227e: the customer's recorded container (environment variable <c>sprk_SharePointEmbeddedContainerId</c>, which
    /// H7 writes on the first run) could not be read, or carries more than one value. H8 creates a container only when it
    /// can tell the customer has none — nothing was created. Resumable.
    /// </summary>
    public const string RecordedContainerUnreadable = "spe-recorded-container-unreadable";

    /// <summary>
    /// Task 227e: the container the environment records (or this run adopted) does not exist for the container type's
    /// owning app — deleted, or of another type or tenant. Not the replication wait: it is not a new container. Nothing
    /// was created or written — Resumable once the record names the customer's container.
    /// </summary>
    public const string RecordedContainerNotFound = "spe-recorded-container-not-found";

    /// <summary>
    /// Task 227e: the container H8 would reuse is not the customer's — another container type, another customer's
    /// <c>spaarkeCustomerId</c>, or another business unit's stamp. H8 wrote nothing to it (no stamp, no marker) and created
    /// nothing — Resumable once the record names the customer's container.
    /// </summary>
    public const string RecordedContainerNotTheCustomers = "spe-recorded-container-not-the-customers";

    /// <summary>
    /// Task 227e: two containers are the customer's — the environment's recorded one and this run's own record name
    /// different containers. H8 never picks one: the diagnostic names both and the operator decides which holds the
    /// customer's data before resuming. Nothing more was created — Resumable.
    /// </summary>
    public const string DuplicateCustomerContainers = "spe-duplicate-customer-containers";

    /// <summary>
    /// Task 227e: the customer's container is bound, but its <c>spaarkeCustomerId</c> marker could not be confirmed or
    /// written (Graph refused, or the container is marked for another customer / unreadably — then it is left untouched).
    /// The container stays on the run's record, so a resume marks it — Resumable.
    /// </summary>
    public const string ContainerMarkerFailed = "spe-container-marker-failed";

    /// <summary>Task 227e: infrastructure fault while confirming / writing the marker — Resumable (the container stays on record).</summary>
    public const string ContainerMarkerInfraFault = "spe-container-marker-infra-fault";

    /// <summary>Provisioner infrastructure fault (transport, timeout, unexpected exception) — Resumable, no external side effect confirmed.</summary>
    public const string ProvisioningInfraFault = "spe-provisioning-infra-fault";

    /// <summary>Provisioner completed but did not populate the required <c>ContainerId</c> output.</summary>
    public const string ProvisioningOutputsIncomplete = "spe-provisioning-outputs-incomplete";

    /// <summary>
    /// Container was created (POST /containers succeeded) but the follow-up
    /// <c>POST /containers/{id}/activate</c> call failed at the Graph API
    /// level (non-transient). QuarantineRequired — a created-but-not-activated
    /// container is unusable per topology doc §6.
    /// </summary>
    public const string ContainerActivationFailed = "spe-container-activation-failed";

    /// <summary>
    /// Container was created but the follow-up <c>/activate</c> call
    /// experienced an infrastructure fault (transport, timeout). QuarantineRequired
    /// — the container exists but its activation status is unknown.
    /// </summary>
    public const string ContainerActivationInfraFault = "spe-container-activation-infra-fault";

    /// <summary>
    /// The container was created + activated but the post-creation app-only
    /// GET verification did not return a Verified result (transient permission
    /// error or unexpected Graph error). QuarantineRequired — external
    /// resource exists but its readability is unconfirmed.
    /// </summary>
    public const string ContainerGetVerificationFailed = "spe-container-get-verification-failed";

    /// <summary>Verifier infrastructure fault after successful creation + activation — QuarantineRequired (created resource, unverified post-condition).</summary>
    public const string VerificationInfraFault = "spe-verification-infra-fault";

    /// <summary>
    /// <c>InterStepState.DataverseEnvUrl</c> (H5 output) is missing — H8 binds the container to the environment's root
    /// business unit (unified-access-control-r2 task 165, owner round 35 item 1), so H5 MUST complete first. Resumable;
    /// checked before any external side effect.
    /// </summary>
    public const string MissingDataverseEnvUrl = "spe-missing-dataverse-env-url";

    /// <summary>
    /// The customer environment's root business unit could not be read, or it reports none (or more than one).
    /// Resumable; checked before any external side effect — H8 never creates a container it could not bind.
    /// </summary>
    public const string RootBusinessUnitUnresolved = "spe-root-business-unit-unresolved";

    /// <summary>
    /// The container was created and verified, but its business-unit stamp did not read back, so it was REMOVED (owner
    /// round 35 item 1: no unbound container is left behind). QuarantineRequired; a resume after the cause is fixed
    /// creates a new container (the removed one is dropped from the run's record).
    /// </summary>
    public const string ContainerBindingFailed = "spe-container-binding-failed";

    /// <summary>
    /// As <see cref="ContainerBindingFailed"/>, but removing the unbound container ALSO failed: an unbound container
    /// remains, which no SPE admin route reaches. QuarantineRequired — the container stays on the run's record (a resume
    /// re-verifies and re-binds it); or bind it with
    /// <c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind &lt;containerId&gt;=&lt;rootBusinessUnitId&gt;</c>, or remove it.
    /// </summary>
    public const string ContainerBindingFailedNotRemoved = "spe-container-binding-failed-not-removed";

    /// <summary>The bind step threw before any Graph call — QuarantineRequired: the container exists, unbound, on record.</summary>
    public const string ContainerBindingInfraFault = "spe-container-binding-infra-fault";

    /// <summary>
    /// H8 created a container but could not record it in the run (the run was deleted, or every merge retry lost a
    /// concurrent write). QuarantineRequired: the diagnostic names the container — it is UNBOUND (no SPE admin route
    /// reaches it); bind it with <c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind
    /// &lt;containerId&gt;=&lt;rootBusinessUnitId&gt;</c> or remove it before the run is resumed (a resume of a run with no
    /// record creates afresh).
    /// </summary>
    public const string CreationRecordNotPersisted = "spe-creation-record-not-persisted";

    /// <summary>
    /// The container POST got no authoritative answer (a client timeout, a dropped connection, a 2xx without an id): a
    /// container may exist, UNBOUND, that the run does not name. QuarantineRequired; H8 creates NO container until an
    /// operator has looked (the diagnostic is the procedure), recorded a container found, and cleared the quarantine
    /// (unified-access-control-r2 task 165, owner round 49 item 2, on H8-B). The container type is shared, so H8 does not
    /// guess by listing it.
    /// </summary>
    public const string ContainerCreationInDoubt = "spe-container-creation-in-doubt";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "spe-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H8 was in flight.</summary>
    public const string RunDeletedDuringProvisioning = "spe-run-deleted-during-provisioning";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H8 (H8-B semantics). Kept as string constants so grep across the codebase
/// finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class SpeContainerGates
{
    /// <summary>
    /// The gate H8 owns for the post-creation + activation + app-only GET
    /// verification. Flipped to <c>Verified</c> once <see cref="ISpeContainerVerifier"/>
    /// confirms the container is readable via a fresh app-only token. Retains
    /// its historical "h8-t6-verified" identifier for backward-compatibility
    /// with any external tooling grepping GateStates by name.
    /// </summary>
    public const string T6Verified = "h8-t6-verified";
}
