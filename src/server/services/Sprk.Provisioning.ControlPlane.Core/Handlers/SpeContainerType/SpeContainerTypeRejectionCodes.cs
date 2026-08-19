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
