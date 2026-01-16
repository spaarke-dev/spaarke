using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for comparing document clauses against standard/reference terms.
/// Identifies deviations, assesses their impact, and provides recommendations.
/// </summary>
/// <remarks>
/// <para>
/// ClauseComparisonHandler analyzes clauses in the document and compares them against
/// standard or reference terms. The standard terms can come from Knowledge records
/// or be provided in the context.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - clauseTypes: Array of clause types to compare (default: standard contract types)
/// - deviationThreshold: Minimum deviation level to report: "minor", "moderate", "significant" (default: "minor")
/// - maxDeviations: Maximum deviations to return (default: 30)
/// - includeRecommendations: Include remediation recommendations (default: true)
/// - includeStandardText: Include standard text in comparison (default: true)
/// </para>
/// </remarks>
public sealed class ClauseComparisonHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "ClauseComparisonHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const int DefaultMaxDeviations = 30;

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextChunkingService _textChunkingService;
    private readonly ILogger<ClauseComparisonHandler> _logger;

    public ClauseComparisonHandler(
        IOpenAiClient openAiClient,
        ITextChunkingService textChunkingService,
        ILogger<ClauseComparisonHandler> logger)
    {
        _openAiClient = openAiClient;
        _textChunkingService = textChunkingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Clause Comparison",
        Description: "Compares document clauses against standard/reference terms, identifying deviations with impact assessment and recommendations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("clause_types", "Clause types to compare", ToolParameterType.Array, Required: false,
                DefaultValue: ClauseTypes.StandardTypes),
            new ToolParameterDefinition("deviation_threshold", "Minimum deviation to report: minor, moderate, significant", ToolParameterType.String, Required: false, DefaultValue: "minor"),
            new ToolParameterDefinition("max_deviations", "Maximum deviations to return", ToolParameterType.Integer, Required: false, DefaultValue: 30),
            new ToolParameterDefinition("include_recommendations", "Include remediation recommendations", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("include_standard_text", "Include standard text in comparison", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.ClauseComparison };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for clause comparison.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<ClauseComparisonConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MaxDeviations is < 1 or > 100)
                    errors.Add("max_deviations must be between 1 and 100.");
                if (config?.DeviationThreshold != null && !IsValidDeviationThreshold(config.DeviationThreshold))
                    errors.Add("deviation_threshold must be 'minor', 'moderate', or 'significant'.");
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
                "Starting clause comparison for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

            // Build standard terms context from Knowledge (if available)
            var standardTermsContext = BuildStandardTermsContext(context.KnowledgeContext, config);

            // Chunk document if needed
            var chunkingOptions = new ChunkingOptions
            {
                ChunkSize = DefaultChunkSize,
                Overlap = ChunkOverlap,
                PreserveSentenceBoundaries = true
            };
            var textChunks = await _textChunkingService.ChunkTextAsync(documentText, chunkingOptions, cancellationToken);
            var chunks = textChunks.Select(c => c.Content).ToList();
            _logger.LogDebug(
                "Document split into {ChunkCount} chunks for clause comparison",
                chunks.Count);

            var allDeviations = new List<ClauseDeviation>();
            var allClausesAnalyzed = new List<ComparedClause>();
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Comparing clauses in chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                var result = await CompareClausesInChunkAsync(chunk, standardTermsContext, config, cancellationToken);
                allDeviations.AddRange(result.Deviations);
                allClausesAnalyzed.AddRange(result.ClausesAnalyzed);
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }

            // Filter and deduplicate deviations
            var filteredDeviations = FilterAndDeduplicateDeviations(allDeviations, config);

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

            var resultData = new ClauseComparisonResult
            {
                Deviations = filteredDeviations,
                TotalDeviationsFound = filteredDeviations.Count,
                SignificantCount = filteredDeviations.Count(d => d.Severity == DeviationSeverity.Significant),
                ModerateCount = filteredDeviations.Count(d => d.Severity == DeviationSeverity.Moderate),
                MinorCount = filteredDeviations.Count(d => d.Severity == DeviationSeverity.Minor),
                ClausesCompared = allClausesAnalyzed.DistinctBy(c => c.ClauseType).Count(),
                ClauseTypesAnalyzed = config.ClauseTypes ?? ClauseTypes.StandardTypes
            };

            var summary = GenerateSummary(resultData);

            _logger.LogInformation(
                "Clause comparison complete for {AnalysisId}: {DeviationCount} deviations found in {Duration}ms",
                context.AnalysisId, filteredDeviations.Count, stopwatch.ElapsedMilliseconds);

            // Calculate average confidence
            var avgConfidence = filteredDeviations.Count > 0
                ? filteredDeviations.Average(d => d.Confidence)
                : 0.85; // Default high confidence if no deviations (clean document)

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
            _logger.LogWarning("Clause comparison cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Clause comparison was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clause comparison failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Clause comparison failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Build context about standard terms from Knowledge records.
    /// </summary>
    private static string BuildStandardTermsContext(string? knowledgeContext, ClauseComparisonConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("STANDARD CONTRACT TERMS REFERENCE:");
        sb.AppendLine();

        // Include any knowledge context provided
        if (!string.IsNullOrWhiteSpace(knowledgeContext))
        {
            sb.AppendLine("From Organization Knowledge:");
            sb.AppendLine(knowledgeContext);
            sb.AppendLine();
        }

        // Add default standard terms guidance for each clause type
        var clauseTypes = config.ClauseTypes ?? ClauseTypes.StandardTypes;
        foreach (var clauseType in clauseTypes)
        {
            sb.AppendLine($"### {clauseType}");
            sb.AppendLine(GetStandardTermsGuidance(clauseType));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Get standard terms guidance for a clause type.
    /// </summary>
    private static string GetStandardTermsGuidance(string clauseType)
    {
        return clauseType switch
        {
            ClauseTypes.Indemnification =>
                "Standard: Mutual indemnification with reasonable scope. Each party indemnifies for own negligence and IP infringement. " +
                "Red flags: One-sided indemnification, unlimited scope, includes consequential damages.",
            ClauseTypes.LimitationOfLiability =>
                "Standard: Mutual liability cap (typically 12 months of fees or contract value). Carve-outs for IP, confidentiality, gross negligence. " +
                "Red flags: Unlimited liability, liability cap too low, no mutual protection.",
            ClauseTypes.Termination =>
                "Standard: Termination for cause with cure period (30 days), termination for convenience with notice (90 days). " +
                "Red flags: No termination rights, excessive notice periods, penalty clauses.",
            ClauseTypes.Confidentiality =>
                "Standard: Mutual confidentiality, 3-5 year term, reasonable exceptions (public info, prior knowledge). " +
                "Red flags: Perpetual terms, no exceptions, unilateral obligations.",
            ClauseTypes.DisputeResolution =>
                "Standard: Escalation to executives, mediation before litigation, arbitration clause with neutral venue. " +
                "Red flags: Distant venue, one-sided arbitration rules, waiver of jury trial.",
            ClauseTypes.GoverningLaw =>
                "Standard: Neutral jurisdiction, common law preferred. " +
                "Red flags: Unfamiliar foreign law, jurisdiction disadvantageous to one party.",
            ClauseTypes.ForcesMajeure =>
                "Standard: Reasonable force majeure events, notification requirements, termination if extended. " +
                "Red flags: Too narrow definition, no notification requirement, no termination option.",
            ClauseTypes.IntellectualProperty =>
                "Standard: Customer owns their data, vendor retains product IP, clear licensing terms. " +
                "Red flags: Unclear ownership, overly broad license grants, work-for-hire without compensation.",
            ClauseTypes.Warranty =>
                "Standard: Material compliance with specifications, reasonable remedy period, disclaimer of implied warranties. " +
                "Red flags: No warranties, too short remedy period, excessive warranty scope.",
            ClauseTypes.PaymentTerms =>
                "Standard: Net 30 payment, clear invoicing process, reasonable late fees. " +
                "Red flags: Prepayment required, excessive late fees, unclear pricing.",
            _ =>
                "Standard: Balanced, mutual obligations with clear definitions. " +
                "Red flags: One-sided terms, vague language, missing key provisions."
        };
    }

    /// <summary>
    /// Compare clauses in a single document chunk.
    /// </summary>
    private async Task<ChunkComparisonResult> CompareClausesInChunkAsync(
        string chunk,
        string standardTermsContext,
        ClauseComparisonConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildComparisonPrompt(chunk, standardTermsContext, config);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var (deviations, clauses) = ParseComparisonResponse(response);

        return new ChunkComparisonResult
        {
            Deviations = deviations,
            ClausesAnalyzed = clauses,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the clause comparison prompt.
    /// </summary>
    private static string BuildComparisonPrompt(string text, string standardTermsContext, ClauseComparisonConfig config)
    {
        var clauseTypes = config.ClauseTypes ?? ClauseTypes.StandardTypes;
        var clauseTypesList = string.Join(", ", clauseTypes);

        return $$"""
            You are a legal contract analyst specializing in clause comparison. Compare the document clauses against standard terms.

            {{standardTermsContext}}

            CLAUSE TYPES TO ANALYZE: {{clauseTypesList}}

            For each clause found, compare it against the standard terms and identify any deviations.

            For each deviation found, provide:
            1. clauseType: The type of clause (from the list above)
            2. severity: "Minor", "Moderate", or "Significant"
            3. documentText: The relevant text from the document (max 300 chars)
            {{(config.IncludeStandardText ? "4. standardExpectation: What standard terms would expect (max 200 chars)" : "")}}
            5. deviationDescription: What specifically deviates (max 200 chars)
            6. impact: Business/legal impact of this deviation
            7. confidence: 0.0 to 1.0 confidence in this finding
            {{(config.IncludeRecommendations ? "8. recommendation: How to address this deviation" : "")}}

            Also list which clause types you analyzed (even if no deviations found).

            Respond in JSON format:
            ```json
            {
              "deviations": [
                {
                  "clauseType": "LimitationOfLiability",
                  "severity": "Significant",
                  "documentText": "Vendor shall not be liable for any damages whatsoever...",
                  "standardExpectation": "Mutual liability cap of 12 months fees with reasonable carve-outs",
                  "deviationDescription": "Complete disclaimer of liability with no cap, highly one-sided",
                  "impact": "High financial risk if vendor causes damages",
                  "confidence": 0.95,
                  "recommendation": "Negotiate for mutual liability cap with reasonable carve-outs"
                }
              ],
              "clausesAnalyzed": [
                {"clauseType": "LimitationOfLiability", "found": true},
                {"clauseType": "Indemnification", "found": false}
              ]
            }
            ```

            Document text to analyze:
            {{text}}

            Analyze and compare against standard terms:
            """;
    }

    /// <summary>
    /// Parse comparison response from AI.
    /// </summary>
    private (List<ClauseDeviation> Deviations, List<ComparedClause> Clauses) ParseComparisonResponse(string response)
    {
        var deviations = new List<ClauseDeviation>();
        var clauses = new List<ComparedClause>();

        try
        {
            // Extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<ComparisonResponseWrapper>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Deviations != null)
                {
                    foreach (var dev in parsed.Deviations)
                    {
                        deviations.Add(new ClauseDeviation
                        {
                            Id = Guid.NewGuid(),
                            ClauseType = dev.ClauseType ?? "General",
                            Severity = ParseSeverity(dev.Severity),
                            DocumentText = dev.DocumentText ?? "",
                            StandardExpectation = dev.StandardExpectation,
                            DeviationDescription = dev.DeviationDescription ?? "",
                            Impact = dev.Impact ?? "",
                            Confidence = Math.Clamp(dev.Confidence, 0.0, 1.0),
                            Recommendation = dev.Recommendation
                        });
                    }
                }

                if (parsed?.ClausesAnalyzed != null)
                {
                    clauses.AddRange(parsed.ClausesAnalyzed);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse clause comparison response as JSON");
        }

        return (deviations, clauses);
    }

    /// <summary>
    /// Filter deviations by threshold and deduplicate.
    /// </summary>
    private static List<ClauseDeviation> FilterAndDeduplicateDeviations(
        List<ClauseDeviation> deviations,
        ClauseComparisonConfig config)
    {
        var threshold = ParseSeverity(config.DeviationThreshold ?? "minor");

        var filtered = deviations
            .Where(d => d.Severity >= threshold)
            .GroupBy(d => new { d.ClauseType, NormalizedDesc = NormalizeText(d.DeviationDescription) })
            .Select(g => g.OrderByDescending(d => d.Confidence).First())
            .OrderByDescending(d => d.Severity)
            .ThenByDescending(d => d.Confidence)
            .Take(config.MaxDeviations)
            .ToList();

        return filtered;
    }

    /// <summary>
    /// Generate summary of comparison results.
    /// </summary>
    private static string GenerateSummary(ClauseComparisonResult result)
    {
        if (result.TotalDeviationsFound == 0)
            return $"No deviations from standard terms found. {result.ClausesCompared} clause types analyzed.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {result.TotalDeviationsFound} deviation(s) from standard terms:");

        var parts = new List<string>();
        if (result.SignificantCount > 0)
            parts.Add($"{result.SignificantCount} significant");
        if (result.ModerateCount > 0)
            parts.Add($"{result.ModerateCount} moderate");
        if (result.MinorCount > 0)
            parts.Add($"{result.MinorCount} minor");

        sb.AppendLine(string.Join(", ", parts));

        if (result.SignificantCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Significant deviations:");
            foreach (var dev in result.Deviations.Where(d => d.Severity == DeviationSeverity.Significant).Take(3))
            {
                sb.AppendLine($"- {dev.ClauseType}: {dev.DeviationDescription}");
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Check if deviation threshold is valid.
    /// </summary>
    private static bool IsValidDeviationThreshold(string threshold)
    {
        return threshold.ToLowerInvariant() is "minor" or "moderate" or "significant";
    }

    /// <summary>
    /// Parse severity from string.
    /// </summary>
    private static DeviationSeverity ParseSeverity(string? severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "significant" => DeviationSeverity.Significant,
            "moderate" => DeviationSeverity.Moderate,
            "minor" or _ => DeviationSeverity.Minor
        };
    }

    /// <summary>
    /// Normalize text for comparison.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return text.Trim().ToLowerInvariant().Substring(0, Math.Min(50, text.Length));
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static ClauseComparisonConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return ClauseComparisonConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<ClauseComparisonConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? ClauseComparisonConfig.Default;
        }
        catch
        {
            return ClauseComparisonConfig.Default;
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
/// Severity levels for clause deviations.
/// </summary>
public enum DeviationSeverity
{
    /// <summary>Minor deviation - low risk, may be acceptable.</summary>
    Minor = 0,

    /// <summary>Moderate deviation - warrants attention and possible negotiation.</summary>
    Moderate = 1,

    /// <summary>Significant deviation - high risk, should be addressed.</summary>
    Significant = 2
}

/// <summary>
/// Configuration for ClauseComparison tool.
/// </summary>
internal class ClauseComparisonConfig
{
    public string[]? ClauseTypes { get; set; }
    public string? DeviationThreshold { get; set; } = "minor";
    public int MaxDeviations { get; set; } = 30;
    public bool IncludeRecommendations { get; set; } = true;
    public bool IncludeStandardText { get; set; } = true;

    public static ClauseComparisonConfig Default => new()
    {
        ClauseTypes = Tools.ClauseTypes.StandardTypes,
        DeviationThreshold = "minor",
        MaxDeviations = 30,
        IncludeRecommendations = true,
        IncludeStandardText = true
    };
}

/// <summary>
/// Result from chunk comparison.
/// </summary>
internal class ChunkComparisonResult
{
    public List<ClauseDeviation> Deviations { get; set; } = new();
    public List<ComparedClause> ClausesAnalyzed { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Wrapper for parsing AI response.
/// </summary>
internal class ComparisonResponseWrapper
{
    public List<DeviationResponseItem>? Deviations { get; set; }
    public List<ComparedClause>? ClausesAnalyzed { get; set; }
}

/// <summary>
/// Deviation item from AI response.
/// </summary>
internal class DeviationResponseItem
{
    public string? ClauseType { get; set; }
    public string? Severity { get; set; }
    public string? DocumentText { get; set; }
    public string? StandardExpectation { get; set; }
    public string? DeviationDescription { get; set; }
    public string? Impact { get; set; }
    public double Confidence { get; set; }
    public string? Recommendation { get; set; }
}

/// <summary>
/// A clause that was analyzed during comparison.
/// </summary>
public class ComparedClause
{
    /// <summary>The clause type.</summary>
    public string ClauseType { get; set; } = string.Empty;

    /// <summary>Whether the clause was found in the document.</summary>
    public bool Found { get; set; }
}

/// <summary>
/// A deviation from standard terms.
/// </summary>
public class ClauseDeviation
{
    /// <summary>Unique identifier for this deviation.</summary>
    public Guid Id { get; set; }

    /// <summary>The clause type (e.g., "LimitationOfLiability").</summary>
    public string ClauseType { get; set; } = string.Empty;

    /// <summary>Severity of the deviation.</summary>
    public DeviationSeverity Severity { get; set; }

    /// <summary>The relevant text from the document.</summary>
    public string DocumentText { get; set; } = string.Empty;

    /// <summary>What standard terms would expect.</summary>
    public string? StandardExpectation { get; set; }

    /// <summary>Description of what specifically deviates.</summary>
    public string DeviationDescription { get; set; } = string.Empty;

    /// <summary>Business/legal impact of this deviation.</summary>
    public string Impact { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Recommendation for addressing this deviation.</summary>
    public string? Recommendation { get; set; }
}

/// <summary>
/// Complete clause comparison result.
/// </summary>
public class ClauseComparisonResult
{
    /// <summary>List of deviations found.</summary>
    public List<ClauseDeviation> Deviations { get; set; } = new();

    /// <summary>Total number of deviations found.</summary>
    public int TotalDeviationsFound { get; set; }

    /// <summary>Count of significant deviations.</summary>
    public int SignificantCount { get; set; }

    /// <summary>Count of moderate deviations.</summary>
    public int ModerateCount { get; set; }

    /// <summary>Count of minor deviations.</summary>
    public int MinorCount { get; set; }

    /// <summary>Number of clause types compared.</summary>
    public int ClausesCompared { get; set; }

    /// <summary>Clause types that were analyzed.</summary>
    public string[] ClauseTypesAnalyzed { get; set; } = Array.Empty<string>();
}
