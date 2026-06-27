using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Workspace;
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
    /// Closed enum of widget-type discriminators accepted by this handler. Mirrors
    /// <see cref="WorkspaceTabWidgetData"/>'s <c>[JsonDerivedType]</c> annotations + the
    /// TypeScript <c>WorkspaceTab</c> contract. Any value outside this set is rejected by
    /// <see cref="ValidateChat"/> with a clear error.
    /// </summary>
    internal static readonly HashSet<string> SupportedWidgetTypes = new(StringComparer.Ordinal)
    {
        "Summary",
        "DocumentViewer",
        "Dashboard",
        "Table"
    };

    private static readonly JsonSerializerOptions WidgetDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWorkspaceStateService _workspaceStateService;
    private readonly IGuidProvider _guidProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SendWorkspaceArtifactHandler> _logger;

    public SendWorkspaceArtifactHandler(
        IWorkspaceStateService workspaceStateService,
        IGuidProvider guidProvider,
        TimeProvider timeProvider,
        ILogger<SendWorkspaceArtifactHandler> logger)
    {
        _workspaceStateService = workspaceStateService ?? throw new ArgumentNullException(nameof(workspaceStateService));
        _guidProvider = guidProvider ?? throw new ArgumentNullException(nameof(guidProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Send Workspace Artifact",
        Description: "Send a finished artifact (Summary, DocumentViewer, Dashboard, or Table) to the user's " +
                     "workspace pane as a new tab. Use this when you have produced a structured result the user " +
                     "should be able to inspect, share, or pin to a matter — for example: a generated executive " +
                     "summary (Summary), a previewed contract (DocumentViewer), a portfolio overview (Dashboard), " +
                     "or a sortable result set (Table). The artifact materializes on the workspace tab strip; " +
                     "the chat reply remains conversational.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "widgetType",
                "Closed enum: 'Summary' | 'DocumentViewer' | 'Dashboard' | 'Table'. Selects the workspace tab variant.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "title",
                "Display title for the workspace tab (shown on the tab strip and in tooltips). Should be short and matter-relevant.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "widgetData",
                "JSON object carrying the per-variant payload. MUST include a 'kind' field equal to widgetType " +
                "and the variant-specific fields (Summary: body; DocumentViewer: documentId/filename/mimeType/sizeBytes; " +
                "Dashboard: layoutId/dashboardName; Table: rowCount/filteredColumns/selectedRows).",
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

            var summaryText = string.IsNullOrWhiteSpace(args.Title)
                ? $"Workspace tab created (widgetType={args.WidgetType}, tabId={tabId})."
                : $"Workspace tab '{args.Title}' created (widgetType={args.WidgetType}, tabId={tabId}).";

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
