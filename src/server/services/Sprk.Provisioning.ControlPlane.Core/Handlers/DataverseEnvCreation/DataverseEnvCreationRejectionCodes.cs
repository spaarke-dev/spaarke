// -----------------------------------------------------------------------------
// DataverseEnvCreationRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H5DataverseEnvCreationHandler
// (task 048). Every distinct failure mode gets its own code so the reconciler
// + operator UI can branch on the exact reason WITHOUT string-matching the
// human-readable Diagnostic (which may be reworded for clarity).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-08 (H5)
//     acceptance: "sprk_dataverseenvironment.sprk_dataverseurl populated + env
//     is accessible". Codes here map to distinct failure surfaces.
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1: no
//     hardcoded default tenant — MissingTenantId code guards this.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H5 row:
//     interim `pac admin create-environment` per M-10; §4C rollback taxonomy
//     applies (rate-limit/quota/auth → Resumable; partial provisioning →
//     QuarantineRequired).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2 fire-and-
//     forget: long-running (15–30 min) handler; EnvCreationTimeout is a
//     distinct terminal signal separate from health-check failures.
//   - .claude/adr/ADR-004-job-contract.md: idempotency contract requires
//     stable rejection codes across retries.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones @[Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Machine-stable rejection codes for <see cref="H5DataverseEnvCreationHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class DataverseEnvCreationRejectionCodes
{
    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "missing-tenant-id";

    /// <summary>Run parameter <c>region</c> missing — <c>pac admin create-environment</c> requires an explicit Dataverse region.</summary>
    public const string MissingRegion = "missing-region";

    /// <summary>Run parameter <c>tier</c> (env type) missing — required for <c>pac admin create-environment --type</c>.</summary>
    public const string MissingTier = "missing-tier";

    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// <c>pac admin create-environment</c> returned an authentication /
    /// authorization failure (e.g. missing <c>Power Platform Admin</c> role,
    /// expired session). Classified Resumable — operator refreshes creds +
    /// POSTs <c>/api/runs/{id}/resume</c>.
    /// </summary>
    public const string PacAuthFailure = "pac-auth-failure";

    /// <summary>
    /// Tenant hit Dataverse env-creation rate limit (~4/hour typical, checked
    /// in H0 preflight via <c>scripts/preflight/Test-DataverseEnvCreationRate.ps1</c>).
    /// Classified Resumable — operator waits for the rate window + resumes.
    /// </summary>
    public const string RateLimited = "rate-limited";

    /// <summary>
    /// Tenant / capacity quota exhausted for the requested tier or region.
    /// Classified Resumable — operator escalates for a quota bump + resumes.
    /// </summary>
    public const string QuotaExhausted = "quota-exhausted";

    /// <summary>
    /// <c>pac admin create-environment</c> reported partial provisioning
    /// (env row exists in tenant but is in an inconsistent state, or the
    /// pac CLI crashed after Dataverse acknowledged the create request).
    /// Classified QuarantineRequired — orphaned env may exist and must be
    /// resolved by an operator before a re-run.
    /// </summary>
    public const string PartialProvisioning = "partial-provisioning";

    /// <summary>
    /// Env creation did NOT complete within the configured window (default
    /// 45 min per <see cref="DataverseEnvCreationOptions.CreationTimeout"/>).
    /// Classified Resumable — operator inspects Power Platform Admin Center
    /// + resumes once env either completes or is confirmed deleted.
    /// Distinct code so operator dashboards can branch on timeout-vs-failure.
    /// </summary>
    public const string EnvCreationTimeout = "env-creation-timeout";

    /// <summary>
    /// pac CLI generic non-zero exit that does not match any classified
    /// subtype. Classified Resumable (default optimistic — operator inspects
    /// stderr + resumes; if partial state is detected during that inspection
    /// the operator can escalate to quarantine manually).
    /// </summary>
    public const string PacInvocationFailed = "pac-invocation-failed";

    /// <summary>
    /// Post-create Dataverse Web API health probe (<c>GET /api/data/v9.2/WhoAmI</c>)
    /// returned non-200 or an unreachable endpoint. The env row may exist
    /// but is not yet Web-API-accessible. Distinct from
    /// <see cref="EnvCreationTimeout"/> which fires when the create call
    /// itself never completed. Classified Resumable — operator inspects
    /// env state in PPAC + resumes.
    /// </summary>
    public const string EnvHealthCheckFailed = "env-health-check-failed";

    /// <summary>
    /// Race with a concurrent Cosmos writer — reconciler will observe
    /// winning state. Resumable — resume re-runs H5 which will short-circuit
    /// on idempotency if the concurrent writer already recorded H5 complete.
    /// </summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H5 was in flight. Resumable — operator recreates run + resumes.</summary>
    public const string RunDeletedDuringCreation = "run-deleted-during-creation";

    /// <summary>
    /// BAP admin REST create call rejected because the requested domain
    /// (subdomain) is already taken — no resource was created by this
    /// attempt. Classified Resumable, but a naive retry with the SAME
    /// domain/customerId will repeat the failure; the operator MUST supply a
    /// different domain before resuming. Distinct from
    /// <see cref="PartialProvisioning"/>. Added task 140 (BAP-REST port).
    /// </summary>
    public const string DomainAlreadyExists = "domain-already-exists";

    /// <summary>
    /// BAP admin REST async-operation-status poll observed a terminal
    /// <c>provisioningState</c> of <c>Failed</c> or <c>Deleted</c>. Distinct
    /// from <see cref="EnvCreationTimeout"/> (which fires when the poll
    /// simply outlasted the configured window without a terminal signal).
    /// Classified Resumable — operator inspects Power Platform Admin Center.
    /// Added task 140 (BAP-REST port).
    /// </summary>
    public const string EnvProvisioningFailed = "env-provisioning-failed";
}
