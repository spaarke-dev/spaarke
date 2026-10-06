// -----------------------------------------------------------------------------
// EnvVarValuesRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H7DataverseEnvVarValuesHandler
// (task 050, wave C4 Batch 3E). Every distinct failure mode gets its own code
// so the reconciler + operator UI can branch on the exact reason WITHOUT
// string-matching the human-readable Diagnostic. Parity with H5 / H6's
// RejectionCodes pattern.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-10 + §10.3:
//       7 canonical per-customer Dataverse env-var values; client fails fast
//       at startup if any is missing (task 024 contract).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H7:
//       Idempotency key `envvars-{customerId}-{configVer}`.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       All H7 failure modes classify as Resumable — every
//       environmentvariablevalue write is a natural upsert (PATCH-if-exists,
//       POST-if-not), so a full handler re-run after ANY failure (including a
//       partial multi-var write) is safe with no cleanup required. There is
//       no QuarantineRequired path for H7.
//
// STABILITY:
//   Codes are string constants used by external tools (reconciler queries,
//   operator UI filters, alerting). Do NOT rename; add new codes as needed
//   and mark old ones [Obsolete] on removal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <summary>
/// Machine-stable rejection codes for <see cref="H7DataverseEnvVarValuesHandler"/>
/// failures. Callers pattern-match on these to route recovery + build operator
/// diagnostics without depending on Diagnostic message wording.
/// </summary>
public static class EnvVarValuesRejectionCodes
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "run-not-found";

    /// <summary>
    /// A required upstream value is missing from the ProvisioningRun (either
    /// InterStepState — bffAppRegId/dataverseEnvUrl/openAiEndpoint/speContainerId
    /// — or Parameters.NonSecret.tenantId per §4D I1). The Diagnostic names the
    /// exact missing key. Handler MUST NOT invoke the writer in this case.
    /// </summary>
    public const string MissingUpstreamState = "missing-upstream-state";

    /// <summary>
    /// <see cref="EnvVarValuesOptions.ClientSecret"/> is not populated AND the
    /// FR-39 ordered credential chain REQUIRES it (primary =
    /// <c>ClientSecret</c> — the legacy/unconfigured default; A44.5, task
    /// 205i). H7 authenticates to the target Dataverse env using the same
    /// confidential-client (BFF app-reg) credential pattern H6 uses for
    /// solution import — the MI-Dataverse App User (H10) has not yet been
    /// created at H7's point in the DAG (H10 runs AFTER H7 per design.md
    /// §4.1); under the secret-free chain the Worker UAMI's federated
    /// assertion proves the SAME app-reg identity (H3-created FIC), so this
    /// rejection cannot fire there — an empty slot is the signal (auth-v4
    /// §9.1), never an error. Wave C5 wires the legacy path to a Key Vault
    /// reference (conditionally emitted per A44.5); wave C4 required the
    /// operator to set the app-setting explicitly.
    /// </summary>
    public const string MissingClientSecret = "missing-client-secret";

    /// <summary>
    /// Dataverse Web API returned 401/403 while writing an environment
    /// variable value. Classified Resumable — operator verifies the BFF
    /// app-reg still has sufficient privilege on the target env + resumes.
    /// </summary>
    public const string DataverseAuthFailure = "dataverse-auth-failure";

    /// <summary>
    /// Dataverse Web API returned 429 (rate-limited) while writing an
    /// environment variable value. Classified Resumable — operator waits for
    /// the rate window + resumes; every write is a natural upsert.
    /// </summary>
    public const string RateLimited = "rate-limited";

    /// <summary>
    /// <c>environmentvariabledefinition</c> lookup by schema name returned
    /// zero rows for one of the 7 canonical names. Indicates the owning
    /// solution (imported by H6) did not ship the expected env-var
    /// definition. Classified Resumable — operator verifies the solution
    /// package + resumes (H6 is idempotent so re-import is safe if needed).
    /// </summary>
    public const string EnvVarDefinitionNotFound = "env-var-definition-not-found";

    /// <summary>
    /// Writer infrastructure fault (HTTP transport error, token acquisition
    /// failure, unclassified non-success status code). Classified Resumable
    /// — operator inspects logs + resumes.
    /// </summary>
    public const string WriterInvocationFailed = "writer-invocation-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state. Resumable — resume re-runs H7 which short-circuits on idempotency.</summary>
    public const string ConcurrentWriteConflict = "concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H7 was in flight. Resumable — operator recreates run + resumes.</summary>
    public const string RunDeletedDuringWrite = "run-deleted-during-write";
}
