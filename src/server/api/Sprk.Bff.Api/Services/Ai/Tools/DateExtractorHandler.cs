using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for extracting and normalizing dates from documents.
/// Identifies date context (effective date, expiry, signature, etc.) and normalizes to ISO 8601.
/// </summary>
/// <remarks>
/// <para>
/// DateExtractorHandler uses AI to find dates in documents, categorize them by context,
/// and normalize them to ISO 8601 format for consistent processing.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - dateTypes: Array of date types to extract (default: all types)
/// - includeRelativeDates: Include relative date expressions like "30 days after" (default: true)
/// - maxDates: Maximum dates to return (default: 50)
/// - includeContext: Include surrounding text context (default: true)
/// </para>
/// </remarks>
public sealed class DateExtractorHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "DateExtractorHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const int DefaultMaxDates = 50;

    private readonly IOpenAiClient _openAiClient;
    private readonly ITextChunkingService _textChunkingService;
    private readonly ILogger<DateExtractorHandler> _logger;

    public DateExtractorHandler(
        IOpenAiClient openAiClient,
        ITextChunkingService textChunkingService,
        ILogger<DateExtractorHandler> logger)
    {
        _openAiClient = openAiClient;
        _textChunkingService = textChunkingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Date Extractor",
        Description: "Extracts and normalizes dates from documents, identifying their context (effective date, expiry, deadlines, etc.).",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("date_types", "Date types to extract", ToolParameterType.Array, Required: false,
                DefaultValue: DateTypes.StandardTypes),
            new ToolParameterDefinition("include_relative_dates", "Include relative date expressions", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("max_dates", "Maximum dates to return", ToolParameterType.Integer, Required: false, DefaultValue: 50),
            new ToolParameterDefinition("include_context", "Include surrounding text context", ToolParameterType.Boolean, Required: false, DefaultValue: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.DateExtractor };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for date extraction.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<DateExtractorConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MaxDates is < 1 or > 200)
                    errors.Add("max_dates must be between 1 and 200.");
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
                "Starting date extraction for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

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
                "Document split into {ChunkCount} chunks for date extraction",
                chunks.Count);

            var allDates = new List<ExtractedDate>();
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Extracting dates from chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                var result = await ExtractDatesFromChunkAsync(chunk, config, cancellationToken);
                allDates.AddRange(result.Dates);
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }

            // Deduplicate and limit dates
            var processedDates = ProcessAndDeduplicateDates(allDates, config);

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

            var resultData = new DateExtractionResult
            {
                Dates = processedDates,
                TotalDatesFound = processedDates.Count,
                DatesByType = processedDates
                    .GroupBy(d => d.DateType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EarliestDate = processedDates.Where(d => d.NormalizedDate.HasValue).MinBy(d => d.NormalizedDate)?.NormalizedDate,
                LatestDate = processedDates.Where(d => d.NormalizedDate.HasValue).MaxBy(d => d.NormalizedDate)?.NormalizedDate,
                HasRelativeDates = processedDates.Any(d => d.IsRelative)
            };

            var summary = GenerateSummary(resultData);

            _logger.LogInformation(
                "Date extraction complete for {AnalysisId}: {DateCount} dates found in {Duration}ms",
                context.AnalysisId, processedDates.Count, stopwatch.ElapsedMilliseconds);

            var avgConfidence = processedDates.Count > 0
                ? processedDates.Average(d => d.Confidence)
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
            _logger.LogWarning("Date extraction cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Date extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Date extraction failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Date extraction failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Extract dates from a single document chunk.
    /// </summary>
    private async Task<ChunkDateResult> ExtractDatesFromChunkAsync(
        string chunk,
        DateExtractorConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildExtractionPrompt(chunk, config);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var dates = ParseDatesFromResponse(response);

        return new ChunkDateResult
        {
            Dates = dates,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the date extraction prompt.
    /// </summary>
    private static string BuildExtractionPrompt(string text, DateExtractorConfig config)
    {
        var dateTypes = config.DateTypes ?? DateTypes.StandardTypes;
        var typesList = string.Join(", ", dateTypes);

        return $$"""
            You are a document analyst specializing in date extraction. Find and categorize all dates in the document.

            DATE TYPES TO IDENTIFY: {{typesList}}

            For each date found, provide:
            1. dateType: The type/context of the date (from the list above, or "Other")
            2. originalText: The exact text as it appears in the document
            3. normalizedDate: ISO 8601 format (YYYY-MM-DD) if a specific date can be determined, null otherwise
            4. isRelative: true if this is a relative date expression (e.g., "30 days after signing")
            5. relativeBase: If relative, what event/date it's relative to
            6. confidence: 0.0 to 1.0 confidence in the extraction
            {{(config.IncludeContext ? "7. context: The surrounding sentence or phrase (max 150 chars)" : "")}}

            Respond in JSON format:
            ```json
            {
              "dates": [
                {
                  "dateType": "EffectiveDate",
                  "originalText": "January 1, 2025",
                  "normalizedDate": "2025-01-01",
                  "isRelative": false,
                  "relativeBase": null,
                  "confidence": 0.95,
                  "context": "This Agreement shall be effective as of January 1, 2025"
                },
                {
                  "dateType": "Deadline",
                  "originalText": "30 days after the Effective Date",
                  "normalizedDate": null,
                  "isRelative": true,
                  "relativeBase": "Effective Date",
                  "confidence": 0.90,
                  "context": "Payment due within 30 days after the Effective Date"
                }
              ]
            }
            ```

            Document text:
            {{text}}

            Extract all dates:
            """;
    }

    /// <summary>
    /// Parse dates from AI response.
    /// </summary>
    private List<ExtractedDate> ParseDatesFromResponse(string response)
    {
        var dates = new List<ExtractedDate>();

        try
        {
            // Extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<DateResponseWrapper>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Dates != null)
                {
                    foreach (var date in parsed.Dates)
                    {
                        var normalizedDate = TryParseDate(date.NormalizedDate);
                        dates.Add(new ExtractedDate
                        {
                            Id = Guid.NewGuid(),
                            DateType = NormalizeDateType(date.DateType),
                            OriginalText = date.OriginalText ?? "",
                            NormalizedDate = normalizedDate,
                            NormalizedDateString = normalizedDate?.ToString("yyyy-MM-dd"),
                            IsRelative = date.IsRelative,
                            RelativeBase = date.RelativeBase,
                            Confidence = Math.Clamp(date.Confidence, 0.0, 1.0),
                            Context = date.Context
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse date extraction response as JSON");
        }

        return dates;
    }

    /// <summary>
    /// Try to parse a date string.
    /// </summary>
    private static DateTime? TryParseDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;

        if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        return null;
    }

    /// <summary>
    /// Normalize date type to standard values.
    /// </summary>
    private static string NormalizeDateType(string? dateType)
    {
        if (string.IsNullOrWhiteSpace(dateType))
            return "Other";

        return dateType.Trim();
    }

    /// <summary>
    /// Process and deduplicate dates.
    /// </summary>
    private static List<ExtractedDate> ProcessAndDeduplicateDates(
        List<ExtractedDate> dates,
        DateExtractorConfig config)
    {
        // Filter relative dates if not included
        var filtered = config.IncludeRelativeDates
            ? dates
            : dates.Where(d => !d.IsRelative);

        // Deduplicate by original text (keep highest confidence)
        var deduplicated = filtered
            .GroupBy(d => d.OriginalText.ToLowerInvariant().Trim())
            .Select(g => g.OrderByDescending(d => d.Confidence).First())
            .OrderBy(d => d.NormalizedDate ?? DateTime.MaxValue)
            .ThenByDescending(d => d.Confidence)
            .Take(config.MaxDates)
            .ToList();

        return deduplicated;
    }

    /// <summary>
    /// Generate summary of extraction results.
    /// </summary>
    private static string GenerateSummary(DateExtractionResult result)
    {
        if (result.TotalDatesFound == 0)
            return "No dates found in the document.";

        var summary = $"Found {result.TotalDatesFound} date(s)";

        if (result.DatesByType.Count > 0)
        {
            var topTypes = result.DatesByType
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => $"{kvp.Key}: {kvp.Value}");
            summary += $" ({string.Join(", ", topTypes)})";
        }

        if (result.EarliestDate.HasValue && result.LatestDate.HasValue)
        {
            summary += $"\nDate range: {result.EarliestDate:yyyy-MM-dd} to {result.LatestDate:yyyy-MM-dd}";
        }

        if (result.HasRelativeDates)
        {
            summary += "\nIncludes relative date expressions.";
        }

        return summary;
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static DateExtractorConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return DateExtractorConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<DateExtractorConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? DateExtractorConfig.Default;
        }
        catch
        {
            return DateExtractorConfig.Default;
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
/// Standard date types for extraction.
/// </summary>
public static class DateTypes
{
    public const string EffectiveDate = "EffectiveDate";
    public const string ExpirationDate = "ExpirationDate";
    public const string SignatureDate = "SignatureDate";
    public const string Deadline = "Deadline";
    public const string RenewalDate = "RenewalDate";
    public const string TerminationDate = "TerminationDate";
    public const string PaymentDue = "PaymentDue";
    public const string NoticeDate = "NoticeDate";
    public const string Amendment = "Amendment";
    public const string Other = "Other";

    public static string[] StandardTypes => new[]
    {
        EffectiveDate,
        ExpirationDate,
        SignatureDate,
        Deadline,
        RenewalDate,
        TerminationDate,
        PaymentDue,
        NoticeDate
    };
}

/// <summary>
/// Configuration for DateExtractor tool.
/// </summary>
internal class DateExtractorConfig
{
    public string[]? DateTypes { get; set; }
    public bool IncludeRelativeDates { get; set; } = true;
    public int MaxDates { get; set; } = 50;
    public bool IncludeContext { get; set; } = true;

    public static DateExtractorConfig Default => new()
    {
        DateTypes = Tools.DateTypes.StandardTypes,
        IncludeRelativeDates = true,
        MaxDates = 50,
        IncludeContext = true
    };
}

/// <summary>
/// Result from chunk date extraction.
/// </summary>
internal class ChunkDateResult
{
    public List<ExtractedDate> Dates { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Wrapper for parsing AI response.
/// </summary>
internal class DateResponseWrapper
{
    public List<DateResponseItem>? Dates { get; set; }
}

/// <summary>
/// Date item from AI response.
/// </summary>
internal class DateResponseItem
{
    public string? DateType { get; set; }
    public string? OriginalText { get; set; }
    public string? NormalizedDate { get; set; }
    public bool IsRelative { get; set; }
    public string? RelativeBase { get; set; }
    public double Confidence { get; set; }
    public string? Context { get; set; }
}

/// <summary>
/// An extracted date from the document.
/// </summary>
public class ExtractedDate
{
    /// <summary>Unique identifier for this date.</summary>
    public Guid Id { get; set; }

    /// <summary>The type/context of the date.</summary>
    public string DateType { get; set; } = string.Empty;

    /// <summary>The original text as it appears in the document.</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>The normalized date (if specific date could be determined).</summary>
    public DateTime? NormalizedDate { get; set; }

    /// <summary>ISO 8601 formatted date string.</summary>
    public string? NormalizedDateString { get; set; }

    /// <summary>Whether this is a relative date expression.</summary>
    public bool IsRelative { get; set; }

    /// <summary>If relative, what event/date it's relative to.</summary>
    public string? RelativeBase { get; set; }

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Surrounding context text.</summary>
    public string? Context { get; set; }
}

/// <summary>
/// Complete date extraction result.
/// </summary>
public class DateExtractionResult
{
    /// <summary>List of extracted dates.</summary>
    public List<ExtractedDate> Dates { get; set; } = new();

    /// <summary>Total number of dates found.</summary>
    public int TotalDatesFound { get; set; }

    /// <summary>Count of dates by type.</summary>
    public Dictionary<string, int> DatesByType { get; set; } = new();

    /// <summary>Earliest date found.</summary>
    public DateTime? EarliestDate { get; set; }

    /// <summary>Latest date found.</summary>
    public DateTime? LatestDate { get; set; }

    /// <summary>Whether any relative dates were found.</summary>
    public bool HasRelativeDates { get; set; }
}
