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
//   - ADR-027 §3-§4 (amended 2026-10-07, T218): ONE package, SpaarkeMaster —
//       managed by default, unmanaged only on explicit instruction; H6 refuses
//       a managed↔unmanaged switch and a downgrade.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       a failed StageAndUpgrade may leave a holding solution behind →
//       QuarantineRequired. Auth / rate-limit / quota / timeout → Resumable.
//   - .claude/adr/ADR-004-job-contract.md: idempotency contract requires
//       stable rejection codes across retries.
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
    /// H6 cannot proceed. Nothing is imported in this case.
    /// </summary>
    public const string MissingDataverseUrl = "missing-dataverse-url";

    /// <summary>
    /// BFF Entra app registration id missing. Populated by upstream H3 into
    /// <see cref="Models.InterStepState.BffAppRegId"/>; the importer signs in to the customer's Dataverse env as
    /// this application (H10 made it System Administrator there).
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
    /// The package artifact is unusable: the provisioning-artifacts manifest is missing or unparseable, has no
    /// SpaarkeMaster entry or no blob for the run's package type, the blob is missing, or the package version cannot
    /// be determined. H6 never falls back to the other type or a local path. Classified Resumable — publish the
    /// package (publish-dataverse-solutions-manifest.yml) and resume.
    /// </summary>
    public const string MissingSolutionZips = "missing-solution-zips";

    /// <summary>
    /// Authentication / authorization failure against the customer's Dataverse env (token acquisition failed, or the
    /// importing identity lacks System Administrator). Code name kept for stability.
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
    /// The import job did not finish within SolutionImportOptions.ImportTimeout. Classified Resumable — a re-run
    /// re-reads the installed version and skips or upgrades (idempotent). Distinct code so operator dashboards can
    /// branch on timeout vs partial-import.
    /// </summary>
    public const string ImportTimeout = "import-timeout";

    /// <summary>
    /// A <c>StageAndUpgrade</c> of SpaarkeMaster failed after it started — a holding solution
    /// (<c>SpaarkeMaster_Upgrade</c>) may be left in the environment. Classified QuarantineRequired — the operator
    /// inspects the environment's solutions, removes or completes the holding solution, then re-runs or escalates per §4C.
    /// </summary>
    public const string PartialImportDetected = "partial-import-detected";

    /// <summary>
    /// The import (or the pre-import read of the installed solution) failed without a classified subtype and no
    /// upgrade was in progress. Classified Resumable — no partial state; the operator reads the diagnostic and resumes.
    /// </summary>
    public const string ImportInvocationFailed = "import-invocation-failed";

    /// <summary>
    /// Post-import verification failed: the importer reported success but the environment does not hold SpaarkeMaster,
    /// or holds it with the wrong type (<c>ismanaged</c> ≠ the run's package type). Classified QuarantineRequired —
    /// state disagrees with the import result; the operator diagnoses before advancing.
    /// </summary>
    public const string VerificationFailed = "verification-failed";

    /// <summary>
    /// T218b review — the post-import check could not read the environment (token, timeout, transport, 408/429/5xx).
    /// Resumable: a re-run skips the equal-version import and verifies again.
    /// </summary>
    public const string VerificationUnavailable = "verification-unavailable";

    /// <summary>
    /// T218b — the environment already holds SpaarkeMaster of the OTHER type (managed vs unmanaged) than the run asks
    /// for. Nothing is imported: a silent switch is refused (ADR-027 §3, amended 2026-10-07). Resumable — the operator
    /// re-runs with the environment's type, or an owner-approved conversion is done first.
    /// </summary>
    public const string PackageTypeMismatch = "package-type-mismatch";

    /// <summary>
    /// T218b — the environment holds a HIGHER SpaarkeMaster version than the published package. Nothing is imported
    /// (no downgrade). Resumable — publish the right package, then re-run.
    /// </summary>
    public const string DowngradeRefused = "downgrade-refused";

    /// <summary>
    /// T218b — the run's <c>solutionPackageType</c> is neither <c>managed</c> nor <c>unmanaged</c> (a run created
    /// before POST /api/runs validated it). Resumable — nothing was touched.
    /// </summary>
    public const string PackageTypeInvalid = "package-type-invalid";

    /// <summary>
    /// RETIRED (T218b): H6 imports one package, SpaarkeMaster, so there is no catalog to scan. Kept per the stability
    /// rule above; never emitted.
    /// </summary>
    [Obsolete("T218b — H6 imports one package (SpaarkeMaster); there is no catalog to scan. Never emitted.")]
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
