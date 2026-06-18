using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler that updates an existing workspace tab's widget data on behalf of
/// the LLM (R6 Pillar 6b / D-C-06 / task 055). Q8 conflict-resolution semantics: USER WINS.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, one method</strong>: the only LLM-facing function is
/// <c>update_workspace_tab(tabId, widgetData, expectedLastUserEditAt?)</c>. The LLM passes
/// the target tab id + the replacement widget-data payload + the timestamp it observed for
/// the tab's <see cref="WorkspaceTab.LastUserEditAt"/> when it last read the tab. The handler
/// fetches the current tab via <see cref="IWorkspaceStateService.GetTabsAsync"/>, compares
/// timestamps, and either applies the change or refuses with a structured
/// <c>stale_read</c> response.
/// </para>
///
/// <para>
/// <strong>Q8 conflict resolution (USER WINS)</strong>:
/// </para>
/// <list type="bullet">
/// <item>If the current tab's <see cref="WorkspaceTab.LastUserEditAt"/> is strictly later than
/// the LLM-supplied <c>expectedLastUserEditAt</c>, the handler REFUSES with a
/// <see cref="ToolResult"/> success carrying a structured <c>stale_read</c> payload
/// (status, currentTimestamp, message) so the agent can re-read the tab and re-attempt in a
/// later turn. NO mutation occurs.</item>
/// <item>If <c>expectedLastUserEditAt</c> is null, the agent is asserting "I created this tab
/// or the tab has never been user-edited" — the handler applies the update unconditionally
/// when the tab's stored <see cref="WorkspaceTab.LastUserEditAt"/> is also null. When the tab
/// has been user-edited (stored <c>LastUserEditAt</c> non-null) but the LLM supplied null,
/// the handler still refuses with a stale_read response.</item>
/// <item>On clean update: the handler preserves <see cref="WorkspaceTab.LastUserEditAt"/>
/// (this field tracks USER edits only; agent edits MUST NOT bump it per spec FR-40), bumps
/// <see cref="WorkspaceTab.UpdatedAt"/> to the agent edit timestamp, replaces
/// <see cref="WorkspaceTab.WidgetData"/>, and persists via
/// <see cref="IWorkspaceStateService.UpsertTabAsync"/>.</item>
/// </list>
///
/// <para>
/// <strong>Auto-discovery + data-driven registration (R6 Pillar 2)</strong>: registered via
/// the <c>sprk_analysistool</c> seed row
/// (<c>infra/dataverse/sprk_analysistool-update-workspace-tab-row.json</c>). Routes to this
/// C# class via <c>sprk_handlerclass = "UpdateWorkspaceTabHandler"</c>. Auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> — ZERO new
/// <c>Program.cs</c> / <c>AnalysisServicesModule</c> lines.
/// </para>
///
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only.
/// Workspace tab mutation is a chat-surface affordance — playbook nodes write to
/// <c>sprk_analysisoutput</c> + <c>sprk_workingdocument</c>, not the chat-session per-tenant
/// workspace tab list. Mirrors task 054 (<see cref="SendWorkspaceArtifactHandler"/>).
/// </para>
///
/// <para>
/// <strong>Capability gate</strong>: <c>sprk_requiredcapability = null</c>. Updating a
/// workspace tab is a default user affordance available in every chat session. The handler's
/// own validation (tab must exist + be owned by the chat session's tenant + must have
/// <see cref="WorkspaceTab.CanEdit"/> = true) is the authorization surface.
/// </para>
///
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.
/// Dependencies (<see cref="IWorkspaceStateService"/>, <see cref="TimeProvider"/>) are
/// already registered by <c>WorkspaceModule</c>.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IWorkspaceStateService"/> directly (BFF-internal workspace plumbing) — NOT
/// through a PublicContracts facade. Mirrors task 054 placement rationale.</item>
/// <item><strong>ADR-014</strong>: <see cref="ChatInvocationContext.TenantId"/> is required
/// and forwarded into every Redis/Cosmos call; the handler refuses when the resolved tab's
/// <see cref="WorkspaceTab.TenantId"/> does not match (defense in depth — the service-layer
/// key already includes tenantId, but a tab-id collision across tenants would otherwise
/// surface a tab to the wrong agent).</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + decision
/// (<c>applied</c> | <c>refused_stale_read</c> | <c>refused_*</c>) + tabId + deterministic
/// IDs + duration ONLY. NEVER the LLM-supplied <c>widgetData</c> body, NEVER the tab's
/// existing widget content, NEVER user message text. The <see cref="ChatInvocationContext.MatterId"/>
/// is logged as boolean present/absent for the same reason.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// <item><strong>ADR-030</strong>: no new SSE / PaneEventBus channel introduced. The
/// frontend tab strip re-materializes the updated tab via the existing workspace polling
/// endpoint (R6 Pillar 6a) — the client-side <c>workspace.tab_edited</c> PaneEventBus event
/// is dispatched by the frontend tab manager when the polled state delta is observed (task
/// 060 additive event types). The handler's responsibility ends at persistence.</item>
/// </list>
/// </remarks>
public sealed class UpdateWorkspaceTabHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(UpdateWorkspaceTabHandler);

    // ─────────────────────────────────────────────────────────────────────────
    // Telemetry (R6 task 058 / Q8 conflict resolution)
    //
    // A single static Meter wired with a deterministic counter
    // `workspace.conflict_refused` emitted at the stale-read refusal point. Per
    // ADR-015 BINDING: dimensions are deterministic IDs ONLY (tenantId,
    // sessionId, tabId, decision discriminator). NEVER user message text,
    // widget body content, or LLM response text. The values flowing in
    // (TenantId, ChatSessionId, TabId) are caller-supplied opaque identifiers
    // already audited as Tier-1 telemetry-safe by ADR-013 / ADR-015.
    //
    // Meter name follows the existing Sprk.Bff.Api.* convention. Static so the
    // counter survives handler instance churn (handlers are Scoped); the OTel
    // SDK picks up the Meter once at startup.
    // ─────────────────────────────────────────────────────────────────────────
    internal const string MeterName = "Sprk.Bff.Api.Workspace";
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> _conflictRefusedCounter = _meter.CreateCounter<long>(
        name: "workspace.conflict_refused",
        unit: "{refusal}",
        description: "Number of update_workspace_tab calls refused because the agent's view was stale (Q8 USER WINS).");

    /// <summary>
    /// Structured-payload status discriminator for the stale-read refusal path. Documented in
    /// the handler description so the agent can pattern-match on the value in its next-turn
    /// reasoning (per Q8 binding: refusal is a re-actable response, not an error).
    /// </summary>
    internal const string StatusStaleRead = "stale_read";

    /// <summary>
    /// Structured-payload status discriminator for the clean-write path.
    /// </summary>
    internal const string StatusApplied = "applied";

    private static readonly JsonSerializerOptions WidgetDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWorkspaceStateService _workspaceStateService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateWorkspaceTabHandler> _logger;

    public UpdateWorkspaceTabHandler(
        IWorkspaceStateService workspaceStateService,
        TimeProvider timeProvider,
        ILogger<UpdateWorkspaceTabHandler> logger)
    {
        _workspaceStateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Update Workspace Tab",
        Description: "Update an existing workspace tab's widget data. Use this when you have refined a previously " +
                     "dispatched artifact and the user should see the updated state on the same tab (instead of a new one). " +
                     "Conflict resolution: pass the 'expectedLastUserEditAt' timestamp you observed when you last read the " +
                     "tab. If the user has edited the tab since then, this tool will REFUSE with a 'stale_read' status — " +
                     "you must re-read the tab in your next turn before re-attempting. User edits always win.",
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
                "widgetData",
                "Replacement widget-data JSON object. MUST include a 'kind' field equal to the existing tab's widgetType " +
                "(Summary | DocumentViewer | Dashboard | Table). The variant-specific fields follow the same shape " +
                "documented for send_workspace_artifact.",
                ToolParameterType.Object,
                Required: true),
            new ToolParameterDefinition(
                "expectedLastUserEditAt",
                "Optional ISO-8601 timestamp the agent observed for the tab's lastUserEditAt when it last read the tab. " +
                "Omit (null) when the tab has never been user-edited (e.g., the agent just created it via " +
                "send_workspace_artifact). When supplied, the handler refuses the write if the current stored " +
                "lastUserEditAt is later than this value.",
                ToolParameterType.String,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context invocation rejected — workspace tab mutation is a chat-only affordance
    /// (playbook nodes write to <c>sprk_analysisoutput</c>, not the chat-session workspace).
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "UpdateWorkspaceTabHandler is chat-context-only. Playbook-context invocation is unsupported.");

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

            // widgetData — required JSON object with 'kind' discriminator.
            if (!doc.RootElement.TryGetProperty("widgetData", out var dataProp) ||
                dataProp.ValueKind != JsonValueKind.Object)
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a 'widgetData' JSON object.");
            }

            if (!dataProp.TryGetProperty("kind", out var kindProp) ||
                kindProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(kindProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "'widgetData' must include a 'kind' string discriminator.");
            }

            // expectedLastUserEditAt — optional; when supplied MUST be parseable ISO-8601.
            if (doc.RootElement.TryGetProperty("expectedLastUserEditAt", out var expectedProp) &&
                expectedProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(expectedProp.GetString()) &&
                !DateTimeOffset.TryParse(expectedProp.GetString(), out _))
            {
                return ToolValidationResult.Failure(
                    "'expectedLastUserEditAt' must be a parseable ISO-8601 timestamp when provided.");
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
            "UpdateWorkspaceTabHandler is chat-context-only. Playbook-context invocation is unsupported.",
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

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "UpdateWorkspaceTabHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // ADR-015: log handler + IDs + tabId + expectedLastUserEditAt PRESENCE (not value) + matter
        // scope presence ONLY. NEVER the widgetData body.
        _logger.LogInformation(
            "UpdateWorkspaceTabHandler ({Correlation}) update start tabId={TabId} tenantId={TenantId} expectedTimestampSupplied={ExpectedSupplied} matterScoped={MatterScoped}",
            correlationLogId, args.TabId, context.TenantId, args.ExpectedLastUserEditAt is not null, context.MatterId is not null);

        var sessionId = context.ChatSessionId.ToString("N");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read the current tab from the merged hot+durable tier. GetTabsAsync returns all
            // tabs for (tenant, session); we filter to the target tabId. The service-layer key
            // already scopes by tenantId so cross-tenant reads are impossible, but we also
            // assert TenantId match below as defense in depth.
            var tabs = await _workspaceStateService
                .GetTabsAsync(context.TenantId, sessionId, cancellationToken)
                .ConfigureAwait(false);

            var current = tabs.FirstOrDefault(t => string.Equals(t.Id, args.TabId, StringComparison.Ordinal));

            if (current is null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "UpdateWorkspaceTabHandler ({Correlation}) refused_not_found tabId={TabId} in {Duration}ms",
                    correlationLogId, args.TabId, stopwatch.ElapsedMilliseconds);

                // Return success-with-status so the LLM can re-react (the LLM may have hallucinated
                // a tab id, or the tab may have been closed by the user). Not an internal error.
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new UpdateWorkspaceTabPayload
                    {
                        Status = "refused_not_found",
                        TabId = args.TabId,
                        Message = $"Tab '{args.TabId}' was not found in the current workspace state. " +
                                  "It may have been closed by the user, or the id may be stale. Re-read the " +
                                  "workspace state before re-attempting."
                    },
                    summary: $"update_workspace_tab refused: tab '{args.TabId}' not found.",
                    confidence: 0.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Defense-in-depth tenant check. The Redis key + Cosmos partition key already scope
            // by tenantId, so this branch is unreachable under normal operation — but a
            // misconfigured cache layer or a future routing bug must not surface a foreign-tenant
            // tab to the agent.
            if (!string.Equals(current.TenantId, context.TenantId, StringComparison.Ordinal))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "UpdateWorkspaceTabHandler ({Correlation}) refused_tenant_mismatch tabId={TabId} ctxTenant={CtxTenant} tabTenant={TabTenant}",
                    correlationLogId, args.TabId, context.TenantId, current.TenantId);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "Tab tenant mismatch — refusing update.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // CanEdit gate (per WorkspaceTab.CanEdit XML doc binding from Pillar 6a model).
            if (!current.CanEdit)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "UpdateWorkspaceTabHandler ({Correlation}) refused_not_editable tabId={TabId} in {Duration}ms",
                    correlationLogId, args.TabId, stopwatch.ElapsedMilliseconds);
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new UpdateWorkspaceTabPayload
                    {
                        Status = "refused_not_editable",
                        TabId = args.TabId,
                        Message = $"Tab '{args.TabId}' is not editable (canEdit=false). " +
                                  "Editing is disabled for this tab variant."
                    },
                    summary: $"update_workspace_tab refused: tab '{args.TabId}' is not editable.",
                    confidence: 0.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Q8 conflict-resolution decision: USER WINS.
            //   - If stored LastUserEditAt > LLM-supplied expectedLastUserEditAt → refuse.
            //   - If LLM supplied null but stored is non-null → also refuse (the agent's view
            //     is inconsistent with the current tab state — a user edit happened since the
            //     last read regardless of when).
            //   - If LLM supplied non-null and stored is null → permitted (the stored value
            //     was reset, e.g., by a prior agent update that cleared it; we don't know but
            //     the user has not edited, so no conflict).
            //   - If both null → permitted (tab has never been user-edited; agent just created it).
            //   - If supplied == stored (or equivalent parsed times) → permitted (the agent's
            //     read is current).
            var conflictDecision = EvaluateConflict(
                storedLastUserEditAt: current.LastUserEditAt,
                expectedLastUserEditAt: args.ExpectedLastUserEditAt);

            if (conflictDecision.IsStale)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "UpdateWorkspaceTabHandler ({Correlation}) refused_stale_read tabId={TabId} in {Duration}ms",
                    correlationLogId, args.TabId, stopwatch.ElapsedMilliseconds);

                // ADR-015 BINDING: deterministic IDs + decision discriminator ONLY.
                // No widget body / message content / timestamp values are attached as
                // tag dimensions. The `decision` tag enables aggregation by refusal
                // category in Application Insights (future: refused_not_found /
                // refused_not_editable can share the same counter with different
                // decisions if value-add emerges).
                _conflictRefusedCounter.Add(1,
                    new KeyValuePair<string, object?>("tenantId", context.TenantId),
                    new KeyValuePair<string, object?>("sessionId", sessionId),
                    new KeyValuePair<string, object?>("tabId", args.TabId),
                    new KeyValuePair<string, object?>("decision", StatusStaleRead));

                // Q8 structured stale-read response. The LLM is instructed (via the persona
                // snippet wired by task 058) to re-read the tab and re-attempt in a later turn.
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new UpdateWorkspaceTabPayload
                    {
                        Status = StatusStaleRead,
                        TabId = args.TabId,
                        CurrentLastUserEditAt = current.LastUserEditAt,
                        Message = "Tab was edited by the user since your last read; please re-read the workspace " +
                                  "state before re-attempting the update."
                    },
                    summary: $"update_workspace_tab refused: stale_read on tab '{args.TabId}'. " +
                             $"Re-read the tab before re-attempting.",
                    confidence: 0.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Clean update path. Deserialize the LLM-supplied widget data; mutate the tab record.
            WorkspaceTabWidgetData widgetData;
            try
            {
                widgetData = DeserializeWidgetData(args.WidgetDataRawJson, current.WidgetType);
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "UpdateWorkspaceTabHandler ({Correlation}) widgetData deserialization failed for tabId={TabId} widgetType={WidgetType}",
                    correlationLogId, args.TabId, current.WidgetType);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"widgetData payload could not be deserialized for widgetType '{current.WidgetType}': {ex.Message}",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Defense in depth: the new payload's kind discriminator MUST match the tab's
            // existing widgetType. An agent attempting to mutate a Summary tab into a Table
            // tab would violate the 4-variant union invariant.
            if (!string.Equals(widgetData.Kind, current.WidgetType, StringComparison.Ordinal))
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "UpdateWorkspaceTabHandler ({Correlation}) refused_kind_mismatch tabId={TabId} existingType={ExistingType} newKind={NewKind}",
                    correlationLogId, args.TabId, current.WidgetType, widgetData.Kind);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"widgetData.kind ('{widgetData.Kind}') must equal the existing tab widgetType ('{current.WidgetType}'). " +
                    "Updating a tab's widgetType is not supported — close the tab and create a new one.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            var updatedAtIso = startedAt.ToString("o");

            // Preserve LastUserEditAt verbatim (this field tracks USER edits only per spec FR-40
            // + WorkspaceTab.LastUserEditAt XML doc); bump UpdatedAt to the agent edit time.
            // Preserve CreatedAt, SourceProvenance, MatterContext, IsPinned, CanEdit,
            // VisibleToAssistant, SessionId, TenantId — they are immutable from the agent's
            // perspective.
            var mutated = new WorkspaceTab
            {
                Id = current.Id,
                WidgetType = current.WidgetType,
                WidgetData = widgetData,
                SessionId = current.SessionId,
                TenantId = current.TenantId,
                VisibleToAssistant = current.VisibleToAssistant,
                SourceProvenance = current.SourceProvenance,
                MatterContext = current.MatterContext,
                IsPinned = current.IsPinned,
                CanEdit = current.CanEdit,
                LastUserEditAt = current.LastUserEditAt,
                CreatedAt = current.CreatedAt,
                UpdatedAt = updatedAtIso
            };

            await _workspaceStateService
                .UpsertTabAsync(context.TenantId, sessionId, mutated, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "UpdateWorkspaceTabHandler ({Correlation}) applied tabId={TabId} widgetType={WidgetType} in {Duration}ms",
                correlationLogId, args.TabId, current.WidgetType, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new UpdateWorkspaceTabPayload
                {
                    Status = StatusApplied,
                    TabId = args.TabId,
                    WidgetType = current.WidgetType,
                    UpdatedAt = updatedAtIso,
                    CurrentLastUserEditAt = current.LastUserEditAt,
                    Message = "Tab updated successfully."
                },
                summary: $"Workspace tab '{args.TabId}' (widgetType={current.WidgetType}) updated.",
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
                "UpdateWorkspaceTabHandler ({Correlation}) cancelled tabId={TabId}",
                correlationLogId, args.TabId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Workspace tab update was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (InvalidOperationException ex)
        {
            // Tenant/session-mismatch guard from WorkspaceStateService.UpsertTabAsync — surface
            // as ValidationFailed.
            stopwatch.Stop();
            _logger.LogError(ex,
                "UpdateWorkspaceTabHandler ({Correlation}) tenant/session mismatch on upsert tabId={TabId}: {Reason}",
                correlationLogId, args.TabId, ex.Message);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Workspace tab update failed validation: {ex.Message}",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "UpdateWorkspaceTabHandler ({Correlation}) update failed tabId={TabId}: {ErrorType}",
                correlationLogId, args.TabId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Workspace tab update failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Conflict evaluation (Q8)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decide whether the agent's read is stale per Q8 (USER WINS) semantics.
    /// </summary>
    /// <param name="storedLastUserEditAt">The tab's currently-stored <c>LastUserEditAt</c>. Null when no user edit has occurred.</param>
    /// <param name="expectedLastUserEditAt">The timestamp the agent observed when it last read the tab. Null when the agent
    /// asserts no preceding user edit (e.g., agent just created the tab).</param>
    /// <returns>A <see cref="ConflictDecision"/> with the <see cref="ConflictDecision.IsStale"/> flag set when the write must be refused.</returns>
    /// <remarks>
    /// <para>
    /// Four cases:
    /// </para>
    /// <list type="bullet">
    /// <item>stored = null, expected = null → permitted (tab has never been user-edited).</item>
    /// <item>stored = null, expected = non-null → permitted (the stored value was reset or never set;
    /// agent's view is at least as old as the stored state — no user edit happened since).</item>
    /// <item>stored = non-null, expected = null → refused (a user edit happened that the agent has not
    /// observed yet — its view is structurally stale).</item>
    /// <item>stored = non-null, expected = non-null → refused if storedTime > expectedTime;
    /// permitted otherwise. Timestamps are compared as <see cref="DateTimeOffset"/> after parsing.</item>
    /// </list>
    /// </remarks>
    internal static ConflictDecision EvaluateConflict(
        string? storedLastUserEditAt,
        string? expectedLastUserEditAt)
    {
        var storedHas = !string.IsNullOrWhiteSpace(storedLastUserEditAt);
        var expectedHas = !string.IsNullOrWhiteSpace(expectedLastUserEditAt);

        if (!storedHas)
        {
            // Either both null or stored null with expected supplied. Both permitted.
            return new ConflictDecision(IsStale: false);
        }

        if (!expectedHas)
        {
            // Stored non-null but the agent thinks no edit happened — its view is stale.
            return new ConflictDecision(IsStale: true);
        }

        // Both non-null: compare parsed timestamps. Validation already gated parse-ability of
        // the LLM-supplied value; the stored value originates from this handler / pillar 6a, so
        // it is well-formed. We still guard with TryParse for defense in depth.
        if (!DateTimeOffset.TryParse(storedLastUserEditAt, out var storedTime) ||
            !DateTimeOffset.TryParse(expectedLastUserEditAt, out var expectedTime))
        {
            // Treat unparseable timestamps as stale — refuse the write rather than risk a
            // false-positive permit (USER WINS is the conservative default).
            return new ConflictDecision(IsStale: true);
        }

        return new ConflictDecision(IsStale: storedTime > expectedTime);
    }

    /// <summary>
    /// Internal record carrying the conflict-evaluation outcome.
    /// </summary>
    internal readonly record struct ConflictDecision(bool IsStale);

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

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

            if (!root.TryGetProperty("widgetData", out var dataProp) ||
                dataProp.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must include a 'widgetData' JSON object.";
                return false;
            }
            var widgetDataRawJson = dataProp.GetRawText();

            string? expectedLastUserEditAt = null;
            if (root.TryGetProperty("expectedLastUserEditAt", out var expectedProp) &&
                expectedProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(expectedProp.GetString()))
            {
                var raw = expectedProp.GetString()!;
                if (!DateTimeOffset.TryParse(raw, out _))
                {
                    error = "'expectedLastUserEditAt' must be a parseable ISO-8601 timestamp when provided.";
                    return false;
                }
                expectedLastUserEditAt = raw;
            }

            args = new ParsedArgs
            {
                TabId = tabId,
                WidgetDataRawJson = widgetDataRawJson,
                ExpectedLastUserEditAt = expectedLastUserEditAt
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Widget data deserialization
    // ─────────────────────────────────────────────────────────────────────────────

    private static WorkspaceTabWidgetData DeserializeWidgetData(string widgetDataRawJson, string widgetType)
    {
        var deserialized = JsonSerializer.Deserialize<WorkspaceTabWidgetData>(widgetDataRawJson, WidgetDataJsonOptions);
        if (deserialized is null)
        {
            throw new JsonException(
                $"widgetData deserialized to null for widgetType '{widgetType}' — the LLM-supplied payload is structurally invalid.");
        }
        return deserialized;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Parsed chat-call arguments.</summary>
    private readonly record struct ParsedArgs
    {
        public string TabId { get; init; }
        public string WidgetDataRawJson { get; init; }
        public string? ExpectedLastUserEditAt { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. The
    /// <see cref="Status"/> field is the dispatch discriminator the LLM pattern-matches on
    /// (<c>applied</c> | <c>refused_stale_read</c> | <c>refused_not_found</c> |
    /// <c>refused_not_editable</c>). Q8 binding: refusal is a re-actable response, not an error.
    /// ADR-015 binding: NEVER carries the widget data body.
    /// </summary>
    public sealed class UpdateWorkspaceTabPayload
    {
        /// <summary>Dispatch discriminator — see class XML doc.</summary>
        [JsonPropertyName("status")]
        public required string Status { get; init; }

        /// <summary>The target tab identifier (echo of the tool argument).</summary>
        [JsonPropertyName("tabId")]
        public required string TabId { get; init; }

        /// <summary>The tab's widget type (echo of the existing tab variant); null on refusal paths that don't fetch.</summary>
        [JsonPropertyName("widgetType")]
        public string? WidgetType { get; init; }

        /// <summary>Server-side updatedAt timestamp on the applied path.</summary>
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; init; }

        /// <summary>The tab's current <c>LastUserEditAt</c> — populated on both the applied path and the stale-read path so the agent can refresh its observed value.</summary>
        [JsonPropertyName("currentLastUserEditAt")]
        public string? CurrentLastUserEditAt { get; init; }

        /// <summary>Human-readable message — re-readable by the LLM in its next turn for prompt construction.</summary>
        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }
}
