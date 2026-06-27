using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// R6 Pillar 2 Wave-1 typed handler — pure deterministic financial math on already-extracted figures (FR-18).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pure deterministic</strong>: NO LLM call, NO Azure OpenAI dependency. The handler runs
/// arithmetic + aggregation over structured numeric input (typically line items extracted upstream
/// by <c>EntityExtractorHandler</c> or <c>InvoiceExtractionToolHandler</c>) and emits exact decimal
/// results. Identical input + identical configuration → identical output, byte-for-byte.
/// </para>
/// <para>
/// <strong>Decimal-only money math (binding)</strong>: every monetary computation uses
/// <see cref="decimal"/> — never <see cref="double"/> or <see cref="float"/>. This avoids the
/// classical floating-point rounding errors that make <c>0.1 + 0.2 != 0.3</c> when computed with
/// IEEE-754. The unit tests assert this property explicitly.
/// </para>
/// <para>
/// <strong>Supported operations</strong> (config-selected via the tool's <c>Configuration.operation</c> field):
/// </para>
/// <list type="bullet">
/// <item><c>Sum</c> — sum a list of equal-currency line-item amounts.</item>
/// <item><c>Tax</c> — apply a tax rate to a subtotal (e.g. 8.25%).</item>
/// <item><c>Discount</c> — subtract a discount rate from a subtotal.</item>
/// <item><c>FxConvert</c> — convert an amount from one currency to another via the configured fx-rate table.</item>
/// <item><c>WeightedAvg</c> — compute weighted average of (amount, weight) pairs.</item>
/// <item><c>AggregateByCategory</c> — sum amounts grouped by a category string.</item>
/// </list>
/// <para>
/// <strong>Currency policy</strong>: mixed-currency operations on Sum / WeightedAvg /
/// AggregateByCategory are rejected outright (no implicit conversion). Use <c>FxConvert</c>
/// explicitly with the configured rate table when conversion is needed. FX rates are loaded
/// from the tool's <see cref="HandlerConfig.FxRateTable"/> at runtime — there is NO live FX
/// lookup or external network call.
/// </para>
/// <para>
/// <strong>Configurable behavior</strong> (from <c>sprk_analysistool.sprk_configuration</c> JSON):
/// </para>
/// <list type="bullet">
/// <item><c>supportedCurrencies</c> — ISO-4217 codes the handler will accept (default USD/EUR/GBP/JPY/CAD).</item>
/// <item><c>fxRateTable</c> — pair-keyed table like <c>{ "USD/EUR": 0.92, "EUR/USD": 1.087 }</c>.</item>
/// <item><c>defaultRoundingMode</c> — <c>BankersRounding</c> (default) or <c>MidpointAwayFromZero</c>.</item>
/// <item><c>decimalPrecision</c> — number of fractional digits to round results to (default 2; JPY default 0).</item>
/// </list>
/// <para>
/// <strong>Telemetry</strong> (ADR-015): the handler logs handler name + operation + outcome +
/// timestamp + duration ONLY. NEVER monetary values, currency-pair specifics from input, or any
/// portion of the request body. Money values are user input and MUST NOT appear in logs.
/// </para>
/// <para>
/// <strong>Distinguishing from <c>FinancialCalculationToolHandler</c> (task 104)</strong>:
/// this handler handles aggregation + tax/discount/FX math on already-extracted figures. Task
/// 104's handler handles formula-driven calculations (rate × principal × time, etc.).
/// </para>
/// <para>
/// See <c>Services/Ai/Handlers/HandlerRegistrationConventions.md</c> for the 4-point R6 Pillar 2
/// handler contract this implementation satisfies.
/// </para>
/// </remarks>
public sealed class FinancialCalculatorHandler : IToolHandler
{
    private const string HandlerIdValue = "FinancialCalculatorHandler";
    private const int DefaultDecimalPrecision = 2;

    /// <summary>
    /// Currencies accepted by default when the tool's configuration does not specify
    /// <see cref="HandlerConfig.SupportedCurrencies"/>. Tool configuration overrides this list.
    /// </summary>
    private static readonly IReadOnlySet<string> DefaultSupportedCurrencies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "USD", "EUR", "GBP", "JPY", "CAD" };

    /// <summary>
    /// Per-currency default precision overrides. JPY is conventionally tracked at 0 decimal places.
    /// Tool configuration's <see cref="HandlerConfig.DecimalPrecision"/> overrides this.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> DefaultPrecisionByCurrency =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["JPY"] = 0
        };

    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<FinancialCalculatorHandler> _logger;

    public FinancialCalculatorHandler(ILogger<FinancialCalculatorHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Financial Calculator Handler",
        Description: "Pure deterministic financial math: sum, tax, discount, FX conversion (configured rate table), " +
                     "weighted average, and aggregation by category. All money math uses decimal; mixed-currency " +
                     "operations are rejected. NO LLM call.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "application/json" },
        Parameters: new[]
        {
            new ToolParameterDefinition("operation", "Operation to perform: Sum, Tax, Discount, FxConvert, WeightedAvg, AggregateByCategory.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("operands", "List of monetary operands (amount + currency [+ optional category/weight]).", ToolParameterType.Array, Required: false),
            new ToolParameterDefinition("rate", "Rate value (0.0825 = 8.25%) for Tax and Discount.", ToolParameterType.Decimal, Required: false),
            new ToolParameterDefinition("amount", "Source amount (object with amount + currency) for FxConvert.", ToolParameterType.Object, Required: false),
            new ToolParameterDefinition("targetCurrency", "Target ISO-4217 currency code for FxConvert.", ToolParameterType.String, Required: false)
        },
        ConfigurationSchema: null);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.FinancialCalculator };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required (ADR-014 cache-key invariant).");

        if (string.IsNullOrWhiteSpace(tool.Configuration))
            errors.Add("Tool configuration JSON is required (operation + operands).");

        return errors.Count == 0
            ? ToolValidationResult.Success()
            : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Execute(tool, context));
    }

    private ToolResult Execute(AnalysisTool tool, ToolExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        FinancialOperation operation = FinancialOperation.Sum;
        try
        {
            var request = ParseRequest(tool.Configuration);
            operation = request.Operation;
            var config = request.Config ?? HandlerConfig.Default;

            ValidateRequest(request, config);

            // ADR-015: log handler + operation only; NEVER amount values.
            _logger.LogInformation(
                "FinancialCalculatorHandler invoked: tool={ToolId}, operation={Operation}",
                tool.Id, operation);

            var result = operation switch
            {
                FinancialOperation.Sum => ExecuteSum(request, config),
                FinancialOperation.Tax => ExecuteTax(request, config),
                FinancialOperation.Discount => ExecuteDiscount(request, config),
                FinancialOperation.FxConvert => ExecuteFxConvert(request, config),
                FinancialOperation.WeightedAvg => ExecuteWeightedAvg(request, config),
                FinancialOperation.AggregateByCategory => ExecuteAggregateByCategory(request, config),
                _ => throw new InvalidOperationException(
                    $"Operation '{operation}' is not implemented in FinancialCalculatorHandler.")
            };

            stopwatch.Stop();

            _logger.LogInformation(
                "FinancialCalculatorHandler success: tool={ToolId}, operation={Operation}, durationMs={DurationMs}",
                tool.Id, operation, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                data: result,
                summary: result.Summary,
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 0,
                    ModelName = null
                });
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "FinancialCalculatorHandler cancelled: tool={ToolId}, operation={Operation}",
                tool.Id, operation);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                errorMessage: "Tool execution was cancelled.",
                errorCode: ToolErrorCodes.Cancelled,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (FinancialValidationException ex)
        {
            stopwatch.Stop();
            // Log code + operation; ex.Message already strips raw operand values per ADR-015.
            _logger.LogWarning(
                "FinancialCalculatorHandler validation failure: tool={ToolId}, operation={Operation}, code={Code}",
                tool.Id, operation, ex.Code);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                errorMessage: ex.Message,
                errorCode: ex.Code,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "FinancialCalculatorHandler input JSON parse failure: tool={ToolId}, operation={Operation}",
                tool.Id, operation);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                errorMessage: $"Failed to parse tool configuration JSON: {ex.Message}",
                errorCode: ToolErrorCodes.InvalidConfiguration,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log exception type only — message may include sensitive raw values from
            // the underlying calculation library; we don't propagate it to telemetry.
            _logger.LogError(
                ex,
                "FinancialCalculatorHandler unexpected error: tool={ToolId}, operation={Operation}, exceptionType={ExceptionType}",
                tool.Id, operation, ex.GetType().FullName);
            return ToolResult.Error(
                HandlerId,
                tool.Id,
                tool.Name,
                errorMessage: $"Calculation failed: {ex.Message}",
                errorCode: ToolErrorCodes.InternalError,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Operation implementations — all `decimal`, no `double`/`float` in money paths.
    // ─────────────────────────────────────────────────────────────────────────────

    private static CalculatorResult ExecuteSum(FinancialRequest request, HandlerConfig config)
    {
        EnsureOperandsPresent(request);
        var currency = EnsureSingleCurrency(request.Operands!, "Sum");
        var precision = ResolvePrecision(currency, config);
        var rounding = ResolveRounding(config);

        decimal total = 0m;
        foreach (var op in request.Operands!)
        {
            total += op.Amount;
        }

        var rounded = decimal.Round(total, precision, rounding);
        return new CalculatorResult
        {
            Operation = FinancialOperation.Sum,
            Result = rounded,
            Currency = currency,
            FormattedAmount = FormatAmount(rounded, currency, precision),
            Summary = $"Sum of {request.Operands.Count} operands = {FormatAmount(rounded, currency, precision)}"
        };
    }

    private static CalculatorResult ExecuteTax(FinancialRequest request, HandlerConfig config)
    {
        EnsureOperandsPresent(request);
        if (request.Rate is null)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Tax operation requires a non-null 'rate'.");
        if (request.Rate < 0m)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Tax rate must be non-negative.");

        var currency = EnsureSingleCurrency(request.Operands!, "Tax");
        var precision = ResolvePrecision(currency, config);
        var rounding = ResolveRounding(config);

        decimal subtotal = 0m;
        foreach (var op in request.Operands!)
            subtotal += op.Amount;

        var taxAmount = decimal.Round(subtotal * request.Rate.Value, precision, rounding);
        var total = decimal.Round(subtotal + taxAmount, precision, rounding);

        return new CalculatorResult
        {
            Operation = FinancialOperation.Tax,
            Result = total,
            Subtotal = decimal.Round(subtotal, precision, rounding),
            TaxAmount = taxAmount,
            AppliedRate = request.Rate.Value,
            Currency = currency,
            FormattedAmount = FormatAmount(total, currency, precision),
            Summary = $"Subtotal + tax @ {FormatRatePercent(request.Rate.Value)} = {FormatAmount(total, currency, precision)}"
        };
    }

    private static CalculatorResult ExecuteDiscount(FinancialRequest request, HandlerConfig config)
    {
        EnsureOperandsPresent(request);
        if (request.Rate is null)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Discount operation requires a non-null 'rate'.");
        if (request.Rate < 0m)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Discount rate must be non-negative.");
        if (request.Rate > 1m)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Discount rate must be <= 1.0 (100%).");

        var currency = EnsureSingleCurrency(request.Operands!, "Discount");
        var precision = ResolvePrecision(currency, config);
        var rounding = ResolveRounding(config);

        decimal subtotal = 0m;
        foreach (var op in request.Operands!)
            subtotal += op.Amount;

        var discountAmount = decimal.Round(subtotal * request.Rate.Value, precision, rounding);
        var total = decimal.Round(subtotal - discountAmount, precision, rounding);

        return new CalculatorResult
        {
            Operation = FinancialOperation.Discount,
            Result = total,
            Subtotal = decimal.Round(subtotal, precision, rounding),
            DiscountAmount = discountAmount,
            AppliedRate = request.Rate.Value,
            Currency = currency,
            FormattedAmount = FormatAmount(total, currency, precision),
            Summary = $"Subtotal − discount @ {FormatRatePercent(request.Rate.Value)} = {FormatAmount(total, currency, precision)}"
        };
    }

    private static CalculatorResult ExecuteFxConvert(FinancialRequest request, HandlerConfig config)
    {
        if (request.Amount is null)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "FxConvert operation requires 'amount' (source amount + currency).");
        if (string.IsNullOrWhiteSpace(request.TargetCurrency))
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "FxConvert operation requires 'targetCurrency'.");

        var sourceCurrency = NormalizeCurrency(request.Amount.Currency);
        var targetCurrency = NormalizeCurrency(request.TargetCurrency);

        if (!IsSupportedCurrency(sourceCurrency, config))
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, $"Source currency '{sourceCurrency}' is not in the supported list.");
        if (!IsSupportedCurrency(targetCurrency, config))
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, $"Target currency '{targetCurrency}' is not in the supported list.");

        decimal rate;
        if (sourceCurrency.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            rate = 1m;
        }
        else
        {
            var key = $"{sourceCurrency}/{targetCurrency}";
            if (config.FxRateTable is null || !config.FxRateTable.TryGetValue(key, out rate))
            {
                throw new FinancialValidationException(
                    ToolErrorCodes.ValidationFailed,
                    $"FX rate '{key}' is not configured in fxRateTable. Live FX lookup is not supported.");
            }
            if (rate <= 0m)
                throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, $"FX rate '{key}' must be > 0.");
        }

        var precision = ResolvePrecision(targetCurrency, config);
        var rounding = ResolveRounding(config);
        var converted = decimal.Round(request.Amount.Amount * rate, precision, rounding);

        return new CalculatorResult
        {
            Operation = FinancialOperation.FxConvert,
            Result = converted,
            Currency = targetCurrency,
            SourceCurrency = sourceCurrency,
            AppliedRate = rate,
            FormattedAmount = FormatAmount(converted, targetCurrency, precision),
            Summary = $"FX {sourceCurrency}→{targetCurrency} @ {rate} = {FormatAmount(converted, targetCurrency, precision)}"
        };
    }

    private static CalculatorResult ExecuteWeightedAvg(FinancialRequest request, HandlerConfig config)
    {
        EnsureOperandsPresent(request);
        var currency = EnsureSingleCurrency(request.Operands!, "WeightedAvg");
        var precision = ResolvePrecision(currency, config);
        var rounding = ResolveRounding(config);

        decimal weightedSum = 0m;
        decimal weightTotal = 0m;
        foreach (var op in request.Operands!)
        {
            var weight = op.Weight ?? 1m;
            if (weight < 0m)
                throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "WeightedAvg weights must be non-negative.");
            weightedSum += op.Amount * weight;
            weightTotal += weight;
        }

        if (weightTotal == 0m)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "WeightedAvg cannot divide by zero — total weight is 0.");

        var avg = decimal.Round(weightedSum / weightTotal, precision, rounding);
        return new CalculatorResult
        {
            Operation = FinancialOperation.WeightedAvg,
            Result = avg,
            Currency = currency,
            FormattedAmount = FormatAmount(avg, currency, precision),
            Summary = $"Weighted average over {request.Operands.Count} operands = {FormatAmount(avg, currency, precision)}"
        };
    }

    private static CalculatorResult ExecuteAggregateByCategory(FinancialRequest request, HandlerConfig config)
    {
        EnsureOperandsPresent(request);
        var currency = EnsureSingleCurrency(request.Operands!, "AggregateByCategory");
        var precision = ResolvePrecision(currency, config);
        var rounding = ResolveRounding(config);

        var grouped = new SortedDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in request.Operands!)
        {
            var category = string.IsNullOrWhiteSpace(op.Category) ? "(uncategorized)" : op.Category.Trim();
            if (grouped.TryGetValue(category, out var existing))
                grouped[category] = existing + op.Amount;
            else
                grouped[category] = op.Amount;
        }

        var buckets = new List<CategoryBucket>(grouped.Count);
        decimal total = 0m;
        foreach (var kvp in grouped)
        {
            var rounded = decimal.Round(kvp.Value, precision, rounding);
            buckets.Add(new CategoryBucket
            {
                Category = kvp.Key,
                Amount = rounded,
                FormattedAmount = FormatAmount(rounded, currency, precision)
            });
            total += rounded;
        }
        total = decimal.Round(total, precision, rounding);

        return new CalculatorResult
        {
            Operation = FinancialOperation.AggregateByCategory,
            Result = total,
            Currency = currency,
            FormattedAmount = FormatAmount(total, currency, precision),
            Buckets = buckets,
            Summary = $"Aggregated {request.Operands.Count} operands into {buckets.Count} categories; total = {FormatAmount(total, currency, precision)}"
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static FinancialRequest ParseRequest(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
            throw new FinancialValidationException(ToolErrorCodes.InvalidConfiguration, "Tool configuration JSON is empty.");

        var request = JsonSerializer.Deserialize<FinancialRequest>(configurationJson, DeserializerOptions);
        if (request is null)
            throw new FinancialValidationException(ToolErrorCodes.InvalidConfiguration, "Tool configuration JSON deserialized to null.");

        return request;
    }

    private static void ValidateRequest(FinancialRequest request, HandlerConfig config)
    {
        if (request.Operands is { Count: > 0 })
        {
            foreach (var op in request.Operands)
            {
                if (string.IsNullOrWhiteSpace(op.Currency))
                    throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Each operand requires a non-empty 'currency' ISO-4217 code.");
                var normalized = NormalizeCurrency(op.Currency);
                if (!IsSupportedCurrency(normalized, config))
                    throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, $"Currency '{normalized}' is not in the supported list.");
            }
        }
    }

    private static void EnsureOperandsPresent(FinancialRequest request)
    {
        if (request.Operands is null || request.Operands.Count == 0)
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "At least one operand is required.");
    }

    private static string EnsureSingleCurrency(IReadOnlyList<MonetaryOperand> operands, string operation)
    {
        string? currency = null;
        foreach (var op in operands)
        {
            var normalized = NormalizeCurrency(op.Currency);
            if (currency is null)
            {
                currency = normalized;
            }
            else if (!currency.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new FinancialValidationException(
                    ToolErrorCodes.ValidationFailed,
                    $"{operation} operation requires all operands share a single currency. Use FxConvert explicitly for cross-currency math.");
            }
        }
        return currency ?? throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "No currency could be determined from operands.");
    }

    private static int ResolvePrecision(string currency, HandlerConfig config)
    {
        if (config.DecimalPrecision is { } p && p >= 0)
            return p;
        if (DefaultPrecisionByCurrency.TryGetValue(currency, out var perCurrency))
            return perCurrency;
        return DefaultDecimalPrecision;
    }

    private static MidpointRounding ResolveRounding(HandlerConfig config)
    {
        var mode = config.DefaultRoundingMode;
        if (string.IsNullOrWhiteSpace(mode))
            return MidpointRounding.ToEven;
        return mode.Trim() switch
        {
            "BankersRounding" => MidpointRounding.ToEven,
            "ToEven" => MidpointRounding.ToEven,
            "MidpointAwayFromZero" => MidpointRounding.AwayFromZero,
            "AwayFromZero" => MidpointRounding.AwayFromZero,
            _ => throw new FinancialValidationException(
                ToolErrorCodes.InvalidConfiguration,
                $"Unsupported rounding mode '{mode}'. Use 'BankersRounding' or 'MidpointAwayFromZero'.")
        };
    }

    private static bool IsSupportedCurrency(string currency, HandlerConfig config)
    {
        if (config.SupportedCurrencies is { Count: > 0 })
            return config.SupportedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase);
        return DefaultSupportedCurrencies.Contains(currency);
    }

    private static string NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new FinancialValidationException(ToolErrorCodes.ValidationFailed, "Currency is required and must be a non-empty ISO-4217 code.");
        return currency.Trim().ToUpperInvariant();
    }

    private static string FormatAmount(decimal amount, string currency, int precision)
    {
        // ISO-4217 prefix + numeric using invariant formatting. We do NOT use culture-specific
        // currency symbols (e.g., $/€/£) so the output is deterministic across host locales.
        var formatString = "F" + precision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{currency} {amount.ToString(formatString, System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string FormatRatePercent(decimal rate)
    {
        // Rate stored as fraction (0.0825 = 8.25%). Format up to 4 fractional digits then trim trailing zeros.
        var percent = rate * 100m;
        return percent.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DTOs (internal — JSON contract for tool.Configuration + tool.HandlerConfiguration)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shape of the tool's <c>sprk_analysistool.sprk_configuration</c> JSON (per-invocation request).
    /// </summary>
    internal sealed class FinancialRequest
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FinancialOperation Operation { get; set; }
        public List<MonetaryOperand>? Operands { get; set; }
        public decimal? Rate { get; set; }
        public MonetaryOperand? Amount { get; set; }
        public string? TargetCurrency { get; set; }
        public HandlerConfig? Config { get; set; }
    }

    internal sealed class MonetaryOperand
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? Category { get; set; }
        public decimal? Weight { get; set; }
    }

    /// <summary>
    /// Handler-wide config defaults; may be supplied via the tool's HandlerConfiguration field
    /// or inline on <see cref="FinancialRequest.Config"/>.
    /// </summary>
    internal sealed class HandlerConfig
    {
        public IReadOnlyList<string>? SupportedCurrencies { get; set; }
        public IReadOnlyDictionary<string, decimal>? FxRateTable { get; set; }
        public string? DefaultRoundingMode { get; set; }
        public int? DecimalPrecision { get; set; }

        public static HandlerConfig Default { get; } = new();
    }

    public enum FinancialOperation
    {
        Sum = 0,
        Tax = 1,
        Discount = 2,
        FxConvert = 3,
        WeightedAvg = 4,
        AggregateByCategory = 5
    }

    public sealed class CalculatorResult
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FinancialOperation Operation { get; set; }
        public decimal Result { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string FormattedAmount { get; set; } = string.Empty;
        public string? Summary { get; set; }

        public decimal? Subtotal { get; set; }
        public decimal? TaxAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? AppliedRate { get; set; }
        public string? SourceCurrency { get; set; }
        public List<CategoryBucket>? Buckets { get; set; }
    }

    public sealed class CategoryBucket
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string FormattedAmount { get; set; } = string.Empty;
    }

    private sealed class FinancialValidationException : Exception
    {
        public string Code { get; }
        public FinancialValidationException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
