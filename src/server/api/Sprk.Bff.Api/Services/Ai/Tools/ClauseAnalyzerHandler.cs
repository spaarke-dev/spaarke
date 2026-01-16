using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for analyzing contract clauses in legal documents.
/// Identifies clause types, assesses risk levels, and flags unusual or missing clauses.
/// </summary>
/// <remarks>
/// <para>
/// Clause analysis uses Azure OpenAI with structured JSON output.
/// For large documents, content is chunked and clauses are aggregated.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - clauseTypes: Array of clause types to detect (default: all standard types)
/// - includeRiskAssessment: Whether to assess risk per clause (default: true)
/// - includeStandardComparison: Compare against standard language (default: true)
/// - detectMissingClauses: Flag expected but missing clauses (default: true)
/// - chunkSize: Characters per chunk for large docs (default: 8000)
/// </para>
/// </remarks>
public sealed class ClauseAnalyzerHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "ClauseAnalyzerHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextChunkingService _textChunkingService;
    private readonly ILogger<ClauseAnalyzerHandler> _logger;

    public ClauseAnalyzerHandler(
        IOpenAiClient openAiClient,
        ITextChunkingService textChunkingService,
        ILogger<ClauseAnalyzerHandler> logger)
    {
        _openAiClient = openAiClient;
        _textChunkingService = textChunkingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Clause Analyzer",
        Description: "Analyzes contract documents to identify clause types, assess risk levels, compare against standard language, and flag unusual or missing clauses.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("clauseTypes", "Clause types to detect", ToolParameterType.Array, Required: false,
                DefaultValue: ClauseTypes.StandardTypes),
            new ToolParameterDefinition("includeRiskAssessment", "Whether to assess risk per clause", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("includeStandardComparison", "Compare against standard language", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("detectMissingClauses", "Flag expected but missing clauses", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("chunkSize", "Characters per chunk for large documents", ToolParameterType.Integer, Required: false, DefaultValue: 8000)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.ClauseAnalyzer };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for clause analysis.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<ClauseAnalyzerConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
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
                "Starting clause analysis for analysis {AnalysisId}, document {DocumentId}",
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

            // Analyze clauses from each chunk
            var allClauses = new List<AnalyzedClause>();
            var totalInputTokens = 0;
            var totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Processing chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                var chunkResult = await AnalyzeClausesInChunkAsync(
                    chunk,
                    config.ClauseTypes ?? ClauseTypes.StandardTypes,
                    config.IncludeRiskAssessment,
                    config.IncludeStandardComparison,
                    cancellationToken);

                if (chunkResult.Clauses != null)
                {
                    allClauses.AddRange(chunkResult.Clauses);
                }

                totalInputTokens += chunkResult.InputTokens;
                totalOutputTokens += chunkResult.OutputTokens;
            }

            // Aggregate and deduplicate clauses
            var aggregatedClauses = AggregateClauses(allClauses);

            // Detect missing expected clauses if enabled
            var missingClauses = config.DetectMissingClauses
                ? DetectMissingClauses(aggregatedClauses, config.ClauseTypes ?? ClauseTypes.StandardTypes)
                : new List<MissingClause>();

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
            var resultData = new ClauseAnalysisResult
            {
                Clauses = aggregatedClauses,
                TotalClausesFound = aggregatedClauses.Count,
                ClausesByType = aggregatedClauses
                    .GroupBy(c => c.Type)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ClausesByRisk = aggregatedClauses
                    .Where(c => c.RiskLevel.HasValue)
                    .GroupBy(c => c.RiskLevel!.Value)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                MissingClauses = missingClauses,
                ChunksProcessed = chunks.Count
            };

            var summary = BuildSummary(aggregatedClauses, missingClauses);

            _logger.LogInformation(
                "Clause analysis complete for {AnalysisId}: {ClauseCount} clauses found, {MissingCount} missing in {Duration}ms",
                context.AnalysisId, aggregatedClauses.Count, missingClauses.Count, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary,
                CalculateOverallConfidence(aggregatedClauses),
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Clause analysis cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Clause analysis was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clause analysis failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Clause analysis failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Analyze clauses in a single text chunk using Azure OpenAI.
    /// </summary>
    private async Task<ChunkAnalysisResult> AnalyzeClausesInChunkAsync(
        string chunk,
        string[] clauseTypes,
        bool includeRiskAssessment,
        bool includeStandardComparison,
        CancellationToken cancellationToken)
    {
        var prompt = BuildAnalysisPrompt(chunk, clauseTypes, includeRiskAssessment, includeStandardComparison);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var clauses = ParseClausesFromResponse(response);

        return new ChunkAnalysisResult
        {
            Clauses = clauses,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the clause analysis prompt for Azure OpenAI.
    /// </summary>
    private static string BuildAnalysisPrompt(string text, string[] clauseTypes, bool includeRiskAssessment, bool includeStandardComparison)
    {
        var typesDescription = string.Join(", ", clauseTypes);
        var riskInstruction = includeRiskAssessment
            ? "- riskLevel: Risk assessment (\"Low\", \"Medium\", \"High\", \"Critical\")\n  - riskReason: Brief explanation for the risk level"
            : "";
        var comparisonInstruction = includeStandardComparison
            ? "- deviatesFromStandard: true/false - whether this clause deviates from standard language\n  - deviationNotes: If deviates, explain how"
            : "";

        return $$"""
            You are a legal document analyst. Analyze the following text and identify contract clauses.

            Clause types to identify: {{typesDescription}}

            For each clause found, provide:
            - type: One of the specified clause types
            - text: The full text of the clause (max 500 chars)
            - summary: Brief summary of what the clause does (max 100 chars)
            - confidence: Your confidence score (0.0-1.0) in the identification
            {{riskInstruction}}
            {{comparisonInstruction}}

            Return ONLY valid JSON in this format:
            {
              "clauses": [
                {
                  "type": "Indemnification",
                  "text": "The Supplier shall indemnify and hold harmless...",
                  "summary": "Supplier indemnifies buyer against third-party claims",
                  "confidence": 0.95,
                  "riskLevel": "Medium",
                  "riskReason": "Standard indemnification but no cap specified",
                  "deviatesFromStandard": false,
                  "deviationNotes": null
                }
              ]
            }

            If no clauses are found, return: {"clauses": []}

            Text to analyze:
            {{text}}
            """;
    }

    /// <summary>
    /// Parse clauses from the AI response JSON.
    /// </summary>
    private List<AnalyzedClause> ParseClausesFromResponse(string response)
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

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var result = JsonSerializer.Deserialize<ClauseAnalysisResponse>(json, options);

            return result?.Clauses ?? new List<AnalyzedClause>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse clause analysis response: {Response}", response);
            return new List<AnalyzedClause>();
        }
    }

    /// <summary>
    /// Aggregate clauses from multiple chunks, handling duplicates.
    /// </summary>
    private static List<AnalyzedClause> AggregateClauses(List<AnalyzedClause> allClauses)
    {
        // Group by type and similar text (using normalized first 100 chars)
        var grouped = allClauses
            .GroupBy(c => (c.Type, NormalizeText(c.Text?.Substring(0, Math.Min(100, c.Text?.Length ?? 0)) ?? "")));

        var aggregated = new List<AnalyzedClause>();

        foreach (var group in grouped)
        {
            var clauses = group.ToList();
            var best = clauses.OrderByDescending(c => c.Confidence).First();
            var avgConfidence = clauses.Average(c => c.Confidence);

            aggregated.Add(new AnalyzedClause
            {
                Type = best.Type,
                Text = best.Text,
                Summary = best.Summary,
                Confidence = Math.Round(avgConfidence, 2),
                RiskLevel = best.RiskLevel,
                RiskReason = best.RiskReason,
                DeviatesFromStandard = best.DeviatesFromStandard,
                DeviationNotes = best.DeviationNotes
            });
        }

        return aggregated.OrderBy(c => c.Type).ThenByDescending(c => c.Confidence).ToList();
    }

    /// <summary>
    /// Detect expected clause types that are missing from the document.
    /// </summary>
    private static List<MissingClause> DetectMissingClauses(List<AnalyzedClause> foundClauses, string[] expectedTypes)
    {
        var foundTypes = foundClauses.Select(c => c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<MissingClause>();

        foreach (var expectedType in expectedTypes)
        {
            if (!foundTypes.Contains(expectedType))
            {
                missing.Add(new MissingClause
                {
                    Type = expectedType,
                    Importance = GetClauseImportance(expectedType),
                    Recommendation = GetMissingClauseRecommendation(expectedType)
                });
            }
        }

        return missing.OrderByDescending(m => m.Importance).ToList();
    }

    /// <summary>
    /// Get the importance level for a clause type.
    /// </summary>
    private static string GetClauseImportance(string clauseType)
    {
        return clauseType switch
        {
            ClauseTypes.Indemnification => "High",
            ClauseTypes.LimitationOfLiability => "High",
            ClauseTypes.Termination => "High",
            ClauseTypes.Confidentiality => "High",
            ClauseTypes.DisputeResolution => "Medium",
            ClauseTypes.GoverningLaw => "Medium",
            ClauseTypes.ForcesMajeure => "Medium",
            ClauseTypes.IntellectualProperty => "High",
            ClauseTypes.Warranty => "Medium",
            ClauseTypes.PaymentTerms => "High",
            ClauseTypes.Assignment => "Low",
            ClauseTypes.Notices => "Low",
            ClauseTypes.Amendment => "Low",
            ClauseTypes.Severability => "Low",
            ClauseTypes.EntireAgreement => "Low",
            _ => "Medium"
        };
    }

    /// <summary>
    /// Get recommendation text for a missing clause.
    /// </summary>
    private static string GetMissingClauseRecommendation(string clauseType)
    {
        return clauseType switch
        {
            ClauseTypes.Indemnification => "Consider adding indemnification provisions to protect against third-party claims.",
            ClauseTypes.LimitationOfLiability => "Consider adding liability caps to limit exposure.",
            ClauseTypes.Termination => "Consider adding clear termination rights and procedures.",
            ClauseTypes.Confidentiality => "Consider adding confidentiality obligations to protect sensitive information.",
            ClauseTypes.DisputeResolution => "Consider specifying dispute resolution mechanisms (arbitration, mediation, litigation).",
            ClauseTypes.GoverningLaw => "Consider specifying which jurisdiction's laws govern the agreement.",
            ClauseTypes.ForcesMajeure => "Consider adding force majeure provisions for unexpected events.",
            ClauseTypes.IntellectualProperty => "Consider clarifying IP ownership and licensing rights.",
            ClauseTypes.Warranty => "Consider adding warranty provisions and disclaimer of warranties.",
            ClauseTypes.PaymentTerms => "Consider specifying payment terms, timing, and methods.",
            _ => $"Consider adding a {clauseType} clause to address this area."
        };
    }

    /// <summary>
    /// Normalize text for comparison.
    /// </summary>
    private static string NormalizeText(string text)
    {
        return text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Build a human-readable summary of the analysis.
    /// </summary>
    private static string BuildSummary(List<AnalyzedClause> clauses, List<MissingClause> missingClauses)
    {
        if (clauses.Count == 0 && missingClauses.Count == 0)
            return "No clauses were identified in the document.";

        var sb = new StringBuilder();

        if (clauses.Count > 0)
        {
            sb.AppendLine($"Found {clauses.Count} clause(s):");
            var byType = clauses.GroupBy(c => c.Type).OrderByDescending(g => g.Count());
            foreach (var group in byType.Take(5))
            {
                var riskInfo = group.Any(c => c.RiskLevel.HasValue)
                    ? $" [Risk: {group.Max(c => c.RiskLevel)}]"
                    : "";
                sb.AppendLine($"- {group.Key}: {group.Count()}{riskInfo}");
            }
            if (byType.Count() > 5)
            {
                sb.AppendLine($"- ... and {byType.Count() - 5} more types");
            }
        }

        if (missingClauses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Missing {missingClauses.Count} expected clause(s):");
            foreach (var missing in missingClauses.Where(m => m.Importance == "High").Take(3))
            {
                sb.AppendLine($"- {missing.Type} (Importance: {missing.Importance})");
            }
            if (missingClauses.Count(m => m.Importance == "High") > 3)
            {
                sb.AppendLine($"- ... and {missingClauses.Count - 3} more");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Calculate overall confidence from clause confidences.
    /// </summary>
    private static double CalculateOverallConfidence(List<AnalyzedClause> clauses)
    {
        if (clauses.Count == 0)
            return 0;

        return Math.Round(clauses.Average(c => c.Confidence), 2);
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static ClauseAnalyzerConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return ClauseAnalyzerConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<ClauseAnalyzerConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? ClauseAnalyzerConfig.Default;
        }
        catch
        {
            return ClauseAnalyzerConfig.Default;
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
/// Standard contract clause types.
/// </summary>
public static class ClauseTypes
{
    public const string Indemnification = "Indemnification";
    public const string LimitationOfLiability = "LimitationOfLiability";
    public const string Termination = "Termination";
    public const string Confidentiality = "Confidentiality";
    public const string DisputeResolution = "DisputeResolution";
    public const string GoverningLaw = "GoverningLaw";
    public const string ForcesMajeure = "ForceMajeure";
    public const string IntellectualProperty = "IntellectualProperty";
    public const string Warranty = "Warranty";
    public const string PaymentTerms = "PaymentTerms";
    public const string Assignment = "Assignment";
    public const string Notices = "Notices";
    public const string Amendment = "Amendment";
    public const string Severability = "Severability";
    public const string EntireAgreement = "EntireAgreement";

    /// <summary>
    /// Standard set of clause types for contract analysis.
    /// </summary>
    public static string[] StandardTypes => new[]
    {
        Indemnification,
        LimitationOfLiability,
        Termination,
        Confidentiality,
        DisputeResolution,
        GoverningLaw,
        ForcesMajeure,
        IntellectualProperty,
        Warranty,
        PaymentTerms
    };
}

/// <summary>
/// Risk level for contract clauses.
/// </summary>
public enum RiskLevel
{
    /// <summary>Low risk - standard, balanced language.</summary>
    Low = 0,

    /// <summary>Medium risk - some deviation from standard or minor concern.</summary>
    Medium = 1,

    /// <summary>High risk - significant concern or one-sided terms.</summary>
    High = 2,

    /// <summary>Critical risk - immediate legal review required.</summary>
    Critical = 3
}

/// <summary>
/// Configuration for ClauseAnalyzer tool.
/// </summary>
internal class ClauseAnalyzerConfig
{
    public string[]? ClauseTypes { get; set; }
    public bool IncludeRiskAssessment { get; set; } = true;
    public bool IncludeStandardComparison { get; set; } = true;
    public bool DetectMissingClauses { get; set; } = true;
    public int ChunkSize { get; set; } = 8000;

    public static ClauseAnalyzerConfig Default => new()
    {
        ClauseTypes = Tools.ClauseTypes.StandardTypes,
        IncludeRiskAssessment = true,
        IncludeStandardComparison = true,
        DetectMissingClauses = true,
        ChunkSize = 8000
    };
}

/// <summary>
/// Result of clause analysis from a single chunk.
/// </summary>
internal class ChunkAnalysisResult
{
    public List<AnalyzedClause>? Clauses { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Response structure from AI clause analysis.
/// </summary>
internal class ClauseAnalysisResponse
{
    public List<AnalyzedClause>? Clauses { get; set; }
}

/// <summary>
/// A single analyzed contract clause.
/// </summary>
public class AnalyzedClause
{
    /// <summary>The clause type classification.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The full text of the clause.</summary>
    public string? Text { get; set; }

    /// <summary>Brief summary of the clause.</summary>
    public string? Summary { get; set; }

    /// <summary>Confidence score (0.0-1.0) of the identification.</summary>
    public double Confidence { get; set; }

    /// <summary>Risk level assessment.</summary>
    public RiskLevel? RiskLevel { get; set; }

    /// <summary>Explanation for the risk level.</summary>
    public string? RiskReason { get; set; }

    /// <summary>Whether this clause deviates from standard language.</summary>
    public bool? DeviatesFromStandard { get; set; }

    /// <summary>Notes on how the clause deviates from standard.</summary>
    public string? DeviationNotes { get; set; }
}

/// <summary>
/// Information about an expected but missing clause.
/// </summary>
public class MissingClause
{
    /// <summary>The missing clause type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Importance level (High, Medium, Low).</summary>
    public string Importance { get; set; } = "Medium";

    /// <summary>Recommendation for addressing the missing clause.</summary>
    public string? Recommendation { get; set; }
}

/// <summary>
/// Complete result of clause analysis.
/// </summary>
public class ClauseAnalysisResult
{
    /// <summary>List of analyzed clauses.</summary>
    public List<AnalyzedClause> Clauses { get; set; } = new();

    /// <summary>Total number of clauses found.</summary>
    public int TotalClausesFound { get; set; }

    /// <summary>Count of clauses by type.</summary>
    public Dictionary<string, int> ClausesByType { get; set; } = new();

    /// <summary>Count of clauses by risk level.</summary>
    public Dictionary<string, int> ClausesByRisk { get; set; } = new();

    /// <summary>Expected but missing clauses.</summary>
    public List<MissingClause> MissingClauses { get; set; } = new();

    /// <summary>Number of document chunks processed.</summary>
    public int ChunksProcessed { get; set; }
}
