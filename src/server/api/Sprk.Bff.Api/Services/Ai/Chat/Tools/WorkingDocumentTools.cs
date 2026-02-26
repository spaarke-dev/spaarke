using System.ComponentModel;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). See SprkChatAgent.cs for the same pattern.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing working-document editing capabilities for the SprkChatAgent.
///
/// Exposes two document-mutation methods that perform inner LLM calls and stream tokens
/// as <see cref="DocumentStreamEvent"/> SSE events to the frontend:
///   - <see cref="EditWorkingDocumentAsync"/> — applies targeted edits to the current
///     working document based on the user's instruction.
///   - <see cref="AppendSectionAsync"/> — adds a new section to the end of the working document.
///
/// Each method emits the full SSE event sequence:
///   1. <see cref="DocumentStreamStartEvent"/> — signals operation start
///   2. N x <see cref="DocumentStreamTokenEvent"/> — streamed content tokens
///   3. <see cref="DocumentStreamEndEvent"/> — signals operation end (success, cancel, or error)
///
/// The inner LLM call uses <see cref="IChatClient.GetStreamingResponseAsync"/> with a focused,
/// single-turn prompt. The current document content is fetched from
/// <see cref="IAnalysisOrchestrationService.GetAnalysisAsync"/> via the captured <c>analysisId</c>.
///
/// The SSE writer delegate is injected at construction time by <see cref="SprkChatAgentFactory"/>,
/// binding it to <c>ChatEndpoints.WriteDocumentStreamSSEAsync</c>. This decouples the tool class
/// from <see cref="Microsoft.AspNetCore.Http.HttpResponse"/> while preserving the streaming contract.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="AIFunction"/> objects via
/// <see cref="AIFunctionFactory.Create"/>. (ADR-010: 0 additional DI registrations.)
///
/// ADR-013: Tool methods use <see cref="AIFunctionFactory.Create"/> pattern; inner LLM calls use IChatClient.
/// ADR-014: Streaming tokens are transient and MUST NOT be cached.
/// ADR-015: Document content (tokens, prompts) MUST NOT appear in log entries.
/// ADR-016: Bounded concurrency — inner calls inherit the outer session's concurrency slot.
/// ADR-019: On failure, emit terminal <see cref="DocumentStreamEndEvent"/> with error details.
/// </summary>
public sealed class WorkingDocumentTools
{
    private readonly IChatClient _chatClient;
    private readonly Func<DocumentStreamEvent, CancellationToken, Task> _writeSSE;
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly ILogger _logger;
    private readonly string? _analysisId;

    /// <summary>
    /// Creates a new <see cref="WorkingDocumentTools"/> instance.
    /// </summary>
    /// <param name="chatClient">
    /// The chat client for inner LLM calls. Used to stream document edits via
    /// <see cref="IChatClient.GetStreamingResponseAsync"/>.
    /// </param>
    /// <param name="writeSSE">
    /// Delegate that writes a <see cref="DocumentStreamEvent"/> to the SSE response stream.
    /// Bound by the factory to <c>ChatEndpoints.WriteDocumentStreamSSEAsync</c>.
    /// </param>
    /// <param name="analysisService">
    /// Orchestration service used to fetch the current working document content
    /// for the active analysis session.
    /// </param>
    /// <param name="logger">Logger for operation metadata (ADR-015: no document content).</param>
    /// <param name="analysisId">
    /// The active analysis ID from the chat session context. When provided, the tool
    /// fetches the current working document before constructing the edit prompt.
    /// May be null if no analysis context is available.
    /// </param>
    public WorkingDocumentTools(
        IChatClient chatClient,
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE,
        IAnalysisOrchestrationService analysisService,
        ILogger logger,
        string? analysisId = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _writeSSE = writeSSE ?? throw new ArgumentNullException(nameof(writeSSE));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisId = analysisId;
    }

    /// <summary>
    /// Applies a targeted edit to the current working document based on the user's instruction.
    /// Use this when the user wants to modify, rewrite, reorganize, or correct existing content
    /// in the working document — for example: "fix the grammar in the second paragraph",
    /// "rewrite the conclusion to be more concise", "add citations to the analysis section".
    ///
    /// The tool streams the edited content token-by-token as SSE events so the frontend can
    /// render updates in real time. Returns a summary of the edit operation for the agent's context.
    /// </summary>
    /// <param name="instruction">The specific edit instruction to apply to the working document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A brief summary of the edit operation for the agent's conversation context.</returns>
    [Description("Apply a targeted edit to the current working document based on the user's instruction. " +
                 "Use this when the user wants to modify, rewrite, reorganize, or correct existing content.")]
    public async Task<string> EditWorkingDocumentAsync(
        [Description("The specific edit instruction to apply to the working document")]
        string instruction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction, nameof(instruction));

        var operationId = Guid.NewGuid();
        var tokenIndex = 0;

        _logger.LogInformation(
            "EditWorkingDocument starting — operationId={OperationId}, analysisId={AnalysisId}, instructionLen={InstructionLen}",
            operationId, _analysisId, instruction.Length);

        try
        {
            // Emit document_stream_start
            await _writeSSE(
                new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"),
                cancellationToken);

            // Fetch the current working document content from the analysis context
            var documentContent = await FetchWorkingDocumentContentAsync(cancellationToken);

            if (documentContent == null)
            {
                // No document content available — emit end and return informative message
                await _writeSSE(
                    new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: 0,
                        ErrorCode: "NO_DOCUMENT", ErrorMessage: "No working document content available for editing."),
                    cancellationToken);

                return "Edit failed: no working document content available. " +
                       "The user may need to run an analysis first to create a working document.";
            }

            // ADR-015: Log content length only, not the content itself
            _logger.LogDebug(
                "Fetched working document for edit — operationId={OperationId}, contentLen={ContentLen}",
                operationId, documentContent.Length);

            // Construct focused edit prompt (ADR-015: prompt content not logged)
            var messages = new List<AiChatMessage>
            {
                new AiChatMessage(ChatRole.System,
                    "You are a professional document editor. You are editing an existing document. " +
                    "Apply the user's edit instruction to the document content below. " +
                    "Output ONLY the complete modified document content — no explanation, preamble, " +
                    "meta-commentary, or markers indicating what changed.\n\n" +
                    "Current document content:\n" + documentContent),
                new AiChatMessage(ChatRole.User, instruction)
            };

            // Stream tokens from the inner LLM call via IChatClient
            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    await _writeSSE(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            // Emit successful document_stream_end
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex),
                cancellationToken);

            _logger.LogInformation(
                "EditWorkingDocument completed — operationId={OperationId}, totalTokens={TotalTokens}",
                operationId, tokenIndex);

            // Return summary for agent context (not the full document content per ADR-015)
            return $"Working document edited successfully. Operation: {operationId}. " +
                   $"Applied edit instruction ({instruction.Length} chars). Streamed {tokenIndex} tokens.";
        }
        catch (OperationCanceledException)
        {
            // Cancellation: emit document_stream_end with cancelled=true.
            // Partial content already emitted is preserved in the editor (not rolled back).
            _logger.LogInformation(
                "EditWorkingDocument cancelled — operationId={OperationId}, tokensEmitted={TokensEmitted}",
                operationId, tokenIndex);

            await EmitCancelledEndEventAsync(operationId, tokenIndex);

            return $"Edit operation cancelled. Operation: {operationId}. " +
                   $"Partial content preserved ({tokenIndex} tokens emitted before cancellation).";
        }
        catch (Exception ex)
        {
            // ADR-019: Emit terminal document_stream_end with error details.
            // ADR-015: Do NOT log document content or full exception details containing content.
            _logger.LogError(ex,
                "EditWorkingDocument failed — operationId={OperationId}, tokensEmitted={TokensEmitted}, errorType={ErrorType}",
                operationId, tokenIndex, ex.GetType().Name);

            await EmitErrorEndEventAsync(operationId, tokenIndex, "LLM_STREAM_FAILED",
                "Document editing failed. The AI service encountered an error during streaming.");

            return $"Edit operation failed. Operation: {operationId}. " +
                   $"Error: {ex.GetType().Name}. Partial content ({tokenIndex} tokens) may have been emitted.";
        }
    }

    /// <summary>
    /// Adds a new section to the end of the working document.
    /// Use this when the user wants to extend the document with new content — for example:
    /// "add a risk assessment section", "append a summary of findings",
    /// "add a recommendations section based on the analysis".
    ///
    /// The tool streams the new section content token-by-token as SSE events so the frontend
    /// can render the addition in real time. Returns a summary for the agent's context.
    /// </summary>
    /// <param name="sectionTitle">The title/heading for the new section to append.</param>
    /// <param name="instruction">Instructions describing what content to generate for the new section.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A brief summary of the append operation for the agent's conversation context.</returns>
    [Description("Add a new section to the end of the working document. " +
                 "Use this when the user wants to extend the document with new content.")]
    public async Task<string> AppendSectionAsync(
        [Description("The title/heading for the new section to append")]
        string sectionTitle,
        [Description("Instructions describing what content to generate for the new section")]
        string instruction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionTitle, nameof(sectionTitle));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction, nameof(instruction));

        var operationId = Guid.NewGuid();
        var tokenIndex = 0;

        _logger.LogInformation(
            "AppendSection starting — operationId={OperationId}, analysisId={AnalysisId}, sectionTitle={SectionTitle}, instructionLen={InstructionLen}",
            operationId, _analysisId, sectionTitle, instruction.Length);

        try
        {
            // Emit document_stream_start
            await _writeSSE(
                new DocumentStreamStartEvent(operationId, TargetPosition: "end", OperationType: "insert"),
                cancellationToken);

            // Fetch the current working document content for context (optional for append)
            var documentContent = await FetchWorkingDocumentContentAsync(cancellationToken);

            // ADR-015: Log content length only
            _logger.LogDebug(
                "Fetched working document for append — operationId={OperationId}, contentLen={ContentLen}",
                operationId, documentContent?.Length ?? 0);

            // Emit the section heading as the first token before LLM content
            var heading = $"## {sectionTitle}\n\n";
            await _writeSSE(
                new DocumentStreamTokenEvent(operationId, Token: heading, Index: tokenIndex),
                cancellationToken);
            tokenIndex++;

            // Construct focused append prompt (ADR-015: prompt content not logged)
            var systemPrompt = "You are a professional document author. " +
                "Generate content for a new section to be appended to an existing document. " +
                "Output ONLY the section body content — do NOT include the section heading " +
                "(it has already been emitted). No explanation, preamble, or meta-commentary.";

            if (!string.IsNullOrWhiteSpace(documentContent))
            {
                systemPrompt += "\n\nFor context, here is the existing document content:\n" + documentContent;
            }

            var messages = new List<AiChatMessage>
            {
                new AiChatMessage(ChatRole.System, systemPrompt),
                new AiChatMessage(ChatRole.User,
                    $"Write the content for a new section titled \"{sectionTitle}\" based on the following instruction: {instruction}")
            };

            // Stream tokens from the inner LLM call via IChatClient
            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    await _writeSSE(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            // Emit successful document_stream_end
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex),
                cancellationToken);

            _logger.LogInformation(
                "AppendSection completed — operationId={OperationId}, sectionTitle={SectionTitle}, totalTokens={TotalTokens}",
                operationId, sectionTitle, tokenIndex);

            // Return summary for agent context (not the full document content per ADR-015)
            return $"New section \"{sectionTitle}\" appended to working document. Operation: {operationId}. " +
                   $"Streamed {tokenIndex} tokens.";
        }
        catch (OperationCanceledException)
        {
            // Cancellation: emit document_stream_end with cancelled=true.
            // Partial content already emitted is preserved in the editor (not rolled back).
            _logger.LogInformation(
                "AppendSection cancelled — operationId={OperationId}, sectionTitle={SectionTitle}, tokensEmitted={TokensEmitted}",
                operationId, sectionTitle, tokenIndex);

            await EmitCancelledEndEventAsync(operationId, tokenIndex);

            return $"Append operation cancelled for section \"{sectionTitle}\". Operation: {operationId}. " +
                   $"Partial content preserved ({tokenIndex} tokens emitted before cancellation).";
        }
        catch (Exception ex)
        {
            // ADR-019: Emit terminal document_stream_end with error details.
            // ADR-015: Do NOT log document content or full exception details containing content.
            _logger.LogError(ex,
                "AppendSection failed — operationId={OperationId}, sectionTitle={SectionTitle}, tokensEmitted={TokensEmitted}, errorType={ErrorType}",
                operationId, sectionTitle, tokenIndex, ex.GetType().Name);

            await EmitErrorEndEventAsync(operationId, tokenIndex, "LLM_STREAM_FAILED",
                "Section generation failed. The AI service encountered an error during streaming.");

            return $"Append operation failed for section \"{sectionTitle}\". Operation: {operationId}. " +
                   $"Error: {ex.GetType().Name}. Partial content ({tokenIndex} tokens) may have been emitted.";
        }
    }

    /// <summary>
    /// Returns <see cref="AIFunction"/> instances for all tool methods in this class.
    ///
    /// Called by <see cref="SprkChatAgentFactory.ResolveTools"/> to register document-editing
    /// tools into the agent's tool set. Uses <see cref="AIFunctionFactory.Create"/> per ADR-013.
    /// </summary>
    /// <returns>An enumerable of <see cref="AIFunction"/> objects wrapping the tool methods.</returns>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            EditWorkingDocumentAsync,
            name: "EditWorkingDocument",
            description: "Apply a targeted edit to the current working document based on the user's instruction.");

        yield return AIFunctionFactory.Create(
            AppendSectionAsync,
            name: "AppendSection",
            description: "Add a new section to the end of the working document with generated content.");
    }

    // === Private helpers ===

    /// <summary>
    /// Fetches the current working document content from the analysis orchestration service.
    /// Returns null if no analysis context is available or the analysis has no working document.
    /// </summary>
    private async Task<string?> FetchWorkingDocumentContentAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_analysisId) || !Guid.TryParse(_analysisId, out var analysisGuid))
        {
            _logger.LogDebug("No analysisId available; skipping working document fetch");
            return null;
        }

        try
        {
            var analysisDetail = await _analysisService.GetAnalysisAsync(analysisGuid, cancellationToken);
            return analysisDetail.WorkingDocument ?? analysisDetail.FinalOutput;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning(
                "Analysis {AnalysisId} not found when fetching working document",
                _analysisId);
            return null;
        }
    }

    /// <summary>
    /// Emits a <see cref="DocumentStreamEndEvent"/> indicating cancellation.
    /// Uses a new non-throwing CancellationToken to ensure delivery even after cancellation.
    /// </summary>
    private async Task EmitCancelledEndEventAsync(Guid operationId, int tokensEmitted)
    {
        try
        {
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: true, TotalTokens: tokensEmitted),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort — if the SSE connection is already closed, log and move on
            _logger.LogWarning(ex,
                "Failed to emit cancelled end event — operationId={OperationId}",
                operationId);
        }
    }

    /// <summary>
    /// Emits a <see cref="DocumentStreamEndEvent"/> indicating an error (ADR-019).
    /// Uses a new non-throwing CancellationToken to ensure delivery.
    /// </summary>
    private async Task EmitErrorEndEventAsync(Guid operationId, int tokensEmitted, string errorCode, string errorMessage)
    {
        try
        {
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokensEmitted,
                    ErrorCode: errorCode, ErrorMessage: errorMessage),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort — if the SSE connection is already closed, log and move on
            _logger.LogWarning(ex,
                "Failed to emit error end event — operationId={OperationId}, errorCode={ErrorCode}",
                operationId, errorCode);
        }
    }
}
