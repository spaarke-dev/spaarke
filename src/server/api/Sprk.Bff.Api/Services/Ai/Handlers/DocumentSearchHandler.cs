using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side (and playbook-side) typed handler that performs document vector search over the
/// tenant's knowledge index (R6 Wave 8). Replaces the legacy hardcoded
/// <c>DocumentSearchTools</c> class previously instantiated in
/// <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two methods</strong>: two <c>sprk_analysistool</c> rows
/// (<c>DOCUMENT-SEARCH</c> and <c>DOCUMENT-DISCOVERY</c>) share this single
/// <c>sprk_handlerclass = DocumentSearchHandler</c>. Each row's
/// <c>sprk_configuration.method</c> selects the internal method. This mirrors the
/// <see cref="KnowledgeRetrievalHandler"/> shape (R6 Wave 7c) — distinct LLM tools with
/// distinct descriptions + behavior, one handler class.
/// </para>
/// <list type="bullet">
/// <item><c>method = "SearchDocuments"</c> — targeted hybrid (semantic + vector + keyword)
/// search against the knowledge index, scoped to the playbook's knowledge sources when
/// <see cref="ChatInvocationContext.KnowledgeScope"/> carries
/// <c>RagKnowledgeSourceIds</c>. Emits an <c>output_pane</c> <c>SearchResults</c> widget
/// alongside the text response (preserves the pre-R6 Gap-1 fix behavior).</item>
/// <item><c>method = "SearchDiscovery"</c> — broad discovery search across all indexed
/// documents for the tenant (not knowledge-scoped). When the playbook supplies a
/// <c>ParentEntityType</c>/<c>ParentEntityId</c> on its knowledge scope, discovery is
/// constrained to the parent entity boundary; otherwise it is tenant-wide. Uses a lower
/// <c>MinScore</c> threshold (0.5) to cast a wider net, and truncates excerpts to 300
/// chars for preview. Also emits an <c>output_pane</c> <c>SearchResults</c> widget with
/// <c>isDiscovery=true</c>.</item>
/// </list>
/// <para>
/// <strong>Citations + widget events</strong>: per the R6 Wave 7b infrastructure contract,
/// the handler returns citation envelopes + widget envelopes via
/// <see cref="ToolResult.Metadata"/> using <see cref="ToolResultMetadataKeys.Citations"/> +
/// <see cref="ToolResultMetadataKeys.Widget"/>. The <c>ToolHandlerToAIFunctionAdapter</c>
/// accumulates citations into the per-chat-turn <c>CitationContext</c> and emits the SSE
/// event via the captured writer delegate.
/// </para>
/// <para>
/// <strong>Capability gate</strong>: NOT set. Per Wave 8 design, both DocumentSearch and
/// DocumentDiscovery should be available to ALL playbooks with a working
/// <see cref="IRagService"/> registration — mirroring the legacy hardcoded behavior where
/// the only gate was the presence of <see cref="IRagService"/>. The corresponding
/// <c>sprk_requiredcapability</c> column is null/empty (always-available per Wave 7b
/// filter semantics).
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IRagService"/> only. Resolved via constructor
/// injection (auto-discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Both"/>. Playbook
/// path reads <c>query</c> / <c>topK</c> from <see cref="AnalysisTool.Configuration"/> and
/// does NOT emit the widget envelope (playbook orchestration has no SSE channel). Chat path
/// reads from <see cref="ChatInvocationContext.ToolArgumentsJson"/> and returns both
/// citations + widget.
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
/// adds an optional playbook-level filter on top of the tenant filter
/// (SearchDocuments uses RagKnowledgeSourceIds; SearchDiscovery uses ParentEntity scoping).</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + IDs + method discriminator
/// + result count + duration ONLY. NEVER the query text, citation excerpt content, or
/// widget data content. Query text MAY appear at <see cref="LogLevel.Debug"/> only.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class DocumentSearchHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DocumentSearchHandler);

    internal const string MethodSearchDocuments = "SearchDocuments";
    internal const string MethodSearchDiscovery = "SearchDiscovery";

    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        MethodSearchDocuments,
        MethodSearchDiscovery
    };

    private const int DefaultDocumentsTopK = 5;
    private const int DefaultDiscoveryTopK = 10;
    private const int MaxTopK = 20;
    private const float DiscoveryMinScore = 0.5f;
    private const int DiscoveryExcerptCap = 300;
    private const int DocumentsExcerptCap = 400;

    private readonly IRagService _ragService;
    private readonly ILogger<DocumentSearchHandler> _logger;

    public DocumentSearchHandler(
        IRagService ragService,
        ILogger<DocumentSearchHandler> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Document Search",
        Description: "Performs hybrid vector + semantic + keyword search across indexed document content. " +
                     "Provides two methods: 'SearchDocuments' is a targeted search scoped to the playbook's " +
                     "knowledge sources (when bound) and is best for specific topics, clauses, or information; " +
                     "'SearchDiscovery' is a broad discovery search across all indexed documents for the " +
                     "tenant (or scoped to a parent entity when host context is provided), useful for " +
                     "exploring what documents are available or finding related content across the corpus. " +
                     "Both methods register citations so the LLM can use [N] markers in its response and emit " +
                     "an output-pane SearchResults widget for the UI.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("query", "Search query text.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("topK", "Max number of results. Default 5 (SearchDocuments) / 10 (SearchDiscovery), max 20.", ToolParameterType.Integer, Required: false)
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

        var method = ResolveMethod(tool.Configuration);
        if (!SupportedMethods.Contains(method))
        {
            return ToolValidationResult.Failure(
                $"Configured method '{method}' is not supported. Use 'SearchDocuments' or 'SearchDiscovery'.");
        }

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (!doc.RootElement.TryGetProperty("query", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'query' string field.");
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
    /// Playbook-context path. Reads <c>query</c> / <c>topK</c> + <c>method</c> from the tool's
    /// <see cref="AnalysisTool.Configuration"/> JSON. Does NOT emit the widget envelope
    /// (playbook orchestration has no SSE channel). Citations are still returned via
    /// <see cref="ToolResult.Metadata"/> — if a future playbook step wires an accumulator
    /// it will be honored.
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
                "DocumentSearchHandler executing method '{Method}' for analysis {AnalysisId}, tool {ToolId}",
                method, context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(tool.Configuration);

            return await DispatchAsync(
                method: method,
                tenantId: context.TenantId,
                query: args.Query,
                topK: args.TopK,
                knowledgeSourceIds: null, // No playbook-level scope on the playbook path (forward-compat)
                parentEntityType: null,
                parentEntityId: null,
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
                "DocumentSearchHandler cancelled for analysis {AnalysisId} method '{Method}'",
                context.AnalysisId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentSearchHandler failed for analysis {AnalysisId} method '{Method}': {ErrorType}",
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
                "DocumentSearchHandler chat-invocation method '{Method}' for session {ChatSessionId}, decision {DecisionId}",
                method, context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(context.ToolArgumentsJson);

            // SearchDocuments uses knowledge-source-ID scoping (when bound to a playbook).
            // SearchDiscovery uses parent-entity scoping (when the playbook supplies a host context).
            IReadOnlyList<string>? knowledgeSourceIds =
                context.KnowledgeScope?.RagKnowledgeSourceIds is { Count: > 0 } ids
                    ? ids
                    : null;
            var parentEntityType = context.KnowledgeScope?.ParentEntityType;
            var parentEntityId = context.KnowledgeScope?.ParentEntityId;

            return await DispatchAsync(
                method: method,
                tenantId: context.TenantId,
                query: args.Query,
                topK: args.TopK,
                knowledgeSourceIds: knowledgeSourceIds,
                parentEntityType: parentEntityType,
                parentEntityId: parentEntityId,
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
                "DocumentSearchHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}, method '{Method}'",
                context.ChatSessionId, context.DecisionId, method);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentSearchHandler chat failed for session {ChatSessionId}, decision {DecisionId}, method '{Method}': {ErrorType}",
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
        string? query,
        int? topK,
        IReadOnlyList<string>? knowledgeSourceIds,
        string? parentEntityType,
        string? parentEntityId,
        AnalysisTool tool,
        bool emitWidget,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "query is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        if (string.Equals(method, MethodSearchDocuments, StringComparison.Ordinal))
        {
            return await ExecuteSearchDocumentsAsync(
                tenantId, query!, topK ?? DefaultDocumentsTopK, knowledgeSourceIds, tool, emitWidget,
                startedAt, stopwatch, correlationLogId, cancellationToken);
        }

        // SearchDiscovery (the dispatcher's else branch — ValidateChat already enforced
        // the SupportedMethods constraint on the chat path; playbook path falls through to
        // discovery if the configuration discriminator is missing/unknown, matching the
        // less-destructive default convention used by KnowledgeRetrievalHandler).
        return await ExecuteSearchDiscoveryAsync(
            tenantId, query!, topK ?? DefaultDiscoveryTopK, parentEntityType, parentEntityId, tool, emitWidget,
            startedAt, stopwatch, correlationLogId, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SearchDocuments — targeted, knowledge-scoped
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteSearchDocumentsAsync(
        string tenantId,
        string query,
        int topK,
        IReadOnlyList<string>? knowledgeSourceIds,
        AnalysisTool tool,
        bool emitWidget,
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
                "DocumentSearchHandler ({Correlation}) SearchDocuments empty in {Duration}ms",
                correlationLogId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new DocumentSearchPayload
                {
                    Method = MethodSearchDocuments,
                    Message = "No relevant documents found for the given query.",
                    ResultCount = 0
                },
                summary: "No relevant documents found.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        var formatted = FormatSearchDocumentsText(query, response);

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
            // Build widget items (preserves the pre-R6 SearchResults widget shape used by the
            // frontend; output_pane → adapter SSE emission).
            var widgetItems = response.Results.Select((r, i) => (object)new
            {
                chunkId = r.Id,
                documentId = r.DocumentId,
                documentName = r.DocumentName,
                knowledgeSourceName = r.KnowledgeSourceName,
                score = r.Score,
                chunkIndex = r.ChunkIndex,
                chunkCount = r.ChunkCount,
                excerpt = r.Content.Length > DocumentsExcerptCap
                    ? r.Content[..DocumentsExcerptCap] + "…"
                    : r.Content,
                citationMarker = $"[{i + 1}]"
            }).ToArray();

            metadata[ToolResultMetadataKeys.Widget] = new ToolResultWidget(
                PaneType: "output_pane",
                WidgetType: "SearchResults",
                Data: new { query, results = widgetItems });
        }

        // ADR-015: count + outcome + duration only. Never query content, never excerpts at Info.
        _logger.LogInformation(
            "DocumentSearchHandler ({Correlation}) SearchDocuments ok resultCount={ResultCount} in {Duration}ms",
            correlationLogId, response.Results.Count, stopwatch.ElapsedMilliseconds);

        // ADR-015: query text MAY appear at Debug level only.
        _logger.LogDebug(
            "DocumentSearchHandler ({Correlation}) SearchDocuments queryLen={QueryLen}",
            correlationLogId, query.Length);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new DocumentSearchPayload
            {
                Method = MethodSearchDocuments,
                Content = formatted,
                ResultCount = response.Results.Count,
                TotalCount = response.TotalCount
            },
            summary: $"Found {response.Results.Count} relevant document(s).",
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
    // SearchDiscovery — broad, tenant-wide (or parent-entity scoped)
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ExecuteSearchDiscoveryAsync(
        string tenantId,
        string query,
        int topK,
        string? parentEntityType,
        string? parentEntityId,
        AnalysisTool tool,
        bool emitWidget,
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
            MinScore = DiscoveryMinScore, // wider net for discovery
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true,
            // Entity scope: when host context is present, constrain discovery to the parent
            // entity boundary; otherwise discovery remains tenant-wide (backward compatible).
            ParentEntityType = parentEntityType,
            ParentEntityId = parentEntityId
        };

        var response = await _ragService.SearchAsync(query, options, cancellationToken);

        stopwatch.Stop();

        if (response.Results.Count == 0)
        {
            _logger.LogInformation(
                "DocumentSearchHandler ({Correlation}) SearchDiscovery empty in {Duration}ms",
                correlationLogId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new DocumentSearchPayload
                {
                    Method = MethodSearchDiscovery,
                    Message = "No documents discovered matching the given query.",
                    ResultCount = 0
                },
                summary: "No documents discovered.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
        }

        // Discovery truncates content for preview (matches legacy DocumentSearchTools behavior).
        var previews = response.Results
            .Select(r => r.Content.Length > DiscoveryExcerptCap
                ? r.Content[..DiscoveryExcerptCap] + "..."
                : r.Content)
            .ToArray();

        var formatted = FormatSearchDiscoveryText(query, response, previews);

        var citations = response.Results.Select((r, i) => new ToolResultCitation(
            ChunkId: r.Id,
            SourceName: r.DocumentName,
            PageNumber: null,
            Excerpt: previews[i])).ToArray();

        var metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations
        };

        if (emitWidget)
        {
            var widgetItems = response.Results.Select((r, i) => (object)new
            {
                chunkId = r.Id,
                documentId = r.DocumentId,
                documentName = r.DocumentName,
                knowledgeSourceName = r.KnowledgeSourceName,
                score = r.Score,
                chunkIndex = r.ChunkIndex,
                chunkCount = r.ChunkCount,
                excerpt = previews[i],
                citationMarker = $"[{i + 1}]"
            }).ToArray();

            metadata[ToolResultMetadataKeys.Widget] = new ToolResultWidget(
                PaneType: "output_pane",
                WidgetType: "SearchResults",
                Data: new { query, results = widgetItems, isDiscovery = true });
        }

        _logger.LogInformation(
            "DocumentSearchHandler ({Correlation}) SearchDiscovery ok resultCount={ResultCount} in {Duration}ms",
            correlationLogId, response.Results.Count, stopwatch.ElapsedMilliseconds);

        _logger.LogDebug(
            "DocumentSearchHandler ({Correlation}) SearchDiscovery queryLen={QueryLen}",
            correlationLogId, query.Length);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new DocumentSearchPayload
            {
                Method = MethodSearchDiscovery,
                Content = formatted,
                ResultCount = response.Results.Count,
                TotalCount = response.TotalCount
            },
            summary: $"Discovery search found {response.Results.Count} document(s).",
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

    private static (string? Query, int? TopK) ParseChatArgs(string? toolArgumentsJson)
    {
        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

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

            return (query, topK);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Read the <c>method</c> discriminator from the tool's configuration JSON. Defaults to
    /// <see cref="MethodSearchDocuments"/> when missing (the narrower, knowledge-scoped
    /// method); ValidateChat surfaces unsupported methods with a clear error.
    /// </summary>
    private static string ResolveMethod(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return MethodSearchDocuments;

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return MethodSearchDocuments;

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

        return MethodSearchDocuments;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Formatting (preserved from legacy DocumentSearchTools so chat output is
    // unchanged for end users)
    // ─────────────────────────────────────────────────────────────────────────────

    private static string FormatSearchDocumentsText(string query, RagSearchResponse response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Found {response.Results.Count} relevant document(s) for query: \"{query}\"");
        sb.AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var r = response.Results[i];
            var marker = $"[{i + 1}]";
            sb.AppendLine($"Source {marker}: {r.DocumentName} (Relevance: {r.Score:P0})");
            if (!string.IsNullOrWhiteSpace(r.KnowledgeSourceName))
            {
                sb.AppendLine($"    Knowledge Source: {r.KnowledgeSourceName}");
            }
            sb.AppendLine($"    Chunk: {r.ChunkIndex + 1}/{r.ChunkCount}, ID: {r.Id}");
            sb.AppendLine($"    {r.Content}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSearchDiscoveryText(string query, RagSearchResponse response, IReadOnlyList<string> previews)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Discovery search found {response.Results.Count} document(s) for: \"{query}\"");
        sb.AppendLine();

        for (var i = 0; i < response.Results.Count; i++)
        {
            var r = response.Results[i];
            var marker = $"[{i + 1}]";
            sb.AppendLine($"Source {marker}: {r.DocumentName} (Score: {r.Score:F2})");
            if (!string.IsNullOrWhiteSpace(r.KnowledgeSourceName))
            {
                sb.AppendLine($"    Collection: {r.KnowledgeSourceName}");
            }
            sb.AppendLine($"    Chunk: {r.ChunkIndex + 1}/{r.ChunkCount}, ID: {r.Id}");
            sb.AppendLine($"    {previews[i]}");
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
            "Document search was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Document search failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. Carries the
    /// method discriminator + the formatted text content the chat agent renders.
    /// </summary>
    public sealed class DocumentSearchPayload
    {
        /// <summary>Method that produced this payload — echo of the row's discriminator.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = MethodSearchDocuments;

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
