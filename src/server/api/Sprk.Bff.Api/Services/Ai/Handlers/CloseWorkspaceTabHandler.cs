using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-only typed handler that closes an open workspace tab (R6 Pillar 6b / D-C-07 / FR-35).
/// Implements the <c>close_workspace_tab(tabId)</c> chat tool: the LLM invokes this when the
/// user asks to close a tab in their workspace. The handler dispatches to
/// <see cref="IWorkspaceStateService.CloseTabAsync"/>, which removes the row from the Redis
/// hot tier ONLY — Cosmos durable rows for previously-pinned tabs are preserved
/// (spec FR-32, R6 Q4 hybrid persistence model).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pin-state policy (spec / R6 Pillar 6b)</strong>: this handler is the agent-facing
/// closer of workspace tabs. The pin guard described in the task POML — refusing to close a
/// pinned tab so the user must explicitly unpin first — is enforced by inspecting the tab's
/// <see cref="Models.Workspace.WorkspaceTab.IsPinned"/> flag returned by
/// <see cref="IWorkspaceStateService.GetTabsAsync"/> before invoking
/// <see cref="IWorkspaceStateService.CloseTabAsync"/>. When the tab is pinned, the handler
/// returns a structured refusal payload (NOT a tool error) so the LLM can relay the polite
/// "the tab is pinned; please unpin first" guidance to the user. No mutation occurs.
/// </para>
/// <para>
/// <strong>Idempotent close</strong>: when the tab is not pinned (or not present at all —
/// the Redis hot tier has 24h TTL, so a previously-closed or expired tab is structurally
/// absent), the handler dispatches to <see cref="IWorkspaceStateService.CloseTabAsync"/>,
/// which is itself idempotent (the underlying Redis DEL is a no-op for missing keys). The
/// handler returns <see cref="ToolResult.Ok"/> with a <c>closed</c> outcome regardless —
/// closing a non-existent tab is not an error from the user's perspective and crashing the
/// chat turn for a stale-handle race would be a bad UX (spec NFR-01 conversational primacy).
/// </para>
/// <para>
/// <strong>Service-failure resilience</strong>: if
/// <see cref="IWorkspaceStateService"/> throws (Redis transient unavailability, network
/// glitch, configuration error), the handler returns <see cref="ToolResult.Error"/> with
/// <see cref="ToolErrorCodes.InternalError"/> — the agent surfaces a graceful "could not
/// close the tab right now" response to the user and the broader chat session continues.
/// We do NOT propagate the exception out of the handler.
/// </para>
/// <para>
/// <strong>Tenant isolation (ADR-014 / NFR-16)</strong>: the handler reads
/// <see cref="ToolInvocationContextBase.TenantId"/> from the chat invocation context and
/// passes it to <see cref="IWorkspaceStateService"/>. The service uses tenantId in both the
/// Redis key (<c>workspace:{tenantId}:{sessionId}</c>) and the Cosmos partition key, so a
/// caller cannot close another tenant's tab even by guessing the tabId — the lookup
/// structurally misses the wrong-tenant partition.
/// </para>
/// <para>
/// <strong>Telemetry (ADR-015 binding)</strong>: emits handler name + decision
/// (<c>closed</c> | <c>refused_pinned</c> | <c>internal_error</c>) + timestamp + deterministic
/// IDs (chatSessionId, decisionId, tabId) ONLY. NEVER tab content, widget data, matter name
/// as content, or any user-message text. The pin-state guard logs <c>isPinned</c> as a
/// boolean flag — pin state is a deterministic structural attribute of the tab, not user
/// content.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IWorkspaceStateService"/> only. Resolved via
/// constructor injection (auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/>. Workspace
/// tabs are a chat-session concept; the playbook orchestrator has no workspace concept and
/// the production node executors per NFR-08 do not include a tab-mutation executor.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IWorkspaceStateService"/> (workspace-state plumbing, NOT an AI internal type).
/// CRUD-side code never injects this handler.</item>
/// <item><strong>ADR-014</strong>: tenantId required + propagated to service.</item>
/// <item><strong>ADR-015</strong>: telemetry IDs + decision + duration only; never tab
/// content.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class CloseWorkspaceTabHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(CloseWorkspaceTabHandler);

    /// <summary>Decision discriminator: tab successfully closed (Redis row removed).</summary>
    internal const string DecisionClosed = "closed";

    /// <summary>Decision discriminator: tab is pinned; mutation refused; no state change.</summary>
    internal const string DecisionRefusedPinned = "refused_pinned";

    /// <summary>Decision discriminator: service failed; no state change reported (caller
    /// may retry).</summary>
    internal const string DecisionInternalError = "internal_error";

    private readonly IWorkspaceStateService _workspaceService;
    private readonly ILogger<CloseWorkspaceTabHandler> _logger;

    public CloseWorkspaceTabHandler(
        IWorkspaceStateService workspaceService,
        ILogger<CloseWorkspaceTabHandler> logger)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Close Workspace Tab",
        Description: "Closes an open workspace tab in the user's current session. The 'tabId' " +
                     "argument is the stable id of the tab the user wants to close. Pinned tabs " +
                     "are NOT closable through this tool — the user must explicitly unpin first " +
                     "(the handler returns a refusal response so the agent can relay the guidance). " +
                     "Closing an unpinned tab removes its Redis hot-tier row only — any Cosmos " +
                     "durable rows for previously-pinned data are preserved. The operation is " +
                     "idempotent: closing a non-existent or already-closed tab returns success.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("tabId", "Stable identifier of the workspace tab to close.", ToolParameterType.String, Required: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only. Workspace tabs are a chat-session concept; the playbook orchestrator has
    /// no workspace concept and the production node executors per NFR-08 do not include a
    /// tab-mutation executor.
    /// </remarks>
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        return ToolValidationResult.Success();
    }

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

            if (!doc.RootElement.TryGetProperty("tabId", out var tabIdProp) ||
                tabIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tabIdProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'tabId' string field.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context path. Not supported — <see cref="SupportedInvocationContexts"/> is
    /// <see cref="InvocationContextKind.Chat"/>, so the playbook dispatcher will not route here.
    /// Provided as a defensive guard: if the contract were extended in the future, the
    /// playbook path returns a structured error rather than throwing.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "CloseWorkspaceTabHandler does not support playbook invocation. Use chat invocation only " +
            "(workspace tabs are a chat-session concept — no playbook node executor mutates them).",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        try
        {
            _logger.LogInformation(
                "CloseWorkspaceTabHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var tabId = ParseTabId(context.ToolArgumentsJson);
            if (string.IsNullOrWhiteSpace(tabId))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "tabId is required.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            // sessionId in the workspace service is a string keying the Redis hot tier;
            // chat sessions are identified by a Guid in the chat invocation context.
            // Stringify via the canonical hyphenated form ("D") so workspace keys match
            // what SprkChatAgentFactory.GenerateContextAsync passes to GetTabsAsync at
            // system-prompt build time.
            var tenantId = context.TenantId;
            var sessionId = context.ChatSessionId.ToString("D");

            // Pin guard: fetch the current tab snapshot for this (tenant, session) tuple
            // and refuse mutation when the target tab is pinned. We pull the full list
            // (rather than a per-id fetch) because IWorkspaceStateService does not expose
            // a single-tab read — GetTabsAsync is the canonical reader and the typical
            // hot-tier set is small (one chat session). This is intentional per the
            // service contract (R6 Pillar 6a).
            IReadOnlyList<Models.Workspace.WorkspaceTab>? tabs;
            try
            {
                tabs = await _workspaceService.GetTabsAsync(tenantId, sessionId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CloseWorkspaceTabHandler ({Correlation}) GetTabsAsync failed — decision={Decision}, errorType={ErrorType}",
                    correlationLogId, DecisionInternalError, ex.GetType().Name);

                stopwatch.Stop();
                return BuildInternalErrorResult(tool, ex, startedAt);
            }

            // Find the target tab. Missing tab → idempotent close (the Redis row may have
            // expired or already been removed). We log this as "closed" because from the
            // user's perspective the tab is gone, and CloseTabAsync is a no-op DEL.
            var targetTab = tabs?.FirstOrDefault(t =>
                string.Equals(t.Id, tabId, StringComparison.Ordinal));

            if (targetTab is not null && targetTab.IsPinned)
            {
                // Pin guard — refuse mutation; no state change; structured response so
                // the LLM can relay the polite "please unpin first" guidance to the user.
                _logger.LogInformation(
                    "CloseWorkspaceTabHandler ({Correlation}) decision={Decision}, tabId={TabId}, isPinned=true",
                    correlationLogId, DecisionRefusedPinned, tabId);

                stopwatch.Stop();
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new CloseWorkspaceTabPayload
                    {
                        Decision = DecisionRefusedPinned,
                        TabId = tabId,
                        Message = "The requested tab is pinned. The user must explicitly unpin it before it can be closed."
                    },
                    summary: $"Tab '{tabId}' is pinned; close refused. Ask the user to unpin first.",
                    confidence: 1.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            // Unpinned (or missing) — close is allowed. CloseTabAsync is idempotent so we
            // call it even when targetTab is null (preserves the "agent says close, tab
            // gone" UX without a stale-handle race).
            try
            {
                await _workspaceService.CloseTabAsync(tenantId, sessionId, tabId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CloseWorkspaceTabHandler ({Correlation}) CloseTabAsync failed — decision={Decision}, tabId={TabId}, errorType={ErrorType}",
                    correlationLogId, DecisionInternalError, tabId, ex.GetType().Name);

                stopwatch.Stop();
                return BuildInternalErrorResult(tool, ex, startedAt);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "CloseWorkspaceTabHandler ({Correlation}) decision={Decision}, tabId={TabId}, found={Found} in {Duration}ms",
                correlationLogId, DecisionClosed, tabId, targetTab is not null, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new CloseWorkspaceTabPayload
                {
                    Decision = DecisionClosed,
                    TabId = tabId,
                    Message = targetTab is not null
                        ? "Tab closed."
                        : "Tab already absent from the workspace (idempotent close)."
                },
                summary: targetTab is not null
                    ? $"Tab '{tabId}' closed."
                    : $"Tab '{tabId}' was not present; no action required.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 0
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "CloseWorkspaceTabHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CloseWorkspaceTabHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static string? ParseTabId(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (doc.RootElement.TryGetProperty("tabId", out var tabIdProp)
                && tabIdProp.ValueKind == JsonValueKind.String)
            {
                return tabIdProp.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall through to null
        }

        return null;
    }

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Close workspace tab invocation was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Close workspace tab failed: {ex.GetType().Name}. The tab may not have been closed; the user can retry.",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// decision discriminator + the target tabId + a user-readable message.
    /// ADR-015 binding: NEVER carries tab content, widget data, matter-name as content, or
    /// any user-message text.
    /// </summary>
    public sealed class CloseWorkspaceTabPayload
    {
        /// <summary>Decision discriminator: see <see cref="DecisionClosed"/> / <see cref="DecisionRefusedPinned"/>.</summary>
        [JsonPropertyName("decision")]
        public string Decision { get; set; } = DecisionClosed;

        /// <summary>The tab identifier the request targeted (echoed for caller correlation).</summary>
        [JsonPropertyName("tabId")]
        public string? TabId { get; set; }

        /// <summary>Human-readable status message — relayed by the LLM to the user.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
