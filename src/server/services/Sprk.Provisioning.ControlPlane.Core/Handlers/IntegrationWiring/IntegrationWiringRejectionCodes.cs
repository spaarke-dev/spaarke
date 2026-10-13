// -----------------------------------------------------------------------------
// IntegrationWiringRejectionCodes.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H14IntegrationWiringHandler + its H14a sub-handler (task 073, wave C4 Batch
// 3F; H14b/H14c removed under ISS-019 / #1560). T4 silent-fail trap owner (Exchange role-assignment drift — RBAC for Applications since task 251).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance) + FR-33 (T4 silent-fail trap).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row
//     + §4B T4 trap catalog + §4C rollback taxonomy.
//
// STABILITY: codes are string constants used by external tools. Do NOT rename;
// add new codes as needed and mark old ones [Obsolete] on removal.
//
// PATTERN PARITY: mirrors Handlers/DataverseAppUserGraphParity/H10Rejections.cs
// and Handlers/KvSecretsPopulation/KvSecretsPopulationRejectionCodes.cs — one
// const per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Machine-stable rejection codes for <see cref="H14IntegrationWiringHandler"/>
/// (parent) failures. Sub-handler-specific codes live in
/// <see cref="H14aRejections"/>.
/// </summary>
public static class H14Rejections
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h14-run-not-found";

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "h14-missing-tenant-id";

    /// <summary>
    /// Retired by task 251 (H14a grants only the stamp identity) — kept so a run record carrying it still
    /// reads. InterStepState.bffAppRegId missing.
    /// </summary>
    public const string MissingBffAppRegId = "h14-missing-bff-appreg-id";

    /// <summary>InterStepState.miClientId missing — H2a (uami.bicep) has not completed yet (upstream dependency for H14a).</summary>
    public const string MissingUamiClientId = "h14-missing-uami-client-id";

    /// <summary>InterStepState.miObjectId missing — H2a (uami.bicep) has not completed yet (H14a registers the identity in Exchange by it).</summary>
    public const string MissingUamiObjectId = "h14-missing-uami-object-id";

    /// <summary>
    /// Aggregate failure — a sub-step
    /// failed. The Diagnostic enumerates every failing sub-step's own code +
    /// message so the operator sees the full picture in one place rather
    /// than fail-fast on the first observed failure.
    /// </summary>
    public const string SubStepFailed = "h14-substep-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h14-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H14 was in flight.</summary>
    public const string RunDeletedDuringWiring = "h14-run-deleted-during-wiring";
}

/// <summary>Machine-stable rejection codes for H14a (Exchange mailbox access — RBAC for Applications).</summary>
public static class H14aRejections
{
    /// <summary>Run parameter <c>exchangePolicyScopeGroupId</c> missing — the customer's mail-enabled security group H14a scopes the Exchange roles to.</summary>
    public const string MissingPolicyScopeGroupId = "h14a-missing-policy-scope-group-id";

    /// <summary>
    /// T4 SILENT-FAIL TRAP (spec.md FR-33): the stamp identity's Exchange role assignments differ
    /// from the expected group-scoped set (a named assignment with another role / app / scope, or
    /// one of these roles held under another name). Quarantine-required — H14a MUST NOT silently
    /// overwrite; the diagnostic lists every conflict.
    /// </summary>
    public const string TrapT4Drift = "h14a-trap-T4-drift";

    /// <summary>
    /// No conclusive Applied/Drift outcome (Exchange sign-in, sidecar, throttle or transport
    /// failure). Resumable — the apply is get-before-set, so a re-run creates only what is missing.
    /// </summary>
    public const string ApplyFailed = "h14a-apply-failed";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H14 + its sub-handlers. Kept as string constants so grep across the
/// codebase finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class H14Gates
{
    /// <summary>T4 gate — flips to Verified once H14a has read back every group-scoped mailbox role with no drift. (Value kept for run-record compatibility.)</summary>
    public const string ExchangePolicyApplied = "h14a-exchange-policy-applied";
}
