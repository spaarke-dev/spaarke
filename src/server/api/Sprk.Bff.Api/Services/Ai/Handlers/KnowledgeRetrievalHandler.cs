using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side (and playbook-side) typed handler that retrieves knowledge from a tenant's
/// indexed knowledge sources via <see cref="IRagService"/> (R6 Wave 7c). Replaces the
/// legacy hardcoded <c>KnowledgeRetrievalTools</c> class previously registered in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two methods</strong>: two <c>sprk_analysistool</c> rows
/// (<c>KNOWLEDGE-SOURCE-GET</c> and <c>KNOWLEDGE-BASE-SEARCH</c>) share this single
/// <c>sprk_handlerclass = KnowledgeRetrievalHandler</c>. Each row's
/// <c>sprk_configuration.method</c> selects the internal method. This mirrors the
/// <see cref="TextRefinementHandler"/> shape (R6 Wave 7 Q9) — distinct LLM tools with
/// distinct descriptions + parameter shapes, one handler class.
/// </para>
/// <list type="bullet">
/// <item><c>method = "GetKnowledgeSource"</c> — retrieve ALL indexed chunks for a specific
/// knowledge source by its ID (queries the knowledge index filtered by
/// <c>knowledgeSourceId</c>). Also emits a <c>source_pane</c> SSE event with
/// <c>DocumentViewer</c> widget data so the frontend can render the knowledge source
/// alongside the chat text response (preserves the pre-R6 Gap-1 behavior).</item>
/// <item><c>method = "SearchKnowledgeBase"</c> — semantic search scoped to the playbook's
/// knowledge sources (when <see cref="ChatInvocationContext.KnowledgeScope"/> is non-null),
/// or across all sources for the tenant otherwise.</item>
/// </list>
/// <para>
/// <strong>Citations + widget events</strong>: per the R6 Wave 7b infrastructure contract,
/// the handler returns citation envelopes + (for GetKnowledgeSource) a widget envelope via
/// <see cref="ToolResult.Metadata"/> using <see cref="ToolResultMetadataKeys.Citations"/> +
/// <see cref="ToolResultMetadataKeys.Widget"/>. The <c>ToolHandlerToAIFunctionAdapter</c>
/// accumulates citations into the per-chat-turn <c>CitationContext</c> and emits the SSE
/// event via the captured writer delegate.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IRagService"/> only. Resolved via constructor
/// injection (auto-discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Both"/>. Playbook
/// path reads <c>knowledgeSourceId</c> / <c>query</c> from
/// <see cref="AnalysisTool.Configuration"/> and does NOT emit the widget envelope
/// (playbook orchestration has no SSE channel). Chat path reads from
/// <see cref="ChatInvocationContext.ToolArgumentsJson"/> and returns both citations + widget.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code
/// never injects this handler.</item>
/// <item><strong>ADR-014</strong>: <see cref="ToolInvocationContextBase.TenantId"/> is
/// validated on both paths and passed to <see cref="IRagService"/> as a required filter so
/// cross-tenant leakage is impossible. <see cref="ChatInvocationContext.KnowledgeScope"/>
/// adds an optional playbook-level filter on top of the tenant filter.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + IDs + method discriminator
/// + result count + duration ONLY. NEVER the query, the knowledge source ID as user input,
/// citation excerpt content, or widget data content.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class KnowledgeRetrievalHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(KnowledgeRetrievalHandler);

    internal const string MethodGetKnowledgeSource = "GetKnowledgeSource";
    internal const string MethodSearchKnowledgeBase = "SearchKnowledgeBase";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodGetKnowledgeSource,
        MethodSearchKnowledgeBase
    };

    private const int DefaultTopK = 5;
    private const int MaxTopK = 20;
    private const int GetKnowledgeSourceTopK = 10;

    private readonly IRagService _ragService;
    private readonly ILogger<KnowledgeRetrievalHandler> _logger;

    public KnowledgeRetrievalHandler(
        IRagService ragService,
        ILogger<KnowledgeRetrievalHandler> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Knowledge Retrieval",
        Description: "Retrieves indexed content from the tenant's knowledge base. Provides two methods: " +
                     "'GetKnowledgeSource' returns all indexed chunks for a specific knowledge source ID " +
                     "(with a source_pane DocumentViewer widget); 'SearchKnowledgeBase' performs a hybrid " +
                     "semantic search scoped to the playbook's knowledge sources (when configured) or " +
                     "across all tenant sources. Both methods register citations so the LLM can use [N] " +
                     "markers in its response.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("knowledgeSourceId", "Knowledge source ID (GUID of a sprk_content record) to retrieve (GetKnowledgeSource method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("query", "Search query text (SearchKnowledgeBase method).", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("topK", "Max number of results (SearchKnowledgeBase method). Default 5, max 20.", ToolParameterType.Integer, Required: false, DefaultValue: DefaultTopK)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

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

        // Resolve method from the tool's configuration (per-row discriminator).
        var method = ResolveMethod(tool.Configuration);
        if (!SupportedMethods.Contains(method))
        {
            return ToolValidationResult.Failure(
                $"Configured method '{method}' is not supported. Use 'GetKnowledgeSource' or 'SearchKnowledgeBase'.");
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (string.Equals(method, MethodGetKnowledgeSource, StringComparison.Ordinal))
            {
                if (!doc.RootElement.TryGetProperty("knowledgeSourceId", out var idProp) ||
                    idProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(idProp.GetString()))
                {
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'knowledgeSourceId' string field for the GetKnowledgeSource method.");
                }
            }
            else
            {
                // SearchKnowledgeBase requires 'query'
                if (!doc.RootElement.TryGetProperty("query", out var queryProp) ||
                    queryProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(queryProp.GetString()))
                {
                    return ToolValidationResult.Failure(
                        "Tool arguments must include a non-empty 'query' string field for the SearchKnowledgeBase method.");
                }
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
    /// Playbook-context path. Reads <c>knowledgeSourceId</c> / <c>query</c> + <c>method</c>
    /// from the tool's <see cref="AnalysisTool.Configuration"/> JSON. Does NOT emit the
    /// widget envelope (playbook orchestration has no SSE channel). Citations are still
    /// returned via <see cref="ToolResult.Metadata"/> — if a future playbook step wires an
    /// accumulator it will be honored.
    /// </remarks>
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var method = ResolveMethod(tool.Configuration);

        try
        {
            _logger.LogInformation(
                "KnowledgeRetrievalHandler executing method '{Method}' for analysis {AnalysisId}, tool {ToolId}",
                method, context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            // Playbook-path args come from Configuration (the playbook author wires them);
            // there is no caller-supplied chat argument JSON.
            var args = ParseConfigurationArgs(tool.Configuration);

            return await DispatchAsync(
                method: method,
                tenantId: context.TenantId,
                knowledgeSourceId: args.KnowledgeSourceId,
                query: args.Query,
                topK: args.TopK ?? DefaultTopK,
                knowledgeSourceIds: null, // No knowledge scope in playbook path (FR-12 path is forward-compat)
                tool: tool,
                emitWidget: false,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: context.AnalysisId.ToString(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "KnowledgeRetrievalHandler cancelled for analysis {AnalysisId} method '{Method}'",
                context.AnalysisId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KnowledgeRetrievalHandler failed for analysis {AnalysisId} method '{Method}': {ErrorType}",
                context.AnalysisId, method, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
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

        try
        {
            _logger.LogInformation(
                "KnowledgeRetrievalHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(context.ToolArgumentsJson);

            // R6 Wave 7c: playbook knowledge scope (when present) restricts SearchKnowledgeBase
            // to the playbook's knowledge sources.
            IReadOnlyList<string>? knowledgeSourceIds =
                context.KnowledgeScope?.RagKnowledgeSourceIds is { Count: > 0 } ids
                    ? ids
                    : null;

            return await DispatchAsync(
                method: method,
                tenantId: context.TenantId,
                knowledgeSourceId: args.KnowledgeSourceId,
                query: args.Query,
                topK: args.TopK ?? DefaultTopK,
                knowledgeSourceIds: knowledgeSourceIds,
                tool: tool,
                emitWidget: true,
                startedAt: startedAt,
                stopwatch: stopwatch,
                correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}",
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "KnowledgeRetrievalHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}, method '{Method}'",
                context.ChatSessionId, context.DecisionId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KnowledgeRetrievalHandler chat failed for session {ChatSessionId}, decision {DecisionId}, method '{Method}': {ErrorType}",
                context.ChatSessionId, context.DecisionId, method, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Dispatcher
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DispatchAsync(
        string method,
        string tenantId,
        string? knowledgeSourceId,
        string? query,
        int topK,
        IReadOnlyList<string>? knowledgeSourceIds,
        AnalysisTool tool,
        bool emitWidget,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // Method-specific arg validation (also enforced by ValidateChat for the chat path;
        // re-checked here so the playbook path is symmetric).
        if (string.Equals(method, MethodGetKnowledgeSource, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(knowledgeSourceId))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "knowledgeSourceId is required for the GetKnowledgeSource method.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            return await ExecuteGetKnowledgeSourceAsync(
                tenantId, knowledgeSourceId!, tool, emitWidget, startedAt, stopwatch, correlationLogId, cancellationToken);
        }

        // SearchKnowledgeBase
        if (string.IsNullOrWhiteSpace(query))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "query is required for the SearchKnowledgeBase method.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        return await ExecuteSearchKnowledgeBaseAsync(
            tenantId, query!, topK, knowledgeSourceIds, tool, startedAt, stopwatch, correlationLogId, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GetKnowledgeSource
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteGetKnowledgeSourceAsync(
        string tenantId,
        string knowledgeSourceId,
        AnalysisTool tool,
        bool emitWidget,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            KnowledgeSourceId = knowledgeSourceId,
            TopK = GetKnowledgeSourceTopK,
            MinScore = 0.0f, // Return all content for this source
            UseSemanticRanking = false,
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        var response = await _ragService.SearchAsync("*", options, cancellationToken);

        stopwatch.Stop();

        if (response.Results.Count == 0)
        {
            _logger.LogInformation(
                "KnowledgeRetrievalHandler ({Correlation}) GetKnowledgeSource empty in {Duration}ms",
                correlationLogId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new KnowledgeRetrievalPayload
                {
                    Method = MethodGetKnowledgeSource,
                    Message = $"No content found for knowledge source '{knowledgeSourceId}'.",
                    ResultCount = 0
                },
                summary: $"No content found for knowledge source '{knowledgeSourceId}'.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var documentName = response.Results[0].DocumentName;
        var formatted = FormatGetKnowledgeSourceText(knowledgeSourceId, response);

        // Build citation envelopes — adapter accumulates into per-turn CitationContext.
        var citations = response.Results.Select(r => new ToolResultCitation(
            ChunkId: r.Id,
            SourceName: r.DocumentName,
            PageNumber: null, // page number not available in knowledge index
            Excerpt: r.Content)).ToArray();

        var metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations
        };

        if (emitWidget)
        {
            // R6 Wave 7c: source_pane / DocumentViewer widget — preserves the pre-R6 Gap-1
            // fix where GetKnowledgeSource emits a source-pane widget alongside the text
            // response. Adapter routes paneType="source_pane" to ChatSseEventFactory.
            metadata[ToolResultMetadataKeys.Widget] = new ToolResultWidget(
                PaneType: "source_pane",
                WidgetType: "DocumentViewer",
                Data: new
                {
                    knowledgeSourceId,
                    documentName,
                    chunkCount = response.Results.Count,
                    totalCount = response.TotalCount
                });
        }

        // ADR-015: count + outcome + duration only. Never query content, never excerpts.
        _logger.LogInformation(
            "KnowledgeRetrievalHandler ({Correlation}) GetKnowledgeSource ok resultCount={ResultCount} in {Duration}ms",
            correlationLogId, response.Results.Count, stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new KnowledgeRetrievalPayload
            {
                Method = MethodGetKnowledgeSource,
                Content = formatted,
                ResultCount = response.Results.Count,
                TotalCount = response.TotalCount
            },
            summary: $"Retrieved {response.Results.Count} chunk(s) for knowledge source '{knowledgeSourceId}'.",
            confidence: 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0
            }) with
        {
            Metadata = metadata
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SearchKnowledgeBase
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteSearchKnowledgeBaseAsync(
        string tenantId,
        string query,
        int topK,
        IReadOnlyList<string>? knowledgeSourceIds,
        AnalysisTool tool,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        var clampedTopK = Math.Clamp(topK, 1, MaxTopK);

        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            TopK = clampedTopK,
            KnowledgeSourceIds = knowledgeSourceIds,
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true
        };

        var response = await _ragService.SearchAsync(query, options, cancellationToken);

        stopwatch.Stop();

        if (response.Results.Count == 0)
        {
            _logger.LogInformation(
                "KnowledgeRetrievalHandler ({Correlation}) SearchKnowledgeBase empty in {Duration}ms",
                correlationLogId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new KnowledgeRetrievalPayload
                {
                    Method = MethodSearchKnowledgeBase,
                    Message = "No knowledge base entries found.",
                    ResultCount = 0
                },
                summary: "No knowledge base entries found.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var formatted = FormatSearchKnowledgeBaseText(query, response);

        var citations = response.Results.Select(r => new ToolResultCitation(
            ChunkId: r.Id,
            SourceName: r.DocumentName,
            PageNumber: null,
            Excerpt: r.Content)).ToArray();

        var metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations
        };

        _logger.LogInformation(
            "KnowledgeRetrievalHandler ({Correlation}) SearchKnowledgeBase ok resultCount={ResultCount} in {Duration}ms",
            correlationLogId, response.Results.Count, stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new KnowledgeRetrievalPayload
            {
                Method = MethodSearchKnowledgeBase,
                Content = formatted,
                ResultCount = response.Results.Count,
                TotalCount = response.TotalCount
            },
            summary: $"Knowledge base search returned {response.Results.Count} result(s).",
            confidence: 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0
            }) with
        {
            Metadata = metadata
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static (string? KnowledgeSourceId, string? Query, int? TopK) ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return (null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null, null);

            var knowledgeSourceId = doc.RootElement.TryGetProperty("knowledgeSourceId", out var idProp)
                && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()
                : null;

            var query = doc.RootElement.TryGetProperty("query", out var queryProp)
                && queryProp.ValueKind == JsonValueKind.String
                ? queryProp.GetString()
                : null;

            int? topK = null;
            if (doc.RootElement.TryGetProperty("topK", out var topKProp)
                && topKProp.ValueKind == JsonValueKind.Number
                && topKProp.TryGetInt32(out var topKVal))
            {
                topK = topKVal;
            }

            return (knowledgeSourceId, query, topK);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static (string? KnowledgeSourceId, string? Query, int? TopK) ParseConfigurationArgs(string? configurationJson)
    {
        // Same shape — the playbook author wires args identically in sprk_configuration.
        return ParseChatArgs(configurationJson);
    }

    /// <summary>
    /// Read the <c>method</c> discriminator from the tool's configuration JSON. Defaults to
    /// <see cref="MethodSearchKnowledgeBase"/> when missing (the broader, less-destructive
    /// method); ValidateChat surfaces unsupported methods with a clear error.
    /// </summary>
    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return MethodSearchKnowledgeBase;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return MethodSearchKnowledgeBase;

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

        return MethodSearchKnowledgeBase;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Formatting (preserved from legacy KnowledgeRetrievalTools so chat output is
    // unchanged for end users)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FormatGetKnowledgeSourceText(string knowledgeSourceId, RagSearchResponse response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Knowledge source '{knowledgeSourceId}' contains {response.Results.Count} chunk(s):");
        sb.AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            // Citation marker uses 1-based ordinal. The adapter will assign real IDs in
            // chat context via the accumulator; the playbook path uses ordinal stand-ins.
            var marker = $"[{i + 1}]";
            sb.AppendLine($"Source {marker}: {result.DocumentName} (Chunk {result.ChunkIndex + 1}/{result.ChunkCount}, ID: {result.Id})");
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();
        }

        if (response.TotalCount > response.Results.Count)
        {
            sb.AppendLine($"Note: Showing {response.Results.Count} of {response.TotalCount} total chunks.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSearchKnowledgeBaseText(string query, RagSearchResponse response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Knowledge base search found {response.Results.Count} result(s) for: \"{query}\"");
        sb.AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            var marker = $"[{i + 1}]";
            sb.AppendLine($"Source {marker}: {result.DocumentName} (Relevance: {result.Score:P0})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Knowledge Source: {result.KnowledgeSourceName}");
            }
            sb.AppendLine($"    Chunk: {result.ChunkIndex + 1}/{result.ChunkCount}, ID: {result.Id}");
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error-result builders
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Knowledge retrieval was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Knowledge retrieval failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// method discriminator + the formatted text content the chat agent renders.
    /// </summary>
    public sealed class KnowledgeRetrievalPayload
    {
        /// <summary>Method that produced this payload — echo of the row's discriminator.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodSearchKnowledgeBase;

        /// <summary>Formatted text content (markdown-style) when results were returned.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Human-readable status message — populated when no results found.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Number of results returned (≤ <see cref="TotalCount"/>).</summary>
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        /// <summary>Total matching results before per-call top-K limit.</summary>
        [JsonPropertyName("totalCount")]
        public long TotalCount { get; set; }
    }
}
