using System.Text.RegularExpressions;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Dashboard metrics endpoints for the SPE Admin application.
///
/// Provides read-only access to cached container metrics and an on-demand refresh trigger.
/// Metrics are populated by the <see cref="SpeDashboardSyncService"/> scheduled job on a configurable interval
/// (default 15 minutes) and cached in IDistributedCache.
///
/// ADR-001: Minimal API — MapGroup() + static handler methods (no controllers).
/// ADR-008: Authorization via SpeAdminAuthorizationFilter applied at parent route group level.
/// ADR-019: ProblemDetails for all error responses.
/// </summary>
/// <remarks>
/// <b>Business-unit scoping</b> (unified-access-control-r2 task 165). The cached aggregate covers EVERY
/// config. Until task 165 both routes returned it whole, so an admin scoped to one customer's business
/// unit saw every customer's container counts, the per-config breakdown (config ids included) and each
/// config's sync failure reason. Both routes now return the aggregate PROJECTED onto the configs the
/// caller can reach (<see cref="SpeAdminTenantScope.GetReachableConfigIdsAsync"/>): a platform operator who
/// reaches every config sees it unchanged; anyone else sees only their configs' counts, storage and concerns,
/// with totals and health recomputed from those (owner round 25 item 5: the job attributes every container to
/// its owning config, so a partial view's storage is its own configs' storage, not 0). When the scope cannot be
/// read the answer is 503, never the unprojected aggregate (ADR-003 fail closed).
/// </remarks>
public static class DashboardEndpoints
{
    /// <summary>How long a refresh waits for the triggered run to publish newer metrics.</summary>
    private static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers dashboard metric endpoints on the provided /api/spe route group.
    /// Called from SpeAdminEndpoints.MapSpeAdminEndpoints() during startup.
    /// </summary>
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        var dashboard = group.MapGroup("/dashboard")
            .WithTags("SpeAdmin.Dashboard");

        // GET /api/spe/dashboard/metrics
        dashboard.MapGet("/metrics", GetDashboardMetricsAsync)
            .WithName("GetDashboardMetrics")
            .WithSummary("Get cached SPE dashboard metrics")
            .WithDescription(
                "Returns the most recently cached container metrics from the SpeDashboardSyncService job, " +
                "limited to the configs in the caller's business units. " +
                "If no metrics are cached yet (first startup), starts a sync and returns 204 No Content. " +
                "Metrics are refreshed automatically every 15 minutes (configurable). " +
                "Use POST /refresh to trigger an immediate sync.");

        // POST /api/spe/dashboard/refresh
        dashboard.MapPost("/refresh", RefreshDashboardMetricsAsync)
            .WithName("RefreshDashboardMetrics")
            .WithSummary("Trigger an immediate dashboard metrics sync")
            .WithDescription(
                "Runs the SpeDashboardSyncService job now (or joins the run already in progress). " +
                "Waits up to 30 seconds for the sync to complete, then returns the updated metrics, " +
                "limited to the configs in the caller's business units.");

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/dashboard/metrics
    ///
    /// Returns cached <see cref="SpeDashboardSyncService.DashboardMetrics"/> from IDistributedCache,
    /// projected onto the caller's configs.
    ///
    /// Responses:
    ///   200 OK    — Metrics available; returns DashboardMetrics JSON.
    ///   204 No Content — No metrics cached yet; a sync has been started.
    ///   503 Service Unavailable — Cache read error, or the caller's scope could not be read.
    /// </summary>
    private static async Task<IResult> GetDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        ScheduledJobHost jobHost,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var reach = await TryGetReachableConfigIdsAsync(tenantScope, logger, context, ct);
        if (reach is null)
        {
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(context.TraceIdentifier);
        }

        try
        {
            var metrics = await syncService.ReadCachedMetricsAsync(ct);

            if (metrics == null)
            {
                // The job runs on its schedule, not at startup: warm the cache on first view instead of leaving the
                // dashboard empty until the next tick.
                logger.LogInformation(
                    "GET /api/spe/dashboard/metrics — no metrics cached yet; starting a sync.");
                await TryStartSyncAsync(jobHost, logger, ct);
                return Results.NoContent();
            }

            logger.LogDebug(
                "GET /api/spe/dashboard/metrics — returning cached metrics. LastSyncedAt={LastSyncedAt}",
                metrics.LastSyncedAt);

            return Results.Ok(ProjectToReachableConfigs(metrics, reach));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Problem(
                detail: "Request was cancelled.",
                statusCode: 499,
                title: "Cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read dashboard metrics from cache.");

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to retrieve dashboard metrics. Please try again.", ex),
                statusCode: 503,
                title: "Service Unavailable",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.dashboard.cache_read_error"
                });
        }
    }

    /// <summary>
    /// POST /api/spe/dashboard/refresh
    ///
    /// Runs the <see cref="SpeDashboardSyncService"/> job now through <see cref="ScheduledJobHost.TriggerNowAsync"/>
    /// (or joins a run already in progress), waits up to 30 seconds for it to publish newer metrics, then returns them,
    /// projected onto the caller's configs.
    ///
    /// Responses:
    ///   200 OK    — Sync completed (or was already in progress); returns updated DashboardMetrics.
    ///   204 No Content — Sync triggered but no metrics available yet (very first sync, slow response).
    ///   503 Service Unavailable — The scheduler cannot run the job, the sync failed, or the caller's scope could not
    ///                             be read.
    /// </summary>
    private static async Task<IResult> RefreshDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        ScheduledJobHost jobHost,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var reach = await TryGetReachableConfigIdsAsync(tenantScope, logger, context, ct);
        if (reach is null)
        {
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(context.TraceIdentifier);
        }

        logger.LogInformation("POST /api/spe/dashboard/refresh — triggering on-demand sync.");

        try
        {
            var previous = await syncService.ReadCachedMetricsAsync(ct);

            try
            {
                await jobHost.TriggerNowAsync(SpeDashboardSyncService.JobIdConstant, parameters: null, ct);
            }
            catch (ScheduledJobBusyException)
            {
                // A run (scheduled or another refresh) is already in progress — wait for it instead.
                logger.LogInformation("Dashboard refresh: a sync is already running; waiting for it.");
            }

            var metrics = await syncService.WaitForMetricsNewerThanAsync(previous?.LastSyncedAt, RefreshWait, ct);

            if (metrics == null)
            {
                logger.LogWarning(
                    "Dashboard refresh triggered but no metrics were returned within timeout.");
                return Results.NoContent();
            }

            logger.LogInformation(
                "Dashboard refresh complete. Containers: {Total}, SyncSucceeded: {SyncSucceeded}",
                metrics.TotalContainerCount, metrics.SyncSucceeded);

            return Results.Ok(ProjectToReachableConfigs(metrics, reach));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Problem(
                detail: "Request was cancelled while waiting for dashboard refresh.",
                statusCode: 499,
                title: "Cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dashboard refresh failed.");

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to refresh dashboard metrics. Check service logs for details.", ex),
                statusCode: 503,
                title: "Service Unavailable",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.dashboard.refresh_error"
                });
        }
    }

    /// <summary>Starts a sync run if none is running; never throws (the caller answers 204 either way).</summary>
    private static async Task TryStartSyncAsync(ScheduledJobHost jobHost, ILogger logger, CancellationToken ct)
    {
        try
        {
            await jobHost.TriggerNowAsync(SpeDashboardSyncService.JobIdConstant, parameters: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Busy (already running) or the scheduler's lease store is unreachable — the next tick fills the cache.
            logger.LogInformation(ex, "Dashboard: could not start a warm-up sync now.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Business-unit projection (task 165)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The caller's reachable configs, or null when they cannot be read (refuse).</summary>
    private static async Task<SpeAdminConfigReach?> TryGetReachableConfigIdsAsync(
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            return await tenantScope.GetReachableConfigIdsAsync(context.User, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "SPE dashboard: the caller's reachable configs could not be read — refusing. TraceId={TraceId}",
                context.TraceIdentifier);
            return null;
        }
    }

    /// <summary>
    /// The tenant-wide completeness concern <see cref="SpeDashboardSyncService"/> records. Its reason counts
    /// skipped config records across EVERY customer, so only a caller who reaches every config sees it.
    /// </summary>
    internal const string CompletenessConcern = "Dataverse config completeness";

    /// <summary>Matches the per-config concern names <see cref="SpeDashboardSyncService"/> records.</summary>
    private static readonly Regex PerConfigConcern = new(
        @"^(Graph containers|Container business-unit bindings) \(config (?<id>[0-9a-fA-F-]{36})\)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The cached aggregate as the caller may see it: only reachable configs' counts, storage and per-config
    /// concerns, with totals and health recomputed from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PLATFORM OPERATOR (own unit = the root) who reaches EVERY config gets the aggregate unchanged — including
    /// the AGGREGATE of the unattributed containers (bound to a unit of this environment under which no config of their
    /// type sits — a count and storage, never an id or a name). UNBOUND containers are in no view at all, this one
    /// included (owner round 41 item 2: under Model 1 they may be another customer's); the backfill's -Verify lists
    /// them.
    /// Anyone else gets a projection, even when every config the aggregate happens to NAME is theirs: a config the
    /// sync skipped as incomplete is named nowhere, yet it is counted in the completeness concern.
    /// </para>
    /// <para>
    /// <b>Storage is per config</b> (owner round 25 item 5): the job attributes every container to the config whose
    /// business unit owns it, so a projection's storage is the sum over the caller's own configs. The unattributed
    /// figures are never part of a projection.
    /// </para>
    /// <para>
    /// The Dataverse config-load and business-unit concerns name no config and are platform health: they stay. The
    /// <see cref="CompletenessConcern"/> is kept only for a caller who reaches every config — its reason is a count of
    /// skipped config records across every customer. A per-config concern whose config the caller cannot reach, or
    /// whose config id cannot be read, is dropped (fail closed).
    /// </para>
    /// </remarks>
    internal static SpeDashboardSyncService.DashboardMetrics ProjectToReachableConfigs(
        SpeDashboardSyncService.DashboardMetrics metrics,
        SpeAdminConfigReach reach)
    {
        bool IsReachable(string configKey) =>
            Guid.TryParse(configKey, out var id) && reach.ConfigIds.Contains(id);

        string? ConcernConfigKey(SpeDashboardSyncService.ConcernOutcome concern)
        {
            var match = PerConfigConcern.Match(concern.Concern);
            if (match.Success) return match.Groups["id"].Value;
            return concern.Concern.StartsWith("Graph containers", StringComparison.Ordinal)
                   || concern.Concern.StartsWith("Container business-unit bindings", StringComparison.Ordinal)
                ? string.Empty
                : null;
        }

        if (reach.IsPlatformOperator && reach.ReachesEveryConfig)
        {
            return metrics;
        }

        var counts = metrics.ContainerCountByConfig
            .Where(kv => IsReachable(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var counted = counts.Where(kv => kv.Value >= 0).Select(kv => kv.Key).ToHashSet();

        var storage = metrics.StorageUsedInBytesByConfig
            .Where(kv => counted.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var reporting = metrics.StorageReportingContainerCountByConfig
            .Where(kv => counted.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var concerns = metrics.Concerns
            .Where(c => reach.ReachesEveryConfig || !string.Equals(c.Concern, CompletenessConcern, StringComparison.Ordinal))
            .Where(c => ConcernConfigKey(c) is not { } key || IsReachable(key))
            .ToList();

        var health = SpeDashboardSyncService.DeriveHealth(concerns);
        var failed = concerns.Where(c => !c.Succeeded).ToList();

        return metrics with
        {
            ContainerCountByConfig = counts,
            StorageUsedInBytesByConfig = storage,
            StorageReportingContainerCountByConfig = reporting,
            TotalContainerCount = counted.Sum(k => counts[k]),
            TotalStorageUsedInBytes = storage.Values.Sum(),
            StorageReportingContainerCount = reporting.Values.Sum(),
            UnattributedContainerCount = 0,
            UnattributedStorageUsedInBytes = 0,
            UnattributedStorageReportingContainerCount = 0,
            Concerns = concerns,
            SyncHealth = health,
            SyncSucceeded = health == SpeDashboardSyncService.SyncHealth.Healthy,
            SyncStatus = health == SpeDashboardSyncService.SyncHealth.Healthy
                ? $"All {concerns.Count} concern(s) synced successfully."
                : $"{failed.Count} of {concerns.Count} concern(s) failed: " + string.Join("; ", failed.Select(f => f.Concern)),
        };
    }
}
