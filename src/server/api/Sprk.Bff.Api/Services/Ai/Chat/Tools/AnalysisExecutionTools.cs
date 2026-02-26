using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). See SprkChatAgent.cs for the same pattern.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing analysis re-execution and refinement capabilities for the SprkChatAgent.
///
/// Exposes two methods:
///   - <see cref="RerunAnalysisAsync"/> — re-runs the analysis pipeline by delegating to
///     <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/>, streaming progress
///     events and emitting a document_replace SSE event with the full new analysis HTML on completion.
///   - <see cref="RefineAnalysisAsync"/> — conversational follow-up that refines the current
///     analysis output using an inner LLM call.
///
/// <see cref="RerunAnalysisAsync"/> calls <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/>
/// with the session's playbook ID and document ID. Progress is emitted as SSE "progress" events
/// via the injected <c>sseWriter</c> delegate. On completion, a "document_replace" SSE event
/// carries the full analysis HTML for the client to replace the document pane content.
/// The previous version MUST be pushed to the undo stack by the client before replacement.
///
/// <see cref="RefineAnalysisAsync"/> performs an inner LLM call via <see cref="IChatClient"/>
/// with a focused, single-turn prompt — it does NOT use the full agent context or chat history.
/// The current analysis output is fetched from <see cref="IAnalysisOrchestrationService.GetAnalysisAsync"/>
/// via the captured <c>analysisId</c>.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="AIFunction"/> objects via
/// <see cref="AIFunctionFactory.Create"/>. (ADR-010: 0 additional DI registrations.)
///
/// ADR-013: Tool methods use <see cref="AIFunctionFactory.Create"/> pattern; ChatHostContext
/// flows through the pipeline.
/// </summary>
public sealed class AnalysisExecutionTools
{
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IChatClient _chatClient;
    private readonly string? _analysisId;
    private readonly Guid _playbookId;
    private readonly string? _documentId;
    private readonly HttpContext? _httpContext;
    private readonly Func<ChatSseEvent, CancellationToken, Task>? _sseWriter;

    /// <summary>
    /// Creates a new <see cref="AnalysisExecutionTools"/> instance.
    /// </summary>
    /// <param name="analysisService">
    /// Orchestration service for analysis operations. Used by <see cref="RerunAnalysisAsync"/>
    /// to execute playbooks, and by <see cref="RefineAnalysisAsync"/> to fetch current analysis content.
    /// </param>
    /// <param name="chatClient">
    /// The chat client for inner LLM calls. Used by <see cref="RefineAnalysisAsync"/> to
    /// refine analysis output via <see cref="IChatClient.GetResponseAsync"/>.
    /// </param>
    /// <param name="analysisId">
    /// The active analysis ID from the chat session context. When provided, methods
    /// fetch the current analysis output before constructing prompts.
    /// May be null if no analysis context is available.
    /// </param>
    /// <param name="playbookId">
    /// The playbook ID for the current session. Used by <see cref="RerunAnalysisAsync"/>
    /// to construct the <see cref="PlaybookExecuteRequest"/>.
    /// </param>
    /// <param name="documentId">
    /// The active document ID. Used by <see cref="RerunAnalysisAsync"/> to specify which
    /// document to re-analyze.
    /// </param>
    /// <param name="httpContext">
    /// HTTP context for OBO authentication. Required by
    /// <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/> to download files from SPE.
    /// </param>
    /// <param name="sseWriter">
    /// Optional SSE writer delegate for out-of-band events. When provided,
    /// <see cref="RerunAnalysisAsync"/> emits "progress" events during execution and a
    /// "document_replace" event on completion. When null, events are silently skipped.
    /// </param>
    public AnalysisExecutionTools(
        IAnalysisOrchestrationService analysisService,
        IChatClient chatClient,
        string? analysisId = null,
        Guid playbookId = default,
        string? documentId = null,
        HttpContext? httpContext = null,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter = null)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _analysisId = analysisId;
        _playbookId = playbookId;
        _documentId = documentId;
        _httpContext = httpContext;
        _sseWriter = sseWriter;
    }

    /// <summary>
    /// Re-runs the analysis with optional additional instructions. Use this when the user wants
    /// to execute the analysis again from scratch — for example: "rerun the analysis",
    /// "analyze this again with more focus on financial risks", "redo the analysis including
    /// regulatory compliance".
    ///
    /// Delegates to <see cref="IAnalysisOrchestrationService.ExecutePlaybookAsync"/> with the
    /// session's playbook ID and document ID. Streams progress SSE events per-stage (not per-token)
    /// and emits a "document_replace" SSE event with the full analysis HTML on completion.
    /// </summary>
    /// <param name="additionalInstructions">
    /// Optional additional instructions or focus areas to include when re-running the analysis.
    /// Appended to the playbook's default instructions as AdditionalContext.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A string describing the re-run result for the agent's conversation context.</returns>
    [Description("Re-run the analysis with optional additional instructions. " +
                 "Use this when the user wants to execute the analysis again from scratch.")]
    public async Task<string> RerunAnalysisAsync(
        [Description("Optional additional instructions or focus areas for the re-run")]
        string additionalInstructions = "",
        CancellationToken cancellationToken = default)
    {
        // Validate required context for real orchestration
        if (_playbookId == Guid.Empty)
        {
            return "Re-analysis failed: no playbook context available for the current session. " +
                   "The session must be associated with a playbook to re-run analysis.";
        }

        if (string.IsNullOrWhiteSpace(_documentId) || !Guid.TryParse(_documentId, out var documentGuid))
        {
            return "Re-analysis failed: no active document available for the current session. " +
                   "A document must be associated with the session to re-run analysis.";
        }

        if (_httpContext is null)
        {
            return "Re-analysis failed: HTTP context not available. " +
                   "This tool requires an active HTTP request to authenticate file downloads.";
        }

        // Emit initial progress event
        await EmitProgressAsync(0, "Starting re-analysis...", cancellationToken);

        // Build the playbook execution request
        var request = new PlaybookExecuteRequest
        {
            PlaybookId = _playbookId,
            DocumentIds = [documentGuid],
            AdditionalContext = string.IsNullOrWhiteSpace(additionalInstructions)
                ? null
                : additionalInstructions
        };

        await EmitProgressAsync(10, "Loading playbook and resolving scopes...", cancellationToken);

        // Execute the playbook and collect results
        var outputBuilder = new StringBuilder();
        var analysisId = Guid.Empty;
        var toolCount = 0;

        try
        {
            await foreach (var chunk in _analysisService.ExecutePlaybookAsync(
                request, _httpContext, cancellationToken))
            {
                switch (chunk.Type)
                {
                    case "metadata":
                        // Playbook loaded, scopes resolved, document extracted
                        analysisId = chunk.AnalysisId ?? Guid.Empty;
                        await EmitProgressAsync(25, "Document extracted, executing analysis tools...", cancellationToken);
                        break;

                    case "chunk":
                        // Tool output streaming — accumulate the full analysis content
                        if (!string.IsNullOrEmpty(chunk.Content))
                        {
                            // Detect tool execution markers for per-stage progress
                            if (chunk.Content.StartsWith("[Executing:"))
                            {
                                toolCount++;
                                // Estimate progress between 25-85% based on tool count
                                var toolProgress = Math.Min(85, 25 + (toolCount * 15));
                                var toolName = chunk.Content.TrimStart('[').TrimEnd(']', '\n').Replace("Executing: ", "");
                                await EmitProgressAsync(toolProgress, $"Running tool: {toolName}...", cancellationToken);
                            }

                            outputBuilder.Append(chunk.Content);
                        }
                        break;

                    case "done":
                        // Playbook execution completed — emit final progress and document_replace
                        await EmitProgressAsync(90, "Analysis complete, preparing results...", cancellationToken);
                        break;

                    case "error":
                        // Error during execution — emit error progress
                        await EmitProgressAsync(100, $"Error: {chunk.Error}", cancellationToken);
                        return $"Re-analysis failed: {chunk.Error}";
                }
            }
        }
        catch (KeyNotFoundException ex)
        {
            return $"Re-analysis failed: {ex.Message}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Re-analysis encountered an error: {ex.Message}";
        }

        // Emit the document_replace SSE event with the full analysis output
        var analysisOutput = outputBuilder.ToString();

        if (!string.IsNullOrWhiteSpace(analysisOutput))
        {
            var replaceData = new ChatSseDocumentReplaceData(
                Html: analysisOutput,
                Metadata: new ChatSseDocumentReplaceMetadata(
                    PlaybookId: _playbookId.ToString(),
                    Timestamp: DateTimeOffset.UtcNow.ToString("o")));

            await EmitSseEventAsync(
                new ChatSseEvent("document_replace", null, Data: replaceData),
                cancellationToken);
        }

        await EmitProgressAsync(100, "Re-analysis completed successfully.", cancellationToken);

        // Return a confirmation message for the agent's conversation context
        var instructionSummary = string.IsNullOrWhiteSpace(additionalInstructions)
            ? "using the original playbook configuration"
            : $"with additional instructions: \"{additionalInstructions}\"";

        return $"Analysis re-run completed successfully {instructionSummary}. " +
               $"Analysis ID: {analysisId}. " +
               $"The updated analysis has been sent to the document panel. " +
               $"The previous version has been preserved in the undo stack.";
    }

    /// <summary>
    /// Refines the current analysis output based on a conversational follow-up instruction.
    /// Use this when the user wants to adjust, expand, or narrow an existing analysis —
    /// for example: "focus more on the risk factors", "add a section about regulatory impact",
    /// "make the recommendations more actionable".
    ///
    /// The tool fetches the current analysis output from
    /// <see cref="IAnalysisOrchestrationService.GetAnalysisAsync"/> and uses an inner LLM call
    /// to produce a refined version. Returns the refined analysis text for the agent's context.
    /// </summary>
    /// <param name="refinementInstruction">The specific refinement instruction to apply to the current analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refined analysis output, or an informative message if no analysis is available.</returns>
    [Description("Refine the current analysis based on a follow-up instruction. " +
                 "Use this when the user wants to adjust, expand, or narrow an existing analysis.")]
    public async Task<string> RefineAnalysisAsync(
        [Description("The specific refinement instruction to apply to the current analysis")]
        string refinementInstruction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refinementInstruction, nameof(refinementInstruction));

        // Fetch the current analysis output to use as context for refinement
        var analysisContent = await FetchAnalysisContentAsync(cancellationToken);

        if (analysisContent == null)
        {
            return "Refinement failed: no analysis output available. " +
                   "The user may need to run an analysis first before refining it.";
        }

        // Inner LLM call with focused single-turn prompt (no agent context/history)
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

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Returns <see cref="AIFunction"/> instances for all tool methods in this class.
    ///
    /// Called by <see cref="SprkChatAgentFactory.ResolveTools"/> to register analysis execution
    /// tools into the agent's tool set. Uses <see cref="AIFunctionFactory.Create"/> per ADR-013.
    /// </summary>
    /// <returns>An enumerable of <see cref="AIFunction"/> objects wrapping the tool methods.</returns>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            RerunAnalysisAsync,
            name: "RerunAnalysis",
            description: "Re-run the analysis with optional additional instructions.");

        yield return AIFunctionFactory.Create(
            RefineAnalysisAsync,
            name: "RefineAnalysis",
            description: "Refine the current analysis based on a follow-up instruction.");
    }

    // === Private helpers ===

    /// <summary>
    /// Fetches the current analysis output from the analysis orchestration service.
    /// Returns null if no analysis context is available or the analysis has no output.
    /// </summary>
    private async Task<string?> FetchAnalysisContentAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_analysisId) || !Guid.TryParse(_analysisId, out var analysisGuid))
        {
            return null;
        }

        try
        {
            var analysisDetail = await _analysisService.GetAnalysisAsync(analysisGuid, cancellationToken);
            return analysisDetail.WorkingDocument ?? analysisDetail.FinalOutput;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Emits a "progress" SSE event with the given percent and message.
    /// Silently skips if no SSE writer is available (non-streaming context).
    /// Progress events are per-stage (not per-token) as required by the task spec.
    /// </summary>
    private Task EmitProgressAsync(int percent, string message, CancellationToken cancellationToken)
    {
        var progressData = new ChatSseProgressData(percent, message);
        return EmitSseEventAsync(
            new ChatSseEvent("progress", null, Data: progressData),
            cancellationToken);
    }

    /// <summary>
    /// Emits an SSE event via the injected writer delegate.
    /// Silently skips if no SSE writer is available (non-streaming context).
    /// </summary>
    private async Task EmitSseEventAsync(ChatSseEvent evt, CancellationToken cancellationToken)
    {
        if (_sseWriter is null)
        {
            return;
        }

        try
        {
            await _sseWriter(evt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — silently ignore
        }
    }
}
