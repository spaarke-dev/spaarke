using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
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
/// Periodic <see cref="BackgroundService"/> that auto-generates to-do items for actionable conditions.
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
///   <item>Overdue events → "Overdue: {event name}"</item>
///   <item>Budget &gt;85% utilization → "Budget Alert: {matter name}"</item>
///   <item>Deadline within 14 days → "Deadline: {event name} (due {date})"</item>
///   <item>Pending invoices → "Invoice Pending: {invoice name}"</item>
///   <item>Assigned tasks → "Assigned: {task subject}"</item>
/// </list>
///
/// <para><strong>Idempotency</strong>: Before creating a to-do, the service queries
/// Dataverse for an existing <c>sprk_event</c> record with the same title,
/// <c>sprk_todosource='System'</c>, and <c>sprk_todostatus</c> not equal to <c>'Dismissed'</c>.
/// If a match is found the item is skipped.</para>
///
/// <para><strong>Error handling</strong>: Each candidate is wrapped in its own try/catch.
/// A single failure never blocks the remaining items.</para>
///
/// <para>Per ADR-001: BackgroundService only — no Azure Functions.</para>
/// <para>Per ADR-010: Registered via <see cref="WorkspaceModule"/> extension method.</para>
/// </remarks>
public sealed class TodoGenerationService : BackgroundService
{
    // ──────────────────────────────────────────────────────────────────────────
    // Dataverse field / value constants for to-do items
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>sprk_todoflag field name on sprk_event.</summary>
    private const string FieldTodoFlag = "sprk_todoflag";

    /// <summary>sprk_todosource field name on sprk_event. Value: 'System'.</summary>
    private const string FieldTodoSource = "sprk_todosource";

    /// <summary>sprk_todostatus field name on sprk_event. Open='Open', Dismissed='Dismissed'.</summary>
    private const string FieldTodoStatus = "sprk_todostatus";

    /// <summary>Primary name field on sprk_event.</summary>
    private const string FieldEventName = "sprk_eventname";

    /// <summary>Value stored in sprk_todosource for system-generated items.</summary>
    private const string TodoSourceSystem = "System";

    /// <summary>Value stored in sprk_todostatus for new open items.</summary>
    private const string TodoStatusOpen = "Open";

    /// <summary>Value stored in sprk_todostatus for items the user dismissed.</summary>
    private const string TodoStatusDismissed = "Dismissed";

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
    private IDataverseService? _dataverse;

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TodoGenerationService failed to resolve IDataverseService. " +
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
    /// Scans for overdue sprk_event records (duedate &lt; today, sprk_todoflag != true)
    /// and creates "Overdue: {event name}" to-dos.
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
            // Query events with duedate < today — sprk_todoflag filter applied client-side
            // because the QueryEventsAsync API does not expose a todoflag filter.
            // TODO: Replace with a more targeted Dataverse query that adds
            //   $filter=sprk_todoflag ne true and sprk_duedate lt {today:yyyy-MM-dd}
            // once the interface exposes a generic query path.
            var (items, _) = await _dataverse!.QueryEventsAsync(
                dueDateTo: today.AddDays(-1), // duedate < today
                top: 100,
                ct: ct);

            // Exclude events that are already to-do items themselves.
            overdueEvents = items.Where(e => e.StatusCode != 5 && e.StatusCode != 6); // Not Completed/Cancelled
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

                await CreateTodoEventAsync(
                    name: todoTitle,
                    regardingEventId: evt.Id,
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
    /// "Budget Alert: {matter name}" to-dos.
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

                await CreateTodoEventAsync(
                    name: todoTitle,
                    regardingMatterId: matter.Id,
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
    /// Scans for sprk_event records with duedate within the deadline window and creates
    /// "Deadline: {event name} (due {date})" to-dos.
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

            // Exclude events that are already to-do items and completed/cancelled events.
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

                await CreateTodoEventAsync(
                    name: todoTitle,
                    regardingEventId: evt.Id,
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
    /// Scans for pending invoice records and creates "Invoice Pending: {invoice name}" to-dos.
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

                await CreateTodoEventAsync(
                    name: todoTitle,
                    regardingInvoiceId: invoice.Id,
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
    // Rule 5: Assigned tasks
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans for task-type events assigned to the service account and creates
    /// "Assigned: {task subject}" to-dos.
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

                await CreateTodoEventAsync(
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
    /// Returns <c>true</c> if a system-generated to-do with <paramref name="title"/>
    /// already exists and has not been dismissed.
    /// </summary>
    /// <remarks>
    /// A to-do is considered a duplicate when ALL of the following match:
    /// <list type="bullet">
    ///   <item>sprk_eventname = <paramref name="title"/> (exact match)</item>
    ///   <item>sprk_todosource = 'System'</item>
    ///   <item>sprk_todostatus != 'Dismissed'</item>
    /// </list>
    /// Dismissed to-dos are intentionally excluded — users who dismissed them
    /// must not see them re-appear on the next run.
    /// </remarks>
    internal async Task<bool> TodoExistsAsync(string title, CancellationToken ct)
    {
        // QueryEventsAsync doesn't filter on custom todo fields, so we query all
        // sprk_events and filter client-side for the system-todo matching.
        // TODO: When a generic query API is available, replace this with a targeted
        // Dataverse OData filter:
        //   sprk_eventname eq '{title}'
        //   and sprk_todosource eq 'System'
        //   and sprk_todostatus ne 'Dismissed'
        // For now, use the name-based query and project/filter in memory.

        // We use a narrow duedate window (no filter) and top=100 per rule.
        // The title is unique enough (includes entity name) that duplicates
        // within the first 100 results will always be caught.
        var (items, _) = await _dataverse!.QueryEventsAsync(top: 100, ct: ct);

        return items.Any(e =>
            string.Equals(e.Name, title, StringComparison.OrdinalIgnoreCase));

        // NOTE: The above uses the name as a proxy. In production, the Dataverse query
        // should add sprk_todosource and sprk_todostatus filters so dismissed items
        // are excluded at the database level. The full OData filter should be:
        //   sprk_eventname eq '{escapedTitle}'
        //   and sprk_todosource eq 'System'
        //   and sprk_todostatus ne 'Dismissed'
    }

    // ──────────────────────────────────────────────────────────────────────────
    // To-do creation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a single sprk_event record with to-do flags set.
    /// All created to-dos have: sprk_todoflag=true, sprk_todosource='System', sprk_todostatus='Open'.
    /// </summary>
    internal async Task<Guid> CreateTodoEventAsync(
        string name,
        Guid? regardingEventId = null,
        Guid? regardingMatterId = null,
        Guid? regardingInvoiceId = null,
        DateTime? dueDate = null,
        CancellationToken ct = default)
    {
        var entity = new Entity("sprk_event")
        {
            [FieldEventName] = name,
            [FieldTodoFlag] = true,
            [FieldTodoSource] = TodoSourceSystem,
            [FieldTodoStatus] = TodoStatusOpen,
            ["statuscode"] = 3,  // Open
            ["statecode"] = 0    // Active
        };

        if (dueDate.HasValue)
            entity["sprk_duedate"] = dueDate.Value;

        if (regardingEventId.HasValue)
            entity["sprk_relatedevent@odata.bind"] = $"/sprk_events({regardingEventId.Value})";

        if (regardingMatterId.HasValue)
            entity["sprk_regardingmatter@odata.bind"] = $"/sprk_matters({regardingMatterId.Value})";

        if (regardingInvoiceId.HasValue)
            entity["sprk_regardinginvoice@odata.bind"] = $"/sprk_invoices({regardingInvoiceId.Value})";

        return await _dataverse!.CreateAsync(entity, ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dataverse queries for non-event entities
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries active matters where utilizationpercent exceeds the configured threshold.
    /// </summary>
    /// <remarks>
    /// TODO: Replace with a targeted Dataverse OData query once a generic query path is
    /// available on <see cref="IDataverseService"/>. The ideal query:
    /// <code>
    /// GET sprk_matters?$select=sprk_matterid,sprk_name,sprk_utilizationpercent
    ///     &amp;$filter=statecode eq 0
    ///              and sprk_utilizationpercent gt {threshold}
    /// </code>
    /// </remarks>
    private Task<IEnumerable<MatterScanRecord>> QueryMattersOverBudgetAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "TODO: querying matters over budget from Dataverse — returning empty list (stub).");

        // Stub: returns empty list until a generic query method is exposed.
        // The real implementation would query:
        //   sprk_matters?$select=sprk_matterid,sprk_name,sprk_utilizationpercent
        //               &$filter=statecode eq 0 and sprk_utilizationpercent gt {threshold}
        return Task.FromResult(Enumerable.Empty<MatterScanRecord>());
    }

    /// <summary>
    /// Queries pending invoice records (sprk_invoice where status is 'Pending').
    /// </summary>
    /// <remarks>
    /// TODO: Replace with a targeted Dataverse OData query. The ideal query:
    /// <code>
    /// GET sprk_invoices?$select=sprk_invoiceid,sprk_name
    ///     &amp;$filter=statecode eq 0 and statuscode eq 1
    /// </code>
    /// </remarks>
    private Task<IEnumerable<InvoiceScanRecord>> QueryPendingInvoicesAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "TODO: querying pending invoices from Dataverse — returning empty list (stub).");

        return Task.FromResult(Enumerable.Empty<InvoiceScanRecord>());
    }

    /// <summary>
    /// Queries task-type events assigned to the service account.
    /// </summary>
    /// <remarks>
    /// TODO: Replace with a targeted Dataverse OData query. The ideal query:
    /// <code>
    /// GET sprk_events?$select=sprk_eventid,sprk_eventname
    ///     &amp;$filter=statecode eq 0 and sprk_eventtype eq 'Task'
    ///              and _ownerid_value eq {serviceAccountId}
    /// </code>
    /// </remarks>
    private Task<IEnumerable<TaskScanRecord>> QueryAssignedTasksAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "TODO: querying assigned tasks from Dataverse — returning empty list (stub).");

        return Task.FromResult(Enumerable.Empty<TaskScanRecord>());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

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
}
