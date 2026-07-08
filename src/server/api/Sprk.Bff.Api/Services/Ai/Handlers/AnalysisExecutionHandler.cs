using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Ai;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Extensions.AI type.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-only typed <see cref="IToolHandler"/> providing analysis re-execution and analysis
/// refinement (spaarke-ai-architecture-redesign-r1 task 036 / FR-P2-07 — migration of the
/// last live legacy chat-tool group into the closed-catalog handler framework).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Method dispatch (TextRefinementHandler pattern)</strong>: two separate
/// <c>sprk_analysistool</c> rows point at this handler with a <c>method</c> discriminator in
/// <c>sprk_configuration</c>:
/// </para>
/// <list type="bullet">
/// <item><c>method = "rerun"</c> — row <c>SYS-Analysis Rerun</c>, tool id <c>analysis.rerun</c>,
/// declared <c>side_effect_class = read</c> since the 2026-07-06 operator ruling (G-P2 UAT
/// fix wave): an explicit re-run executes immediately without the confirmation gate because
/// it regenerates the session's OWN analysis output (client undo preserved) — it never
/// mutates tenant records. Originally declared write by task 036. Gating stays
/// declaration-driven — never a tool-name list (ADR-039); <c>dataverse.*</c> writes stay gated.</item>
/// <item><c>method = "refine"</c> — row <c>SYS-Analysis Refine</c>, tool id
/// <c>analysis.refine</c>, declared <c>side_effect_class = read</c> (fetches the current
/// analysis output, transforms it with a focused inner LLM call, returns text to the
/// conversation — no persistence).</item>
/// </list>
/// <para>
/// Two rows (not one row + method arg) because each method carries its OWN declared
/// side-effect class and the gate fires on the row's declaration (the classes happen to
/// coincide at read since the 2026-07-06 ruling, but they remain independently declarable).
/// </para>
/// <para>
/// <strong>Session context</strong>: <c>rerun</c> reads <see cref="ChatInvocationContext.PlaybookId"/>
/// + <see cref="ChatInvocationContext.DocumentId"/> (set by the catalog projector's context
/// factory) and emits per-stage <c>progress</c> + completion <c>document_replace</c> SSE events
/// via <see cref="ChatInvocationContext.SseWriter"/> (ADR-033 side-channel principle).
/// <c>refine</c> reads <see cref="ChatInvocationContext.AnalysisId"/>.
/// </para>
/// <para>
/// <strong>Identity note (audit F-1; task-044 re-trace 2026-07-06)</strong>:
/// <c>rerun</c> delegates to <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/>,
/// whose engine persists results under application identity. Task 044 deleted the three
/// F-1 legacy legs (the generic playbook dispatch, analysis-query, and working-document
/// handlers); this is the LAST LLM-reachable app-only engine leg, retained because the
/// re-run capability is live product surface and the engine is frozen (OBO-migrating the
/// node executors is out of scope per spec §Out of Scope). The reach stays EXPLICIT in the
/// catalog (<c>permission_scope = app-only-engine</c>); blast radius is bounded to the
/// session's OWN playbook + document (both BFF-resolved from session context — the LLM
/// supplies only optional instructions, never target ids). Escalated for a standing ruling
/// in the task-044 report; candidate for the FR-P4-01 Track-B completion audit.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>: ADR-010 auto-discovered (zero manual DI); ADR-013 lives in
/// <c>Services/Ai/Handlers/</c>; ADR-015 telemetry emits handler + method + IDs + outcome +
/// duration only (never instructions, analysis content, or output bodies); ADR-016 inner LLM
/// call routes through the injected <see cref="IChatClient"/>; ADR-029 BCL-only.
/// </para>
/// </remarks>
public sealed class AnalysisExecutionHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(AnalysisExecutionHandler);

    private const string MethodRerun = "rerun";
    private const string MethodRefine = "refine";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        MethodRerun, MethodRefine
    };

    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IChatClient _chatClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEngineOutputLedgerAdapter _engineOutputLedger;
    private readonly ILogger<AnalysisExecutionHandler> _logger;

    public AnalysisExecutionHandler(
        IAnalysisOrchestrationService analysisService,
        IChatClient chatClient,
        IHttpContextAccessor httpContextAccessor,
        IEngineOutputLedgerAdapter engineOutputLedger,
        ILogger<AnalysisExecutionHandler> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _engineOutputLedger = engineOutputLedger ?? throw new ArgumentNullException(nameof(engineOutputLedger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Analysis Execution",
        Description: "Re-executes the session's analysis playbook from scratch ('rerun' — streams " +
                     "per-stage progress and replaces the document pane content on completion) or " +
                     "refines the current analysis output per a follow-up instruction ('refine' — " +
                     "focused single-turn LLM transform of the existing output). Method selected by " +
                     "the 'method' discriminator in sprk_configuration.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("additionalInstructions", "Optional additional instructions or focus areas for the re-run (rerun method only).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("refinementInstruction", "The refinement instruction to apply to the current analysis (refine method only).", ToolParameterType.String, Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only: the legacy tool group was registered exclusively for chat sessions; the
    /// playbook engine must never re-enter itself via a playbook-orchestrated tool.
    /// </remarks>
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "AnalysisExecutionHandler supports chat invocation only — playbook orchestration " +
            "must not re-enter the analysis engine through a tool node.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "AnalysisExecutionHandler does not support playbook invocation. Use chat invocation only.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        string method;
        try
        {
            method = ResolveMethod(tool.Configuration);
        }
        catch (InvalidOperationException ex)
        {
            return ToolValidationResult.Failure(ex.Message);
        }

        if (string.Equals(method, MethodRefine, StringComparison.OrdinalIgnoreCase))
        {
            // refine requires a non-empty instruction from the LLM args.
            var instruction = ReadStringArg(context.ToolArgumentsJson, "refinementInstruction");
            if (string.IsNullOrWhiteSpace(instruction))
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'refinementInstruction' string field for method 'refine'.");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var method = ResolveMethod(tool.Configuration);

            // ADR-015: handler + method + IDs only — never instructions or content.
            _logger.LogInformation(
                "AnalysisExecutionHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var result = string.Equals(method, MethodRerun, StringComparison.OrdinalIgnoreCase)
                ? await RerunAnalysisAsync(context, tool, startedAt, cancellationToken).ConfigureAwait(false)
                : await RefineAnalysisAsync(context, tool, startedAt, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // ADR-015: outcome + duration only.
            _logger.LogInformation(
                "AnalysisExecutionHandler complete: method='{Method}' session={ChatSessionId} decision={DecisionId} success={Success} in {Duration}ms",
                method, context.ChatSessionId, context.DecisionId, result.Success, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AnalysisExecutionHandler cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Analysis execution was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-015: exception TYPE only in the structured log.
            _logger.LogError(
                ex,
                "AnalysisExecutionHandler failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Analysis execution failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Method: rerun — re-execute the session's playbook (ported from the legacy
    // chat-tool group's RerunAnalysisAsync; behavior preserved).
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> RerunAnalysisAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        // Validate required session context.
        if (context.PlaybookId is null || context.PlaybookId == Guid.Empty)
        {
            return RerunError(tool, startedAt,
                "Re-analysis failed: no playbook context available for the current session. " +
                "The session must be associated with a playbook to re-run analysis.");
        }

        if (string.IsNullOrWhiteSpace(context.DocumentId) ||
            !Guid.TryParse(context.DocumentId, out var documentGuid))
        {
            return RerunError(tool, startedAt,
                "Re-analysis failed: no active document available for the current session. " +
                "A document must be associated with the session to re-run analysis.");
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return RerunError(tool, startedAt,
                "Re-analysis failed: HTTP context not available. " +
                "This tool requires an active HTTP request to authenticate file downloads.");
        }

        var additionalInstructions = ReadStringArg(context.ToolArgumentsJson, "additionalInstructions") ?? string.Empty;

        await EmitProgressAsync(context, 0, "Starting re-analysis...", cancellationToken).ConfigureAwait(false);

        var request = new PlaybookExecuteRequest
        {
            PlaybookId = context.PlaybookId.Value,
            DocumentIds = [documentGuid],
            AdditionalContext = string.IsNullOrWhiteSpace(additionalInstructions)
                ? null
                : additionalInstructions
        };

        await EmitProgressAsync(context, 10, "Loading playbook and resolving scopes...", cancellationToken).ConfigureAwait(false);

        var outputBuilder = new StringBuilder();
        var analysisId = Guid.Empty;
        var toolCount = 0;

        try
        {
            await foreach (var chunk in _analysisService.ExecutePlaybookAsync(
                request, httpContext, cancellationToken).ConfigureAwait(false))
            {
                switch (chunk.Type)
                {
                    case "metadata":
                        analysisId = chunk.AnalysisId ?? Guid.Empty;
                        await EmitProgressAsync(context, 25, "Document extracted, executing analysis tools...", cancellationToken).ConfigureAwait(false);
                        break;

                    case "chunk":
                        if (!string.IsNullOrEmpty(chunk.Content))
                        {
                            // Detect tool execution markers for per-stage progress.
                            if (chunk.Content.StartsWith("[Executing:", StringComparison.Ordinal))
                            {
                                toolCount++;
                                var toolProgress = Math.Min(85, 25 + (toolCount * 15));
                                var toolName = chunk.Content.TrimStart('[').TrimEnd(']', '\n').Replace("Executing: ", "");
                                await EmitProgressAsync(context, toolProgress, $"Running tool: {toolName}...", cancellationToken).ConfigureAwait(false);
                            }

                            outputBuilder.Append(chunk.Content);
                        }
                        break;

                    case "done":
                        await EmitProgressAsync(context, 90, "Analysis complete, preparing results...", cancellationToken).ConfigureAwait(false);
                        break;

                    case "error":
                        await EmitProgressAsync(context, 100, $"Error: {chunk.Error}", cancellationToken).ConfigureAwait(false);
                        return RerunError(tool, startedAt, $"Re-analysis failed: {chunk.Error}");
                }
            }
        }
        catch (KeyNotFoundException ex)
        {
            return RerunError(tool, startedAt, $"Re-analysis failed: {ex.Message}");
        }

        // ── E-2 engine-output→ledger adapter (ADR-040 / FR-P1-05; re-homed here by
        // FR-P3-05 task 044 from the deleted generic playbook-dispatch leg) ─────────────
        // At this exact point the frozen engine's aggregated run output is in hand and
        // NOTHING has rendered (document_replace below + the LLM's assistant turn are the
        // renders). The adapter writes the addressable SessionOutput ledger entry through
        // the task-021 IOutputRouter write path FIRST — storage precedes rendering. A
        // ledger-write failure propagates to ExecuteChatAsync's generic catch and fails
        // the tool call: unstored output is never rendered (ADR-040 D2/D8). A null entry
        // means no chat session was resolvable (record-context runs join the ledger at
        // P3 FR-P3-08).
        var analysisOutput = outputBuilder.ToString();
        string? ledgerKey = null;
        if (!string.IsNullOrWhiteSpace(analysisOutput))
        {
            var ledgerEntry = await _engineOutputLedger
                .RecordAsync(
                    context.TenantId,
                    context.ChatSessionId,
                    context.PlaybookId.Value,
                    new EngineRunOutput
                    {
                        RunId = analysisId,
                        TextContent = analysisOutput,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            ledgerKey = ledgerEntry?.Key;
        }

        // Emit the document_replace SSE event with the full analysis output.
        if (!string.IsNullOrWhiteSpace(analysisOutput))
        {
            var replaceData = new ChatSseDocumentReplaceData(
                Html: analysisOutput,
                Metadata: new ChatSseDocumentReplaceMetadata(
                    PlaybookId: context.PlaybookId.Value.ToString(),
                    Timestamp: DateTimeOffset.UtcNow.ToString("o")));

            await EmitSseEventAsync(
                context,
                new ChatSseEvent("document_replace", null, Data: replaceData),
                cancellationToken).ConfigureAwait(false);
        }

        await EmitProgressAsync(context, 100, "Re-analysis completed successfully.", cancellationToken).ConfigureAwait(false);

        var instructionSummary = string.IsNullOrWhiteSpace(additionalInstructions)
            ? "using the original playbook configuration"
            : $"with additional instructions: \"{additionalInstructions}\"";

        var confirmation =
            $"Analysis re-run completed successfully {instructionSummary}. " +
            $"Analysis ID: {analysisId}. " +
            $"The updated analysis has been sent to the document panel. " +
            $"The previous version has been preserved in the undo stack.";

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: new AnalysisExecutionResult { Method = MethodRerun, Text = confirmation, AnalysisId = analysisId, LedgerKey = ledgerKey },
            summary: confirmation,
            confidence: 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            });
    }

    private ToolResult RerunError(AnalysisTool tool, DateTimeOffset startedAt, string message) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            message,
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // Method: refine — refine the current analysis output via a focused inner LLM
    // call (ported from the legacy chat-tool group's RefineAnalysisAsync).
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> RefineAnalysisAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var refinementInstruction = ReadStringArg(context.ToolArgumentsJson, "refinementInstruction");
        if (string.IsNullOrWhiteSpace(refinementInstruction))
        {
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Input 'refinementInstruction' is required for method 'refine'.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var analysisContent = await FetchAnalysisContentAsync(context.AnalysisId, cancellationToken).ConfigureAwait(false);
        if (analysisContent is null)
        {
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Refinement failed: no analysis output available. " +
                "The user may need to run an analysis first before refining it.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // Focused single-turn prompt — no agent context/history (token-efficiency by design).
        var messages = new List<AiChatMessage>
        {
            new AiChatMessage(ChatRole.System,
                "You are an expert analyst refining an existing analysis document. " +
                "Apply the user's refinement instruction to the analysis output below. " +
                "Produce the complete refined analysis — preserve structure and content that " +
                "is not affected by the instruction. " +
                "Output ONLY the refined analysis — no explanation, preamble, or meta-commentary.\n\n" +
                "Current analysis output:\n" + analysisContent),
            new AiChatMessage(ChatRole.User, refinementInstruction)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        var refined = response.Text ?? string.Empty;

        return ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: new AnalysisExecutionResult { Method = MethodRefine, Text = refined },
            summary: "Refined the current analysis output per the follow-up instruction.",
            confidence: string.IsNullOrEmpty(refined) ? 0.0 : 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 1
            });
    }

    private async Task<string?> FetchAnalysisContentAsync(Guid? analysisId, CancellationToken cancellationToken)
    {
        if (analysisId is null || analysisId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var analysisDetail = await _analysisService.GetAnalysisAsync(analysisId.Value, cancellationToken).ConfigureAwait(false);
            return analysisDetail.WorkingDocument ?? analysisDetail.FinalOutput;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SSE side-channel helpers (ADR-033 principle via ChatInvocationContext.SseWriter)
    // ─────────────────────────────────────────────────────────────────────────────

    private static Task EmitProgressAsync(
        ChatInvocationContext context,
        int percent,
        string message,
        CancellationToken cancellationToken) =>
        EmitSseEventAsync(
            context,
            new ChatSseEvent("progress", null, Data: new ChatSseProgressData(percent, message)),
            cancellationToken);

    private static async Task EmitSseEventAsync(
        ChatInvocationContext context,
        ChatSseEvent evt,
        CancellationToken cancellationToken)
    {
        if (context.SseWriter is null)
        {
            return;
        }

        try
        {
            await context.SseWriter(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — silently ignore (parity with legacy behavior).
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration + argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            throw new InvalidOperationException(
                "Configuration JSON is required and must include 'method' = 'rerun' | 'refine'.");

        using var doc = JsonDocument.Parse(configurationJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("method", out var methodProp) ||
            methodProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Configuration must include 'method' as a string ('rerun' | 'refine').");
        }

        var method = methodProp.GetString() ?? string.Empty;
        if (!SupportedMethods.Contains(method))
            throw new InvalidOperationException(
                $"Unsupported method '{method}'. Use one of: {string.Join(", ", SupportedMethods)}.");

        return method;
    }

    private static string? ReadStringArg(string? toolArgumentsJson, string field)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(field, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // Tolerated — callers treat null as missing and fail with a clear diagnostic.
        }
        return null;
    }

    /// <summary>
    /// Structured output returned in <see cref="ToolResult.Data"/>: method discriminator +
    /// the produced text (+ analysis id for rerun).
    /// </summary>
    public sealed class AnalysisExecutionResult
    {
        public string Method { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public Guid? AnalysisId { get; set; }

        /// <summary>
        /// Addressable session-ledger key of the stored rerun output
        /// (<c>{bindingId}@t{n}</c> — E-2 adapter, ADR-040; re-homed by FR-P3-05 task 044).
        /// Later turns reference the output by this key. Null for the refine method and
        /// when no chat session was resolvable.
        /// </summary>
        public string? LedgerKey { get; set; }
    }
}
