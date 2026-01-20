using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for semantic search across documents.
/// Enables Copilot to search knowledge bases via the tool framework.
/// </summary>
/// <remarks>
/// <para>
/// Unlike other tool handlers that analyze a single document, this handler
/// searches across documents in a knowledge base. The search parameters are
/// provided via the tool Configuration JSON.
/// </para>
/// <para>
/// Configuration parameters:
/// - query: The search query text (required)
/// - scope: Search scope - "entity" or "documents" (default: "entity")
/// - entityType: Parent entity type when scope is "entity"
/// - entityId: Parent entity ID when scope is "entity"
/// - documentIds: Specific document IDs when scope is "documents"
/// - filters: Optional filters (documentTypes, fileTypes, tags, dateRange)
/// - limit: Maximum results to return (default: 10)
/// </para>
/// </remarks>
public sealed class SemanticSearchToolHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "SemanticSearchHandler";
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;

    private readonly ISemanticSearchService _searchService;
    private readonly ILogger<SemanticSearchToolHandler> _logger;

    public SemanticSearchToolHandler(
        ISemanticSearchService searchService,
        ILogger<SemanticSearchToolHandler> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "search_documents",
        Description: "Search documents in knowledge bases using semantic similarity and keyword matching. Use when users ask to find documents about a topic.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "application/json" },
        Parameters: new[]
        {
            new ToolParameterDefinition("query", "The search query text", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("scope", "Search scope: 'entity' (search within entity) or 'documents' (specific document IDs)", ToolParameterType.String, Required: false, DefaultValue: "entity"),
            new ToolParameterDefinition("entity_type", "Parent entity type (Matter, Project, Invoice, Account, Contact) when scope is 'entity'", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("entity_id", "Parent entity ID (GUID) when scope is 'entity'", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("document_ids", "Array of document IDs when scope is 'documents'", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("limit", "Maximum number of results (1-50, default: 10)", ToolParameterType.Integer, Required: false, DefaultValue: 10),
            new ToolParameterDefinition("filters", "Optional filters object with documentTypes, fileTypes, tags, dateRange", ToolParameterType.Object, Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Parse configuration to validate
        if (string.IsNullOrWhiteSpace(tool.Configuration))
        {
            errors.Add("Configuration with search parameters is required.");
            return ToolValidationResult.Failure(errors);
        }

        try
        {
            var config = ParseConfiguration(tool.Configuration);

            if (string.IsNullOrWhiteSpace(config.Query))
                errors.Add("Query parameter is required.");

            // Validate scope and required parameters
            var scope = config.Scope?.ToLowerInvariant() ?? "entity";
            if (scope == "entity")
            {
                if (string.IsNullOrWhiteSpace(config.EntityType))
                    errors.Add("entity_type is required when scope is 'entity'.");
                if (string.IsNullOrWhiteSpace(config.EntityId))
                    errors.Add("entity_id is required when scope is 'entity'.");
                else if (!Guid.TryParse(config.EntityId, out _))
                    errors.Add("entity_id must be a valid GUID.");
            }
            else if (scope == "documentids")
            {
                if (config.DocumentIds == null || config.DocumentIds.Length == 0)
                    errors.Add("document_ids is required when scope is 'documentIds'.");
            }
            else
            {
                errors.Add($"Invalid scope '{scope}'. Must be 'entity' or 'documentIds'.");
            }

            // Validate limit
            if (config.Limit is < 1 or > MaxLimit)
                errors.Add($"limit must be between 1 and {MaxLimit}.");
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid configuration JSON: {ex.Message}");
        }

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation(
                "Starting semantic search for analysis {AnalysisId}, tenant {TenantId}",
                context.AnalysisId, context.TenantId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration!);
            var limit = Math.Clamp(config.Limit ?? DefaultLimit, 1, MaxLimit);

            // Build search request
            var searchRequest = BuildSearchRequest(config, limit);

            // Execute search
            var response = await _searchService.SearchAsync(
                searchRequest,
                context.TenantId,
                cancellationToken);

            stopwatch.Stop();

            // Build result data
            var resultData = new SemanticSearchToolResult
            {
                Query = config.Query!,
                TotalResults = response.Metadata.TotalResults,
                ReturnedResults = response.Metadata.ReturnedResults,
                SearchDurationMs = response.Metadata.SearchDurationMs,
                ExecutedMode = response.Metadata.ExecutedMode,
                Results = response.Results.Select(r => new SearchResultItem
                {
                    DocumentId = r.DocumentId,
                    Name = r.Name,
                    DocumentType = r.DocumentType,
                    FileType = r.FileType,
                    Score = r.CombinedScore,
                    Highlights = r.Highlights,
                    ParentEntityType = r.ParentEntityType,
                    ParentEntityName = r.ParentEntityName
                }).ToList()
            };

            // Build summary for Copilot
            var summary = BuildSummary(resultData);

            // Calculate confidence based on result quality
            var confidence = CalculateConfidence(response);

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 1 // Embedding generation counts as 1 model call
            };

            _logger.LogInformation(
                "Semantic search completed for {AnalysisId}: {ResultCount}/{TotalCount} results in {Duration}ms",
                context.AnalysisId, response.Metadata.ReturnedResults,
                response.Metadata.TotalResults, stopwatch.ElapsedMilliseconds);

            // Include warnings from search response
            var warnings = response.Metadata.Warnings?
                .Select(w => $"{w.Code}: {w.Message}")
                .ToList() ?? new List<string>();

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary,
                confidence,
                executionMetadata,
                warnings.Count > 0 ? warnings : null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Semantic search cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Semantic search was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Semantic search failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Parses the configuration JSON into a typed object.
    /// </summary>
    private static SemanticSearchConfig ParseConfiguration(string configJson)
    {
        return JsonSerializer.Deserialize<SemanticSearchConfig>(configJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new SemanticSearchConfig();
    }

    /// <summary>
    /// Builds a SemanticSearchRequest from the configuration.
    /// </summary>
    private static SemanticSearchRequest BuildSearchRequest(SemanticSearchConfig config, int limit)
    {
        // Normalize scope for comparison, but use proper casing in request
        var scopeNormalized = config.Scope?.ToLowerInvariant() ?? "entity";
        var scopeForRequest = scopeNormalized == "documentids" ? SearchScope.DocumentIds : SearchScope.Entity;

        // Determine values based on scope
        string? entityType = null;
        string? entityId = null;
        IReadOnlyList<string>? documentIds = null;

        if (scopeNormalized == "entity")
        {
            entityType = config.EntityType;
            entityId = config.EntityId;
        }
        else if (scopeNormalized == "documentids" && config.DocumentIds != null)
        {
            // Filter to valid GUID strings only
            documentIds = config.DocumentIds
                .Where(id => Guid.TryParse(id, out _))
                .ToList();
        }

        // Build filters if provided
        SearchFilters? filters = null;
        if (config.Filters != null)
        {
            filters = new SearchFilters
            {
                DocumentTypes = config.Filters.DocumentTypes,
                FileTypes = config.Filters.FileTypes,
                Tags = config.Filters.Tags,
                DateRange = config.Filters.DateRange != null
                    ? new DateRangeFilter
                    {
                        From = config.Filters.DateRange.From,
                        To = config.Filters.DateRange.To
                    }
                    : null
            };
        }

        // Create request using object initializer (all init-only properties)
        return new SemanticSearchRequest
        {
            Query = config.Query,
            Scope = scopeForRequest,
            EntityType = entityType,
            EntityId = entityId,
            DocumentIds = documentIds,
            Filters = filters,
            Options = new SearchOptions
            {
                Limit = limit,
                Offset = 0,
                HybridMode = HybridSearchMode.Rrf,
                IncludeHighlights = true
            }
        };
    }

    /// <summary>
    /// Builds a human-readable summary of the search results.
    /// </summary>
    private static string BuildSummary(SemanticSearchToolResult result)
    {
        if (result.ReturnedResults == 0)
        {
            return $"No documents found matching '{result.Query}'.";
        }

        var summary = $"Found {result.TotalResults} document(s) matching '{result.Query}'.";

        if (result.ReturnedResults > 0 && result.Results.Count > 0)
        {
            summary += " Top results:";
            foreach (var item in result.Results.Take(3))
            {
                summary += $"\n- {item.Name} ({item.FileType ?? "unknown"}, score: {item.Score:F2})";
            }

            if (result.ReturnedResults > 3)
            {
                summary += $"\n... and {result.ReturnedResults - 3} more";
            }
        }

        return summary;
    }

    /// <summary>
    /// Calculates a confidence score based on search result quality.
    /// </summary>
    private static double CalculateConfidence(SemanticSearchResponse response)
    {
        if (response.Results.Count == 0)
            return 0.5; // Neutral confidence for no results

        // Base confidence on top result score and result count
        var topScore = response.Results.First().CombinedScore;
        var resultFactor = Math.Min(response.Results.Count / 5.0, 1.0); // More results = more confidence

        // Normalize: RRF scores are typically 0-1, semantic reranker scores are 0-4
        var normalizedScore = topScore > 1 ? topScore / 4.0 : topScore;

        return Math.Clamp(normalizedScore * 0.7 + resultFactor * 0.3, 0.0, 1.0);
    }
}

/// <summary>
/// Configuration for semantic search tool.
/// </summary>
internal class SemanticSearchConfig
{
    public string? Query { get; set; }
    public string? Scope { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string[]? DocumentIds { get; set; }
    public int? Limit { get; set; }
    public SearchFiltersConfig? Filters { get; set; }
}

/// <summary>
/// Filters configuration for semantic search.
/// </summary>
internal class SearchFiltersConfig
{
    public IReadOnlyList<string>? DocumentTypes { get; set; }
    public IReadOnlyList<string>? FileTypes { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public DateRangeConfig? DateRange { get; set; }
}

/// <summary>
/// Date range filter configuration.
/// </summary>
internal class DateRangeConfig
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

/// <summary>
/// Result structure for semantic search tool output.
/// </summary>
public class SemanticSearchToolResult
{
    /// <summary>The search query that was executed.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of matching documents.</summary>
    public long TotalResults { get; set; }

    /// <summary>Number of results returned in this response.</summary>
    public int ReturnedResults { get; set; }

    /// <summary>Search execution time in milliseconds.</summary>
    public long SearchDurationMs { get; set; }

    /// <summary>The search mode that was executed (rrf, vectorOnly, keywordOnly).</summary>
    public string? ExecutedMode { get; set; }

    /// <summary>The search results.</summary>
    public List<SearchResultItem> Results { get; set; } = new();
}

/// <summary>
/// Individual search result item for tool output.
/// </summary>
public class SearchResultItem
{
    /// <summary>Document identifier.</summary>
    public string? DocumentId { get; set; }

    /// <summary>Document name.</summary>
    public string? Name { get; set; }

    /// <summary>Document type classification.</summary>
    public string? DocumentType { get; set; }

    /// <summary>File type/extension.</summary>
    public string? FileType { get; set; }

    /// <summary>Combined relevance score.</summary>
    public double Score { get; set; }

    /// <summary>Highlighted text snippets.</summary>
    public IReadOnlyList<string>? Highlights { get; set; }

    /// <summary>Parent entity type.</summary>
    public string? ParentEntityType { get; set; }

    /// <summary>Parent entity name.</summary>
    public string? ParentEntityName { get; set; }
}
