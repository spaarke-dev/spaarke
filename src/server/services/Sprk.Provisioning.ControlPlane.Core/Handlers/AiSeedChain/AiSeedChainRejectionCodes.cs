// -----------------------------------------------------------------------------
// AiSeedChainRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H12aAiSeedChainHandler (task 070).
// Every distinct failure mode gets its own code so the reconciler + operator UI
// can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity). Parity with the H2a codes
// pattern (BicepDeployRejectionCodes).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-15 (H12a):
//       AI seed chain acceptance — all seed rows present, no duplicates,
//       playbook consumers resolve to shipped playbooks. Terminates at
//       playbook consumers per ADR-039 (single AI routing surface).
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Partial seed = QuarantineRequired (some rows exist, FK broken).
//       Pre-write validation failures = Resumable (operator fixes params +
//       resumes).
//   - ADR-039 amendment 2026-07-05: no new capability on the frozen node-graph
//       engine; retired-artifact enforcement is a HARD gate at manifest-read
//       time.
//   - task 069 manifest.yaml: authoritative source; retired-artifact list
//       enumerates spaarke-playbook-embeddings + multinode + dispatcher.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones [Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Machine-stable rejection codes for <see cref="H12aAiSeedChainHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class AiSeedChainRejectionCodes
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).
    /// Handler MUST NOT fall back to a default tenant when acquiring auth to
    /// the target customer's Dataverse env.
    /// </summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>
    /// Target Dataverse environment URL missing. Populated by upstream H5/H6
    /// into <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.DataverseEnvUrl"/>;
    /// absence means the customer's Dataverse env hasn't been created yet and
    /// H12a cannot proceed. POML acceptance criterion 6: handler MUST NOT
    /// invoke the seeder script in this case.
    /// </summary>
    public const string MissingDataverseUrl = "missing-dataverse-url";

    /// <summary>
    /// Manifest.yaml not present on disk at the configured path. Configuration
    /// / deployment error — verify <c>scripts/seed-data/</c> ships in the L2
    /// publish output (parity with NFR-05 fail-fast rule).
    /// </summary>
    public const string ManifestNotFound = "manifest-not-found";

    /// <summary>
    /// Startup defense-in-depth: manifest.yaml body references a retired
    /// artifact (<c>spaarke-playbook-embeddings</c>, <c>dispatcher</c>, or the
    /// multi-node target-state playbook file). Task 069's Invoke-SeedManifest.ps1
    /// enforces this at the ORCHESTRATOR layer via <c>retiredArtifacts</c>
    /// scan; the handler re-runs the check so a hand-edited manifest that
    /// bypasses the generator ALSO fails loudly (spec.md FR-15 + ADR-039
    /// amendment 2026-07-05 — "MUST NOT land new capability on the frozen
    /// node-graph engine").
    /// </summary>
    public const string ManifestContainsRetiredArtifact = "manifest-contains-retired-artifact";

    /// <summary>
    /// The invoked Invoke-SeedManifest.ps1 orchestrator reported a hard
    /// failure (non-zero exit — includes retired-check violations detected at
    /// the orchestrator layer, per-seeder failures, or infrastructure faults
    /// like missing az login). Partial seed state may exist in the target
    /// Dataverse env; §4C classification is Quarantine-required (some rows
    /// exist with broken FK).
    /// </summary>
    public const string SeedManifestInvocationFailed = "seed-manifest-invocation-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H12a was in flight.</summary>
    public const string RunDeletedDuringSeed = "run-deleted-during-seed";
}
