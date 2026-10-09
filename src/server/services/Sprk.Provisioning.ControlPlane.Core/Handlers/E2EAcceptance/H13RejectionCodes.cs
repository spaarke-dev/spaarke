// -----------------------------------------------------------------------------
// H13RejectionCodes.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H13E2EAcceptanceGateHandler (task 055, wave C4 Batch 4E). H13 is the FINAL
// acceptance gate — it re-verifies EVERY T1–T7 silent-fail trap + the
// I2–I5 tenant-isolation invariants on the stamp + cost envelope (task 230a:
// I1 and naming conformance are build gates, not per-run checks), and
// gates the Dataverse registry `sprk_setupstatus → Ready` transition on the
// aggregate pass/fail outcome.
//
// SPEC / DESIGN references:
//   - spec.md FR-18 (H13 acceptance) + SC #5 (extended validate script) +
//     SC #6 (traps re-verified) + SC #17 (naming exit 0) + §15 #14 (cost).
//   - design.md §4.1 H13 row + §4B (T1–T7 trap catalog) + §4C (Quarantined
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

    // ---- upstream H2a outputs (InterStepState — task 245a, G25) ----

    /// <summary><c>InterStepState.resourceGroupName</c> missing — H2a (Bicep infra deploy) produces it; H13 scopes the cost-envelope query + T1/T5/T7 ARM trap probes to it. Resumable.</summary>
    public const string MissingResourceGroupName = "h13-missing-resource-group-name";

    /// <summary><c>InterStepState.appServiceName</c> missing — H2a (Bicep infra deploy) produces it; the T1/T5/T7 ARM trap probes inspect this App Service. Resumable.</summary>
    public const string MissingAppServiceName = "h13-missing-app-service-name";

    /// <summary><c>InterStepState.keyVaultName</c> (the CUSTOMER Key Vault) missing — H2a (Bicep infra deploy) produces it; the T5 trap probe checks slot-MI RBAC on this vault. Resumable.</summary>
    public const string MissingKeyVaultName = "h13-missing-key-vault-name";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h13-run-not-found";

    /// <summary>
    /// <c>run.TenancyModel</c> was null / whitespace / not a recognized <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel"/>.
    /// Task 223 (D-12): H13 cost-envelope selection requires a parsed tenancy value — retires the
    /// pre-D-12 silent `_`-arm fallback to <c>Model1SharedFloorEnvelopeUsd</c>. Resumable — the
    /// operator fixes the row's tenancyModel value + re-runs.
    /// </summary>
    public const string InvalidTenancyModel = "h13-invalid-tenancy-model";

    // ---- extended Validate-DeployedEnvironment.ps1 (SC #5) ----

    /// <summary>
    /// A live check against the deployed BFF failed — /healthz, /ping, the Dataverse CORS origin, or the keyless proof
    /// (task 230b: the L2 identity refused, or any stamp service not proved with the BFF's managed identity).
    /// </summary>
    public const string ExtendedValidationFailed = "h13-extended-validation-failed";

    /// <summary>The validation runner threw — no confirmed outcome.</summary>
    public const string ExtendedValidationInfraFault = "h13-extended-validation-infra-fault";

    /// <summary>
    /// Task 230b: no live check failed, but at least one reached no verdict (transport fault, timeout, throttling,
    /// a server error, or a stamp service the BFF could not reach). Resumable.
    /// </summary>
    public const string ExtendedValidationInconclusive = "h13-extended-validation-inconclusive";

    /// <summary>Task 230b: <c>InterStepState.bffAppRegId</c> (H3's output) is missing — the keyless proof's token audience.</summary>
    public const string MissingBffAppRegId = "h13-missing-bff-app-reg-id";

    // ---- Secure-record isolation census (task 260, ISS-014) ----

    /// <summary>
    /// The stamp BFF's isolation census reported <c>findings</c>: a principal reaches the Secure Record unit (role depth,
    /// a user in the unit, an owner-team member, the owner role held elsewhere) or the owner role misses a codified table.
    /// QuarantineRequired — the findings are in the diagnostic.
    /// </summary>
    public const string SecureIsolationNotIsolated = "h13-secure-isolation-not-isolated";

    /// <summary>
    /// The census was <c>inert</c>: the BFF finds no Secure Record unit (by its <c>SecureRecord:BusinessUnitName</c>), so
    /// it asserted nothing. H7b creates that unit before H13, so this is a broken stamp, not a fresh one —
    /// QuarantineRequired.
    /// </summary>
    public const string SecureIsolationInert = "h13-secure-isolation-inert";

    /// <summary>
    /// The census call failed with a verdict: the BFF refused the L2 identity (401/403, a token without the role), answered
    /// 500, or answered a shape or status this build does not know. QuarantineRequired (fail-closed, never a skip).
    /// </summary>
    public const string SecureIsolationCensusFailed = "h13-secure-isolation-census-failed";

    /// <summary>
    /// No verdict: the BFF could not read the census (<c>error</c>), or transport / timeout / throttling, or a BFF build
    /// without the route (404). Resumable.
    /// </summary>
    public const string SecureIsolationInconclusive = "h13-secure-isolation-inconclusive";

    // ---- Keyless stamp (task 230b, owner D13) ----

    /// <summary>
    /// ARM shows a stamp resource that accepts keys (local/shared-key auth enabled), a slot carrying a key setting, or an
    /// expected keyed resource absent. QuarantineRequired — the stamp is not keyless.
    /// </summary>
    public const string StampKeyAuthEnabled = "h13-stamp-key-auth-enabled";

    /// <summary>Task 230b: the ARM keyless check could not read the stamp (permission, transport, throttling). Resumable.</summary>
    public const string StampKeylessInfraFault = "h13-stamp-keyless-infra-fault";

    // ---- §4B silent-fail trap failures (SC #6 — one code per trap) ----

    /// <summary>T1 SILENT-FAIL TRAP — App Service <c>keyVaultReferenceIdentity</c> is NOT the customer UAMI (or drifted to system-assigned/none/orphaned RID). BFF cannot resolve Tier-1 KV refs at boot.</summary>
    public const string TrapT1Failed = "h13-trap-T1-key-vault-reference-identity";

    /// <summary>T2 SILENT-FAIL TRAP — expected Dataverse App Users are NOT present (Web API systemusers filter did not return the expected UAMI + BFF app-reg pair).</summary>
    public const string TrapT2Failed = "h13-trap-T2-dataverse-app-user";

    /// <summary>T3 SILENT-FAIL TRAP — UAMI service principal's Graph appRoleAssignments do NOT match the full <c>GraphAppRoles.cs</c> catalog (14 roles).</summary>
    public const string TrapT3Failed = "h13-trap-T3-graph-app-role-parity";

    /// <summary>T4 SILENT-FAIL TRAP — the stamp identity is missing a group-scoped Exchange mailbox role, or holds one outside the customer's group (RBAC for Applications, task 251). Value kept for run-record compatibility.</summary>
    public const string TrapT4Failed = "h13-trap-T4-exchange-policy-count";

    /// <summary>T5 SILENT-FAIL TRAP — Neither both-slot System-Assigned MI KV RBAC NOR post-Phase-C UAMI-only structural state is present on the App Service.</summary>
    public const string TrapT5Failed = "h13-trap-T5-slot-mi-kv-rbac";

    /// <summary>T6 SILENT-FAIL TRAP — the SPE container-type creation audit reveals a delegated-token creation path (public client) rather than the required app-only confidential-client one.</summary>
    public const string TrapT6Failed = "h13-trap-T6-spe-confidential-client";

    /// <summary>T7 SILENT-FAIL TRAP — a BFF slot lacks <c>Customer__Id</c>, has it blank, or carries a value other than the run's customerId (task 238, D-14).</summary>
    public const string TrapT7Failed = "h13-trap-T7-customer-identity";

    /// <summary>Trap verifier infra fault (Graph/ARM/Dataverse REST/az CLI blew up) — no confirmed pass/fail outcome. Resumable.</summary>
    public const string TrapVerifierInfraFault = "h13-trap-verifier-infra-fault";

    // ---- §4D tenant-isolation invariant failures (one code per invariant) ----
    // Task 230a: InvariantI1Failed DELETED — I1 (no hardcoded tenant) is a build-time property the
    // I1 ArchTest enforces; H13 has no runtime I1 probe (it scanned scripts the Worker publish never ships).

    /// <summary>I2 CATASTROPHIC — a sample AI Search query per index does NOT carry the required unconditional <c>tenantId eq</c> filter.</summary>
    public const string InvariantI2Failed = "h13-invariant-I2-ai-search-tenant-filter";

    /// <summary>I3 CATASTROPHIC — a sample Cosmos query does NOT carry the required partition-key predicate.</summary>
    public const string InvariantI3Failed = "h13-invariant-I3-cosmos-partition-key";

    /// <summary>I4 CATASTROPHIC — the deployed BFF is not configured with this run's container type and container (SpeContainerTenantDerivationInvariantProbe). The code string predates task 227f, which retired the resolver it names.</summary>
    public const string InvariantI4Failed = "h13-invariant-I4-spe-container-resolver";

    /// <summary>I5 CATASTROPHIC — Graph token acquisition is NOT per-tenant scoped (ambient default-tenant credential detected in the sample).</summary>
    public const string InvariantI5Failed = "h13-invariant-I5-graph-token-tenant";

    /// <summary>Invariant verifier infra fault (probe blew up) — no confirmed pass/fail outcome. Resumable.</summary>
    public const string InvariantVerifierInfraFault = "h13-invariant-verifier-infra-fault";

    // Task 230a: NamingConformanceFailed / NamingConformanceInfraFault DELETED with H13's naming step —
    // naming conformance (SC #17) lints repo files and runs once as a blocking CI step.

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
    /// <summary>Flips to Verified when every live check against the BFF passes, the keyless proof included (SC #5, task 230b).</summary>
    public const string ExtendedValidationVerified = "h13-extended-validation";

    /// <summary>Flips to Verified when ARM shows every keyed stamp resource keyless and no key setting on any slot (task 230b).</summary>
    public const string StampKeylessVerified = "h13-stamp-keyless";

    /// <summary>Flips to Verified when the stamp BFF's secure-record isolation census answers <c>isolated</c> (task 260).</summary>
    public const string SecureIsolationVerified = "h13-secure-isolation";

    /// <summary>Flips to Verified when ALL 7 §4B T1–T7 trap re-verifications pass (SC #6).</summary>
    public const string TrapCatalogVerified = "h13-trap-catalog";

    /// <summary>Flips to Verified when ALL 4 runtime §4D I2–I5 sample invariants pass (I1 is build-time — task 230a).</summary>
    public const string InvariantCatalogVerified = "h13-invariant-catalog";

    // Task 230a: NamingConformanceVerified DELETED with H13's naming step (now a blocking CI step).

    /// <summary>Flips to Verified when the cost envelope query is within tolerance (§15 #14).</summary>
    public const string CostEnvelopeVerified = "h13-cost-envelope";

    /// <summary>Flips to Verified once Dataverse registry <c>sprk_setupstatus</c> is transitioned to <c>Ready</c>.</summary>
    public const string RegistryReadyTransitioned = "h13-registry-ready";
}
