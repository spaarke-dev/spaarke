using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
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
/// <c>send_workspace_artifact(widgetType, title, widgetData)</c> where <c>widgetType</c> is
/// <c>Workspace</c> — it opens a named workspace LAYOUT (e.g. "Compose") as a live tab in the
/// SpaarkeAi workspace pane by emitting a <c>workspace_open_tab</c> frame on the existing
/// context_event SSE channel, then gating success on a client ack (D-F3 UI-action
/// truthfulness). The legacy artifact variants (Summary / DocumentViewer / Dashboard / Table)
/// were RETIRED by AIR2-075 — this handler no longer writes <c>IWorkspaceStateService</c> tab
/// state at all.
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
/// <strong>Capability gate</strong>: <c>sprk_requiredcapability = null</c>. Opening a workspace
/// layout tab is a default user affordance — every chat session, standalone or playbook-bound,
/// can open a tab. The single <c>Workspace</c> widgetType is the authorization surface — there
/// is no "free text widget" escape hatch.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.
/// Dependencies (<see cref="IGuidProvider"/>, <see cref="TimeProvider"/>,
/// <c>WorkspaceLayoutService</c>, <c>IDataverseUserClient</c>, <c>IUiActionAckCoordinator</c>)
/// are already registered.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; the Workspace
/// layout-tab open uses the SSE writer + <c>WorkspaceLayoutService</c> + ack coordinator —
/// BFF-internal chat-session plumbing, NOT an AI-internal seam behind a PublicContracts
/// facade.</item>
/// <item><strong>ADR-014</strong>: <c>TenantId</c> is required on the
/// <see cref="ChatInvocationContext"/>; the Compose pre-seed Dataverse read runs under the
/// calling user's OBO token so cross-tenant access is structurally impossible.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + decision + tabId + widgetType
/// + duration ONLY. NEVER the LLM-supplied <c>widgetData</c> content; NEVER the user's chat
/// message. Layout/document identifiers are logged as flags or GUIDs, never file names.</item>
/// <item><strong>ADR-016</strong>: no separate rate limiter — the chat session's existing
/// concurrency slot (per-session lock in <c>ChatSessionManager</c>) caps the open rate.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation.</item>
/// <item><strong>ADR-030</strong>: the <c>workspace_open_tab</c> frame is additive on the
/// existing context_event SSE channel — no new SSE channel is introduced.</item>
/// </list>
/// </remarks>
public sealed class SendWorkspaceArtifactHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(SendWorkspaceArtifactHandler);

    /// <summary>
    /// Closed enum of widget-type discriminators accepted by this handler. <c>Workspace</c>
    /// (G-P3 UAT round-2 R2-D, 2026-07-07) opens a NAMED workspace LAYOUT (e.g. "Compose") as a
    /// live tab in the SpaarkeAi workspace pane — the same mechanism the Workspaces menu drives
    /// client-side (<c>widget_load {widgetType:'workspace', widgetData:{layoutId, layoutName}}</c>).
    /// The legacy artifact variants (Summary / DocumentViewer / Dashboard / Table) were retired
    /// by AIR2-075. Any value outside this set is rejected by <see cref="ValidateChat"/> with a
    /// clear error.
    /// </summary>
    internal static readonly HashSet<string> SupportedWidgetTypes = new(StringComparer.Ordinal)
    {
        "Workspace"
    };

    /// <summary>
    /// The client workspace-widget registry key for a layout tab. MUST match the
    /// registration in <c>register-workspace-widgets.ts</c> ("workspace" renders the
    /// embedded LegalWorkspaceApp with <c>{layoutId, layoutName}</c>).
    /// </summary>
    internal const string ClientWorkspaceWidgetKey = "workspace";

    /// <summary>
    /// The client workspace-widget registry key for the first-class Compose DIRECT
    /// widget (spaarkeai-compose-r2). MUST match <c>registerComposeWidget.ts</c>
    /// (<c>widgetType: 'compose'</c> → <c>ComposeDirectWidget</c> → <c>ComposeWorkspace</c>).
    /// A COMPOSE mount emits THIS key (not <see cref="ClientWorkspaceWidgetKey"/>) so
    /// ComposeWorkspace mounts UNCONDITIONALLY on the client — never
    /// LegalWorkspaceApp/dashboard, and with no layout-row lookup. The
    /// <c>compose:{…}</c> seed rides <c>widgetData</c> unchanged. Every OTHER layout
    /// (Daily Briefing, Documents, …) keeps <see cref="ClientWorkspaceWidgetKey"/>.
    /// </summary>
    internal const string ClientComposeWidgetKey = "compose";

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

    /// <summary>
    /// Default workspace layout used when the tool call targets a Compose open without an
    /// explicit layout (R5 / UAT defects 6/7). The handler is chat-only and Compose is its
    /// flagship layout; defaulting a missing layout to this is strictly better than the prior
    /// hard-error that forced the model to synthesize a layout id. This is a PARAMETER default
    /// (not an intent-detection mechanism — ADR-039 §MUST NOT): it never selects a capability
    /// or routes dispatch, it just fills a missing tool argument.
    /// </summary>
    internal const string DefaultComposeLayoutName = "Compose";

    private readonly IGuidProvider _guidProvider;
    private readonly TimeProvider _timeProvider;
    private readonly WorkspaceLayoutService _layoutService;
    private readonly IDataverseUserClient _dataverse;
    private readonly IUiActionAckCoordinator _ackCoordinator;
    private readonly ChatSessionManager _sessionManager;
    private readonly ILogger<SendWorkspaceArtifactHandler> _logger;

    public SendWorkspaceArtifactHandler(
        IGuidProvider guidProvider,
        TimeProvider timeProvider,
        WorkspaceLayoutService layoutService,
        IDataverseUserClient dataverse,
        IUiActionAckCoordinator ackCoordinator,
        ChatSessionManager sessionManager,
        ILogger<SendWorkspaceArtifactHandler> logger)
    {
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
        // task 113 (UAT defect 5): reads the session-scoped ActiveDocument to resolve the Compose
        // mount target when the LLM supplies no explicit current pointer — so "edit in Compose"
        // mounts the just-uploaded file, not a stale stored one. AI-internal→AI-internal injection
        // (both under Services/Ai) — ADR-013 compliant; ADR-010 concrete-injection.
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Send Workspace Artifact",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Open a tab in the user's workspace pane. PREFERRED USE — open a named workspace LAYOUT as a live tab: widgetType 'Workspace' with widgetData {""kind"":""Workspace"",""layoutName"":""<layout name>""}. Use layoutName 'Compose' when the user asks to open the Compose editor or to start composing/drafting a document in the workspace; other layouts (e.g. 'Daily Briefing', 'Documents') open the same way. When opening the Compose layout ABOUT A SPECIFIC DOCUMENT, add widgetData.documentId (the sprk_document GUID, e.g. from a search result path tables/sprk_document/records/{guid}) — Compose then opens WITH that document loaded. To open a file the user UPLOADED into this chat, add widgetData.sessionFileId (the uploaded file's id from the session's attachments) instead of documentId — Compose opens with that file mounted as a transient working draft (no document record is created until the user saves). Use documentId for stored sprk_document records and sessionFileId for session uploads; set at most one. If the requested layout does not exist, the tool result lists the available layout names — relay them honestly. The tab opens immediately in the workspace pane and the tool result confirms it; only claim a tab was opened when this tool result says so.",
        Version: "1.1.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "widgetType",
                "Closed enum: 'Workspace' (open a named workspace layout as a live tab).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "title",
                "Display title for the workspace tab (shown on the tab strip and in tooltips). Should be short and matter-relevant.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "widgetData",
                "JSON object carrying the payload. MUST include a 'kind' field equal to widgetType ('Workspace'). " +
                "Workspace: layoutName (the workspace layout's display name, e.g. 'Compose') or layoutId (GUID); " +
                "optional documentId (sprk_document GUID) to pre-seed the Compose layout with a STORED document, " +
                "OR sessionFileId (a chat-session uploaded file id) to mount an UPLOADED file as a transient " +
                "working draft (create-on-save) — set at most one.",
                ToolParameterType.Object,
                Required: true)
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

            // Workspace layout tabs (R2-D): the payload MAY name the layout to open.
            if (string.Equals(widgetType, "Workspace", StringComparison.Ordinal))
            {
                // R5 (task 113 / UAT defects 6/7): layoutName/layoutId are NO LONGER required.
                // When neither is supplied the execute path defaults to the 'Compose' layout
                // (ExecuteOpenWorkspaceTabAsync), so a literal-following model never has to
                // synthesize a layout id for "open/edit in compose". This is a parameter default,
                // not a new intent-detection mechanism (ADR-039). The optional documentId /
                // sessionFileId checks below still apply.

                // R4-2 (2026-07-07): optional STORED-document pre-seed pointer — a real
                // sprk_document GUID resolved server-side to its SPE drive-item.
                if (dataProp.TryGetProperty("documentId", out var docIdProp) &&
                    docIdProp.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(docIdProp.GetString()) &&
                    !Guid.TryParse(docIdProp.GetString(), out _))
                {
                    return ToolValidationResult.Failure(
                        "'widgetData.documentId' must be a sprk_document GUID when provided. " +
                        "To open a file the user uploaded into this chat, use 'widgetData.sessionFileId' instead.");
                }

                // FR-03 (task 012): optional UPLOADED-file transient mount pointer. A
                // session-uploaded file id whose retained bytes Compose mounts as a transient
                // working draft (create-on-save). Must be a non-empty string; documentId and
                // sessionFileId are mutually exclusive (documentId = stored, sessionFileId = upload).
                var hasSessionFileId = dataProp.TryGetProperty("sessionFileId", out var sfIdProp) &&
                                       sfIdProp.ValueKind == JsonValueKind.String &&
                                       !string.IsNullOrWhiteSpace(sfIdProp.GetString());
                var hasDocumentId = dataProp.TryGetProperty("documentId", out var docId2) &&
                                    docId2.ValueKind == JsonValueKind.String &&
                                    !string.IsNullOrWhiteSpace(docId2.GetString());
                if (hasSessionFileId && hasDocumentId)
                {
                    return ToolValidationResult.Failure(
                        "'widgetData' cannot set both 'documentId' (a stored sprk_document) and " +
                        "'sessionFileId' (an uploaded chat file) — set at most one.");
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

        // ADR-015: log handler + IDs + widgetType + title LENGTH. NEVER the widgetData body; NEVER the title text.
        _logger.LogInformation(
            "SendWorkspaceArtifactHandler ({Correlation}) dispatch start widgetType={WidgetType} tenantId={TenantId} titleLen={TitleLen}",
            correlationLogId, args.WidgetType, context.TenantId, args.Title.Length);

        // ── G-P3 UAT round-2 R2-D (2026-07-07): Workspace layout tab — live open ─────
        // Opens a named workspace layout (e.g. "Compose") as a tab in the SpaarkeAi
        // workspace pane by emitting a `workspace_open_tab` frame on the existing
        // context_event SSE channel; the client bridge dispatches the SAME
        // PaneEventBus `workspace.widget_load` event the Workspaces menu uses.
        //
        // AIR2-075: this is the ONLY remaining path. The Summary / DocumentViewer /
        // Dashboard / Table legacy artifact variants were retired with the orphaned
        // IWorkspaceStateService write path; TryParseArgs already gated widgetType to the
        // closed {"Workspace"} set, so dispatch straight to the live-open path.
        return await ExecuteOpenWorkspaceTabAsync(
            context, tool, args, startedAt, stopwatch, correlationLogId, cancellationToken)
            .ConfigureAwait(false);
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
        string? sessionFileId = null;
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
            // FR-03 (task 012): an uploaded session-file id — mounts transiently (create-on-save).
            if (doc.RootElement.TryGetProperty("sessionFileId", out var sfProp) && sfProp.ValueKind == JsonValueKind.String)
            {
                var raw = sfProp.GetString();
                sessionFileId = string.IsNullOrWhiteSpace(raw) ? null : raw;
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

        // ── DEF-08 Part A: resolve a recent FULL-DOCUMENT compose draft to SEED the tab ───────
        // When the model supplied NO explicit pointer, first look for a recent full-document
        // compose output (a compose-draft-document SessionOutput) in the session ledger. If one
        // exists, seed the opened tab with its ledgerRef (the client re-materializes the document
        // from the ledger — ADR-040 render-follows-store; the SSE frame carries the identifier,
        // never content — ADR-015). This is the fix for the blank-Compose-tab defect: a chat
        // "write a letter" → "open as a document" flow now lands the draft IN the editor.
        //
        // Precedence: an explicit documentId/sessionFileId from the model always wins; a fresh
        // full-document draft wins over the ActiveDocument mount (it is the more specific fresh
        // intent). The draft resolution is gated to FULL-DOCUMENT compose outputs (body_html), so
        // an "edit in Compose" of an uploaded file — which produces NO such output — still falls
        // through to the ActiveDocument path (task 113 preserved). Deterministic session-state
        // read (not intent detection — ADR-039).
        ComposeDraftSeed? draftSeed = null;
        if (documentId == Guid.Empty && sessionFileId is null)
        {
            draftSeed = await ResolveComposeDraftSeedAsync(context, correlationLogId, cancellationToken).ConfigureAwait(false);
        }

        // ── task 113 (UAT defect 5): resolve the Compose mount target from the session-scoped
        // ACTIVE-DOCUMENT when the LLM supplied NO explicit current pointer AND no full-document
        // draft seed applies ─────────────────────────────────────────────────────────────────
        // "edit in Compose" after a fresh upload must mount THAT upload, not a stale stored doc.
        // An explicit documentId/sessionFileId from the model always wins (the stored-document
        // "open this specific doc" override) — we only consult the active document when BOTH are
        // absent AND no draft seed resolved. Deterministic session-state read (not intent
        // detection — ADR-039).
        if (documentId == Guid.Empty && sessionFileId is null && draftSeed is null)
        {
            var active = await ResolveActiveDocumentAsync(context, cancellationToken).ConfigureAwait(false);
            if (active is not null)
            {
                if (!string.IsNullOrWhiteSpace(active.SessionFileId))
                {
                    sessionFileId = active.SessionFileId;
                    _logger.LogInformation(
                        "SendWorkspaceArtifactHandler ({Correlation}) resolved Compose mount from active document — source={Source} kind=session-upload",
                        correlationLogId, active.Source);
                }
                else if (!string.IsNullOrWhiteSpace(active.SprkDocumentId) &&
                         Guid.TryParse(active.SprkDocumentId, out var activeDocGuid))
                {
                    documentId = activeDocGuid;
                    _logger.LogInformation(
                        "SendWorkspaceArtifactHandler ({Correlation}) resolved Compose mount from active document — source={Source} kind=stored",
                        correlationLogId, active.Source);
                }
            }
        }

        // R5 (task 113 / UAT defects 6/7): default a missing layout to 'Compose' rather than
        // erroring, so a literal-following model never has to synthesize a layout id for
        // "open/edit in compose". Parameter default, not intent detection (ADR-039).
        if (layoutId == Guid.Empty && string.IsNullOrWhiteSpace(layoutName))
        {
            layoutName = DefaultComposeLayoutName;
            _logger.LogInformation(
                "SendWorkspaceArtifactHandler ({Correlation}) no layout supplied — defaulting to '{Layout}' (R5 / UAT defects 6/7).",
                correlationLogId, DefaultComposeLayoutName);
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

            // spaarkeai-compose-r2: a COMPOSE mount is dispatched to the client as the
            // first-class DIRECT 'compose' widget (ClientComposeWidgetKey) so
            // ComposeWorkspace mounts unconditionally — never LegalWorkspaceApp/dashboard,
            // and with no client-side layout-row lookup or race. This holds for EVERY
            // compose case below, including the no-seed default fallback (an EMPTY Compose
            // editor, never a non-Compose layout). Any OTHER layout (Daily Briefing,
            // Documents, …) keeps the LAYOUT door (ClientWorkspaceWidgetKey). The
            // widgetData seed payload is unchanged either way.
            var clientWidgetKey = string.Equals(layout.Name, DefaultComposeLayoutName, StringComparison.OrdinalIgnoreCase)
                ? ClientComposeWidgetKey
                : ClientWorkspaceWidgetKey;

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

            // FR-03 (task 012): a session-uploaded file mounts transiently. No SPE pointer
            // resolution is needed — the bytes are served from the retained-upload cache by
            // POST /api/compose/upload keyed by (chat session id, sessionFileId). The client
            // fetches them into the editor's docxBytes seam; no sprk_document until first Save.
            var hasUploadMount = composeSeed is null && sessionFileId is not null;
            var chatSessionIdForSeed = context.ChatSessionId.ToString("D");

            string widgetDataJson;
            if (composeSeed is not null)
            {
                widgetDataJson = JsonSerializer.Serialize(new
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
            }
            else if (hasUploadMount)
            {
                widgetDataJson = JsonSerializer.Serialize(new
                {
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name,
                    compose = new
                    {
                        upload = new
                        {
                            sessionId = chatSessionIdForSeed,
                            sessionFileId
                        }
                    }
                });
            }
            else if (draftSeed is not null)
            {
                // DEF-08 Part A: seed the tab with the ledger REFERENCE to the drafted document
                // (ADR-040 render-follows-store + ADR-015 identifier-only on the wire). The client
                // reads GET /api/ai/chat/sessions/{sessionId}/compose-outputs, materializes the
                // full-document body (body_html) from the ledger entry at ledgerRef, and mounts it
                // as an editable transient draft (create-on-save). No content rides the SSE frame.
                widgetDataJson = JsonSerializer.Serialize(new
                {
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name,
                    compose = new
                    {
                        draft = new
                        {
                            ledgerRef = draftSeed.LedgerRef,
                            sessionId = chatSessionIdForSeed
                        }
                    }
                });
            }
            else
            {
                widgetDataJson = JsonSerializer.Serialize(new
                {
                    layoutId = layout.Id.ToString("D"),
                    layoutName = layout.Name
                });
            }

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
                        ContextWidgetType = clientWidgetKey,
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
            else if (hasUploadMount)
            {
                summaryText += " The tab is mounting the uploaded file as a transient working draft — it opens " +
                               "with the file loaded; no document record is created until the user saves.";
            }
            else if (draftSeed is not null)
            {
                summaryText += " The tab is pre-seeded with the drafted document — it opens with the draft loaded " +
                               "for editing, not empty; no document record is created until the user saves.";
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
                    preSeededDocumentId = composeSeed?.SprkDocumentId,
                    mountedSessionFileId = hasUploadMount ? sessionFileId : null,
                    seededDraftLedgerRef = draftSeed?.LedgerRef
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
    // Active-document resolution (task 113 / UAT defect 5)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the chat session for this invocation and returns its session-scoped
    /// <see cref="ActiveDocumentIdentity"/> (or null when the session/identity is absent).
    /// Probes the session id in "N" form first (the canonical <c>ChatSessionManager</c> key —
    /// <c>Guid.ToString("N")</c>) then "D", mirroring the Compose upload path's tolerance for
    /// either spelling. Fail-soft: any lookup error degrades to null (the handler then behaves
    /// exactly as it did before — the model's explicit pointer or an honest empty open).
    /// </summary>
    private async Task<ActiveDocumentIdentity?> ResolveActiveDocumentAsync(
        ChatInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            return null;
        }

        try
        {
            foreach (var sessionIdForm in new[]
                     {
                         context.ChatSessionId.ToString("N"),
                         context.ChatSessionId.ToString("D"),
                     })
            {
                var session = await _sessionManager
                    .GetSessionAsync(context.TenantId, sessionIdForm, cancellationToken)
                    .ConfigureAwait(false);
                if (session?.ActiveDocument is { } active)
                {
                    return active;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft (ADR-015: identifiers only) — never let an active-document lookup
            // failure break the tab-open; the model's explicit pointer / empty open still works.
            _logger.LogWarning(ex,
                "SendWorkspaceArtifactHandler active-document resolution failed for session={Session} — degrading to no active document.",
                context.ChatSessionId);
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Compose full-document draft seed resolution (DEF-08 Part A)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ledger reference threaded to the client for a full-document Compose draft seed. Carries
    /// ONLY the addressable ledger key (<c>{bindingId}@t{n}</c>) — an identifier, never the drafted
    /// content (ADR-015). The client resolves the body from
    /// <c>GET /api/ai/chat/sessions/{sessionId}/compose-outputs</c> (ADR-040 render-follows-store).
    /// </summary>
    internal sealed record ComposeDraftSeed(string LedgerRef);

    /// <summary>
    /// Resolves the CURRENT (highest-turn) FULL-DOCUMENT <c>compose</c> output for this chat
    /// session — the output a <c>compose-draft-document</c> capability wrote to the session ledger
    /// (DEF-08 Part A). Returns its addressable key as a <see cref="ComposeDraftSeed"/>, or null when
    /// the session holds no such output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Full-document discriminator</b>: a <c>compose</c> ledger entry is treated as a
    /// full-document draft ONLY when its opaque payload carries a non-empty <c>body_html</c> string
    /// (the <c>compose-draft-document</c> output schema field). This deliberately EXCLUDES a
    /// <c>compose-draft-alternative</c> EDIT payload (<c>target_text</c>/<c>new_text</c>) — an edit
    /// is not a full document and must never seed a whole tab. (Edits also route to the DOCUMENT
    /// session, not this chat session, per DEF-09 — this is a defense-in-depth second gate.)
    /// </para>
    /// <para>
    /// <b>Session resolution</b>: mirrors <see cref="ResolveActiveDocumentAsync"/> — probes the
    /// <c>ChatSessionManager</c> key in "N" then "D" form. Fail-soft: any lookup error degrades to
    /// null (the tab still opens, empty — the handler behaves exactly as before DEF-08).
    /// </para>
    /// <para>
    /// <b>ADR-015</b>: logs the ledger KEY + turn (identifiers) only — never the drafted body.
    /// </para>
    /// </remarks>
    private async Task<ComposeDraftSeed?> ResolveComposeDraftSeedAsync(
        ChatInvocationContext context,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            return null;
        }

        try
        {
            foreach (var sessionIdForm in new[]
                     {
                         context.ChatSessionId.ToString("N"),
                         context.ChatSessionId.ToString("D"),
                     })
            {
                var session = await _sessionManager
                    .GetSessionAsync(context.TenantId, sessionIdForm, cancellationToken)
                    .ConfigureAwait(false);
                if (session?.Outputs is not { Count: > 0 } outputs)
                {
                    continue;
                }

                Models.Ai.Chat.SessionOutput? current = null;
                foreach (var output in outputs)
                {
                    if (!string.Equals(output.Disposition, ComposeDisposition.DispositionValue, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (Models.Ai.Chat.SessionLedger.IsTruncationMarker(output.Payload))
                    {
                        continue; // a truncated draft cannot be materialized — never seed it
                    }
                    if (output.Payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    // Full-document discriminator: non-empty body_html (compose-draft-document).
                    if (!output.Payload.TryGetProperty("body_html", out var bodyHtml) ||
                        bodyHtml.ValueKind != JsonValueKind.String ||
                        string.IsNullOrEmpty(bodyHtml.GetString()))
                    {
                        continue;
                    }
                    if (current is null || output.Turn > current.Turn)
                    {
                        current = output;
                    }
                }

                if (current is not null)
                {
                    // ADR-015: ledger key + turn are identifiers — never the drafted body.
                    _logger.LogInformation(
                        "SendWorkspaceArtifactHandler ({Correlation}) resolved compose full-document draft seed — ledgerRef={LedgerRef} turn={Turn}",
                        correlationLogId, current.Key, current.Turn);
                    return new ComposeDraftSeed(current.Key);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft (ADR-015: identifiers only) — a draft-seed lookup failure never breaks the
            // tab-open; the tab still opens (empty) exactly as it did before DEF-08.
            _logger.LogWarning(ex,
                "SendWorkspaceArtifactHandler ({Correlation}) compose draft-seed resolution failed — degrading to no seed.",
                correlationLogId);
        }

        return null;
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
    /// Parse <c>widgetType</c> / <c>title</c> / <c>widgetData</c> from the chat tool-call
    /// arguments JSON. ValidateChat catches most errors but we re-project defensively for the
    /// dispatch path so test fixtures that skip ValidateChat still get clear diagnostics.
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

            args = new ParsedArgs
            {
                WidgetType = widgetType,
                Title = title,
                WidgetDataRawJson = widgetDataRawJson
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
    }
}
