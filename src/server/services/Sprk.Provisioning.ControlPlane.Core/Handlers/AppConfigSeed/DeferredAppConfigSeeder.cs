// -----------------------------------------------------------------------------
// DeferredAppConfigSeeder.cs
//
// *** RETIRED (task 152, Wave G-5 Batch G-5B, 2026-08-20) ***
// No longer registered in AppConfigSeedModule.AddH12bAppConfigSeedHandler —
// superseded by DataverseWebApiFieldMappingSeeder.cs (FieldMapping scope) and
// DataverseWebApiChartDefSeeder.cs (ChartDefinition scope), both GREENFIELD
// pure HttpClient + DefaultAzureCredential seeders (NOT ports — neither scope
// ever had a shipping repo source, per DS-4 §3). Kept on disk UNREGISTERED
// per the Wave G-2/G-3/G-4/G-5 retirement convention (see
// PowerShellAppConfigSeeder.cs for the precedent) — NOT reachable from any
// production code path. FR-16's 4-scope delivery is now fully complete;
// this class has zero remaining callers anywhere in the repo.
//
// <see cref="IAppConfigSeeder"/> implementation for app-config scopes that
// have NO shipping repo source yet. Returns a Deferred outcome with a clear
// diagnostic + drift-resolution reference so the operator understands the
// skip is intentional (per task 004 §4b decision matrix).
//
// SCOPES FORMERLY DEFERRED (both now real — see retirement banner above):
//   scope=field-mapping    — task 004 §4b row 11 + §5b N3 delta. NO repo
//                            source; replaced by DataverseWebApiFieldMappingSeeder
//                            (task 152), which ground-truthed the default
//                            attorney-matrix seed content directly against
//                            live spaarkedev1 via the Dataverse MCP.
//   scope=chart-definition — task 004 §4b row 13 + §5b N5 delta. Per-family
//                            scripts (create-test-chartdefinitions.ps1,
//                            Create-UpcomingTodosChartDefinitions.ps1) exist
//                            but a consolidated mirror does NOT. Replaced by
//                            DataverseWebApiChartDefSeeder (task 152), which
//                            seeds the one family WITH a repo JSON mirror
//                            (Upcoming To Dos, 4 rows) — see that file's
//                            header for the documented scope decision on the
//                            other ~15 live, unmirrored chart-def records.
//
// WHY NOT SILENTLY OMIT THE SCOPE?
//   Explicit Deferred rows in the handler's outcome roll-up (visible in
//   ProvisioningRun.GateStates + audit logs) make the deferral visible to
//   operators + to the H13 acceptance-gate handler. A silent omission
//   would drift back into R14-style two-source ambiguity — the DeferredAppConfigSeeder
//   registration IS the deferral's system-of-record until mirror-authoring
//   completes.
//
// SEMANTICS:
//   - Handler treats Deferred == Success for run-outcome purposes (does NOT
//     fail the run) BUT rolls up the deferred-scope list into GateStates +
//     the H12b success diagnostic so operators + downstream handlers see it.
//   - Never throws. Never touches the environment.
//   - Logs a single Warning per invocation so scaffold environments notice
//     the deferred scope is still active before Wave-C5 replaces it.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// Placeholder <see cref="IAppConfigSeeder"/> for app-config scopes lacking
/// a shipping repo source. Returns <see cref="AppConfigSeederStatus.Deferred"/>
/// with a diagnostic naming the drift-resolution note reference.
/// </summary>
public sealed class DeferredAppConfigSeeder : IAppConfigSeeder
{
    private readonly ILogger<DeferredAppConfigSeeder> _logger;
    private readonly string _driftMatrixReference;
    private readonly string _followOnDeltaId;

    /// <inheritdoc/>
    public string ScopeName { get; }

    /// <summary>
    /// Constructs a Deferred seeder bound to <paramref name="scopeName"/>.
    /// </summary>
    /// <param name="scopeName">Scope identifier (see <see cref="AppConfigSeedScopes"/>).</param>
    /// <param name="driftMatrixReference">Reference into <c>notes/ai-seed-drift-resolution-2026-08.md</c> (e.g. <c>§4b row 11</c>).</param>
    /// <param name="followOnDeltaId">Migration-delta id from that same note (e.g. <c>N3</c>) — the operator-visible pointer for what needs to happen to un-defer this scope.</param>
    /// <param name="logger">Structured logger.</param>
    public DeferredAppConfigSeeder(
        string scopeName,
        string driftMatrixReference,
        string followOnDeltaId,
        ILogger<DeferredAppConfigSeeder> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(driftMatrixReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(followOnDeltaId);
        ArgumentNullException.ThrowIfNull(logger);

        ScopeName = scopeName;
        _driftMatrixReference = driftMatrixReference;
        _followOnDeltaId = followOnDeltaId;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        _logger.LogWarning(
            "App-config seeder scope={ScopeName} is DEFERRED (interim, no repo source). " +
            "customerId={CustomerId}. Un-deferring requires migration delta {FollowOnDelta} " +
            "per notes/ai-seed-drift-resolution-2026-08.md {DriftMatrixRef}.",
            ScopeName, input.CustomerId, _followOnDeltaId, _driftMatrixReference);

        var diagnostic =
            $"{ScopeName} seed DEFERRED (interim — no shipping repo source). " +
            $"Un-deferring requires migration delta {_followOnDeltaId} " +
            $"per notes/ai-seed-drift-resolution-2026-08.md {_driftMatrixReference}. " +
            "Handler continues without failing the run; downstream H13 acceptance-gate " +
            "verifies at run close.";

        return Task.FromResult(AppConfigSeederResult.Deferred(diagnostic));
    }
}
