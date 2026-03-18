using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;

// Explicit alias to resolve ChatMessage ambiguity between the Dataverse domain model
// (Sprk.Bff.Api.Models.Ai.Chat.ChatMessage) and the Agent Framework type
// (Microsoft.Extensions.AI.ChatMessage). See SprkChatAgent.cs for the same pattern.
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

// ──────────────────────────────────────────────────────────────────────────────
// CRITICAL SAFETY CONSTRAINT (spec FR-12, ADR-013):
//   Write-back targets sprk_analysisoutput.sprk_workingdocument ONLY.
//   This class MUST NEVER call SpeFileStore, GraphServiceClient write methods,
//   UploadContent, PutContent, UpdateFile, or any SharePoint Embedded write operation.
//   All mutations route through IWorkingDocumentService → IGenericEntityService (Dataverse).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AI tool class providing working-document editing capabilities for the SprkChatAgent.
///
/// Exposes three document-mutation methods:
///   - <see cref="EditWorkingDocumentAsync"/> — applies targeted edits to the current
///     working document based on the user's instruction (streams tokens via SSE).
///   - <see cref="AppendSectionAsync"/> — adds a new section to the end of the working
///     document (streams tokens via SSE).
///   - <see cref="WriteBackToWorkingDocumentAsync"/> — persists AI-generated content to
///     <c>sprk_analysisoutput.sprk_workingdocument</c> in Dataverse. This tool is
///     plan-preview-gated (MUST only execute from an approved plan — spec FR-11) and
///     MUST NEVER write to SPE/SharePoint source files (spec FR-12).
///
/// Each streaming method emits the full SSE event sequence:
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
    private readonly IWorkingDocumentService _workingDocumentService;
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
    /// <param name="workingDocumentService">
    /// Persistence service for writing back content to <c>sprk_analysisoutput.sprk_workingdocument</c>.
    /// Routes through Dataverse only — no SPE/SharePoint writes (spec FR-12).
    /// </param>
    /// <param name="logger">Logger for operation metadata (ADR-015: no document content).</param>
    /// <param name="analysisId">
    /// The active analysis ID from the chat session context. When provided, the tool
    /// fetches the current working document before constructing the edit prompt, and
    /// uses this ID as the write-back target for <see cref="WriteBackToWorkingDocumentAsync"/>.
    /// May be null if no analysis context is available.
    /// </param>
    public WorkingDocumentTools(
        IChatClient chatClient,
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE,
        IAnalysisOrchestrationService analysisService,
        IWorkingDocumentService workingDocumentService,
        ILogger logger,
        string? analysisId = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _writeSSE = writeSSE ?? throw new ArgumentNullException(nameof(writeSSE));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _workingDocumentService = workingDocumentService ?? throw new ArgumentNullException(nameof(workingDocumentService));
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
        var contentBuilder = new StringBuilder();

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

            // Stream tokens from the inner LLM call via IChatClient.
            // Accumulate content for SHA-256 hash computation (ADR-014: hash only, not cached).
            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    contentBuilder.Append(tokenText);
                    await _writeSSE(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            // R2-023: Compute SHA-256 hash of the final assembled content for integrity verification.
            var contentHash = ComputeContentHash(contentBuilder.ToString());

            // Emit successful document_stream_end with content hash
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex,
                    ContentHash: contentHash),
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
        var contentBuilder = new StringBuilder();

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
            contentBuilder.Append(heading);
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

            // Stream tokens from the inner LLM call via IChatClient.
            // Accumulate content for SHA-256 hash computation (ADR-014: hash only, not cached).
            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    contentBuilder.Append(tokenText);
                    await _writeSSE(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            // R2-023: Compute SHA-256 hash of the final assembled content for integrity verification.
            var contentHash = ComputeContentHash(contentBuilder.ToString());

            // Emit successful document_stream_end with content hash
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex,
                    ContentHash: contentHash),
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
    /// Writes AI-generated content back to <c>sprk_analysisoutput.sprk_workingdocument</c>
    /// in Dataverse.
    ///
    /// SAFETY CONSTRAINT (spec FR-12): This method MUST ONLY write to the Dataverse field
    /// <c>sprk_analysisoutput.sprk_workingdocument</c>. It MUST NEVER call SpeFileStore,
    /// GraphServiceClient write methods, or any SharePoint Embedded write operation.
    ///
    /// PLAN GATE (spec FR-11): This tool is listed in <see cref="CompoundIntentDetector.WriteBackToolNames"/>
    /// and therefore ALWAYS triggers a plan preview gate before execution. It should only be
    /// called from an approved plan execution (POST /api/ai/chat/sessions/{sessionId}/plan/approve).
    ///
    /// When <c>_analysisId</c> is null (no analysis context available), the method returns
    /// an informative error string so the agent can surface a helpful message to the user.
    /// </summary>
    /// <param name="content">The complete AI-generated content to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of the write-back operation for the agent's conversation context.</returns>
    [Description("Write AI-generated content back to the analysis working document in Dataverse (sprk_analysisoutput.sprk_workingdocument). " +
                 "PLAN-PREVIEW-GATED: this tool always requires user approval before execution. " +
                 "SAFETY: writes ONLY to Dataverse — never modifies the SharePoint source file.")]
    public async Task<string> WriteBackToWorkingDocumentAsync(
        [Description("The complete content to write back to sprk_analysisoutput.sprk_workingdocument")]
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content, nameof(content));

        var operationId = Guid.NewGuid();

        // ADR-015: Log content length only — never the content itself
        _logger.LogInformation(
            "WriteBackToWorkingDocument starting — operationId={OperationId}, analysisId={AnalysisId}, contentLen={ContentLen}",
            operationId, _analysisId, content.Length);

        // Guard: require a valid analysis ID in the session context
        if (string.IsNullOrWhiteSpace(_analysisId) || !Guid.TryParse(_analysisId, out var analysisGuid))
        {
            _logger.LogWarning(
                "WriteBackToWorkingDocument: no valid analysisId in session context — cannot write back. analysisId={AnalysisId}",
                _analysisId);

            return "Write-back failed: no analysis context is available for this chat session. " +
                   "The user must open SprkChat from within an active analysis to enable write-back.";
        }

        try
        {
            // R2-023: Emit document_stream_start before write-back content streaming (spec FR-04).
            // The "replace" operation type indicates the full working document content is being replaced.
            await _writeSSE(
                new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"),
                cancellationToken);

            // R2-023: Stream the write-back content as document_stream_token events.
            // Content is chunked to provide progressive streaming to the client (spec FR-04).
            // ADR-014: Tokens are write-through only — not cached.
            // ADR-015: Token content MUST NOT be logged.
            var position = 0;
            var tokenIndex = 0;
            const int chunkSize = 100; // Characters per chunk — balances granularity vs overhead.

            for (var offset = 0; offset < content.Length; offset += chunkSize)
            {
                var chunk = content.Substring(offset, Math.Min(chunkSize, content.Length - offset));
                await _writeSSE(
                    new DocumentStreamTokenEvent(operationId, Token: chunk, Index: tokenIndex),
                    cancellationToken);
                position += chunk.Length;
                tokenIndex++;
            }

            // R2-023: Compute SHA-256 hash of the final content for integrity verification.
            // ADR-014: Only the hash is retained, not the content itself.
            var contentHash = ComputeContentHash(content);

            // R2-023: Emit document_stream_end with the content hash (spec FR-04).
            // This MUST precede the "done" SSE event (ordering constraint).
            await _writeSSE(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex,
                    ContentHash: contentHash),
                cancellationToken);

            // SAFETY: This is the ONLY write operation in this method.
            // It routes through IWorkingDocumentService → IGenericEntityService (Dataverse SDK).
            // It targets sprk_analysisoutput.sprk_workingdocument ONLY.
            // No SpeFileStore, GraphServiceClient, or SPE write calls are made here.
            await _workingDocumentService.UpdateWorkingDocumentAsync(
                analysisGuid,
                content,
                cancellationToken);

            _logger.LogInformation(
                "WriteBackToWorkingDocument completed — operationId={OperationId}, analysisId={AnalysisId}, contentLen={ContentLen}, totalTokens={TotalTokens}",
                operationId, _analysisId, content.Length, tokenIndex);

            return $"Working document written back to Dataverse successfully. " +
                   $"Target: sprk_analysisoutput.sprk_workingdocument (analysisId={_analysisId}). " +
                   $"Content length: {content.Length} characters. Streamed {tokenIndex} tokens. " +
                   $"Content hash: {contentHash}.";
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "WriteBackToWorkingDocument cancelled — operationId={OperationId}, analysisId={AnalysisId}",
                operationId, _analysisId);

            await EmitCancelledEndEventAsync(operationId, 0);

            return $"Write-back operation cancelled for analysisId={_analysisId}. " +
                   $"Operation: {operationId}. The working document was not updated in Dataverse.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WriteBackToWorkingDocument failed — operationId={OperationId}, analysisId={AnalysisId}, errorType={ErrorType}",
                operationId, _analysisId, ex.GetType().Name);

            await EmitErrorEndEventAsync(operationId, 0, "WRITE_BACK_FAILED",
                "Write-back to Dataverse failed. The working document was not updated.");

            return $"Write-back failed for analysisId={_analysisId}. " +
                   $"Error: {ex.GetType().Name}. The working document was not updated in Dataverse. " +
                   "Please retry or contact support if the issue persists.";
        }
    }

    /// <summary>
    /// Returns <see cref="AIFunction"/> instances for all tool methods in this class.
    ///
    /// Called by <see cref="SprkChatAgentFactory.ResolveTools"/> to register document-editing
    /// tools into the agent's tool set. Uses <see cref="AIFunctionFactory.Create"/> per ADR-013.
    ///
    /// WriteBackToWorkingDocument is included in the returned tools and is registered in
    /// <see cref="CompoundIntentDetector.WriteBackToolNames"/>, ensuring it always triggers
    /// the plan preview gate before execution (spec FR-11).
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

        yield return AIFunctionFactory.Create(
            WriteBackToWorkingDocumentAsync,
            name: "WriteBackToWorkingDocument",
            description: "Write AI-generated content back to the analysis working document in Dataverse " +
                         "(sprk_analysisoutput.sprk_workingdocument). " +
                         "PLAN-PREVIEW-GATED: always requires user approval. " +
                         "SAFETY: writes ONLY to Dataverse, never to the SharePoint source file.");
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
    /// Computes a SHA-256 hash of the content, prefixed with "sha256:" for the
    /// <see cref="DocumentStreamEndEvent.ContentHash"/> field.
    ///
    /// Enables the client to verify that the reconstructed document content matches
    /// what the BFF computed — a lightweight integrity check that prevents partial writes
    /// from being applied to the editor if SSE events were lost.
    ///
    /// ADR-014: Only the hash is retained, not the content.
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();
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
