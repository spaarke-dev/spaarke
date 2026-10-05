// -----------------------------------------------------------------------------
// SpeContainerTypeRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H8SpeContainerTypeHandler (task 051).
// Every distinct failure mode gets its own code so the reconciler + operator UI
// can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity).
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegRejectionCodes.cs and
// Handlers/KvSecretsPopulation/KvSecretsPopulationRejectionCodes.cs — one const
// per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Machine-stable rejection codes for <see cref="H8SpeContainerTypeHandler"/>
/// failures.
/// </summary>
public static class SpeContainerTypeRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1/I5 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "spe-missing-tenant-id";

    /// <summary>Run parameter <c>keyVaultName</c> missing — the customer's KV vault target for the cert bootstrap + container-type id secret.</summary>
    public const string MissingKeyVaultName = "spe-missing-kv-name";

    /// <summary>Run parameter <c>subscriptionId</c> missing — needed for `az` CLI --subscription scoping on the KV write.</summary>
    public const string MissingSubscriptionId = "spe-missing-subscription-id";

    /// <summary>Run parameter <c>sharePointDomain</c> missing — the script requires <c>-SharePointDomain</c> for the SharePoint-side registration call.</summary>
    public const string MissingSharePointDomain = "spe-missing-sharepoint-domain";

    /// <summary>
    /// <c>InterStepState.BffAppRegId</c> (H3 output) is missing — H8 owns no
    /// path to acquire the owning app id itself; H3 MUST complete first.
    /// </summary>
    public const string MissingOwningAppId = "spe-missing-owning-app-id";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "spe-run-not-found";

    /// <summary>
    /// <c>InterStepState.DataverseEnvUrl</c> (H5 output) is missing — H8 binds the root container to the environment's
    /// root business unit (unified-access-control-r2 task 165, owner round 35 item 1), so H5 MUST complete first.
    /// Resumable; checked before any external side effect.
    /// </summary>
    public const string MissingDataverseEnvUrl = "spe-missing-dataverse-env-url";

    /// <summary>
    /// The customer environment's root business unit could not be read, or it reports none (or more than one).
    /// Resumable; checked before any external side effect — H8 never creates a container it could not bind.
    /// </summary>
    public const string RootBusinessUnitUnresolved = "spe-root-business-unit-unresolved";

    /// <summary>
    /// The root container was created and verified, but its business-unit stamp did not read back, so it was REMOVED
    /// (round 35 item 1: no unbound container is left behind). QuarantineRequired — the container type exists without
    /// a root container; an operator re-runs H8 after the cause is fixed.
    /// </summary>
    public const string ContainerBindingFailed = "spe-container-binding-failed";

    /// <summary>
    /// As <see cref="ContainerBindingFailed"/>, but removing the unbound container ALSO failed: an unbound container
    /// remains, which no SPE admin route reaches. QuarantineRequired — bind it with
    /// <c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind &lt;containerId&gt;=&lt;rootBusinessUnitId&gt;</c> or remove it.
    /// </summary>
    public const string ContainerBindingFailedNotRemoved = "spe-container-binding-failed-not-removed";

    /// <summary>The bind step threw (cert load before any Graph call) — QuarantineRequired: the container exists, unbound.</summary>
    public const string ContainerBindingInfraFault = "spe-container-binding-infra-fault";

    /// <summary>
    /// The run records a root container this H8 created but no container type for it — a state H8 never writes
    /// (unified-access-control-r2 task 165, owner round 41 item 1). QuarantineRequired, and NOTHING is created: H8 never
    /// guesses which type it made, and never makes a second container while one it created is unaccounted for.
    /// </summary>
    public const string CreationRecordInconsistent = "spe-creation-record-inconsistent";

    /// <summary>
    /// H8 created a container type and root container but could not record them in the run (the run was deleted, or
    /// every merge retry lost a concurrent write). QuarantineRequired: the diagnostic names both ids — the root container
    /// is UNBOUND (no SPE admin route reaches it); bind it with
    /// <c>scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind &lt;containerId&gt;=&lt;rootBusinessUnitId&gt;</c> or remove it
    /// before the run is resumed (a resume of a run with no record creates afresh).
    /// </summary>
    public const string CreationRecordNotPersisted = "spe-creation-record-not-persisted";

    /// <summary>
    /// The provisioner reported a hard failure (PS script non-zero exit /
    /// Graph error) that is NOT a T6 delegated-token trap. Resumable — operator
    /// resolves the precondition (connectivity, permissions, SPE cert-replication
    /// lead-time) then POSTs /api/runs/{id}/resume.
    /// </summary>
    public const string ProvisioningFailed = "spe-provisioning-failed";

    /// <summary>Provisioner infrastructure fault (process launch failure, timeout) — Resumable, no external side effect confirmed.</summary>
    public const string ProvisioningInfraFault = "spe-provisioning-infra-fault";

    /// <summary>Provisioner completed but did not populate the required output set (containerTypeId + rootContainerId).</summary>
    public const string ProvisioningOutputsIncomplete = "spe-provisioning-outputs-incomplete";

    /// <summary>
    /// T6 silent-fail trap (spec.md FR-33): a delegated token was used (or the
    /// confidential-client "T6 cleared" evidence is missing/incomplete) for
    /// SPE container-type or container creation, OR the post-creation
    /// app-only GET verification. QuarantineRequired — this is the specific
    /// failure T6 exists to catch; it MUST NOT be classified as a routine
    /// Resumable failure.
    /// </summary>
    public const string TrapT6DelegatedTokenDetected = "spe-trap-t6-delegated-token-detected";

    /// <summary>
    /// The root container was created but the post-creation app-only GET
    /// verification did not return a Verified result (non-T6 reason, e.g.
    /// transient 404 propagation lag or unexpected Graph error).
    /// QuarantineRequired — external resource exists but is unverified.
    /// </summary>
    public const string ContainerGetVerificationFailed = "spe-container-get-verification-failed";

    /// <summary>Verifier infrastructure fault after successful creation — QuarantineRequired (created resource, unverified post-condition).</summary>
    public const string VerificationInfraFault = "spe-verification-infra-fault";

    /// <summary>KV write of the real container-type id failed after successful creation + verification — QuarantineRequired (external resource exists, not persisted).</summary>
    public const string KvWriteFailed = "spe-kv-write-failed";

    /// <summary>KV writer infrastructure fault after successful creation + verification — QuarantineRequired.</summary>
    public const string KvWriteInfraFault = "spe-kv-write-infra-fault";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "spe-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H8 was in flight.</summary>
    public const string RunDeletedDuringProvisioning = "spe-run-deleted-during-provisioning";

    /// <summary>
    /// The container-type POST got no authoritative answer (a client timeout, a dropped connection, a 2xx without an
    /// id): a container type may exist that the run does not name — and a container type cannot be deleted and is
    /// capped per tenant. QuarantineRequired; H8 creates NO type until an operator has checked with a delegated
    /// SharePoint Embedded admin token, recorded the type in the run if one exists, and cleared the quarantine
    /// (unified-access-control-r2 task 165, owner round 49 item 2).
    /// </summary>
    public const string ContainerTypeCreationInDoubt = "spe-container-type-creation-in-doubt";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H8. Kept as string constants so grep across the codebase finds every
/// read/write of the same gate name (design.md §6.2).
/// </summary>
public static class SpeContainerTypeGates
{
    /// <summary>
    /// The gate H8 owns for the T6 post-creation app-only verification.
    /// Flipped to <c>Verified</c> once <see cref="ISpeContainerVerifier"/>
    /// confirms the root container is readable via a fresh app-only token.
    /// </summary>
    public const string T6Verified = "h8-t6-verified";
}
