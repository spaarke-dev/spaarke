using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using OpenAI.Chat;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Streaming;
using Sprk.Bff.Api.Telemetry;

// Disambiguate ChatMessage — SDK type (OpenAI.Chat.ChatMessage) is the one consumed by
// OpenAiClient.StreamStructuredCompletionAsync; Sprk.Bff.Api.Models.Ai.Chat.ChatMessage
// is the R3 internal type. Alias both so call sites can be explicit.
using OaiChatMessage = OpenAI.Chat.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Convergence orchestrator for the R5 chat-driven Summarize-for-Chat vertical slice. Bridges
/// the two valid entry points for chat-driven Summarize — the direct endpoint
/// (<c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>; task 014 / D2-04) AND the
/// natural-language agent-tool dispatch (<c>InvokeSummarizePlaybookTool</c>; task 015 / D2-05)
/// — by ensuring BOTH paths delegate to a SINGLE convergence method
/// (<see cref="SummarizeSessionFilesAsync"/>) on this class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a new class (per R5 CLAUDE.md §3.1 reuse-mandate step 3)</b>: this orchestrator is
/// NOT a parallel to <see cref="AnalysisOrchestrationService"/> or <c>InsightsOrchestrator</c>
/// — those orchestrate <i>document-record-scoped</i> analysis (with a Dataverse
/// <c>sprk_document</c> as the unit of work). The R5 Summarize-for-Chat flow is
/// <i>chat-session-scoped</i>: it operates over <see cref="ChatSession.UploadedFiles"/>
/// (task 004), the session-files AI Search slice (task 002), and a multi-file
/// combined-summary semantic that the existing orchestrators do not cover. Specifically:
/// </para>
/// <list type="number">
///   <item>Chat-session scope: a new orchestration shape (sessionId + fileIds subset; the
///         session-files index slice rather than the global knowledge index).</item>
///   <item>Multi-file combined-summary interjection (FR-04) is chat-layer logic, not
///         analysis-layer logic.</item>
///   <item>Agent-tool/endpoint convergence (FR-01 + FR-08 + SC-08) requires a single dedicated
///         delegation target.</item>
///   <item><see cref="AnalysisOrchestrationService"/> requires a <c>DocumentId</c> +
///         <c>HttpContext</c> for SPE OBO file download; the chat flow has neither — the file
///         text is already indexed in the session-files slice at upload time (task 003).</item>
/// </list>
/// <para>
/// <b>Reuse statement</b>: this class composes EXISTING platform primitives — it does NOT
/// re-implement orchestration logic. Specifically it consumes:
/// <see cref="ChatSessionManager"/> (task 004 — for the <see cref="ChatSession.UploadedFiles"/>
/// manifest), <see cref="IRagService"/> with <see cref="RagSearchOptions.SessionId"/> (task 002
/// — for session-scoped retrieval), <see cref="IOpenAiClient.StreamStructuredCompletionAsync"/>
/// (task 006 — for streaming Structured Outputs), <see cref="IncrementalJsonParser"/> (task 006
/// — for in-stream FieldDelta extraction), <see cref="AnalysisChunk.FromDelta"/> /
/// <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> / <see cref="AnalysisChunk.FromError"/>
/// (task 005 — for the SSE envelope), <see cref="DocumentAnalysisResult"/> (task 005 — for the
/// final output shape), the deployed action seed <c>SUM-CHAT@v1</c> (task 010) and playbook
/// <c>summarize-document-for-chat@v1</c> (task 011) — resolved via
/// <see cref="IGenericEntityService"/> by alternate key for <c>sprk_systemprompt</c> +
/// <c>sprk_outputschemajson</c>.
/// </para>
/// <para>
/// <b>Convergence contract (spec FR-01 + FR-08 + SC-08)</b>: <see cref="SummarizeSessionFilesAsync"/>
/// is the SINGLE public streaming entry point. Task 014's endpoint passes
/// <see cref="SummarizeInvocationPath.DirectEndpoint"/>; task 015's tool handler passes
/// <see cref="SummarizeInvocationPath.AgentTool"/>. Both paths consume the same
/// <see cref="IAsyncEnumerable{T}"/> of <see cref="AnalysisChunk"/> values; no alternate code
/// path is exposed.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>: concrete <c>sealed</c> class; NO interface authored by R5
/// (unit tests target the concrete type per ADR-010 — interface-for-testability-alone is
/// explicitly forbidden). Registration lives inside
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/> via the existing
/// <c>AddAnalysisOrchestrationServices</c> helper — ZERO new <c>Program.cs</c> lines, ZERO new
/// feature flags (R5 CLAUDE.md §3.2 + §3.3).
/// </para>
/// <para>
/// <b>ADR-014 tenant + session isolation</b>: all <see cref="IRagService.SearchAsync(string, RagSearchOptions, CancellationToken)"/>
/// calls set <see cref="RagSearchOptions.TenantId"/> AND <see cref="RagSearchOptions.SessionId"/>
/// — the session-files slice is partitioned by both keys (task 001 schema). A session query in
/// tenant A can never leak across to tenant B.
/// </para>
/// <para>
/// <b>NFR-02 cap</b>: rejects <see cref="SummarizeSessionFilesRequest.FileIds"/> lists exceeding
/// <see cref="ChatSession.MaxUploadedFiles"/> (20). The session itself enforces the same cap
/// upstream (task 004); this is defense in depth at the orchestrator boundary.
/// </para>
/// </remarks>
public sealed class SessionSummarizeOrchestrator
{
    /// <summary>Action code alternate key for the R5 chat-Summarize action seed (task 010 / D2-01).</summary>
    internal const string SummarizeActionCode = "SUM-CHAT@v1";

    /// <summary>Schema name passed to Azure OpenAI Structured Outputs (informational; observability).</summary>
    internal const string SchemaName = "DocumentAnalysisResult";

    /// <summary>Logical-name of the action entity used for portable-code lookup.</summary>
    internal const string ActionEntityLogicalName = "sprk_analysisaction";

    /// <summary>Deterministic combined-summary chat interjection emitted before multi-file streams (FR-04).</summary>
    internal const string CombinedSummaryInterjection =
        "I'll provide a combined summary for the files you uploaded.";

    /// <summary>JSON options used for final-result deserialization (camelCase Web defaults).</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ChatSessionManager _sessionManager;
    private readonly IRagService _ragService;
    private readonly IOpenAiClient _openAiClient;
    private readonly IGenericEntityService _entityService;
    private readonly R5SummarizeTelemetry _telemetry;
    private readonly ILogger<SessionSummarizeOrchestrator> _logger;

    public SessionSummarizeOrchestrator(
        ChatSessionManager sessionManager,
        IRagService ragService,
        IOpenAiClient openAiClient,
        IGenericEntityService entityService,
        R5SummarizeTelemetry telemetry,
        ILogger<SessionSummarizeOrchestrator> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// THE convergence method (per spec FR-01 + FR-08 + SC-08). Both task 014's direct endpoint AND
    /// task 015's agent-tool handler delegate to THIS method — no other entry point is exposed.
    /// </summary>
    /// <param name="request">The chat-session-scoped Summarize request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// SSE-shaped chunks emitted in this order:
    /// <list type="number">
    ///   <item>For multi-file requests (<c>FileIds.Count &gt;= 2</c>): one <see cref="AnalysisChunk.FromContent"/>
    ///         interjection per FR-04.</item>
    ///   <item>Zero or more <see cref="AnalysisChunk.FromDelta"/> events as
    ///         <see cref="IOpenAiClient.StreamStructuredCompletionAsync"/> + <see cref="IncrementalJsonParser"/>
    ///         emit per-field deltas (TL;DR-first; declaration order from the action's output schema).</item>
    ///   <item>One terminal <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/> event with the
    ///         parsed result (or <see cref="DocumentAnalysisResult.Fallback(string, string)"/> when
    ///         the accumulated JSON is malformed).</item>
    /// </list>
    /// On mid-stream exceptions an <see cref="AnalysisChunk.FromError"/> chunk is yielded and the
    /// enumerable terminates gracefully (no re-throw out of the iterator).
    /// </returns>
    public async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
        SummarizeSessionFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId, $"{nameof(request)}.{nameof(request.TenantId)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId, $"{nameof(request)}.{nameof(request.SessionId)}");

        // NFR-02 — hard cap (defense-in-depth; task 004 also enforces upstream).
        if (request.FileIds is { Count: > ChatSession.MaxUploadedFiles })
        {
            throw new ArgumentException(
                $"Session Summarize request exceeds the {ChatSession.MaxUploadedFiles}-file per-session cap " +
                $"(spec NFR-02). Received {request.FileIds.Count} file IDs.",
                nameof(request));
        }

        var stopwatch = Stopwatch.StartNew();
        // Manual Activity lifecycle (no `using` so we don't introduce a try-finally around the
        // iterator body — that would conflict with `yield return` per the C# language spec).
        // Activity disposes itself when its outer scope ends; explicit Dispose at end is best-effort.
        var activity = _telemetry.StartActivity(
            "R5.Summarize.OrchestrateSession", request.TenantId, request.CorrelationId);
        activity?.SetTag("path", request.Path.ToTelemetryValue());

        var completionStatus = "failed";
        long fileCountForTelemetry = 0;
        long totalTokens = 0;

        // Load session + resolve fileIds. Validation errors propagate (caller maps to 4xx).
        ChatSession? session = await _sessionManager.GetSessionAsync(
            request.TenantId, request.SessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Chat session '{request.SessionId}' not found for tenant '{request.TenantId}'.");
        }

        var uploadedFiles = session.UploadedFiles ?? Array.Empty<ChatSessionFile>();
        var fileIds = ResolveFileIds(request.FileIds, uploadedFiles);
        fileCountForTelemetry = fileIds.Count;

        if (fileIds.Count == 0)
        {
            _logger.LogInformation(
                "R5 Summarize: no files in session {SessionId} (tenant={TenantId}) — emitting decline.",
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

        // Load the action seed (SUM-CHAT@v1 per task 010). Includes the JPS system prompt + output schema.
        // C# does not allow `yield return` inside a `catch` block; we hoist the error message out
        // and yield AFTER the try-catch to keep the IAsyncEnumerable contract intact.
        SessionSummarizeActionConfig? actionConfig = null;
        string? earlyError = null;
        try
        {
            actionConfig = await LoadActionConfigAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "R5 Summarize: failed to load action seed '{ActionCode}' (tenant={TenantId} session={SessionId}).",
                SummarizeActionCode, request.TenantId, request.SessionId);
            earlyError = $"Summarize action configuration is unavailable: {ex.Message}";
        }
        if (earlyError is not null)
        {
            yield return AnalysisChunk.FromError(earlyError);
            RecordTelemetry(request, completionStatus, fileCountForTelemetry, totalTokens, stopwatch.Elapsed.TotalMilliseconds);
            activity?.Dispose();
            yield break;
        }

        // Session-scoped RAG retrieval (ADR-014 — tenant + session filters BOTH set; spec NFR-03).
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
                "R5 Summarize: RAG retrieval failed (tenant={TenantId} session={SessionId} fileCount={FileCount}).",
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

        // Build the chat messages: system prompt from action seed; user content composed from RAG hits.
        // OaiChatMessage = OpenAI.Chat.ChatMessage (SDK type — disambiguated from the BFF's own
        // Sprk.Bff.Api.Models.Ai.Chat.ChatMessage). The SDK type is what
        // OpenAiClient.StreamStructuredCompletionAsync accepts.
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
                .StreamStructuredCompletionAsync(messages, jsonSchemaBinaryData, SchemaName, model: null, maxOutputTokens: null, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex,
                "R5 Summarize: OpenAI circuit broken (tenant={TenantId} session={SessionId}).",
                request.TenantId, request.SessionId);
            earlyError = $"AI service is temporarily unavailable. Retry in ~{ex.RetryAfter.TotalSeconds:F0}s.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "R5 Summarize: failed to start streaming (tenant={TenantId} session={SessionId}).",
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
            // the load-bearing invariant from task 006). Safe to call outside try-catch.
            IReadOnlyList<FieldDeltaEvent> parserEvents = parser.Append(token);
            deltaBatch.Clear();
            foreach (var ev in parserEvents)
            {
                if (ev.Kind == FieldDeltaEventKind.FieldContent && !string.IsNullOrEmpty(ev.Content))
                {
                    deltaBatch.Add(AnalysisChunk.FromDelta(ev.Path, ev.Content, ev.Sequence));
                }
                // FieldStart / FieldComplete are state-only signals (Content is empty); R5 does NOT
                // surface them as SSE events per the AnalysisChunk envelope contract (FieldDelta
                // carries only FieldContent semantics today). Forward-compat: if a future R5 task
                // needs start/end markers, AnalysisChunk's discriminator surface can be extended
                // without changing the orchestrator's per-token contract.
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
            "R5 Summarize completed: path={Path} status={Status} fileCount={FileCount} tokens={Tokens} latencyMs={LatencyMs} tenant={TenantId} session={SessionId}",
            request.Path.ToTelemetryValue(), completionStatus, fileCountForTelemetry, totalTokens,
            stopwatch.Elapsed.TotalMilliseconds, request.TenantId, request.SessionId);

        activity?.Dispose();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────

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

    private async Task<SessionSummarizeActionConfig> LoadActionConfigAsync(CancellationToken ct)
    {
        var keyValues = new KeyAttributeCollection
        {
            { "sprk_actioncode", SummarizeActionCode }
        };

        var columns = new[]
        {
            "sprk_analysisactionid",
            "sprk_name",
            "sprk_actioncode",
            "sprk_systemprompt",
            "sprk_outputschemajson"
        };

        var entity = await _entityService
            .RetrieveByAlternateKeyAsync(ActionEntityLogicalName, keyValues, columns, ct)
            .ConfigureAwait(false);

        var systemPrompt = entity.GetAttributeValue<string>("sprk_systemprompt");
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new InvalidOperationException(
                $"Action '{SummarizeActionCode}' has no sprk_systemprompt — re-run task 010 deployment.");
        }
        var outputSchema = entity.GetAttributeValue<string>("sprk_outputschemajson");
        if (string.IsNullOrWhiteSpace(outputSchema))
        {
            throw new InvalidOperationException(
                $"Action '{SummarizeActionCode}' has no sprk_outputschemajson — re-run task 010 deployment.");
        }

        return new SessionSummarizeActionConfig(
            ActionId: entity.GetAttributeValue<Guid>("sprk_analysisactionid"),
            SystemPrompt: systemPrompt,
            OutputSchemaJson: outputSchema);
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
        SummarizeSessionFilesRequest request,
        string completionStatus,
        long fileCount,
        long totalTokens,
        double latencyMs)
    {
        try
        {
            _telemetry.RecordSummarizeInvocation(
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
                "R5 Summarize: telemetry cardinality guard rejected path={Path} status={Status} — coding error.",
                request.Path.ToTelemetryValue(), completionStatus);
        }
    }

    private static long EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length / 4;

    /// <summary>
    /// Advance the token enumerator once; isolate the try-catch from the iterator body so the
    /// orchestrator's body itself can keep `yield return` legal (C# disallows `yield return`
    /// inside try-catch / try-finally blocks).
    /// </summary>
    /// <returns>
    /// Tuple of (moveNextOk, token, errorMessage). On success: (true, token, null). On clean
    /// stream-end: (false, null, null). On exception: (false, null, errorMessage). On
    /// cancellation: re-throws OperationCanceledException (the caller's try-catch propagates).
    /// </returns>
    private static async Task<(bool MoveNextOk, string? Token, string? Error)> TryAdvanceAsync(
        IAsyncEnumerator<string> enumerator,
        SummarizeSessionFilesRequest request,
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
                "R5 Summarize: mid-stream exception (tenant={TenantId} session={SessionId}).",
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
            _logger.LogWarning(ex, "R5 Summarize: enumerator disposal failed (cleanup-tail; non-fatal).");
        }
    }
}

/// <summary>
/// Request shape consumed by <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>.
/// Both task 014 (direct endpoint) AND task 015 (agent-tool handler) construct THIS record and
/// delegate — the convergence contract per spec FR-01 + FR-08 + SC-08.
/// </summary>
/// <param name="TenantId">Tenant ID (ADR-014). Required.</param>
/// <param name="SessionId">Chat session ID (ADR-014; task 004 manifest key). Required.</param>
/// <param name="FileIds">
/// Optional subset of <see cref="ChatSession.UploadedFiles"/> to summarize. When null/empty,
/// defaults to ALL files in the session manifest (FR-08). Cap: 20 (NFR-02).
/// </param>
/// <param name="StyleHint">
/// Optional natural-language style hint passed through to the system prompt
/// (e.g., <c>executive</c>, <c>detailed</c>, <c>bullet-points</c>).
/// </param>
/// <param name="Path">
/// Invocation-path discriminator. <see cref="SummarizeInvocationPath.DirectEndpoint"/> when
/// dispatched from task 014's endpoint; <see cref="SummarizeInvocationPath.AgentTool"/> when
/// dispatched from task 015's agent-tool handler. Drives the <c>path</c> dimension on
/// <see cref="R5SummarizeTelemetry.RecordSummarizeInvocation"/>.
/// </param>
/// <param name="CorrelationId">
/// Optional correlation ID propagated to the distributed-tracing span (NFR-17).
/// </param>
public sealed record SummarizeSessionFilesRequest(
    string TenantId,
    string SessionId,
    IReadOnlyList<string>? FileIds,
    string? StyleHint,
    SummarizeInvocationPath Path,
    string? CorrelationId = null);

/// <summary>
/// Discriminator identifying which downstream caller is invoking
/// <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/>. Drives the
/// <c>path</c> dimension on <see cref="R5SummarizeTelemetry.RecordSummarizeInvocation"/>.
/// </summary>
public enum SummarizeInvocationPath
{
    /// <summary>Direct endpoint dispatch (task 014 / D2-04 — <c>POST /api/ai/chat/sessions/{id}/summarize</c>).</summary>
    DirectEndpoint = 0,

    /// <summary>Agent-tool dispatch (task 015 / D2-05 — <c>InvokeSummarizePlaybookTool</c>).</summary>
    AgentTool = 1,
}

/// <summary>Extension helpers for <see cref="SummarizeInvocationPath"/>.</summary>
internal static class SummarizeInvocationPathExtensions
{
    /// <summary>
    /// Maps the enum to the locked <see cref="R5SummarizeTelemetry"/> <c>path</c> dimension value
    /// (<c>direct_endpoint</c> or <c>agent_tool</c>). Out-of-enum input throws — by design, the
    /// telemetry cardinality guard would reject it anyway.
    /// </summary>
    public static string ToTelemetryValue(this SummarizeInvocationPath path) => path switch
    {
        SummarizeInvocationPath.DirectEndpoint => "direct_endpoint",
        SummarizeInvocationPath.AgentTool => "agent_tool",
        _ => throw new ArgumentOutOfRangeException(nameof(path), path,
            "Unknown SummarizeInvocationPath value — orchestrator/telemetry contract drift.")
    };
}

/// <summary>Action seed configuration loaded from <c>sprk_analysisaction</c> by alternate key.</summary>
/// <param name="ActionId">Dataverse GUID of the action row (task 010 — <c>SUM-CHAT@v1</c>).</param>
/// <param name="SystemPrompt">JPS-formatted system prompt (the <c>sprk_systemprompt</c> field verbatim).</param>
/// <param name="OutputSchemaJson">Structured-Outputs JSON Schema (the <c>sprk_outputschemajson</c> field verbatim).</param>
internal sealed record SessionSummarizeActionConfig(
    Guid ActionId,
    string SystemPrompt,
    string OutputSchemaJson);
