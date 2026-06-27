using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler that searches the public web via Azure Bing Web Search v7 API
/// (R6 Wave 8). Replaces the legacy hardcoded <c>WebSearchTools</c> class previously
/// instantiated in <c>SprkChatAgentFactory.ResolveTools</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, one method</strong>: the only LLM-facing function is
/// <c>SearchWeb(query, maxResults)</c> — mirrors the legacy tool's single
/// <c>SearchWebAsync</c> method.
/// </para>
/// <para>
/// <strong>Capability gate (R6 Wave 7b infrastructure)</strong>: the corresponding
/// <c>sprk_analysistool</c> row sets <c>sprk_requiredcapability = "web_search"</c>
/// (= <see cref="PlaybookCapabilities.WebSearch"/>). The data-driven block in
/// <c>SprkChatAgentFactory.ResolveTools</c> applies <c>IsCapabilityGateSatisfied</c> at
/// session start and silently withholds this handler when the playbook's capability set
/// lacks the value. Standalone chat (no playbook, capabilities = <c>CoreCapabilities</c>)
/// does NOT include <c>web_search</c>, so this handler is unreachable from standalone chat
/// — preserving the pre-Wave-8 governance boundary enforced by the hardcoded
/// <c>if (capabilities.Contains(PlaybookCapabilities.WebSearch))</c> block that this
/// migration removes (ADR-015: external content governance).
/// </para>
/// <para>
/// <strong>Chat-only invocation</strong>: <see cref="SupportedInvocationContexts"/> =
/// <see cref="InvocationContextKind.Chat"/>. Web search makes no sense from a playbook
/// node (matches the legacy hardcoded registration which was wired only into the chat
/// agent factory). Playbook orchestration does not invoke this handler.
/// </para>
/// <para>
/// <strong>Scope-guided search (FR-10)</strong>: when
/// <see cref="ChatInvocationContext.KnowledgeScope"/> carries a non-empty
/// <see cref="ChatKnowledgeScope.ScopeSearchGuidance"/>, the handler prepends it to the
/// raw query as a free-text qualifier (e.g., "Westlaw LexisNexis" or "authoritative legal
/// sources") so Bing biases results toward authoritative domain sources. The handler reads
/// the scope on every invocation (it is no longer captured at construction like the legacy
/// per-session tool was — handlers are scoped instances, not per-session factory-built).
/// </para>
/// <para>
/// <strong>Resilience contract (preserved verbatim from legacy)</strong>:
/// </para>
/// <list type="bullet">
/// <item>No Bing API key configured → graceful mock fallback with a Warning log.</item>
/// <item>Bing concurrency limit reached (max 2 concurrent calls, static
/// <see cref="SemaphoreSlim"/>) → graceful mock fallback with a degradation note.</item>
/// <item>HTTP timeout (5s per call) / HTTP failure / JSON parse failure → empty results
/// with a degradation note; never throws to the caller.</item>
/// </list>
/// <para>
/// <strong>Citations</strong>: per the R6 Wave 7b infrastructure contract, the handler
/// returns citation envelopes via <see cref="ToolResult.Metadata"/> using
/// <see cref="ToolResultMetadataKeys.Citations"/>. The
/// <c>ToolHandlerToAIFunctionAdapter</c> accumulates them into the per-chat-turn
/// <see cref="CitationContext"/> with <c>SourceType = "web"</c> (signals the frontend to
/// render the globe icon + [External Source] badge per ADR-015). Position-based confidence
/// scoring is computed in the adapter envelope (1st result = 0.95, linear decay to 10th =
/// 0.50) — preserved from the legacy tool's <c>RegisterCitations</c>.
/// </para>
/// <para>
/// <strong>Dependencies</strong>: <see cref="IHttpClientFactory"/> +
/// <see cref="IConfiguration"/> +
/// <see cref="ILogger{TCategoryName}"/>. Resolved via constructor injection (auto-discovered
/// by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>).
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered; ZERO manual DI line.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side
/// callers route through <c>PublicContracts</c> facades, never directly into this
/// handler.</item>
/// <item><strong>ADR-014</strong>: <see cref="ToolInvocationContextBase.TenantId"/> is
/// validated for log correlation. Bing is tenant-agnostic but tenant ID is enforced for
/// telemetry only.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + query LENGTH + result
/// count + timing ONLY. NEVER the query text or result bodies above Debug level.
/// Effective-query (with prepended scope guidance) is logged at Debug only.</item>
/// <item><strong>ADR-016</strong>: static <see cref="SemaphoreSlim"/> (max 2) bounds
/// concurrent Bing API calls — preserved verbatim from legacy. Static across all handler
/// instances (DI is scoped; the semaphore is process-wide).</item>
/// <item><strong>ADR-018</strong>: this is NOT a feature flag. The capability gate is
/// per-tool authorization (data-driven via <c>sprk_requiredcapability</c>), not a
/// service-registration kill switch.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size
/// delta ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class WebSearchHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(WebSearchHandler);

    /// <summary>
    /// Named HttpClient registration name for Bing Web Search API. Preserved from the
    /// legacy <c>WebSearchTools.HttpClientName</c> so the existing typed HttpClient
    /// registration continues to work without DI changes.
    /// </summary>
    public const string HttpClientName = "BingWebSearch";

    /// <summary>
    /// Bounds concurrent Bing API calls to max 2 per ADR-016. Static across all
    /// <see cref="WebSearchHandler"/> instances (handler is DI-scoped; the semaphore is
    /// process-wide — same shape as the legacy <c>WebSearchTools.s_bingConcurrencyGate</c>).
    /// </summary>
    private static readonly SemaphoreSlim s_bingConcurrencyGate = new(2, 2);

    /// <summary>
    /// Timeout for acquiring the concurrency semaphore before falling back to mock results.
    /// Preserved from legacy <c>WebSearchTools.s_semaphoreTimeout</c>.
    /// </summary>
    private static readonly TimeSpan s_semaphoreTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for individual Bing API HTTP requests. Preserved from legacy
    /// <c>WebSearchTools.s_httpTimeout</c>.
    /// </summary>
    private static readonly TimeSpan s_httpTimeout = TimeSpan.FromSeconds(5);

    private const int DefaultMaxResults = 5;
    private const int HardMaxResultsCap = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebSearchHandler> _logger;

    public WebSearchHandler(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WebSearchHandler> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Web Search",
        Description: "Search the web for information relevant to the user's query. " +
                     "Use this when the user asks a question that cannot be answered from internal documents " +
                     "alone — for example: recent news, industry regulations, public company information, " +
                     "or general knowledge topics. Results are marked as [External Source] to indicate they " +
                     "originate from the public web and have not been verified against internal knowledge.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "query",
                "Web search query.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "maxResults",
                "Maximum number of results to return. Defaults to 5; clamped to [1, 10].",
                ToolParameterType.Integer,
                Required: false,
                DefaultValue: DefaultMaxResults)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    /// <remarks>
    /// Chat-only. Web search has no playbook use case (mirrors the legacy hardcoded
    /// registration which was wired only into the chat agent factory).
    /// </remarks>
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        // Chat-only handler; the playbook path should never reach Validate. Return a
        // descriptive failure so an accidental playbook invocation is surfaced clearly.
        return ToolValidationResult.Failure(
            $"{HandlerIdValue} is chat-only and does not support playbook invocation.");
    }

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

            if (!doc.RootElement.TryGetProperty("query", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                return ToolValidationResult.Failure("Tool arguments must include a non-empty 'query' string field.");
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
    /// Chat-only handler — playbook orchestration must never invoke this path. Returns an
    /// error <see cref="ToolResult"/> rather than throwing so the caller sees a clean
    /// failure message.
    /// </remarks>
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"{HandlerIdValue} is chat-only and does not support playbook invocation.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var args = ParseChatArgs(context.ToolArgumentsJson);
            var query = args.Query;

            if (string.IsNullOrWhiteSpace(query))
            {
                stopwatch.Stop();
                return ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    "query is required.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });
            }

            // R6 Wave 7c: pull scope-guided search hint from the chat-attached knowledge
            // scope (FR-10). Reading per-invocation (vs legacy per-session capture) is
            // correct for the DI-scoped handler shape: the handler instance lives across
            // multiple chat turns; the scope is a per-turn property of the context.
            var scopeSearchGuidance = context.KnowledgeScope?.ScopeSearchGuidance;

            // Per-row configurable cap; clamp the LLM-supplied maxResults to that ceiling
            // (preserves legacy behavior where the per-tool config max was 10).
            var configMaxResults = ReadConfigMaxResults();
            var requestedMaxResults = args.MaxResults ?? DefaultMaxResults;
            var count = Math.Clamp(requestedMaxResults, 1, configMaxResults);

            // ADR-015: log query LENGTH + result count only — never the query text or
            // result bodies above Debug level.
            _logger.LogInformation(
                "WebSearchHandler chat-invocation start — session={ChatSessionId} decision={DecisionId} queryLen={QueryLen} maxResults={MaxResults}",
                context.ChatSessionId, context.DecisionId, query.Length, count);

            var apiKey = _configuration["BingSearch:ApiKey"];
            var endpoint = ReadConfigEndpoint();

            // No API key → graceful mock fallback with a Warning log.
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning(
                    "Bing API key not configured — using mock results for web search (session={ChatSessionId} decision={DecisionId})",
                    context.ChatSessionId, context.DecisionId);

                stopwatch.Stop();
                var mockResults = GenerateMockResults(count);
                return BuildToolResult(
                    tool, query, mockResults,
                    degradationNote: null,
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }

            // Apply scope-guided search: prepend guidance to the query if available (FR-10).
            var effectiveQuery = ApplyScopeGuidance(query, scopeSearchGuidance);

            // ADR-015: effective query may contain sensitive case terms; log at Debug only.
            _logger.LogDebug(
                "WebSearchHandler effective query (with scope guidance) — session={ChatSessionId} decision={DecisionId}: {EffectiveQuery}",
                context.ChatSessionId, context.DecisionId, effectiveQuery);

            // ADR-016: bound concurrent Bing API calls to max 2 via SemaphoreSlim.
            if (!await s_bingConcurrencyGate.WaitAsync(s_semaphoreTimeout, cancellationToken))
            {
                _logger.LogWarning(
                    "Bing API concurrency limit reached (max 2 concurrent calls) — falling back to mock results " +
                    "(session={ChatSessionId} decision={DecisionId})",
                    context.ChatSessionId, context.DecisionId);

                stopwatch.Stop();
                var mockResults = GenerateMockResults(count);
                return BuildToolResult(
                    tool, query, mockResults,
                    degradationNote: "Web search is temporarily limited. Results shown are from a fallback source.",
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }

            try
            {
                var results = await CallBingApiAsync(effectiveQuery, count, apiKey!, endpoint, cancellationToken);
                stopwatch.Stop();

                // ADR-015: counts only; never the query text or result content.
                _logger.LogInformation(
                    "WebSearchHandler chat-invocation ok — session={ChatSessionId} decision={DecisionId} queryLen={QueryLen} resultCount={ResultCount} duration={DurationMs}ms",
                    context.ChatSessionId, context.DecisionId, query.Length, results.Count, stopwatch.ElapsedMilliseconds);

                return BuildToolResult(
                    tool, query, results,
                    degradationNote: null,
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning(
                    "Bing API request timed out after {TimeoutSeconds}s — returning empty results " +
                    "(session={ChatSessionId} decision={DecisionId})",
                    s_httpTimeout.TotalSeconds, context.ChatSessionId, context.DecisionId);

                stopwatch.Stop();
                return BuildToolResult(
                    tool, query, results: Array.Empty<WebSearchResult>(),
                    degradationNote: "Web search timed out. Please try again or refine your query.",
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Bing API request failed with HTTP error — returning empty results " +
                    "(session={ChatSessionId} decision={DecisionId})",
                    context.ChatSessionId, context.DecisionId);

                stopwatch.Stop();
                return BuildToolResult(
                    tool, query, results: Array.Empty<WebSearchResult>(),
                    degradationNote: "Web search is temporarily unavailable. Please try again later.",
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse Bing API response — returning empty results " +
                    "(session={ChatSessionId} decision={DecisionId})",
                    context.ChatSessionId, context.DecisionId);

                stopwatch.Stop();
                return BuildToolResult(
                    tool, query, results: Array.Empty<WebSearchResult>(),
                    degradationNote: "Web search returned an unexpected response. Please try again.",
                    startedAt: startedAt,
                    stopwatch: stopwatch,
                    correlationLogId: $"session={context.ChatSessionId},decision={context.DecisionId}");
            }
            finally
            {
                s_bingConcurrencyGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "WebSearchHandler chat cancelled — session={ChatSessionId} decision={DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return BuildCancelledResult(tool, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WebSearchHandler chat failed — session={ChatSessionId} decision={DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return BuildInternalErrorResult(tool, ex, startedAt);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static (string? Query, int? MaxResults) ParseChatArgs(string? toolArgumentsJson)
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

            int? maxResults = null;
            if (doc.RootElement.TryGetProperty("maxResults", out var maxProp)
                && maxProp.ValueKind == JsonValueKind.Number
                && maxProp.TryGetInt32(out var maxVal))
            {
                maxResults = maxVal;
            }

            return (query, maxResults);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration accessors
    // ─────────────────────────────────────────────────────────────────────────────

    private int ReadConfigMaxResults()
    {
        var configured = _configuration["BingSearch:MaxResults"];
        if (int.TryParse(configured, out var parsed))
            return Math.Clamp(parsed, 1, HardMaxResultsCap);

        return HardMaxResultsCap;
    }

    private string ReadConfigEndpoint()
    {
        var endpoint = _configuration["BingSearch:Endpoint"];
        return string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.bing.microsoft.com/v7.0/search"
            : endpoint;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scope-guided search (FR-10)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies scope search guidance to the query by prepending guidance terms — preserves
    /// the verbatim behavior of <c>WebSearchTools.ApplyScopeGuidance</c>.
    /// </summary>
    internal static string ApplyScopeGuidance(string query, string? scopeSearchGuidance)
    {
        if (string.IsNullOrWhiteSpace(scopeSearchGuidance))
            return query;

        // Prepend scope guidance as a free-text qualifier. Bing handles natural language
        // well — prepending "Westlaw LexisNexis" or "authoritative legal sources"
        // naturally biases results toward those sources.
        return $"{scopeSearchGuidance} {query}";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bing API integration
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the Bing Web Search v7 API and returns parsed search results.
    /// </summary>
    private async Task<List<WebSearchResult>> CallBingApiAsync(
        string query, int count, string apiKey, string endpoint, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = s_httpTimeout;

        var requestUri = $"{endpoint}?q={Uri.EscapeDataString(query)}&count={count}&mkt=en-US";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Bing API returned non-success status {StatusCode}",
                (int)response.StatusCode);
            return new List<WebSearchResult>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var bingResponse = JsonSerializer.Deserialize<BingSearchResponse>(json);

        if (bingResponse?.WebPages?.Value is not { Count: > 0 })
        {
            _logger.LogInformation("Bing API returned no web page results");
            return new List<WebSearchResult>();
        }

        return bingResponse.WebPages.Value
            .Take(count)
            .Select((r, i) => new WebSearchResult(
                Title: r.Name ?? "Untitled",
                Url: r.Url ?? string.Empty,
                Snippet: TruncateSnippet(r.Snippet ?? string.Empty, 500),
                Position: i + 1))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Result assembly
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical <see cref="ToolResult"/> for a (real or mock) result list.
    /// Carries citations via <see cref="ToolResult.Metadata"/>; the adapter accumulates
    /// them into the per-turn <see cref="CitationContext"/>. Each citation uses
    /// <c>SourceType = "web"</c> so the frontend renders the globe icon + [External
    /// Source] badge (ADR-015).
    /// </summary>
    private ToolResult BuildToolResult(
        AnalysisTool tool,
        string originalQuery,
        IReadOnlyList<WebSearchResult> results,
        string? degradationNote,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId)
    {
        // Build the citation envelopes the adapter forwards into the per-turn CitationContext.
        // Each web result is tagged SourceType = "web" so the frontend renders the
        // [External Source] badge (ADR-015). The adapter assigns sequential CitationIds.
        var citations = results.Select(r =>
        {
            var truncated = TruncateSnippet(r.Snippet, CitationContext.MaxExcerptLength);
            return new ToolResultCitation(
                ChunkId: r.Url,
                SourceName: r.Title,
                PageNumber: null,
                Excerpt: truncated,
                SourceType: "web",
                Url: r.Url,
                Snippet: truncated);
        }).ToArray();

        var formattedText = FormatResults(results, degradationNote);
        var summary = string.IsNullOrEmpty(degradationNote)
            ? $"Web search returned {results.Count} result(s)."
            : $"Web search returned {results.Count} result(s) (degraded).";

        var metadata = new Dictionary<string, object?>
        {
            [ToolResultMetadataKeys.Citations] = citations
        };

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new WebSearchPayload
            {
                Content = formattedText,
                ResultCount = results.Count,
                DegradationNote = degradationNote
            },
            summary: summary,
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

    /// <summary>
    /// Formats search results into an AI-readable text block with inline [N] markers.
    /// Preserves the legacy <c>WebSearchTools.FormatResults</c> shape so the LLM sees the
    /// same text format it expects.
    /// </summary>
    private static string FormatResults(IReadOnlyList<WebSearchResult> results, string? degradationNote)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Web search returned {results.Count} result(s).");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(degradationNote))
        {
            sb.AppendLine($"**Note**: {degradationNote}");
            sb.AppendLine();
        }

        sb.AppendLine("**Note**: These results are from external web sources and have not been verified against internal knowledge.");
        sb.AppendLine();

        foreach (var result in results)
        {
            sb.AppendLine($"[{result.Position}] [External Source] {result.Title} - {result.Url}");
            sb.AppendLine($"    {result.Snippet}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Mock fallback
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates mock search results for development and testing. Used when Bing API key
    /// is not configured or when concurrency limit is reached. Preserved verbatim from
    /// legacy <c>WebSearchTools.GenerateMockResults</c>.
    /// </summary>
    internal static List<WebSearchResult> GenerateMockResults(int count)
    {
        var allMockResults = new List<WebSearchResult>
        {
            new(
                Title: "Understanding Legal Document Analysis Best Practices",
                Url: "https://www.example.com/legal-document-analysis-guide",
                Snippet: "A comprehensive guide to modern document analysis techniques including AI-assisted review, " +
                         "key clause extraction, and automated compliance checking for legal professionals.",
                Position: 1),
            new(
                Title: "Microsoft Graph API Documentation - SharePoint Embedded",
                Url: "https://learn.microsoft.com/en-us/graph/api/resources/sharepoint-embedded",
                Snippet: "Official documentation for SharePoint Embedded (SPE) APIs via Microsoft Graph, " +
                         "covering container management, file operations, and permission models.",
                Position: 2),
            new(
                Title: "Azure AI Services - Document Intelligence Overview",
                Url: "https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview",
                Snippet: "Azure AI Document Intelligence uses machine learning models to automate data extraction " +
                         "from documents, forms, and invoices with high accuracy.",
                Position: 3),
            new(
                Title: "Industry Trends in Legal Technology 2026",
                Url: "https://www.example.com/legaltech-trends-2026",
                Snippet: "Emerging trends in legal technology for 2026, including AI-powered contract review, " +
                         "predictive analytics for case outcomes, and integrated collaboration platforms.",
                Position: 4),
            new(
                Title: "Data Governance Best Practices for AI Systems",
                Url: "https://www.example.com/ai-data-governance-best-practices",
                Snippet: "Guidelines for implementing responsible data governance in AI-powered systems, " +
                         "covering data minimization, audit trails, and privacy-by-design principles.",
                Position: 5)
        };

        return allMockResults.Take(count).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Truncates a snippet to the specified maximum length, appending "..." if truncated.
    /// </summary>
    private static string TruncateSnippet(string snippet, int maxLength) =>
        snippet.Length > maxLength ? snippet[..maxLength] + "..." : snippet;

    // ─────────────────────────────────────────────────────────────────────────────
    // Error-result builders
    // ─────────────────────────────────────────────────────────────────────────────

    private ToolResult BuildCancelledResult(AnalysisTool tool, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "Web search was cancelled.",
            ToolErrorCodes.Cancelled,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    private ToolResult BuildInternalErrorResult(AnalysisTool tool, Exception ex, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            $"Web search failed: {ex.Message}",
            ToolErrorCodes.InternalError,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow });

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. The LLM reads
    /// <see cref="Content"/> (the formatted text block with [N] markers); the frontend
    /// renders citations from <see cref="ToolResult.Metadata"/>.
    /// </summary>
    public sealed class WebSearchPayload
    {
        /// <summary>Formatted text content (markdown-style) with [N] citation markers.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Number of results returned.</summary>
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        /// <summary>
        /// Human-readable degradation note when the result list is a mock fallback or
        /// the upstream API returned an error. Null on the happy path.
        /// </summary>
        [JsonPropertyName("degradationNote")]
        public string? DegradationNote { get; set; }
    }

    /// <summary>
    /// Internal record representing a single web search result. Used for both mock results
    /// and Bing API response mapping. Internal so tests in the same assembly can construct
    /// fixtures without exposing the type publicly.
    /// </summary>
    internal sealed record WebSearchResult(string Title, string Url, string Snippet, int Position);

    /// <summary>
    /// Bing Web Search v7 API response model (partial — only fields we need).
    /// </summary>
    private sealed class BingSearchResponse
    {
        [JsonPropertyName("webPages")]
        public BingWebPages? WebPages { get; set; }
    }

    private sealed class BingWebPages
    {
        [JsonPropertyName("value")]
        public List<BingWebPage>? Value { get; set; }
    }

    private sealed class BingWebPage
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }
    }
}
