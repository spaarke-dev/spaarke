using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

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

    /// <summary>Interval between successive runs in hours. Default: 24.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>UTC hour at which the first run fires (0-23). Default: 2 (2 AM UTC).</summary>
    public int StartHourUtc { get; set; } = 2;

    /// <summary>Number of days before a deadline that triggers a to-do. Default: 14.</summary>
    public int DeadlineWindowDays { get; set; } = 14;

    /// <summary>Budget utilization percentage threshold that triggers a to-do. Default: 85.</summary>
    public decimal BudgetAlertThresholdPercent { get; set; } = 85m;
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
}

// ─────────────────────────────────────────────────────────────────────────────
// Service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Periodic <see cref="BackgroundService"/> that auto-generates <c>sprk_todo</c>
/// records for actionable conditions.
/// </summary>
/// <remarks>
/// <para>
/// Runs on a 24-hour interval (configurable via <c>TodoGeneration:IntervalHours</c>).
/// The first tick is delayed until the configured start hour (default 2 AM UTC) to avoid
/// hammering Dataverse at application startup.
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
/// <para>Per ADR-001: BackgroundService only — no Azure Functions.</para>
/// <para>Per ADR-010: Registered via <see cref="Infrastructure.DI.WorkspaceModule"/> extension method.</para>
/// <para>Per ADR-024: All regarding fields applied via <see cref="TodoRegardingBuilder"/>.</para>
/// </remarks>
public sealed class TodoGenerationService : BackgroundService
{
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
    // INTENTIONAL: Keeps IDataverseService — casts to DataverseServiceClientImpl for FetchXML queries
    // and uses CreateAsync across multiple domain groups.
    private IDataverseService? _dataverse;

    // Lazily resolved alongside _dataverse so the regarding builder
    // can use the same lifetime semantics (no eager Dataverse touch at host startup).
    private TodoRegardingBuilder? _regardingBuilder;

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
    // BackgroundService loop
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "TodoGenerationService starting. Interval={IntervalHours}h, StartHour={StartHour}h UTC",
            _options.IntervalHours,
            _options.StartHourUtc);

        // Delay the first tick until the next configured start-hour window so we
        // don't blast Dataverse at application startup.
        var initialDelay = CalculateInitialDelay();
        if (initialDelay > TimeSpan.Zero)
        {
            _logger.LogInformation(
                "TodoGenerationService waiting {DelayMinutes} minutes before first run",
                (int)initialDelay.TotalMinutes);

            try
            {
                await Task.Delay(initialDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        // Lazily resolve IDataverseService here (after the initial delay) rather than
        // in the constructor to avoid forcing a Dataverse connection during host startup.
        try
        {
            _dataverse = _serviceProvider.GetRequiredService<IDataverseService>();
            var commService = _serviceProvider.GetRequiredService<ICommunicationDataverseService>();
            var builderLogger = _serviceProvider.GetRequiredService<ILogger<TodoRegardingBuilder>>();
            _regardingBuilder = new TodoRegardingBuilder(commService, builderLogger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TodoGenerationService failed to resolve Dataverse dependencies. " +
                "Service will not run. This does not affect app startup.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));

        // Run immediately for the first tick, then on the periodic interval.
        do
        {
            try
            {
                await RunGenerationPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log but do not crash the service; the timer will fire again next interval.
                _logger.LogError(ex, "Unhandled exception in TodoGenerationService run pass");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("TodoGenerationService stopped");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Generation pass
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single generation pass: scans all 5 rules and creates missing to-dos.
    /// </summary>
    internal async Task RunGenerationPassAsync(CancellationToken ct)
    {
        if (_dataverse is null)
        {
            _logger.LogWarning("TodoGenerationService: IDataverseService not available, skipping pass");
            return;
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

        _logger.LogDebug("TodoGeneration Rule 1: scanning for overdue events");

        IEnumerable<EventEntity> overdueEvents;
        try
        {
            var (items, _) = await _dataverse!.QueryEventsAsync(
                dueDateTo: today.AddDays(-1), // duedate < today
                top: 100,
                ct: ct);

            // Exclude completed/cancelled events.
            overdueEvents = items.Where(e => e.StatusCode != 5 && e.StatusCode != 6);
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

        _logger.LogDebug(
            "TodoGeneration Rule 3: scanning for events due between {From:yyyy-MM-dd} and {To:yyyy-MM-dd}",
            today, windowEnd);

        IEnumerable<EventEntity> upcomingEvents;
        try
        {
            var (items, _) = await _dataverse!.QueryEventsAsync(
                dueDateFrom: today,
                dueDateTo: windowEnd,
                top: 100,
                ct: ct);

            // Exclude completed/cancelled events.
            upcomingEvents = items.Where(e => e.StatusCode != 5 && e.StatusCode != 6);
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

                // Standalone — no regarding parent
                await CreateTodoAsync(
                    name: todoTitle,
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
    /// <param name="ownerId">Optional owner (user or team) — written to <c>ownerid</c>.</param>
    /// <param name="ownerEntityName">Owner entity logical name (e.g. <c>systemuser</c> or <c>team</c>). Required when <paramref name="ownerId"/> is set.</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task<Guid> CreateTodoAsync(
        string name,
        string? regardingEntityName = null,
        Guid? regardingId = null,
        string? regardingDisplayName = null,
        string? notes = null,
        DateTime? dueDate = null,
        int? priorityScore = null,
        int? effortScore = null,
        Guid? ownerId = null,
        string? ownerEntityName = null,
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

        if (ownerId.HasValue && !string.IsNullOrEmpty(ownerEntityName))
            entity[FieldOwnerId] = new EntityReference(ownerEntityName, ownerId.Value);

        // ADR-024: regarding fields applied atomically by the builder when present.
        if (!string.IsNullOrEmpty(regardingEntityName) && regardingId.HasValue && regardingId.Value != Guid.Empty)
        {
            if (_regardingBuilder is null)
            {
                throw new InvalidOperationException(
                    "TodoRegardingBuilder not initialized. Service was called before ExecuteAsync " +
                    "completed lazy resolution of Dataverse dependencies.");
            }

            await _regardingBuilder.ApplyResolverFieldsAsync(
                entity,
                regardingEntityName,
                regardingId.Value,
                regardingDisplayName ?? string.Empty,
                ct);
        }

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
        var serviceClient = GetServiceClient();

        var query = new QueryExpression("sprk_matter")
        {
            ColumnSet = new ColumnSet("sprk_matterid", "sprk_name", "sprk_utilizationpercent"),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(
            "sprk_utilizationpercent", ConditionOperator.GreaterThan, _options.BudgetAlertThresholdPercent);

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

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
        var serviceClient = GetServiceClient();

        var query = new QueryExpression("sprk_invoice")
        {
            ColumnSet = new ColumnSet("sprk_invoiceid", "sprk_name"),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);  // Active
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, 1); // Pending

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

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
        var serviceClient = GetServiceClient();

        var query = new QueryExpression("sprk_event")
        {
            ColumnSet = new ColumnSet("sprk_eventid", "sprk_eventname"),
            TopCount = 100
        };

        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);  // Active
        query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, 3); // Open

        var results = await serviceClient.RetrieveMultipleAsync(query, ct);

        return results.Entities.Select(e => new TaskScanRecord
        {
            Id = e.Id,
            Subject = e.GetAttributeValue<string>("sprk_eventname") ?? string.Empty
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the underlying ServiceClient from the IDataverseService implementation.
    /// Required for generic SDK operations (QueryExpression) not exposed on the interface.
    /// </summary>
    private ServiceClient GetServiceClient()
    {
        if (_dataverse is DataverseServiceClientImpl impl)
            return impl.OrganizationService;

        throw new InvalidOperationException(
            $"TodoGenerationService requires IDataverseService to be backed by DataverseServiceClientImpl. " +
            $"Actual type: {_dataverse?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Calculates the delay until the next configured start-hour UTC.
    /// Returns <see cref="TimeSpan.Zero"/> if the start hour has not yet passed today.
    /// </summary>
    private TimeSpan CalculateInitialDelay()
    {
        var now = DateTime.UtcNow;
        var nextRun = new DateTime(now.Year, now.Month, now.Day,
            _options.StartHourUtc, 0, 0, DateTimeKind.Utc);

        if (nextRun <= now)
            nextRun = nextRun.AddDays(1); // Already past today's window — wait until tomorrow

        return nextRun - now;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test seam
    // ──────────────────────────────────────────────────────────────────────────

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
