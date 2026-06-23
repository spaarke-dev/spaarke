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

    // ── Task 022 — /history limit clamps ─────────────────────────────────────────────
    // Default 50 (5x the per-job /status surface; admins doing history triage want
    // more rows than the summary). Hard cap 500 to prevent a poison query from
    // pulling unbounded run history once the Dataverse-backed store ships (task 023+).
    private const int DefaultHistoryLimit = 50;
    private const int MaxHistoryLimit = 500;

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
        // ===== Task 022 — GET /api/admin/jobs/{jobId}/history?limit=N ===============
        // ============================================================================
        // Returns the last N run records for the given job, ordered newest-first. Default
        // limit 50, hard cap 500 (defends against unbounded history pulls once the
        // Dataverse-backed store ships). 404 if jobId is not registered.
        group.MapGet("/{jobId}/history", GetJobHistoryAsync)
            .WithName("AdminJobsHistory")
            .WithSummary("Get recent run history for a specific background job")
            .WithDescription("Returns the most-recent run records for jobId, ordered newest-first. Optional `limit` query string (default 50, capped at 500). 404 when jobId is not registered with the in-process ScheduledJobRegistry.")
            .Produces<IReadOnlyList<JobRunDetail>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // ===== Task 022 — POST /api/admin/jobs/{jobId}/enable ======================
        // ============================================================================
        // Flips sprk_backgroundjob.sprk_enabled = true (in-memory: BackgroundJobDefinition.Enabled).
        // Triggers ScheduledJobHost.RefreshDefinitionsAsync so the new state takes effect on the
        // very next scheduling-loop tick (not the hourly refresh — per spec FR-2.6, runtime
        // re-evaluation is required). 404 when jobId has no definition in the store.
        group.MapPost("/{jobId}/enable", EnableJobAsync)
            .WithName("AdminJobsEnable")
            .WithSummary("Enable a registered background job")
            .WithDescription("Sets BackgroundJobDefinition.Enabled=true for jobId and triggers an immediate ScheduledJobHost refresh so the change takes effect on the next scheduling-loop tick. Returns 204 No Content on success. 404 when jobId has no definition in the store.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // ===== Task 022 — POST /api/admin/jobs/{jobId}/disable =====================
        // ============================================================================
        // Mirror of /enable — sets Enabled=false. Disabled jobs remain visible in the admin
        // surface (list + status + history) but the host MUST NOT dispatch them on cron tick.
        group.MapPost("/{jobId}/disable", DisableJobAsync)
            .WithName("AdminJobsDisable")
            .WithSummary("Disable a registered background job")
            .WithDescription("Sets BackgroundJobDefinition.Enabled=false for jobId and triggers an immediate ScheduledJobHost refresh so the change takes effect on the next scheduling-loop tick. Disabled jobs remain in the admin surface but are skipped by the cron loop. Returns 204 No Content on success. 404 when jobId has no definition in the store.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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
    // ===== Task 022 handlers ====================================================
    // ============================================================================

    /// <summary>
    /// Handler for <c>GET /api/admin/jobs/{jobId}/history?limit=N</c>. Returns the last N run
    /// records for the job, ordered newest-first. <c>limit</c> defaults to
    /// <see cref="DefaultHistoryLimit"/> (50) and is clamped to <see cref="MaxHistoryLimit"/> (500)
    /// to defend against unbounded history pulls once the Dataverse-backed store ships.
    /// </summary>
    /// <remarks>
    /// 404 when <paramref name="jobId"/> is not registered with the registry. We deliberately
    /// check the <i>registry</i> (handler) not the <i>store</i> (definition) — same precedent
    /// as <see cref="GetJobStatusAsync"/> so handler-registered-but-undefined jobs return an
    /// empty history list rather than 404. Status surface and history surface 404 on the same
    /// trigger (unknown handler), which keeps the admin client's mental model consistent.
    /// </remarks>
    private static async Task<IResult> GetJobHistoryAsync(
        string jobId,
        [FromQuery] int? limit,
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

        if (registry.Resolve(jobId) is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"No background job with jobId '{jobId}' is registered.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Clamp limit: default 50 if absent or non-positive, hard cap at 500. The store also
        // clamps to >= 1 but defending the upper bound is the endpoint's job (the store has no
        // notion of "admin reasonable bound" — its contract just says limit > 0).
        var effectiveLimit = limit is null or <= 0
            ? DefaultHistoryLimit
            : Math.Min(limit.Value, MaxHistoryLimit);

        var runs = await store.GetRecentRunsAsync(jobId, effectiveLimit, cancellationToken)
            .ConfigureAwait(false);

        // Project to the same JobRunDetail shape used by /status — admin clients deserialize
        // both with the same type. Store guarantees newest-first ordering per its contract.
        var details = runs.Select(ToJobRunDetail).ToArray();
        return Results.Ok(details);
    }

    /// <summary>
    /// Handler for <c>POST /api/admin/jobs/{jobId}/enable</c>. Flips the definition's
    /// <c>Enabled</c> flag to <c>true</c> in the store, then triggers an immediate
    /// <see cref="ScheduledJobHost.RefreshDefinitionsAsync"/> so the host rebuilds its scheduling
    /// state on the next tick (not the hourly refresh).
    /// </summary>
    private static Task<IResult> EnableJobAsync(
        string jobId,
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ScheduledJobHost host,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(jobId, enabled: true, registry, store, host, logger, cancellationToken);

    /// <summary>
    /// Handler for <c>POST /api/admin/jobs/{jobId}/disable</c>. Same as <see cref="EnableJobAsync"/>
    /// but flips to <c>false</c>. Disabled jobs remain visible in the admin surface
    /// (list/status/history) but the host's scheduling loop skips them per
    /// <see cref="IBackgroundJobStore.LoadJobsAsync"/> contract.
    /// </summary>
    private static Task<IResult> DisableJobAsync(
        string jobId,
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ScheduledJobHost host,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
        SetEnabledAsync(jobId, enabled: false, registry, store, host, logger, cancellationToken);

    /// <summary>
    /// Shared body for <see cref="EnableJobAsync"/> and <see cref="DisableJobAsync"/>: validate
    /// route param, 404 if no definition exists for the job, mutate via
    /// <see cref="IBackgroundJobStore.SetEnabledAsync"/>, then force-refresh the host so the new
    /// state takes effect immediately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// We 404 on missing <i>definition</i> (not missing handler) here because enable/disable
    /// only makes sense for jobs that have a persisted definition row to mutate. A
    /// handler-registered-but-undefined job has nothing to flip — surfacing this as 404 makes
    /// the missing-definition condition explicit to the admin caller.
    /// </para>
    /// <para>
    /// Host refresh failures are NOT propagated to the admin — the state mutation is the
    /// durable change; the in-memory host refresh is a best-effort optimization (the hourly
    /// refresh will pick up the change as a fallback). Logged at Warning for ops visibility.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SetEnabledAsync(
        string jobId,
        bool enabled,
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ScheduledJobHost host,
        ILogger logger,
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

        var updated = await store.SetEnabledAsync(jobId, enabled, cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"No background job definition with jobId '{jobId}' exists in the store.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Force-refresh the host so the scheduling loop observes the new Enabled flag on its
        // next tick. Best-effort — failure here doesn't roll back the durable mutation. The
        // host's own hourly refresh is the backstop.
        try
        {
            await host.RefreshDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled — the store mutation already succeeded, so we still return 204.
            // The host's next periodic refresh will pick up the change.
            logger.LogInformation(
                "Admin {Action} for job '{JobId}' succeeded in store; host refresh skipped (caller cancelled). Hourly refresh will pick up the change.",
                enabled ? "enable" : "disable", jobId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Admin {Action} for job '{JobId}' succeeded in store but host RefreshDefinitionsAsync threw — change will take effect at the next hourly refresh",
                enabled ? "enable" : "disable", jobId);
        }

        logger.LogInformation(
            "Admin {Action} succeeded for background job '{JobId}'",
            enabled ? "enable" : "disable", jobId);

        return Results.NoContent();
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
