using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools.Handlers;

/// <summary>
/// Analyzes contract and legal clauses in document text.
/// Identifies clause types, obligations, risks, and key terms.
/// </summary>
public class ClauseAnalyzerHandler : IAnalysisToolHandler
{
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<ClauseAnalyzerHandler> _logger;

    // Default prompt template - can be overridden via tool Configuration
    private const string DefaultPromptTemplate = """
        Analyze the following document for legal and contractual clauses. Return a JSON object with this structure:
        {
            "clauses": [
                {
                    "type": "clause type (e.g., Indemnification, Limitation of Liability, Termination, Confidentiality, etc.)",
                    "title": "clause title or heading if present",
                    "summary": "brief summary of what this clause covers",
                    "obligations": ["list of obligations this clause creates"],
                    "riskLevel": "Low|Medium|High",
                    "riskReason": "explanation if risk is Medium or High",
                    "keyTerms": ["important terms, dates, amounts, conditions"],
                    "section": "section/article number if identifiable"
                }
            ],
            "documentType": "type of document (Contract, Agreement, NDA, Terms of Service, etc.)",
            "parties": ["list of parties mentioned in the document"],
            "effectiveDate": "effective date if mentioned",
            "expirationDate": "expiration/termination date if mentioned",
            "summary": "overall document summary in 2-3 sentences"
        }

        Important:
        - Focus on legally significant clauses
        - Identify potential risks or unusual terms
        - Use empty arrays [] for categories with no matches
        - Only include clauses explicitly present in the document
        - Risk assessment should consider industry standard practices

        Document text:
        {documentText}
        """;

    public ClauseAnalyzerHandler(
        IOpenAiClient openAiClient,
        ILogger<ClauseAnalyzerHandler> logger)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ToolType ToolType => ToolType.ClauseAnalyzer;

    public string DisplayName => "Clause Analyzer";

    public string? HandlerClassName => null; // Built-in tool, no custom class name needed

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "ClauseAnalyzer starting for document {DocumentId}, content length {ContentLength}",
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
            var analysis = ParseClauseAnalysis(aiResponse);

            // Generate markdown summary
            var markdown = GenerateMarkdown(analysis);

            // Create summary
            var clauseCount = analysis.Clauses?.Length ?? 0;
            var highRiskCount = analysis.Clauses?.Count(c =>
                c.RiskLevel?.Equals("High", StringComparison.OrdinalIgnoreCase) == true) ?? 0;

            var summary = highRiskCount > 0
                ? $"Found {clauseCount} clauses ({highRiskCount} high-risk)"
                : $"Found {clauseCount} clauses";

            _logger.LogInformation(
                "ClauseAnalyzer completed for document {DocumentId}: {ClauseCount} clauses, {HighRisk} high-risk in {DurationMs}ms",
                context.DocumentId, clauseCount, highRiskCount, stopwatch.ElapsedMilliseconds);

            return AnalysisToolResult.Ok(
                data: analysis,
                summary: summary,
                markdown: markdown,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "ClauseAnalyzer failed for document {DocumentId}: {Error}",
                context.DocumentId, ex.Message);

            return AnalysisToolResult.Fail(
                $"Clause analysis failed: {ex.Message}",
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

    private ClauseAnalysisResult ParseClauseAnalysis(string aiResponse)
    {
        // Clean response (remove markdown code blocks if present)
        var cleanedResponse = CleanJsonResponse(aiResponse);

        try
        {
            var result = JsonSerializer.Deserialize<ClauseAnalysisResult>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new ClauseAnalysisResult();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse clause analysis response as JSON, returning empty result. Response preview: {Preview}",
                aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse);

            return new ClauseAnalysisResult { ParseError = ex.Message };
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

    private static string GenerateMarkdown(ClauseAnalysisResult analysis)
    {
        var sections = new List<string>();

        // Document overview
        if (!string.IsNullOrEmpty(analysis.DocumentType))
        {
            sections.Add($"**Document Type:** {analysis.DocumentType}");
        }

        if (analysis.Parties?.Length > 0)
        {
            sections.Add($"**Parties:** {string.Join(", ", analysis.Parties)}");
        }

        if (!string.IsNullOrEmpty(analysis.EffectiveDate))
        {
            sections.Add($"**Effective Date:** {analysis.EffectiveDate}");
        }

        if (!string.IsNullOrEmpty(analysis.ExpirationDate))
        {
            sections.Add($"**Expiration Date:** {analysis.ExpirationDate}");
        }

        if (!string.IsNullOrEmpty(analysis.Summary))
        {
            sections.Add($"\n{analysis.Summary}");
        }

        // Clauses
        if (analysis.Clauses?.Length > 0)
        {
            sections.Add("\n---\n### Clauses");

            foreach (var clause in analysis.Clauses)
            {
                var clauseSection = new List<string>();

                var header = !string.IsNullOrEmpty(clause.Title)
                    ? $"#### {clause.Type}: {clause.Title}"
                    : $"#### {clause.Type}";
                clauseSection.Add(header);

                if (!string.IsNullOrEmpty(clause.Section))
                {
                    clauseSection.Add($"*Section: {clause.Section}*");
                }

                if (!string.IsNullOrEmpty(clause.Summary))
                {
                    clauseSection.Add(clause.Summary);
                }

                // Risk indicator
                if (!string.IsNullOrEmpty(clause.RiskLevel))
                {
                    var riskEmoji = clause.RiskLevel.ToLowerInvariant() switch
                    {
                        "high" => "🔴",
                        "medium" => "🟡",
                        "low" => "🟢",
                        _ => "⚪"
                    };
                    var riskText = $"{riskEmoji} **Risk:** {clause.RiskLevel}";
                    if (!string.IsNullOrEmpty(clause.RiskReason))
                    {
                        riskText += $" - {clause.RiskReason}";
                    }
                    clauseSection.Add(riskText);
                }

                if (clause.Obligations?.Length > 0)
                {
                    clauseSection.Add($"**Obligations:** {string.Join("; ", clause.Obligations)}");
                }

                if (clause.KeyTerms?.Length > 0)
                {
                    clauseSection.Add($"**Key Terms:** {string.Join(", ", clause.KeyTerms)}");
                }

                sections.Add(string.Join("\n", clauseSection));
            }
        }
        else
        {
            sections.Add("\n*No clauses identified*");
        }

        return string.Join("\n\n", sections);
    }
}

/// <summary>
/// Result structure for clause analysis.
/// </summary>
public class ClauseAnalysisResult
{
    public ClauseInfo[]? Clauses { get; set; }
    public string? DocumentType { get; set; }
    public string[]? Parties { get; set; }
    public string? EffectiveDate { get; set; }
    public string? ExpirationDate { get; set; }
    public string? Summary { get; set; }

    /// <summary>
    /// If set, indicates JSON parsing failed and this contains the error.
    /// </summary>
    public string? ParseError { get; set; }
}

/// <summary>
/// Information about a single clause.
/// </summary>
public class ClauseInfo
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string[]? Obligations { get; set; }
    public string? RiskLevel { get; set; }
    public string? RiskReason { get; set; }
    public string[]? KeyTerms { get; set; }
    public string? Section { get; set; }
}
