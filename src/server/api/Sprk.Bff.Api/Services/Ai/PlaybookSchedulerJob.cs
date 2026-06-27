using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Spaarke.Scheduling reference consumer that fans out notification-mode playbooks for all
/// active users on the cadence dictated by each playbook's <c>sprk_configjson</c>. Migrated
/// from the legacy <c>PlaybookSchedulerService : BackgroundService</c> per R3 task 023 / FR-2.8 / D2.
/// </summary>
/// <remarks>
/// <para><b>Spec coverage:</b></para>
/// <list type="bullet">
///   <item>FR-2.8 — single <c>sprk_backgroundjob</c> row <see cref="JobIdConstant"/> that
///     internally fans out across the 7 active playbooks (preserves the legacy 1:1 cadence).</item>
///   <item>D2 — single-row fan-out architecture; per-playbook "Run Now" deferred to a follow-up.</item>
///   <item>Q1 (2026-06-20 owner decision) — each fanned-out child playbook receives a
///     <i>fresh</i> correlationId. The parent run's correlationId
///     (<see cref="JobRunContext.CorrelationId"/>) is preserved on the
///     <c>sprk_backgroundjobrun</c> row; the children are recorded in
///     <see cref="JobRunResult.ResultJson"/> as a JSON document so operators can join
///     parent ↔ children.</item>
///   <item>NFR-04 — cadence preservation: the in-process <see cref="ScheduledJobHost"/>
///     ticks this job hourly (cron <c>0 * * * *</c>, seeded by
///     <c>SchedulingModule.SeedNotificationPlaybookScheduler</c>); the legacy
///     <c>PlaybookSchedulerService.DefaultTickInterval = TimeSpan.FromHours(1)</c> is matched
///     1:1. The per-playbook due-check (<see cref="IsPlaybookDue"/>) is unchanged — playbooks
///     still respect their own <c>sprk_configjson.schedule.frequency</c> (hourly / daily / weekly)
///     keyed off <c>sprk_lastrundate</c>.</item>
///   <item>NFR-07 — every async hop observes <see cref="CancellationToken"/>; the
///     fan-out <see cref="Parallel.ForEachAsync"/> is cancelable so host shutdown
///     drains within the 30s ceiling.</item>
///   <item>NFR-08 — fresh correlationId per child playbook (<see cref="Guid.NewGuid"/>.ToString("N")).</item>
///   <item>ADR-001 — pure in-process; no Azure Function / external scheduler.</item>
///   <item>ADR-010 — concrete singleton; <see cref="IScheduledJob"/> is the legitimate
///     framework-defined seam.</item>
///   <item>ADR-013 — lives under <c>Services/Ai/</c> alongside its AI-internal dependencies
///     (<c>IPlaybookOrchestrationService</c>, <c>IGenericEntityService</c>).</item>
/// </list>
/// <para><b>Migration vs legacy <c>PlaybookSchedulerService</c>:</b></para>
/// <list type="bullet">
///   <item>The legacy class owned its own <c>BackgroundService.ExecuteAsync</c> loop + a
///     hand-rolled <see cref="TimeSpan"/> tick. The host-loop is now
///     <see cref="ScheduledJobHost"/> (cron-driven) and this class implements only the
///     per-tick fan-out body (<see cref="ExecuteAsync"/>).</item>
///   <item>The legacy class held an in-memory <c>ConcurrentDictionary</c> seeded once at startup
///     from <c>sprk_lastrundate</c>. This job re-reads <c>sprk_lastrundate</c> from Dataverse
///     on every tick — the per-tick read is cheap (1 query, ~7 rows) and removes restart-window
///     gaps where the in-memory dictionary differed from the canonical row. The legacy seed +
///     write-back semantics are now collapsed into the per-tick query, with <c>sprk_lastrundate</c>
///     remaining the canonical source of truth.</item>
///   <item>Per-playbook failure tolerance preserved: one failing playbook does not abort the
///     remaining 6 in the same tick.</item>
///   <item>User-loop parallelism (<see cref="MaxDegreeOfParallelism"/> = 5) preserved verbatim.</item>
///   <item>Coordination with task 024: schedule-config migration — task 024 may relocate
///     <c>sprk_configjson.schedule</c> to a first-class column. Until that lands, this job
///     reads from <c>sprk_configjson</c> via <see cref="ParseScheduleConfig"/> (unchanged from
///     the legacy implementation). Task 024 will replace <see cref="ParseScheduleConfig"/> with
///     a column read but keep the <see cref="ScheduleConfig"/> record and
///     <see cref="IsPlaybookDue"/> semantics intact.</item>
/// </list>
/// </remarks>
public sealed class PlaybookSchedulerJob : IScheduledJob
{
    /// <summary>
    /// Canonical <see cref="IScheduledJob.JobId"/> — matches the seeded <c>sprk_backgroundjob.sprk_jobid</c>
    /// row created by <c>SchedulingModule.SeedNotificationPlaybookScheduler</c>. Stable contract; do
    /// not rename without coordinating the Dataverse seed.
    /// </summary>
    public const string JobIdConstant = "notification-playbook-scheduler";

    /// <summary>
    /// Maximum users processed in parallel per playbook fan-out. Preserved from the legacy
    /// <c>PlaybookSchedulerService.MaxDegreeOfParallelism = 5</c>.
    /// </summary>
    private const int MaxDegreeOfParallelism = 5;

    /// <summary>
    /// Option-set value for <c>sprk_playbooktype = Notification</c>. Preserved from the legacy
    /// service. Centralized here so task 024 (config migration) can audit the single reference.
    /// </summary>
    private const int NotificationPlaybookType = 2;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Writer options: omit nulls so an empty <c>children</c> array doesn't bloat the row.
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaybookSchedulerJob> _logger;

    public PlaybookSchedulerJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlaybookSchedulerJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Notification Playbook Scheduler";

    /// <inheritdoc />
    public string Description =>
        "Periodically executes notification-mode playbooks (sprk_playbooktype=2) for all active users. " +
        "Each fanned-out playbook gets a fresh correlationId; children recorded in result JSON (FR-2.8).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();
        var children = new List<ChildPlaybookRun>();

        try
        {
            _logger.LogInformation(
                "PlaybookSchedulerJob tick started — parentCorrelationId={ParentCorrelationId} runId={RunId}",
                context.CorrelationId, context.RunId);

            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider
                .GetRequiredService<Spaarke.Dataverse.IGenericEntityService>();

            var playbooks = await QueryNotificationPlaybooksAsync(entityService, cancellationToken)
                .ConfigureAwait(false);

            if (playbooks.Count == 0)
            {
                _logger.LogDebug(
                    "PlaybookSchedulerJob found no active notification playbooks (parentCorrelationId={ParentCorrelationId})",
                    context.CorrelationId);
                sw.Stop();
                return new JobRunResult(
                    Success: true,
                    ErrorMessage: null,
                    ProcessedItems: 0,
                    Duration: sw.Elapsed,
                    ResultJson: SerializeChildren(children));
            }

            _logger.LogInformation(
                "PlaybookSchedulerJob found {Count} notification playbook(s) — fanning out",
                playbooks.Count);

            // Per-playbook iteration (sequential like the legacy service) — keeps per-playbook
            // due-checks ordered + simplifies error isolation. Within each playbook, users are
            // processed in parallel via Parallel.ForEachAsync(MaxDegreeOfParallelism=5).
            foreach (var playbook in playbooks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "PlaybookSchedulerJob cancellation observed mid-tick — returning partial fan-out (children processed: {Count})",
                        children.Count);
                    break;
                }

                var playbookId = playbook.Id;
                var playbookName = playbook.GetAttributeValue<string>("sprk_name") ?? "(unnamed)";

                // Q1: fresh correlationId per child playbook. Recorded in ResultJson so operators
                // can join parent ↔ children. Using N format (no dashes) for consistency with
                // ScheduledJobHost.DispatchAndAdvance which uses Guid.NewGuid().ToString("N").
                var childCorrelationId = Guid.NewGuid().ToString("N");

                try
                {
                    var scheduleConfig = ParseScheduleConfig(playbook);
                    var lastRun = ReadLastRunFromEntity(playbook);

                    if (!IsPlaybookDue(lastRun, scheduleConfig))
                    {
                        _logger.LogDebug(
                            "Playbook {PlaybookId} ({Name}) not due — lastRun={LastRun} freq={Frequency} (skipped; childCorrelationId={ChildCorrelationId})",
                            playbookId, playbookName,
                            lastRun?.ToString("u") ?? "never",
                            scheduleConfig.Frequency,
                            childCorrelationId);

                        children.Add(new ChildPlaybookRun(
                            PlaybookId: playbookId,
                            PlaybookName: playbookName,
                            CorrelationId: childCorrelationId,
                            Status: "Skipped",
                            UserCount: 0,
                            SuccessCount: 0,
                            FailureCount: 0,
                            ErrorMessage: null));
                        continue;
                    }

                    _logger.LogInformation(
                        "Dispatching playbook {PlaybookId} ({Name}) — freq={Frequency} childCorrelationId={ChildCorrelationId} parentCorrelationId={ParentCorrelationId}",
                        playbookId, playbookName, scheduleConfig.Frequency,
                        childCorrelationId, context.CorrelationId);

                    var (userCount, successCount, failureCount) = await ProcessPlaybookForAllUsersAsync(
                        scope.ServiceProvider,
                        playbookId,
                        childCorrelationId,
                        cancellationToken).ConfigureAwait(false);

                    // Persist sprk_lastrundate AFTER successful fan-out (matches legacy
                    // PlaybookSchedulerService.PersistLastRunTimestampAsync). Failure here is
                    // non-fatal — the next tick re-reads the (still-stale) value and will
                    // re-dispatch.
                    var now = DateTimeOffset.UtcNow;
                    await PersistLastRunTimestampAsync(entityService, playbookId, now, cancellationToken)
                        .ConfigureAwait(false);

                    children.Add(new ChildPlaybookRun(
                        PlaybookId: playbookId,
                        PlaybookName: playbookName,
                        CorrelationId: childCorrelationId,
                        Status: failureCount == 0 ? "Succeeded" : "PartialFailure",
                        UserCount: userCount,
                        SuccessCount: successCount,
                        FailureCount: failureCount,
                        ErrorMessage: null));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    children.Add(new ChildPlaybookRun(
                        PlaybookId: playbookId,
                        PlaybookName: playbookName,
                        CorrelationId: childCorrelationId,
                        Status: "Cancelled",
                        UserCount: 0,
                        SuccessCount: 0,
                        FailureCount: 0,
                        ErrorMessage: "Cancelled by host shutdown"));
                    break;
                }
                catch (Exception ex)
                {
                    // Per-playbook isolation — log + record + continue with next playbook.
                    _logger.LogError(
                        ex,
                        "Playbook {PlaybookId} ({Name}) fan-out failed — continuing with next playbook (childCorrelationId={ChildCorrelationId})",
                        playbookId, playbookName, childCorrelationId);

                    children.Add(new ChildPlaybookRun(
                        PlaybookId: playbookId,
                        PlaybookName: playbookName,
                        CorrelationId: childCorrelationId,
                        Status: "Failed",
                        UserCount: 0,
                        SuccessCount: 0,
                        FailureCount: 0,
                        ErrorMessage: ex.Message));
                }
            }

            sw.Stop();

            _logger.LogInformation(
                "PlaybookSchedulerJob tick completed — playbooksProcessed={Total} succeeded={Succeeded} failed={Failed} skipped={Skipped} duration={DurationMs}ms",
                children.Count,
                children.Count(c => c.Status == "Succeeded"),
                children.Count(c => c.Status is "Failed" or "PartialFailure"),
                children.Count(c => c.Status == "Skipped"),
                (long)sw.Elapsed.TotalMilliseconds);

            // Success is true even if individual playbooks failed — per-playbook errors are
            // reported in children[].errorMessage. Only an unhandled exception ABOVE the per-
            // playbook try/catch turns the run into a failure (see outer catch).
            return new JobRunResult(
                Success: true,
                ErrorMessage: null,
                ProcessedItems: children.Count,
                Duration: sw.Elapsed,
                ResultJson: SerializeChildren(children));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "PlaybookSchedulerJob cancelled before completion — partial fan-out (children={Count})",
                children.Count);
            return new JobRunResult(
                Success: false,
                ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                ProcessedItems: children.Count,
                Duration: sw.Elapsed,
                ResultJson: SerializeChildren(children));
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "PlaybookSchedulerJob tick threw unexpectedly (parentCorrelationId={ParentCorrelationId})",
                context.CorrelationId);
            return new JobRunResult(
                Success: false,
                ErrorMessage: ex.Message,
                ProcessedItems: children.Count,
                Duration: sw.Elapsed,
                ResultJson: SerializeChildren(children));
        }
    }

    /// <summary>
    /// Queries <c>sprk_analysisplaybook</c> where <c>sprk_playbooktype = Notification (2)</c>
    /// and <c>statecode = Active (0)</c>. Returns the entities verbatim so per-playbook attrs
    /// (<c>sprk_configjson</c>, <c>sprk_lastrundate</c>) can be inspected without an extra round-trip.
    /// </summary>
    /// <remarks>Behavior preserved from legacy <c>PlaybookSchedulerService.QueryNotificationPlaybooksAsync</c>.
    /// Static so future task-024 refactor can extract this as a helper if needed.</remarks>
    private static async Task<List<Entity>> QueryNotificationPlaybooksAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        CancellationToken ct)
    {
        var query = new QueryExpression("sprk_analysisplaybook")
        {
            ColumnSet = new ColumnSet(
                "sprk_analysisplaybookid",
                "sprk_name",
                "sprk_configjson",
                "sprk_lastrundate"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_playbooktype", ConditionOperator.Equal, NotificationPlaybookType),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active
                }
            }
        };

        var result = await entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.ToList();
    }

    /// <summary>
    /// Queries active <c>systemuser</c> records (non-disabled, non-integration). Preserved
    /// verbatim from legacy <c>PlaybookSchedulerService.QueryActiveUsersAsync</c>.
    /// </summary>
    private static async Task<List<Entity>> QueryActiveUsersAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        CancellationToken ct)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false),
                    new ConditionExpression("accessmode", ConditionOperator.NotEqual, 4) // Exclude non-interactive (4)
                }
            }
        };

        var result = await entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.ToList();
    }

    /// <summary>
    /// Processes one playbook for all active users in parallel
    /// (<see cref="MaxDegreeOfParallelism"/> = 5). Returns (userCount, successCount, failureCount)
    /// so the caller can roll the metrics into the parent <see cref="ChildPlaybookRun"/>.
    /// </summary>
    /// <remarks>Behavior preserved from legacy <c>ProcessPlaybookForAllUsersAsync</c> with two
    /// additions: (1) returns tuple metrics instead of writing them to instance state, (2) accepts
    /// <paramref name="childCorrelationId"/> for log correlation. Per-user failure isolation
    /// preserved — a single failing user does NOT abort the remaining parallel users.</remarks>
    private async Task<(int userCount, int successCount, int failureCount)> ProcessPlaybookForAllUsersAsync(
        IServiceProvider serviceProvider,
        Guid playbookId,
        string childCorrelationId,
        CancellationToken ct)
    {
        var entityService = serviceProvider.GetRequiredService<Spaarke.Dataverse.IGenericEntityService>();
        var users = await QueryActiveUsersAsync(entityService, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Playbook {PlaybookId} fan-out — userCount={UserCount} maxParallelism={MaxParallelism} childCorrelationId={ChildCorrelationId}",
            playbookId, users.Count, MaxDegreeOfParallelism, childCorrelationId);

        if (users.Count == 0)
        {
            // No users to process — the playbook is still considered processed (its
            // sprk_lastrundate is updated by the caller); 0/0/0 metrics surfaced.
            return (0, 0, 0);
        }

        var tenantId = _configuration["TENANT_ID"] ?? "default";
        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(
            users,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = ct
            },
            async (user, userCt) =>
            {
                var userId = user.Id;
                var userName = user.GetAttributeValue<string>("fullname") ?? "(unknown)";

                try
                {
                    using var userScope = _scopeFactory.CreateScope();
                    var orchestrationService = userScope.ServiceProvider
                        .GetRequiredService<IPlaybookOrchestrationService>();

                    var request = new PlaybookRunRequest
                    {
                        PlaybookId = playbookId,
                        DocumentIds = Array.Empty<Guid>(),
                        UserContext = $"Scheduled notification run for user {userName} (childCorrelationId={childCorrelationId})",
                        Parameters = new Dictionary<string, string>
                        {
                            ["userId"] = userId.ToString(),
                            ["userName"] = userName
                        }
                    };

                    await foreach (var evt in orchestrationService.ExecuteAppOnlyAsync(request, tenantId, userCt)
                        .ConfigureAwait(false))
                    {
                        if (evt.Type == PlaybookEventType.RunFailed)
                        {
                            _logger.LogWarning(
                                "Playbook {PlaybookId} failed for user {UserId} ({UserName}): {Error} (childCorrelationId={ChildCorrelationId})",
                                playbookId, userId, userName, evt.Error, childCorrelationId);
                            Interlocked.Increment(ref failureCount);
                            return;
                        }
                    }

                    Interlocked.Increment(ref successCount);

                    _logger.LogDebug(
                        "Playbook {PlaybookId} completed for user {UserId} ({UserName}) childCorrelationId={ChildCorrelationId}",
                        playbookId, userId, userName, childCorrelationId);
                }
                catch (OperationCanceledException) when (userCt.IsCancellationRequested)
                {
                    // Suppress — Parallel.ForEachAsync propagates cancellation; the caller's
                    // OperationCanceledException catch in ExecuteAsync handles it.
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failureCount);
                    _logger.LogWarning(
                        ex,
                        "Playbook {PlaybookId} failed for user {UserId} ({UserName}) childCorrelationId={ChildCorrelationId}",
                        playbookId, userId, userName, childCorrelationId);
                }
            }).ConfigureAwait(false);

        _logger.LogInformation(
            "Playbook {PlaybookId} fan-out complete — success={SuccessCount} failures={FailureCount} total={TotalCount} childCorrelationId={ChildCorrelationId}",
            playbookId, successCount, failureCount, users.Count, childCorrelationId);

        return (users.Count, successCount, failureCount);
    }

    /// <summary>
    /// Parses the schedule from a playbook's <c>sprk_configjson</c>. Defaults to daily at 06:00 UTC
    /// on missing or unparseable config. Behavior preserved from legacy
    /// <c>PlaybookSchedulerService.ParseScheduleConfig</c>.
    /// </summary>
    /// <remarks>
    /// Coordination with task 024: this method will be replaced when task 024 migrates the schedule
    /// from <c>sprk_configjson</c> to a first-class column (or a dedicated table). The
    /// <see cref="ScheduleConfig"/> record + <see cref="IsPlaybookDue"/> are the stable surface
    /// task 024 must preserve.
    /// </remarks>
    internal ScheduleConfig ParseScheduleConfig(Entity playbook)
    {
        ArgumentNullException.ThrowIfNull(playbook);
        var configJson = playbook.GetAttributeValue<string>("sprk_configjson");

        if (string.IsNullOrWhiteSpace(configJson))
        {
            return ScheduleConfig.Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);

            if (doc.RootElement.TryGetProperty("schedule", out var scheduleElement))
            {
                var frequency = scheduleElement.TryGetProperty("frequency", out var freqProp)
                    ? freqProp.GetString() ?? "daily"
                    : "daily";

                var time = scheduleElement.TryGetProperty("time", out var timeProp)
                    ? timeProp.GetString() ?? "06:00"
                    : "06:00";

                return new ScheduleConfig(frequency, time);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse schedule config for playbook {PlaybookId} — using defaults",
                playbook.Id);
        }
        _ = JsonReadOptions; // referenced to keep reader options binding in IntelliSense scope; STJ JsonDocument doesn't take options.
        return ScheduleConfig.Default;
    }

    /// <summary>
    /// Determines if a playbook is due for execution given <paramref name="lastRun"/> (canonical
    /// from <c>sprk_lastrundate</c>) and the parsed <paramref name="schedule"/>. Behavior preserved
    /// from legacy <c>PlaybookSchedulerService.IsPlaybookDue</c> minus the in-memory dictionary
    /// (we read <c>sprk_lastrundate</c> per-tick instead).
    /// </summary>
    internal static bool IsPlaybookDue(DateTimeOffset? lastRun, ScheduleConfig schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (lastRun is null)
        {
            // Never run before — due immediately.
            return true;
        }

        var elapsed = DateTimeOffset.UtcNow - lastRun.Value;
        return schedule.Frequency.ToLowerInvariant() switch
        {
            "hourly" => elapsed >= TimeSpan.FromHours(1),
            "daily" => elapsed >= TimeSpan.FromHours(24),
            "weekly" => elapsed >= TimeSpan.FromDays(7),
            _ => elapsed >= TimeSpan.FromHours(24) // Default to daily.
        };
    }

    /// <summary>
    /// Reads <c>sprk_lastrundate</c> off the playbook entity, normalizing to UTC
    /// <see cref="DateTimeOffset"/>. Returns <c>null</c> when the attribute is absent
    /// (never-run case).
    /// </summary>
    internal static DateTimeOffset? ReadLastRunFromEntity(Entity playbook)
    {
        ArgumentNullException.ThrowIfNull(playbook);
        var lastRunDate = playbook.GetAttributeValue<DateTime?>("sprk_lastrundate");
        return lastRunDate.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(lastRunDate.Value, DateTimeKind.Utc), TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// Persists the per-playbook last-run timestamp to Dataverse. Failure is non-fatal —
    /// next tick re-reads the (stale) value and re-dispatches. Behavior preserved from legacy
    /// <c>PlaybookSchedulerService.PersistLastRunTimestampAsync</c>.
    /// </summary>
    private async Task PersistLastRunTimestampAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        Guid playbookId,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        try
        {
            await entityService.UpdateAsync(
                "sprk_analysisplaybook",
                playbookId,
                new Dictionary<string, object>
                {
                    ["sprk_lastrundate"] = timestamp.UtcDateTime
                },
                ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persisted last-run timestamp for playbook {PlaybookId}: {Timestamp:u}",
                playbookId, timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist last-run timestamp for playbook {PlaybookId} — next tick will re-dispatch",
                playbookId);
        }
    }

    /// <summary>
    /// Serialize the children list as the <see cref="JobRunResult.ResultJson"/> payload.
    /// Shape: <c>{ "children": [ { ... }, ... ] }</c>. Always emits an object (never null) so
    /// admin tooling can parse without null-checks. The container key is <c>children</c> for
    /// forward-compat (future fields like <c>summary</c>, <c>warnings</c> can co-exist without
    /// breaking parsers).
    /// </summary>
    internal static string SerializeChildren(IReadOnlyList<ChildPlaybookRun> children)
    {
        var payload = new ResultPayload(children);
        return JsonSerializer.Serialize(payload, JsonWriteOptions);
    }

    /// <summary>
    /// Per-playbook record in the parent run's <see cref="JobRunResult.ResultJson"/> blob.
    /// </summary>
    /// <param name="PlaybookId">Dataverse playbook id (<c>sprk_analysisplaybookid</c>).</param>
    /// <param name="PlaybookName">Playbook display name (<c>sprk_name</c>), or <c>"(unnamed)"</c> on null.</param>
    /// <param name="CorrelationId">Fresh per-child correlationId (Q1; <see cref="Guid.NewGuid"/> N-format).</param>
    /// <param name="Status">
    /// One of: <c>"Succeeded"</c> (all users OK), <c>"PartialFailure"</c> (some users failed),
    /// <c>"Failed"</c> (fan-out aborted), <c>"Skipped"</c> (not due per schedule), <c>"Cancelled"</c>
    /// (host shutdown).
    /// </param>
    /// <param name="UserCount">Total active users targeted for this playbook.</param>
    /// <param name="SuccessCount">Users where orchestration completed without a RunFailed event.</param>
    /// <param name="FailureCount">Users where orchestration emitted RunFailed OR threw an exception.</param>
    /// <param name="ErrorMessage">
    /// Per-playbook error (only set on Status=Failed or Cancelled); <c>null</c> otherwise — per-user
    /// errors are not surfaced individually (kept in logs to bound payload size; per design
    /// guidance "keep the payload small").
    /// </param>
    public sealed record ChildPlaybookRun(
        Guid PlaybookId,
        string PlaybookName,
        string CorrelationId,
        string Status,
        int UserCount,
        int SuccessCount,
        int FailureCount,
        string? ErrorMessage);

    /// <summary>Wrapper object for the JSON payload. Single field <see cref="Children"/>.</summary>
    private sealed record ResultPayload(IReadOnlyList<ChildPlaybookRun> Children);

    /// <summary>
    /// Per-playbook schedule descriptor parsed from <c>sprk_configjson</c>. <see cref="Default"/>
    /// is daily at 06:00 UTC. <see cref="Time"/> is currently informational (the legacy service
    /// also did not enforce time-of-day — the cron host triggers the tick and per-playbook
    /// elapsed-since-last-run drives due-check).
    /// </summary>
    internal sealed record ScheduleConfig(string Frequency, string Time)
    {
        public static readonly ScheduleConfig Default = new("daily", "06:00");
    }
}
