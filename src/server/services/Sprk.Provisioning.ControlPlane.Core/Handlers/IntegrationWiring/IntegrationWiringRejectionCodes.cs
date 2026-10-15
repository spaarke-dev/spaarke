// -----------------------------------------------------------------------------
// IntegrationWiringRejectionCodes.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H14IntegrationWiringHandler + its H14a sub-handler (task 073, wave C4 Batch
// 3F; H14b/H14c removed under ISS-019 / #1560) and H14m (customer mailbox, task 263). T4 silent-fail trap owner (Exchange role-assignment drift — RBAC for Applications since task 251).
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
    /// InterStepState.bffAppRegId missing — H3 has not completed. Retired by task 251 and used again by task 263: H14m
    /// writes the stamp's sprk_communicationaccount row as the BFF app registration (the H7/H7b identity).
    /// </summary>
    public const string MissingBffAppRegId = "h14-missing-bff-appreg-id";

    /// <summary>InterStepState.dataverseEnvUrl missing — H5 has not completed (H14m writes the account row there).</summary>
    public const string MissingDataverseEnvUrl = "h14-missing-dataverse-env-url";

    /// <summary>Run parameter <c>displayName</c> missing (H14m: the shared mailbox's display name).</summary>
    public const string MissingDisplayName = "h14-missing-display-name";

    /// <summary>Run parameter <c>communicationDefaultMailbox</c> missing (H14m: the shared mailbox's address).</summary>
    public const string MissingMailboxAddress = "h14-missing-mailbox-address";

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

/// <summary>Machine-stable rejection codes for H14m (the customer's shared mailbox — task 263).</summary>
public static class H14mRejections
{
    /// <summary>H14m's ParametersJson could not be read or misses a field (a parent bug) — Resumable.</summary>
    public const string InvalidParameters = "h14m-invalid-parameters";

    /// <summary>
    /// A recipient with the mailbox's name, alias or address exists and is not this customer's shared mailbox in its
    /// scope group only (a foreign or out-of-scope mailbox), or the scope group is not Spaarke-AppAccess-{customerId}.
    /// QuarantineRequired — nothing was created or changed.
    /// </summary>
    public const string MailboxDrift = "h14m-mailbox-drift";

    /// <summary>No conclusive ensure (sign-in, sidecar, Exchange, PRQ-E-16 not applied) — Resumable; nothing half-made by a missing permission.</summary>
    public const string MailboxEnsureFailed = "h14m-mailbox-ensure-failed";

    /// <summary>The mailbox is in place but Exchange does not report every stamp mail role in scope yet — Resumable; no row written.</summary>
    public const string MailboxUnverified = "h14m-mailbox-unverified";

    /// <summary>The stamp's sprk_communicationaccount rows for the address cannot be adopted (inactive, another type, several) — QuarantineRequired.</summary>
    public const string AccountRowConflict = "h14m-account-row-conflict";

    /// <summary>The row's verification is Failed (the BFF's own Graph test) — never overwritten; Resumable after a re-verify in the app.</summary>
    public const string AccountVerificationFailed = "h14m-account-verification-failed";

    /// <summary>Writing or reading the row failed (auth, HTTP, Dataverse) — Resumable.</summary>
    public const string AccountRowFailed = "h14m-account-row-failed";
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

    /// <summary>Flips to Verified once H14m has the customer's shared mailbox in scope and its verified account row (task 263).</summary>
    public const string CustomerMailboxVerified = "h14m-customer-mailbox-verified";
}
