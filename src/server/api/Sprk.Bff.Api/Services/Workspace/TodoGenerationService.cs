using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Dataverse;
using EventStatusCode = Sprk.Bff.Api.Api.Events.Dtos.EventStatusCode;

namespace Sprk.Bff.Api.Services.Workspace;

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for the <see cref="TodoGenerationService"/>.
/// Binds from appsettings.json section "TodoGeneration".
/// </summary>
public sealed class TodoGenerationOptions
{
    public const string SectionName = "TodoGeneration";

    /// <summary>
    /// Interval between successive runs in hours. Default: 24. Since task 152 (ADR-052 §1 migration to
    /// <see cref="IScheduledJob"/>) this and <see cref="StartHourUtc"/> compile to the job's cron schedule at startup
    /// (<see cref="TodoGenerationService.BuildCronSchedule"/>); a change needs a restart.
    /// </summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>UTC hour at which the first run of the day fires (0-23). Default: 2 (2 AM UTC).</summary>
    public int StartHourUtc { get; set; } = 2;

    /// <summary>Number of days before a deadline that triggers a to-do. Default: 14.</summary>
    public int DeadlineWindowDays { get; set; } = 14;

    /// <summary>Budget utilization percentage threshold that triggers a to-do. Default: 85.</summary>
    public decimal BudgetAlertThresholdPercent { get; set; } = 85m;

    /// <summary>
    /// Feature gate for event-sourced To Do generation — Rules 1 (overdue events) and
    /// 3 (deadline proximity), which read <c>sprk_event</c>. Default <c>false</c> =
    /// <b>dry-run</b>: the rules query real events (via <see cref="IEventDataverseService"/>)
    /// and LOG how many To Dos they would create, but create none — so an operator can
    /// validate first-run volume, the same-name dedupe, and notification side-effects
    /// before enabling. Set <c>true</c> to create for real.
    /// <para>
    /// Introduced by smart-todo-r5 (2026-08-17, "Option A gated") to fix the latent bug
    /// where Rules 1 &amp; 3 routed event queries through the composite
    /// <c>IDataverseService</c> whose <c>QueryEventsAsync</c> is a silent-empty stub, so
    /// they produced ZERO To Dos. See
    /// <c>projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md</c>.
    /// </para>
    /// </summary>
    public bool EnableEventSourcedGeneration { get; set; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal models used only within this service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight representation of a matter record used for budget-alert scanning.
/// </summary>
internal sealed class MatterScanRecord
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal UtilizationPercent { get; init; }
}

/// <summary>
/// Lightweight representation of an invoice record used for pending-invoice scanning.
/// </summary>
internal sealed class InvoiceScanRecord
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Lightweight representation of a task record used for assigned-task scanning.
/// </summary>
internal sealed class TaskScanRecord
{
    public Guid Id { get; init; }
    public string Subject { get; init; } = string.Empty;

    /// <summary>Task 152: the source event's assigned contact — Rule 5's triggering person.</summary>
    public Guid? AssignedToContactId { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scheduled job (<see cref="IScheduledJob"/>, ADR-036) that auto-generates <c>sprk_todo</c>
/// records for actionable conditions.
/// </summary>
/// <remarks>
/// <para>
/// Runs daily at the configured start hour (default 02:00 UTC; <c>TodoGeneration:StartHourUtc</c> /
/// <c>TodoGeneration:IntervalHours</c> compile to the cron schedule). <b>Migrated from a hand-rolled
/// <c>PeriodicTimer</c> <c>BackgroundService</c> by unified-access-control-r2 task 152</b> — ADR-052 §1 requires an
/// existing timer service to migrate when a PR changes its behaviour, and task 152 does (Assigned To). Under
/// <c>ScheduledJobHost</c> the run is dispatched ONCE per schedule across instances (ADR-036 A1 distributed lease):
/// the old timer ran on every BFF instance, so a scaled-out BFF raced itself past the same-name dedupe.
/// </para>
/// <para>
/// <b>Who each to-do is FOR</b> (task 152, #1044 split agreed with word-add-in-r1): Created By is the BFF
/// application user for every to-do this job creates, so the job fills <c>sprk_assignedto</c> through
/// <see cref="AssignedToDefaults"/> — the triggering person (Rule 5: the source event's assigned contact), else the
/// regarding parent's responsible internal contact, else blank with <c>todo_unassigned</c>. Never a team, never an
/// email match.
/// </para>
///
/// <para><strong>Rules (5 total)</strong></para>
/// <list type="number">
///   <item>Overdue events → "Overdue: {event name}" (regarding: <c>sprk_event</c>)</item>
///   <item>Budget &gt;85% utilization → "Budget Alert: {matter name}" (regarding: <c>sprk_matter</c>)</item>
///   <item>Deadline within 14 days → "Deadline: {event name} (due {date})" (regarding: <c>sprk_event</c>)</item>
///   <item>Pending invoices → "Invoice Pending: {invoice name}" (regarding: <c>sprk_invoice</c>)</item>
///   <item>Assigned tasks → "Assigned: {task subject}" (standalone, no regarding)</item>
/// </list>
///
/// <para><strong>Output entity</strong>: <c>sprk_todo</c> (first-class custom entity per
/// smart-todo-decoupling-r3 D-1). Previously created <c>sprk_event</c> records with
/// <c>sprk_todoflag=true</c>; that legacy model was removed in r3 Phase 1.
/// All regarding associations go through <see cref="TodoRegardingBuilder"/> which
/// enforces ADR-024 (one specific lookup + 4 resolver fields, set atomically).</para>
///
/// <para><strong>Idempotency</strong>: Before creating a to-do, the service queries
/// <c>sprk_todo</c> for an existing record with the same name and not Dismissed.
/// If a match is found the item is skipped.</para>
///
/// <para><strong>Error handling</strong>: Each candidate is wrapped in its own try/catch.
/// A single failure never blocks the remaining items.</para>
///
/// <para>Placement (ADR-052): stays in the BFF — B2 (uses BFF domain code: TodoRegardingBuilder, CoreAncestorResolver),
/// B3 (low volume, once a day, same identity and release cadence). Registered with <c>AddScheduledJob</c> in
/// <see cref="Infrastructure.DI.WorkspaceModule"/>.</para>
/// <para>Per ADR-024: All regarding fields applied via <see cref="TodoRegardingBuilder"/>.</para>
/// </remarks>
public sealed class TodoGenerationService : IScheduledJob
{
    /// <summary>The scheduled job id (ADR-036).</summary>
    public const string JobIdConstant = "todo-generation";

    // ──────────────────────────────────────────────────────────────────────────
    // Dataverse field / value constants for sprk_todo
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Logical name of the to-do entity.</summary>
    internal const string EntityTodo = "sprk_todo";

    /// <summary>Primary name field on sprk_todo.</summary>
    private const string FieldTodoName = "sprk_name";

    /// <summary>Rich-notes field on sprk_todo (replaces legacy sprk_eventtodo.sprk_todonotes).</summary>
    private const string FieldTodoNotes = "sprk_notes";

    /// <summary>Due-date field on sprk_todo.</summary>
    private const string FieldTodoDueDate = "sprk_duedate";

    /// <summary>Priority score (0-100) on sprk_todo.</summary>
    private const string FieldTodoPriorityScore = "sprk_priorityscore";

    /// <summary>Effort score (0-100) on sprk_todo.</summary>
    private const string FieldTodoEffortScore = "sprk_effortscore";

    /// <summary>Owner attribute (User/Team) on sprk_todo.</summary>
    private const string FieldOwnerId = "ownerid";

    /// <summary>Status reason values for sprk_todo (see entity-schema.md).</summary>
    private const int StatusCodeOpen = 1;        // Active
    private const int StatusCodeCompleted = 2;   // Inactive
    private const int StatusCodeDismissed = 3;   // Inactive

    /// <summary>statecode values for sprk_todo.</summary>
    private const int StateCodeActive = 0;
    private const int StateCodeInactive = 1;

    // ──────────────────────────────────────────────────────────────────────────
    // Fields
    // ──────────────────────────────────────────────────────────────────────────

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TodoGenerationService> _logger;
    private readonly TodoGenerationOptions _options;

    // Lazily resolved to avoid forcing Dataverse connection at host startup.
    // DataverseServiceClientImpl connects eagerly in its constructor — if that
    // connection fails (transient auth, Key Vault cold start, etc.) it throws,
    // which crashes the host with HTTP 500.30 because BackgroundService
    // resolution happens during IHost.StartAsync().
    // INTENTIONAL: Keeps IDataverseService — QueryExpression reads + CreateAsync across multiple domain groups.
    private IDataverseService? _dataverse;

    // Lazily resolved alongside _dataverse so the regarding builder
    // can use the same lifetime semantics (no eager Dataverse touch at host startup).
    private TodoRegardingBuilder? _regardingBuilder;

    // Lazily resolved alongside _dataverse. Rules 1 & 3 (event-sourced) MUST query
    // events through IEventDataverseService — the REAL sprk_event query lives on
    // DataverseWebApiService, reachable only via this interface. The composite
    // IDataverseService (DataverseServiceClientImpl) has only a silent-empty
    // QueryEventsAsync stub, so routing event queries through _dataverse produced
    // ZERO To Dos (smart-todo-r5 INBOUND fix, 2026-08-17).
    private IEventDataverseService? _events;

    // Lazily resolved alongside _dataverse (unified-access-control-r2 task 146): every generated to-do is owned by
    // the team of the record it regards (or, for a standalone to-do, of the record its content is about) — the named
    // Secure team when that record is secure. Never app-owned: with no record at all, the to-do is not created.
    private Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver? _ownership;

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    public TodoGenerationService(
        IServiceProvider serviceProvider,
        ILogger<TodoGenerationService> logger,
        Microsoft.Extensions.Options.IOptions<TodoGenerationOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IScheduledJob (ADR-036) — migrated from the PeriodicTimer loop by task 152 (ADR-052 §1)
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string JobId => JobIdConstant;

    /// <inheritdoc/>
    public string DisplayName => "To Do Generation";

    /// <inheritdoc/>
    public string Description =>
        "Daily scan that creates sprk_todo records for overdue events, budget alerts, deadlines, pending invoices and "
        + "assigned tasks, each assigned to the person it is for (unified-access-control-r2 task 152).";

    /// <summary>
    /// The cron schedule <see cref="TodoGenerationOptions"/> compiles to: daily at <c>StartHourUtc</c> when the interval
    /// is 24 hours or more, otherwise every <c>IntervalHours</c> hours anchored on <c>StartHourUtc</c>. Out-of-range
    /// values are clamped rather than failing startup. Cron cannot express a period longer than a day or one that does
    /// not divide 24: an interval above 24 runs daily (the old timer ran every N hours), and a non-divisor leaves a
    /// shorter gap across midnight (e.g. 5 h from 02:00 fires 02, 07, 12, 17, 22).
    /// </summary>
    public static string BuildCronSchedule(TodoGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var hour = Math.Clamp(options.StartHourUtc, 0, 23);
        var interval = options.IntervalHours;
        return interval is >= 24 or <= 0
            ? $"0 {hour} * * *"
            : $"0 {hour % interval}/{interval} * * *";
    }

    /// <inheritdoc/>
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = DateTimeOffset.UtcNow;

        // Lazily resolve the Dataverse dependencies on the first run rather than at host startup (avoids a 500.30 when
        // the Dataverse connection is unavailable during a cold start — the reason the old timer service did the same).
        if (!TryEnsureDependencies())
        {
            return new JobRunResult(
                Success: false,
                ErrorMessage: "TodoGenerationService could not resolve its Dataverse dependencies.",
                ProcessedItems: 0,
                Duration: DateTimeOffset.UtcNow - started);
        }

        var (created, skipped, failed) = await RunGenerationPassAsync(cancellationToken).ConfigureAwait(false);

        return new JobRunResult(
            Success: true,
            ErrorMessage: null,
            ProcessedItems: created,
            Duration: DateTimeOffset.UtcNow - started,
            ResultJson: $"{{\"created\":{created},\"skipped\":{skipped},\"failed\":{failed}}}");
    }

    private bool TryEnsureDependencies()
    {
        if (_dataverse is not null && _events is not null && _regardingBuilder is not null && _ownership is not null)
        {
            return true;
        }

        try
        {
            _dataverse ??= _serviceProvider.GetRequiredService<IDataverseService>();
            _events ??= _serviceProvider.GetRequiredService<IEventDataverseService>();
            if (_regardingBuilder is null)
            {
                var commService = _serviceProvider.GetRequiredService<ICommunicationDataverseService>();
                var builderLogger = _serviceProvider.GetRequiredService<ILogger<TodoRegardingBuilder>>();
                // FR-26 (task 052): the builder stamps the regarding target's core-record ancestor onto every
                // to-do it writes, so generated to-dos inherit access the same way PCF-authored ones do.
                var coreAncestors = _serviceProvider.GetRequiredService<CoreAncestorResolver>();
                _regardingBuilder = new TodoRegardingBuilder(commService, coreAncestors, builderLogger);
            }

            // Task 146: every generated to-do's owner comes from the one resolver (CreateTodoAsync).
            _ownership ??= _serviceProvider
                .GetRequiredService<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TodoGenerationService failed to resolve Dataverse dependencies; this run is skipped.");
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Generation pass
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single generation pass: scans all 5 rules and creates missing to-dos.
    /// </summary>
    internal async Task<(int Created, int Skipped, int Failed)> RunGenerationPassAsync(CancellationToken ct)
    {
        if (_dataverse is null)
        {
            _logger.LogWarning("TodoGenerationService: IDataverseService not available, skipping pass");
            return (0, 0, 0);
        }

        _logger.LogInformation("TodoGenerationService: starting generation pass");

        var today = DateTime.UtcNow.Date;
        var totalCreated = 0;
        var totalSkipped = 0;
        var totalFailed = 0;

        // ── Rule 1: Overdue events ────────────────────────────────────────────
        var (created1, skipped1, failed1) =
            await ProcessOverdueEventsAsync(today, ct);
        totalCreated += created1;
        totalSkipped += skipped1;
        totalFailed += failed1;

        // ── Rule 2: Budget > threshold ────────────────────────────────────────
        var (created2, skipped2, failed2) =
            await ProcessBudgetAlertsAsync(ct);
        totalCreated += created2;
        totalSkipped += skipped2;
        totalFailed += failed2;

        // ── Rule 3: Deadline within window ───────────────────────────────────
        var (created3, skipped3, failed3) =
            await ProcessDeadlineProximityAsync(today, ct);
        totalCreated += created3;
        totalSkipped += skipped3;
        totalFailed += failed3;

        // ── Rule 4: Pending invoices ──────────────────────────────────────────
        var (created4, skipped4, failed4) =
            await ProcessPendingInvoicesAsync(ct);
        totalCreated += created4;
        totalSkipped += skipped4;
        totalFailed += failed4;

        // ── Rule 5: Assigned tasks ────────────────────────────────────────────
        var (created5, skipped5, failed5) =
            await ProcessAssignedTasksAsync(ct);
        totalCreated += created5;
        totalSkipped += skipped5;
        totalFailed += failed5;

        _logger.LogInformation(
            "TodoGeneration completed: {Created} created, {Skipped} skipped, {Failed} failed",
            totalCreated,
            totalSkipped,
            totalFailed);

        return (totalCreated, totalSkipped, totalFailed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 1: Overdue events
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for overdue <c>sprk_event</c> records (duedate &lt; today) and creates
    /// "Overdue: {event name}" <c>sprk_todo</c> records regarding each event.
    /// </summary>
    private async Task<(int Created, int Skipped, int Failed)> ProcessOverdueEventsAsync(
        DateTime today, CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var failed = 0;

        var wouldCreate = 0;

        _logger.LogDebug("TodoGeneration Rule 1: scanning for overdue events");

        IEnumerable<EventEntity> overdueEvents;
        try
        {
            // IEventDataverseService (real sprk_event query), NOT the _dataverse
            // composite whose QueryEventsAsync is a silent-empty stub (INBOUND fix).
            var (items, _) = await _events!.QueryEventsAsync(
                dueDateTo: today.AddDays(-1), // duedate < today
                top: 100,
                ct: ct);

            // Exclude completed/cancelled events — by the LIVE sprk_event statuscodes (task 159: the old 5/6 are not
            // values of this table, so nothing was ever excluded).
            overdueEvents = items.Where(e => e.StatusCode != EventStatusCode.Completed && e.StatusCode != EventStatusCode.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TodoGeneration Rule 1: failed to query overdue events");
            return (0, 0, 1);
        }

        foreach (var evt in overdueEvents)
        {
            var todoTitle = $"Overdue: {evt.Name}";
            try
            {
                if (await TodoExistsAsync(todoTitle, ct))
                {
                    _logger.LogDebug(
                        "TodoGeneration Rule 1: skipping existing to-do '{Title}'", todoTitle);
                    skipped++;
                    continue;
                }

                if (!_options.EnableEventSourcedGeneration)
                {
                    // Dry-run gate (default): the reroute above is live so the rule now
                    // sees real events, but creation stays OFF until an operator validates
                    // volume / dedupe / notification impact and flips the flag.
                    wouldCreate++;
                    _logger.LogInformation(
                        "TodoGeneration Rule 1 [dry-run]: WOULD create to-do '{Title}' for event {EventId} "
                        + "(set TodoGeneration:EnableEventSourcedGeneration=true to enable)",
                        todoTitle, evt.Id);
                    continue;
                }

                await CreateTodoAsync(
                    name: todoTitle,
                    regardingEntityName: "sprk_event",
                    regardingId: evt.Id,
                    regardingDisplayName: evt.Name,
                    ct: ct);

                _logger.LogInformation(
                    "TodoGeneration Rule 1: created to-do '{Title}' for event {EventId}",
                    todoTitle, evt.Id);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TodoGeneration Rule 1: failed creating to-do '{Title}' for event {EventId}",
                    todoTitle, evt.Id);
                failed++;
            }
        }

        if (!_options.EnableEventSourcedGeneration && wouldCreate > 0)
        {
            _logger.LogInformation(
                "TodoGeneration Rule 1 [dry-run]: {WouldCreate} to-do(s) would be created "
                + "once TodoGeneration:EnableEventSourcedGeneration=true", wouldCreate);
        }

        return (created, skipped, failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 2: Budget alert
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for matters with utilizationpercent &gt; threshold and creates
    /// "Budget Alert: {matter name}" <c>sprk_todo</c> records regarding each matter.
    /// </summary>
    private async Task<(int Created, int Skipped, int Failed)> ProcessBudgetAlertsAsync(
        CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var failed = 0;

        _logger.LogDebug(
            "TodoGeneration Rule 2: scanning for matters with budget > {Threshold}%",
            _options.BudgetAlertThresholdPercent);

        IEnumerable<MatterScanRecord> matters;
        try
        {
            matters = await QueryMattersOverBudgetAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TodoGeneration Rule 2: failed to query matters over budget");
            return (0, 0, 1);
        }

        foreach (var matter in matters)
        {
            var todoTitle = $"Budget Alert: {matter.Name}";
            try
            {
                if (await TodoExistsAsync(todoTitle, ct))
                {
                    _logger.LogDebug(
                        "TodoGeneration Rule 2: skipping existing to-do '{Title}'", todoTitle);
                    skipped++;
                    continue;
                }

                await CreateTodoAsync(
                    name: todoTitle,
                    regardingEntityName: "sprk_matter",
                    regardingId: matter.Id,
                    regardingDisplayName: matter.Name,
                    ct: ct);

                _logger.LogInformation(
                    "TodoGeneration Rule 2: created to-do '{Title}' for matter {MatterId} ({Utilization:0.#}%)",
                    todoTitle, matter.Id, matter.UtilizationPercent);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TodoGeneration Rule 2: failed creating to-do '{Title}' for matter {MatterId}",
                    todoTitle, matter.Id);
                failed++;
            }
        }

        return (created, skipped, failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 3: Deadline proximity
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for <c>sprk_event</c> records with duedate within the deadline window and creates
    /// "Deadline: {event name} (due {date})" <c>sprk_todo</c> records regarding each event.
    /// </summary>
    private async Task<(int Created, int Skipped, int Failed)> ProcessDeadlineProximityAsync(
        DateTime today, CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var failed = 0;

        var windowEnd = today.AddDays(_options.DeadlineWindowDays);
        var wouldCreate = 0;

        _logger.LogDebug(
            "TodoGeneration Rule 3: scanning for events due between {From:yyyy-MM-dd} and {To:yyyy-MM-dd}",
            today, windowEnd);

        IEnumerable<EventEntity> upcomingEvents;
        try
        {
            // IEventDataverseService (real sprk_event query), NOT the _dataverse
            // composite whose QueryEventsAsync is a silent-empty stub (INBOUND fix).
            var (items, _) = await _events!.QueryEventsAsync(
                dueDateFrom: today,
                dueDateTo: windowEnd,
                top: 100,
                ct: ct);

            // Exclude completed/cancelled events — by the LIVE sprk_event statuscodes (task 159).
            upcomingEvents = items.Where(e => e.StatusCode != EventStatusCode.Completed && e.StatusCode != EventStatusCode.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TodoGeneration Rule 3: failed to query upcoming events");
            return (0, 0, 1);
        }

        foreach (var evt in upcomingEvents)
        {
            if (!evt.DueDate.HasValue)
                continue;

            var dueDateDisplay = evt.DueDate.Value.ToString("yyyy-MM-dd");
            var todoTitle = $"Deadline: {evt.Name} (due {dueDateDisplay})";

            try
            {
                if (await TodoExistsAsync(todoTitle, ct))
                {
                    _logger.LogDebug(
                        "TodoGeneration Rule 3: skipping existing to-do '{Title}'", todoTitle);
                    skipped++;
                    continue;
                }

                if (!_options.EnableEventSourcedGeneration)
                {
                    // Dry-run gate (default): the reroute above is live so the rule now
                    // sees real events, but creation stays OFF until an operator validates
                    // volume / dedupe / notification impact and flips the flag.
                    wouldCreate++;
                    _logger.LogInformation(
                        "TodoGeneration Rule 3 [dry-run]: WOULD create to-do '{Title}' for event {EventId} "
                        + "(set TodoGeneration:EnableEventSourcedGeneration=true to enable)",
                        todoTitle, evt.Id);
                    continue;
                }

                await CreateTodoAsync(
                    name: todoTitle,
                    regardingEntityName: "sprk_event",
                    regardingId: evt.Id,
                    regardingDisplayName: evt.Name,
                    dueDate: evt.DueDate,
                    ct: ct);

                _logger.LogInformation(
                    "TodoGeneration Rule 3: created to-do '{Title}' for event {EventId}",
                    todoTitle, evt.Id);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TodoGeneration Rule 3: failed creating to-do '{Title}' for event {EventId}",
                    todoTitle, evt.Id);
                failed++;
            }
        }

        if (!_options.EnableEventSourcedGeneration && wouldCreate > 0)
        {
            _logger.LogInformation(
                "TodoGeneration Rule 3 [dry-run]: {WouldCreate} to-do(s) would be created "
                + "once TodoGeneration:EnableEventSourcedGeneration=true", wouldCreate);
        }

        return (created, skipped, failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 4: Pending invoices
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for pending invoice records and creates "Invoice Pending: {invoice name}"
    /// <c>sprk_todo</c> records regarding each invoice.
    /// </summary>
    private async Task<(int Created, int Skipped, int Failed)> ProcessPendingInvoicesAsync(
        CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var failed = 0;

        _logger.LogDebug("TodoGeneration Rule 4: scanning for pending invoices");

        IEnumerable<InvoiceScanRecord> invoices;
        try
        {
            invoices = await QueryPendingInvoicesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TodoGeneration Rule 4: failed to query pending invoices");
            return (0, 0, 1);
        }

        foreach (var invoice in invoices)
        {
            var todoTitle = $"Invoice Pending: {invoice.Name}";
            try
            {
                if (await TodoExistsAsync(todoTitle, ct))
                {
                    _logger.LogDebug(
                        "TodoGeneration Rule 4: skipping existing to-do '{Title}'", todoTitle);
                    skipped++;
                    continue;
                }

                await CreateTodoAsync(
                    name: todoTitle,
                    regardingEntityName: "sprk_invoice",
                    regardingId: invoice.Id,
                    regardingDisplayName: invoice.Name,
                    ct: ct);

                _logger.LogInformation(
                    "TodoGeneration Rule 4: created to-do '{Title}' for invoice {InvoiceId}",
                    todoTitle, invoice.Id);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TodoGeneration Rule 4: failed creating to-do '{Title}' for invoice {InvoiceId}",
                    todoTitle, invoice.Id);
                failed++;
            }
        }

        return (created, skipped, failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Rule 5: Assigned tasks (standalone — no regarding parent)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for active <c>sprk_event</c> records and creates "Assigned: {task subject}"
    /// standalone <c>sprk_todo</c> records (no regarding parent).
    /// </summary>
    private async Task<(int Created, int Skipped, int Failed)> ProcessAssignedTasksAsync(
        CancellationToken ct)
    {
        var created = 0;
        var skipped = 0;
        var failed = 0;

        _logger.LogDebug("TodoGeneration Rule 5: scanning for assigned tasks");

        IEnumerable<TaskScanRecord> tasks;
        try
        {
            tasks = await QueryAssignedTasksAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TodoGeneration Rule 5: failed to query assigned tasks");
            return (0, 0, 1);
        }

        foreach (var task in tasks)
        {
            var todoTitle = $"Assigned: {task.Subject}";
            try
            {
                if (await TodoExistsAsync(todoTitle, ct))
                {
                    _logger.LogDebug(
                        "TodoGeneration Rule 5: skipping existing to-do '{Title}'", todoTitle);
                    skipped++;
                    continue;
                }

                // Standalone — no regarding parent. Its content (the title) IS the source event's, so the event is
                // the OWNERSHIP source (task 146): a to-do naming a secure event is owned by the named Secure team.
                // It is not written as a regarding — the to-do stays standalone, as before. Task 152: the source
                // event also names the person — its assigned contact (triggering person), else its responsible
                // internal contact.
                await CreateTodoAsync(
                    name: todoTitle,
                    ownershipSource: new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent("sprk_event", task.Id),
                    triggeringContactId: task.AssignedToContactId,
                    assigneeParentEntity: "sprk_event",
                    assigneeParentId: task.Id,
                    ct: ct);

                _logger.LogInformation(
                    "TodoGeneration Rule 5: created to-do '{Title}' for task {TaskId}",
                    todoTitle, task.Id);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "TodoGeneration Rule 5: failed creating to-do '{Title}' for task {TaskId}",
                    todoTitle, task.Id);
                failed++;
            }
        }

        return (created, skipped, failed);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idempotency check
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if a <c>sprk_todo</c> with <paramref name="title"/>
    /// already exists and has not been dismissed.
    /// </summary>
    /// <remarks>
    /// A to-do is considered a duplicate when ALL of the following match:
    /// <list type="bullet">
    ///   <item><c>sprk_name</c> = <paramref name="title"/> (exact match)</item>
    ///   <item><c>statuscode</c> != Dismissed (3)</item>
    /// </list>
    /// Dismissed to-dos are intentionally excluded — users who dismissed them
    /// must not see them re-appear on the next run.
    /// </remarks>
    internal async Task<bool> TodoExistsAsync(string title, CancellationToken ct)
    {
        var query = new QueryExpression(EntityTodo)
        {
            ColumnSet = new ColumnSet("sprk_todoid", "sprk_name", "statuscode"),
            TopCount = 1,
            NoLock = true
        };

        query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, title);
        query.Criteria.AddCondition("statuscode", ConditionOperator.NotEqual, StatusCodeDismissed);

        var results = await _dataverse!.RetrieveMultipleAsync(query, ct);
        return results.Entities.Count > 0;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // To-do creation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a single <c>sprk_todo</c> record. When
    /// <paramref name="regardingEntityName"/> is non-null, the matching specific
    /// regarding lookup and all four resolver fields are populated atomically by
    /// <see cref="TodoRegardingBuilder"/> (ADR-024).
    /// </summary>
    /// <param name="name">Card title (<c>sprk_name</c>).</param>
    /// <param name="regardingEntityName">Optional regarding parent entity logical name.</param>
    /// <param name="regardingId">Required when <paramref name="regardingEntityName"/> is non-null.</param>
    /// <param name="regardingDisplayName">Display name of the regarding parent (for the resolver name field).</param>
    /// <param name="notes">Optional rich notes (<c>sprk_notes</c>).</param>
    /// <param name="dueDate">Optional due date (<c>sprk_duedate</c>).</param>
    /// <param name="priorityScore">Optional priority score 0-100 (<c>sprk_priorityscore</c>).</param>
    /// <param name="effortScore">Optional effort score 0-100 (<c>sprk_effortscore</c>).</param>
    /// <param name="ownershipSource">
    /// For a STANDALONE to-do (no regarding), the record its content is about — the ownership parent (task 146). Not
    /// written to the row. Ignored when a regarding is supplied (the regarding is the parent).
    /// </param>
    /// <param name="assignedToContactId">A SUPPLIED assignee (contact). Never overwritten.</param>
    /// <param name="triggeringContactId">Task 152: the triggering person's contact, when the rule has one.</param>
    /// <param name="assigneeParentEntity">
    /// Task 152: the record whose responsible internal contact names the person when there is no triggering person.
    /// Defaults to the regarding parent.
    /// </param>
    /// <param name="assigneeParentId">Id for <paramref name="assigneeParentEntity"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// <para><b>Owner (unified-access-control-r2 task 146).</b> The to-do is owned by the team the ONE resolver names
    /// over every parent on the row — the regarding and its FR-26 core-ancestor stamps — or the
    /// <paramref name="ownershipSource"/>: the named Secure team when any is secure, else the primary parent's
    /// business-unit team. This is a background writer with NO acting user, so a to-do with neither a parent nor a
    /// source is NOT created: <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException"/> is thrown, and
    /// each rule's own catch logs it with the reason and counts it, as for any failed row — the pass continues.</para>
    /// <para>The former <c>ownerId</c>/<c>ownerEntityName</c> parameters were removed: no caller passed them
    /// (word-add-in-r1 note 080 §6.5 item 3), and a second way to set the owner is a way around the resolver.</para>
    /// </remarks>
    internal async Task<Guid> CreateTodoAsync(
        string name,
        string? regardingEntityName = null,
        Guid? regardingId = null,
        string? regardingDisplayName = null,
        string? notes = null,
        DateTime? dueDate = null,
        int? priorityScore = null,
        int? effortScore = null,
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent? ownershipSource = null,
        Guid? assignedToContactId = null,
        Guid? triggeringContactId = null,
        string? assigneeParentEntity = null,
        Guid? assigneeParentId = null,
        CancellationToken ct = default)
    {
        var entity = new Entity(EntityTodo)
        {
            [FieldTodoName] = name,
            ["statuscode"] = new OptionSetValue(StatusCodeOpen),
            ["statecode"] = new OptionSetValue(StateCodeActive)
        };

        if (!string.IsNullOrEmpty(notes))
            entity[FieldTodoNotes] = notes;

        if (dueDate.HasValue)
            entity[FieldTodoDueDate] = dueDate.Value;

        if (priorityScore.HasValue)
            entity[FieldTodoPriorityScore] = priorityScore.Value;

        if (effortScore.HasValue)
            entity[FieldTodoEffortScore] = effortScore.Value;

        if (assignedToContactId is { } suppliedAssignee && suppliedAssignee != Guid.Empty)
            entity[AssignedToDefaults.AssignedToAttribute] = new EntityReference("contact", suppliedAssignee);

        // ADR-024: regarding fields applied atomically by the builder when present.
        var hasRegarding = !string.IsNullOrEmpty(regardingEntityName) && regardingId.HasValue && regardingId.Value != Guid.Empty;
        if (hasRegarding)
        {
            if (_regardingBuilder is null)
            {
                throw new InvalidOperationException(
                    "TodoRegardingBuilder not initialized. Service was called before ExecuteAsync " +
                    "completed lazy resolution of Dataverse dependencies.");
            }

            await _regardingBuilder.ApplyResolverFieldsAsync(
                entity,
                regardingEntityName!,
                regardingId!.Value,
                regardingDisplayName ?? string.Empty,
                ct);
        }

        // Task 146: owner from every parent now on the row (regarding + FR-26 stamps), or the standalone to-do's
        // content source. Resolved AFTER the regarding fields so the stamps count. No acting user on this path.
        if (_ownership is null)
        {
            throw new InvalidOperationException(
                "IRecordOwnershipResolver not initialized. Service was called before ExecuteAsync completed lazy "
                + "resolution of Dataverse dependencies; refusing to create an app-owned to-do (task 146).");
        }

        var primary = hasRegarding
            ? new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(regardingEntityName!, regardingId!.Value)
            : ownershipSource;
        var ownerContext = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForChild(entity, primary);
        if (!hasRegarding && ownershipSource is { IsSpecified: true } source)
        {
            ownerContext = ownerContext with { Parents = ownerContext.Parents.Append(source).ToArray() };
        }

        var owner = await _ownership.ResolveOwnerAsync(ownerContext, ct);
        if (!owner.IsOwned)
        {
            throw new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException(EntityTodo, owner);
        }

        entity[FieldOwnerId] = new EntityReference("team", owner.OwningTeamId!.Value);

        // Task 152 (#1044): name the person this to-do is FOR — after the regarding + core-ancestor stamp, so a parent
        // without responsible columns (an invoice) defers to its stamped core record.
        await AssignedToDefaults.ApplyAsync(
            _dataverse!,
            entity,
            triggeringContactId,
            assigneeParentEntity ?? regardingEntityName,
            assigneeParentId ?? regardingId,
            _logger,
            ct).ConfigureAwait(false);

        return await _dataverse!.CreateAsync(entity, ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dataverse queries for non-event entities
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries active matters where utilizationpercent exceeds the configured threshold.
    /// </summary>
    /// <remarks>
    /// Uses QueryExpression via ServiceClient to query:
    ///   sprk_matter where statecode eq 0 and sprk_utilizationpercent gt {threshold}
    /// Explicit column selection per ADR-002.
    /// </remarks>
    private async Task<IEnumerable<MatterScanRecord>> QueryMattersOverBudgetAsync(CancellationToken ct)
    {
        // _dataverse is lazily resolved (nullable field) but these private query helpers only run after
        // the pass-level `if (_dataverse is null) return;` guard — non-null here. Task 152: queried through the
        // IGenericEntityService surface rather than an unwrapped ServiceClient — same query, and Rules 2/4/5 become
        // testable at the module boundary (their Assigned-To precedence is pinned by TodoGenerationServiceTests).

        var query = new QueryExpression("sprk_matter")
        {
            ColumnSet = new ColumnSet("sprk_matterid", "sprk_name", "sprk_utilizationpercent"),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(
            "sprk_utilizationpercent", ConditionOperator.GreaterThan, _options.BudgetAlertThresholdPercent);

        var results = await _dataverse!.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new MatterScanRecord
        {
            Id = e.Id,
            Name = e.GetAttributeValue<string>("sprk_name") ?? string.Empty,
            UtilizationPercent = e.GetAttributeValue<decimal?>("sprk_utilizationpercent") ?? 0m
        });
    }

    /// <summary>
    /// Queries pending invoice records (sprk_invoice where status is Active and statuscode eq 1 / Pending).
    /// </summary>
    /// <remarks>
    /// Uses QueryExpression via ServiceClient to query:
    ///   sprk_invoice where statecode eq 0 and statuscode eq 1
    /// Explicit column selection per ADR-002.
    /// </remarks>
    private async Task<IEnumerable<InvoiceScanRecord>> QueryPendingInvoicesAsync(CancellationToken ct)
    {
        // _dataverse is lazily resolved (nullable field) but these private query helpers only run after
        // the pass-level `if (_dataverse is null) return;` guard — non-null here. Task 152: queried through the
        // IGenericEntityService surface rather than an unwrapped ServiceClient — same query, and Rules 2/4/5 become
        // testable at the module boundary (their Assigned-To precedence is pinned by TodoGenerationServiceTests).

        var query = new QueryExpression("sprk_invoice")
        {
            ColumnSet = new ColumnSet("sprk_invoiceid", "sprk_name"),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);  // Active
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, 1); // Pending

        var results = await _dataverse!.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new InvoiceScanRecord
        {
            Id = e.Id,
            Name = e.GetAttributeValue<string>("sprk_name") ?? string.Empty
        });
    }

    /// <summary>
    /// Queries task-type events (active + open sprk_event records).
    /// </summary>
    /// <remarks>
    /// Uses QueryExpression via ServiceClient.
    /// Explicit column selection per ADR-002.
    /// Note: r3 Phase 1 removed the legacy <c>sprk_todoflag</c> field from <c>sprk_event</c>,
    /// so we no longer filter on it. All active+open events are candidates.
    /// </remarks>
    private async Task<IEnumerable<TaskScanRecord>> QueryAssignedTasksAsync(CancellationToken ct)
    {
        // _dataverse is lazily resolved (nullable field) but these private query helpers only run after
        // the pass-level `if (_dataverse is null) return;` guard — non-null here. Task 152: queried through the
        // IGenericEntityService surface rather than an unwrapped ServiceClient — same query, and Rules 2/4/5 become
        // testable at the module boundary (their Assigned-To precedence is pinned by TodoGenerationServiceTests).

        var query = new QueryExpression("sprk_event")
        {
            ColumnSet = new ColumnSet("sprk_eventid", "sprk_eventname", AssignedToDefaults.AssignedToAttribute),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);  // Active
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, 3); // Open

        var results = await _dataverse!.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new TaskScanRecord
        {
            Id = e.Id,
            Subject = e.GetAttributeValue<string>("sprk_eventname") ?? string.Empty,
            AssignedToContactId = e.GetAttributeValue<EntityReference>(AssignedToDefaults.AssignedToAttribute) is { } assignee
                && assignee.Id != Guid.Empty
                ? assignee.Id
                : null,
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test seam
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Internal test seam (task 146): injects the ownership resolver that <c>ExecuteAsync</c> otherwise resolves
    /// lazily, so creation paths can run without the BackgroundService loop — the same shape as
    /// <see cref="SetRegardingBuilderForTest"/>.
    /// </summary>
    internal void SetOwnershipResolverForTest(Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
    }

    /// <summary>
    /// Internal test seam: allows unit tests to inject a pre-built
    /// <see cref="TodoRegardingBuilder"/> so creation paths can exercise resolver
    /// field population without running the full BackgroundService loop.
    /// </summary>
    internal void SetRegardingBuilderForTest(TodoRegardingBuilder builder)
    {
        _regardingBuilder = builder ?? throw new ArgumentNullException(nameof(builder));
    }
}
