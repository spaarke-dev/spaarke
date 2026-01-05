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
    private readonly ILogger<SummaryHandler> _logger;

    public SummaryHandler(
        IOpenAiClient openAiClient,
        ILogger<SummaryHandler> logger)
    {
        _openAiClient = openAiClient;
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
        });

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

            // Chunk document if needed
            var chunks = ChunkText(documentText, DefaultChunkSize);
            _logger.LogDebug(
                "Document split into {ChunkCount} chunks for summarization",
                chunks.Count);

            string summaryText;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            if (chunks.Count == 1)
            {
                // Single chunk - generate summary directly
                var result = await GenerateSummaryAsync(chunks[0], config, cancellationToken);
                summaryText = result.Summary;
                totalInputTokens = result.InputTokens;
                totalOutputTokens = result.OutputTokens;
            }
            else
            {
                // Multiple chunks - generate chunk summaries then synthesize
                var chunkSummaries = new List<string>();

                foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.LogDebug("Summarizing chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                    var chunkResult = await GenerateChunkSummaryAsync(chunk, index + 1, chunks.Count, cancellationToken);
                    chunkSummaries.Add(chunkResult.Summary);
                    totalInputTokens += chunkResult.InputTokens;
                    totalOutputTokens += chunkResult.OutputTokens;
                }

                // Synthesize final summary from chunk summaries
                var synthesisResult = await SynthesizeSummaryAsync(chunkSummaries, config, cancellationToken);
                summaryText = synthesisResult.Summary;
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
            var resultData = ParseSummaryResult(summaryText, config);

            _logger.LogInformation(
                "Document summarization complete for {AnalysisId}: {WordCount} words in {Duration}ms",
                context.AnalysisId, CountWords(summaryText), stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summaryText,
                0.9, // High confidence for well-formed summaries
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

        return new SummaryGenerationResult
        {
            Summary = response,
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

            Provide a comprehensive summary (300-500 words) of this section:
            """;

        var inputTokens = EstimateTokens(prompt);
        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        return new SummaryGenerationResult
        {
            Summary = response,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Synthesize a final summary from multiple chunk summaries.
    /// </summary>
    private async Task<SummaryGenerationResult> SynthesizeSummaryAsync(
        List<string> chunkSummaries,
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

            Create the synthesized summary:
            """;

        var inputTokens = EstimateTokens(prompt);
        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        return new SummaryGenerationResult
        {
            Summary = response,
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

            Generate the summary:
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
    /// Parse the summary text into a structured result object.
    /// </summary>
    private static SummaryResult ParseSummaryResult(string summaryText, SummaryConfig config)
    {
        var result = new SummaryResult
        {
            FullText = summaryText,
            WordCount = CountWords(summaryText),
            Format = config.Format ?? "structured"
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
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
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

    /// <summary>Extracted sections if structured format.</summary>
    public Dictionary<string, string>? Sections { get; set; }
}
