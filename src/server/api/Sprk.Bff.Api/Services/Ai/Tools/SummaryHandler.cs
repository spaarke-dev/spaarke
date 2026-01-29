using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for generating document summaries.
/// Produces executive summaries, key points, and structured overviews.
/// </summary>
/// <remarks>
/// <para>
/// Summary generation uses Azure OpenAI with configurable output length.
/// For large documents, content is chunked and summaries are synthesized.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - maxLength: Maximum summary length in words (default: 500)
/// - format: Output format - "paragraph", "bullets", "structured" (default: "structured")
/// - includeSections: Array of sections to include (default: all)
/// - chunkSize: Characters per chunk for large docs (default: 8000)
/// </para>
/// </remarks>
public sealed class SummaryHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "SummaryHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const int DefaultMaxLength = 500;

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextChunkingService _textChunkingService;
    private readonly ILogger<SummaryHandler> _logger;

    /// <summary>JSON Schema (Draft 07) for configuration validation.</summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Summary Generator Configuration",
        type = "object",
        properties = new
        {
            max_length = new
            {
                type = "integer",
                description = "Maximum summary length in words",
                minimum = 50,
                maximum = 5000,
                @default = 500
            },
            format = new
            {
                type = "string",
                description = "Output format: paragraph, bullets, or structured",
                @enum = new[] { "paragraph", "bullets", "structured" },
                @default = "structured"
            },
            include_sections = new
            {
                type = "array",
                description = "Sections to include in summary",
                items = new { type = "string" },
                @default = new[] { "executive_summary", "key_terms", "obligations", "notable_provisions" }
            },
            use_plain_language = new
            {
                type = "boolean",
                description = "Use plain language accessible to non-lawyers",
                @default = true
            },
            highlight_time_sensitive = new
            {
                type = "boolean",
                description = "Highlight time-sensitive items",
                @default = true
            }
        }
    };

    public SummaryHandler(
        IOpenAiClient openAiClient,
        ITextChunkingService textChunkingService,
        ILogger<SummaryHandler> logger)
    {
        _openAiClient = openAiClient;
        _textChunkingService = textChunkingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Summary Generator",
        Description: "Generates concise document summaries focusing on key points, parties, terms, and essential obligations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("max_length", "Maximum summary length in words", ToolParameterType.Integer, Required: false, DefaultValue: 500),
            new ToolParameterDefinition("format", "Output format: paragraph, bullets, or structured", ToolParameterType.String, Required: false, DefaultValue: "structured"),
            new ToolParameterDefinition("include_sections", "Sections to include in summary", ToolParameterType.Array, Required: false,
                DefaultValue: new[] { "executive_summary", "key_terms", "obligations", "notable_provisions" }),
            new ToolParameterDefinition("use_plain_language", "Use plain language accessible to non-lawyers", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("highlight_time_sensitive", "Highlight time-sensitive items", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Summary };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for summarization.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<SummaryConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MaxLength is < 50 or > 5000)
                    errors.Add("max_length must be between 50 and 5000 words.");
                if (config?.Format != null && !IsValidFormat(config.Format))
                    errors.Add("format must be 'paragraph', 'bullets', or 'structured'.");
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
                "Starting document summarization for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

            // Chunk document if needed using the consolidated chunking service
            // SummaryHandler uses 8000 char chunks with 200 overlap (different from RAG defaults)
            var chunkingOptions = new ChunkingOptions
            {
                ChunkSize = DefaultChunkSize,
                Overlap = ChunkOverlap,
                PreserveSentenceBoundaries = true
            };
            var textChunks = await _textChunkingService.ChunkTextAsync(documentText, chunkingOptions, cancellationToken);
            var chunks = textChunks.Select(c => c.Content).ToList();

            _logger.LogDebug(
                "Document split into {ChunkCount} chunks for summarization",
                chunks.Count);

            string summaryText;
            double confidence;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            if (chunks.Count == 1)
            {
                // Single chunk - generate summary directly
                var result = await GenerateSummaryAsync(chunks[0], config, cancellationToken);
                summaryText = result.Summary;
                confidence = result.Confidence;
                totalInputTokens = result.InputTokens;
                totalOutputTokens = result.OutputTokens;
            }
            else
            {
                // Multiple chunks - generate chunk summaries then synthesize
                var chunkSummaries = new List<string>();
                var chunkConfidences = new List<double>();

                foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.LogDebug("Summarizing chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                    var chunkResult = await GenerateChunkSummaryAsync(chunk, index + 1, chunks.Count, cancellationToken);
                    chunkSummaries.Add(chunkResult.Summary);
                    chunkConfidences.Add(chunkResult.Confidence);
                    totalInputTokens += chunkResult.InputTokens;
                    totalOutputTokens += chunkResult.OutputTokens;
                }

                // Synthesize final summary from chunk summaries
                var synthesisResult = await SynthesizeSummaryAsync(chunkSummaries, chunkConfidences, config, cancellationToken);
                summaryText = synthesisResult.Summary;
                confidence = synthesisResult.Confidence;
                totalInputTokens += synthesisResult.InputTokens;
                totalOutputTokens += synthesisResult.OutputTokens;
            }

            stopwatch.Stop();

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                ModelCalls = chunks.Count == 1 ? 1 : chunks.Count + 1,
                ModelName = "gpt-4o-mini"
            };

            // Parse structured summary if format is structured
            var resultData = ParseSummaryResult(summaryText, confidence, config);

            _logger.LogInformation(
                "Document summarization complete for {AnalysisId}: {WordCount} words in {Duration}ms",
                context.AnalysisId, CountWords(summaryText), stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summaryText,
                confidence,
                executionMetadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Document summarization cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Document summarization was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document summarization failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Document summarization failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Generate a complete summary from a single document chunk.
    /// </summary>
    private async Task<SummaryGenerationResult> GenerateSummaryAsync(
        string text,
        SummaryConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildSummaryPrompt(text, config);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var (summary, confidence) = ParseSummaryJsonResponse(response);

        return new SummaryGenerationResult
        {
            Summary = summary,
            Confidence = confidence,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Generate a brief summary from a chunk (for multi-chunk documents).
    /// </summary>
    private async Task<SummaryGenerationResult> GenerateChunkSummaryAsync(
        string chunk,
        int chunkIndex,
        int totalChunks,
        CancellationToken cancellationToken)
    {
        var prompt = $$"""
            You are summarizing part {{chunkIndex}} of {{totalChunks}} of a document.

            Create a detailed summary of this section, capturing all important information,
            parties mentioned, dates, obligations, and key terms. This will be combined with
            summaries of other sections to create a final document summary.

            Text to summarize:
            {{chunk}}

            Return ONLY valid JSON in this format:
            {
              "summary": "Your 300-500 word summary of this section...",
              "confidence": 0.85
            }

            Where confidence is a score from 0.0 to 1.0 indicating how well you captured all important information from this section.
            """;

        var inputTokens = EstimateTokens(prompt);
        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var (summary, confidence) = ParseSummaryJsonResponse(response);

        return new SummaryGenerationResult
        {
            Summary = summary,
            Confidence = confidence,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Synthesize a final summary from multiple chunk summaries.
    /// </summary>
    private async Task<SummaryGenerationResult> SynthesizeSummaryAsync(
        List<string> chunkSummaries,
        List<double> chunkConfidences,
        SummaryConfig config,
        CancellationToken cancellationToken)
    {
        var combinedSummaries = string.Join("\n\n---\n\n", chunkSummaries.Select((s, i) => $"Section {i + 1}:\n{s}"));

        var formatInstructions = GetFormatInstructions(config);
        var sectionsToInclude = config.IncludeSections ?? SummaryConfig.Default.IncludeSections!;

        var prompt = $$"""
            You have summaries of {{chunkSummaries.Count}} sections of a document.
            Synthesize these into a cohesive final summary.

            {{formatInstructions}}

            Include these sections: {{string.Join(", ", sectionsToInclude)}}

            Target length: approximately {{config.MaxLength}} words
            {{(config.UsePlainLanguage ? "Use plain language accessible to non-legal readers." : "")}}
            {{(config.HighlightTimeSensitive ? "Highlight any time-sensitive deadlines or dates." : "")}}

            Section summaries:
            {{combinedSummaries}}

            Return ONLY valid JSON in this format:
            {
              "summary": "Your synthesized summary with all requested sections...",
              "confidence": 0.85
            }

            Where confidence is a score from 0.0 to 1.0 indicating:
            - How well the section summaries allowed you to create a complete picture
            - How accurate and comprehensive the final summary is
            - Consider the input confidence scores were: {{string.Join(", ", chunkConfidences.Select(c => c.ToString("F2")))}}
            """;

        var inputTokens = EstimateTokens(prompt);
        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var (summary, confidence) = ParseSummaryJsonResponse(response);

        return new SummaryGenerationResult
        {
            Summary = summary,
            Confidence = confidence,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the main summary generation prompt.
    /// </summary>
    private static string BuildSummaryPrompt(string text, SummaryConfig config)
    {
        var formatInstructions = GetFormatInstructions(config);
        var sectionsToInclude = config.IncludeSections ?? SummaryConfig.Default.IncludeSections!;

        return $$"""
            You are a document summarization assistant. Create a comprehensive summary of the following document.

            {{formatInstructions}}

            Include these sections:
            {{string.Join("\n", sectionsToInclude.Select(s => $"- {FormatSectionName(s)}"))}}

            Target length: approximately {{config.MaxLength}} words
            {{(config.UsePlainLanguage ? "Use plain language that is accessible to non-legal readers." : "")}}
            {{(config.HighlightTimeSensitive ? "Highlight any time-sensitive deadlines, dates, or renewal periods." : "")}}

            Document to summarize:
            {{text}}

            Return ONLY valid JSON in this format:
            {
              "summary": "Your complete summary text here with all sections...",
              "confidence": 0.85
            }

            Where confidence is a score from 0.0 to 1.0 indicating your confidence in:
            - How well you understood the document
            - How accurately the summary captures the key points
            - How complete the summary is
            """;
    }

    /// <summary>
    /// Get format-specific instructions for the prompt.
    /// </summary>
    private static string GetFormatInstructions(SummaryConfig config)
    {
        return config.Format?.ToLowerInvariant() switch
        {
            "paragraph" => "Write the summary as flowing paragraphs.",
            "bullets" => "Write the summary as bullet points organized by topic.",
            "structured" or _ => """
                Structure the summary with clear section headers:

                ## Executive Summary
                Brief 2-3 sentence overview

                ## Key Terms
                - Term 1: description
                - Term 2: description

                ## Obligations
                - Party A must...
                - Party B must...

                ## Notable Provisions
                - Important clause 1
                - Important clause 2
                """
        };
    }

    /// <summary>
    /// Format a section name for display.
    /// </summary>
    private static string FormatSectionName(string section)
    {
        return section.Replace("_", " ").ToUpperInvariant() switch
        {
            "EXECUTIVE SUMMARY" => "Executive Summary - Brief overview of the document",
            "KEY TERMS" => "Key Terms - Important definitions and terminology",
            "OBLIGATIONS" => "Obligations - What each party must do",
            "NOTABLE PROVISIONS" => "Notable Provisions - Unusual or important clauses",
            "FINANCIAL TERMS" => "Financial Terms - Payment amounts and schedules",
            "DATES" => "Important Dates - Key deadlines and milestones",
            "RISKS" => "Risk Factors - Potential concerns or issues",
            _ => section.Replace("_", " ")
        };
    }

    /// <summary>
    /// Parse JSON response containing summary and confidence.
    /// </summary>
    private static (string Summary, double Confidence) ParseSummaryJsonResponse(string response)
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

            var result = JsonSerializer.Deserialize<SummaryJsonResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result is not null)
            {
                // Clamp confidence to valid range
                var confidence = Math.Clamp(result.Confidence, 0.0, 1.0);
                return (result.Summary, confidence);
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, treat the entire response as the summary with default confidence
        }

        // Fallback: return the raw response as summary with default confidence
        return (response, 0.7);
    }

    /// <summary>
    /// Parse the summary text into a structured result object.
    /// </summary>
    private static SummaryResult ParseSummaryResult(string summaryText, double confidence, SummaryConfig config)
    {
        var result = new SummaryResult
        {
            FullText = summaryText,
            WordCount = CountWords(summaryText),
            Format = config.Format ?? "structured",
            Confidence = confidence
        };

        // Try to extract sections if structured format
        if (config.Format == "structured" || config.Format == null)
        {
            result.Sections = ExtractSections(summaryText);
        }

        return result;
    }

    /// <summary>
    /// Extract sections from a structured summary.
    /// </summary>
    private static Dictionary<string, string> ExtractSections(string text)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n');
        string? currentSection = null;
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("# "))
            {
                // Save previous section
                if (currentSection != null)
                {
                    sections[currentSection] = currentContent.ToString().Trim();
                }

                // Start new section
                currentSection = trimmed.TrimStart('#', ' ');
                currentContent.Clear();
            }
            else if (currentSection != null)
            {
                currentContent.AppendLine(line);
            }
        }

        // Save last section
        if (currentSection != null)
        {
            sections[currentSection] = currentContent.ToString().Trim();
        }

        return sections;
    }

    /// <summary>
    /// Check if a format string is valid.
    /// </summary>
    private static bool IsValidFormat(string format)
    {
        return format.ToLowerInvariant() is "paragraph" or "bullets" or "structured";
    }

    /// <summary>
    /// Count words in a text string.
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static SummaryConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return SummaryConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<SummaryConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? SummaryConfig.Default;
        }
        catch
        {
            return SummaryConfig.Default;
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
/// Configuration for Summary tool.
/// </summary>
internal class SummaryConfig
{
    public int MaxLength { get; set; } = 500;
    public string? Format { get; set; } = "structured";
    public string[]? IncludeSections { get; set; }
    public bool UsePlainLanguage { get; set; } = true;
    public bool HighlightTimeSensitive { get; set; } = true;

    public static SummaryConfig Default => new()
    {
        MaxLength = 500,
        Format = "structured",
        IncludeSections = new[] { "executive_summary", "key_terms", "obligations", "notable_provisions" },
        UsePlainLanguage = true,
        HighlightTimeSensitive = true
    };
}

/// <summary>
/// Result from summary generation.
/// </summary>
internal class SummaryGenerationResult
{
    public string Summary { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// JSON response structure for AI summary with confidence.
/// </summary>
internal class SummaryJsonResponse
{
    public string Summary { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

/// <summary>
/// Complete summary result structure.
/// </summary>
public class SummaryResult
{
    /// <summary>The full summary text.</summary>
    public string FullText { get; set; } = string.Empty;

    /// <summary>Word count of the summary.</summary>
    public int WordCount { get; set; }

    /// <summary>Format used (paragraph, bullets, structured).</summary>
    public string Format { get; set; } = "structured";

    /// <summary>Confidence score (0.0-1.0) for the summary accuracy and completeness.</summary>
    public double Confidence { get; set; }

    /// <summary>Extracted sections if structured format.</summary>
    public Dictionary<string, string>? Sections { get; set; }
}
