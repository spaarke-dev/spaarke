// -----------------------------------------------------------------------------
// AiSearchIndexRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H2bAiSearchIndexHandler (task 045).
// Every distinct failure mode gets its own code so the reconciler + operator
// UI can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity — parity with
// BicepDeployRejectionCodes for H2a).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05 (H2b):
//       7 canonical AI Search indexes provisioned via
//       scripts/ai-search/Deploy-AllIndexes.ps1; per-index invariant verifier
//       passes (required filterable + vector + forbidden absent).
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I1 / FR-28): -TenantId mandatory; no hardcoded default tenant.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I2 / FR-29): unconditional tenantId eq filter on every AI Search
//       query — H2b's Model 1 template provisioner is the source-of-truth for
//       this invariant at provisioning time.
//   - projects/customer-provisioning-orchestration-r1/spec.md SC #10: retired
//       `spaarke-playbook-embeddings` (ADR-039) + archived `spaarke-knowledge-
//       index*` lineage MUST NOT be re-provisioned.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a Model 1
//       vs Model 2: H2b behaves differently per tenancy tier.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback
//       taxonomy — mapping from rejection code to FailureClass.
//   - projects/customer-provisioning-orchestration-r1/notes/
//       ai-search-catalog-audit-2026-08.md — canonical 7 + retired lineage.
//   - ADR-039: single AI routing surface; `spaarke-playbook-embeddings` RETIRED
//       (FR-P2-06).
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Machine-stable rejection codes for <see cref="H2bAiSearchIndexHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class AiSearchIndexRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>
    /// Task 245b: a requested index has no embedded schema, so H2b cannot compute the schema-set
    /// version (its idempotency key's <c>indexVer</c>) or apply it. Nothing has been applied — Resumable
    /// once the run's <c>requestedIndexes</c> intake value is corrected.
    /// </summary>
    public const string IndexSchemaUnavailable = "index-schema-unavailable";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// <c>ProvisioningRun.TenancyModel</c> is missing / whitespace / not a recognized
    /// <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel"/> member. Task 223 (D-12):
    /// retires the pre-D-12 silent default (blank → "Model2Dedicated") that let malformed rows fall
    /// into the Model 2 branch. Handler MUST reject rather than default.
    /// </summary>
    public const string InvalidTenancyModel = "invalid-tenancy-model";

    /// <summary>
    /// AI Search endpoint could not be resolved — H2a must have populated
    /// <c>ProvisioningRun.InterStepState.AiSearchEndpoint</c> (both tenancy
    /// models; task 225b).
    /// </summary>
    public const string MissingSearchEndpoint = "missing-search-endpoint";

    /// <summary>
    /// Requested index catalog contains a name from the retired / archived
    /// lineage (<c>spaarke-playbook-embeddings</c> per ADR-039 /
    /// <c>spaarke-knowledge-index*</c> per spec SC #10). Structural design-
    /// intent violation — quarantine + escalate rather than silently proceed.
    /// </summary>
    public const string RetiredIndexProvisioningForbidden = "retired-index-provisioning-forbidden";

    /// <summary>
    /// The index provisioner reported a hard failure (REST PUT 4xx-5xx).
    /// Partial index creation possible per §4C — treated as Quarantine-required
    /// so the operator can inspect the stamp's AI Search state before downstream
    /// handlers depend on the indexes.
    /// </summary>
    public const string IndexProvisioningFailed = "index-provisioning-failed";

    /// <summary>
    /// The stamp's AI Search service refused the L2 identity (HTTP 401/403) — on a new stamp, the Search Service
    /// Contributor / Search Index Data Reader assignments H2a just created have not taken effect yet (role
    /// propagation can take several minutes; the service is Entra-only since task 244). Resumable: wait, then
    /// <c>POST /api/runs/{id}/resume</c>. If it persists, check the two assignments on the search service.
    /// </summary>
    public const string SearchAccessDenied = "search-access-denied";

    /// <summary>
    /// Post-deploy invariant verifier flagged a violation (required filterable
    /// field missing, required vector field missing/wrong dim, forbidden
    /// field present, semantic-config-field mismatch). Diagnostic names the
    /// failing index + field.
    /// </summary>
    public const string IndexInvariantViolation = "index-invariant-violation";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H2b was in flight.</summary>
    public const string RunDeletedDuringProvision = "run-deleted-during-provision";
}
