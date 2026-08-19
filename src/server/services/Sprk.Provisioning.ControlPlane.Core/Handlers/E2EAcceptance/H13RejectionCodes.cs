// -----------------------------------------------------------------------------
// H13RejectionCodes.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H13E2EAcceptanceGateHandler (task 055, wave C4 Batch 4E). H13 is the FINAL
// acceptance gate — it re-verifies EVERY T1–T6 silent-fail trap + EVERY
// I1–I5 tenant-isolation invariant + naming conformance + cost envelope, and
// gates the Dataverse registry `sprk_setupstatus → Ready` transition on the
// aggregate pass/fail outcome.
//
// SPEC / DESIGN references:
//   - spec.md FR-18 (H13 acceptance) + SC #5 (extended validate script) +
//     SC #6 (traps re-verified) + SC #17 (naming exit 0) + §15 #14 (cost).
//   - design.md §4.1 H13 row + §4B (T1–T6 trap catalog) + §4C (Quarantined
//     semantics) + §4D (I1–I5 tenant-isolation invariants).
//
// STABILITY: codes are string constants used by external tools. Do NOT rename;
// add new codes as needed and mark old ones [Obsolete] on removal.
//
// PATTERN PARITY: mirrors Handlers/BffDeploy/BffDeployRejectionCodes.cs and
// Handlers/IntegrationWiring/IntegrationWiringRejectionCodes.cs — one const
// per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Machine-stable rejection codes for <see cref="H13E2EAcceptanceGateHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class H13Rejections
{
    // ---- parameter guards (§4D I1 / ADR-027 / project idempotency contract) ----

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant / POML acceptance criterion "Negative: H13 invoked without tenantId parameter returns failure").</summary>
    public const string MissingTenantId = "h13-missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing — H13 needs it for cost-envelope + ARM trap probes (ADR-027 D4).</summary>
    public const string MissingSubscriptionId = "h13-missing-subscription-id";

    /// <summary>Run parameter <c>buildId</c> missing — idempotency key <c>validate-{customerId}-{buildId}</c> requires the BFF CI build number.</summary>
    public const string MissingBuildId = "h13-missing-build-id";

    /// <summary>Run parameter <c>customerId</c>-scoped Dataverse env URL missing — no target to sample-query.</summary>
    public const string MissingDataverseEnvUrl = "h13-missing-dataverse-env-url";

    /// <summary>Run parameter <c>bffApiUrl</c> (or resolvable BFF App Service URL) missing — no target to sample the /healthz + E2E round-trip.</summary>
    public const string MissingBffApiUrl = "h13-missing-bff-api-url";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h13-run-not-found";

    // ---- extended Validate-DeployedEnvironment.ps1 (SC #5) ----

    /// <summary>
    /// The extended Validate-DeployedEnvironment.ps1 (Phase B) reported at
    /// least one sample-check failure — BFF /healthz OR sample analysis OR
    /// sample doc upload+index OR workspace-layout render OR wizard field-map.
    /// R7 EFFECT assertion failed (not intent assertion).
    /// </summary>
    public const string ExtendedValidationFailed = "h13-extended-validation-failed";

    /// <summary>The Validate-DeployedEnvironment.ps1 invocation threw (pwsh not on PATH, script not found, timeout) — no confirmed E2E outcome.</summary>
    public const string ExtendedValidationInfraFault = "h13-extended-validation-infra-fault";

    // ---- §4B silent-fail trap failures (SC #6 — one code per trap) ----

    /// <summary>T1 SILENT-FAIL TRAP — App Service <c>keyVaultReferenceIdentity</c> is NOT the customer UAMI (or drifted to system-assigned/none/orphaned RID). BFF cannot resolve Tier-1 KV refs at boot.</summary>
    public const string TrapT1Failed = "h13-trap-T1-key-vault-reference-identity";

    /// <summary>T2 SILENT-FAIL TRAP — expected Dataverse App Users are NOT present (Web API systemusers filter did not return the expected UAMI + BFF app-reg pair).</summary>
    public const string TrapT2Failed = "h13-trap-T2-dataverse-app-user";

    /// <summary>T3 SILENT-FAIL TRAP — UAMI service principal's Graph appRoleAssignments do NOT match the full <c>GraphAppRoles.cs</c> catalog (14 roles).</summary>
    public const string TrapT3Failed = "h13-trap-T3-graph-app-role-parity";

    /// <summary>T4 SILENT-FAIL TRAP — Exchange <c>Get-ApplicationAccessPolicy</c> does NOT return the expected 2 entries (BFF app-reg + UAMI) matching the run's expected AppIds.</summary>
    public const string TrapT4Failed = "h13-trap-T4-exchange-policy-count";

    /// <summary>T5 SILENT-FAIL TRAP — Neither both-slot System-Assigned MI KV RBAC NOR post-Phase-C UAMI-only structural state is present on the App Service.</summary>
    public const string TrapT5Failed = "h13-trap-T5-slot-mi-kv-rbac";

    /// <summary>T6 SILENT-FAIL TRAP — the SPE container-type creation audit reveals a delegated-token creation path (public client) rather than the required app-only confidential-client one.</summary>
    public const string TrapT6Failed = "h13-trap-T6-spe-confidential-client";

    /// <summary>Trap verifier infra fault (Graph/ARM/Dataverse REST/az CLI blew up) — no confirmed pass/fail outcome. Resumable.</summary>
    public const string TrapVerifierInfraFault = "h13-trap-verifier-infra-fault";

    // ---- §4D tenant-isolation invariant failures (one code per invariant) ----

    /// <summary>I1 CATASTROPHIC — a hardcoded tenant-shaped GUID default was found in the r1-owned provisioning scripts (grep gate).</summary>
    public const string InvariantI1Failed = "h13-invariant-I1-hardcoded-tenant";

    /// <summary>I2 CATASTROPHIC — a sample AI Search query per index does NOT carry the required unconditional <c>tenantId eq</c> filter.</summary>
    public const string InvariantI2Failed = "h13-invariant-I2-ai-search-tenant-filter";

    /// <summary>I3 CATASTROPHIC — a sample Cosmos query does NOT carry the required partition-key predicate.</summary>
    public const string InvariantI3Failed = "h13-invariant-I3-cosmos-partition-key";

    /// <summary>I4 CATASTROPHIC — SPE container-ID resolution does NOT flow through <c>ITenantContainerResolver</c> (direct hard-coded id observed).</summary>
    public const string InvariantI4Failed = "h13-invariant-I4-spe-container-resolver";

    /// <summary>I5 CATASTROPHIC — Graph token acquisition is NOT per-tenant scoped (ambient default-tenant credential detected in the sample).</summary>
    public const string InvariantI5Failed = "h13-invariant-I5-graph-token-tenant";

    /// <summary>Invariant verifier infra fault (probe blew up) — no confirmed pass/fail outcome. Resumable.</summary>
    public const string InvariantVerifierInfraFault = "h13-invariant-verifier-infra-fault";

    // ---- naming-conformance (SC #17) ----

    /// <summary>The independent invocation of scripts/naming-conformance-check.ps1 returned non-zero — r1-owned surface has a naming violation post-provisioning.</summary>
    public const string NamingConformanceFailed = "h13-naming-conformance-failed";

    /// <summary>The naming-conformance script invocation threw (pwsh not on PATH, script not found, timeout).</summary>
    public const string NamingConformanceInfraFault = "h13-naming-conformance-infra-fault";

    // ---- cost envelope (SC #14 + §15 #14) ----

    /// <summary>
    /// Cost envelope drift exceeds <see cref="H13AcceptanceOptions.CostDriftAdvisoryThreshold"/>
    /// AND <see cref="H13AcceptanceOptions.CostDriftFailsRun"/> is true. Per
    /// project deviation note: default posture is advisory-warn, NOT fail; this
    /// code is only emitted when the operator has explicitly opted in.
    /// </summary>
    public const string CostDriftExceeded = "h13-cost-drift-exceeded";

    /// <summary>Cost query infra fault (Cost Management API / az CLI) — no confirmed cost outcome. Resumable.</summary>
    public const string CostQueryInfraFault = "h13-cost-query-infra-fault";

    // ---- registry transition ----

    /// <summary>The Dataverse registry Setup Status transition to <c>Ready</c> failed (Web API 4xx/5xx or optimistic-concurrency conflict).</summary>
    public const string RegistryUpdateFailed = "h13-registry-update-failed";

    // ---- concurrency / row lifecycle ----

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h13-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H13 was in flight.</summary>
    public const string RunDeletedDuringAcceptance = "h13-run-deleted-during-acceptance";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H13. Kept as string constants so grep across the codebase finds every
/// read/write of the same gate name (design.md §6.2).
/// </summary>
public static class H13Gates
{
    /// <summary>Flips to Verified when the extended Validate-DeployedEnvironment.ps1 exits 0 (SC #5).</summary>
    public const string ExtendedValidationVerified = "h13-extended-validation";

    /// <summary>Flips to Verified when ALL 6 §4B T1–T6 trap re-verifications pass (SC #6).</summary>
    public const string TrapCatalogVerified = "h13-trap-catalog";

    /// <summary>Flips to Verified when ALL 5 §4D I1–I5 sample invariants pass.</summary>
    public const string InvariantCatalogVerified = "h13-invariant-catalog";

    /// <summary>Flips to Verified when the independent naming-conformance run exits 0 (SC #17).</summary>
    public const string NamingConformanceVerified = "h13-naming-conformance";

    /// <summary>Flips to Verified when the cost envelope query is within tolerance (§15 #14).</summary>
    public const string CostEnvelopeVerified = "h13-cost-envelope";

    /// <summary>Flips to Verified once Dataverse registry <c>sprk_setupstatus</c> is transitioned to <c>Ready</c>.</summary>
    public const string RegistryReadyTransitioned = "h13-registry-ready";
}
