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
    /// Task 227e: the lookup for the customer's existing container got no verdict (Graph refused the listing or a
    /// candidate's custom properties, or the listing did not finish). Nothing was created — Resumable.
    /// </summary>
    public const string ContainerLookupFailed = "spe-container-lookup-failed";

    /// <summary>Task 227e: infrastructure fault (token exchange, transport, timeout) during the lookup — Resumable, nothing created.</summary>
    public const string ContainerLookupInfraFault = "spe-container-lookup-infra-fault";

    /// <summary>
    /// Task 227e: more than one container of the type is this customer's (same display name, this customer's marker
    /// or none). H8 never picks one: the diagnostic names them all and the operator decides (delete or rename the
    /// extra one, or set its marker) before resuming. Nothing was created — Resumable.
    /// </summary>
    public const string DuplicateCustomerContainers = "spe-duplicate-customer-containers";

    /// <summary>
    /// Task 227e: the customer's container exists and is verified, but its <c>spaarkeCustomerId</c> marker could not
    /// be confirmed or written (Graph refused, or the container is marked for another customer — then it is left
    /// untouched). The container id is kept on the run, so a resume reuses it — Resumable.
    /// </summary>
    public const string ContainerMarkerFailed = "spe-container-marker-failed";

    /// <summary>Task 227e: infrastructure fault while confirming / writing the marker — Resumable (the container id is kept).</summary>
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
