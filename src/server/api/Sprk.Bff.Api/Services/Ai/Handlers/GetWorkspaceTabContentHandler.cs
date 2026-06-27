using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// chat-routing-redesign-r1 task 118b / FR-57 — READ-ONLY chat tool exposing
/// <c>get_workspace_tab_content(tabId, sectionName?)</c> to the LLM. Closes the T2
/// workspace-output → AI-memory round-trip per spec §"Phase 5+7 Revised Scope" point 7 and
/// architecture §6.5 + §11.1: composed widget state (sections + values) for an existing
/// workspace tab becomes AI-readable on subsequent chat turns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this enables (UX)</strong>: when a multi-node Output Composition playbook
/// (e.g., <c>summarize-document-for-workspace@v1</c> per task 118R) writes 4 sections
/// (<c>tldr</c>, <c>summary</c>, <c>keywords</c>, <c>entities</c>) into a workspace tab and
/// the user follows up with "make the summary shorter", the agent invokes this tool to
/// re-read the CURRENT composed widget state — NOT the raw streaming chunks — so subsequent
/// reasoning can target the correct section.
/// </para>
///
/// <para>
/// <strong>Architecture binding</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>§6.5 workspace state read/write</strong> — reuses the existing
/// <see cref="IWorkspaceStateService.GetTabsAsync"/> plumbing (Q4 hybrid Redis hot +
/// Cosmos durable tier). DOES NOT introduce a new state-storage path.</item>
/// <item><strong>§11.1 R6 carry-forward</strong> — DO NOT REBUILD <c>WorkspaceStateService</c>.
/// Per CLAUDE.md §10 (BFF hygiene) + §11 (component justification): this handler is a pure
/// projection over Pillar 6b plumbing.</item>
/// <item><strong>NFR-A1 explicit T2→T3 promotion</strong> — this handler is READ-ONLY.
/// It MUST NOT mutate tab state, MUST NOT bump <c>LastUserEditAt</c>, MUST NOT write to
/// long-term memory, MUST NOT promote to any cross-session tier. Mutation is the exclusive
/// responsibility of <see cref="UpdateWorkspaceTabHandler"/> (Q8 USER WINS).</item>
/// </list>
///
/// <para>
/// <strong>Projection shape (composed, not raw)</strong>:
/// </para>
/// <list type="bullet">
/// <item>For a tab whose <see cref="WorkspaceTab.WidgetData"/> is a closed-union variant
/// (Summary | DocumentViewer | Dashboard | Table), the handler returns the deterministic
/// field set per variant — display metadata + identifier fields only.</item>
/// <item>The <c>sections</c> map is keyed by <em>section name</em> when the tab carries
/// multi-section composite state (FR-54 / FR-55 multi-node Output composition). For a
/// single-section legacy tab the map has one entry whose key is the widget variant name
/// (e.g., <c>"Summary"</c>).</item>
/// <item><c>sectionName</c> is optional. When supplied AND present in the projected map,
/// the response carries only that section. When supplied AND absent, the response carries
/// an empty <c>sections</c> map and a clear <c>summary</c> message — NEVER an error
/// (graceful per architecture §9.2 pattern).</item>
/// </list>
///
/// <para>
/// <strong>Decoupling from <see cref="WorkspaceTab"/> internals</strong>: the projection
/// uses the <see cref="WorkspaceTab.WidgetType"/> discriminator + the typed
/// <see cref="WorkspaceTabWidgetData"/> subclass fields. It does NOT assume the multi-node
/// composite shape (per POML constraint "DO NOT couple this handler to <c>DeliverComposite</c>
/// shape — it should work for any workspace tab content"). The handler degrades gracefully
/// for any variant the closed union supports.
/// </para>
///
/// <para>
/// <strong>Auto-discovery + data-driven registration (R6 Pillar 2)</strong>: registered via
/// the <c>sprk_analysistool</c> seed row
/// (<c>infra/dataverse/sprk_analysistool-get-workspace-tab-content-row.json</c>). Routes to
/// this C# class via <c>sprk_handlerclass = "GetWorkspaceTabContentHandler"</c>. Auto-
/// discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> per ADR-010 —
/// ZERO new manual DI line; ZERO <c>if (flag)</c> block (CLAUDE.md §10 F.1 anti-pattern
/// kept clear).
/// </para>
///
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only.
/// Workspace tab content is a chat-session affordance — playbook nodes write to
/// <c>sprk_analysisoutput</c> + <c>sprk_workingdocument</c>, not the chat-session per-tenant
/// workspace tab list.
/// </para>
///
/// <para>
/// <strong>Capability gate</strong>: <c>sprk_requiredcapability = null</c>. Reading the
/// current workspace tab content is a default user affordance. The handler's own
/// invariants — tab MUST exist + MUST belong to the chat session's tenant — are the
/// authorization surface.
/// </para>
///
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IWorkspaceStateService"/> directly (BFF-internal workspace plumbing — NOT
/// through a PublicContracts facade). Mirrors task 055
/// (<see cref="UpdateWorkspaceTabHandler"/>) placement rationale.</item>
/// <item><strong>ADR-014</strong>: <see cref="ChatInvocationContext.TenantId"/> is required
/// and forwarded into <see cref="IWorkspaceStateService.GetTabsAsync"/>; cross-tenant reads
/// are structurally impossible (the service-layer Redis key + Cosmos partition key already
/// scope by tenantId).</item>
/// <item><strong>ADR-015 BINDING</strong>: telemetry emits handler name + decision
/// (<c>ok</c> | <c>not_found</c> | <c>section_missing</c> | <c>validation_failed</c> |
/// <c>cancelled</c> | <c>exception</c>) + tabId + sectionName presence flag + sectionCount
/// + tenantId + durationMs ONLY. NEVER tab content text, section body, widget data values,
/// or user message text. The projected <c>Data</c> payload IS the response body and is
/// returned to the LLM (which is its purpose — composed widget state is the read), but it
/// MUST NOT appear in app-logs.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// target ≤+0.1 MB.</item>
/// <item><strong>ADR-033</strong>: SINGLE <see cref="ToolResult"/> return; no streaming
/// side-channel. Memory tools are non-streaming.</item>
/// </list>
/// </remarks>
public sealed class GetWorkspaceTabContentHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(GetWorkspaceTabContentHandler);

    // ────────────────────────────────────────────────────────────────────────
    // Outcome discriminators (ADR-015 tier-1 safe telemetry vocabulary)
    // ────────────────────────────────────────────────────────────────────────

    internal const string OutcomeOk = "ok";
    internal const string OutcomeNotFound = "not_found";
    internal const string OutcomeSectionMissing = "section_missing";
    internal const string OutcomeValidationFailed = "validation_failed";
    internal const string OutcomeCancelled = "cancelled";
    internal const string OutcomeException = "exception";

    // ────────────────────────────────────────────────────────────────────────
    // Payload status discriminators (LLM pattern-matches on these)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Status: tab was found and content (possibly section-scoped) returned.</summary>
    internal const string StatusOk = "ok";

    /// <summary>Status: tab not found in current workspace state — re-readable response.</summary>
    internal const string StatusNotFound = "not_found";

    /// <summary>Status: tab found but the requested sectionName is not present — re-readable response.</summary>
    internal const string StatusSectionMissing = "section_missing";

    // ────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ────────────────────────────────────────────────────────────────────────

    private readonly IWorkspaceStateService _workspaceStateService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetWorkspaceTabContentHandler> _logger;

    public GetWorkspaceTabContentHandler(
        IWorkspaceStateService workspaceStateService,
        TimeProvider timeProvider,
        ILogger<GetWorkspaceTabContentHandler> logger)
    {
        _workspaceStateService = workspaceStateService
            ?? throw new ArgumentNullException(nameof(workspaceStateService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ────────────────────────────────────────────────────────────────────────
    // IToolHandler surface
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Get Workspace Tab Content",
        Description: "Read the composed widget state of an existing workspace tab. Use this " +
                     "when the user refers to content they can see in a workspace tab (e.g., " +
                     "'make the summary shorter', 'change the tldr') and you need the CURRENT " +
                     "state to reason about what to modify. Returns the composed sections + " +
                     "values from the tab's widget data — NOT raw streaming chunks. Read-only — " +
                     "this tool NEVER mutates tab state; for edits use update_workspace_tab. " +
                     "Architecture §6.5 / §11.1.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "tabId",
                "Stable workspace tab identifier (matches WorkspaceTab.id from the system-prompt snapshot).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "sectionName",
                "Optional section name to scope the read to a single composed section (e.g., 'summary'). " +
                "Omit to return all sections. When supplied but not present in the tab, returns a " +
                "'section_missing' status — never an error.",
                ToolParameterType.String,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context invocation rejected — workspace tab content is a chat-session
    /// affordance (playbook nodes write to sprk_analysisoutput, not the chat-session
    /// workspace tab list).
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "GetWorkspaceTabContentHandler is chat-context-only. Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            // tabId — required, non-empty string.
            if (!doc.RootElement.TryGetProperty("tabId", out var tabIdProp) ||
                tabIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tabIdProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'tabId' string field.");
            }

            // sectionName — optional; when supplied MUST be a non-empty string.
            if (doc.RootElement.TryGetProperty("sectionName", out var sectionProp) &&
                sectionProp.ValueKind != JsonValueKind.Null)
            {
                if (sectionProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(sectionProp.GetString()))
                {
                    return ToolValidationResult.Failure(
                        "'sectionName' must be a non-empty string when provided.");
                }
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "GetWorkspaceTabContentHandler is chat-context-only. Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = _timeProvider.GetUtcNow();
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "TenantId is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            stopwatch.Stop();
            // ADR-015: log structural error only — NEVER raw arguments JSON body.
            _logger.LogWarning(
                "GetWorkspaceTabContentHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // ADR-015: log handler + IDs + tabId + sectionName presence (not value) +
        // tenantId ONLY. NEVER the projected widget body that comes back.
        _logger.LogInformation(
            "GetWorkspaceTabContentHandler ({Correlation}) read start tabId={TabId} tenantId={TenantId} sectionScoped={SectionScoped}",
            correlationLogId, args.TabId, context.TenantId, args.SectionName is not null);

        var sessionId = context.ChatSessionId.ToString("N");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read merged hot+durable tabs from the existing Pillar 6b plumbing. The service
            // layer's key already scopes by tenantId so cross-tenant reads are impossible.
            // We still assert TenantId match below as defense in depth.
            var tabs = await _workspaceStateService
                .GetTabsAsync(context.TenantId, sessionId, cancellationToken)
                .ConfigureAwait(false);

            var current = tabs.FirstOrDefault(t => string.Equals(t.Id, args.TabId, StringComparison.Ordinal));

            if (current is null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "GetWorkspaceTabContentHandler ({Correlation}) not_found tabId={TabId} in {Duration}ms",
                    correlationLogId, args.TabId, stopwatch.ElapsedMilliseconds);

                // Tab absent — re-actable response (the user may have closed the tab, or the
                // agent may be referencing a stale id from an earlier turn). Mirrors the
                // refused_not_found pattern in UpdateWorkspaceTabHandler.
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new GetWorkspaceTabContentPayload
                    {
                        Status = StatusNotFound,
                        TabId = args.TabId,
                        WidgetType = null,
                        Sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                        SectionCount = 0,
                        Message = $"Tab '{args.TabId}' was not found in the current workspace state. " +
                                  "It may have been closed by the user, or the id may be stale. Re-read the " +
                                  "workspace state before re-attempting."
                    },
                    summary: $"get_workspace_tab_content: tab '{args.TabId}' not found.",
                    confidence: 0.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Defense-in-depth tenant check. The Redis key + Cosmos partition key already scope
            // by tenantId, so this branch is unreachable under normal operation — but a
            // misconfigured cache layer must not surface a foreign-tenant tab to the agent.
            if (!string.Equals(current.TenantId, context.TenantId, StringComparison.Ordinal))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "GetWorkspaceTabContentHandler ({Correlation}) tenant_mismatch tabId={TabId} ctxTenant={CtxTenant} tabTenant={TabTenant}",
                    correlationLogId, args.TabId, context.TenantId, current.TenantId);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "Tab tenant mismatch — refusing read.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Project the composed widget state to a section-name-keyed map. The shape is
            // closed-union safe — variant-aware projection per WidgetType.
            var sections = ProjectComposedSections(current);

            // Section-scoping path: when sectionName provided, keep only that key.
            if (args.SectionName is not null)
            {
                if (!sections.TryGetValue(args.SectionName, out var sectionValue))
                {
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "GetWorkspaceTabContentHandler ({Correlation}) section_missing tabId={TabId} sectionName={SectionName} in {Duration}ms",
                        correlationLogId, args.TabId, args.SectionName, stopwatch.ElapsedMilliseconds);

                    return ToolResult.Ok(
                        HandlerId, tool.Id, tool.Name,
                        data: new GetWorkspaceTabContentPayload
                        {
                            Status = StatusSectionMissing,
                            TabId = args.TabId,
                            WidgetType = current.WidgetType,
                            Sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                            SectionCount = 0,
                            Message = $"Section '{args.SectionName}' is not present on tab '{args.TabId}'. " +
                                      $"Available sections: [{string.Join(", ", sections.Keys)}]. " +
                                      "Re-attempt with one of the available section names, or omit sectionName to read all."
                        },
                        summary: $"get_workspace_tab_content: section '{args.SectionName}' missing on tab '{args.TabId}'.",
                        confidence: 0.0,
                        execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
                }

                sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [args.SectionName] = sectionValue
                };
            }

            stopwatch.Stop();
            // ADR-015 BINDING: log section COUNT only — NEVER section names from user-author
            // payloads (section names CAN appear on the wire as they are projected from the
            // typed widget data; the tabId + count + widgetType are the safe telemetry shape).
            _logger.LogInformation(
                "GetWorkspaceTabContentHandler ({Correlation}) ok tabId={TabId} widgetType={WidgetType} sectionCount={SectionCount} sectionScoped={SectionScoped} in {Duration}ms",
                correlationLogId, args.TabId, current.WidgetType, sections.Count, args.SectionName is not null, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new GetWorkspaceTabContentPayload
                {
                    Status = StatusOk,
                    TabId = args.TabId,
                    WidgetType = current.WidgetType,
                    Sections = sections,
                    SectionCount = sections.Count,
                    Message = args.SectionName is null
                        ? $"Read {sections.Count} section(s) from workspace tab '{args.TabId}' (widgetType={current.WidgetType})."
                        : $"Read section '{args.SectionName}' from workspace tab '{args.TabId}' (widgetType={current.WidgetType})."
                },
                summary: args.SectionName is null
                    ? $"Read {sections.Count} section(s) from workspace tab '{args.TabId}'."
                    : $"Read section '{args.SectionName}' from workspace tab '{args.TabId}'.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = _timeProvider.GetUtcNow(),
                    ModelCalls = 0
                });
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "GetWorkspaceTabContentHandler ({Correlation}) cancelled tabId={TabId}",
                correlationLogId, args.TabId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Workspace tab read was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log exception TYPE only — never message body which may carry
            // service-side content fragments.
            _logger.LogError(ex,
                "GetWorkspaceTabContentHandler ({Correlation}) read failed tabId={TabId}: {ErrorType}",
                correlationLogId, args.TabId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Workspace tab read failed.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Projection: WorkspaceTabWidgetData → section-name-keyed map
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Project the typed widget data variant into a section-name-keyed dictionary the LLM
    /// can reason over. Variant-aware: each closed-union subclass contributes its
    /// LLM-visible fields. Returns deterministic identifier + display metadata only — per
    /// ADR-015, no raw user-message content (the tab itself may legitimately contain user
    /// content via SummaryTabWidgetData.Body, but that body is the agent-readable state by
    /// design; it does not leak to LOGS, only to the LLM through the tool result).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Decoupling note (POML constraint)</strong>: this handler does NOT couple to
    /// the <c>DeliverComposite</c> output-composition shape. It works for any
    /// <see cref="WorkspaceTabWidgetData"/> variant by projecting the typed fields.
    /// </para>
    /// <para>
    /// Section-name choices:
    /// </para>
    /// <list type="bullet">
    /// <item><c>Summary</c> variant → <c>tldr</c> + <c>body</c> + <c>hasUserEdits</c> (when set).
    /// The "tldr" / "body" / "hasUserEdits" key names mirror the JSON property names so the
    /// agent's pattern matching ("make the summary shorter" → modify "body") is stable.</item>
    /// <item><c>DocumentViewer</c> variant → <c>documentId</c> + <c>filename</c> +
    /// <c>mimeType</c> + <c>sizeBytes</c> + (when set) <c>hasSelection</c> +
    /// <c>selectionText</c>.</item>
    /// <item><c>Dashboard</c> variant → <c>layoutId</c> + <c>dashboardName</c> +
    /// (when set) <c>lastViewedSection</c>.</item>
    /// <item><c>Table</c> variant → <c>rowCount</c> + (when set) <c>sortColumn</c> +
    /// <c>sortDirection</c> + <c>filteredColumns</c> + <c>selectedRows</c> +
    /// <c>dataSourceId</c>.</item>
    /// </list>
    /// <para>
    /// Future variants (e.g., a true multi-section composite variant emitted by
    /// <c>DeliverComposite</c> when task 118R's schema gap is resolved) extend this switch
    /// in an additive way — no consumer rework needed.
    /// </para>
    /// </remarks>
    internal static IDictionary<string, JsonElement> ProjectComposedSections(WorkspaceTab tab)
    {
        var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        switch (tab.WidgetData)
        {
            case SummaryTabWidgetData summary:
                AddSection(sections, "body", summary.Body);
                if (summary.Tldr is not null)
                    AddSection(sections, "tldr", summary.Tldr);
                if (summary.HasUserEdits.HasValue)
                    AddSection(sections, "hasUserEdits", summary.HasUserEdits.Value);
                break;

            case DocumentViewerTabWidgetData docView:
                AddSection(sections, "documentId", docView.DocumentId);
                AddSection(sections, "filename", docView.Filename);
                AddSection(sections, "mimeType", docView.MimeType);
                AddSection(sections, "sizeBytes", docView.SizeBytes);
                if (docView.HasSelection.HasValue)
                    AddSection(sections, "hasSelection", docView.HasSelection.Value);
                if (docView.SelectionText is not null)
                    AddSection(sections, "selectionText", docView.SelectionText);
                break;

            case DashboardTabWidgetData dashboard:
                AddSection(sections, "layoutId", dashboard.LayoutId);
                AddSection(sections, "dashboardName", dashboard.DashboardName);
                if (dashboard.LastViewedSection is not null)
                    AddSection(sections, "lastViewedSection", dashboard.LastViewedSection);
                break;

            case TableTabWidgetData table:
                AddSection(sections, "rowCount", table.RowCount);
                AddSection(sections, "filteredColumns", table.FilteredColumns);
                AddSection(sections, "selectedRows", table.SelectedRows);
                if (table.SortColumn is not null)
                    AddSection(sections, "sortColumn", table.SortColumn);
                if (table.SortDirection is not null)
                    AddSection(sections, "sortDirection", table.SortDirection);
                if (table.DataSourceId is not null)
                    AddSection(sections, "dataSourceId", table.DataSourceId);
                break;
        }

        return sections;
    }

    private static void AddSection<T>(IDictionary<string, JsonElement> sections, string name, T value)
    {
        sections[name] = JsonSerializer.SerializeToElement(value);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ────────────────────────────────────────────────────────────────────────

    private static bool TryParseArgs(
        string? toolArgumentsJson,
        out ParsedArgs args,
        out string? error)
    {
        args = default;
        error = null;

        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
        {
            error = "Tool arguments JSON is required.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            var root = doc.RootElement;

            if (!root.TryGetProperty("tabId", out var tabIdProp) ||
                tabIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tabIdProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'tabId' string field.";
                return false;
            }
            var tabId = tabIdProp.GetString()!;

            string? sectionName = null;
            if (root.TryGetProperty("sectionName", out var sectionProp) &&
                sectionProp.ValueKind != JsonValueKind.Null)
            {
                if (sectionProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(sectionProp.GetString()))
                {
                    error = "'sectionName' must be a non-empty string when provided.";
                    return false;
                }
                sectionName = sectionProp.GetString();
            }

            args = new ParsedArgs
            {
                TabId = tabId,
                SectionName = sectionName
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // DTOs
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Parsed chat-call arguments.</summary>
    private readonly record struct ParsedArgs
    {
        public string TabId { get; init; }
        public string? SectionName { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. The
    /// <see cref="Status"/> field is the dispatch discriminator the LLM pattern-matches on
    /// (<c>ok</c> | <c>not_found</c> | <c>section_missing</c>). The <see cref="Sections"/>
    /// map carries the composed widget state — section-name → value.
    /// </summary>
    public sealed class GetWorkspaceTabContentPayload
    {
        /// <summary>Dispatch discriminator — see class XML doc.</summary>
        [JsonPropertyName("status")]
        public required string Status { get; init; }

        /// <summary>The target tab identifier (echo of the tool argument).</summary>
        [JsonPropertyName("tabId")]
        public required string TabId { get; init; }

        /// <summary>The tab's widget type (echo of the existing tab variant). Null on the not_found path.</summary>
        [JsonPropertyName("widgetType")]
        public string? WidgetType { get; init; }

        /// <summary>
        /// Composed sections — section name → value. Single entry when sectionName argument
        /// was provided; multi-entry when omitted. Empty on the not_found / section_missing paths.
        /// </summary>
        [JsonPropertyName("sections")]
        public required IDictionary<string, JsonElement> Sections { get; init; }

        /// <summary>
        /// Number of section entries returned (semantic count — mirrors Sections.Count). Surfaced
        /// as its own field so the LLM can pattern-match on count cheaply without iterating the map.
        /// </summary>
        [JsonPropertyName("sectionCount")]
        public required int SectionCount { get; init; }

        /// <summary>Human-readable message — re-readable by the LLM in its next turn for prompt construction.</summary>
        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }
}
