// -----------------------------------------------------------------------------
// BffDeployRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H9BffDeployHandler (task 052).
// Every distinct failure mode gets its own code so the reconciler + operator
// UI can branch on the exact reason WITHOUT string-matching the human-readable
// Diagnostic (which may be reworded for clarity).
//
// SPEC / DESIGN references:
//   - spec.md FR-12 (H9 acceptance): blue-green slot swap + rollback on
//     smoke-test failure + r3-era gates block swap.
//   - spec.md NFR-01: publish-size ≤60 MB ceiling; ≥+5 MB per-task delta =
//     explicit justification required.
//   - spec.md NFR-05: post-deploy /health returns 200 (BFF ValidateOnStart
//     r3 task 061 enforces Tier-1 KV-ref resolution at startup).
//   - spec.md §5.5 r3-era gates: analyzers-as-errors (r3 task 041), god-class
//     ratchet (r3 task 040 — <see cref="tests/Spaarke.ArchTests/GodClassGuardTests.cs"/>),
//     4 new ArchTests (r1 task 064), naming-conformance (r3 task 063),
//     Graph app-role parity (r3 task 062 / r1 task 067). Distinct rejection
//     code per failing gate (POML criterion 2).
//   - spec.md §4D I1 / FR-28: NO hardcoded default tenant in provisioning
//     scripts (Gap 2 assertion — POML criterion 5 requires
//     <see cref="Spaarkedev1HardcodeDetected"/> when the regression is
//     detected in Deploy-Release.ps1).
//   - design.md §4C rollback taxonomy — mapping from rejection code to
//     FailureClass is inline in H9BffDeployHandler file header.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Machine-stable rejection codes for <see cref="H9BffDeployHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class BffDeployRejectionCodes
{
    // ---- parameter guards (§4D I1 / ADR-027 / project idempotency contract) ----

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>Run parameter <c>subscriptionId</c> missing — H9 MUST know the target subscription (ADR-027 D4).</summary>
    public const string MissingSubscriptionId = "missing-subscription-id";

    /// <summary>Run parameter <c>resourceGroupName</c> missing — H9 needs the App Service resource group for slot-swap ARM ops.</summary>
    public const string MissingResourceGroupName = "missing-resource-group-name";

    /// <summary>Run parameter <c>appServiceName</c> missing — H9 needs the BFF App Service name for deploy + slot-swap targeting.</summary>
    public const string MissingAppServiceName = "missing-app-service-name";

    /// <summary>Run parameter <c>buildId</c> missing — idempotency key requires the BFF CI build number (deterministic per §4.1 preamble).</summary>
    public const string MissingBuildId = "missing-build-id";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    // ---- Gap 2 assertion (§4D I1 / FR-28 — Deploy-Release.ps1 Phase 4) ----

    /// <summary>
    /// Deploy-Release.ps1 (or Deploy-BffApi.ps1) contains a hardcoded
    /// <c>spaarkedev1</c> literal — Phase B hardening (task 013) has been
    /// regressed. Per POML criterion 5 + spec.md §4D I1 / FR-28: NO hardcoded
    /// default tenant in provisioning scripts. Fails BEFORE any external side
    /// effect so a bad script cannot deploy.
    /// </summary>
    public const string Spaarkedev1HardcodeDetected = "spaarkedev1-hardcode-detected";

    // ---- task 132 (Wave G-3, DS-4 §5 re-scope) — artifact manifest / download / Kudu deploy ----

    /// <summary>
    /// <see cref="IArtifactManifestVerifier"/> hard-blocked the
    /// deploy — missing manifest, missing gate key, a RED gate, or a
    /// requested buildId that does not match the manifest's buildId. Slot-swap
    /// BLOCKED per DS-4 §5 (not a warning-and-proceed condition).
    /// </summary>
    public const string ArtifactManifestRejected = "artifact-manifest-rejected";

    /// <summary>
    /// The manifest verifier's blob-container call threw (unreachable, auth
    /// failure) BEFORE any external side effect against the customer's App
    /// Service — Resumable.
    /// </summary>
    public const string ManifestVerifierInfraFault = "manifest-verifier-infra-fault";

    /// <summary>
    /// <see cref="IBffArtifactDownloader"/> reported a domain
    /// failure (artifact blob not found) — Resumable, no external side effect
    /// against the customer's App Service.
    /// </summary>
    public const string ArtifactDownloadFailed = "artifact-download-failed";

    /// <summary>The artifact downloader's blob-container call threw (unreachable, auth failure, disk I/O) — Resumable.</summary>
    public const string ArtifactDownloadInfraFault = "artifact-download-infra-fault";

    /// <summary>
    /// <see cref="IKuduZipDeployer"/> reported a non-2xx Kudu
    /// response — Resumable (staging is dedicated + re-deployable; production
    /// is untouched at this point in the flow).
    /// </summary>
    public const string KuduZipDeployFailed = "kudu-zip-deploy-failed";

    /// <summary>The Kudu zip-deploy HTTP call threw (DNS failure, timeout, local zip missing) — Resumable.</summary>
    public const string KuduZipDeployInfraFault = "kudu-zip-deploy-infra-fault";

    /// <summary>
    /// Post-deploy STAGING /health probe (via the existing, reused IHealthProbe)
    /// never returned 200 within the retry budget — Resumable (staging is
    /// dedicated; no production impact; the swap step is never reached).
    /// </summary>
    public const string StagingHealthCheckFailed = "staging-health-check-failed";

    // ---- r3-era gate failures (SUPERSEDED by task 132 — see ArtifactManifestRejected
    //      above; retained per this file's stability policy — "do NOT rename;
    //      mark old ones @[Obsolete] on removal" — no code path emits these
    //      anymore, but DotnetR3GateVerifier.cs / IR3GateVerifier.cs remain on
    //      disk unregistered, so the constants stay defined) ----

    /// <summary>
    /// r3-era analyzers-as-errors gate failed — <c>dotnet build</c> exited
    /// non-zero OR reported at least one warning (r3 task 041's zero-warning
    /// baseline). Slot-swap BLOCKED. Full build log written to run notes.
    /// </summary>
    public const string R3GateFailedAnalyzers = "r3-gate-failed-analyzers";

    /// <summary>
    /// r3-era god-class ratchet gate failed —
    /// <c>tests/Spaarke.ArchTests/GodClassGuardTests.cs</c> reported a new
    /// oversized server .cs file OR an existing waived file grew beyond its
    /// frozen LOC + grace (r3 task 040). Slot-swap BLOCKED.
    /// </summary>
    public const string R3GateFailedGodClassRatchet = "r3-gate-failed-god-class-ratchet";

    /// <summary>
    /// r3-era ArchTests gate failed — one or more of the 4 new tenant-
    /// isolation ArchTests (r1 task 064) reported a violation. Slot-swap
    /// BLOCKED.
    /// </summary>
    public const string R3GateFailedArchTests = "r3-gate-failed-arch-tests";

    /// <summary>
    /// r3-era naming-conformance gate failed —
    /// <c>scripts/naming-conformance-check.ps1</c> (r3 task 063) reported at
    /// least one KV secret or resource name violating the canonical
    /// convention. Slot-swap BLOCKED.
    /// </summary>
    public const string R3GateFailedNamingConformance = "r3-gate-failed-naming-conformance";

    /// <summary>
    /// r3-era Graph app-role parity gate failed — the compiled
    /// <c>GraphAppRoles.cs</c> catalog diverges from the tenant's live Graph
    /// grants (r3 task 062 / r1 task 067 nightly ArchTest). Slot-swap
    /// BLOCKED.
    /// </summary>
    public const string R3GateFailedGraphAppRoleParity = "r3-gate-failed-graph-app-role-parity";

    // ---- deploy runner failures (Deploy-BffApi.ps1) — SUPERSEDED by task 132's
    //      KuduZipDeployFailed/KuduZipDeployInfraFault/ArtifactDownloadFailed
    //      above; retained per this file's stability policy (DeployBffApiScriptRunner.cs
    //      remains on disk unregistered) ----

    /// <summary>The invoked <c>Deploy-BffApi.ps1</c> reported a hard failure (non-zero exit, build failure, slot deploy failure, staging health check failure).</summary>
    public const string BffDeployFailed = "bff-deploy-failed";

    /// <summary>The <c>Deploy-BffApi.ps1</c> invocation threw (pwsh not on PATH, script not found, timeout, IO fault) — no confirmed external side effect.</summary>
    public const string BffDeployInfraFault = "bff-deploy-infra-fault";

    /// <summary>
    /// <c>Deploy-BffApi.ps1</c> exited 0 but did NOT populate the deploy zip
    /// / publish directory / slot URL fields the H9 handler needs downstream.
    /// Configuration drift — operator must resolve.
    /// </summary>
    public const string BffDeployOutputsIncomplete = "bff-deploy-outputs-incomplete";

    // ---- publish-size (spec.md NFR-01) ----

    /// <summary>
    /// Compressed BFF publish size exceeds the NFR-01 hard ceiling (60 MB)
    /// OR the single-task delta exceeds the ≥+5 MB threshold — deploy
    /// REJECTED per spec.md NFR-01 with explicit justification requirement.
    /// </summary>
    public const string PublishSizeDeltaExceeded = "publish-size-delta-exceeded";

    /// <summary>
    /// Publish-size reporter infrastructure fault (zip not found, IO error,
    /// etc.) BEFORE the slot-swap — treated as Resumable so the operator can
    /// re-run once the publish artifact is discoverable.
    /// </summary>
    public const string PublishSizeReporterInfraFault = "publish-size-reporter-infra-fault";

    // ---- slot-swap failures ----

    /// <summary>The <c>az webapp deployment slot swap</c> ARM call returned non-zero exit / ARM error. Staging still has the new version; production unchanged.</summary>
    public const string SlotSwapFailed = "slot-swap-failed";

    /// <summary>The <c>az</c> CLI slot-swap invocation threw (az not on PATH, timeout, IO fault) — swap not attempted.</summary>
    public const string SlotSwapInfraFault = "slot-swap-infra-fault";

    // ---- smoke-test + rollback failures ----

    /// <summary>
    /// Post-swap production /health returned non-200 (NFR-05 — BFF's
    /// ValidateOnStart fails at startup on missing Tier-1 KV refs); handler
    /// re-swapped production back to prior slot; deploy FAILED but production
    /// is safe (rolled back). Operator inspects staging logs for root cause
    /// before retrying.
    /// </summary>
    public const string SmokeTestFailedRolledBack = "smoke-test-failed-rolled-back";

    /// <summary>
    /// Post-swap production /health failed AND the rollback re-swap ALSO
    /// failed — CATASTROPHIC. Both slots are in a bad state (spec.md POML
    /// escalation trigger 2). Operator MUST diagnose manually — do NOT
    /// retry.
    /// </summary>
    public const string SmokeTestFailedRollbackAlsoFailed = "smoke-test-failed-rollback-also-failed";

    /// <summary>The rollback re-swap invocation threw a raw infra exception (az CLI blew up mid-swap) — treated as CATASTROPHIC per POML escalation trigger 2.</summary>
    public const string RollbackInfraFault = "rollback-infra-fault";

    /// <summary>The post-swap production /health probe threw a raw infra exception (network fault, DNS timeout) — treated as smoke-test failure, triggers rollback.</summary>
    public const string SmokeTestInfraFault = "smoke-test-infra-fault";

    // ---- concurrency / row lifecycle ----

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H9 was in flight.</summary>
    public const string RunDeletedDuringDeploy = "run-deleted-during-deploy";
}
