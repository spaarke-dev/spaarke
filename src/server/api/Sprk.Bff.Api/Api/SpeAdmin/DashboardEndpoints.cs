using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Dashboard metrics endpoints for the SPE Admin application.
///
/// Provides read-only access to cached container metrics and an on-demand refresh trigger.
/// Metrics are populated by <see cref="SpeDashboardSyncService"/> on a configurable interval
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
/// caller can reach (<see cref="SpeAdminTenantScope.GetReachableConfigIdsAsync"/>): an admin who reaches
/// every config sees it unchanged; anyone else sees only their configs' counts and concerns, with totals
/// and health recomputed from those. When the scope cannot be read the answer is 503, never the
/// unprojected aggregate (ADR-003 fail closed).
/// </remarks>
public static class DashboardEndpoints
{
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
                "Returns the most recently cached container metrics from the SpeDashboardSyncService, " +
                "limited to the configs in the caller's business units. " +
                "If no metrics are cached yet (first startup), returns 204 No Content. " +
                "Metrics are refreshed automatically every 15 minutes (configurable). " +
                "Use POST /refresh to trigger an immediate sync.");

        // POST /api/spe/dashboard/refresh
        dashboard.MapPost("/refresh", RefreshDashboardMetricsAsync)
            .WithName("RefreshDashboardMetrics")
            .WithSummary("Trigger an immediate dashboard metrics sync")
            .WithDescription(
                "Signals the SpeDashboardSyncService to perform an immediate sync from Graph API. " +
                "Waits up to 30 seconds for the sync to complete, then returns the updated metrics, " +
                "limited to the configs in the caller's business units. " +
                "Multiple concurrent refresh requests coalesce into a single sync run.");

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
    ///   204 No Content — No metrics cached yet (service just started, hasn't completed first sync).
    ///   503 Service Unavailable — Cache read error, or the caller's scope could not be read.
    /// </summary>
    private static async Task<IResult> GetDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var reachable = await TryGetReachableConfigIdsAsync(tenantScope, logger, context, ct);
        if (reachable is null)
        {
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(context.TraceIdentifier);
        }

        try
        {
            var metrics = await syncService.ReadCachedMetricsAsync(ct);

            if (metrics == null)
            {
                logger.LogInformation(
                    "GET /api/spe/dashboard/metrics — no metrics cached yet (first sync pending).");
                return Results.NoContent();
            }

            logger.LogDebug(
                "GET /api/spe/dashboard/metrics — returning cached metrics. LastSyncedAt={LastSyncedAt}",
                metrics.LastSyncedAt);

            return Results.Ok(ProjectToReachableConfigs(metrics, reachable.Value.ConfigIds, reachable.Value.ReachesEveryConfig));
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
    /// Triggers an immediate sync via <see cref="SpeDashboardSyncService.TriggerRefreshAsync"/>.
    /// Waits up to 30 seconds for the sync to complete, then returns the updated metrics, projected onto
    /// the caller's configs.
    ///
    /// Responses:
    ///   200 OK    — Sync completed (or was already in progress); returns updated DashboardMetrics.
    ///   204 No Content — Sync triggered but no metrics available yet (very first sync, slow response).
    ///   503 Service Unavailable — Sync failed or timed out, or the caller's scope could not be read.
    /// </summary>
    private static async Task<IResult> RefreshDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var reachable = await TryGetReachableConfigIdsAsync(tenantScope, logger, context, ct);
        if (reachable is null)
        {
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(context.TraceIdentifier);
        }

        logger.LogInformation("POST /api/spe/dashboard/refresh — triggering on-demand sync.");

        try
        {
            var metrics = await syncService.TriggerRefreshAsync(ct);

            if (metrics == null)
            {
                logger.LogWarning(
                    "Dashboard refresh triggered but no metrics were returned within timeout.");
                return Results.NoContent();
            }

            logger.LogInformation(
                "Dashboard refresh complete. Containers: {Total}, SyncSucceeded: {SyncSucceeded}",
                metrics.TotalContainerCount, metrics.SyncSucceeded);

            return Results.Ok(ProjectToReachableConfigs(metrics, reachable.Value.ConfigIds, reachable.Value.ReachesEveryConfig));
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

    // ─────────────────────────────────────────────────────────────────────────
    // Business-unit projection (task 165)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The caller's reachable config ids, or null when they cannot be read (refuse).</summary>
    private static async Task<(IReadOnlySet<Guid> ConfigIds, bool ReachesEveryConfig)?> TryGetReachableConfigIdsAsync(
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

    /// <summary>Matches the per-config concern name <see cref="SpeDashboardSyncService"/> records.</summary>
    private static readonly Regex GraphConcernConfigId = new(
        @"^Graph containers \(config (?<id>[0-9a-fA-F-]{36})\)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The cached aggregate as the caller may see it: only reachable configs' counts and per-config
    /// concerns, with totals and health recomputed from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller who reaches EVERY config in the table (<paramref name="reachesEveryConfig"/>) gets the
    /// aggregate unchanged — the root operator's view. Anyone else gets a projection, even when every config
    /// the aggregate happens to NAME is theirs: a config the sync skipped as incomplete is named nowhere, yet
    /// it is counted in the completeness concern.
    /// </para>
    /// <para>
    /// <b>Storage in a partial view is reported as not reported</b> (0 bytes from 0 reporting containers),
    /// because the cached aggregate holds only a cross-config storage SUM and no per-config split; the
    /// metrics contract already treats a reporting count below the total as "a floor, not a total". The
    /// per-config split belongs in <see cref="SpeDashboardSyncService"/>, which is an ADR-052
    /// ratchet-listed timer that migrates when next touched — recorded in the task 165 note as follow-up.
    /// </para>
    /// <para>
    /// The Dataverse config-load concern names no config and is platform health: it stays. The
    /// <see cref="CompletenessConcern"/> is dropped from a projection — its reason is a count of skipped
    /// config records across every customer, which a leaf admin may not see. A per-config concern whose
    /// config id cannot be read is dropped (fail closed).
    /// </para>
    /// <para>
    /// Storage is kept only when every config the aggregate counts is reachable (no other customer
    /// contributed to the sum); otherwise it is reported as not reported (see above).
    /// </para>
    /// </remarks>
    internal static SpeDashboardSyncService.DashboardMetrics ProjectToReachableConfigs(
        SpeDashboardSyncService.DashboardMetrics metrics,
        IReadOnlySet<Guid> reachable,
        bool reachesEveryConfig)
    {
        bool IsReachable(string configKey) =>
            Guid.TryParse(configKey, out var id) && reachable.Contains(id);

        string? ConcernConfigKey(SpeDashboardSyncService.ConcernOutcome concern)
        {
            var match = GraphConcernConfigId.Match(concern.Concern);
            if (match.Success) return match.Groups["id"].Value;
            return concern.Concern.StartsWith("Graph containers", StringComparison.Ordinal) ? string.Empty : null;
        }

        if (reachesEveryConfig)
        {
            return metrics;
        }

        var everyCountReachable = metrics.ContainerCountByConfig.Keys.All(IsReachable);

        var counts = metrics.ContainerCountByConfig
            .Where(kv => IsReachable(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var concerns = metrics.Concerns
            .Where(c => !string.Equals(c.Concern, CompletenessConcern, StringComparison.Ordinal))
            .Where(c => ConcernConfigKey(c) is not { } key || IsReachable(key))
            .ToList();

        var health = SpeDashboardSyncService.DeriveHealth(concerns);
        var failed = concerns.Where(c => !c.Succeeded).ToList();

        return metrics with
        {
            ContainerCountByConfig = counts,
            TotalContainerCount = counts.Values.Where(v => v > 0).Sum(),
            TotalStorageUsedInBytes = everyCountReachable ? metrics.TotalStorageUsedInBytes : 0,
            StorageReportingContainerCount = everyCountReachable ? metrics.StorageReportingContainerCount : 0,
            Concerns = concerns,
            SyncHealth = health,
            SyncSucceeded = health == SpeDashboardSyncService.SyncHealth.Healthy,
            SyncStatus = health == SpeDashboardSyncService.SyncHealth.Healthy
                ? $"All {concerns.Count} concern(s) synced successfully."
                : $"{failed.Count} of {concerns.Count} concern(s) failed: " + string.Join("; ", failed.Select(f => f.Concern)),
        };
    }
}
