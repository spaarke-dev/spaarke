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
    /// Run parameter <c>indexVer</c> missing — idempotency key requires the
    /// manifest hash of the schema JSONs in <c>scripts/ai-search/</c>.
    /// </summary>
    public const string MissingIndexVersion = "missing-index-version";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// AI Search endpoint could not be resolved — for Model 2, H2a must have
    /// populated <c>ProvisioningRun.InterStepState.AiSearchEndpoint</c>; for
    /// Model 1, <c>AiSearchIndexOptions.SharedPlatformSearchEndpoint</c> must
    /// be configured.
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
    /// Model 2 provisioner reported a hard failure (script exit non-zero /
    /// REST PUT 4xx-5xx). Partial index creation possible per §4C — treated
    /// as Quarantine-required so the operator can inspect the shared/dedicated
    /// AI Search state before downstream handlers depend on the indexes.
    /// </summary>
    public const string IndexProvisioningFailed = "index-provisioning-failed";

    /// <summary>
    /// Post-deploy invariant verifier flagged a violation (required filterable
    /// field missing, required vector field missing/wrong dim, forbidden
    /// field present, semantic-config-field mismatch). Diagnostic names the
    /// failing index + field.
    /// </summary>
    public const string IndexInvariantViolation = "index-invariant-violation";

    /// <summary>
    /// Model 1 branch: one or more of the 7 canonical indexes is NOT deployed
    /// on the shared platform AI Search service — H2b MUST NOT re-create them
    /// (Model 1 shares the platform indexes). The platform is under-provisioned
    /// — operator must run <c>Deploy-AllIndexes.ps1</c> against the shared
    /// service before onboarding this Model 1 tenant.
    /// </summary>
    public const string SharedIndexMissing = "shared-index-missing";

    /// <summary>
    /// Model 1 branch: tenant-filter query template provisioner failed. The
    /// PUT is idempotent (§4D I2 template store contract) so a re-run is
    /// safe — Resumable classification per §4C.
    /// </summary>
    public const string TenantFilterTemplateProvisionFailed = "tenant-filter-template-provision-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H2b was in flight.</summary>
    public const string RunDeletedDuringProvision = "run-deleted-during-provision";
}
