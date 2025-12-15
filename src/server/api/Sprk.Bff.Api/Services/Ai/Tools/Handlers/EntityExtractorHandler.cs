using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools.Handlers;

/// <summary>
/// Extracts entities (organizations, people, dates, amounts, references) from document text.
/// Uses Azure OpenAI with a structured extraction prompt.
/// </summary>
public class EntityExtractorHandler : IAnalysisToolHandler
{
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<EntityExtractorHandler> _logger;

    // Default prompt template - can be overridden via tool Configuration
    private const string DefaultPromptTemplate = """
        Extract entities from the following document text. Return a JSON object with this exact structure:
        {
            "organizations": ["list of company/organization names"],
            "people": ["list of person names"],
            "dates": ["list of significant dates in ISO format or original text"],
            "amounts": ["list of monetary amounts with currency"],
            "references": ["list of reference numbers, case IDs, invoice numbers, etc."],
            "locations": ["list of addresses, cities, countries"],
            "emails": ["list of email addresses"],
            "phoneNumbers": ["list of phone numbers"]
        }

        Important:
        - Include only entities explicitly mentioned in the text
        - Use empty arrays [] for categories with no matches
        - For dates, preserve the original format if ambiguous
        - For amounts, include the currency symbol/code
        - Do not infer or guess entities not present in the text

        Document text:
        {documentText}
        """;

    public EntityExtractorHandler(
        IOpenAiClient openAiClient,
        ILogger<EntityExtractorHandler> logger)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ToolType ToolType => ToolType.EntityExtractor;

    public string DisplayName => "Entity Extractor";

    public string? HandlerClassName => null; // Built-in tool, no custom class name needed

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "EntityExtractor starting for document {DocumentId}, content length {ContentLength}",
            context.DocumentId, context.DocumentContent.Length);

        try
        {
            // Build prompt using template (from config or default)
            var promptTemplate = GetPromptTemplate(context.ParsedConfiguration);
            var prompt = promptTemplate.Replace("{documentText}", context.DocumentContent);

            // Add any additional context from parent analysis
            if (!string.IsNullOrEmpty(context.AdditionalContext))
            {
                prompt = $"{context.AdditionalContext}\n\n{prompt}";
            }

            // Call AI
            var aiResponse = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

            stopwatch.Stop();

            // Parse response
            var entities = ParseEntities(aiResponse);

            // Generate markdown summary
            var markdown = GenerateMarkdown(entities);

            // Count total entities for summary
            var totalCount = CountEntities(entities);

            _logger.LogInformation(
                "EntityExtractor completed for document {DocumentId}: {EntityCount} entities in {DurationMs}ms",
                context.DocumentId, totalCount, stopwatch.ElapsedMilliseconds);

            return AnalysisToolResult.Ok(
                data: entities,
                summary: $"Extracted {totalCount} entities",
                markdown: markdown,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "EntityExtractor failed for document {DocumentId}: {Error}",
                context.DocumentId, ex.Message);

            return AnalysisToolResult.Fail(
                $"Entity extraction failed: {ex.Message}",
                ex.ToString(),
                stopwatch.ElapsedMilliseconds);
        }
    }

    public ToolValidationResult ValidateConfiguration(string? configuration)
    {
        // Configuration is optional - uses defaults if not provided
        if (string.IsNullOrEmpty(configuration))
        {
            return ToolValidationResult.Valid();
        }

        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, object>>(configuration);
            if (config == null)
            {
                return ToolValidationResult.Invalid("Configuration must be a valid JSON object");
            }

            // Validate known configuration keys
            if (config.TryGetValue("promptTemplate", out var template) &&
                template is JsonElement element &&
                element.ValueKind == JsonValueKind.String)
            {
                var templateStr = element.GetString();
                if (string.IsNullOrWhiteSpace(templateStr))
                {
                    return ToolValidationResult.Invalid("promptTemplate cannot be empty");
                }

                if (!templateStr.Contains("{documentText}"))
                {
                    return ToolValidationResult.Invalid("promptTemplate must contain {documentText} placeholder");
                }
            }

            return ToolValidationResult.Valid();
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Invalid($"Invalid JSON configuration: {ex.Message}");
        }
    }

    private string GetPromptTemplate(Dictionary<string, object>? config)
    {
        if (config?.TryGetValue("promptTemplate", out var template) == true &&
            template is JsonElement element &&
            element.ValueKind == JsonValueKind.String)
        {
            var customTemplate = element.GetString();
            if (!string.IsNullOrWhiteSpace(customTemplate))
            {
                return customTemplate;
            }
        }

        return DefaultPromptTemplate;
    }

    private ExtractedEntitiesResult ParseEntities(string aiResponse)
    {
        // Clean response (remove markdown code blocks if present)
        var cleanedResponse = CleanJsonResponse(aiResponse);

        try
        {
            var result = JsonSerializer.Deserialize<ExtractedEntitiesResult>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new ExtractedEntitiesResult();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse entity extraction response as JSON, returning empty result. Response preview: {Preview}",
                aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse);

            return new ExtractedEntitiesResult { ParseError = ex.Message };
        }
    }

    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();

        // Handle markdown code blocks: ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var startIndex = trimmed.IndexOf('\n');
            if (startIndex > 0)
            {
                var endIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (endIndex > startIndex)
                {
                    trimmed = trimmed[(startIndex + 1)..endIndex].Trim();
                }
            }
        }

        return trimmed;
    }

    private static string GenerateMarkdown(ExtractedEntitiesResult entities)
    {
        var sections = new List<string>();

        if (entities.Organizations?.Length > 0)
        {
            sections.Add($"**Organizations:** {string.Join(", ", entities.Organizations)}");
        }

        if (entities.People?.Length > 0)
        {
            sections.Add($"**People:** {string.Join(", ", entities.People)}");
        }

        if (entities.Dates?.Length > 0)
        {
            sections.Add($"**Dates:** {string.Join(", ", entities.Dates)}");
        }

        if (entities.Amounts?.Length > 0)
        {
            sections.Add($"**Amounts:** {string.Join(", ", entities.Amounts)}");
        }

        if (entities.References?.Length > 0)
        {
            sections.Add($"**References:** {string.Join(", ", entities.References)}");
        }

        if (entities.Locations?.Length > 0)
        {
            sections.Add($"**Locations:** {string.Join(", ", entities.Locations)}");
        }

        if (entities.Emails?.Length > 0)
        {
            sections.Add($"**Emails:** {string.Join(", ", entities.Emails)}");
        }

        if (entities.PhoneNumbers?.Length > 0)
        {
            sections.Add($"**Phone Numbers:** {string.Join(", ", entities.PhoneNumbers)}");
        }

        return sections.Count > 0
            ? string.Join("\n\n", sections)
            : "*No entities found*";
    }

    private static int CountEntities(ExtractedEntitiesResult entities)
    {
        return (entities.Organizations?.Length ?? 0) +
               (entities.People?.Length ?? 0) +
               (entities.Dates?.Length ?? 0) +
               (entities.Amounts?.Length ?? 0) +
               (entities.References?.Length ?? 0) +
               (entities.Locations?.Length ?? 0) +
               (entities.Emails?.Length ?? 0) +
               (entities.PhoneNumbers?.Length ?? 0);
    }
}

/// <summary>
/// Result structure for entity extraction.
/// </summary>
public class ExtractedEntitiesResult
{
    public string[]? Organizations { get; set; }
    public string[]? People { get; set; }
    public string[]? Dates { get; set; }
    public string[]? Amounts { get; set; }
    public string[]? References { get; set; }
    public string[]? Locations { get; set; }
    public string[]? Emails { get; set; }
    public string[]? PhoneNumbers { get; set; }

    /// <summary>
    /// If set, indicates JSON parsing failed and this contains the error.
    /// </summary>
    public string? ParseError { get; set; }
}
