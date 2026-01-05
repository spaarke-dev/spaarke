using System.Diagnostics;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Tool handler for extracting and calculating financial data from documents.
/// Identifies monetary values, payment terms, and provides financial summaries.
/// </summary>
/// <remarks>
/// <para>
/// FinancialCalculatorHandler extracts monetary values with currency, calculates totals,
/// identifies payment schedules, and summarizes financial obligations.
/// </para>
/// <para>
/// Configuration parameters (via AnalysisTool.Configuration):
/// - currencies: Array of currencies to detect (default: USD, EUR, GBP)
/// - includePaymentTerms: Extract payment terms (default: true)
/// - includeTotals: Calculate totals by category (default: true)
/// - maxItems: Maximum financial items to return (default: 100)
/// </para>
/// </remarks>
public sealed class FinancialCalculatorHandler : IAnalysisToolHandler
{
    private const string HandlerIdValue = "FinancialCalculatorHandler";
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;
    private const int DefaultMaxItems = 100;

    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<FinancialCalculatorHandler> _logger;

    public FinancialCalculatorHandler(
        IOpenAiClient openAiClient,
        ILogger<FinancialCalculatorHandler> logger)
    {
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Financial Calculator",
        Description: "Extracts monetary values, calculates totals, identifies payment terms, and summarizes financial obligations.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        Parameters: new[]
        {
            new ToolParameterDefinition("currencies", "Currencies to detect", ToolParameterType.Array, Required: false,
                DefaultValue: new[] { "USD", "EUR", "GBP" }),
            new ToolParameterDefinition("include_payment_terms", "Extract payment terms", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("include_totals", "Calculate totals by category", ToolParameterType.Boolean, Required: false, DefaultValue: true),
            new ToolParameterDefinition("max_items", "Maximum financial items to return", ToolParameterType.Integer, Required: false, DefaultValue: 100)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.FinancialCalculator };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (context.Document is null)
            errors.Add("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document?.ExtractedText))
            errors.Add("Document extracted text is required for financial analysis.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required.");

        // Validate configuration if provided
        if (!string.IsNullOrWhiteSpace(tool.Configuration))
        {
            try
            {
                var config = JsonSerializer.Deserialize<FinancialCalculatorConfig>(tool.Configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config?.MaxItems is < 1 or > 500)
                    errors.Add("max_items must be between 1 and 500.");
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
                "Starting financial analysis for analysis {AnalysisId}, document {DocumentId}",
                context.AnalysisId, context.Document.DocumentId);

            // Parse configuration
            var config = ParseConfiguration(tool.Configuration);
            var documentText = context.Document.ExtractedText;

            // Chunk document if needed
            var chunks = ChunkText(documentText, DefaultChunkSize);
            _logger.LogDebug(
                "Document split into {ChunkCount} chunks for financial analysis",
                chunks.Count);

            var allItems = new List<FinancialItem>();
            var allPaymentTerms = new List<PaymentTerm>();
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Analyzing financials in chunk {ChunkIndex}/{TotalChunks}", index + 1, chunks.Count);

                var result = await ExtractFinancialsFromChunkAsync(chunk, config, cancellationToken);
                allItems.AddRange(result.Items);
                allPaymentTerms.AddRange(result.PaymentTerms);
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }

            // Process and deduplicate
            var processedItems = ProcessAndDeduplicateItems(allItems, config);
            var processedTerms = ProcessPaymentTerms(allPaymentTerms);

            // Calculate totals if configured
            var totals = config.IncludeTotals
                ? CalculateTotals(processedItems)
                : new Dictionary<string, CurrencyTotal>();

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

            var resultData = new FinancialAnalysisResult
            {
                Items = processedItems,
                TotalItemsFound = processedItems.Count,
                PaymentTerms = config.IncludePaymentTerms ? processedTerms : new List<PaymentTerm>(),
                TotalsByCurrency = totals,
                TotalsByCategory = CalculateCategoryTotals(processedItems),
                CurrenciesFound = processedItems.Select(i => i.Currency).Distinct().ToArray(),
                GrandTotalUsd = CalculateGrandTotalUsd(processedItems)
            };

            var summary = GenerateSummary(resultData);

            _logger.LogInformation(
                "Financial analysis complete for {AnalysisId}: {ItemCount} items, grand total ${GrandTotal:N2} USD in {Duration}ms",
                context.AnalysisId, processedItems.Count, resultData.GrandTotalUsd, stopwatch.ElapsedMilliseconds);

            var avgConfidence = processedItems.Count > 0
                ? processedItems.Average(i => i.Confidence)
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
            _logger.LogWarning("Financial analysis cancelled for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                "Financial analysis was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Financial analysis failed for analysis {AnalysisId}", context.AnalysisId);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                $"Financial analysis failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    /// <summary>
    /// Extract financial data from a single document chunk.
    /// </summary>
    private async Task<ChunkFinancialResult> ExtractFinancialsFromChunkAsync(
        string chunk,
        FinancialCalculatorConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildExtractionPrompt(chunk, config);
        var inputTokens = EstimateTokens(prompt);

        var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
        var outputTokens = EstimateTokens(response);

        var (items, terms) = ParseFinancialsFromResponse(response);

        return new ChunkFinancialResult
        {
            Items = items,
            PaymentTerms = terms,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// Build the financial extraction prompt.
    /// </summary>
    private static string BuildExtractionPrompt(string text, FinancialCalculatorConfig config)
    {
        var currencies = config.Currencies ?? new[] { "USD", "EUR", "GBP" };
        var currencyList = string.Join(", ", currencies);

        return $$"""
            You are a financial analyst specializing in contract and document analysis. Extract all monetary values and financial terms.

            CURRENCIES TO DETECT: {{currencyList}}

            For each monetary value, provide:
            1. category: Category (Fee, Penalty, Cap, Minimum, Deposit, Payment, Expense, Other)
            2. description: What the amount is for (max 100 chars)
            3. amount: The numeric amount
            4. currency: The currency code (USD, EUR, GBP, etc.)
            5. frequency: Payment frequency if applicable (OneTime, Monthly, Quarterly, Annual, AsIncurred)
            6. isEstimate: true if the amount is estimated or approximate
            7. confidence: 0.0 to 1.0 confidence in the extraction

            {{(config.IncludePaymentTerms ? @"Also extract payment terms:
            - termType: Type (PaymentDue, LateFee, Discount, Deposit, Refund)
            - description: Details of the term
            - daysOrPeriod: Number of days or period description
            - percentage: Percentage if applicable" : "")}}

            Respond in JSON format:
            ```json
            {
              "items": [
                {
                  "category": "Fee",
                  "description": "Annual license fee",
                  "amount": 50000.00,
                  "currency": "USD",
                  "frequency": "Annual",
                  "isEstimate": false,
                  "confidence": 0.95
                }
              ],
              "paymentTerms": [
                {
                  "termType": "PaymentDue",
                  "description": "Payment due within 30 days of invoice",
                  "daysOrPeriod": "30 days",
                  "percentage": null
                }
              ]
            }
            ```

            Document text:
            {{text}}

            Extract all financial information:
            """;
    }

    /// <summary>
    /// Parse financial data from AI response.
    /// </summary>
    private (List<FinancialItem> Items, List<PaymentTerm> Terms) ParseFinancialsFromResponse(string response)
    {
        var items = new List<FinancialItem>();
        var terms = new List<PaymentTerm>();

        try
        {
            // Extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<FinancialResponseWrapper>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Items != null)
                {
                    foreach (var item in parsed.Items)
                    {
                        items.Add(new FinancialItem
                        {
                            Id = Guid.NewGuid(),
                            Category = NormalizeCategory(item.Category),
                            Description = item.Description ?? "",
                            Amount = item.Amount,
                            Currency = NormalizeCurrency(item.Currency),
                            Frequency = NormalizeFrequency(item.Frequency),
                            IsEstimate = item.IsEstimate,
                            Confidence = Math.Clamp(item.Confidence, 0.0, 1.0)
                        });
                    }
                }

                if (parsed?.PaymentTerms != null)
                {
                    foreach (var term in parsed.PaymentTerms)
                    {
                        terms.Add(new PaymentTerm
                        {
                            Id = Guid.NewGuid(),
                            TermType = term.TermType ?? "Other",
                            Description = term.Description ?? "",
                            DaysOrPeriod = term.DaysOrPeriod,
                            Percentage = term.Percentage
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse financial extraction response as JSON");
        }

        return (items, terms);
    }

    /// <summary>
    /// Normalize category string.
    /// </summary>
    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "Other";

        return category.Trim();
    }

    /// <summary>
    /// Normalize currency code.
    /// </summary>
    private static string NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return "USD";

        return currency.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Normalize frequency string.
    /// </summary>
    private static string NormalizeFrequency(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency))
            return "OneTime";

        return frequency.Trim();
    }

    /// <summary>
    /// Process and deduplicate financial items.
    /// </summary>
    private static List<FinancialItem> ProcessAndDeduplicateItems(
        List<FinancialItem> items,
        FinancialCalculatorConfig config)
    {
        // Deduplicate by similar amount and description
        var deduplicated = items
            .GroupBy(i => new
            {
                i.Amount,
                i.Currency,
                NormalizedDesc = i.Description.ToLowerInvariant().Trim().Substring(0, Math.Min(30, i.Description.Length))
            })
            .Select(g => g.OrderByDescending(i => i.Confidence).First())
            .OrderByDescending(i => i.Amount)
            .Take(config.MaxItems)
            .ToList();

        return deduplicated;
    }

    /// <summary>
    /// Process and deduplicate payment terms.
    /// </summary>
    private static List<PaymentTerm> ProcessPaymentTerms(List<PaymentTerm> terms)
    {
        return terms
            .GroupBy(t => t.Description.ToLowerInvariant().Trim())
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Calculate totals by currency.
    /// </summary>
    private static Dictionary<string, CurrencyTotal> CalculateTotals(List<FinancialItem> items)
    {
        return items
            .GroupBy(i => i.Currency)
            .ToDictionary(
                g => g.Key,
                g => new CurrencyTotal
                {
                    Currency = g.Key,
                    Total = g.Sum(i => i.Amount),
                    Count = g.Count(),
                    OneTimeTotal = g.Where(i => i.Frequency == "OneTime").Sum(i => i.Amount),
                    RecurringTotal = g.Where(i => i.Frequency != "OneTime").Sum(i => i.Amount)
                });
    }

    /// <summary>
    /// Calculate totals by category.
    /// </summary>
    private static Dictionary<string, decimal> CalculateCategoryTotals(List<FinancialItem> items)
    {
        return items
            .GroupBy(i => i.Category)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));
    }

    /// <summary>
    /// Calculate grand total in USD (with approximate conversions).
    /// </summary>
    private static decimal CalculateGrandTotalUsd(List<FinancialItem> items)
    {
        decimal total = 0;
        foreach (var item in items)
        {
            total += item.Currency switch
            {
                "USD" => item.Amount,
                "EUR" => item.Amount * 1.10m, // Approximate conversion
                "GBP" => item.Amount * 1.27m,
                "CAD" => item.Amount * 0.74m,
                "AUD" => item.Amount * 0.65m,
                _ => item.Amount // Assume USD if unknown
            };
        }
        return Math.Round(total, 2);
    }

    /// <summary>
    /// Generate summary of financial analysis.
    /// </summary>
    private static string GenerateSummary(FinancialAnalysisResult result)
    {
        if (result.TotalItemsFound == 0)
            return "No financial values found in the document.";

        var summary = $"Found {result.TotalItemsFound} financial item(s)";

        if (result.TotalsByCurrency.Count > 0)
        {
            var totals = result.TotalsByCurrency
                .Select(kvp => $"{kvp.Key} {kvp.Value.Total:N2}")
                .ToArray();
            summary += $"\nTotals: {string.Join(", ", totals)}";
        }

        if (result.GrandTotalUsd > 0)
        {
            summary += $"\nGrand total (USD equivalent): ${result.GrandTotalUsd:N2}";
        }

        if (result.PaymentTerms.Count > 0)
        {
            summary += $"\n{result.PaymentTerms.Count} payment term(s) identified.";
        }

        return summary;
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
            position += advance > 0 ? advance : length;
        }

        return chunks;
    }

    /// <summary>
    /// Parse configuration from JSON or use defaults.
    /// </summary>
    private static FinancialCalculatorConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return FinancialCalculatorConfig.Default;

        try
        {
            var config = JsonSerializer.Deserialize<FinancialCalculatorConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? FinancialCalculatorConfig.Default;
        }
        catch
        {
            return FinancialCalculatorConfig.Default;
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
/// Configuration for FinancialCalculator tool.
/// </summary>
internal class FinancialCalculatorConfig
{
    public string[]? Currencies { get; set; }
    public bool IncludePaymentTerms { get; set; } = true;
    public bool IncludeTotals { get; set; } = true;
    public int MaxItems { get; set; } = 100;

    public static FinancialCalculatorConfig Default => new()
    {
        Currencies = new[] { "USD", "EUR", "GBP" },
        IncludePaymentTerms = true,
        IncludeTotals = true,
        MaxItems = 100
    };
}

/// <summary>
/// Result from chunk financial extraction.
/// </summary>
internal class ChunkFinancialResult
{
    public List<FinancialItem> Items { get; set; } = new();
    public List<PaymentTerm> PaymentTerms { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// Wrapper for parsing AI response.
/// </summary>
internal class FinancialResponseWrapper
{
    public List<FinancialItemResponse>? Items { get; set; }
    public List<PaymentTermResponse>? PaymentTerms { get; set; }
}

/// <summary>
/// Financial item from AI response.
/// </summary>
internal class FinancialItemResponse
{
    public string? Category { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? Frequency { get; set; }
    public bool IsEstimate { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// Payment term from AI response.
/// </summary>
internal class PaymentTermResponse
{
    public string? TermType { get; set; }
    public string? Description { get; set; }
    public string? DaysOrPeriod { get; set; }
    public decimal? Percentage { get; set; }
}

/// <summary>
/// A financial item extracted from the document.
/// </summary>
public class FinancialItem
{
    /// <summary>Unique identifier for this item.</summary>
    public Guid Id { get; set; }

    /// <summary>Category (Fee, Penalty, Cap, etc.).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Description of what the amount is for.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The monetary amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency code (USD, EUR, etc.).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Payment frequency (OneTime, Monthly, Annual, etc.).</summary>
    public string Frequency { get; set; } = "OneTime";

    /// <summary>Whether the amount is estimated.</summary>
    public bool IsEstimate { get; set; }

    /// <summary>Confidence score (0.0 to 1.0).</summary>
    public double Confidence { get; set; }
}

/// <summary>
/// A payment term extracted from the document.
/// </summary>
public class PaymentTerm
{
    /// <summary>Unique identifier for this term.</summary>
    public Guid Id { get; set; }

    /// <summary>Type of payment term.</summary>
    public string TermType { get; set; } = string.Empty;

    /// <summary>Description of the term.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Days or period description.</summary>
    public string? DaysOrPeriod { get; set; }

    /// <summary>Percentage if applicable.</summary>
    public decimal? Percentage { get; set; }
}

/// <summary>
/// Totals for a specific currency.
/// </summary>
public class CurrencyTotal
{
    /// <summary>The currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Total amount in this currency.</summary>
    public decimal Total { get; set; }

    /// <summary>Number of items in this currency.</summary>
    public int Count { get; set; }

    /// <summary>Total of one-time amounts.</summary>
    public decimal OneTimeTotal { get; set; }

    /// <summary>Total of recurring amounts.</summary>
    public decimal RecurringTotal { get; set; }
}

/// <summary>
/// Complete financial analysis result.
/// </summary>
public class FinancialAnalysisResult
{
    /// <summary>List of extracted financial items.</summary>
    public List<FinancialItem> Items { get; set; } = new();

    /// <summary>Total number of items found.</summary>
    public int TotalItemsFound { get; set; }

    /// <summary>List of payment terms.</summary>
    public List<PaymentTerm> PaymentTerms { get; set; } = new();

    /// <summary>Totals grouped by currency.</summary>
    public Dictionary<string, CurrencyTotal> TotalsByCurrency { get; set; } = new();

    /// <summary>Totals grouped by category.</summary>
    public Dictionary<string, decimal> TotalsByCategory { get; set; } = new();

    /// <summary>Currencies found in the document.</summary>
    public string[] CurrenciesFound { get; set; } = Array.Empty<string>();

    /// <summary>Grand total converted to USD.</summary>
    public decimal GrandTotalUsd { get; set; }
}
