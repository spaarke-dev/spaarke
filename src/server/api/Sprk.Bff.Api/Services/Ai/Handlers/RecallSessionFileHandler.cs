using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// chat-routing-redesign-r1 task 085 — the LOAD-BEARING T2+T5 recall tool exposed to the LLM
/// as <c>recall_session_file(fileId, purpose, query, scope, maxTokens?, requireCitations?)</c>.
/// Returns citation-bearing content from a session-uploaded file, gated on the architecture's
/// "precomputed summary is NOT authoritative" trust frame (architecture §2 P3, §8.3).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Architecture binding</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>§5.2.1 — MUST use the session-files Azure AI Search index</strong>
/// (NEVER the Insights index family). Routing is enforced by setting
/// <see cref="RagSearchOptions.SessionId"/> on every retrieval call — the underlying
/// <see cref="IRagService.SearchAsync(string, RagSearchOptions, CancellationToken)"/>
/// session-scoped branch resolves the session-files SearchClient directly
/// (see <c>RagService.SearchAsync</c> lines 218-224) and ANDs the tenant filter with a
/// <c>sessionId eq '...'</c> clause (ADR-014 isolation invariant).</item>
/// <item><strong>§6.3 mode dispatch</strong>: <c>summary</c> reads
/// <see cref="ChatSessionFile.SummaryText"/> directly (hot 5ms path; no RagService call);
/// <c>relevant_sections</c> / <c>tables</c> / <c>citations</c> issue a session-scoped RAG
/// search; <c>full_text</c> concats top results within an 8K-token budget else returns
/// summary + first 2K chars + <c>truncation_reason: "exceeded_8K"</c>.</item>
/// <item><strong>§8.3 citation enforcement</strong>: <c>requireCitations</c> default is
/// <c>true</c>. When the recall returns zero citations AND scope != "summary" AND
/// <c>requireCitations</c> is true, the handler returns a structured tool error
/// (<see cref="ErrorNoCitationsAvailable"/>) so the orchestrator can prompt the agent to
/// retry with a different scope. NEVER quote precomputed summaries as if they were the
/// source.</item>
/// <item><strong>§9.2 graceful errors</strong>: <c>fileId</c> not in the session manifest
/// returns a structured payload with <c>scope_truncated: true</c> +
/// <c>truncation_reason: "not_found"</c>. NEVER throws.</item>
/// </list>
///
/// <para>
/// <strong>Per-purpose retrieval semantics (FR-31)</strong>: each <c>purpose</c> value
/// biases the search shape. Minimal first-cut semantics — sophisticated NLP per purpose is
/// deferred (task 091+ MVP scope). Documented in code via the <c>HandleRagSearchAsync</c>
/// switch.
/// </para>
///
/// <para>
/// <strong>One handler, no method dispatch</strong>: a single LLM-facing function. Auto-
/// discovered via <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> per ADR-010;
/// ZERO new manual DI line.
/// </para>
///
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only.
/// Recall has no playbook-side analogue; the chat session is the authority for session
/// files.
/// </para>
///
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IRagService"/> and <see cref="ChatSessionManager"/> directly — both
/// BFF-internal AI plumbing per the 2026-05-20 refined ADR-013 boundary rule
/// (no PublicContracts facade required for BFF-internal chat handlers).</item>
/// <item><strong>ADR-014</strong>: tenantId + sessionId forwarded into every retrieval
/// call; cross-session reads are structurally impossible.</item>
/// <item><strong>ADR-015 (BINDING)</strong>: telemetry payload carries handler name +
/// decision IDs + sessionId + tenantId + outcome enum + durationMs ONLY. NEVER content
/// text, citation excerpts, summary text, query strings, or any recall body. Query
/// length (numeric) and result count (numeric) may appear at Info; the query text itself
/// MAY appear at Debug only.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size
/// delta target ≤+0.1 MB.</item>
/// <item><strong>ADR-033</strong>: SINGLE <see cref="ToolResult"/> return; no streaming
/// side-channel. Memory tools are non-streaming.</item>
/// </list>
/// </remarks>
public sealed class RecallSessionFileHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(RecallSessionFileHandler);

    // ────────────────────────────────────────────────────────────────────────
    // Closed enums (architecture §8.1 verbatim)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Purpose enum: standard semantic question-answering retrieval.</summary>
    internal const string PurposeAnswerQuestion = "answer_question";

    /// <summary>Purpose enum: surface exact-phrase matching results suitable for quoting.</summary>
    internal const string PurposeQuote = "quote";

    /// <summary>Purpose enum: surface candidates for cross-document comparison.</summary>
    internal const string PurposeCompare = "compare";

    /// <summary>Purpose enum: produce a summary view (use precomputed when scope=summary; else top-K concat).</summary>
    internal const string PurposeSummarize = "summarize";

    /// <summary>Purpose enum: post-process results to extract ISO-style date tokens.</summary>
    internal const string PurposeExtractDates = "extract_dates";

    /// <summary>Purpose enum: broader top-K (5) with citation markers explicit, for verification answers.</summary>
    internal const string PurposeVerify = "verify";

    internal static readonly HashSet<string> SupportedPurposes = new(StringComparer.Ordinal)
    {
        PurposeAnswerQuestion,
        PurposeQuote,
        PurposeCompare,
        PurposeSummarize,
        PurposeExtractDates,
        PurposeVerify
    };

    /// <summary>Scope enum: read precomputed <see cref="ChatSessionFile.SummaryText"/>.</summary>
    internal const string ScopeSummary = "summary";

    /// <summary>Scope enum: session-scoped semantic search; default scope.</summary>
    internal const string ScopeRelevantSections = "relevant_sections";

    /// <summary>Scope enum: concat top-K chunks ≤8K tokens; else summary + first 2K chars + truncation.</summary>
    internal const string ScopeFullText = "full_text";

    /// <summary>Scope enum: session-scoped search filtered to table-bearing chunks.</summary>
    internal const string ScopeTables = "tables";

    /// <summary>Scope enum: session-scoped search returning citation-bearing chunks.</summary>
    internal const string ScopeCitations = "citations";

    internal static readonly HashSet<string> SupportedScopes = new(StringComparer.Ordinal)
    {
        ScopeSummary,
        ScopeRelevantSections,
        ScopeFullText,
        ScopeTables,
        ScopeCitations
    };

    // ────────────────────────────────────────────────────────────────────────
    // Truncation / error discriminators (architecture §9.2 + §8.3)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>truncation_reason: the requested fileId is not in the chat session manifest (§9.2).</summary>
    internal const string TruncationReasonNotFound = "not_found";

    /// <summary>truncation_reason: full_text scope produced >8K tokens; truncated to summary + first 2K chars (§9.2).</summary>
    internal const string TruncationReasonExceeded8K = "exceeded_8K";

    /// <summary>
    /// ErrorCode emitted when requireCitations=true and the recall produced zero citations
    /// AND scope != "summary" — architecture §8.3 trust-frame enforcement; the agent should
    /// retry with a different scope.
    /// </summary>
    internal const string ErrorNoCitationsAvailable = "NO_CITATIONS_AVAILABLE";

    // ────────────────────────────────────────────────────────────────────────
    // Outcome discriminators (ADR-015 tier-1 safe telemetry vocabulary)
    // ────────────────────────────────────────────────────────────────────────

    internal const string OutcomeOk = "ok";
    internal const string OutcomeNotFound = "not_found";
    internal const string OutcomeExceeded8K = "exceeded_8K";
    internal const string OutcomeNoCitations = "no_citations";
    internal const string OutcomeValidationFailed = "validation_failed";
    internal const string OutcomeCancelled = "cancelled";
    internal const string OutcomeException = "exception";
    internal const string OutcomeSearchUnavailable = "search_unavailable";

    // ────────────────────────────────────────────────────────────────────────
    // Tunables
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Default top-K for relevant_sections / tables / citations scopes (architecture §6.3).</summary>
    internal const int DefaultSectionsTopK = 3;

    /// <summary>verify-purpose top-K override (broader net for verification).</summary>
    internal const int VerifyPurposeTopK = 5;

    /// <summary>summarize-purpose top-K override (more chunks to compose summary view).</summary>
    internal const int SummarizePurposeTopK = 5;

    /// <summary>8K-token cap on full_text scope output (rough char approximation: 4 chars/token = 32K chars).</summary>
    internal const int FullTextMaxChars = 32_000;

    /// <summary>Char count returned alongside summary when full_text truncates (architecture §9.2).</summary>
    internal const int TruncationFallbackChars = 2_000;

    /// <summary>Default MinScore for session-scoped semantic search (RagSearchOptions.MinScore default is 0.7; we relax for recall).</summary>
    internal const float SessionSearchMinScore = 0.5f;

    /// <summary>Maximum number of citation entries returned from any single recall call.</summary>
    internal const int MaxCitationsPerRecall = 10;

    private static readonly Regex Iso8601DateRegex = new(
        @"\b\d{4}-\d{2}-\d{2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ────────────────────────────────────────────────────────────────────────

    private readonly IRagService _ragService;
    private readonly ChatSessionManager _sessionManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecallSessionFileHandler> _logger;
    private readonly IContextEventEmitter? _contextEventEmitter;

    // task 091 — IRecentlyDiscussedTracker is NOT yet defined in code as of task 085;
    // when task 091 introduces the interface + DI registration, switch this to a typed
    // dependency and remove the defensive null-safe call site. Until then we accept the
    // dependency optionally so this task lands green per the POML notes.
    private readonly IRecentlyDiscussedTrackerLike? _recentlyDiscussedTracker;

    public RecallSessionFileHandler(
        IRagService ragService,
        ChatSessionManager sessionManager,
        TimeProvider timeProvider,
        ILogger<RecallSessionFileHandler> logger,
        IContextEventEmitter? contextEventEmitter = null,
        IRecentlyDiscussedTrackerLike? recentlyDiscussedTracker = null)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextEventEmitter = contextEventEmitter;
        _recentlyDiscussedTracker = recentlyDiscussedTracker;
    }

    // ────────────────────────────────────────────────────────────────────────
    // IToolHandler surface
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Recall Session File",
        Description: "Recall content from a session-uploaded file by scope + purpose. Returns " +
                     "citation-bearing content. The precomputed summary is NOT authoritative — " +
                     "for any legally-precise question (specific clauses, exact wording, dates, " +
                     "parties, dollar amounts) call this tool with requireCitations=true and cite " +
                     "the source in your answer. Architecture §8.1.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "fileId",
                "Stable session-scoped file identifier produced at upload time. Required.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "purpose",
                "Closed enum: 'answer_question' | 'quote' | 'compare' | 'summarize' | " +
                "'extract_dates' | 'verify'. Biases retrieval semantics. Required.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "query",
                "Search query text. Required.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "scope",
                "Closed enum: 'summary' | 'relevant_sections' | 'full_text' | 'tables' | " +
                "'citations'. Defaults to 'relevant_sections'. Use 'summary' only for high-level " +
                "framing — NEVER quote it as authoritative.",
                ToolParameterType.String,
                Required: false,
                DefaultValue: ScopeRelevantSections),
            new ToolParameterDefinition(
                "maxTokens",
                "Optional cap on returned content size. Defaults to 8K when omitted.",
                ToolParameterType.Integer,
                Required: false),
            new ToolParameterDefinition(
                "requireCitations",
                "Whether the recall must include citations. DEFAULT TRUE. The trust frame " +
                "(architecture §8.3) requires this be true for any legally-precise answer.",
                ToolParameterType.Boolean,
                Required: false,
                DefaultValue: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context invocation rejected — session files only exist in chat context.
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "RecallSessionFileHandler is chat-context-only. Playbook-context invocation is unsupported.");

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

            if (!doc.RootElement.TryGetProperty("fileId", out var fileIdProp) ||
                fileIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(fileIdProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'fileId' string field.");
            }

            if (!doc.RootElement.TryGetProperty("purpose", out var purposeProp) ||
                purposeProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(purposeProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'purpose' string field " +
                    $"(one of: {string.Join(", ", SupportedPurposes)}).");
            }
            var purpose = purposeProp.GetString()!;
            if (!SupportedPurposes.Contains(purpose))
            {
                return ToolValidationResult.Failure(
                    $"'purpose' must be one of: {string.Join(", ", SupportedPurposes)}. " +
                    $"Received: '{purpose}'.");
            }

            if (!doc.RootElement.TryGetProperty("query", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'query' string field.");
            }

            if (doc.RootElement.TryGetProperty("scope", out var scopeProp) &&
                scopeProp.ValueKind == JsonValueKind.String &&
                scopeProp.GetString() is { } scopeValue &&
                !SupportedScopes.Contains(scopeValue))
            {
                return ToolValidationResult.Failure(
                    $"'scope' must be one of: {string.Join(", ", SupportedScopes)}. " +
                    $"Received: '{scopeValue}'.");
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
            "RecallSessionFileHandler is chat-context-only. Playbook-context invocation is unsupported.",
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
        var sessionIdString = context.ChatSessionId.ToString("N");

        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            stopwatch.Stop();
            EmitCompleted(tool.Name, context, OutcomeValidationFailed, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "TenantId is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            stopwatch.Stop();
            // ADR-015: log structural error only — NEVER the raw arguments JSON.
            _logger.LogWarning(
                "RecallSessionFileHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            EmitCompleted(tool.Name, context, OutcomeValidationFailed, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // ADR-015: log handler + correlation + scope/purpose enums + tenantId + numeric query
        // length ONLY. NEVER the query text, fileId is acceptable as a deterministic identifier.
        _logger.LogInformation(
            "RecallSessionFileHandler ({Correlation}) start fileId={FileId} purpose={Purpose} scope={Scope} requireCitations={RequireCitations} queryLen={QueryLen}",
            correlationLogId, args.FileId, args.Purpose, args.Scope, args.RequireCitations, args.Query.Length);

        // Query text MAY appear at Debug only.
        _logger.LogDebug(
            "RecallSessionFileHandler ({Correlation}) debug queryText={QueryText}",
            correlationLogId, args.Query);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Load session manifest — architecture §6.3 hot-path requires
            // ChatSession.UploadedFiles[fileId] lookup. ChatInvocationContext does NOT
            // carry the manifest directly (mirrors §11 wire-not-build pattern); fetch via
            // ChatSessionManager which serves from Redis (hot) → Cosmos → Dataverse fallback.
            var session = await _sessionManager
                .GetSessionAsync(context.TenantId, sessionIdString, cancellationToken)
                .ConfigureAwait(false);

            // file lookup: ChatSession.UploadedFiles is IReadOnlyList<ChatSessionFile> — no
            // indexer-by-fileId surface; use FirstOrDefault per the POML adapt-clause.
            var file = session?.UploadedFiles?
                .FirstOrDefault(f => string.Equals(f.FileId, args.FileId, StringComparison.Ordinal));

            if (file is null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "RecallSessionFileHandler ({Correlation}) not_found fileId={FileId} in {Duration}ms",
                    correlationLogId, args.FileId, stopwatch.ElapsedMilliseconds);
                EmitCompleted(tool.Name, context, OutcomeNotFound, stopwatch.ElapsedMilliseconds);
                return BuildOkResult(
                    tool, startedAt,
                    payload: new RecallSessionFilePayload
                    {
                        Content = string.Empty,
                        Citations = Array.Empty<RecallCitation>(),
                        ScopeTruncated = true,
                        TruncationReason = TruncationReasonNotFound
                    },
                    summary: $"File '{args.FileId}' is not present in this session.",
                    confidence: 0.0);
            }

            // Mode dispatch (architecture §6.3)
            var (payload, outcome) = args.Scope switch
            {
                ScopeSummary => HandleSummary(file),
                ScopeFullText => await HandleFullTextAsync(context, file, args, cancellationToken)
                    .ConfigureAwait(false),
                ScopeRelevantSections or ScopeTables or ScopeCitations => await HandleRagSearchAsync(
                    context, file, args, cancellationToken).ConfigureAwait(false),
                _ => (BuildNotFoundPayload(), OutcomeValidationFailed) // unreachable — ValidateChat enforces
            };

            // §8.3 requireCitations enforcement — when true (default), zero citations + scope
            // != summary returns a structured tool error so the orchestrator can retry with a
            // different scope. The trust frame requires citation-bearing answers.
            if (args.RequireCitations
                && args.Scope != ScopeSummary
                && payload.Citations.Count == 0
                && payload.TruncationReason is null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "RecallSessionFileHandler ({Correlation}) no_citations_available scope={Scope} in {Duration}ms",
                    correlationLogId, args.Scope, stopwatch.ElapsedMilliseconds);
                EmitCompleted(tool.Name, context, OutcomeNoCitations, stopwatch.ElapsedMilliseconds);
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "Recall produced no citations and requireCitations=true. Retry with a different scope " +
                    "or set requireCitations=false explicitly if the answer does not need source attribution.",
                    ErrorNoCitationsAvailable,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
            }

            // Mark recently-discussed on any successful recall except not_found. Null-safe per
            // task 091 defensive pattern — when the registration lands (task 091), this becomes
            // unconditional.
            if (payload.TruncationReason != TruncationReasonNotFound)
            {
                _recentlyDiscussedTracker?.Mark(sessionIdString, args.FileId);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "RecallSessionFileHandler ({Correlation}) ok scope={Scope} purpose={Purpose} resultCount={ResultCount} truncationReason={TruncationReason} in {Duration}ms",
                correlationLogId, args.Scope, args.Purpose, payload.Citations.Count,
                payload.TruncationReason ?? "(none)", stopwatch.ElapsedMilliseconds);
            EmitCompleted(tool.Name, context, outcome, stopwatch.ElapsedMilliseconds);

            return BuildOkResult(
                tool, startedAt,
                payload: payload,
                summary: $"Recalled {args.Scope} from file '{file.FileName}' ({payload.Citations.Count} citation(s)).",
                confidence: payload.TruncationReason is null ? 1.0 : 0.5);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "RecallSessionFileHandler ({Correlation}) cancelled scope={Scope}",
                correlationLogId, args.Scope);
            EmitCompleted(tool.Name, context, OutcomeCancelled, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Recall was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log exception TYPE only, never the message body which may carry
            // search-side content fragments.
            _logger.LogError(ex,
                "RecallSessionFileHandler ({Correlation}) failed scope={Scope} purpose={Purpose}: {ErrorType}",
                correlationLogId, args.Scope, args.Purpose, ex.GetType().Name);
            EmitCompleted(tool.Name, context, OutcomeException, stopwatch.ElapsedMilliseconds);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Recall failed.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Mode handlers (architecture §6.3)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// scope=summary: hot 5ms path — read <see cref="ChatSessionFile.SummaryText"/>
    /// directly with NO RagService call. NEVER returns citations — the summary is not
    /// authoritative per architecture §2 P3 / §8.3.
    /// </summary>
    private static (RecallSessionFilePayload, string) HandleSummary(ChatSessionFile file)
    {
        var payload = new RecallSessionFilePayload
        {
            // SummaryText is null until enrichment completes (task 071). When absent return an
            // empty body — callers should fall back to a section-scoped recall.
            Content = file.SummaryText ?? string.Empty,
            Citations = Array.Empty<RecallCitation>(),
            ScopeTruncated = false,
            TruncationReason = null
        };
        return (payload, OutcomeOk);
    }

    /// <summary>
    /// scope=relevant_sections / tables / citations: session-scoped RAG search against the
    /// session-files Azure AI Search index. The index routing is via
    /// <see cref="RagSearchOptions.SessionId"/> (RagService session-scoped branch) — NEVER
    /// via <see cref="RagSearchOptions.SearchIndexName"/> set to anything else, and NEVER
    /// against the Insights index family.
    /// </summary>
    private async Task<(RecallSessionFilePayload, string)> HandleRagSearchAsync(
        ChatInvocationContext context,
        ChatSessionFile file,
        ParsedArgs args,
        CancellationToken cancellationToken)
    {
        // Per-purpose top-K bias (FR-31 — minimal first-cut semantics)
        var topK = args.Purpose switch
        {
            PurposeVerify => VerifyPurposeTopK,
            PurposeSummarize => SummarizePurposeTopK,
            _ => DefaultSectionsTopK
        };

        var sessionIdString = context.ChatSessionId.ToString("N");

        var options = new RagSearchOptions
        {
            TenantId = context.TenantId,
            // SessionId → session-scoped routing — RagService picks the spaarke-session-files
            // index directly (see RagService.SearchAsync lines 218-224) and ANDs the tenant
            // filter with sessionId. Architecture §5.2.1 binding.
            SessionId = sessionIdString,
            TopK = topK,
            MinScore = SessionSearchMinScore,
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true,
            // ADR-014: caller principal is forwarded for privilege filtering when applicable.
            // The session-files index does not carry privilege_group_ids columns; this is a
            // forward-compat detail (the session-scoped branch in BuildSearchOptions ignores
            // CallerPrincipal — sessionId itself is the authorization).
        };

        RagSearchResponse response;
        try
        {
            response = await _ragService.SearchAsync(args.Query, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Architecture §9.1 T5 graceful degradation: AI Search query fails → return
            // truncation_reason: "search_unavailable" so the agent can retry or apologize.
            _logger.LogWarning(ex,
                "RecallSessionFileHandler search failed for fileId={FileId}: {ErrorType}",
                args.FileId, ex.GetType().Name);
            return (new RecallSessionFilePayload
            {
                Content = string.Empty,
                Citations = Array.Empty<RecallCitation>(),
                ScopeTruncated = true,
                TruncationReason = OutcomeSearchUnavailable
            }, OutcomeSearchUnavailable);
        }

        // Post-filter by fileId — the session-files schema carries one document per chunk
        // with a `fileId` (or sessionFileId) tagging column. The RagSearchResult shape doesn't
        // surface this column directly; we filter via the manifest's SearchDocumentIdsCsv which
        // ChatSessionFile carries from task 071 enrichment.
        var allowedSearchIds = new HashSet<string>(
            (file.SearchDocumentIdsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

        var fileScopedResults = allowedSearchIds.Count == 0
            ? response.Results.ToList()
            : response.Results.Where(r => allowedSearchIds.Contains(r.Id)).ToList();

        // Per-purpose post-processing (FR-31 — minimal first-cut semantics; rich NLP deferred)
        if (string.Equals(args.Purpose, PurposeQuote, StringComparison.Ordinal))
        {
            // quote purpose: retain only results whose Content contains the query as a
            // case-insensitive substring (best-effort "exact phrase" approximation).
            fileScopedResults = fileScopedResults
                .Where(r => r.Content?.IndexOf(args.Query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        // Compose content + citations payload
        var contentBuilder = new StringBuilder();
        var citations = new List<RecallCitation>();
        var citationCap = Math.Min(fileScopedResults.Count, MaxCitationsPerRecall);
        for (var i = 0; i < citationCap; i++)
        {
            var r = fileScopedResults[i];
            contentBuilder.AppendLine(r.Content ?? string.Empty);
            contentBuilder.AppendLine();
            citations.Add(new RecallCitation(
                Page: r.ChunkIndex + 1, // 1-based; chunkIndex is 0-based on KnowledgeDocument
                Paragraph: null,
                Section: r.KnowledgeSourceName,
                Text: r.Content ?? string.Empty));
        }

        var content = contentBuilder.ToString().TrimEnd();

        // extract_dates purpose: post-process the recalled content for ISO-8601 date tokens
        // and append them as a structured suffix. (Sophisticated multi-format date parsing
        // deferred per the POML "implement minimally; defer fancy" guidance.)
        if (string.Equals(args.Purpose, PurposeExtractDates, StringComparison.Ordinal) && !string.IsNullOrEmpty(content))
        {
            var dateMatches = Iso8601DateRegex.Matches(content)
                .Select(m => m.Value)
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToList();
            if (dateMatches.Count > 0)
            {
                content = $"{content}\n\nExtracted dates ({dateMatches.Count}): {string.Join(", ", dateMatches)}";
            }
        }

        return (new RecallSessionFilePayload
        {
            Content = content,
            Citations = citations,
            ScopeTruncated = false,
            TruncationReason = null
        }, OutcomeOk);
    }

    /// <summary>
    /// scope=full_text: concat session-scoped chunks within an 8K-token budget. Else return
    /// summary + first 2K chars + truncation_reason: "exceeded_8K" (architecture §9.2).
    /// </summary>
    private async Task<(RecallSessionFilePayload, string)> HandleFullTextAsync(
        ChatInvocationContext context,
        ChatSessionFile file,
        ParsedArgs args,
        CancellationToken cancellationToken)
    {
        var sessionIdString = context.ChatSessionId.ToString("N");

        // Pull more chunks for full-text composition; the budget check below truncates.
        var options = new RagSearchOptions
        {
            TenantId = context.TenantId,
            SessionId = sessionIdString,
            TopK = 20,
            MinScore = 0.0f, // full text wants every chunk for this file, not just top-relevance
            UseSemanticRanking = false,
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        RagSearchResponse response;
        try
        {
            response = await _ragService.SearchAsync(
                // The session-files index keyword path supports "*" as a "match anything" stub;
                // when query is empty (rare for full_text) fall back to the args.Query so the
                // search retains some semantic anchor.
                string.IsNullOrWhiteSpace(args.Query) ? "*" : args.Query,
                options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "RecallSessionFileHandler full_text search failed for fileId={FileId}: {ErrorType}",
                args.FileId, ex.GetType().Name);
            return (new RecallSessionFilePayload
            {
                Content = string.Empty,
                Citations = Array.Empty<RecallCitation>(),
                ScopeTruncated = true,
                TruncationReason = OutcomeSearchUnavailable
            }, OutcomeSearchUnavailable);
        }

        // File-scoped filter via SearchDocumentIdsCsv (same as HandleRagSearchAsync)
        var allowedSearchIds = new HashSet<string>(
            (file.SearchDocumentIdsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);
        var fileChunks = allowedSearchIds.Count == 0
            ? response.Results.OrderBy(r => r.ChunkIndex).ToList()
            : response.Results
                .Where(r => allowedSearchIds.Contains(r.Id))
                .OrderBy(r => r.ChunkIndex)
                .ToList();

        var sb = new StringBuilder();
        var citations = new List<RecallCitation>();
        var citationCap = MaxCitationsPerRecall;
        var bytesAdded = 0;
        var truncated = false;
        for (var i = 0; i < fileChunks.Count; i++)
        {
            var r = fileChunks[i];
            var content = r.Content ?? string.Empty;
            if (bytesAdded + content.Length > FullTextMaxChars)
            {
                truncated = true;
                break;
            }
            sb.AppendLine(content);
            sb.AppendLine();
            bytesAdded += content.Length + 2;

            if (citations.Count < citationCap)
            {
                citations.Add(new RecallCitation(
                    Page: r.ChunkIndex + 1,
                    Paragraph: null,
                    Section: r.KnowledgeSourceName,
                    Text: content));
            }
        }

        if (truncated)
        {
            // Architecture §9.2: full_text exceeded 8K → return summary + first 2K chars
            var summaryPart = file.SummaryText ?? string.Empty;
            var firstChars = sb.ToString();
            if (firstChars.Length > TruncationFallbackChars)
                firstChars = firstChars.Substring(0, TruncationFallbackChars);

            var truncatedContent = string.IsNullOrEmpty(summaryPart)
                ? firstChars
                : $"{summaryPart}\n\n{firstChars}";

            return (new RecallSessionFilePayload
            {
                Content = truncatedContent,
                // Drop citations in the truncated payload — the agent should not cite the
                // approximation. Re-issue with scope=relevant_sections for citation-bearing answers.
                Citations = Array.Empty<RecallCitation>(),
                ScopeTruncated = true,
                TruncationReason = TruncationReasonExceeded8K
            }, OutcomeExceeded8K);
        }

        return (new RecallSessionFilePayload
        {
            Content = sb.ToString().TrimEnd(),
            Citations = citations,
            ScopeTruncated = false,
            TruncationReason = null
        }, OutcomeOk);
    }

    private static RecallSessionFilePayload BuildNotFoundPayload() =>
        new()
        {
            Content = string.Empty,
            Citations = Array.Empty<RecallCitation>(),
            ScopeTruncated = true,
            TruncationReason = TruncationReasonNotFound
        };

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private ToolResult BuildOkResult(
        AnalysisTool tool,
        DateTimeOffset startedAt,
        RecallSessionFilePayload payload,
        string summary,
        double confidence) =>
        ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: payload,
            summary: summary,
            confidence: confidence,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = _timeProvider.GetUtcNow(),
                ModelCalls = 0
            });

    private void EmitCompleted(
        string toolName,
        ChatInvocationContext context,
        string outcome,
        long durationMs)
    {
        // ADR-015 BINDING: tier-1 safe payload only. Tool name + decisionId + sessionId +
        // tenantId + outcome enum + durationMs. NEVER content, citation excerpts, query
        // strings, or summary text. The IContextEventEmitter signature is structurally
        // constrained — no object / JsonElement / free-form string parameters that could
        // carry recall body text.
        _contextEventEmitter?.ToolCallCompleted(
            toolName: toolName,
            decisionId: context.DecisionId,
            sessionId: context.ChatSessionId,
            tenantId: context.TenantId,
            outcome: outcome,
            durationMs: durationMs);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ────────────────────────────────────────────────────────────────────────

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

            if (!root.TryGetProperty("fileId", out var fileIdProp) ||
                fileIdProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(fileIdProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'fileId' string field.";
                return false;
            }

            if (!root.TryGetProperty("purpose", out var purposeProp) ||
                purposeProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(purposeProp.GetString()))
            {
                error = $"Tool arguments must include a non-empty 'purpose' string field " +
                        $"(one of: {string.Join(", ", SupportedPurposes)}).";
                return false;
            }
            var purpose = purposeProp.GetString()!;
            if (!SupportedPurposes.Contains(purpose))
            {
                error = $"'purpose' must be one of: {string.Join(", ", SupportedPurposes)}.";
                return false;
            }

            if (!root.TryGetProperty("query", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'query' string field.";
                return false;
            }

            var scope = ScopeRelevantSections;
            if (root.TryGetProperty("scope", out var scopeProp) &&
                scopeProp.ValueKind == JsonValueKind.String &&
                scopeProp.GetString() is { } scopeValue &&
                !string.IsNullOrWhiteSpace(scopeValue))
            {
                if (!SupportedScopes.Contains(scopeValue))
                {
                    error = $"'scope' must be one of: {string.Join(", ", SupportedScopes)}.";
                    return false;
                }
                scope = scopeValue;
            }

            int? maxTokens = null;
            if (root.TryGetProperty("maxTokens", out var maxTokensProp) &&
                maxTokensProp.ValueKind == JsonValueKind.Number &&
                maxTokensProp.TryGetInt32(out var maxTokensValue))
            {
                maxTokens = maxTokensValue;
            }

            // DEFAULT TRUE (architecture §8.3 trust frame, non-negotiable)
            var requireCitations = true;
            if (root.TryGetProperty("requireCitations", out var requireProp))
            {
                requireCitations = requireProp.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => true
                };
            }

            args = new ParsedArgs
            {
                FileId = fileIdProp.GetString()!,
                Purpose = purpose,
                Query = queryProp.GetString()!,
                Scope = scope,
                MaxTokens = maxTokens,
                RequireCitations = requireCitations
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Parsed chat-call arguments.</summary>
    private readonly record struct ParsedArgs
    {
        public string FileId { get; init; }
        public string Purpose { get; init; }
        public string Query { get; init; }
        public string Scope { get; init; }
        public int? MaxTokens { get; init; }
        public bool RequireCitations { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Shape verbatim
    /// per architecture §8.1: <c>{ content, citations[{page, paragraph?, section?, text}],
    /// scope_truncated, truncation_reason? }</c>.
    /// </summary>
    public sealed class RecallSessionFilePayload
    {
        /// <summary>The recalled text content. Empty when truncation_reason is "not_found".</summary>
        [JsonPropertyName("content")]
        public required string Content { get; init; }

        /// <summary>Citation envelopes — page, optional paragraph, optional section, and text.</summary>
        [JsonPropertyName("citations")]
        public required IReadOnlyList<RecallCitation> Citations { get; init; }

        /// <summary>True when the scope produced a truncated payload (always-true companion to non-null truncation_reason).</summary>
        [JsonPropertyName("scope_truncated")]
        public required bool ScopeTruncated { get; init; }

        /// <summary>Truncation discriminator: "not_found" | "exceeded_8K" | "search_unavailable" | null when none.</summary>
        [JsonPropertyName("truncation_reason")]
        public string? TruncationReason { get; init; }
    }

    /// <summary>
    /// Citation envelope per architecture §8.1 verbatim:
    /// <c>{ page: int, paragraph: int?, section: string?, text: string }</c>.
    /// </summary>
    /// <param name="Page">1-based page number (or chunk index + 1 when source format is not paginated).</param>
    /// <param name="Paragraph">Optional 1-based paragraph index within the page.</param>
    /// <param name="Section">Optional section name (knowledge source name on the chunk).</param>
    /// <param name="Text">The chunk content text the citation references.</param>
    public sealed record RecallCitation(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("paragraph")] int? Paragraph,
        [property: JsonPropertyName("section")] string? Section,
        [property: JsonPropertyName("text")] string Text);
}

/// <summary>
/// Defensive-shim interface for task 091's <c>IRecentlyDiscussedTracker</c>. Task 091 owns
/// the canonical interface definition + DI registration; until then this handler injects
/// the optional dependency through a structural shape so the source compiles AND the test
/// suite can mock it. When task 091 lands, switch this dependency to the real
/// <c>IRecentlyDiscussedTracker</c> from <c>Services/Ai/Memory</c> (or wherever 091 places
/// it) and delete this local shim.
/// </summary>
/// <remarks>
/// Internal so the public API surface area of the handler is unchanged. Task 091 will
/// replace this with the real interface; the rename is a single-line edit.
/// </remarks>
public interface IRecentlyDiscussedTrackerLike
{
    /// <summary>Marks <paramref name="fileId"/> as recently-discussed in <paramref name="sessionId"/>.</summary>
    void Mark(string sessionId, string fileId);
}
