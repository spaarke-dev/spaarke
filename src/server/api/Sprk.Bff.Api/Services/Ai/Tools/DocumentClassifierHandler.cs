using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for classifying documents into predefined or custom categories.
/// Supports contract types, business document types, and custom taxonomies.
/// </summary>
/// <remarks>
/// <para>
/// Document classification uses Azure OpenAI with structured JSON output.
/// Optionally uses RAG to retrieve example documents for few-shot classification.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - categories: Array of category names to classify into (default: standard types)
/// - useRagExamples: Whether to use RAG for example-based classification (default: false)
/// - ragExampleCount: Number of RAG examples to retrieve (default: 3)
/// - minConfidence: Minimum confidence threshold (default: 0.5)
/// - includeSecondaryClassifications: Include alternative classifications (default: true)
/// </para>
/// </remarks>
public sealed class DocumentClassifierHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "DocumentClassifierHandler";
    private const int DefaultRagExampleCount = 3;
    private const double DefaultMinConfidence = 0.5;
    private const int MaxTextLengthForClassification = 12000;

    private readonly IOpenAiClient _openAiClient;
    private readonly IRagService? _ragService;
    private readonly ILogger<DocumentClassifierHandler> _logger;

    public DocumentClassifierHandler(
        IOpenAiClient openAiClient,
        ILogger<DocumentClassifierHandler> logger,
        IRagService? ragService = null)
    {
        _openAiClient = openAiClient;
        _logger = logger;
        _ragService = ragService;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Document Classifier",
        Description: "Classifies documents into predefined categories (contracts, business documents) or custom taxonomies using AI analysis.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("categories", "Document categories to classify into", ToolParameterType.Array, Required: false,
                DefaultValue: DocumentCategories.AllStandardCategories),
            new ToolParameterDefinition("useRagExamples", "Use RAG to retrieve example documents for classification", ToolParameterType.Boolean, Required: false, DefaultValue: false),
            new ToolParameterDefinition("ragExampleCount", "Number of RAG examples to retrieve (1-5)", ToolParameterType.Integer, Required: false, DefaultValue: 3),
            new ToolParameterDefinition("minConfidence", "Minimum confidence threshold (0.0-1.0)", ToolParameterType.Decimal, Required: false, DefaultValue: 0.5),
            new ToolParameterDefinition("includeSecondaryClassifications", "Include alternative classifications", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.DocumentClassifier };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for classification.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<DocumentClassifierConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config?.MinConfidence is < 0 or > 1)
                    errors.Add("minConfidence must be between 0.0 and 1.0.");

                if (config?.RagExampleCount is < 1 or > 5)
                    errors.Add("ragExampleCount must be between 1 and 5.");

                if (config?.UseRagExamples == true && _ragService is null)
                    errors.Add("RAG examples requested but IRagService is not available.");

                if (config?.Categories is { Length: 0 })
                    errors.Add("At least one category must be specified.");
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
                "Starting document classification for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);

            // Prepare document text (truncate if too long for classification)
            var documentText = TruncateForClassification(context.Document.ExtractedText);

            // Get RAG examples if enabled
            var ragExamples = new List<RagExample>();
            var ragTokens = 0;
            if (config.UseRagExamples && _ragService != null)
            {
                var ragResult = await GetRagExamplesAsync(
                    documentText,
                    context.TenantId,
                    config.RagExampleCount,
                    cancellationToken);
                ragExamples = ragResult.Examples;
                ragTokens = ragResult.Tokens;
            }

            // Classify document
            var classificationResult = await ClassifyDocumentAsync(
                documentText,
                config.Categories ?? DocumentCategories.AllStandardCategories,
                ragExamples,
                config.IncludeSecondaryClassifications,
                cancellationToken);

            stopwatch.Stop();

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                InputTokens = classificationResult.InputTokens + ragTokens,
                OutputTokens = classificationResult.OutputTokens,
                ModelCalls = config.UseRagExamples ? 2 : 1, // RAG query + classification
                ModelName = "gpt-4o-mini"
            };

            // Filter by confidence
            var primaryClassification = classificationResult.Classification;
            var secondaryClassifications = classificationResult.SecondaryClassifications
                .Where(c => c.Confidence >= config.MinConfidence)
                .ToList();

            // Build result
            var resultData = new DocumentClassificationResult
            {
                PrimaryCategory = primaryClassification.Category,
                PrimaryConfidence = primaryClassification.Confidence,
                CategoryDescription = primaryClassification.Description,
                SecondaryClassifications = secondaryClassifications,
                RagExamplesUsed = ragExamples.Count,
                DocumentSummary = classificationResult.DocumentSummary
            };

            var summary = BuildSummary(primaryClassification, secondaryClassifications, ragExamples.Count);

            _logger.LogInformation(
                "Document classification complete for {AnalysisId}: {Category} ({Confidence:P0}) in {Duration}ms",
                context.AnalysisId, primaryClassification.Category, primaryClassification.Confidence, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary,
                primaryClassification.Confidence,
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Document classification cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Document classification was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document classification failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Document classification failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Get RAG examples for few-shot classification.
    /// </summary>
    private async Task<(List<RagExample> Examples, int Tokens)> GetRagExamplesAsync(
        string documentText,
        string tenantId,
        int exampleCount,
        CancellationToken cancellationToken)
    {
        if (_ragService is null)
            return (new List<RagExample>(), 0);

        try
        {
            // Use first 500 chars as query for similar documents
            var queryText = documentText.Length > 500
                ? documentText.Substring(0, 500)
                : documentText;

            var searchResult = await _ragService.SearchAsync(
                queryText,
                new RagSearchOptions
                {
                    TenantId = tenantId,
                    TopK = exampleCount,
                    MinScore = 0.5f
                },
                cancellationToken);

            var examples = searchResult.Results
                .Select(r => new RagExample
                {
                    Content = r.Content,
                    Category = ExtractCategoryFromMetadata(r.Metadata),
                    RelevanceScore = r.Score
                })
                .ToList();

            return (examples, EstimateTokens(queryText));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve RAG examples, proceeding without them");
            return (new List<RagExample>(), 0);
        }
    }

    /// <summary>
    /// Classify document using Azure OpenAI.
    /// </summary>
    private async Task<ClassificationResponse> ClassifyDocumentAsync(
        string documentText,
        string[] categories,
        List<RagExample> ragExamples,
        bool includeSecondary,
        CancellationToken cancellationToken)
    {
        var prompt = BuildClassificationPrompt(documentText, categories, ragExamples, includeSecondary);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var classification = ParseClassificationResponse(response, categories);

        return new ClassificationResponse
        {
            Classification = classification.Primary,
            SecondaryClassifications = classification.Secondary,
            DocumentSummary = classification.Summary,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the classification prompt for Azure OpenAI.
    /// </summary>
    private static string BuildClassificationPrompt(
        string documentText,
        string[] categories,
        List<RagExample> ragExamples,
        bool includeSecondary)
    {
        var categoriesList = string.Join(", ", categories);

        var examplesSection = "";
        if (ragExamples.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\nHere are some example documents and their categories for reference:");
            foreach (var (example, index) in ragExamples.Select((e, i) => (e, i)))
            {
                sb.AppendLine($"\nExample {index + 1} (Category: {example.Category}):");
                sb.AppendLine(example.Content.Length > 300
                    ? example.Content.Substring(0, 300) + "..."
                    : example.Content);
            }
            examplesSection = sb.ToString();
        }

        var secondaryInstruction = includeSecondary
            ? """
              - secondaryClassifications: Array of other possible categories with confidence scores (only include if confidence > 0.3)
              """
            : "";

        return $$"""
            You are a document classification expert. Analyze the following document and classify it into one of these categories:
            {{categoriesList}}
            {{examplesSection}}

            Provide your classification with:
            - category: The most appropriate category from the list above
            - confidence: Your confidence score (0.0-1.0) in this classification
            - description: Brief explanation of why this category was chosen (max 100 chars)
            - documentSummary: Brief summary of the document content (max 150 chars)
            {{secondaryInstruction}}

            Return ONLY valid JSON in this format:
            {
              "category": "NDA",
              "confidence": 0.95,
              "description": "Contains mutual confidentiality obligations and disclosure restrictions",
              "documentSummary": "Non-disclosure agreement between two parties for protecting proprietary information",
              "secondaryClassifications": [
                {"category": "Contract", "confidence": 0.85}
              ]
            }

            If the document doesn't clearly match any category, use the closest match with appropriate confidence.

            Document to classify:
            {{documentText}}
            """;
    }

    /// <summary>
    /// Parse classification from the AI response JSON.
    /// </summary>
    private (DocumentClassification Primary, List<DocumentClassification> Secondary, string? Summary) ParseClassificationResponse(
        string response,
        string[] validCategories)
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

            var result = JsonSerializer.Deserialize<ClassificationJsonResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result is null)
                return (CreateUnknownClassification(), new List<DocumentClassification>(), null);

            // Validate category is in the allowed list
            var category = result.Category;
            if (!validCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                // Try to find closest match
                category = validCategories.FirstOrDefault(c =>
                    c.Contains(result.Category, StringComparison.OrdinalIgnoreCase) ||
                    result.Category.Contains(c, StringComparison.OrdinalIgnoreCase))
                    ?? result.Category;
            }

            var primary = new DocumentClassification
            {
                Category = category,
                Confidence = Math.Clamp(result.Confidence, 0, 1),
                Description = result.Description
            };

            var secondary = result.SecondaryClassifications?
                .Select(s => new DocumentClassification
                {
                    Category = s.Category,
                    Confidence = Math.Clamp(s.Confidence, 0, 1)
                })
                .ToList() ?? new List<DocumentClassification>();

            return (primary, secondary, result.DocumentSummary);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse classification response: {Response}", response);
            return (CreateUnknownClassification(), new List<DocumentClassification>(), null);
        }
    }

    /// <summary>
    /// Create an unknown classification result.
    /// </summary>
    private static DocumentClassification CreateUnknownClassification()
    {
        return new DocumentClassification
        {
            Category = "Unknown",
            Confidence = 0,
            Description = "Unable to classify document"
        };
    }

    /// <summary>
    /// Extract category from JSON metadata string.
    /// </summary>
    private static string ExtractCategoryFromMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return "Unknown";

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("category", out var categoryElement) ||
                doc.RootElement.TryGetProperty("Category", out categoryElement))
            {
                return categoryElement.GetString() ?? "Unknown";
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return "Unknown";
    }

    /// <summary>
    /// Truncate document text to maximum length for classification.
    /// </summary>
    private static string TruncateForClassification(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxTextLengthForClassification)
            return text;

        // Take beginning and end of document (most relevant for classification)
        var halfLength = MaxTextLengthForClassification / 2;
        var beginning = text.Substring(0, halfLength);
        var ending = text.Substring(text.Length - halfLength);

        return $"{beginning}\n\n[... document truncated ...]\n\n{ending}";
    }

    /// <summary>
    /// Build a human-readable summary of the classification.
    /// </summary>
    private static string BuildSummary(
        DocumentClassification primary,
        List<DocumentClassification> secondary,
        int ragExamplesUsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Classification: {primary.Category} ({primary.Confidence:P0} confidence)");

        if (!string.IsNullOrWhiteSpace(primary.Description))
        {
            sb.AppendLine($"Reason: {primary.Description}");
        }

        if (secondary.Count > 0)
        {
            sb.AppendLine($"Alternatives: {string.Join(", ", secondary.Take(3).Select(s => $"{s.Category} ({s.Confidence:P0})"))}");
        }

        if (ragExamplesUsed > 0)
        {
            sb.AppendLine($"Based on {ragExamplesUsed} similar document(s)");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static DocumentClassifierConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return DocumentClassifierConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<DocumentClassifierConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? DocumentClassifierConfig.Default;
        }
        catch
        {
            return DocumentClassifierConfig.Default;
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
/// Standard document category definitions.
/// </summary>
public static class DocumentCategories
{
    // Contract Types
    public const string NDA = "NDA";
    public const string MSA = "MSA";
    public const string SOW = "SOW";
    public const string Amendment = "Amendment";
    public const string SLA = "SLA";
    public const string License = "License";
    public const string EmploymentContract = "EmploymentContract";
    public const string LeaseAgreement = "LeaseAgreement";
    public const string PurchaseAgreement = "PurchaseAgreement";

    // Business Document Types
    public const string Invoice = "Invoice";
    public const string Proposal = "Proposal";
    public const string Report = "Report";
    public const string Memo = "Memo";
    public const string Policy = "Policy";
    public const string Specification = "Specification";

    /// <summary>
    /// Contract document types.
    /// </summary>
    public static string[] ContractTypes => new[]
    {
        NDA, MSA, SOW, Amendment, SLA, License, EmploymentContract, LeaseAgreement, PurchaseAgreement
    };

    /// <summary>
    /// Business document types.
    /// </summary>
    public static string[] BusinessTypes => new[]
    {
        Invoice, Proposal, Report, Memo, Policy, Specification
    };

    /// <summary>
    /// All standard document categories.
    /// </summary>
    public static string[] AllStandardCategories => ContractTypes.Concat(BusinessTypes).ToArray();
}

/// <summary>
/// Configuration for DocumentClassifier tool.
/// </summary>
internal class DocumentClassifierConfig
{
    public string[]? Categories { get; set; }
    public bool UseRagExamples { get; set; }
    public int RagExampleCount { get; set; } = 3;
    public double MinConfidence { get; set; } = 0.5;
    public bool IncludeSecondaryClassifications { get; set; } = true;

    public static DocumentClassifierConfig Default => new()
    {
        Categories = DocumentCategories.AllStandardCategories,
        UseRagExamples = false,
        RagExampleCount = 3,
        MinConfidence = 0.5,
        IncludeSecondaryClassifications = true
    };
}

/// <summary>
/// RAG example for few-shot classification.
/// </summary>
internal class RagExample
{
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
}

/// <summary>
/// Response from classification AI call.
/// </summary>
internal class ClassificationResponse
{
    public DocumentClassification Classification { get; set; } = new();
    public List<DocumentClassification> SecondaryClassifications { get; set; } = new();
    public string? DocumentSummary { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// JSON response structure from AI classification.
/// </summary>
internal class ClassificationJsonResponse
{
    public string Category { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? Description { get; set; }
    public string? DocumentSummary { get; set; }
    public List<SecondaryClassificationJson>? SecondaryClassifications { get; set; }
}

/// <summary>
/// Secondary classification in JSON response.
/// </summary>
internal class SecondaryClassificationJson
{
    public string Category { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

/// <summary>
/// A single document classification result.
/// </summary>
public class DocumentClassification
{
    /// <summary>The classified category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0-1.0) of the classification.</summary>
    public double Confidence { get; set; }

    /// <summary>Description/reason for the classification.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Complete result of document classification.
/// </summary>
public class DocumentClassificationResult
{
    /// <summary>The primary/best classification category.</summary>
    public string PrimaryCategory { get; set; } = string.Empty;

    /// <summary>Confidence score for the primary classification.</summary>
    public double PrimaryConfidence { get; set; }

    /// <summary>Description of why this category was chosen.</summary>
    public string? CategoryDescription { get; set; }

    /// <summary>Alternative classifications with lower confidence.</summary>
    public List<DocumentClassification> SecondaryClassifications { get; set; } = new();

    /// <summary>Number of RAG examples used for classification.</summary>
    public int RagExamplesUsed { get; set; }

    /// <summary>Brief summary of the document content.</summary>
    public string? DocumentSummary { get; set; }
}
