using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler that dispatches a finished agent artifact (Summary, DocumentViewer,
/// Dashboard, or Table) to the workspace pane as a new tab (R6 Pillar 6b / D-C-05 / task 054).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, one method</strong>: the only LLM-facing function is
/// <c>send_workspace_artifact(widgetType, title, widgetData, matterId?)</c>. The LLM passes a
/// widget-typed payload + display title; the handler constructs a <see cref="WorkspaceTab"/>
/// + calls <see cref="IWorkspaceStateService.UpsertTabAsync"/> + returns a tab-created summary
/// to the LLM. The frontend tab strip materializes the tab on the next
/// <c>GET /api/workspace/tabs</c> poll (the workspace state service is the deterministic source
/// of truth — no out-of-band SSE event is required for tab materialization because the polling
/// channel ALREADY exists per Pillar 6a).
/// </para>
/// <para>
/// <strong>Auto-discovery + data-driven registration (R6 Pillar 2)</strong>: registered via the
/// <c>sprk_analysistool</c> seed row
/// (<c>infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json</c>). Routes to this
/// C# class via <c>sprk_handlerclass = "SendWorkspaceArtifactHandler"</c>. Auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> — ZERO new
/// <c>Program.cs</c> / <c>AnalysisServicesModule</c> lines.
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only.
/// Workspace tab dispatch is a chat-surface affordance — a playbook node creating a workspace
/// tab via chat-tool indirection is not a coherent flow (playbook nodes write to
/// <c>sprk_analysisoutput</c> + <c>sprk_workingdocument</c>, not the chat session's per-tenant
/// workspace tab list).
/// </para>
/// <para>
/// <strong>Capability gate</strong>: <c>sprk_requiredcapability = null</c>. Sending an artifact
/// to the workspace pane is a default user affordance — every chat session, standalone or
/// playbook-bound, can dispatch artifacts. The 4-variant <see cref="WidgetType"/> enum is the
/// authorization surface (Summary | DocumentViewer | Dashboard | Table) — there is no "free
/// text widget" escape hatch, so the LLM cannot smuggle arbitrary payloads into the workspace
/// outside the closed union.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.
/// Dependencies (<see cref="IWorkspaceStateService"/>, <see cref="IGuidProvider"/>,
/// <see cref="TimeProvider"/>) are already registered by <c>WorkspaceModule</c>.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IWorkspaceStateService"/> (BFF-internal workspace plumbing) directly — NOT
/// through a PublicContracts facade because workspace state is not an AI capability (it is
/// a chat-session state primitive owned by Pillar 6a). Mirrors task 053 (system-prompt
/// composition reads <see cref="IWorkspaceStateService"/> directly inside
/// <c>SprkChatAgentFactory</c>).</item>
/// <item><strong>ADR-014</strong>: <c>TenantId</c> is required on the
/// <see cref="ChatInvocationContext"/> and forwarded into the tab's
/// <see cref="WorkspaceTab.TenantId"/> + the underlying Redis key + Cosmos partition key. Cross-
/// tenant dispatch is structurally impossible (the workspace state service enforces a tenant
/// match between <paramref name="tenantId"/> and <see cref="WorkspaceTab.TenantId"/>).</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + decision + tabId + widgetType
/// + matter scope present/absent + duration ONLY. NEVER the LLM-supplied <c>widgetData</c>
/// content; NEVER the user's chat message. The handler does NOT inspect or log
/// <see cref="WorkspaceTab.WidgetData"/> bodies — only the discriminator. The tab's
/// <see cref="WorkspaceTabSourceProvenance.CreatedBy"/> is the deterministic
/// <c>agent:{chatSessionId}</c> sentinel — never user message text.</item>
/// <item><strong>ADR-016</strong>: no separate rate limiter — the chat session's existing
/// concurrency slot (per-session lock in <c>ChatSessionManager</c>) caps the dispatch rate.
/// One artifact per LLM tool-call turn matches the natural conversational cadence.</item>
/// <item><strong>ADR-018</strong>: NO new feature flag. The handler's auto-discovery is gated
/// by <see cref="IWorkspaceStateService"/> resolving in DI — when Pillar 6a is not deployed
/// (e.g., Redis + Cosmos unconfigured), the handler is not auto-discovered and the LLM does
/// not see the tool.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// <item><strong>ADR-030</strong>: no new SSE channel added. The frontend tab strip
/// re-materializes via the existing workspace polling endpoint (R6 Pillar 6a). The R6 Pillar
/// 9 prompt builder reads the same workspace state on the next chat turn so the LLM sees
/// the new tab in its system prompt.</item>
/// </list>
/// </remarks>
public sealed class SendWorkspaceArtifactHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(SendWorkspaceArtifactHandler);

    /// <summary>
    /// Closed enum of widget-type discriminators accepted by this handler. The four
    /// artifact variants mirror <see cref="WorkspaceTabWidgetData"/>'s
    /// <c>[JsonDerivedType]</c> annotations + the R6-era TypeScript <c>WorkspaceTab</c>
    /// contract. <c>Workspace</c> (G-P3 UAT round-2 R2-D, 2026-07-07) opens a NAMED
    /// workspace LAYOUT (e.g. "Compose") as a live tab in the SpaarkeAi workspace pane —
    /// the same mechanism the Workspaces menu drives client-side
    /// (<c>widget_load {widgetType:'workspace', widgetData:{layoutId, layoutName}}</c>).
    /// Any value outside this set is rejected by <see cref="ValidateChat"/> with a clear error.
    /// </summary>
    internal static readonly HashSet<string> SupportedWidgetTypes = new(StringComparer.Ordinal)
    {
        "Summary",
        "DocumentViewer",
        "Dashboard",
        "Table",
        "Workspace"
    };

    /// <summary>
    /// The client workspace-widget registry key for a layout tab. MUST match the
    /// registration in <c>register-workspace-widgets.ts</c> ("workspace" renders the
    /// embedded LegalWorkspaceApp with <c>{layoutId, layoutName}</c>).
    /// </summary>
    internal const string ClientWorkspaceWidgetKey = "workspace";

    /// <summary>SSE discriminant for the live tab-open frame (rides the existing context_event channel — ADR-030 additive).</summary>
    internal const string WorkspaceOpenTabEventType = "workspace_open_tab";

    /// <summary>
    /// D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): how long the tool result waits
    /// for the client's <c>POST /api/ai/chat/sessions/{sessionId}/ack</c> before failing
    /// honestly. Generous enough to absorb SSE delivery + PaneEventBus dispatch + the client's
    /// ack round-trip under normal network conditions, short enough that a genuinely
    /// unreachable client doesn't stall the tool call.
    /// </summary>
    internal static readonly TimeSpan WorkspaceTabAckTimeout = TimeSpan.FromSeconds(8);

    private static readonly JsonSerializerOptions WidgetDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWorkspaceStateService _workspaceStateService;
    private readonly IGuidProvider _guidProvider;
    private readonly TimeProvider _timeProvider;
    private readonly WorkspaceLayoutService _layoutService;
    private readonly IDataverseUserClient _dataverse;
    private readonly IUiActionAckCoordinator _ackCoordinator;
    private readonly ILogger<SendWorkspaceArtifactHandler> _logger;

    public SendWorkspaceArtifactHandler(
        IWorkspaceStateService workspaceStateService,
        IGuidProvider guidProvider,
        TimeProvider timeProvider,
        WorkspaceLayoutService layoutService,
        IDataverseUserClient dataverse,
        IUiActionAckCoordinator ackCoordinator,
        ILogger<SendWorkspaceArtifactHandler> logger)
    {
        _workspaceStateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
        _guidProvider = guidProvider ?? throw new ArgumentNullException(nameof(guidProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        // R4-2 (2026-07-07): resolves a widgetData.documentId (sprk_document) into its SPE
        // drive-item pointer for the Compose pre-seed — user-OBO, same posture as the
        // dataverse.* handlers (a document invisible to the user fails as unresolvable).
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): gates this tool's success
        // on a client ack referencing the emitted frame id (see ExecuteOpenWorkspaceTabAsync).
        _ackCoordinator = ackCoordinator ?? throw new ArgumentNullException(nameof(ackCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Send Workspace Artifact",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Open a tab in the user's workspace pane. PREFERRED USE — open a named workspace LAYOUT as a live tab: widgetType 'Workspace' with widgetData {""kind"":""Workspace"",""layoutName"":""<layout name>""}. Use layoutName 'Compose' when the user asks to open the Compose editor or to start composing/drafting a document in the workspace; other layouts (e.g. 'Daily Briefing', 'Documents') open the same way. When opening the Compose layout ABOUT A SPECIFIC DOCUMENT, add widgetData.documentId (the sprk_document GUID, e.g. from a search result path tables/sprk_document/records/{guid}) — Compose then opens WITH that document loaded. Session-UPLOADED chat files cannot pre-seed Compose (they exist only as extracted text, not stored documents) — omit documentId for them; the tab opens empty and the tool result says so — relay it honestly. If the requested layout does not exist, the tool result lists the available layout names — relay them honestly. The tab opens immediately in the workspace pane and the tool result confirms it; only claim a tab was opened when this tool result says so. Legacy artifact variants (Summary, DocumentViewer, Dashboard, Table) record an artifact to workspace state but are NOT visible in the current workspace UI — avoid them unless explicitly instructed. Agent-created tabs default to visible-to-assistant.",
        Version: "1.1.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "widgetType",
                "Closed enum: 'Workspace' (open a named workspace layout as a live tab — preferred) | " +
                "'Summary' | 'DocumentViewer' | 'Dashboard' | 'Table' (legacy artifact variants).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "title",
                "Display title for the workspace tab (shown on the tab strip and in tooltips). Should be short and matter-relevant.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "widgetData",
                "JSON object carrying the per-variant payload. MUST include a 'kind' field equal to widgetType. " +
                "Workspace: layoutName (the workspace layout's display name, e.g. 'Compose') or layoutId (GUID); " +
                "optional documentId (sprk_document GUID) to pre-seed the Compose layout with that document " +
                "(session-uploaded chat files are NOT pre-seedable — omit documentId for them). " +
                "Legacy variants: Summary: body; DocumentViewer: documentId/filename/mimeType/sizeBytes; " +
                "Dashboard: layoutId/dashboardName; Table: rowCount/filteredColumns/selectedRows.",
                ToolParameterType.Object,
                Required: true),
            new ToolParameterDefinition(
                "matterId",
                "Optional Dataverse sprk_matter GUID to anchor the tab to. When omitted, the chat session's " +
                "MatterId is used; when neither is present the tab is anchored to a synthetic 'unattached' " +
                "context that the user can later pin to a matter.",
                ToolParameterType.String,
                Required: false),
            new ToolParameterDefinition(
                "matterName",
                "Optional display name for the matter (used in tab tooltip when matterId is supplied).",
                ToolParameterType.String,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context invocation rejected — workspace tab dispatch is a chat-only affordance
    /// (playbook nodes write to <c>sprk_analysisoutput</c>, not the chat-session workspace).
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "SendWorkspaceArtifactHandler is chat-context-only. Playbook-context invocation is unsupported.");

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

            // widgetType — required, non-empty, closed enum.
            if (!doc.RootElement.TryGetProperty("widgetType", out var wtProp) ||
                wtProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(wtProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'widgetType' string field.");
            }

            var widgetType = wtProp.GetString()!;
            if (!SupportedWidgetTypes.Contains(widgetType))
            {
                return ToolValidationResult.Failure(
                    $"'widgetType' must be one of: {string.Join(", ", SupportedWidgetTypes)}. Received: '{widgetType}'.");
            }

            // title — required, non-empty string.
            if (!doc.RootElement.TryGetProperty("title", out var titleProp) ||
                titleProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'title' string field.");
            }

            // widgetData — required JSON object; MUST contain a 'kind' discriminator that matches widgetType.
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
                    "'widgetData' must include a 'kind' string discriminator matching widgetType.");
            }

            if (!string.Equals(kindProp.GetString(), widgetType, StringComparison.Ordinal))
            {
                return ToolValidationResult.Failure(
                    $"'widgetData.kind' ('{kindProp.GetString()}') must equal 'widgetType' ('{widgetType}').");
            }

            // Workspace layout tabs (R2-D): the payload must name the layout to open.
            if (string.Equals(widgetType, "Workspace", StringComparison.Ordinal))
            {
                var hasLayoutName = dataProp.TryGetProperty("layoutName", out var lnProp) &&
                                    lnProp.ValueKind == JsonValueKind.String &&
                                    !string.IsNullOrWhiteSpace(lnProp.GetString());
                var hasLayoutId = dataProp.TryGetProperty("layoutId", out var liProp) &&
                                  liProp.ValueKind == JsonValueKind.String &&
                                  Guid.TryParse(liProp.GetString(), out _);
                if (!hasLayoutName && !hasLayoutId)
                {
                    return ToolValidationResult.Failure(
                        "'widgetData' for widgetType 'Workspace' must include 'layoutName' (the layout's " +
                        "display name, e.g. 'Compose') or 'layoutId' (a GUID).");
                }

                // R4-2 (2026-07-07): optional Compose pre-seed pointer — when present it
                // must be a real sprk_document GUID (session file ids are rejected here
                // with an instructive message; they carry no SPE pointer to load from).
                if (dataProp.TryGetProperty("documentId", out var docIdProp) &&
                    docIdProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(docIdProp.GetString()) &&
                    !Guid.TryParse(docIdProp.GetString(), out _))
                {
                    return ToolValidationResult.Failure(
                        "'widgetData.documentId' must be a sprk_document GUID when provided. " +
                        "Session-uploaded chat file ids cannot pre-seed Compose — omit documentId " +
                        "and tell the user the Compose tab opens empty for session files.");
                }
            }

            // matterId — optional; when present MUST be a valid GUID.
            if (doc.RootElement.TryGetProperty("matterId", out var matterIdProp) &&
                matterIdProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(matterIdProp.GetString()) &&
                !Guid.TryParse(matterIdProp.GetString(), out _))
            {
                return ToolValidationResult.Failure("'matterId' must be a valid GUID when provided.");
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
            "SendWorkspaceArtifactHandler is chat-context-only. Playbook-context invocation is unsupported.",
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
                "SendWorkspaceArtifactHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // ADR-015: log handler + IDs + widgetType + matter scope present/absent + title LENGTH.
        // NEVER the widgetData body; NEVER the title text.
        _logger.LogInformation(
            "SendWorkspaceArtifactHandler ({Correlation}) dispatch start widgetType={WidgetType} tenantId={TenantId} matterScoped={MatterScoped} titleLen={TitleLen}",
            correlationLogId, args.WidgetType, context.TenantId, args.MatterId is not null, args.Title.Length);

        // ── G-P3 UAT round-2 R2-D (2026-07-07): Workspace layout tab — live open ─────
        // Opens a named workspace layout (e.g. "Compose") as a tab in the SpaarkeAi
        // workspace pane by emitting a `workspace_open_tab` frame on the existing
        // context_event SSE channel; the client bridge dispatches the SAME
        // PaneEventBus `workspace.widget_load` event the Workspaces menu uses.
        if (string.Equals(args.WidgetType, "Workspace", StringComparison.Ordinal))
        {
            return await ExecuteOpenWorkspaceTabAsync(
                context, tool, args, startedAt, stopwatch, correlationLogId, cancellationToken)
                .ConfigureAwait(false);
        }

        // Resolve matter context: explicit matterId arg > ChatInvocationContext.MatterId > synthetic unattached.
        var matterContext = ResolveMatterContext(args, context);

        // Build deterministic IDs + timestamps via the seamed providers (Phase 4 Track C).
        var tabId = _guidProvider.NewGuid().ToString("N");
        var createdAtIso = startedAt.ToString("o");

        // Deserialize the LLM-supplied widgetData JSON object into the polymorphic
        // WorkspaceTabWidgetData. System.Text.Json reads the 'kind' discriminator (already
        // validated to match widgetType in ValidateChat).
        WorkspaceTabWidgetData widgetData;
        try
        {
            widgetData = DeserializeWidgetData(args.WidgetDataRawJson, args.WidgetType);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) widgetData deserialization failed for widgetType={WidgetType}",
                correlationLogId, args.WidgetType);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"widgetData payload could not be deserialized for widgetType '{args.WidgetType}': {ex.Message}",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        var sessionId = context.ChatSessionId.ToString("N");

        var tab = new WorkspaceTab
        {
            Id = tabId,
            WidgetType = args.WidgetType,
            WidgetData = widgetData,
            SessionId = sessionId,
            TenantId = context.TenantId,
            // Pillar 9 visibility: agent-created tabs default to visible so the LLM sees its
            // own artifact in the next turn's system prompt. The user can toggle visibility
            // via the tab strip (FR-35 acceptance criterion).
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                // ADR-015 binding: deterministic creator id only — NEVER user message text.
                CreatedBy = $"agent:{context.ChatSessionId:N}",
                CreatedAt = createdAtIso
            },
            MatterContext = matterContext,
            IsPinned = false,
            // Agent-created tabs default to non-editable (the chat reply is the authoring
            // surface). The user pins or "Convert to editable" toggles this in the UI per
            // Pillar 6b user affordances.
            CanEdit = false,
            LastUserEditAt = null,
            CreatedAt = createdAtIso,
            UpdatedAt = createdAtIso
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _workspaceStateService.UpsertTabAsync(
                context.TenantId,
                sessionId,
                tab,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "SendWorkspaceArtifactHandler ({Correlation}) dispatch complete tabId={TabId} widgetType={WidgetType} in {Duration}ms",
                correlationLogId, tabId, args.WidgetType, stopwatch.ElapsedMilliseconds);

            // G-P3 UAT round-2 R2-D honesty (2026-07-07): the four legacy artifact
            // variants persist to workspace state (R6 Pillar 6a), but the post-046
            // SpaarkeAi client has no polling channel and no registry keys for these
            // widget types — the artifact is NOT visible on the live tab strip. The
            // summary must not claim a visible tab (the round-2 fabrications built on
            // exactly this kind of over-claiming result text).
            var summaryText =
                $"Workspace artifact '{args.Title}' recorded to workspace state (widgetType={args.WidgetType}, " +
                $"tabId={tabId}). NOTE: this artifact is NOT visible as a tab in the current workspace UI — " +
                "do not tell the user a tab was opened. To open a live workspace tab use widgetType 'Workspace' " +
                "with a layoutName.";

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new SendWorkspaceArtifactPayload
                {
                    TabId = tabId,
                    WidgetType = args.WidgetType,
                    SessionId = sessionId,
                    MatterId = matterContext.MatterId,
                    VisibleToAssistant = tab.VisibleToAssistant,
                    Title = args.Title
                },
                summary: summaryText,
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
                "SendWorkspaceArtifactHandler ({Correlation}) cancelled tabId={TabId}",
                correlationLogId, tabId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Workspace artifact dispatch was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (InvalidOperationException ex)
        {
            // Tenant/session-mismatch guard from WorkspaceStateService.UpsertTabAsync — surface
            // as ValidationFailed because the error is structural / arg-derived, not a system
            // outage.
            stopwatch.Stop();
            _logger.LogError(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) tenant/session mismatch on upsert tabId={TabId}: {Reason}",
                correlationLogId, tabId, ex.Message);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Workspace artifact dispatch failed validation: {ex.Message}",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) dispatch failed tabId={TabId}: {ErrorType}",
                correlationLogId, tabId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Workspace artifact dispatch failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Workspace layout tab — live open (G-P3 UAT round-2 R2-D, 2026-07-07)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a named workspace layout as a LIVE tab in the SpaarkeAi workspace pane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chain</b>: layout resolved by name/id against <see cref="WorkspaceLayoutService"/>
    /// (hard-coded + Dataverse-system + user layouts, under the calling user) → a
    /// <c>workspace_open_tab</c> frame is emitted on the context_event SSE channel via
    /// <see cref="ChatInvocationContext.SseWriter"/> → the SpaarkeAi
    /// <c>useContextEventBridge</c> dispatches PaneEventBus
    /// <c>workspace.widget_load {widgetType:'workspace', widgetData:{layoutId, layoutName}}</c>
    /// — byte-compatible with the Workspaces-menu path, so <c>WorkspaceTabManager.addTab</c>
    /// materializes + auto-activates the tab and the client's own tab persistence
    /// (PATCH /sessions/{id}/tabs) makes it survive reload.
    /// </para>
    /// <para>
    /// <b>Fail-honest</b>: without a live SSE writer the tab CANNOT open (the post-046
    /// client has no polling channel) — the handler returns an error instead of a
    /// success the model would relay as "opened". An unknown layout name returns the
    /// available layout names so the model can ground a retry or an honest answer.
    /// </para>
    /// </remarks>
    private async Task<ToolResult> ExecuteOpenWorkspaceTabAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        ParsedArgs args,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        ToolExecutionMetadata Timed() => new() { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() };

        if (context.SseWriter is null)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "SendWorkspaceArtifactHandler ({Correlation}) workspace tab requested but no SSE writer on this surface — refused (fail-honest).",
                correlationLogId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "The workspace pane cannot be reached from this surface (no live chat stream) — the tab was NOT opened. Tell the user honestly.",
                ToolErrorCodes.ValidationFailed,
                Timed());
        }

        // Parse layoutName / layoutId (+ optional R4-2 documentId pre-seed pointer) out of
        // the validated widgetData payload.
        string? layoutName = null;
        Guid layoutId = Guid.Empty;
        Guid documentId = Guid.Empty;
        try
        {
            using var doc = JsonDocument.Parse(args.WidgetDataRawJson);
            if (doc.RootElement.TryGetProperty("layoutName", out var lnProp) && lnProp.ValueKind == JsonValueKind.String)
            {
                layoutName = lnProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("layoutId", out var liProp) && liProp.ValueKind == JsonValueKind.String)
            {
                Guid.TryParse(liProp.GetString(), out layoutId);
            }
            if (doc.RootElement.TryGetProperty("documentId", out var diProp) && diProp.ValueKind == JsonValueKind.String)
            {
                Guid.TryParse(diProp.GetString(), out documentId);
            }
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"widgetData payload could not be parsed for widgetType 'Workspace': {ex.Message}",
                ToolErrorCodes.ValidationFailed,
                Timed());
        }

        if (layoutId == Guid.Empty && string.IsNullOrWhiteSpace(layoutName))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "widgetData for widgetType 'Workspace' must include 'layoutName' (e.g. 'Compose') or a 'layoutId' GUID.",
                ToolErrorCodes.ValidationFailed,
                Timed());
        }

        try
        {
            var layouts = await _layoutService
                .GetLayoutsAsync(context.UserId ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);

            var layout = layoutId != Guid.Empty
                ? layouts.FirstOrDefault(l => l.Id == layoutId)
                : layouts.FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));

            if (layout is null)
            {
                stopwatch.Stop();
                var available = string.Join(", ", layouts.Select(l => $"'{l.Name}'"));
                _logger.LogInformation(
                    "SendWorkspaceArtifactHandler ({Correlation}) workspace layout not found — requestedById={ById} availableCount={Count}",
                    correlationLogId, layoutId != Guid.Empty, layouts.Count);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"No workspace layout named '{layoutName ?? layoutId.ToString("D")}' exists for this user. " +
                    $"Available layouts: {available}. Tell the user honestly or retry with one of these names.",
                    ToolErrorCodes.ValidationFailed,
                    Timed());
            }

            var tabId = _guidProvider.NewGuid().ToString("N");
            var displayName = string.IsNullOrWhiteSpace(args.Title) ? layout.Name : args.Title;

            // ── R4-2 (2026-07-07): Compose document pre-seed ─────────────────────────
            // A widgetData.documentId (sprk_document GUID) resolves — under the calling
            // user's OBO token — to its SPE drive-item pointer, which rides the
            // widgetData as a `compose` seed. The SpaarkeAi host threads it into
            // ComposeLaunchContext so the Compose section opens LOADED instead of empty.
            // Fail-honest: an unresolvable document (no stored file, no access, transport
            // error) still opens the tab — EMPTY — and the summary says so explicitly.
            ComposeSeed? composeSeed = null;
            string? preSeedNote = null;
            if (documentId != Guid.Empty)
            {
                (composeSeed, preSeedNote) = await ResolveComposeSeedAsync(documentId, correlationLogId, cancellationToken)
                    .ConfigureAwait(false);
            }

            var widgetDataJson = composeSeed is null
                ? JsonSerializer.Serialize(new
                {
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name
                })
                : JsonSerializer.Serialize(new
                {
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name,
                    compose = new
                    {
                        sprkDocumentId = composeSeed.SprkDocumentId,
                        speDriveItemId = composeSeed.SpeDriveItemId,
                        speDriveId = composeSeed.SpeDriveId,
                        fileName = composeSeed.FileName
                    }
                });

            // Presentation frame — rides the existing context_event channel (ADR-030
            // additive; old clients ignore the unknown discriminant). Identifiers +
            // tab title only (ADR-015).
            await context.SseWriter(
                new Api.Ai.ChatSseEvent(
                    "context_event",
                    null,
                    new Telemetry.ContextSseEventDto
                    {
                        ContextEventType = WorkspaceOpenTabEventType,
                        ContextTimestamp = _timeProvider.GetUtcNow().ToString("o"),
                        ContextWidgetType = ClientWorkspaceWidgetKey,
                        ContextDisplayName = displayName,
                        ContextTabId = tabId,
                        ContextWidgetDataJson = widgetDataJson
                    }),
                cancellationToken).ConfigureAwait(false);

            // ── D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037) ───────────────
            // The SSE frame carries `tabId` as the frame id the client MUST echo back via
            // POST /api/ai/chat/sessions/{sessionId}/ack once it has actually materialized
            // the tab (WorkspacePane's widget_load handler → manager.addTab). The tool
            // result completes ONLY on that ack — an SSE write succeeding proves the frame
            // left the server, NOT that the client rendered it (the R2-D fabrication gap:
            // "I opened the tab" with no backing client event). On timeout we fail HONESTLY
            // — never fall through to the success summary below.
            var ackOutcome = await _ackCoordinator
                .WaitForAckAsync(
                    context.ChatSessionId.ToString("N"),
                    tabId,
                    WorkspaceTabAckTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            if (ackOutcome == UiActionAckOutcome.TimedOut)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "SendWorkspaceArtifactHandler ({Correlation}) workspace tab ack TIMEOUT layoutId={LayoutId} tabId={TabId} after {Duration}ms — no client acknowledgment referencing the frame id (FR-A1-08 honest-failure path).",
                    correlationLogId, layout.Id, tabId, stopwatch.ElapsedMilliseconds);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"Could not confirm the '{layout.Name}' workspace tab actually opened in the client " +
                    "(no acknowledgment received within the timeout) — it may NOT be visible. Tell the " +
                    "user honestly instead of claiming the tab opened; suggest they try again or check " +
                    "the workspace pane manually.",
                    ToolErrorCodes.Timeout,
                    Timed());
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "SendWorkspaceArtifactHandler ({Correlation}) workspace tab opened + ACKED layoutId={LayoutId} tabId={TabId} in {Duration}ms",
                correlationLogId, layout.Id, tabId, stopwatch.ElapsedMilliseconds);

            var summaryText = $"Opened the '{layout.Name}' workspace as a tab in the workspace pane" +
                              (string.Equals(displayName, layout.Name, StringComparison.Ordinal)
                                  ? "."
                                  : $" (tab title '{displayName}').");
            if (composeSeed is not null)
            {
                summaryText += " The tab is pre-seeded with the requested document — it opens loaded, not empty.";
            }
            else if (preSeedNote is not null)
            {
                // Honest partial success: the tab opened, the pre-seed did not.
                summaryText += $" {preSeedNote}";
            }

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tabId,
                    widgetType = "Workspace",
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name,
                    preSeededDocumentId = composeSeed?.SprkDocumentId
                },
                summary: summaryText,
                confidence: 1.0,
                execution: Timed());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Workspace tab open was cancelled.",
                ToolErrorCodes.Cancelled,
                Timed());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) workspace tab open failed: {ErrorType}",
                correlationLogId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Opening the workspace tab failed unexpectedly — the tab was NOT opened.",
                ToolErrorCodes.InternalError,
                Timed());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Compose pre-seed resolution (G-P3 UAT round-4 R4-2, 2026-07-07)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The document pointer threaded to the client for the Compose pre-seed. Field names
    /// mirror the ribbon/modal launch params (<c>launch-resolver.ts</c>
    /// <c>SpaarkeAiComposeLaunchParams</c>) so the client maps them 1:1 into
    /// <c>ComposeLaunchContext</c>.
    /// </summary>
    internal sealed record ComposeSeed(
        string SprkDocumentId,
        string SpeDriveItemId,
        string? SpeDriveId,
        string? FileName);

    /// <summary>
    /// Resolves a <c>sprk_document</c> GUID to its SPE drive-item pointer under the
    /// calling user's OBO token. Returns <c>(seed, null)</c> on success or
    /// <c>(null, honestNote)</c> when the pre-seed is impossible — the note is appended
    /// to the tool summary so the model relays the truth ("opened empty because …").
    /// </summary>
    /// <remarks>
    /// REALITY CHECK (round-2/round-4 investigation): session-UPLOADED chat files carry
    /// NO SPE pointer (<c>ChatSessionFile</c> = extracted text + AI-Search chunk ids only),
    /// so they can never pre-seed Compose — only promoted/real <c>sprk_document</c> rows
    /// (with <c>sprk_graphitemid</c>) can. This method is the sprk_document leg; the
    /// session-file leg is refused honestly at validation/description level.
    /// </remarks>
    private async Task<(ComposeSeed? Seed, string? HonestNote)> ResolveComposeSeedAsync(
        Guid documentId,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _dataverse.GetAsync(
                $"sprk_documents({documentId:D})?$select=sprk_documentid,sprk_documentname,sprk_filename," +
                "sprk_graphdriveid,sprk_graphitemid",
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess || response.Body is not { } body || body.ValueKind != JsonValueKind.Object)
            {
                _logger.LogInformation(
                    "SendWorkspaceArtifactHandler ({Correlation}) compose pre-seed document unresolvable — documentId={DocumentId} errorCode={ErrorCode}",
                    correlationLogId, documentId, response.ErrorCode);
                return (null,
                    "The requested document could not be loaded for pre-seeding (not found or not accessible " +
                    "to you) — the Compose tab opened EMPTY; tell the user honestly and offer Browse/Search inside Compose.");
            }

            static string? Str(JsonElement el, string prop) =>
                el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            var driveItemId = Str(body, "sprk_graphitemid");
            if (string.IsNullOrWhiteSpace(driveItemId))
            {
                _logger.LogInformation(
                    "SendWorkspaceArtifactHandler ({Correlation}) compose pre-seed document has no SPE file — documentId={DocumentId}",
                    correlationLogId, documentId);
                return (null,
                    "The document record exists but has NO stored file (no SharePoint Embedded item), so Compose " +
                    "cannot load it — the tab opened EMPTY; tell the user honestly.");
            }

            var seed = new ComposeSeed(
                SprkDocumentId: documentId.ToString("D"),
                SpeDriveItemId: driveItemId,
                SpeDriveId: Str(body, "sprk_graphdriveid"),
                FileName: Str(body, "sprk_documentname") ?? Str(body, "sprk_filename"));

            // ADR-015: identifiers only — never the document/file name.
            _logger.LogInformation(
                "SendWorkspaceArtifactHandler ({Correlation}) compose pre-seed resolved — documentId={DocumentId} hasDriveId={HasDriveId}",
                correlationLogId, documentId, seed.SpeDriveId is not null);

            return (seed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) compose pre-seed resolution failed — documentId={DocumentId} errorType={ErrorType}",
                correlationLogId, documentId, ex.GetType().Name);
            return (null,
                "Resolving the document for pre-seeding failed unexpectedly — the Compose tab opened EMPTY; " +
                "tell the user honestly.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse <c>widgetType</c> / <c>title</c> / <c>widgetData</c> / <c>matterId</c> /
    /// <c>matterName</c> from the chat tool-call arguments JSON. ValidateChat catches most
    /// errors but we re-project defensively for the dispatch path so test fixtures that skip
    /// ValidateChat still get clear diagnostics.
    /// </summary>
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

            if (!root.TryGetProperty("widgetType", out var wtProp) ||
                wtProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(wtProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'widgetType' string field.";
                return false;
            }
            var widgetType = wtProp.GetString()!;
            if (!SupportedWidgetTypes.Contains(widgetType))
            {
                error = $"'widgetType' must be one of: {string.Join(", ", SupportedWidgetTypes)}. Received: '{widgetType}'.";
                return false;
            }

            if (!root.TryGetProperty("title", out var titleProp) ||
                titleProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'title' string field.";
                return false;
            }
            var title = titleProp.GetString()!;

            if (!root.TryGetProperty("widgetData", out var dataProp) ||
                dataProp.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must include a 'widgetData' JSON object.";
                return false;
            }
            var widgetDataRawJson = dataProp.GetRawText();

            Guid? matterId = null;
            if (root.TryGetProperty("matterId", out var matterIdProp) &&
                matterIdProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(matterIdProp.GetString()))
            {
                if (!Guid.TryParse(matterIdProp.GetString(), out var parsedMatterId))
                {
                    error = "'matterId' must be a valid GUID when provided.";
                    return false;
                }
                matterId = parsedMatterId;
            }

            string? matterName = null;
            if (root.TryGetProperty("matterName", out var matterNameProp) &&
                matterNameProp.ValueKind == JsonValueKind.String)
            {
                matterName = matterNameProp.GetString();
            }

            args = new ParsedArgs
            {
                WidgetType = widgetType,
                Title = title,
                WidgetDataRawJson = widgetDataRawJson,
                MatterId = matterId,
                MatterName = matterName
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

    /// <summary>
    /// Deserialize the LLM-supplied widget data JSON into the polymorphic
    /// <see cref="WorkspaceTabWidgetData"/>. The 'kind' discriminator was validated to match
    /// <paramref name="widgetType"/> by <see cref="ValidateChat"/>; System.Text.Json's
    /// polymorphism metadata layer reads 'kind' and instantiates the concrete subtype.
    /// </summary>
    private static WorkspaceTabWidgetData DeserializeWidgetData(string widgetDataRawJson, string widgetType)
    {
        var deserialized = JsonSerializer.Deserialize<WorkspaceTabWidgetData>(widgetDataRawJson, WidgetDataJsonOptions);
        if (deserialized is null)
        {
            throw new JsonException(
                $"widgetData deserialized to null for widgetType '{widgetType}' — the LLM-supplied payload " +
                "is structurally invalid.");
        }
        return deserialized;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Matter resolution
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the workspace tab's matter context with the following precedence:
    /// (1) explicit <paramref name="args"/>.MatterId from the tool call,
    /// (2) <see cref="ChatInvocationContext.MatterId"/> when the chat session is matter-scoped,
    /// (3) synthetic 'unattached' sentinel ("00000000-0000-0000-0000-000000000000" + "Unattached")
    /// when neither is present — the user can later pin the tab to a real matter via
    /// <see cref="IWorkspaceStateService.PinTabAsync"/>.
    /// </summary>
    private static WorkspaceTabMatterContext ResolveMatterContext(ParsedArgs args, ChatInvocationContext context)
    {
        if (args.MatterId is not null)
        {
            return new WorkspaceTabMatterContext
            {
                MatterId = args.MatterId.Value.ToString("D"),
                MatterName = args.MatterName ?? args.MatterId.Value.ToString("D")
            };
        }

        if (context.MatterId is not null)
        {
            return new WorkspaceTabMatterContext
            {
                MatterId = context.MatterId.Value.ToString("D"),
                MatterName = args.MatterName ?? context.MatterId.Value.ToString("D")
            };
        }

        return new WorkspaceTabMatterContext
        {
            MatterId = Guid.Empty.ToString("D"),
            MatterName = "Unattached"
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsed chat-call arguments. All non-optional fields are guaranteed non-null/non-empty
    /// by <see cref="TryParseArgs"/>.
    /// </summary>
    private readonly record struct ParsedArgs
    {
        public string WidgetType { get; init; }
        public string Title { get; init; }
        public string WidgetDataRawJson { get; init; }
        public Guid? MatterId { get; init; }
        public string? MatterName { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// deterministic tab identity + widget discriminator + matter scope + title — suitable for
    /// the LLM to render or summarize in its next assistant turn.
    /// ADR-015 binding: NEVER carries the widgetData body content.
    /// </summary>
    public sealed class SendWorkspaceArtifactPayload
    {
        /// <summary>Deterministic tab identifier (matches <see cref="WorkspaceTab.Id"/>).</summary>
        [JsonPropertyName("tabId")]
        public required string TabId { get; init; }

        /// <summary>Widget-type discriminator that was applied (echo of the tool argument).</summary>
        [JsonPropertyName("widgetType")]
        public required string WidgetType { get; init; }

        /// <summary>Chat session id the tab was attached to (32-char no-dash format).</summary>
        [JsonPropertyName("sessionId")]
        public required string SessionId { get; init; }

        /// <summary>Matter id the tab was anchored to (D-format GUID or empty-GUID for unattached).</summary>
        [JsonPropertyName("matterId")]
        public required string MatterId { get; init; }

        /// <summary>Whether the tab is initially visible to the assistant (always true for agent-created tabs).</summary>
        [JsonPropertyName("visibleToAssistant")]
        public required bool VisibleToAssistant { get; init; }

        /// <summary>Display title that was applied to the tab.</summary>
        [JsonPropertyName("title")]
        public required string Title { get; init; }
    }
}
