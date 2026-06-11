using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OpenAI.Chat;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Streaming;
using Sprk.Bff.Api.Telemetry;

// Disambiguate ChatMessage — the OpenAI SDK type (OpenAI.Chat.ChatMessage) is what
// IOpenAiClient.StreamStructuredCompletionAsync consumes. Aliased so the engine's
// chat-summarize implementation is unambiguous at the call site.
using OaiChatMessage = OpenAI.Chat.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Unified playbook execution engine supporting both batch and conversational modes,
/// plus the chat-session Summarize path added by R6 Pillar 4 (D-A-17).
/// Implements ADR-013 AI Architecture with dual execution paths.
/// </summary>
/// <remarks>
/// This engine coordinates:
/// <list type="bullet">
/// <item><b>Batch mode</b>: Delegates to IPlaybookOrchestrationService for document analysis</item>
/// <item><b>Conversational mode</b>: Uses IAiPlaybookBuilderService for multi-turn interactions</item>
/// <item><b>Chat-session Summarize</b>: Resolves playbook → node → action FK chain (post-R6
///       task 024 valid) and runs session-scoped RAG + Structured Outputs streaming with
///       per-field deltas. Replaces the R5 <c>SessionSummarizeOrchestrator.LoadActionConfigAsync</c>
///       alternate-key bypass.</item>
/// </list>
/// </remarks>
public class PlaybookExecutionEngine : IPlaybookExecutionEngine
{
    /// <summary>Schema name passed to Azure OpenAI Structured Outputs (informational; observability).</summary>
    internal const string ChatSummarizeSchemaName = "DocumentAnalysisResult";

    /// <summary>Deterministic combined-summary chat interjection emitted before multi-file streams (FR-04).</summary>
    internal const string CombinedSummaryInterjection =
        "I'll provide a combined summary for the files you uploaded.";

    /// <summary>Logical-name of the action entity used for portable-code lookup.</summary>
    internal const string ActionEntityLogicalName = "sprk_analysisaction";

    /// <summary>JSON options used for final-result deserialization (camelCase Web defaults).</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAiPlaybookBuilderService _builderService;
    private readonly IPlaybookOrchestrationService _orchestrationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INodeService _nodeService;
    private readonly IGenericEntityService _entityService;
    private readonly IRagService _ragService;
    private readonly IOpenAiClient _openAiClient;
    private readonly R5SummarizeTelemetry _summarizeTelemetry;
    private readonly ILogger<PlaybookExecutionEngine> _logger;

    public PlaybookExecutionEngine(
        IAiPlaybookBuilderService builderService,
        IPlaybookOrchestrationService orchestrationService,
        IHttpContextAccessor httpContextAccessor,
        INodeService nodeService,
        IGenericEntityService entityService,
        IRagService ragService,
        IOpenAiClient openAiClient,
        R5SummarizeTelemetry summarizeTelemetry,
        ILogger<PlaybookExecutionEngine> logger)
    {
        _builderService = builderService ?? throw new ArgumentNullException(nameof(builderService));
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _summarizeTelemetry = summarizeTelemetry ?? throw new ArgumentNullException(nameof(summarizeTelemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BuilderResult> ExecuteConversationalAsync(
        ConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.CurrentMessage);
        ArgumentNullException.ThrowIfNull(context.SessionState);

        _logger.LogInformation(
            "Starting conversational execution. SessionId: {SessionId}, MessageLength: {Length}",
            context.SessionState.SessionId,
            context.CurrentMessage.Length);

        // Emit thinking indicator
        yield return BuilderResult.Thinking("Processing your request...");

        // Convert conversation context to builder request
        var builderRequest = ConvertToBuilderRequest(context);

        // Process through builder service and convert results
        await foreach (var chunk in _builderService.ProcessMessageAsync(builderRequest, cancellationToken))
        {
            var result = ConvertToBuilderResult(chunk);
            if (result != null)
            {
                yield return result;
            }
        }

        // Update session state if needed
        var updatedState = context.SessionState with
        {
            LastActiveAt = DateTimeOffset.UtcNow
        };
        yield return BuilderResult.StateUpdate(updatedState);

        _logger.LogInformation(
            "Conversational execution completed. SessionId: {SessionId}",
            context.SessionState.SessionId);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PlaybookStreamEvent> ExecuteBatchAsync(
        PlaybookRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Starting batch execution. PlaybookId: {PlaybookId}, DocumentCount: {Count}",
            request.PlaybookId,
            request.DocumentIds.Length);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for batch execution.");

        // Delegate to orchestration service
        await foreach (var streamEvent in _orchestrationService.ExecuteAsync(
            request, httpContext, cancellationToken))
        {
            yield return streamEvent;
        }

        _logger.LogInformation(
            "Batch execution completed. PlaybookId: {PlaybookId}",
            request.PlaybookId);
    }

    /// <inheritdoc />
    public ExecutionMode DetermineExecutionMode(
        Guid playbookId,
        bool hasCanvasState,
        bool hasDocuments)
    {
        // Conversational mode: When builder is providing canvas state
        // Batch mode: When documents are provided for analysis
        if (hasCanvasState && !hasDocuments)
        {
            _logger.LogDebug(
                "Determined execution mode: Conversational for playbook {PlaybookId}",
                playbookId);
            return ExecutionMode.Conversational;
        }

        if (hasDocuments)
        {
            _logger.LogDebug(
                "Determined execution mode: Batch for playbook {PlaybookId}",
                playbookId);
            return ExecutionMode.Batch;
        }

        // Default to conversational if neither is provided (interactive mode)
        _logger.LogDebug(
            "Determined execution mode: Conversational (default) for playbook {PlaybookId}",
            playbookId);
        return ExecutionMode.Conversational;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisChunk> ExecuteChatSummarizeAsync(
        Guid playbookId,
        ChatSummarizeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");
        if (playbookId == Guid.Empty)
        {
            throw new ArgumentException("playbookId must not be Guid.Empty.", nameof(playbookId));
        }

        // NFR-02 — hard cap (defense-in-depth; ChatSession also enforces upstream).
        if (request.FileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Chat Summarize request exceeds the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {request.FileIds.Count} file IDs.",
                nameof(request));
        }

        var stopwatch = Stopwatch.StartNew();
        // Manual Activity lifecycle (no `using` so we don't introduce a try-finally around the
        // iterator body — that would conflict with `yield return` per the C# language spec).
        // Activity disposes itself when its outer scope ends; explicit Dispose at end is best-effort.
        var activity = _summarizeTelemetry.StartActivity(
            "R6.ChatSummarize.OrchestratePlaybook", request.TenantId, request.CorrelationId);
        activity?.SetTag("path", request.Path.ToTelemetryValue());
        activity?.SetTag("playbookId", playbookId.ToString());

        var completionStatus = "failed";
        long fileCountForTelemetry = 0;
        long totalTokens = 0;

        var uploadedFiles = request.UploadedFiles ?? Array.Empty<ChatSessionFile>();
        var fileIds = ResolveFileIds(request.FileIds, uploadedFiles);
        fileCountForTelemetry = fileIds.Count;

        if (fileIds.Count == 0)
        {
            _logger.LogInformation(
                "R6 ChatSummarize: no files in session {SessionId} (tenant={TenantId}) — emitting decline.",
                request.SessionId, request.TenantId);
            completionStatus = "declined";
            yield return AnalysisChunk.FromError(
                "No files are available in this chat session to summarize. Upload one or more files first.");
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // FR-04 — multi-file combined-summary interjection emitted BEFORE the playbook streams.
        if (fileIds.Count >= 2)
        {
            yield return AnalysisChunk.FromContent(CombinedSummaryInterjection);
        }

        // Resolve action config through the playbook → node → action FK chain (R6 Pillar 4 / D-A-17).
        // NO alternate-key lookup — task 024 wired the FK chain so the action ID is reachable from
        // INodeService.GetNodesAsync(playbookId) on the playbook's single AI node.
        //
        // C# does not allow `yield return` inside a `catch` block; we hoist the error message out
        // and yield AFTER the try-catch to keep the IAsyncEnumerable contract intact.
        ChatSummarizeActionConfig? actionConfig = null;
        string? earlyError = null;
        try
        {
            actionConfig = await ResolveActionConfigViaFkChainAsync(playbookId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "R6 ChatSummarize: failed to resolve action via FK chain for playbook {PlaybookId} " +
                "(tenant={TenantId} session={SessionId}).",
                playbookId, request.TenantId, request.SessionId);
            earlyError = $"Summarize action configuration is unavailable: {ex.Message}";
        }
        if (earlyError is not null)
        {
            yield return AnalysisChunk.FromError(earlyError);
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // Session-scoped RAG retrieval (ADR-014 — tenant + session filters BOTH set).
        RagSearchResponse? ragResponse = null;
        try
        {
            var searchQuery = BuildRagQuery(uploadedFiles, fileIds, request.StyleHint);
            var searchOptions = new RagSearchOptions
            {
                TenantId = request.TenantId,
                SessionId = request.SessionId,
                TopK = Math.Min(20, fileIds.Count * 4), // ~4 chunks per file (best-effort); session slice is small
                MinScore = 0.0f                          // session slice is small + curated; no score threshold
            };
            ragResponse = await _ragService.SearchAsync(searchQuery, searchOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "R6 ChatSummarize: RAG retrieval failed (tenant={TenantId} session={SessionId} fileCount={FileCount}).",
                request.TenantId, request.SessionId, fileIds.Count);
            earlyError = $"Failed to retrieve session content for summarization: {ex.Message}";
        }
        if (earlyError is not null)
        {
            yield return AnalysisChunk.FromError(earlyError);
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // Build the chat messages: system prompt from action; user content composed from RAG hits.
        var userContent = BuildUserContent(uploadedFiles, fileIds, ragResponse!, request.StyleHint);
        var messages = new List<OaiChatMessage>
        {
            new SystemChatMessage(actionConfig!.SystemPrompt),
            new UserChatMessage(userContent)
        };

        // Token-counting estimation for telemetry (4-char heuristic, consistent with AnalysisOrchestrationService).
        var inputTokens = EstimateTokens(actionConfig.SystemPrompt) + EstimateTokens(userContent);
        var jsonSchemaBinaryData = BinaryData.FromString(actionConfig.OutputSchemaJson);

        // Stream structured outputs token-by-token; feed every token to the incremental parser.
        var parser = new IncrementalJsonParser();
        IAsyncEnumerator<string>? tokenEnumerator = null;

        try
        {
            tokenEnumerator = _openAiClient
                .StreamStructuredCompletionAsync(messages, jsonSchemaBinaryData, ChatSummarizeSchemaName, model: null, maxOutputTokens: null, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex,
                "R6 ChatSummarize: OpenAI circuit broken (tenant={TenantId} session={SessionId}).",
                request.TenantId, request.SessionId);
            earlyError = $"AI service is temporarily unavailable. Retry in ~{ex.RetryAfter.TotalSeconds:F0}s.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "R6 ChatSummarize: failed to start streaming (tenant={TenantId} session={SessionId}).",
                request.TenantId, request.SessionId);
            earlyError = $"Failed to start summarization stream: {ex.Message}";
        }
        if (earlyError is not null || tokenEnumerator is null)
        {
            yield return AnalysisChunk.FromError(earlyError ?? "Failed to start summarization stream.");
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // Iterate the token stream. Per-token try/catch keeps mid-stream exceptions graceful
        // (yields FromError + terminates) without aborting the IAsyncEnumerable contract.
        //
        // C# constraint: `yield return` is NOT allowed inside a `try` block that has a `finally`
        // clause (or `catch` clause). So we manually manage the enumerator's lifecycle:
        // (1) advance + parse via a try-catch that does NOT contain any yield;
        // (2) emit deltas with yield AFTER the catch returns;
        // (3) dispose the enumerator at the end via a non-iterator helper.
        string? midStreamError = null;
        var deltaBatch = new List<AnalysisChunk>(capacity: 8);

        while (true)
        {
            bool moveNextOk;
            string? token;
            (moveNextOk, token, midStreamError) = await TryAdvanceAsync(
                tokenEnumerator!, request, _logger).ConfigureAwait(false);
            if (midStreamError is not null)
            {
                break;
            }
            if (!moveNextOk)
            {
                break;
            }
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            // Parser.Append is synchronous + does NOT throw on partial JSON (parser tolerance is
            // the load-bearing invariant from R5 task 006). Safe to call outside try-catch.
            IReadOnlyList<FieldDeltaEvent> parserEvents = parser.Append(token);
            deltaBatch.Clear();
            foreach (var ev in parserEvents)
            {
                if (ev.Kind == FieldDeltaEventKind.FieldContent && !string.IsNullOrEmpty(ev.Content))
                {
                    deltaBatch.Add(AnalysisChunk.FromDelta(ev.Path, ev.Content, ev.Sequence));
                }
                // FieldStart / FieldComplete are state-only signals (Content is empty); the engine
                // does NOT surface them as SSE events per the AnalysisChunk envelope contract
                // (FieldDelta carries only FieldContent semantics today).
            }
            foreach (var deltaChunk in deltaBatch)
            {
                yield return deltaChunk;
            }
        }

        // Dispose the enumerator. Helper isolates the await DisposeAsync (which would otherwise
        // require an `await using` block, which itself would create a try-finally that conflicts
        // with `yield return` semantics).
        await DisposeEnumeratorAsync(tokenEnumerator!).ConfigureAwait(false);

        if (midStreamError is not null)
        {
            completionStatus = "failed";
            yield return AnalysisChunk.FromError(midStreamError);
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // Final-result deserialization (or fallback) — terminal Completed chunk.
        var accumulatedJson = parser.GetAccumulatedJson();
        var finalResult = parser.TryParseFinal(s_jsonOptions)
            ?? DocumentAnalysisResult.Fallback(accumulatedJson);

        // Update token estimates for telemetry.
        totalTokens = inputTokens + EstimateTokens(accumulatedJson);
        completionStatus = finalResult.ParsedSuccessfully ? "success" : "failed";

        yield return AnalysisChunk.Completed(finalResult);

        RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "R6 ChatSummarize completed: path={Path} status={Status} fileCount={FileCount} tokens={Tokens} latencyMs={LatencyMs} tenant={TenantId} session={SessionId} playbookId={PlaybookId}",
            request.Path.ToTelemetryValue(), completionStatus, fileCountForTelemetry, totalTokens,
            stopwatch.Elapsed.TotalMilliseconds, request.TenantId, request.SessionId, playbookId);

        activity?.Dispose();
    }

    // ─── Chat-Summarize helpers (R6 Pillar 4 / D-A-17) ─────────────────────────────

    /// <summary>
    /// Resolves the action seed (system prompt + output schema) for the chat-Summarize
    /// playbook via the playbook → node → action FK chain (post-R6 task 024).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FR-26 invariant: this method MUST NOT use <c>RetrieveByAlternateKeyAsync</c> on
    /// <c>sprk_actioncode</c>. The chat-summarize playbook
    /// (<c>summarize-document-for-chat@v1</c>) has a single AI node whose
    /// <c>sprk_actionid</c> FK now resolves to the <c>SUM-CHAT@v1</c> action seed.
    /// </para>
    /// <para>
    /// The current playbook has exactly ONE node (verified by task 024 evidence note).
    /// If a future variant introduces multi-node chat-summarize playbooks, this resolver
    /// would need extension (e.g., first-AI-node selection). For now we assert single-node
    /// and surface a clear error if violated.
    /// </para>
    /// </remarks>
    private async Task<ChatSummarizeActionConfig> ResolveActionConfigViaFkChainAsync(
        Guid playbookId,
        CancellationToken ct)
    {
        var nodes = await _nodeService.GetNodesAsync(playbookId, ct).ConfigureAwait(false);
        if (nodes is null || nodes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Playbook '{playbookId}' has no nodes; the chat-summarize FK chain is broken " +
                $"(expected single AI node → action seed). Re-run R6 task 024 deployment.");
        }

        // The chat-summarize playbook has a single AI node. If multi-node variants emerge later,
        // adjust to select the first AI-type node by NodeType filter.
        var node = nodes[0];
        if (node.ActionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Playbook '{playbookId}' node '{node.Id}' has empty ActionId — FK chain " +
                $"playbook → node → action is broken. Re-run R6 task 024 deployment.");
        }

        var columns = new[]
        {
            "sprk_analysisactionid",
            "sprk_name",
            "sprk_actioncode",
            "sprk_systemprompt",
            "sprk_outputschemajson"
        };

        var entity = await _entityService
            .RetrieveAsync(ActionEntityLogicalName, node.ActionId, columns, ct)
            .ConfigureAwait(false);

        var systemPrompt = entity.GetAttributeValue<string>("sprk_systemprompt");
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new InvalidOperationException(
                $"Action '{node.ActionId}' has no sprk_systemprompt — re-run action seed deployment.");
        }
        var outputSchema = entity.GetAttributeValue<string>("sprk_outputschemajson");
        if (string.IsNullOrWhiteSpace(outputSchema))
        {
            throw new InvalidOperationException(
                $"Action '{node.ActionId}' has no sprk_outputschemajson — re-run action seed deployment.");
        }

        return new ChatSummarizeActionConfig(
            ActionId: node.ActionId,
            SystemPrompt: systemPrompt,
            OutputSchemaJson: outputSchema);
    }

    private static IReadOnlyList<string> ResolveFileIds(
        IReadOnlyList<string>? requestFileIds,
        IReadOnlyList<ChatSessionFile> uploadedFiles)
    {
        if (requestFileIds is { Count: > 0 })
        {
            var manifestIds = new HashSet<string>(uploadedFiles.Select(f => f.FileId), StringComparer.Ordinal);
            return requestFileIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && manifestIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        return uploadedFiles.Select(f => f.FileId).ToList();
    }

    private static string BuildRagQuery(
        IReadOnlyList<ChatSessionFile> uploadedFiles,
        IReadOnlyList<string> fileIds,
        string? styleHint)
    {
        var filteredNames = uploadedFiles
            .Where(f => fileIds.Contains(f.FileId, StringComparer.Ordinal))
            .Select(f => f.FileName)
            .ToList();
        var nameClause = filteredNames.Count > 0
            ? string.Join(", ", filteredNames)
            : "session files";
        var styleClause = string.IsNullOrWhiteSpace(styleHint) ? string.Empty : $" Style hint: {styleHint}.";
        return $"Summarize content from: {nameClause}.{styleClause}";
    }

    private static string BuildUserContent(
        IReadOnlyList<ChatSessionFile> uploadedFiles,
        IReadOnlyList<string> fileIds,
        RagSearchResponse ragResponse,
        string? styleHint)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(styleHint))
        {
            sb.AppendLine($"Style hint: {styleHint}").AppendLine();
        }

        sb.AppendLine("Files in this chat session:");
        foreach (var file in uploadedFiles.Where(f => fileIds.Contains(f.FileId, StringComparer.Ordinal)))
        {
            sb.AppendLine($"- {file.FileName} ({file.ContentType}, {file.SizeBytes} bytes)");
        }
        sb.AppendLine();

        sb.AppendLine("Content chunks (most relevant first):");
        if (ragResponse.Results.Count == 0)
        {
            sb.AppendLine("(No content available for the requested files.)");
        }
        else
        {
            var idx = 1;
            foreach (var hit in ragResponse.Results)
            {
                sb.AppendLine($"[{idx}] From {hit.DocumentName}:");
                sb.AppendLine(hit.Content);
                sb.AppendLine();
                idx++;
            }
        }

        return sb.ToString();
    }

    private void RecordTelemetry(
        ChatSummarizeRequest request,
        string completionStatus,
        long fileCount,
        long totalTokens,
        double latencyMs)
    {
        try
        {
            _summarizeTelemetry.RecordSummarizeInvocation(
                path: request.Path.ToTelemetryValue(),
                completionStatus: completionStatus,
                fileCount: fileCount,
                totalTokens: totalTokens,
                latencyMs: latencyMs,
                tenantId: request.TenantId);
        }
        catch (ArgumentException ex)
        {
            // Defensive: out-of-enum input would only happen if the path mapping fell out of
            // sync with R5SummarizeTelemetry.ValidPaths. Log loudly so the bug is visible in
            // dev/integration; do NOT propagate (we are in the cleanup tail of a stream).
            _logger.LogError(ex,
                "R6 ChatSummarize: telemetry cardinality guard rejected path={Path} status={Status} — coding error.",
                request.Path.ToTelemetryValue(), completionStatus);
        }
    }

    private static long EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length / 4;

    /// <summary>
    /// Advance the token enumerator once; isolate the try-catch from the iterator body so the
    /// engine's body itself can keep `yield return` legal (C# disallows `yield return`
    /// inside try-catch / try-finally blocks).
    /// </summary>
    /// <returns>
    /// Tuple of (moveNextOk, token, errorMessage). On success: (true, token, null). On clean
    /// stream-end: (false, null, null). On exception: (false, null, errorMessage). On
    /// cancellation: re-throws OperationCanceledException (the caller's try-catch propagates).
    /// </returns>
    private static async Task<(bool MoveNextOk, string? Token, string? Error)> TryAdvanceAsync(
        IAsyncEnumerator<string> enumerator,
        ChatSummarizeRequest request,
        ILogger logger)
    {
        try
        {
            var ok = await enumerator.MoveNextAsync().ConfigureAwait(false);
            return (ok, ok ? enumerator.Current : null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "R6 ChatSummarize: mid-stream exception (tenant={TenantId} session={SessionId}).",
                request.TenantId, request.SessionId);
            return (false, null, $"Summarization stream interrupted: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispose the token enumerator quietly. Disposal errors are logged but do not affect the
    /// terminal chunk emission (the caller already received the meaningful chunks; disposal
    /// failure is a cleanup-tail concern only).
    /// </summary>
    private async Task DisposeEnumeratorAsync(IAsyncEnumerator<string> enumerator)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "R6 ChatSummarize: enumerator disposal failed (cleanup-tail; non-fatal).");
        }
    }

    /// <summary>
    /// Convert ConversationContext to BuilderRequest for the builder service.
    /// </summary>
    private static BuilderRequest ConvertToBuilderRequest(ConversationContext context)
    {
        // Convert conversation history to chat message format
        var chatHistory = context.History
            .Select(h => new BuilderChatMessage
            {
                Role = h.Role.ToString().ToLowerInvariant(),
                Content = h.Content,
                Timestamp = h.Timestamp.DateTime
            })
            .ToArray();

        return new BuilderRequest
        {
            Message = context.CurrentMessage,
            CanvasState = context.SessionState.CanvasState,
            PlaybookId = context.PlaybookId,
            SessionId = context.SessionState.SessionId,
            ChatHistory = chatHistory.Length > 0 ? chatHistory : null
        };
    }

    /// <summary>
    /// Convert BuilderStreamChunk to BuilderResult.
    /// </summary>
    private static BuilderResult? ConvertToBuilderResult(BuilderStreamChunk chunk)
    {
        return chunk.Type switch
        {
            BuilderChunkType.Message => BuilderResult.Message(chunk.Text ?? string.Empty),

            BuilderChunkType.CanvasOperation when chunk.Patch != null =>
                BuilderResult.Operation(chunk.Patch),

            BuilderChunkType.Clarification =>
                BuilderResult.Clarification(chunk.Text ?? "Could you please clarify?"),

            BuilderChunkType.PlanPreview => null, // Plan preview handled separately

            BuilderChunkType.Complete => BuilderResult.Complete(),

            BuilderChunkType.Error =>
                BuilderResult.ErrorResult(chunk.Error ?? "An error occurred"),

            _ => null
        };
    }
}

/// <summary>
/// Action seed configuration resolved via the playbook → node → action FK chain
/// (R6 Pillar 4 / D-A-17). Replaces the R5 alternate-key-loaded
/// <c>SessionSummarizeActionConfig</c> with an FK-resolved equivalent.
/// </summary>
/// <param name="ActionId">Dataverse GUID of the action row (FK-resolved from playbook → node).</param>
/// <param name="SystemPrompt">JPS-formatted system prompt (the <c>sprk_systemprompt</c> field verbatim).</param>
/// <param name="OutputSchemaJson">Structured-Outputs JSON Schema (the <c>sprk_outputschemajson</c> field verbatim).</param>
internal sealed record ChatSummarizeActionConfig(
    Guid ActionId,
    string SystemPrompt,
    string OutputSchemaJson);

/// <summary>
/// Extension methods for PlaybookExecutionEngine registration.
/// </summary>
public static class PlaybookExecutionEngineExtensions
{
    /// <summary>
    /// Add PlaybookExecutionEngine and related services to the DI container.
    /// </summary>
    public static IServiceCollection AddPlaybookExecutionEngine(this IServiceCollection services)
    {
        services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>();
        return services;
    }
}
