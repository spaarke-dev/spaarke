// -----------------------------------------------------------------------------
// SolutionImportRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H6SolutionImportHandler (task 049,
// wave C4 Batch 3D). Every distinct failure mode gets its own code so the
// reconciler + operator UI can branch on the exact reason WITHOUT string-
// matching the human-readable Diagnostic. Parity with the H5 / H12a codes
// pattern.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-09 (H6):
//       Package Deployer import of the 8 authoritative solutions per §11.1a
//       with Tier 1 → Tier 2 → Tier 3 dependency ordering; upgrade mode
//       retires the holding solution via --stage-and-upgrade.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H6:
//       Idempotency key `solimport-{customerId}-{solutionVer}`; the PS script
//       Deploy-DataverseSolutions.ps1 is the authoritative solution-list
//       source ($SolutionImportOrder), NOT a hardcoded list in this handler.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Package Deployer partial-import is complex to unwind (holding
//       solutions may be left behind after --stage-and-upgrade fails mid-
//       flight) → QuarantineRequired. Auth / rate-limit / quota → Resumable.
//       Timeout → Resumable (partial state may or may not exist — err toward
//       Resumable so operator can inspect and escalate to quarantine if
//       partial state is confirmed via `pac solution list`).
//   - .claude/adr/ADR-004-job-contract.md: idempotency contract requires
//       stable rejection codes across retries.
//   - .claude/adr/ADR-039: single AI routing surface — retired dispatcher +
//       spaarke-playbook-embeddings must never be re-introduced via a
//       solution. A defense-in-depth catalog scan enforces this at the
//       handler layer even though the 8 authoritative solutions in the PS
//       script's $SolutionImportOrder do not include any retired artifact.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones [Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Machine-stable rejection codes for <see cref="H6SolutionImportHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class SolutionImportRejectionCodes
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
    /// Target Dataverse environment URL missing. Populated by upstream H5
    /// into <see cref="Models.InterStepState.DataverseEnvUrl"/>;
    /// absence means the customer's Dataverse env hasn't been created yet and
    /// H6 cannot proceed. Handler MUST NOT invoke the PS script in this case.
    /// </summary>
    public const string MissingDataverseUrl = "missing-dataverse-url";

    /// <summary>
    /// BFF Entra app registration id missing. Populated by upstream H3 into
    /// <see cref="Models.InterStepState.BffAppRegId"/>; the PS script's
    /// <c>pac auth create --applicationId</c> requires this to authenticate
    /// against the customer's Dataverse env.
    /// </summary>
    public const string MissingBffAppRegId = "missing-bff-app-reg-id";

    /// <summary>
    /// Auth material (client secret / cert thumbprint) could not be resolved.
    /// Configuration binding for <c>SolutionImportOptions:ClientSecret</c> is
    /// null or whitespace. Wave C5 wires this to a Key Vault reference; for
    /// wave C4 this is a Resumable configuration failure.
    /// </summary>
    public const string MissingClientSecret = "missing-client-secret";

    /// <summary>
    /// Solution ZIPs missing on disk at the configured solution path. Build /
    /// deployment error — verify <c>src/solutions/**/bin/Release/*.zip</c>
    /// ships in the L2 publish output or is mounted at the configured path.
    /// Classified Resumable — operator rebuilds the solution pack + resumes.
    /// </summary>
    public const string MissingSolutionZips = "missing-solution-zips";

    /// <summary>
    /// PAC CLI reported an authentication / authorization failure (missing
    /// System Administrator role on Dataverse env, expired secret, etc.).
    /// Classified Resumable — operator refreshes creds + resumes.
    /// </summary>
    public const string PacAuthFailure = "pac-auth-failure";

    /// <summary>
    /// Tenant hit Dataverse rate limit during solution import. Classified
    /// Resumable — operator waits for rate window + resumes; Package Deployer
    /// is idempotent on retry (re-runs skip already-at-version solutions).
    /// </summary>
    public const string RateLimited = "rate-limited";

    /// <summary>
    /// Tenant / capacity quota exhausted (rare for solution import but
    /// possible if Dataverse env storage limit is hit). Classified Resumable
    /// — operator escalates for quota bump + resumes.
    /// </summary>
    public const string QuotaExhausted = "quota-exhausted";

    /// <summary>
    /// PS script timed out during solution import. Classified Resumable —
    /// operator inspects `pac solution list` to determine what completed and
    /// resumes (Package Deployer is idempotent on retry). Distinct code so
    /// operator dashboards can branch on timeout vs partial-import.
    /// </summary>
    public const string ImportTimeout = "import-timeout";

    /// <summary>
    /// PS script exited non-zero AFTER at least one solution was imported —
    /// partial state left in Dataverse. Package Deployer's <c>--stage-and-
    /// upgrade</c> may leave a holding solution behind on mid-flight failure.
    /// Classified QuarantineRequired — operator inspects `pac solution list`,
    /// manually retires any orphaned holding solutions, and either re-runs
    /// (idempotent) or escalates to full decommission per §4C.
    /// </summary>
    public const string PartialImportDetected = "partial-import-detected";

    /// <summary>
    /// PS script exited non-zero without a classified subtype and the
    /// verifier confirmed NO solutions imported. Classified Resumable — no
    /// external side effect, operator inspects stderr + resumes.
    /// </summary>
    public const string ImportInvocationFailed = "import-invocation-failed";

    /// <summary>
    /// Post-import verification failed: PS script reported success but the
    /// verifier's `pac solution list` query shows at least one of the 8
    /// authoritative solutions is missing OR at a wrong version.
    /// Classified QuarantineRequired — script reported success but state
    /// disagrees, indicating out-of-band mutation or a script bug the
    /// operator must diagnose before advance.
    /// </summary>
    public const string VerificationFailed = "verification-failed";

    /// <summary>
    /// Defense-in-depth: the canonical solution catalog contains a name that
    /// matches an ADR-039 retired-artifact pattern. Task 008 audit's R5
    /// binding + ADR-039 amendment 2026-07-05 forbid reintroducing the
    /// retired dispatcher / embeddings surface via any solution.
    /// Classified QuarantineRequired — deployment-time code review missed
    /// a retired-artifact reintroduction; operator must review + revert.
    /// </summary>
    public const string RetiredSolutionReintroduction = "retired-solution-reintroduction";

    /// <summary>
    /// HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13 verbatim.
    /// One or more required Power Platform applications (e.g.
    /// msft_PowerBI_Anchor) could not be installed on the target Dataverse
    /// env within the pre-import ensure step. Solution import would fail
    /// 5 min in with MissingDependency; H6 fails fast (Resumable) with the
    /// specific app-name list so the operator can pre-install manually
    /// before re-running.
    /// </summary>
    public const string MissingRequiredApplication = "missing-required-application";

    /// <summary>
    /// HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) — F14 verbatim.
    /// Org Settings contract (e.g. maxuploadfilesize=25MB) could not be
    /// applied on the target Dataverse env within the pre-import ensure
    /// step. UniversalDocumentUpload PCF bundle would fail 5 min in with
    /// "Webresource content size is too big"; H6 fails fast (Resumable)
    /// with the specific setting name + expected value.
    /// </summary>
    public const string OrgSettingsContractFailed = "org-settings-contract-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state. Resumable — resume re-runs H6 which short-circuits on idempotency.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H6 was in flight. Resumable — operator recreates run + resumes.</summary>
    public const string RunDeletedDuringImport = "run-deleted-during-import";
}
