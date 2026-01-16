using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for extracting named entities from documents.
/// Extracts people, organizations, dates, monetary values, and custom entity types.
/// </summary>
/// <remarks>
/// <para>
/// Entity extraction uses Azure OpenAI with structured JSON output.
/// For large documents, content is chunked and entities are aggregated.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - entityTypes: Array of entity types to extract (default: all standard types)
/// - minConfidence: Minimum confidence threshold (default: 0.5)
/// - chunkSize: Characters per chunk for large docs (default: 8000)
/// </para>
/// </remarks>
public sealed class EntityExtractorHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "EntityExtractorHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const double DefaultMinConfidence = 0.5;

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextChunkingService _textChunkingService;
    private readonly ILogger<EntityExtractorHandler> _logger;

    public EntityExtractorHandler(
        IOpenAiClient openAiClient,
        ITextChunkingService textChunkingService,
        ILogger<EntityExtractorHandler> logger)
    {
        _openAiClient = openAiClient;
        _textChunkingService = textChunkingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Entity Extractor",
        Description: "Extracts named entities (people, organizations, dates, monetary values, legal references) from documents using AI.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("entityTypes", "Entity types to extract", ToolParameterType.Array, Required: false,
                DefaultValue: new[] { "Person", "Organization", "Date", "MonetaryValue", "LegalReference" }),
            new ToolParameterDefinition("minConfidence", "Minimum confidence threshold (0.0-1.0)", ToolParameterType.Decimal, Required: false, DefaultValue: 0.5),
            new ToolParameterDefinition("chunkSize", "Characters per chunk for large documents", ToolParameterType.Integer, Required: false, DefaultValue: 8000)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.EntityExtractor };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for entity extraction.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<EntityExtractorConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MinConfidence is < 0 or > 1)
                    errors.Add("minConfidence must be between 0.0 and 1.0.");
                if (config?.ChunkSize is < 500)
                    errors.Add("chunkSize must be at least 500 characters.");
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid configuration JSON: {ex.Message}");
            }
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
                "Starting entity extraction for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

            // Chunk document if needed
            var chunkingOptions = new ChunkingOptions
            {
                ChunkSize = config.ChunkSize,
                Overlap = ChunkOverlap,
                PreserveSentenceBoundaries = true
            };
            var textChunks = await _textChunkingService.ChunkTextAsync(documentText, chunkingOptions, cancellationToken);
            var chunks = textChunks.Select(c => c.Content).ToList();
            _logger.LogDebug(
                "Document split into {ChunkCount} chunks (size: {ChunkSize})",
                chunks.Count, config.ChunkSize);

            // Extract entities from each chunk
            var allEntities = new List<ExtractedEntity>();
            var totalInputTokens = 0;
            var totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Processing chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                var chunkResult = await ExtractEntitiesFromChunkAsync(
                    chunk, config.EntityTypes ?? EntityExtractorConfig.Default.EntityTypes!, cancellationToken);

                if (chunkResult.Entities != null)
                {
                    allEntities.AddRange(chunkResult.Entities);
                }

                totalInputTokens += chunkResult.InputTokens;
                totalOutputTokens += chunkResult.OutputTokens;
            }

            // Deduplicate and aggregate entities
            var aggregatedEntities = AggregateEntities(allEntities, config.MinConfidence);

            stopwatch.Stop();

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                ModelCalls = chunks.Count,
                ModelName = "gpt-4o-mini"
            };

            // Build result
            var resultData = new EntityExtractionResult
            {
                Entities = aggregatedEntities,
                TotalCount = aggregatedEntities.Count,
                TypeCounts = aggregatedEntities
                    .GroupBy(e => e.Type)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ChunksProcessed = chunks.Count
            };

            var summary = BuildSummary(aggregatedEntities);

            _logger.LogInformation(
                "Entity extraction complete for {AnalysisId}: {EntityCount} entities found in {Duration}ms",
                context.AnalysisId, aggregatedEntities.Count, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary,
                CalculateOverallConfidence(aggregatedEntities),
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Entity extraction cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Entity extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Entity extraction failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Entity extraction failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Extract entities from a single text chunk using Azure OpenAI.
    /// </summary>
    private async Task<ChunkExtractionResult> ExtractEntitiesFromChunkAsync(
        string chunk,
        string[] entityTypes,
        CancellationToken cancellationToken)
    {
        var prompt = BuildExtractionPrompt(chunk, entityTypes);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var entities = ParseEntitiesFromResponse(response);

        return new ChunkExtractionResult
        {
            Entities = entities,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the extraction prompt for Azure OpenAI.
    /// </summary>
    private static string BuildExtractionPrompt(string text, string[] entityTypes)
    {
        var typesDescription = string.Join(", ", entityTypes);

        return $$"""
            You are an entity extraction assistant. Extract named entities from the following text and return them as JSON.

            Entity types to extract: {{typesDescription}}

            For each entity, provide:
            - value: The exact text of the entity
            - type: One of the specified entity types
            - confidence: Your confidence score (0.0-1.0) in the extraction
            - context: Brief context where the entity appears (max 50 chars)

            Return ONLY valid JSON in this format:
            {
              "entities": [
                {"value": "John Smith", "type": "Person", "confidence": 0.95, "context": "CEO John Smith announced..."},
                {"value": "Acme Corp", "type": "Organization", "confidence": 0.9, "context": "...contract with Acme Corp"},
                {"value": "$1,500,000", "type": "MonetaryValue", "confidence": 0.98, "context": "...total value of $1,500,000"},
                {"value": "January 15, 2025", "type": "Date", "confidence": 0.95, "context": "...effective January 15, 2025"}
              ]
            }

            If no entities are found, return: {"entities": []}

            Text to analyze:
            {{text}}
            """;
    }

    /// <summary>
    /// Parse entities from the AI response JSON.
    /// </summary>
    private List<ExtractedEntity> ParseEntitiesFromResponse(string response)
    {
        try
        {
            // Clean up response - remove markdown code blocks if present
            var json = response.Trim();
            if (json.StartsWith("```"))
            {
                var startIndex = json.IndexOf('{');
                var endIndex = json.LastIndexOf('}');
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    json = json.Substring(startIndex, endIndex - startIndex + 1);
                }
            }

            var result = JsonSerializer.Deserialize<EntityExtractionResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Entities ?? new List<ExtractedEntity>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity extraction response: {Response}", response);
            return new List<ExtractedEntity>();
        }
    }

    /// <summary>
    /// Aggregate entities from multiple chunks, deduplicating and merging.
    /// </summary>
    private static List<ExtractedEntity> AggregateEntities(
        List<ExtractedEntity> allEntities,
        double minConfidence)
    {
        // Group by normalized value and type
        var grouped = allEntities
            .Where(e => e.Confidence >= minConfidence)
            .GroupBy(e => (NormalizeValue(e.Value), e.Type));

        var aggregated = new List<ExtractedEntity>();

        foreach (var group in grouped)
        {
            var entities = group.ToList();
            var best = entities.OrderByDescending(e => e.Confidence).First();

            // Average confidence across occurrences
            var avgConfidence = entities.Average(e => e.Confidence);

            aggregated.Add(new ExtractedEntity
            {
                Value = best.Value,
                Type = best.Type,
                Confidence = Math.Round(avgConfidence, 2),
                Context = best.Context,
                Occurrences = entities.Count
            });
        }

        return aggregated.OrderByDescending(e => e.Confidence).ToList();
    }

    /// <summary>
    /// Normalize entity value for deduplication.
    /// </summary>
    private static string NormalizeValue(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Build a human-readable summary of extracted entities.
    /// </summary>
    private static string BuildSummary(List<ExtractedEntity> entities)
    {
        if (entities.Count == 0)
            return "No entities were found in the document.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {entities.Count} entities:");

        var byType = entities.GroupBy(e => e.Type).OrderByDescending(g => g.Count());
        foreach (var group in byType)
        {
            sb.AppendLine($"- {group.Key}: {group.Count()} ({string.Join(", ", group.Take(3).Select(e => e.Value))}{(group.Count() > 3 ? "..." : "")})");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Calculate overall confidence from entity confidences.
    /// </summary>
    private static double CalculateOverallConfidence(List<ExtractedEntity> entities)
    {
        if (entities.Count == 0)
            return 0;

        return Math.Round(entities.Average(e => e.Confidence), 2);
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static EntityExtractorConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return EntityExtractorConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<EntityExtractorConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? EntityExtractorConfig.Default;
        }
        catch
        {
            return EntityExtractorConfig.Default;
        }
    }

    /// <summary>
    /// Estimate token count for a text string.
    /// </summary>
    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

/// <summary>
/// Configuration for EntityExtractor tool.
/// </summary>
internal class EntityExtractorConfig
{
    public string[]? EntityTypes { get; set; }
    public double MinConfidence { get; set; } = 0.5;
    public int ChunkSize { get; set; } = 8000;

    public static EntityExtractorConfig Default => new()
    {
        EntityTypes = new[] { "Person", "Organization", "Date", "MonetaryValue", "LegalReference" },
        MinConfidence = 0.5,
        ChunkSize = 8000
    };
}

/// <summary>
/// Result of entity extraction from a single chunk.
/// </summary>
internal class ChunkExtractionResult
{
    public List<ExtractedEntity>? Entities { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Response structure from AI entity extraction.
/// </summary>
internal class EntityExtractionResponse
{
    public List<ExtractedEntity>? Entities { get; set; }
}

/// <summary>
/// A single extracted entity.
/// </summary>
public class ExtractedEntity
{
    /// <summary>The extracted entity text value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The entity type (Person, Organization, Date, etc.).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0-1.0) of the extraction.</summary>
    public double Confidence { get; set; }

    /// <summary>Context where the entity was found.</summary>
    public string? Context { get; set; }

    /// <summary>Number of times this entity was found (after aggregation).</summary>
    public int Occurrences { get; set; } = 1;
}

/// <summary>
/// Complete result of entity extraction.
/// </summary>
public class EntityExtractionResult
{
    /// <summary>List of extracted entities.</summary>
    public List<ExtractedEntity> Entities { get; set; } = new();

    /// <summary>Total number of unique entities.</summary>
    public int TotalCount { get; set; }

    /// <summary>Count of entities by type.</summary>
    public Dictionary<string, int> TypeCounts { get; set; } = new();

    /// <summary>Number of document chunks processed.</summary>
    public int ChunksProcessed { get; set; }
}
