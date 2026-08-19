// -----------------------------------------------------------------------------
// AppConfigSeedRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H12bAppConfigSeedHandler on
// HandlerResult.Failure. Operators + BFF surface these verbatim so external
// tooling (dashboards, runbook lookups) can pattern-match without parsing
// English diagnostics.
//
// DESIGN REF:
//   - HandlerResult.Failure.RejectionCode (Handlers/HandlerResult.cs)
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-16 (H12b
//     acceptance — each failure branch has a distinct code)
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1
//     (tenant hardcoding forbidden — MissingTenantId / MissingCustomerId
//     enforce; MissingDataverseUrl too since target-env URL is a per-run
//     parameter)
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H12b row +
//     §4C rollback (H12b failures are Resumable — every wrapped script is
//     upsert-safe so post-remediation resume re-drives cleanly)
//
// PATTERN PARITY:
//   Mirrors Handlers/SubscriptionReadiness/SubscriptionReadinessRejectionCodes.cs
//   (task 043) — one const per failure branch + lowercase kebab-case for
//   greppability + runbook lookups.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// Stable rejection codes returned by <c>H12bAppConfigSeedHandler</c> on
/// <c>HandlerResult.Failure</c>. Values are lowercase kebab-case so grep +
/// runbook lookup are trivial (parity with
/// <see cref="SubscriptionReadiness.SubscriptionReadinessRejectionCodes"/>).
/// </summary>
public static class AppConfigSeedRejectionCodes
{
    /// <summary>
    /// Cosmos partition read returned no matching ProvisioningRun for
    /// <c>(customerId, runId)</c>. Reconciler treats as Resumable (operator
    /// verifies the run exists + resumes).
    /// </summary>
    public const string RunNotFound = "appconfig-run-not-found";

    /// <summary>
    /// <c>run.Parameters.NonSecret["tenantId"]</c> was null / whitespace.
    /// §4D I1 forbids a default-tenant fallback — operator must supply the
    /// target tenantId in the run's non-secret parameters.
    /// </summary>
    public const string MissingTenantId = "appconfig-missing-tenant-id";

    /// <summary>
    /// <c>run.Parameters.NonSecret["dataverseUrl"]</c> was null / whitespace.
    /// H12b needs an explicit target-env URL to hand the wrapped PS scripts
    /// (they in turn call <c>az account get-access-token --resource {url}</c>).
    /// </summary>
    public const string MissingDataverseUrl = "appconfig-missing-dataverse-url";

    /// <summary>
    /// One or more registered <see cref="IAppConfigSeeder"/> instances
    /// returned <see cref="AppConfigSeederStatus.Failed"/>. Handler
    /// aggregates the failing scope name(s) + diagnostics into the
    /// HandlerResult.Failure.Diagnostic string for operator triage.
    /// </summary>
    public const string SeederFailed = "appconfig-seeder-failed";

    /// <summary>
    /// One or more registered <see cref="IAppConfigSeeder"/> instances threw
    /// an unexpected infrastructure exception (pwsh missing, connectivity
    /// fault, out-of-memory). Handler classifies as Resumable so the operator
    /// can fix the infra + resume.
    /// </summary>
    public const string SeederInfrastructureError = "appconfig-seeder-infrastructure-error";

    /// <summary>
    /// Optimistic-concurrency race: another writer advanced the run row
    /// between H12b's read and its state-mutating write. Reconciler resumes
    /// (safe: H12b re-runs the seeders and rediscovers the winning state;
    /// the underlying scripts are all upsert-safe).
    /// </summary>
    public const string ConcurrentWriteConflict = "appconfig-concurrent-write-conflict";

    /// <summary>
    /// The run row was deleted between H12b's read and its state-mutating
    /// write. Surfaces the row-delete race distinctly from ETag conflict so
    /// the operator knows to recreate the run rather than resume.
    /// </summary>
    public const string RunDeletedDuringSeed = "appconfig-run-deleted-during-seed";

    /// <summary>
    /// The manifest file (<c>scripts/seed-data/manifest.yaml</c>) was not
    /// found on disk. Manifest is the source of the manifestHash portion of
    /// the H12b idempotency key — its absence blocks the run because we
    /// cannot compute a deterministic key without it.
    /// </summary>
    public const string ManifestNotFound = "appconfig-manifest-not-found";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c>
/// by H12b app-config seed. Kept as string constants so grep across the
/// codebase finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class AppConfigSeedGates
{
    /// <summary>
    /// The gate H12b owns for the overall app-config seed outcome — flipped
    /// from <c>Pending</c> to <c>Verified</c> when every registered seeder
    /// returns <see cref="AppConfigSeederStatus.Ok"/> or
    /// <see cref="AppConfigSeederStatus.Deferred"/>.
    /// </summary>
    public const string AppConfigSeeded = "app-config-seeded";
}
