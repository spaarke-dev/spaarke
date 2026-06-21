using Cronos;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Admin.Models;

namespace Sprk.Bff.Api.Api.Admin;

/// <summary>
/// Admin endpoints for inspecting <c>Spaarke.Scheduling</c> background jobs registered with
/// the in-process <see cref="ScheduledJobRegistry"/> and their run history from
/// <see cref="IBackgroundJobStore"/>.
/// </summary>
/// <remarks>
/// <para><b>Spec coverage</b>: R3 spec.md FR-2.6 (admin endpoints under <c>/api/admin/jobs/*</c>),
/// AC-2.5 (non-admin tokens receive 403), AC-2.7 (failed jobs surface in status response with
/// last error).</para>
///
/// <para><b>Auth model</b>: All endpoints behind <c>RequireAuthorization("SystemAdmin")</c> per
/// Q6 owner clarification (existing policy at
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AuthorizationModule"/> line 241) — NOT a new
/// <c>PlatformAdmin</c> policy. Precedent: <c>RagEndpoints.cs</c> bulk-indexing admin group uses
/// the same policy.</para>
///
/// <para><b>ADR compliance</b>: ADR-001 (Minimal API + BackgroundService), ADR-008 (endpoint-filter
/// authorization, not global middleware), ADR-010 (concretes via DI; <see cref="IBackgroundJobStore"/>
/// is justified as an interface because there are ≥2 implementations from day one — in-memory now,
/// Dataverse-backed in task 023+).</para>
///
/// <para><b>Task ownership (coordination with tasks 021 + 022)</b>:
/// This file is the single home for every <c>/api/admin/jobs/*</c> endpoint. Tasks 021 and 022
/// will append handlers to their own demarcated sections below. Task 020 owns:
/// <list type="bullet">
///   <item><c>GET /api/admin/jobs</c> — list registered jobs + status summary</item>
///   <item><c>GET /api/admin/jobs/{jobId}/status</c> — per-job detail + last 10 runs</item>
/// </list>
/// </para>
/// </remarks>
public static class JobsEndpoints
{
    private const int DefaultRecentRunsLimit = 10;

    public static IEndpointRouteBuilder MapAdminJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/admin/jobs")
            .RequireAuthorization("SystemAdmin")
            .WithTags("Admin Jobs");

        // ============================================================================
        // ===== Task 020 — GET /api/admin/jobs ======================================
        // ============================================================================
        // Lists every IScheduledJob registered with ScheduledJobRegistry, joined with the
        // most-recent run from IBackgroundJobStore + the next computed cron occurrence.
        // Returns 200 + empty list when registry is empty.
        group.MapGet("", ListJobsAsync)
            .WithName("AdminJobsList")
            .WithSummary("List all registered background jobs with status summary")
            .WithDescription("Enumerates every IScheduledJob registered with the in-process ScheduledJobRegistry, joined with the most-recent run record from IBackgroundJobStore and the next computed cron occurrence (via Cronos). Returns 200 + empty list if no jobs are registered.")
            .Produces<IReadOnlyList<JobStatusSummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // ===== Task 020 — GET /api/admin/jobs/{jobId}/status ========================
        // ============================================================================
        // Per-job detail view — same summary fields as list endpoint plus last 10 run records.
        // 404 when jobId is not registered with ScheduledJobRegistry. AC-2.7: most-recent
        // failure surfaces in RecentRuns[0].ErrorMessage.
        group.MapGet("/{jobId}/status", GetJobStatusAsync)
            .WithName("AdminJobsStatus")
            .WithSummary("Get detailed status for a specific background job")
            .WithDescription("Returns the job's status summary plus its last 10 run records (newest-first). Returns 404 if jobId is not registered with the in-process ScheduledJobRegistry. Failed runs surface in the most-recent RecentRuns entry with ErrorMessage populated (AC-2.7).")
            .Produces<JobStatusDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // ===== Task 021 — POST /api/admin/jobs/{jobId}/trigger =====================
        // ============================================================================
        // Manual out-of-band dispatch (Trigger=ManualAdmin). Writes a sprk_backgroundjobrun
        // row with the manual trigger + a fresh correlationId (NFR-08) and returns 202
        // immediately so the admin client is not blocked on the job's actual duration.
        // 404 when the jobId is not registered with ScheduledJobRegistry.
        group.MapPost("/{jobId}/trigger", TriggerJobAsync)
            .WithName("AdminJobsTrigger")
            .WithSummary("Manually trigger a registered background job (R3 task 021)")
            .WithDescription("Dispatches the named IScheduledJob out-of-band with Trigger=ManualAdmin and a fresh correlationId (NFR-08). Returns 202 Accepted immediately with the persistent run id and dispatch timestamp — the admin client polls GET /api/admin/jobs/{jobId}/status for run outcome. 404 when the jobId is not registered.")
            .Produces<TriggerResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // Future expansion slots (DO NOT remove these comment headers — they reserve
        // demarcated areas for task 022 to append its handlers without merge conflict
        // risk against this task's work):
        //
        //   ===== Task 022 — GET  /api/admin/jobs/{jobId}/history?limit=N =============
        //   ===== Task 022 — POST /api/admin/jobs/{jobId}/enable ======================
        //   ===== Task 022 — POST /api/admin/jobs/{jobId}/disable =====================
        // ============================================================================

        return app;
    }

    // ============================================================================
    // ===== Task 020 handlers ====================================================
    // ============================================================================

    /// <summary>
    /// Handler for <c>GET /api/admin/jobs</c>. Enumerates the registry, joins each job with
    /// its definition (from <see cref="IBackgroundJobStore.LoadJobsAsync"/>) and most-recent
    /// run, and computes <c>NextScheduledOn</c> from the cron expression.
    /// </summary>
    /// <remarks>
    /// Jobs registered with <see cref="ScheduledJobRegistry"/> but missing a corresponding
    /// <see cref="BackgroundJobDefinition"/> in the store are still surfaced — they appear with
    /// <c>Enabled=false</c>, empty cron, and <c>NextScheduledOn=null</c>. This mirrors the
    /// <see cref="ScheduledJobHost"/>'s tolerance for the same condition (it logs Debug and
    /// skips dispatch). Operators can use this surface to detect "handler registered but no
    /// definition row exists" misconfigurations.
    /// </remarks>
    private static async Task<IResult> ListJobsAsync(
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var registered = registry.EnumerateAll();
        if (registered.Count == 0)
        {
            // Empty registry → empty list (200, not 404). The admin surface is "what's
            // registered", so "nothing is registered" is a valid 200 answer.
            return Results.Ok(Array.Empty<JobStatusSummary>());
        }

        var definitions = await store.LoadJobsAsync(cancellationToken).ConfigureAwait(false);
        var definitionsByJobId = definitions.ToDictionary(
            d => d.JobId,
            StringComparer.Ordinal);

        var summaries = new List<JobStatusSummary>(registered.Count);
        var nowUtc = DateTime.UtcNow;

        foreach (var job in registered)
        {
            var definition = definitionsByJobId.GetValueOrDefault(job.JobId);
            var summary = await BuildSummaryAsync(job, definition, store, nowUtc, logger, cancellationToken)
                .ConfigureAwait(false);
            summaries.Add(summary);
        }

        // Deterministic ordering for admin UI consistency. JobId is the stable
        // human-typeable key — alphabetic sort is the predictable contract.
        summaries.Sort(static (a, b) => string.CompareOrdinal(a.JobId, b.JobId));
        return Results.Ok(summaries);
    }

    /// <summary>
    /// Handler for <c>GET /api/admin/jobs/{jobId}/status</c>. Returns 404 when jobId is not
    /// registered with the registry; otherwise returns a detail view with the last 10 run
    /// records ordered newest-first.
    /// </summary>
    private static async Task<IResult> GetJobStatusAsync(
        string jobId,
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "jobId route parameter is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var job = registry.Resolve(jobId);
        if (job is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"No background job with jobId '{jobId}' is registered.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Load definition + recent runs in parallel — both Dataverse round-trips eventually,
        // independent of each other.
        var definitionsTask = store.LoadJobsAsync(cancellationToken);
        var recentRunsTask = store.GetRecentRunsAsync(jobId, DefaultRecentRunsLimit, cancellationToken);
        await Task.WhenAll(definitionsTask, recentRunsTask).ConfigureAwait(false);

        var definition = definitionsTask.Result
            .FirstOrDefault(d => string.Equals(d.JobId, jobId, StringComparison.Ordinal));
        var recentRuns = recentRunsTask.Result;

        var nowUtc = DateTime.UtcNow;
        var summary = BuildSummaryFromRuns(job, definition, recentRuns, nowUtc, logger);

        var detail = new JobStatusDetail(
            JobId: summary.JobId,
            DisplayName: summary.DisplayName,
            Description: summary.Description,
            Enabled: summary.Enabled,
            CronSchedule: summary.CronSchedule,
            LastRunStartedOn: summary.LastRunStartedOn,
            LastRunCompletedOn: summary.LastRunCompletedOn,
            LastRunStatus: summary.LastRunStatus,
            NextScheduledOn: summary.NextScheduledOn,
            RecentRuns: recentRuns.Select(ToJobRunDetail).ToArray());

        return Results.Ok(detail);
    }

    // ============================================================================
    // ===== Task 021 handlers ====================================================
    // ============================================================================

    /// <summary>
    /// Handler for <c>POST /api/admin/jobs/{jobId}/trigger</c>. Dispatches the registered
    /// <see cref="IScheduledJob"/> via <see cref="ScheduledJobHost.TriggerNowAsync"/> with
    /// <see cref="JobRunTrigger.ManualAdmin"/> and returns 202 Accepted immediately with the
    /// persistent run id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <c>202 Accepted</c> + <see cref="TriggerResponse"/> body + a <c>Location</c>
    /// header at <c>/api/admin/jobs/{jobId}/runs/{runId}</c> (the canonical "where to find
    /// this run later" path; even though task 020 exposes status at <c>.../status</c>, the
    /// runs/{runId} shape is the resource-style location admin clients expect for a created
    /// resource per Microsoft Learn admin API guidance).
    /// </para>
    /// <para>
    /// <see cref="JobNotFoundException"/> (thrown by the host when the jobId is not in
    /// <see cref="ScheduledJobRegistry"/>) maps to 404 ProblemDetails. The cancellation token
    /// here cancels the dispatch path only — once the host starts the background task, admin
    /// client cancellation does NOT interrupt the in-flight run (NFR-07 — only host shutdown
    /// can cancel an in-flight run).
    /// </para>
    /// </remarks>
    private static async Task<IResult> TriggerJobAsync(
        string jobId,
        ScheduledJobHost host,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "jobId route parameter is required",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var trigger = await host.TriggerNowAsync(jobId, parameters: null, cancellationToken)
                .ConfigureAwait(false);

            var response = new TriggerResponse(
                RunId: trigger.RunId,
                Status: trigger.Status,
                StartedAt: trigger.StartedAt);

            // 202 Accepted with Location pointing at the canonical run-resource path. The
            // body still carries the same TriggerResponse so admin clients that don't follow
            // Location (most do not for 202) still get the runId.
            return Results.Accepted($"/api/admin/jobs/{jobId}/runs/{trigger.RunId}", response);
        }
        catch (JobNotFoundException ex)
        {
            // Host's own message is sufficient; pass it through to the admin client for
            // troubleshooting (e.g., "did you spell the jobId right?").
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled (e.g., admin closed the tab). Return a clean response rather
            // than throwing — this is not a server error.
            logger.LogInformation(
                "Admin trigger for job '{JobId}' cancelled by caller before run row was persisted",
                jobId);
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    // ============================================================================
    // ===== Shared helpers (used by all task 020/021/022 handlers) ==============
    // ============================================================================

    private static async Task<JobStatusSummary> BuildSummaryAsync(
        IScheduledJob job,
        BackgroundJobDefinition? definition,
        IBackgroundJobStore store,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // For list view, pulling 1 recent run is sufficient — we only need the most-recent
        // start/complete/status for the summary projection.
        var recentRuns = await store.GetRecentRunsAsync(job.JobId, 1, cancellationToken)
            .ConfigureAwait(false);
        return BuildSummaryFromRuns(job, definition, recentRuns, nowUtc, logger);
    }

    private static JobStatusSummary BuildSummaryFromRuns(
        IScheduledJob job,
        BackgroundJobDefinition? definition,
        IReadOnlyList<BackgroundJobRunRecord> recentRuns,
        DateTime nowUtc,
        ILogger logger)
    {
        // recentRuns is newest-first per IBackgroundJobStore.GetRecentRunsAsync contract.
        // Index 0 is the most-recent run (may be in-progress or completed).
        var lastRun = recentRuns.Count > 0 ? recentRuns[0] : null;

        // Definition fields fall back to handler-supplied values when the store has no row —
        // this surfaces "handler registered but no definition" misconfigs in the admin UI
        // without 404'ing the entire list endpoint.
        var displayName = definition?.DisplayName ?? job.DisplayName;
        var description = definition?.Description ?? job.Description;
        var enabled = definition?.Enabled ?? false;
        var cronSchedule = definition?.CronSchedule ?? string.Empty;

        // Compute next occurrence only when enabled AND cron is parseable. Cron-parse failures
        // are logged but do NOT surface a 500 to the admin — the row appears with
        // NextScheduledOn=null and the operator can investigate via the underlying definition.
        DateTimeOffset? nextScheduledOn = null;
        if (enabled && !string.IsNullOrWhiteSpace(cronSchedule))
        {
            try
            {
                var cron = ParseCron(cronSchedule);
                var next = cron.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
                if (next.HasValue)
                {
                    nextScheduledOn = new DateTimeOffset(next.Value, TimeSpan.Zero);
                }
            }
            catch (CronFormatException ex)
            {
                logger.LogWarning(
                    ex,
                    "Background job '{JobId}' has invalid cron expression '{Cron}' — NextScheduledOn omitted from admin response",
                    job.JobId, cronSchedule);
            }
        }

        return new JobStatusSummary(
            JobId: job.JobId,
            DisplayName: displayName,
            Description: description,
            Enabled: enabled,
            CronSchedule: cronSchedule,
            LastRunStartedOn: lastRun?.StartedOn,
            LastRunCompletedOn: lastRun?.CompletedOn,
            LastRunStatus: lastRun?.Status,
            NextScheduledOn: nextScheduledOn);
    }

    private static JobRunDetail ToJobRunDetail(BackgroundJobRunRecord record)
    {
        return new JobRunDetail(
            RunId: record.RunId,
            Trigger: record.Trigger.ToString(),
            CorrelationId: record.CorrelationId,
            StartedOn: record.StartedOn,
            CompletedOn: record.CompletedOn,
            Status: record.Status,
            ErrorMessage: record.ErrorMessage,
            ProcessedItems: record.ProcessedItems,
            DurationMs: record.CompletedOn is null ? null : (long)record.Duration.TotalMilliseconds);
    }

    /// <summary>
    /// Parse a cron expression supporting both 5-field (minute-precision; spec.md default for
    /// <c>sprk_cronschedule</c>) and 6-field (second-precision) syntaxes. Mirrors the field-count
    /// detection in <see cref="ScheduledJobHost"/>.<c>ParseCron</c> (kept internal there) so the
    /// admin endpoints accept the same set of cron expressions the host actually schedules.
    /// </summary>
    private static CronExpression ParseCron(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length == 6
            ? CronExpression.Parse(expression, CronFormat.IncludeSeconds)
            : CronExpression.Parse(expression);
    }
}
