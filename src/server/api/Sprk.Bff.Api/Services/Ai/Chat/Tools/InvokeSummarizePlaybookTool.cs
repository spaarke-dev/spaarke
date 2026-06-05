using System.ComponentModel;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool wrapper that exposes the R5 chat-session Summarize playbook as a single
/// LLM-invokable function (<see cref="InvokeSummarizePlaybookAsync"/>) on the
/// <see cref="SprkChatAgent"/>.
///
/// <para>
/// The tool DELEGATES — does not duplicate — to <see cref="SessionSummarizeOrchestrator"/>
/// (task 012 / D2-03). Both the direct endpoint path (task 014 / D2-04) and this agent-tool
/// path converge on the SAME convergence method
/// <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/> — per spec FR-01 +
/// FR-08 + SC-08 (one internal Summarize implementation, two invocation paths). The tool
/// supplies <see cref="SummarizeInvocationPath.AgentTool"/> as the path discriminator so the
/// orchestrator records the correct telemetry dimension.
/// </para>
///
/// <para>
/// <b>Tool-routing scope (NFR-12 / UR-01 mitigation)</b>: the tool description text is the
/// load-bearing artifact for correct LLM tool-routing between this Summarize tool and the
/// upcoming <c>insights.query</c> tool (task 024 / D2-14). The description scopes routing
/// to <i>session-uploaded files</i> and explicitly differentiates from the <c>insights.query</c>
/// tool, which is for analytical questions about matter/project/invoice entities.
/// </para>
///
/// <para>
/// <b>Reuse statement (per R5 CLAUDE.md §3.1)</b>: this tool class is a THIN delegating
/// adapter. It composes EXISTING primitives — the orchestrator from task 012 (delegated
/// to verbatim) + the agent's existing <c>sseWriter</c> + the <see cref="ChatSession"/>
/// context flowed through from <see cref="SprkChatAgentFactory"/>. NO parallel chat agent,
/// NO parallel orchestrator, NO parallel SSE envelope, NO new feature flag, NO new DI
/// top-level lines. Registered inside <see cref="SprkChatAgentFactory.ResolveTools"/> via
/// <see cref="AIFunctionFactory.Create"/> — mirrors the canonical
/// <see cref="AnalysisExecutionTools"/> pattern (ADR-013 + ADR-010 + AIPL-053).
/// </para>
///
/// <para>
/// <b>ADR-028 token discipline</b>: this tool does NOT capture or snapshot bearer tokens.
/// The orchestrator from task 012 runs entirely on the agent's existing scoped service
/// resolution; fresh tokens are obtained per call by downstream services (RAG, OpenAI,
/// Dataverse) through their own scoped clients. There is no <c>HttpContext</c> dependency
/// because the Summarize flow operates over pre-indexed session-files content (task 003)
/// rather than SPE OBO-authenticated file download (per task 012 Why-New-Class
/// Justification item 4).
/// </para>
///
/// <para>
/// <b>ADR-014 tenant/session isolation</b>: <see cref="_sessionId"/> and <see cref="_tenantId"/>
/// flow from <see cref="SprkChatAgentFactory.CreateAgentAsync"/> through to every orchestrator
/// invocation; the orchestrator enforces ADR-014 at the RAG layer (tenantId + sessionId
/// filters BOTH set per task 012 evidence). The tool itself is a passive forwarder of these
/// scoping keys.
/// </para>
///
/// <para>
/// Instantiated by <see cref="SprkChatAgentFactory"/> per session, not DI-registered (per
/// ADR-010 + AIPL-053 — same pattern as <see cref="AnalysisExecutionTools"/>,
/// <see cref="DocumentSearchTools"/>, <see cref="KnowledgeRetrievalTools"/>,
/// <see cref="WorkingDocumentTools"/>).
/// </para>
/// </summary>
public sealed class InvokeSummarizePlaybookTool
{
    /// <summary>The AIFunction name exposed to the LLM tool-schema. Lower-snake-case to match the
    /// project's emerging convention for capability-aligned tools (e.g., <c>verify_citations</c>,
    /// <c>insights.query</c>) and to keep the tool name discoverable in the capability
    /// manifest's <c>ToolNames</c> allow-list.</summary>
    public const string ToolName = "invoke_summarize_playbook";

    /// <summary>
    /// Description string passed to <see cref="AIFunctionFactory.Create"/>. This is the
    /// single LOAD-BEARING artifact for LLM tool routing (NFR-12 / UR-01 mitigation). It
    /// MUST scope routing to chat-session uploaded files AND MUST explicitly differentiate
    /// from the upcoming <c>insights.query</c> tool (task 024) so the LLM picks the right
    /// tool for each natural-language Summarize-vs-Insights prompt.
    /// </summary>
    public const string ToolDescription =
        "Summarize the user's currently-uploaded chat session files (or a specific subset by " +
        "fileIds) using the Summarize playbook. Returns a streamed structured summary with a " +
        "TL;DR and per-file highlights. Use for ANY natural-language request to summarize, " +
        "recap, distill, TL;DR, or produce an executive overview of the files attached to the " +
        "current chat session (e.g., 'summarize the attached document', 'TL;DR these files', " +
        "'give me a bullet recap of what I uploaded'). " +
        "Do NOT use this tool for analytical questions about a matter, project, or invoice " +
        "entity — those are handled by insights.query.";

    private readonly SessionSummarizeOrchestrator _orchestrator;
    private readonly string _tenantId;
    private readonly string _sessionId;
    private readonly string? _correlationId;
    private readonly Func<ChatSseEvent, CancellationToken, Task>? _sseWriter;
    private readonly ILogger<InvokeSummarizePlaybookTool> _logger;

    /// <summary>
    /// Constructs the agent-tool wrapper.
    /// </summary>
    /// <param name="orchestrator">
    /// The R5 task 012 convergence orchestrator. Required. Tool DELEGATES every Summarize
    /// invocation to <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>
    /// (per spec FR-01 + FR-08 + SC-08 convergence contract).
    /// </param>
    /// <param name="tenantId">Tenant ID (ADR-014). Required.</param>
    /// <param name="sessionId">
    /// Chat session ID (ADR-014; task 004 manifest key). Required. Flows into every
    /// orchestrator call as the session scoping key.
    /// </param>
    /// <param name="correlationId">
    /// Optional correlation ID propagated to the orchestrator for distributed tracing (NFR-17).
    /// </param>
    /// <param name="sseWriter">
    /// Optional out-of-band SSE writer for streaming events. When provided, the tool forwards
    /// every <see cref="AnalysisChunk"/> yielded by the orchestrator as a structured SSE
    /// event (so the workspace widget's streaming UI receives FieldDelta tokens in real time
    /// — per spec FR-04 + task 016 additive event types). When null, the orchestrator's
    /// chunks are still iterated (drained) but no SSE is emitted; only the final TL;DR
    /// string is returned to the LLM's conversation context. Null is a valid no-op state.
    /// </param>
    /// <param name="logger">Logger.</param>
    public InvokeSummarizePlaybookTool(
        SessionSummarizeOrchestrator orchestrator,
        string tenantId,
        string sessionId,
        string? correlationId,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter,
        ILogger<InvokeSummarizePlaybookTool> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _tenantId = !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new ArgumentException("Tenant ID required (ADR-014).", nameof(tenantId));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : throw new ArgumentException("Session ID required (ADR-014).", nameof(sessionId));
        _correlationId = correlationId;
        _sseWriter = sseWriter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// LLM-invokable Summarize tool function. Delegates to the R5 task 012 convergence
    /// orchestrator with <see cref="SummarizeInvocationPath.AgentTool"/> as the path
    /// discriminator (so telemetry records this as the agent-tool path, not the direct
    /// endpoint path).
    /// </summary>
    /// <param name="fileIds">
    /// Optional subset of files (by their session-files FileId) to summarize. When null
    /// or empty, defaults to ALL files in the current chat session manifest (per FR-08
    /// and the orchestrator's <see cref="SummarizeSessionFilesRequest.FileIds"/> contract).
    /// Cap: 20 (NFR-02); the orchestrator throws if exceeded.
    /// </param>
    /// <param name="style">
    /// Optional natural-language style hint passed through to the system prompt
    /// (e.g., <c>executive</c>, <c>detailed</c>, <c>bullet</c>, <c>tldr</c>). When null
    /// or whitespace, defaults to the playbook's default style.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A final TL;DR text suitable for the agent's conversation context. Per task 012,
    /// the orchestrator emits SSE chunks for the streaming UI; this tool returns the
    /// terminal summary text (or a single-line completion notice if no terminal text
    /// content is available) so the LLM has a record of the action in its history.
    /// </returns>
    [Description(ToolDescription)]
    public async Task<string> InvokeSummarizePlaybookAsync(
        [Description("Optional list of FileIds (from ChatSession.UploadedFiles) to summarize. " +
                     "Defaults to ALL session-uploaded files when null or empty. Maximum 20.")]
        string[]? fileIds = null,
        [Description("Optional style hint for the summary (e.g., 'executive', 'detailed', " +
                     "'bullet', 'tldr'). Defaults to the playbook's default style when null.")]
        string? style = null,
        CancellationToken cancellationToken = default)
    {
        // Build the convergence request. Note: nullable string[] → IReadOnlyList<string>?
        // — an empty array (or null) maps to "summarize all session files" per the
        // orchestrator's defaulting contract (task 012).
        var request = new SummarizeSessionFilesRequest(
            TenantId: _tenantId,
            SessionId: _sessionId,
            FileIds: (fileIds is { Length: > 0 }) ? fileIds : null,
            StyleHint: string.IsNullOrWhiteSpace(style) ? null : style,
            Path: SummarizeInvocationPath.AgentTool,
            CorrelationId: _correlationId);

        _logger.LogInformation(
            "InvokeSummarizePlaybookTool: dispatching to orchestrator (tenant={TenantId} " +
            "session={SessionId} fileCount={FileCount} style={Style})",
            _tenantId, _sessionId, fileIds?.Length ?? 0, style ?? "(default)");

        // Drain the orchestrator's stream. Forward every chunk to the SSE writer (if
        // provided) so the streaming UI sees FieldDelta tokens in real time; capture
        // a final TL;DR text for the LLM's conversation history.
        string? finalSummary = null;
        var fallbackContent = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in _orchestrator
                .SummarizeSessionFilesAsync(request, cancellationToken)
                .ConfigureAwait(false))
            {
                // Forward to SSE writer if available (streaming UI consumes these via FR-04
                // + task 016 additive workspace.field_delta / workspace.streaming_complete
                // event types — see ChatEndpoints.CreateDocumentStreamSseWriter pattern).
                if (_sseWriter is not null)
                {
                    try
                    {
                        await _sseWriter(
                            new ChatSseEvent(chunk.Type, chunk.Content, Data: chunk),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Client disconnected — propagate to terminate the iterator.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // SSE writer failure is non-fatal — log and continue draining the
                        // orchestrator stream so telemetry still gets a terminal status.
                        _logger.LogWarning(ex,
                            "InvokeSummarizePlaybookTool: SSE writer threw — continuing without SSE.");
                    }
                }

                // Capture final-summary text for the LLM's conversation context.
                if (chunk.Done && chunk.Result is not null)
                {
                    // Prefer the structured Result.Tldr (task 005 / task 006 contract) when
                    // present. The DocumentAnalysisResult shape is set by the action's
                    // sprk_outputschemajson (task 010 — has a "tldr" required field).
                    finalSummary = chunk.Summary ?? chunk.Content;
                }
                else if (chunk.Type == "error" && !string.IsNullOrEmpty(chunk.Error))
                {
                    // Decline / mid-stream failure paths set Type="error". Return the error
                    // message so the LLM can communicate the issue to the user.
                    return $"Summarize was unable to complete: {chunk.Error}";
                }
                else if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fallbackContent.Append(chunk.Content);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "InvokeSummarizePlaybookTool: cancelled (tenant={TenantId} session={SessionId})",
                _tenantId, _sessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "InvokeSummarizePlaybookTool: orchestrator threw (tenant={TenantId} session={SessionId})",
                _tenantId, _sessionId);
            return $"Summarize encountered an unexpected error: {ex.Message}";
        }

        // Prefer the terminal Summary; otherwise fall back to accumulated content; otherwise
        // a deterministic single-line completion notice so the LLM has a record.
        if (!string.IsNullOrWhiteSpace(finalSummary))
        {
            return finalSummary;
        }
        if (fallbackContent.Length > 0)
        {
            return fallbackContent.ToString();
        }
        return "Summarize completed (no summary text produced — see the streaming output panel " +
               "for the rendered summary).";
    }

    /// <summary>
    /// Returns the single <see cref="AIFunction"/> for this tool, registered under
    /// <see cref="ToolName"/> with <see cref="ToolDescription"/>. Called by
    /// <see cref="SprkChatAgentFactory.ResolveTools"/>.
    /// </summary>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            InvokeSummarizePlaybookAsync,
            name: ToolName,
            description: ToolDescription);
    }
}
