// -----------------------------------------------------------------------------
// H10Rejections.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H10DataverseAppUserGraphParityHandler (task 053, wave C4). Every distinct
// failure mode gets its own code so the reconciler + operator UI can branch on
// the exact reason WITHOUT string-matching the human-readable Diagnostic.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-13 + FR-33
//     (T2 + T3 silent-fail traps) + H10 escalation gate MUST rule.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H10 row
//     + §4B T2/T3 trap catalog + §4C rollback taxonomy.
//
// STABILITY: codes are string constants used by external tools. Do NOT rename;
// add new codes as needed and mark old ones [Obsolete] on removal.
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegRejectionCodes.cs and
// Handlers/KvSecretsPopulation/KvSecretsPopulationRejectionCodes.cs — one const
// per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Machine-stable rejection codes for <see cref="H10DataverseAppUserGraphParityHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class H10Rejections
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h10-run-not-found";

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "h10-missing-tenant-id";

    /// <summary>InterStepState.bffAppRegId missing — H3 has not completed yet (upstream dependency).</summary>
    public const string MissingBffAppRegId = "h10-missing-bff-appreg-id";

    /// <summary>InterStepState.miClientId missing — H2a (uami.bicep) has not completed yet.</summary>
    public const string MissingUamiClientId = "h10-missing-uami-client-id";

    /// <summary>InterStepState.miObjectId missing — H2a (uami.bicep) has not completed yet.</summary>
    public const string MissingUamiObjectId = "h10-missing-uami-object-id";

    /// <summary>InterStepState.dataverseEnvUrl missing — H5/H6 has not completed yet.</summary>
    public const string MissingDataverseEnvUrl = "h10-missing-dataverse-env-url";

    /// <summary>
    /// H10 ESCALATION GATE (spec.md MUST rule, BINDING): one or more
    /// <c>GraphAppRoles.cs</c>-mirrored entries have a null/empty AppRoleId,
    /// meaning Phase A GUID completion (task 005) is incomplete. Fires BEFORE
    /// any Graph or Dataverse write. NEVER emitted after Phase A completion
    /// (all 14 GUIDs populated 2026-08-17) — kept as a forcing-function per
    /// task 005/067 nightly-parity intent.
    /// </summary>
    public const string EscalationGateNullAppRoleId = "h10-escalation-gate-null-approleid";

    /// <summary>
    /// Dataverse App User registration failed for the BFF app-registration
    /// applicationId. Resumable — App User upsert is idempotent; operator
    /// resolves Dataverse connectivity/RBAC and resumes.
    /// </summary>
    public const string BffAppUserCreationFailed = "h10-bff-app-user-creation-failed";

    /// <summary>
    /// Dataverse App User registration failed for the UAMI applicationId.
    /// Resumable — same rationale as <see cref="BffAppUserCreationFailed"/>.
    /// </summary>
    public const string UamiAppUserCreationFailed = "h10-uami-app-user-creation-failed";

    /// <summary>
    /// T2 SILENT-FAIL TRAP (spec.md FR-33): post-registration re-query of
    /// <c>systemusers?$filter=applicationid eq {uami-app-id}</c> did NOT
    /// return count == 1. Quarantine-required — the registration reported
    /// success but the independent post-condition re-query disagrees; this is
    /// exactly the silent-fail shape T2 exists to catch (parity with H4's T1
    /// ARM-read-mismatch classification).
    /// </summary>
    public const string TrapT2VerificationFailed = "h10-trap-T2-verification-failed";

    /// <summary>
    /// One or more Graph app-role grant calls failed. RetryableWithCleanup —
    /// grants are individually idempotent (re-POSTing an existing assignment
    /// is a safe no-op per Grant-GraphAppRoles.ps1's delta-apply design); a
    /// full re-run of the grant loop safely completes the partial state.
    /// </summary>
    public const string GraphRoleGrantFailed = "h10-graph-role-grant-failed";

    /// <summary>
    /// T3 SILENT-FAIL TRAP (spec.md FR-33): post-grant re-query of the UAMI
    /// SP's <c>appRoleAssignments</c> is still missing one or more of the 14
    /// expected role IDs despite the grant loop reporting success.
    /// Quarantine-required — per the POML escalation trigger, this shape
    /// (grants "succeeded" but parity re-query disagrees) most often means a
    /// GraphAppRoles.cs GUID value is WRONG (not just null), which requires
    /// operator diagnosis via live `az ad sp show` re-enumeration — NOT a
    /// blind retry.
    /// </summary>
    public const string TrapT3VerificationFailed = "h10-trap-T3-verification-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h10-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H10 was in flight.</summary>
    public const string RunDeletedDuringProvisioning = "h10-run-deleted-during-provisioning";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H10. Kept as string constants so grep across the codebase finds every
/// read/write of the same gate name (design.md §6.2). <c>app-user-created</c>
/// is the example gate id cited directly in <see cref="Models.GateEntry"/>'s
/// doc comment.
/// </summary>
public static class H10Gates
{
    /// <summary>
    /// T2 gate — flips to Verified once the UAMI Dataverse App User is
    /// confirmed present via the independent post-registration re-query.
    /// </summary>
    public const string AppUserCreated = "app-user-created";

    /// <summary>
    /// T3 gate — flips to Verified once all 14 Graph app-role assignments
    /// are confirmed present on the UAMI service principal via the
    /// independent post-grant re-query.
    /// </summary>
    public const string GraphRoleParity = "graph-role-parity";
}
