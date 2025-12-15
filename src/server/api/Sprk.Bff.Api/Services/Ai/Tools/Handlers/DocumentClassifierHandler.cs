using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools.Handlers;

/// <summary>
/// Classifies documents into categories and extracts metadata.
/// This is a Custom tool type - referenced by HandlerClassName in Dataverse.
/// </summary>
public class DocumentClassifierHandler : IAnalysisToolHandler
{
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<DocumentClassifierHandler> _logger;

    // Handler class name for Custom tool routing
    public const string ClassName = "Sprk.Bff.Api.Services.Ai.Tools.Handlers.DocumentClassifierHandler";

    // Default prompt template - can be overridden via tool Configuration
    private const string DefaultPromptTemplate = """
        Classify the following document and extract metadata. Return a JSON object with this structure:
        {
            "primaryCategory": "main document category",
            "secondaryCategories": ["additional relevant categories"],
            "documentType": "specific document type (e.g., Invoice, Contract, Email, Report, Letter, Memo, etc.)",
            "confidenceScore": 0.95,
            "language": "detected language (e.g., English, Spanish, French)",
            "sentiment": "Overall|Positive|Negative|Neutral|Mixed",
            "topics": ["main topics covered in the document"],
            "summary": "brief 1-2 sentence summary",
            "suggestedTags": ["tags useful for organization and search"],
            "metadata": {
                "author": "author if identifiable",
                "date": "document date if present",
                "recipient": "recipient if applicable",
                "subject": "subject/title if present"
            }
        }

        Document categories to consider:
        - Legal (contracts, agreements, court documents)
        - Financial (invoices, statements, tax documents)
        - Correspondence (emails, letters, memos)
        - Technical (specifications, manuals, reports)
        - HR (employment documents, policies)
        - Marketing (proposals, presentations)
        - Administrative (forms, applications)

        Important:
        - Primary category should be the single best match
        - Confidence score should be between 0.0 and 1.0
        - Include only categories that apply
        - Suggested tags should be specific and useful for search

        Document text:
        {documentText}
        """;

    public DocumentClassifierHandler(
        IOpenAiClient openAiClient,
        ILogger<DocumentClassifierHandler> logger)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ToolType ToolType => ToolType.Custom;

    public string DisplayName => "Document Classifier";

    public string? HandlerClassName => ClassName;

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "DocumentClassifier starting for document {DocumentId}, content length {ContentLength}",
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

            // Add custom categories from configuration if provided
            var customCategories = GetCustomCategories(context.ParsedConfiguration);
            if (customCategories != null)
            {
                prompt = prompt.Replace(
                    "Document categories to consider:",
                    $"Document categories to consider:\n{string.Join("\n", customCategories.Select(c => $"- {c}"))}");
            }

            // Call AI
            var aiResponse = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

            stopwatch.Stop();

            // Parse response
            var classification = ParseClassification(aiResponse);

            // Generate markdown summary
            var markdown = GenerateMarkdown(classification);

            // Create summary
            var summary = !string.IsNullOrEmpty(classification.PrimaryCategory)
                ? $"Classified as: {classification.PrimaryCategory} ({classification.DocumentType})"
                : "Classification uncertain";

            _logger.LogInformation(
                "DocumentClassifier completed for document {DocumentId}: {Category} ({Type}) confidence {Confidence:P0} in {DurationMs}ms",
                context.DocumentId,
                classification.PrimaryCategory,
                classification.DocumentType,
                classification.ConfidenceScore,
                stopwatch.ElapsedMilliseconds);

            return AnalysisToolResult.Ok(
                data: classification,
                summary: summary,
                markdown: markdown,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "DocumentClassifier failed for document {DocumentId}: {Error}",
                context.DocumentId, ex.Message);

            return AnalysisToolResult.Fail(
                $"Document classification failed: {ex.Message}",
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

            // Validate categories array if provided
            if (config.TryGetValue("categories", out var categories) &&
                categories is JsonElement catElement)
            {
                if (catElement.ValueKind != JsonValueKind.Array)
                {
                    return ToolValidationResult.Invalid("categories must be an array");
                }

                if (catElement.GetArrayLength() == 0)
                {
                    return ToolValidationResult.Invalid("categories array cannot be empty");
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

    private static string[]? GetCustomCategories(Dictionary<string, object>? config)
    {
        if (config?.TryGetValue("categories", out var categories) == true &&
            categories is JsonElement element &&
            element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        return null;
    }

    private DocumentClassificationResult ParseClassification(string aiResponse)
    {
        // Clean response (remove markdown code blocks if present)
        var cleanedResponse = CleanJsonResponse(aiResponse);

        try
        {
            var result = JsonSerializer.Deserialize<DocumentClassificationResult>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new DocumentClassificationResult();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse classification response as JSON, returning empty result. Response preview: {Preview}",
                aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse);

            return new DocumentClassificationResult { ParseError = ex.Message };
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

    private static string GenerateMarkdown(DocumentClassificationResult classification)
    {
        var sections = new List<string>();

        // Primary classification
        if (!string.IsNullOrEmpty(classification.PrimaryCategory))
        {
            var confidence = classification.ConfidenceScore > 0
                ? $" ({classification.ConfidenceScore:P0} confidence)"
                : "";
            sections.Add($"**Category:** {classification.PrimaryCategory}{confidence}");
        }

        if (!string.IsNullOrEmpty(classification.DocumentType))
        {
            sections.Add($"**Document Type:** {classification.DocumentType}");
        }

        if (classification.SecondaryCategories?.Length > 0)
        {
            sections.Add($"**Secondary Categories:** {string.Join(", ", classification.SecondaryCategories)}");
        }

        if (!string.IsNullOrEmpty(classification.Language))
        {
            sections.Add($"**Language:** {classification.Language}");
        }

        if (!string.IsNullOrEmpty(classification.Sentiment))
        {
            sections.Add($"**Sentiment:** {classification.Sentiment}");
        }

        if (!string.IsNullOrEmpty(classification.Summary))
        {
            sections.Add($"\n{classification.Summary}");
        }

        if (classification.Topics?.Length > 0)
        {
            sections.Add($"\n**Topics:** {string.Join(", ", classification.Topics)}");
        }

        if (classification.SuggestedTags?.Length > 0)
        {
            sections.Add($"**Suggested Tags:** {string.Join(", ", classification.SuggestedTags)}");
        }

        // Metadata
        if (classification.Metadata != null)
        {
            var metadataItems = new List<string>();

            if (!string.IsNullOrEmpty(classification.Metadata.Author))
                metadataItems.Add($"Author: {classification.Metadata.Author}");
            if (!string.IsNullOrEmpty(classification.Metadata.Date))
                metadataItems.Add($"Date: {classification.Metadata.Date}");
            if (!string.IsNullOrEmpty(classification.Metadata.Recipient))
                metadataItems.Add($"Recipient: {classification.Metadata.Recipient}");
            if (!string.IsNullOrEmpty(classification.Metadata.Subject))
                metadataItems.Add($"Subject: {classification.Metadata.Subject}");

            if (metadataItems.Count > 0)
            {
                sections.Add($"\n**Metadata:**\n- {string.Join("\n- ", metadataItems)}");
            }
        }

        return sections.Count > 0
            ? string.Join("\n", sections)
            : "*Classification could not be determined*";
    }
}

/// <summary>
/// Result structure for document classification.
/// </summary>
public class DocumentClassificationResult
{
    public string? PrimaryCategory { get; set; }
    public string[]? SecondaryCategories { get; set; }
    public string? DocumentType { get; set; }
    public double ConfidenceScore { get; set; }
    public string? Language { get; set; }
    public string? Sentiment { get; set; }
    public string[]? Topics { get; set; }
    public string? Summary { get; set; }
    public string[]? SuggestedTags { get; set; }
    public DocumentMetadata? Metadata { get; set; }

    /// <summary>
    /// If set, indicates JSON parsing failed and this contains the error.
    /// </summary>
    public string? ParseError { get; set; }
}

/// <summary>
/// Document metadata extracted during classification.
/// </summary>
public class DocumentMetadata
{
    public string? Author { get; set; }
    public string? Date { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
}
