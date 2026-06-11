using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Pure-deterministic date extraction + normalization handler (R6 Pillar 2, FR-17).
/// </summary>
/// <remarks>
/// <para>
/// Scans free-text input for date references (ISO 8601, US/EU short formats, named months,
/// quarter notation, and relative phrases like "next quarter", "tomorrow", "in 30 days")
/// and emits a structured list of normalized ISO 8601 dates with confidence scores.
/// </para>
/// <para>
/// <strong>Pure-deterministic constraint (FR-17)</strong>: NO LLM call, NO Azure OpenAI
/// dependency. Implementation uses <see cref="DateTime"/> arithmetic + regex only. Identical
/// input + identical config → identical output across runs. Sub-millisecond latency target.
/// </para>
/// <para>
/// <strong>Invocation contexts</strong>: Supports <see cref="InvocationContextKind.Both"/>
/// (Playbook + Chat). The chat-side adapter (task D-A-10) passes the input text via the
/// <c>text</c> parameter on <see cref="ChatInvocationContext.ToolArgumentsJson"/>; the
/// playbook-side reads it from <see cref="ToolExecutionContext.Document"/>'s extracted text.
/// </para>
/// <para>
/// <strong>Configuration (sprk_analysistool.sprk_configuration JSON)</strong>:
/// </para>
/// <list type="bullet">
/// <item><c>referenceDate</c> (string, ISO 8601, optional): anchor for relative-date resolution.
/// Defaults to <see cref="DateTimeOffset.UtcNow"/> when null.</item>
/// <item><c>locale</c> (string, optional): "US" (MM/DD/YYYY) or "EU" (DD/MM/YYYY) — disambiguates
/// "01/02/2026". Defaults to "US".</item>
/// <item><c>confidenceThreshold</c> (double, optional, 0.0–1.0): filters out low-confidence
/// matches. Defaults to 0.0 (return all).</item>
/// </list>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>.</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; CRUD-side code routes
/// through the <c>PublicContracts</c> facade, never directly into this handler.</item>
/// <item><strong>ADR-015</strong>: telemetry emits handler name + outcome + IDs + timestamp ONLY.
/// Never raw input text or extracted token spans.</item>
/// <item><strong>ADR-029</strong>: implementation uses BCL types only (no new NuGet dep);
/// per-handler publish-size delta ≤+0.5 MB target.</item>
/// </list>
/// </remarks>
public sealed class DateExtractorHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DateExtractorHandler);

    private readonly ILogger<DateExtractorHandler> _logger;

    public DateExtractorHandler(ILogger<DateExtractorHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Date Extractor",
        Description: "Pure-deterministic date normalization + relative-date resolution. " +
                     "Parses ISO 8601, US/EU short formats, named months, quarter notation, and " +
                     "relative phrases (\"next quarter\", \"tomorrow\", \"in 30 days\") into " +
                     "canonical ISO 8601 dates. No LLM call.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition("referenceDate", "Anchor date for relative-date resolution (ISO 8601). Defaults to current UTC time.", ToolParameterType.String, Required: false),
            new ToolParameterDefinition("locale", "Date-format locale for ambiguous short formats: 'US' (MM/DD/YYYY) or 'EU' (DD/MM/YYYY). Defaults to 'US'.", ToolParameterType.String, Required: false, DefaultValue: "US"),
            new ToolParameterDefinition("confidenceThreshold", "Filter threshold for confidence (0.0-1.0). Defaults to 0.0 (return all).", ToolParameterType.Decimal, Required: false, DefaultValue: 0.0)
        },
        ConfigurationSchema: ConfigSchema);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.DateExtractor };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

    /// <summary>
    /// JSON Schema (Draft 07) describing the handler's configuration JSON (stored in
    /// <c>sprk_analysistool.sprk_configuration</c>).
    /// </summary>
    private static readonly object ConfigSchema = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Date Extractor Handler Configuration",
        type = "object",
        properties = new
        {
            referenceDate = new
            {
                type = "string",
                format = "date-time",
                description = "Anchor date (ISO 8601) for relative-date resolution. When null, uses current UTC."
            },
            locale = new
            {
                type = "string",
                @enum = new[] { "US", "EU" },
                @default = "US",
                description = "Locale for ambiguous short formats (e.g., '01/02/2026'). 'US' = MM/DD/YYYY, 'EU' = DD/MM/YYYY."
            },
            confidenceThreshold = new
            {
                type = "number",
                minimum = 0.0,
                maximum = 1.0,
                @default = 0.0,
                description = "Minimum confidence to include in results."
            }
        },
        required = Array.Empty<string>()
    };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (context.Document is null)
            return ToolValidationResult.Failure("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
            return ToolValidationResult.Failure("Document extracted text is required.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        // Validate the chat argument shape contains a 'text' field.
        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            if (!doc.RootElement.TryGetProperty("text", out var textProp) ||
                textProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(textProp.GetString()))
            {
                return ToolValidationResult.Failure("Tool arguments must include a non-empty 'text' string field.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ValidateConfiguration(tool.Configuration);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // ADR-015: log handler name + IDs + outcome only — never the input text body
            _logger.LogInformation(
                "DateExtractorHandler executing for analysis {AnalysisId}, tool {ToolId}",
                context.AnalysisId, tool.Id);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);
            var inputText = context.Document!.ExtractedText ?? string.Empty;

            var extracted = ExtractDates(inputText, config);

            stopwatch.Stop();

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0, // pure deterministic — no LLM call
                ModelName = null
            };

            // ADR-015: telemetry emits count only — not the extracted values themselves
            _logger.LogInformation(
                "DateExtractorHandler complete for analysis {AnalysisId}: extracted {Count} dates in {Duration}ms",
                context.AnalysisId, extracted.Count, stopwatch.ElapsedMilliseconds);

            return Task.FromResult(ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: new DateExtractionResult { Dates = extracted },
                summary: $"Extracted {extracted.Count} date reference(s).",
                confidence: extracted.Count == 0 ? 0.0 : extracted.Average(d => d.Confidence),
                execution: executionMetadata));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "DateExtractorHandler cancelled for analysis {AnalysisId}", context.AnalysisId);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Date extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                }));
        }
        catch (Exception ex)
        {
            // ADR-015: log only the exception message classification (no input echo)
            _logger.LogError(
                ex,
                "DateExtractorHandler failed for analysis {AnalysisId}: {ErrorType}",
                context.AnalysisId, ex.GetType().Name);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Date extraction failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                }));
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            // ADR-015: log handler name + IDs + outcome only — never the input text body
            _logger.LogInformation(
                "DateExtractorHandler chat-invocation for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);

            cancellationToken.ThrowIfCancellationRequested();

            var config = ParseConfiguration(tool.Configuration);

            // The chat adapter has already validated ToolArgumentsJson against the tool's
            // JsonSchema (per task D-A-10). We safely extract 'text'.
            string inputText;
            using (var doc = JsonDocument.Parse(context.ToolArgumentsJson ?? "{}"))
            {
                inputText = doc.RootElement.TryGetProperty("text", out var textProp)
                    ? textProp.GetString() ?? string.Empty
                    : string.Empty;
            }

            var extracted = ExtractDates(inputText, config);

            stopwatch.Stop();

            var executionMetadata = new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ModelCalls = 0,
                ModelName = null
            };

            _logger.LogInformation(
                "DateExtractorHandler chat complete for session {ChatSessionId}, decision {DecisionId}: extracted {Count} dates in {Duration}ms",
                context.ChatSessionId, context.DecisionId, extracted.Count, stopwatch.ElapsedMilliseconds);

            return Task.FromResult(ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: new DateExtractionResult { Dates = extracted },
                summary: $"Extracted {extracted.Count} date reference(s).",
                confidence: extracted.Count == 0 ? 0.0 : extracted.Average(d => d.Confidence),
                execution: executionMetadata));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "DateExtractorHandler chat cancelled for session {ChatSessionId}, decision {DecisionId}",
                context.ChatSessionId, context.DecisionId);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Date extraction was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DateExtractorHandler chat failed for session {ChatSessionId}, decision {DecisionId}: {ErrorType}",
                context.ChatSessionId, context.DecisionId, ex.GetType().Name);
            return Task.FromResult(ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Date extraction failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Configuration parsing + validation
    // ─────────────────────────────────────────────────────────────────────────────

    private static ToolValidationResult ValidateConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return ToolValidationResult.Success(); // configuration is optional

        try
        {
            var config = JsonSerializer.Deserialize<DateExtractorConfig>(configurationJson, JsonOpts);
            if (config is null)
                return ToolValidationResult.Failure("Invalid configuration JSON.");

            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(config.ReferenceDate)
                && !TryParseIso8601(config.ReferenceDate, out _))
            {
                errors.Add($"referenceDate '{config.ReferenceDate}' is not a valid ISO 8601 date.");
            }

            if (!string.IsNullOrWhiteSpace(config.Locale)
                && !string.Equals(config.Locale, "US", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(config.Locale, "EU", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"locale '{config.Locale}' is not supported. Use 'US' or 'EU'.");
            }

            if (config.ConfidenceThreshold is < 0.0 or > 1.0)
                errors.Add($"confidenceThreshold {config.ConfidenceThreshold} must be between 0.0 and 1.0.");

            return errors.Count == 0
                ? ToolValidationResult.Success()
                : ToolValidationResult.Failure(errors);
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Configuration JSON parse error: {ex.Message}");
        }
    }

    private static DateExtractorConfig ParseConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            return new DateExtractorConfig();

        try
        {
            return JsonSerializer.Deserialize<DateExtractorConfig>(configurationJson, JsonOpts)
                   ?? new DateExtractorConfig();
        }
        catch (JsonException)
        {
            return new DateExtractorConfig();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Date extraction core
    // ─────────────────────────────────────────────────────────────────────────────

    private static List<ExtractedDate> ExtractDates(string text, DateExtractorConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<ExtractedDate>();

        var referenceDate = ResolveReferenceDate(config);
        var locale = config.Locale ?? "US";
        var threshold = config.ConfidenceThreshold ?? 0.0;

        var results = new List<ExtractedDate>();

        // ORDER MATTERS — match most specific patterns first to avoid double-extraction.
        ExtractIso8601(text, results);
        ExtractQuarterNotation(text, results);
        ExtractNamedMonth(text, results, referenceDate);
        ExtractShortNumericDate(text, results, locale);
        ExtractRelativePhrases(text, results, referenceDate);

        // Filter by confidence threshold
        if (threshold > 0.0)
            results = results.Where(r => r.Confidence >= threshold).ToList();

        // Deterministic ordering — by span start index so identical input → identical output order
        return results.OrderBy(r => r.StartIndex).ThenBy(r => r.OriginalText, StringComparer.Ordinal).ToList();
    }

    private static DateTimeOffset ResolveReferenceDate(DateExtractorConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ReferenceDate)
            && TryParseIso8601(config.ReferenceDate, out var parsed))
        {
            return parsed;
        }
        return DateTimeOffset.UtcNow;
    }

    // ──────────────────────── ISO 8601 (YYYY-MM-DD) ────────────────────────

    private static readonly Regex Iso8601Regex = new(
        @"\b(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    private static void ExtractIso8601(string text, List<ExtractedDate> results)
    {
        foreach (Match m in Iso8601Regex.Matches(text))
        {
            if (DateTimeOffset.TryParseExact(
                    m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                results.Add(new ExtractedDate
                {
                    OriginalText = m.Value,
                    NormalizedDate = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Confidence = 1.0,
                    Kind = "absolute",
                    StartIndex = m.Index
                });
            }
        }
    }

    // ──────────────────────── Quarter notation (Q1 2026, Q4 2025) ────────────────────────

    private static readonly Regex QuarterRegex = new(
        @"\bQ([1-4])\s+(\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ExtractQuarterNotation(string text, List<ExtractedDate> results)
    {
        foreach (Match m in QuarterRegex.Matches(text))
        {
            var quarter = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var month = ((quarter - 1) * 3) + 1; // Q1→1, Q2→4, Q3→7, Q4→10
            var normalized = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = 0.95,
                Kind = "range",
                StartIndex = m.Index
            });
        }
    }

    // ──────────────────────── Named month (March 15, March 15 2026) ────────────────────────

    private static readonly Regex NamedMonthRegex = new(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{1,2})(?:,\s*|\s+)?(\d{4})?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, int> MonthLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        { "January", 1 }, { "February", 2 }, { "March", 3 }, { "April", 4 },
        { "May", 5 }, { "June", 6 }, { "July", 7 }, { "August", 8 },
        { "September", 9 }, { "October", 10 }, { "November", 11 }, { "December", 12 }
    };

    private static void ExtractNamedMonth(
        string text, List<ExtractedDate> results, DateTimeOffset referenceDate)
    {
        foreach (Match m in NamedMonthRegex.Matches(text))
        {
            var monthName = m.Groups[1].Value;
            if (!MonthLookup.TryGetValue(monthName, out var month))
                continue;

            if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                continue;

            int year;
            double confidence;
            if (m.Groups[3].Success
                && int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitYear))
            {
                year = explicitYear;
                confidence = 0.95;
            }
            else
            {
                year = referenceDate.Year;
                confidence = 0.75; // year inferred — lower confidence
            }

            if (!IsValidDate(year, month, day))
                continue;

            var normalized = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = confidence,
                Kind = "absolute",
                StartIndex = m.Index
            });
        }
    }

    // ──────────────────────── Short numeric (01/02/2026 — locale-disambiguated) ────────────────────────

    private static readonly Regex ShortNumericRegex = new(
        @"\b(\d{1,2})/(\d{1,2})/(\d{4})\b",
        RegexOptions.Compiled);

    private static void ExtractShortNumericDate(string text, List<ExtractedDate> results, string locale)
    {
        var isUs = string.Equals(locale, "US", StringComparison.OrdinalIgnoreCase);

        foreach (Match m in ShortNumericRegex.Matches(text))
        {
            var first = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var second = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

            int month, day;
            if (isUs)
            {
                month = first;
                day = second;
            }
            else
            {
                day = first;
                month = second;
            }

            if (!IsValidDate(year, month, day))
                continue;

            var normalized = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

            // Ambiguous when both first and second are ≤12 (could be either order)
            var ambiguous = first <= 12 && second <= 12 && first != second;
            var confidence = ambiguous ? 0.7 : 0.9;

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = confidence,
                Kind = "absolute",
                StartIndex = m.Index
            });
        }
    }

    // ──────────────────────── Relative phrases ────────────────────────

    // "next quarter", "this quarter", "last quarter"
    private static readonly Regex RelativeQuarterRegex = new(
        @"\b(next|this|last)\s+quarter\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "tomorrow", "yesterday", "today"
    private static readonly Regex RelativeDayRegex = new(
        @"\b(today|tomorrow|yesterday)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "in 30 days", "in 3 weeks", "in 6 months", "in 2 years"
    private static readonly Regex RelativeInDurationRegex = new(
        @"\bin\s+(\d{1,3})\s+(day|week|month|year)s?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "next month", "next week", "next year"
    private static readonly Regex RelativeNextRegex = new(
        @"\b(next|last)\s+(week|month|year)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ExtractRelativePhrases(
        string text, List<ExtractedDate> results, DateTimeOffset referenceDate)
    {
        foreach (Match m in RelativeQuarterRegex.Matches(text))
        {
            var modifier = m.Groups[1].Value.ToLowerInvariant();
            var refDate = referenceDate.UtcDateTime;
            var currentQuarterStartMonth = ((GetQuarter(refDate.Month) - 1) * 3) + 1;
            var currentQuarterStart = new DateTime(refDate.Year, currentQuarterStartMonth, 1, 0, 0, 0, DateTimeKind.Utc);

            DateTime normalized = modifier switch
            {
                "next" => currentQuarterStart.AddMonths(3),
                "last" => currentQuarterStart.AddMonths(-3),
                _ => currentQuarterStart // "this"
            };

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = 0.85,
                Kind = "relative",
                StartIndex = m.Index
            });
        }

        foreach (Match m in RelativeDayRegex.Matches(text))
        {
            var phrase = m.Value.ToLowerInvariant();
            var refDate = referenceDate.UtcDateTime.Date;
            DateTime normalized = phrase switch
            {
                "tomorrow" => refDate.AddDays(1),
                "yesterday" => refDate.AddDays(-1),
                _ => refDate // "today"
            };

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = 0.95,
                Kind = "relative",
                StartIndex = m.Index
            });
        }

        foreach (Match m in RelativeInDurationRegex.Matches(text))
        {
            var amount = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToLowerInvariant();
            var refDate = referenceDate.UtcDateTime.Date;

            DateTime normalized = unit switch
            {
                "day" => refDate.AddDays(amount),
                "week" => refDate.AddDays(amount * 7),
                "month" => refDate.AddMonths(amount),
                "year" => refDate.AddYears(amount),
                _ => refDate
            };

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = 0.9,
                Kind = "relative",
                StartIndex = m.Index
            });
        }

        foreach (Match m in RelativeNextRegex.Matches(text))
        {
            var modifier = m.Groups[1].Value.ToLowerInvariant();
            var unit = m.Groups[2].Value.ToLowerInvariant();
            var refDate = referenceDate.UtcDateTime.Date;
            var sign = modifier == "next" ? 1 : -1;

            DateTime normalized = unit switch
            {
                "week" => refDate.AddDays(7 * sign),
                "month" => refDate.AddMonths(1 * sign),
                "year" => refDate.AddYears(1 * sign),
                _ => refDate
            };

            results.Add(new ExtractedDate
            {
                OriginalText = m.Value,
                NormalizedDate = normalized.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Confidence = 0.85,
                Kind = "relative",
                StartIndex = m.Index
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static int GetQuarter(int month) => ((month - 1) / 3) + 1;

    private static bool IsValidDate(int year, int month, int day)
    {
        if (year < 1 || year > 9999) return false;
        if (month < 1 || month > 12) return false;
        if (day < 1) return false;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return day <= daysInMonth;
    }

    private static bool TryParseIso8601(string value, out DateTimeOffset parsed)
    {
        // Accept ISO 8601 with or without time component.
        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configuration JSON shape stored in <c>sprk_analysistool.sprk_configuration</c>.
    /// </summary>
    internal sealed class DateExtractorConfig
    {
        /// <summary>Anchor date for relative-date resolution (ISO 8601).</summary>
        [JsonPropertyName("referenceDate")]
        public string? ReferenceDate { get; set; }

        /// <summary>"US" (MM/DD/YYYY) or "EU" (DD/MM/YYYY) for ambiguous short formats.</summary>
        [JsonPropertyName("locale")]
        public string? Locale { get; set; }

        /// <summary>Minimum confidence threshold (0.0-1.0) for results.</summary>
        [JsonPropertyName("confidenceThreshold")]
        public double? ConfidenceThreshold { get; set; }
    }

    /// <summary>
    /// Structured output of date extraction. Returned in <see cref="ToolResult.Data"/>.
    /// </summary>
    public sealed class DateExtractionResult
    {
        /// <summary>Extracted dates, ordered by appearance in the input text.</summary>
        [JsonPropertyName("dates")]
        public IReadOnlyList<ExtractedDate> Dates { get; set; } = Array.Empty<ExtractedDate>();
    }

    /// <summary>
    /// Single extracted date reference.
    /// </summary>
    public sealed class ExtractedDate
    {
        /// <summary>The original text span as it appeared in the input.</summary>
        [JsonPropertyName("originalText")]
        public string OriginalText { get; set; } = string.Empty;

        /// <summary>Normalized ISO 8601 date (YYYY-MM-DD).</summary>
        [JsonPropertyName("normalizedDate")]
        public string NormalizedDate { get; set; } = string.Empty;

        /// <summary>Extraction confidence (0.0-1.0).</summary>
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        /// <summary>Date kind: "absolute", "relative", or "range".</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "absolute";

        /// <summary>Character offset where the date appeared in the input text.</summary>
        [JsonPropertyName("startIndex")]
        public int StartIndex { get; set; }
    }
}
