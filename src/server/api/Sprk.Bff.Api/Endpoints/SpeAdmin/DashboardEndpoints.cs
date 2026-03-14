using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Endpoints.SpeAdmin;

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
                "Returns the most recently cached container metrics from the SpeDashboardSyncService. " +
                "If no metrics are cached yet (first startup), returns 204 No Content. " +
                "Metrics are refreshed automatically every 15 minutes (configurable). " +
                "Use POST /refresh to trigger an immediate sync.");

        // POST /api/spe/dashboard/refresh
        dashboard.MapPost("/refresh", RefreshDashboardMetricsAsync)
            .WithName("RefreshDashboardMetrics")
            .WithSummary("Trigger an immediate dashboard metrics sync")
            .WithDescription(
                "Signals the SpeDashboardSyncService to perform an immediate sync from Graph API. " +
                "Waits up to 30 seconds for the sync to complete, then returns the updated metrics. " +
                "Multiple concurrent refresh requests coalesce into a single sync run.");

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/dashboard/metrics
    ///
    /// Returns cached <see cref="SpeDashboardSyncService.DashboardMetrics"/> from IDistributedCache.
    ///
    /// Responses:
    ///   200 OK    — Metrics available; returns DashboardMetrics JSON.
    ///   204 No Content — No metrics cached yet (service just started, hasn't completed first sync).
    ///   503 Service Unavailable — Cache read error.
    /// </summary>
    private static async Task<IResult> GetDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
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

            return Results.Ok(metrics);
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
                detail: "Failed to retrieve dashboard metrics. Please try again.",
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
    /// Waits up to 30 seconds for the sync to complete, then returns the updated metrics.
    ///
    /// Responses:
    ///   200 OK    — Sync completed (or was already in progress); returns updated DashboardMetrics.
    ///   204 No Content — Sync triggered but no metrics available yet (very first sync, slow response).
    ///   503 Service Unavailable — Sync failed or timed out.
    /// </summary>
    private static async Task<IResult> RefreshDashboardMetricsAsync(
        SpeDashboardSyncService syncService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
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

            return Results.Ok(metrics);
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
                detail: "Failed to refresh dashboard metrics. Check service logs for details.",
                statusCode: 503,
                title: "Service Unavailable",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.dashboard.refresh_error"
                });
        }
    }
}
