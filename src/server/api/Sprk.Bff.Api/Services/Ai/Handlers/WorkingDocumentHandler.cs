using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

// ──────────────────────────────────────────────────────────────────────────────
// CRITICAL SAFETY CONSTRAINT (spec FR-12, ADR-013):
//   Write-back targets sprk_analysisoutput.sprk_workingdocument ONLY.
//   This handler MUST NEVER call SpeFileStore, GraphServiceClient write methods,
//   UploadContent, PutContent, UpdateFile, or any SharePoint Embedded write operation.
//   All mutations route through IWorkingDocumentService → IGenericEntityService (Dataverse).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Chat-side typed handler that performs working-document mutation operations (R6 Wave 9).
/// Replaces the legacy hardcoded <c>WorkingDocumentTools</c> class previously instantiated
/// in <c>SprkChatAgentFactory.ResolveTools</c>. Closes the Q9 chat-tool migration at 10/10.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, three methods</strong>: three <c>sprk_analysistool</c> rows
/// (<c>WORKING-DOC-EDIT</c>, <c>WORKING-DOC-APPEND-SECTION</c>, <c>WORKING-DOC-WRITE-BACK</c>)
/// share this single <c>sprk_handlerclass = WorkingDocumentHandler</c>. Each row's
/// <c>sprk_configuration.method</c> selects the internal method. This mirrors the
/// <see cref="KnowledgeRetrievalHandler"/> (Wave 7c) + <see cref="CodeInterpreterHandler"/>
/// (Wave 8) multi-method-handler shape.
/// </para>
/// <list type="bullet">
/// <item><c>method = "EditWorkingDocument"</c> — apply a targeted edit to the current working
/// document. Streams tokens via the document-stream SSE side-channel writer
/// (<see cref="ChatInvocationContext.DocumentStreamWriter"/>). Returns a single
/// <see cref="ToolResult"/> summary to the LLM.</item>
/// <item><c>method = "AppendSection"</c> — append a new section with a heading + LLM-generated
/// body content. Streams tokens via the document-stream SSE side-channel writer.</item>
/// <item><c>method = "WriteBackToWorkingDocument"</c> — persist AI-generated content to the
/// Dataverse field <c>sprk_analysisoutput.sprk_workingdocument</c> (spec FR-12). Chunks the
/// supplied content into stream tokens for client progress display, then writes via
/// <see cref="IWorkingDocumentService.UpdateWorkingDocumentAsync"/>. NEVER touches SPE / Graph
/// (FR-12 safety constraint).</item>
/// </list>
/// <para>
/// <strong>Capability gate (R6 Wave 7b infrastructure)</strong>: the corresponding
/// <c>sprk_analysistool</c> rows set <c>sprk_requiredcapability = "write_back"</c>
/// (= <see cref="Sprk.Bff.Api.Models.Ai.Chat.PlaybookCapabilities.WriteBack"/>). The
/// data-driven block in <c>SprkChatAgentFactory.ResolveTools</c> applies
/// <c>IsCapabilityGateSatisfied</c> at session start and silently withholds these rows when
/// the playbook's capability set lacks the value. Standalone chat (no playbook,
/// capabilities = <c>CoreCapabilities</c>) does NOT include <c>write_back</c>, so this handler
/// is unreachable from standalone chat — preserving the pre-Wave-9 security boundary that the
/// hardcoded <c>if (capabilities.Contains(PlaybookCapabilities.WriteBack))</c> block in the
/// factory enforced.
/// </para>
/// <para>
/// <strong>Streaming side-channel (ADR-033 binding pattern)</strong>: streaming methods read
/// <see cref="ChatInvocationContext.DocumentStreamWriter"/> at the top of
/// <see cref="ExecuteChatAsync"/> and emit the Start → N×Token → End event sequence directly
/// during execution. When the writer is null (background processing, replay, tests without
/// wiring) the handler returns a graceful <see cref="ToolResult.Error"/> with a clear "not
/// wired" diagnostic AND does NOT attempt the inner LLM call. The <see cref="IToolHandler"/>
/// contract is unchanged — streaming is a side effect, not a return-type change. See ADR-033
/// §4.2 for the canonical handler emit pattern.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IChatClient"/> (inner streaming LLM calls),
/// <see cref="IAnalysisOrchestrationService"/> (fetch current document content), and
/// <see cref="IWorkingDocumentService"/> (persist write-back content to Dataverse).
/// Resolved via constructor injection (auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/>. Write-back
/// chat tools are not invocable from playbook orchestration (spec FR-12 safety constraint;
/// the 11 production node executors per NFR-08 do not include document mutation), matching
/// the legacy hardcoded registration shape.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// never injects this handler.</item>
/// <item><strong>ADR-014</strong>: streaming tokens are TRANSIENT — never persisted to Redis /
/// Cosmos. The accumulating <see cref="StringBuilder"/> used to compute the SHA-256 content
/// hash lives only for the duration of the handler invocation. The per-call <c>ContentHash</c>
/// returned in <see cref="DocumentStreamEndEvent"/> is the cacheable artifact.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + method + IDs + content
/// LENGTH + token COUNTS + duration ONLY. NEVER tokens, NEVER prompts, NEVER document content,
/// NEVER write-back content.</item>
/// <item><strong>ADR-016</strong>: inner streaming LLM calls inherit the outer chat session's
/// concurrency slot — no separate semaphore.</item>
/// <item><strong>ADR-018</strong>: dependencies (<c>IWorkingDocumentService</c> +
/// <c>IAnalysisOrchestrationService</c>) are gated by <c>Analysis:Enabled</c> +
/// <c>DocumentIntelligence:Enabled</c>. When kill-switched off, the handler is simply not
/// auto-discovered (its DI deps don't resolve), matching the hardcoded path's pre-R6 behavior.</item>
/// <item><strong>ADR-019</strong>: emits a terminal <see cref="DocumentStreamEndEvent"/> in
/// EVERY exit path (success / cancellation / error) so the frontend can finalize editor UI
/// state. No half-open document streams.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// <item><strong>ADR-033</strong>: implements the document-stream side-channel emit pattern
/// (§4.2) — reads <see cref="ChatInvocationContext.DocumentStreamWriter"/>, emits events
/// directly, returns single <see cref="ToolResult"/> to the LLM.</item>
/// </list>
/// </remarks>
public sealed class WorkingDocumentHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(WorkingDocumentHandler);

    internal const string MethodEditWorkingDocument = "EditWorkingDocument";
    internal const string MethodAppendSection = "AppendSection";
    internal const string MethodWriteBackToWorkingDocument = "WriteBackToWorkingDocument";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodEditWorkingDocument,
        MethodAppendSection,
        MethodWriteBackToWorkingDocument
    };

    /// <summary>
    /// Chunk size for WriteBack token-progress emission. Matches the legacy
    /// <c>WorkingDocumentTools</c> value to preserve client rendering cadence.
    /// </summary>
    private const int WriteBackChunkSize = 100;

    private readonly IChatClient _chatClient;
    private readonly IAnalysisOrchestrationService _analysisService;
    private readonly IWorkingDocumentService _workingDocumentService;
    private readonly ILogger<WorkingDocumentHandler> _logger;

    public WorkingDocumentHandler(
        IChatClient chatClient,
        IAnalysisOrchestrationService analysisService,
        IWorkingDocumentService workingDocumentService,
        ILogger<WorkingDocumentHandler> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _workingDocumentService = workingDocumentService ?? throw new ArgumentNullException(nameof(workingDocumentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Working Document",
        Description: "Mutates the current analysis working document via three methods: " +
                     "'EditWorkingDocument' applies targeted edits and streams the result token-by-token; " +
                     "'AppendSection' adds a new section with a heading + LLM-generated body and streams tokens; " +
                     "'WriteBackToWorkingDocument' persists supplied content to sprk_analysisoutput.sprk_workingdocument " +
                     "in Dataverse (plan-preview-gated per spec FR-11; NEVER writes to SharePoint per spec FR-12). " +
                     "All three methods emit document-stream SSE events (Start → N×Token → End) via the " +
                     "context-side writer delegate (ADR-033).",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("instruction", "Edit instruction text (EditWorkingDocument + AppendSection methods).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("sectionTitle", "Heading for the new section (AppendSection method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("content", "Content to persist to Dataverse (WriteBackToWorkingDocument method).", ToolParameterType.String, Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only. The legacy <c>WorkingDocumentTools</c> was registered exclusively for chat;
    /// document-mutation tools must not be invocable from playbook orchestration per spec FR-12
    /// safety constraint, and the 11 production node executors (NFR-08) do not include a
    /// document mutation executor.
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

        var method = ResolveMethod(tool.Configuration);
        if (!SupportedMethods.Contains(method))
        {
            return ToolValidationResult.Failure(
                $"Configured method '{method}' is not supported. " +
                $"Use 'EditWorkingDocument', 'AppendSection', or 'WriteBackToWorkingDocument'.");
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (string.Equals(method, MethodEditWorkingDocument, StringComparison.Ordinal))
            {
                if (!HasNonEmptyString(doc.RootElement, "instruction"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'instruction' string field for the EditWorkingDocument method.");
            }
            else if (string.Equals(method, MethodAppendSection, StringComparison.Ordinal))
            {
                if (!HasNonEmptyString(doc.RootElement, "sectionTitle"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'sectionTitle' string field for the AppendSection method.");

                if (!HasNonEmptyString(doc.RootElement, "instruction"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'instruction' string field for the AppendSection method.");
            }
            else
            {
                // WriteBackToWorkingDocument
                if (!HasNonEmptyString(doc.RootElement, "content"))
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'content' string field for the WriteBackToWorkingDocument method.");
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
    /// Playbook-context path. Not used today — <see cref="SupportedInvocationContexts"/> is
    /// <see cref="InvocationContextKind.Chat"/>, so the playbook dispatcher will not route here.
    /// Provided as a defensive guard: if the contract were extended in the future, the playbook
    /// path returns an error result rather than throwing.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "WorkingDocumentHandler does not support playbook invocation. Use chat invocation only " +
            "(spec FR-12 — document mutation tools must not run in playbook orchestration).",
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
        var method = ResolveMethod(tool.Configuration);
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        // ADR-033 §4.2: read the document-stream writer up front; null-check + degrade
        // gracefully when not wired (background processing, replay, tests without wiring).
        // Returns a clear diagnostic and does NOT attempt any inner LLM / Dataverse call.
        var streamWriter = context.DocumentStreamWriter;
        if (streamWriter is null)
        {
            _logger.LogWarning(
                "WorkingDocumentHandler ({Correlation}) method '{Method}' rejected — DocumentStreamWriter not wired",
                correlationLogId, method);

            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "DocumentStreamWriter not wired on ChatInvocationContext; document streaming is unavailable " +
                "for this session. The chat-session entry point must bind DocumentStreamWriter to the " +
                "per-request SSE writer (ADR-033 wiring point).",
                ToolErrorCodes.DependencyUnavailable,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        try
        {
            _logger.LogInformation(
                "WorkingDocumentHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            return method switch
            {
                MethodEditWorkingDocument =>
                    await ExecuteEditWorkingDocumentAsync(context, tool, streamWriter, startedAt, stopwatch, correlationLogId, cancellationToken),

                MethodAppendSection =>
                    await ExecuteAppendSectionAsync(context, tool, streamWriter, startedAt, stopwatch, correlationLogId, cancellationToken),

                MethodWriteBackToWorkingDocument =>
                    await ExecuteWriteBackAsync(context, tool, streamWriter, startedAt, stopwatch, correlationLogId, cancellationToken),

                _ => BuildUnsupportedMethodResult(tool, method, startedAt)
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "WorkingDocumentHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}, method '{Method}'",
                context.ChatSessionId, context.DecisionId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WorkingDocumentHandler chat failed for session {ChatSessionId}, decision {DecisionId}, method '{Method}': {ErrorType}",
                context.ChatSessionId, context.DecisionId, method, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // EditWorkingDocument — streams targeted edit per instruction
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteEditWorkingDocumentAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        Func<DocumentStreamEvent, CancellationToken, Task> streamWriter,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var args = ParseChatArgs(context.ToolArgumentsJson);
        if (string.IsNullOrWhiteSpace(args.Instruction))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "instruction is required for the EditWorkingDocument method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var operationId = Guid.NewGuid();
        var tokenIndex = 0;
        var contentBuilder = new StringBuilder();

        // ADR-015: log instruction LENGTH only — never the instruction text.
        _logger.LogInformation(
            "WorkingDocumentHandler ({Correlation}) EditWorkingDocument starting — operationId={OperationId}, instructionLen={InstructionLen}",
            correlationLogId, operationId, args.Instruction!.Length);

        // ADR-033: emit Start in the entry path so the editor can show the in-flight indicator
        // even before the inner LLM call begins.
        await streamWriter(
            new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"),
            cancellationToken);

        try
        {
            var documentContent = await FetchWorkingDocumentContentAsync(context, operationId, correlationLogId, cancellationToken);
            if (documentContent is null)
            {
                // No document context — emit terminal End with informative error and return.
                await EmitErrorEndEventAsync(
                    streamWriter, operationId, tokenIndex,
                    "NO_DOCUMENT", "No working document content available for editing.",
                    correlationLogId);

                stopwatch.Stop();
                return ToolResult.Ok(
                    HandlerId, tool.Id, tool.Name,
                    data: new WorkingDocumentPayload
                    {
                        Method = MethodEditWorkingDocument,
                        OperationId = operationId,
                        Message = "Edit failed: no working document content available. " +
                                  "The user may need to run an analysis first to create a working document, " +
                                  "or open this chat from within an active analysis context.",
                        TotalTokens = 0
                    },
                    summary: $"Edit deferred — no working document. OperationId={operationId}.",
                    confidence: 0.0,
                    execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            // ADR-015: log content LENGTH only — never the content text.
            _logger.LogDebug(
                "WorkingDocumentHandler ({Correlation}) EditWorkingDocument fetched document — operationId={OperationId}, contentLen={ContentLen}",
                correlationLogId, operationId, documentContent.Length);

            var messages = new List<AiChatMessage>
            {
                new AiChatMessage(ChatRole.System,
                    "You are a professional document editor. You are editing an existing document. " +
                    "Apply the user's edit instruction to the document content below. " +
                    "Output ONLY the complete modified document content — no explanation, preamble, " +
                    "meta-commentary, or markers indicating what changed.\n\n" +
                    "Current document content:\n" + documentContent),
                new AiChatMessage(ChatRole.User, args.Instruction!)
            };

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    contentBuilder.Append(tokenText);
                    await streamWriter(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            var contentHash = ComputeContentHash(contentBuilder.ToString());

            await streamWriter(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex, ContentHash: contentHash),
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) EditWorkingDocument completed — operationId={OperationId}, totalTokens={TotalTokens} in {Duration}ms",
                correlationLogId, operationId, tokenIndex, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodEditWorkingDocument,
                    OperationId = operationId,
                    TotalTokens = tokenIndex,
                    ContentHash = contentHash
                },
                summary: $"Working document edited successfully. OperationId={operationId}. " +
                         $"Streamed {tokenIndex} tokens.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1
                });
        }
        catch (OperationCanceledException)
        {
            // ADR-019 + ADR-033: emit terminal End with Cancelled=true so the editor finalizes;
            // do NOT rethrow — convert to a user-readable summary returned to the LLM.
            await EmitCancelledEndEventAsync(streamWriter, operationId, tokenIndex, correlationLogId);

            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) EditWorkingDocument cancelled — operationId={OperationId}, tokensEmitted={TokensEmitted}",
                correlationLogId, operationId, tokenIndex);

            stopwatch.Stop();
            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodEditWorkingDocument,
                    OperationId = operationId,
                    Cancelled = true,
                    TotalTokens = tokenIndex
                },
                summary: $"Edit operation cancelled. OperationId={operationId}. " +
                         $"Partial content preserved ({tokenIndex} tokens emitted before cancellation).",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            // ADR-019: emit terminal End with error so the editor finalizes the UI state.
            // ADR-015: error message MUST NOT carry document content / exception details with content.
            await EmitErrorEndEventAsync(
                streamWriter, operationId, tokenIndex,
                "LLM_STREAM_FAILED", "Document editing failed. The AI service encountered an error during streaming.",
                correlationLogId);

            _logger.LogError(ex,
                "WorkingDocumentHandler ({Correlation}) EditWorkingDocument failed — operationId={OperationId}, tokensEmitted={TokensEmitted}, errorType={ErrorType}",
                correlationLogId, operationId, tokenIndex, ex.GetType().Name);

            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Edit failed. Operation: {operationId}. Error: {ex.GetType().Name}. " +
                $"Partial content ({tokenIndex} tokens) may have been emitted.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // AppendSection — streams a heading + LLM-generated body content
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteAppendSectionAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        Func<DocumentStreamEvent, CancellationToken, Task> streamWriter,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var args = ParseChatArgs(context.ToolArgumentsJson);
        if (string.IsNullOrWhiteSpace(args.SectionTitle) || string.IsNullOrWhiteSpace(args.Instruction))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "sectionTitle and instruction are required for the AppendSection method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var operationId = Guid.NewGuid();
        var tokenIndex = 0;
        var contentBuilder = new StringBuilder();

        // ADR-015: log lengths + section title — section title is a deterministic structural
        // label (not user content body) and is preserved per legacy WorkingDocumentTools.
        _logger.LogInformation(
            "WorkingDocumentHandler ({Correlation}) AppendSection starting — operationId={OperationId}, sectionTitle={SectionTitle}, instructionLen={InstructionLen}",
            correlationLogId, operationId, args.SectionTitle, args.Instruction!.Length);

        await streamWriter(
            new DocumentStreamStartEvent(operationId, TargetPosition: "end", OperationType: "insert"),
            cancellationToken);

        try
        {
            // Append context is OPTIONAL — append can proceed even if no current document exists.
            var documentContent = await FetchWorkingDocumentContentAsync(context, operationId, correlationLogId, cancellationToken);
            _logger.LogDebug(
                "WorkingDocumentHandler ({Correlation}) AppendSection fetched document — operationId={OperationId}, contentLen={ContentLen}",
                correlationLogId, operationId, documentContent?.Length ?? 0);

            // Emit the section heading as the first token so it renders immediately.
            var heading = $"## {args.SectionTitle}\n\n";
            contentBuilder.Append(heading);
            await streamWriter(
                new DocumentStreamTokenEvent(operationId, Token: heading, Index: tokenIndex),
                cancellationToken);
            tokenIndex++;

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
                    $"Write the content for a new section titled \"{args.SectionTitle}\" based on the following instruction: {args.Instruction}")
            };

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
            {
                var tokenText = update.Text;
                if (!string.IsNullOrEmpty(tokenText))
                {
                    contentBuilder.Append(tokenText);
                    await streamWriter(
                        new DocumentStreamTokenEvent(operationId, Token: tokenText, Index: tokenIndex),
                        cancellationToken);
                    tokenIndex++;
                }
            }

            var contentHash = ComputeContentHash(contentBuilder.ToString());

            await streamWriter(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex, ContentHash: contentHash),
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) AppendSection completed — operationId={OperationId}, sectionTitle={SectionTitle}, totalTokens={TotalTokens} in {Duration}ms",
                correlationLogId, operationId, args.SectionTitle, tokenIndex, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodAppendSection,
                    OperationId = operationId,
                    SectionTitle = args.SectionTitle,
                    TotalTokens = tokenIndex,
                    ContentHash = contentHash
                },
                summary: $"New section \"{args.SectionTitle}\" appended to working document. " +
                         $"OperationId={operationId}. Streamed {tokenIndex} tokens.",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 1
                });
        }
        catch (OperationCanceledException)
        {
            await EmitCancelledEndEventAsync(streamWriter, operationId, tokenIndex, correlationLogId);

            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) AppendSection cancelled — operationId={OperationId}, sectionTitle={SectionTitle}, tokensEmitted={TokensEmitted}",
                correlationLogId, operationId, args.SectionTitle, tokenIndex);

            stopwatch.Stop();
            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodAppendSection,
                    OperationId = operationId,
                    SectionTitle = args.SectionTitle,
                    Cancelled = true,
                    TotalTokens = tokenIndex
                },
                summary: $"Append operation cancelled for section \"{args.SectionTitle}\". " +
                         $"OperationId={operationId}. Partial content preserved ({tokenIndex} tokens).",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            await EmitErrorEndEventAsync(
                streamWriter, operationId, tokenIndex,
                "LLM_STREAM_FAILED", "Section generation failed. The AI service encountered an error during streaming.",
                correlationLogId);

            _logger.LogError(ex,
                "WorkingDocumentHandler ({Correlation}) AppendSection failed — operationId={OperationId}, sectionTitle={SectionTitle}, tokensEmitted={TokensEmitted}, errorType={ErrorType}",
                correlationLogId, operationId, args.SectionTitle, tokenIndex, ex.GetType().Name);

            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Append failed for section \"{args.SectionTitle}\". Operation: {operationId}. " +
                $"Error: {ex.GetType().Name}. Partial content ({tokenIndex} tokens) may have been emitted.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // WriteBackToWorkingDocument — persists supplied content to Dataverse
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteWriteBackAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        Func<DocumentStreamEvent, CancellationToken, Task> streamWriter,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var args = ParseChatArgs(context.ToolArgumentsJson);
        if (string.IsNullOrWhiteSpace(args.Content))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "content is required for the WriteBackToWorkingDocument method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var analysisId = ResolveAnalysisId(context);
        if (analysisId is null || analysisId == Guid.Empty)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "WorkingDocumentHandler ({Correlation}) WriteBackToWorkingDocument: no analysisId on context — cannot write back",
                correlationLogId);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodWriteBackToWorkingDocument,
                    Message = "Write-back failed: no analysis context is available for this chat session. " +
                              "The user must open SprkChat from within an active analysis to enable write-back."
                },
                summary: "Write-back deferred — no analysis context on chat session.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var operationId = Guid.NewGuid();
        var tokenIndex = 0;
        var content = args.Content!;

        // ADR-015: log content LENGTH only — never the content text.
        _logger.LogInformation(
            "WorkingDocumentHandler ({Correlation}) WriteBackToWorkingDocument starting — operationId={OperationId}, analysisId={AnalysisId}, contentLen={ContentLen}",
            correlationLogId, operationId, analysisId, content.Length);

        await streamWriter(
            new DocumentStreamStartEvent(operationId, TargetPosition: "document", OperationType: "replace"),
            cancellationToken);

        try
        {
            // R2-023 + ADR-033: stream the write-back content as document_stream_token events
            // so the client can render progress. Chunk size matches the legacy
            // WorkingDocumentTools to preserve rendering cadence.
            for (var offset = 0; offset < content.Length; offset += WriteBackChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = content.Substring(offset, Math.Min(WriteBackChunkSize, content.Length - offset));
                await streamWriter(
                    new DocumentStreamTokenEvent(operationId, Token: chunk, Index: tokenIndex),
                    cancellationToken);
                tokenIndex++;
            }

            var contentHash = ComputeContentHash(content);

            await streamWriter(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokenIndex, ContentHash: contentHash),
                cancellationToken);

            // SAFETY (spec FR-12): the ONLY write operation in this method.
            // Routes through IWorkingDocumentService → IGenericEntityService (Dataverse SDK).
            // Targets sprk_analysisoutput.sprk_workingdocument ONLY.
            // NEVER touches SpeFileStore, GraphServiceClient, or any SharePoint Embedded write.
            await _workingDocumentService.UpdateWorkingDocumentAsync(
                analysisId.Value,
                content,
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) WriteBackToWorkingDocument completed — operationId={OperationId}, analysisId={AnalysisId}, contentLen={ContentLen}, totalTokens={TotalTokens} in {Duration}ms",
                correlationLogId, operationId, analysisId, content.Length, tokenIndex, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodWriteBackToWorkingDocument,
                    OperationId = operationId,
                    AnalysisId = analysisId,
                    TotalTokens = tokenIndex,
                    ContentHash = contentHash,
                    ContentLength = content.Length
                },
                summary: $"Working document written back to Dataverse successfully. " +
                         $"Target: sprk_analysisoutput.sprk_workingdocument (analysisId={analysisId}). " +
                         $"Content length: {content.Length} characters. OperationId={operationId}. " +
                         $"Streamed {tokenIndex} tokens. Content hash: {contentHash}.",
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
            await EmitCancelledEndEventAsync(streamWriter, operationId, tokenIndex, correlationLogId);

            _logger.LogInformation(
                "WorkingDocumentHandler ({Correlation}) WriteBackToWorkingDocument cancelled — operationId={OperationId}, analysisId={AnalysisId}",
                correlationLogId, operationId, analysisId);

            stopwatch.Stop();
            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new WorkingDocumentPayload
                {
                    Method = MethodWriteBackToWorkingDocument,
                    OperationId = operationId,
                    AnalysisId = analysisId,
                    Cancelled = true,
                    TotalTokens = tokenIndex
                },
                summary: $"Write-back operation cancelled for analysisId={analysisId}. " +
                         $"OperationId={operationId}. The working document was not updated in Dataverse.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            await EmitErrorEndEventAsync(
                streamWriter, operationId, tokenIndex,
                "WRITE_BACK_FAILED", "Write-back to Dataverse failed. The working document was not updated.",
                correlationLogId);

            _logger.LogError(ex,
                "WorkingDocumentHandler ({Correlation}) WriteBackToWorkingDocument failed — operationId={OperationId}, analysisId={AnalysisId}, errorType={ErrorType}",
                correlationLogId, operationId, analysisId, ex.GetType().Name);

            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Write-back failed for analysisId={analysisId}. OperationId={operationId}. " +
                $"Error: {ex.GetType().Name}. The working document was not updated in Dataverse. " +
                $"Please retry or contact support if the issue persists.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the current working document content from the analysis orchestration service.
    /// Returns null when no analysis context is available on the chat session or the analysis
    /// has no working document. NEVER throws — KeyNotFoundException is converted to null.
    /// </summary>
    private async Task<string?> FetchWorkingDocumentContentAsync(
        ChatInvocationContext context,
        Guid operationId,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var analysisId = ResolveAnalysisId(context);
        if (analysisId is null || analysisId == Guid.Empty)
        {
            _logger.LogDebug(
                "WorkingDocumentHandler ({Correlation}) no analysisId on context — skipping working document fetch. operationId={OperationId}",
                correlationLogId, operationId);
            return null;
        }

        try
        {
            var analysisDetail = await _analysisService.GetAnalysisAsync(analysisId.Value, cancellationToken);
            return analysisDetail.WorkingDocument ?? analysisDetail.FinalOutput;
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning(
                "WorkingDocumentHandler ({Correlation}) Analysis {AnalysisId} not found when fetching working document. operationId={OperationId}",
                correlationLogId, analysisId, operationId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the active <c>sprk_analysisoutput</c> record GUID from the chat invocation
    /// context. Reads <see cref="ChatInvocationContext.AnalysisId"/> when available; returns
    /// null when the chat session is not bound to an analysis (standalone chat, background
    /// processing, replay).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>R6 Wave 9 dispatch note</strong>: this method assumes <c>ChatInvocationContext</c>
    /// carries an <c>AnalysisId</c> field. If the field has not yet been added by the parallel
    /// Stage 2 work (ADR-033 §4.1 + the analysisId addition surfaced by Stage 3), the build
    /// will fail with a clean compile error and the main session resolves the design gap in
    /// Stage 4. See the Wave 9 bookkeeping note for the full surfacing.
    /// </para>
    /// </remarks>
    private static Guid? ResolveAnalysisId(ChatInvocationContext context)
    {
        return context.AnalysisId;
    }

    private async Task EmitCancelledEndEventAsync(
        Func<DocumentStreamEvent, CancellationToken, Task> streamWriter,
        Guid operationId,
        int tokensEmitted,
        string correlationLogId)
    {
        try
        {
            // Use CancellationToken.None — the original token may be cancelled but the terminal
            // End event MUST be delivered so the editor finalizes UI state (ADR-019).
            await streamWriter(
                new DocumentStreamEndEvent(operationId, Cancelled: true, TotalTokens: tokensEmitted),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort — if the SSE connection is already closed, log and move on.
            _logger.LogWarning(ex,
                "WorkingDocumentHandler ({Correlation}) failed to emit cancelled end event — operationId={OperationId}",
                correlationLogId, operationId);
        }
    }

    private async Task EmitErrorEndEventAsync(
        Func<DocumentStreamEvent, CancellationToken, Task> streamWriter,
        Guid operationId,
        int tokensEmitted,
        string errorCode,
        string errorMessage,
        string correlationLogId)
    {
        try
        {
            // Use CancellationToken.None — the original token may be cancelled but the terminal
            // End event MUST be delivered so the editor finalizes UI state (ADR-019).
            await streamWriter(
                new DocumentStreamEndEvent(operationId, Cancelled: false, TotalTokens: tokensEmitted,
                    ErrorCode: errorCode, ErrorMessage: errorMessage),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort — if the SSE connection is already closed, log and move on.
            _logger.LogWarning(ex,
                "WorkingDocumentHandler ({Correlation}) failed to emit error end event — operationId={OperationId}, errorCode={ErrorCode}",
                correlationLogId, operationId, errorCode);
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the content, prefixed with "sha256:" for the
    /// <see cref="DocumentStreamEndEvent.ContentHash"/> field (R2-023 + ADR-014). Enables the
    /// client to verify that the reconstructed document content matches what the BFF computed.
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool HasNonEmptyString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind != JsonValueKind.String)
            return false;

        return !string.IsNullOrWhiteSpace(prop.GetString());
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static WorkingDocumentArgs ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return default;

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return default;

            var root = doc.RootElement;

            return new WorkingDocumentArgs
            {
                Instruction = TryReadString(root, "instruction"),
                SectionTitle = TryReadString(root, "sectionTitle"),
                Content = TryReadString(root, "content")
            };
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Read the <c>method</c> discriminator from the tool's configuration JSON. Defaults to
    /// <see cref="MethodEditWorkingDocument"/> when missing (the least-destructive method —
    /// streams a preview the user can review without persisting). <see cref="ValidateChat"/>
    /// surfaces unsupported methods with a clear error.
    /// </summary>
    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return MethodEditWorkingDocument;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return MethodEditWorkingDocument;

            if (doc.RootElement.TryGetProperty("method", out var methodProp)
                && methodProp.ValueKind == JsonValueKind.String)
            {
                var v = methodProp.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    return v!;
            }
        }
        catch (JsonException)
        {
            // Fall through to default
        }

        return MethodEditWorkingDocument;
    }

    private ToolResult BuildUnsupportedMethodResult(AnalysisTool tool, string method, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"WorkingDocumentHandler does not support method '{method}'. " +
            $"Use 'EditWorkingDocument', 'AppendSection', or 'WriteBackToWorkingDocument'.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "WorkingDocument invocation was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"WorkingDocument invocation failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsed chat-call arguments. All three fields are nullable because the three methods use
    /// disjoint argument subsets (Edit + Append use instruction; Append also uses sectionTitle;
    /// WriteBack uses content).
    /// </summary>
    private readonly record struct WorkingDocumentArgs
    {
        public string? Instruction { get; init; }
        public string? SectionTitle { get; init; }
        public string? Content { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the method
    /// discriminator + operation correlation IDs + counts / flags for telemetry attribution.
    /// ADR-015 binding: NEVER carries token content, prompt content, or document content text.
    /// </summary>
    public sealed class WorkingDocumentPayload
    {
        /// <summary>Method that produced this payload — echo of the row's discriminator.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodEditWorkingDocument;

        /// <summary>Operation correlation GUID — matches the DocumentStreamEvent.OperationId values.</summary>
        [JsonPropertyName("operationId")]
        public Guid? OperationId { get; set; }

        /// <summary>Analysis record GUID — populated for WriteBack only.</summary>
        [JsonPropertyName("analysisId")]
        public Guid? AnalysisId { get; set; }

        /// <summary>Section title — populated for AppendSection only (deterministic structural label).</summary>
        [JsonPropertyName("sectionTitle")]
        public string? SectionTitle { get; set; }

        /// <summary>Total tokens emitted during the operation (counts only, not content).</summary>
        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        /// <summary>SHA-256 content hash (prefixed "sha256:") — ADR-014 cacheable artifact.</summary>
        [JsonPropertyName("contentHash")]
        public string? ContentHash { get; set; }

        /// <summary>Persisted content length in characters — populated for WriteBack only (length only, not content).</summary>
        [JsonPropertyName("contentLength")]
        public int? ContentLength { get; set; }

        /// <summary>True when the operation was cancelled before completion.</summary>
        [JsonPropertyName("cancelled")]
        public bool Cancelled { get; set; }

        /// <summary>Human-readable status message — populated for skip paths (no document, no analysis).</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
