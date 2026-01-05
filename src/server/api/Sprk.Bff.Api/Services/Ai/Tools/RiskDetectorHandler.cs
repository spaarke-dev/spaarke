using System.Diagnostics;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for detecting and categorizing risks in documents.
/// Identifies potential risks, categorizes by severity, and provides explanations.
/// </summary>
/// <remarks>
/// <para>
/// Risk detection uses Azure OpenAI to analyze document content for potential risks.
/// Risks are categorized by type and severity (High/Medium/Low) with confidence scores.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - categories: Array of risk categories to detect (default: all)
/// - severityThreshold: Minimum severity to report - "low", "medium", "high" (default: "low")
/// - maxRisks: Maximum number of risks to return (default: 20)
/// - includeRecommendations: Whether to include mitigation recommendations (default: true)
/// </para>
/// </remarks>
public sealed class RiskDetectorHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "RiskDetectorHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const int DefaultMaxRisks = 20;

    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<RiskDetectorHandler> _logger;

    public RiskDetectorHandler(
        IOpenAiClient openAiClient,
        ILogger<RiskDetectorHandler> logger)
    {
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Risk Detector",
        Description: "Identifies and categorizes potential risks in documents with severity ratings and mitigation recommendations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("categories", "Risk categories to detect (e.g., legal, financial, operational)", ToolParameterType.Array, Required: false,
                DefaultValue: new[] { "legal", "financial", "operational", "compliance", "contractual", "liability", "confidentiality", "termination" }),
            new ToolParameterDefinition("severity_threshold", "Minimum severity to report: low, medium, high", ToolParameterType.String, Required: false, DefaultValue: "low"),
            new ToolParameterDefinition("max_risks", "Maximum number of risks to return", ToolParameterType.Integer, Required: false, DefaultValue: 20),
            new ToolParameterDefinition("include_recommendations", "Include mitigation recommendations", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.RiskDetector };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for risk detection.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<RiskDetectorConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MaxRisks is < 1 or > 100)
                    errors.Add("max_risks must be between 1 and 100.");
                if (config?.SeverityThreshold != null && !IsValidSeverity(config.SeverityThreshold))
                    errors.Add("severity_threshold must be 'low', 'medium', or 'high'.");
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
                "Starting risk detection for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

            // Chunk document if needed
            var chunks = ChunkText(documentText, DefaultChunkSize);
            _logger.LogDebug(
                "Document split into {ChunkCount} chunks for risk detection",
                chunks.Count);

            var allRisks = new List<RiskItem>();
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Analyzing chunk {ChunkIndex}/{TotalChunks} for risks", index + 1, chunks.Count);

                var result = await DetectRisksInChunkAsync(chunk, config, cancellationToken);
                allRisks.AddRange(result.Risks);
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }

            // Deduplicate and filter risks
            var filteredRisks = FilterAndDeduplicateRisks(allRisks, config);

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

            var resultData = new RiskDetectionResult
            {
                Risks = filteredRisks,
                TotalRisksFound = filteredRisks.Count,
                HighSeverityCount = filteredRisks.Count(r => r.Severity == RiskSeverity.High),
                MediumSeverityCount = filteredRisks.Count(r => r.Severity == RiskSeverity.Medium),
                LowSeverityCount = filteredRisks.Count(r => r.Severity == RiskSeverity.Low),
                CategoriesAnalyzed = config.Categories ?? RiskDetectorConfig.Default.Categories!
            };

            var summary = GenerateSummary(resultData);

            _logger.LogInformation(
                "Risk detection complete for {AnalysisId}: {RiskCount} risks found in {Duration}ms",
                context.AnalysisId, filteredRisks.Count, stopwatch.ElapsedMilliseconds);

            // Calculate average confidence
            var avgConfidence = filteredRisks.Count > 0
                ? filteredRisks.Average(r => r.Confidence)
                : 0.0;

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary,
                avgConfidence,
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Risk detection cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Risk detection was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Risk detection failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Risk detection failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Detect risks in a single document chunk.
    /// </summary>
    private async Task<RiskDetectionChunkResult> DetectRisksInChunkAsync(
        string chunk,
        RiskDetectorConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildRiskDetectionPrompt(chunk, config);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var risks = ParseRisksFromResponse(response);

        return new RiskDetectionChunkResult
        {
            Risks = risks,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the risk detection prompt.
    /// </summary>
    private static string BuildRiskDetectionPrompt(string text, RiskDetectorConfig config)
    {
        var categories = config.Categories ?? RiskDetectorConfig.Default.Categories!;
        var categoriesList = string.Join(", ", categories);

        return $$"""
            You are a legal and business risk analyst. Analyze the following document text for potential risks.

            Categories to analyze: {{categoriesList}}

            For each risk found, provide:
            1. Category (one of the categories listed above)
            2. Severity (High, Medium, or Low)
            3. Title (brief description, max 10 words)
            4. Description (detailed explanation of the risk)
            5. Location (quote the relevant text, max 100 characters)
            6. Confidence (0.0 to 1.0, how confident you are this is a real risk)
            {{(config.IncludeRecommendations ? "7. Recommendation (how to mitigate this risk)" : "")}}

            Respond in JSON format:
            ```json
            {
              "risks": [
                {
                  "category": "legal",
                  "severity": "High",
                  "title": "Unlimited liability clause",
                  "description": "The agreement contains an unlimited liability clause that could expose the party to significant financial risk.",
                  "location": "...shall be liable for all damages without limitation...",
                  "confidence": 0.95{{(config.IncludeRecommendations ? ",\n              \"recommendation\": \"Negotiate a liability cap or mutual limitation clause.\"" : "")}}
                }
              ]
            }
            ```

            Document text:
            {{text}}

            Analyze the document and identify all risks:
            """;
    }

    /// <summary>
    /// Parse risks from AI response.
    /// </summary>
    private List<RiskItem> ParseRisksFromResponse(string response)
    {
        var risks = new List<RiskItem>();

        try
        {
            // Extract JSON from response (may be wrapped in markdown code blocks)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<RiskResponseWrapper>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Risks != null)
                {
                    foreach (var risk in parsed.Risks)
                    {
                        risks.Add(new RiskItem
                        {
                            Id = Guid.NewGuid(),
                            Category = NormalizeCategory(risk.Category),
                            Severity = ParseSeverity(risk.Severity),
                            Title = risk.Title ?? "Unnamed Risk",
                            Description = risk.Description ?? "",
                            Location = risk.Location ?? "",
                            Confidence = Math.Clamp(risk.Confidence, 0.0, 1.0),
                            Recommendation = risk.Recommendation
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse risk detection response as JSON");
        }

        return risks;
    }

    /// <summary>
    /// Filter risks by severity threshold and deduplicate.
    /// </summary>
    private static List<RiskItem> FilterAndDeduplicateRisks(List<RiskItem> risks, RiskDetectorConfig config)
    {
        var threshold = ParseSeverity(config.SeverityThreshold ?? "low");

        var filtered = risks
            .Where(r => r.Severity >= threshold)
            .GroupBy(r => new { r.Category, NormalizedTitle = r.Title.ToLowerInvariant().Trim() })
            .Select(g => g.OrderByDescending(r => r.Confidence).First())
            .OrderByDescending(r => r.Severity)
            .ThenByDescending(r => r.Confidence)
            .Take(config.MaxRisks)
            .ToList();

        return filtered;
    }

    /// <summary>
    /// Generate a human-readable summary of the risks.
    /// </summary>
    private static string GenerateSummary(RiskDetectionResult result)
    {
        if (result.TotalRisksFound == 0)
            return "No risks detected in the document.";

        var summary = $"Found {result.TotalRisksFound} risk(s): ";
        var parts = new List<string>();

        if (result.HighSeverityCount > 0)
            parts.Add($"{result.HighSeverityCount} high");
        if (result.MediumSeverityCount > 0)
            parts.Add($"{result.MediumSeverityCount} medium");
        if (result.LowSeverityCount > 0)
            parts.Add($"{result.LowSeverityCount} low");

        summary += string.Join(", ", parts) + " severity.";

        if (result.HighSeverityCount > 0)
        {
            var highRisks = result.Risks.Where(r => r.Severity == RiskSeverity.High).ToList();
            summary += $"\n\nHigh severity risks:\n";
            foreach (var risk in highRisks.Take(3))
            {
                summary += $"- {risk.Title} ({risk.Category})\n";
            }
        }

        return summary;
    }

    /// <summary>
    /// Check if a severity string is valid.
    /// </summary>
    private static bool IsValidSeverity(string severity)
    {
        return severity.ToLowerInvariant() is "low" or "medium" or "high";
    }

    /// <summary>
    /// Parse severity from string.
    /// </summary>
    private static RiskSeverity ParseSeverity(string? severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "high" => RiskSeverity.High,
            "medium" => RiskSeverity.Medium,
            "low" or _ => RiskSeverity.Low
        };
    }

    /// <summary>
    /// Normalize category string.
    /// </summary>
    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "general";

        return category.ToLowerInvariant().Trim();
    }

    /// <summary>
    /// Chunk text for processing large documents.
    /// </summary>
    private static List<string> ChunkText(string text, int chunkSize)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= chunkSize)
            return new List<string> { text };

        var chunks = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - position);
            var chunk = text.Substring(position, length);

            // Try to break at sentence boundary
            if (position + length < text.Length)
            {
                var lastPeriod = chunk.LastIndexOf(". ");
                if (lastPeriod > chunkSize / 2)
                {
                    chunk = chunk.Substring(0, lastPeriod + 1);
                    length = chunk.Length;
                }
            }

            chunks.Add(chunk);

            var advance = length - ChunkOverlap;
            if (advance <= 0)
            {
                position += length;
            }
            else
            {
                position += advance;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static RiskDetectorConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return RiskDetectorConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<RiskDetectorConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? RiskDetectorConfig.Default;
        }
        catch
        {
            return RiskDetectorConfig.Default;
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
/// Configuration for Risk Detector tool.
/// </summary>
internal class RiskDetectorConfig
{
    public string[]? Categories { get; set; }
    public string? SeverityThreshold { get; set; } = "low";
    public int MaxRisks { get; set; } = 20;
    public bool IncludeRecommendations { get; set; } = true;

    public static RiskDetectorConfig Default => new()
    {
        Categories = new[] { "legal", "financial", "operational", "compliance", "contractual", "liability", "confidentiality", "termination" },
        SeverityThreshold = "low",
        MaxRisks = 20,
        IncludeRecommendations = true
    };
}

/// <summary>
/// Result from chunk risk detection.
/// </summary>
internal class RiskDetectionChunkResult
{
    public List<RiskItem> Risks { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Wrapper for parsing AI response.
/// </summary>
internal class RiskResponseWrapper
{
    public List<RiskResponseItem>? Risks { get; set; }
}

/// <summary>
/// Risk item from AI response.
/// </summary>
internal class RiskResponseItem
{
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public double Confidence { get; set; }
    public string? Recommendation { get; set; }
}

/// <summary>
/// Severity levels for risks.
/// </summary>
public enum RiskSeverity
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Individual risk item.
/// </summary>
public class RiskItem
{
    /// <summary>Unique identifier for this risk.</summary>
    public Guid Id { get; set; }

    /// <summary>Risk category (legal, financial, operational, etc.).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Severity level (High, Medium, Low).</summary>
    public RiskSeverity Severity { get; set; }

    /// <summary>Brief title describing the risk.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detailed description of the risk.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Location in document (quoted text).</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Mitigation recommendation (if requested).</summary>
    public string? Recommendation { get; set; }
}

/// <summary>
/// Complete risk detection result.
/// </summary>
public class RiskDetectionResult
{
    /// <summary>List of detected risks.</summary>
    public List<RiskItem> Risks { get; set; } = new();

    /// <summary>Total number of risks found.</summary>
    public int TotalRisksFound { get; set; }

    /// <summary>Count of high severity risks.</summary>
    public int HighSeverityCount { get; set; }

    /// <summary>Count of medium severity risks.</summary>
    public int MediumSeverityCount { get; set; }

    /// <summary>Count of low severity risks.</summary>
    public int LowSeverityCount { get; set; }

    /// <summary>Categories that were analyzed.</summary>
    public string[] CategoriesAnalyzed { get; set; } = Array.Empty<string>();
}
