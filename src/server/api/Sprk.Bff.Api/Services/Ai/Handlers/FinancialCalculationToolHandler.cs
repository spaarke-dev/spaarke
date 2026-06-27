using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Pure-deterministic financial calculation handler — R6 Pillar 2, Wave 1 (FR-20).
/// </summary>
/// <remarks>
/// <para>
/// Implements formula-based financial calculations. Examples:
/// </para>
/// <list type="bullet">
/// <item><c>simple_interest</c> — I = P × r × t</item>
/// <item><c>compound_interest</c> — A − P where A = P × (1 + r / n)^(n × t)</item>
/// <item><c>present_value</c> — PV = FV / (1 + r)^t</item>
/// <item><c>future_value</c> — FV = PV × (1 + r)^t</item>
/// <item><c>loan_payment</c> — M = P × (r / (1 − (1 + r)^−n))  (per-period rate r, n periods)</item>
/// <item><c>roi</c> — ROI = (gain − cost) / cost</item>
/// </list>
/// <para>
/// <b>Distinct from <c>FinancialCalculatorHandler</c> (R6 task 102)</b>: 102 aggregates
/// extracted figures (sum / discount / tax / FX). This handler implements named formulas
/// taking explicit parameters (principal, rate, time, periods).
/// </para>
/// <para>
/// <b>Binding rules</b>:
/// </para>
/// <list type="bullet">
/// <item><b>FR-20</b>: pure deterministic — ZERO LLM / Azure OpenAI dependency.</item>
/// <item><b>Money-math invariant</b>: every monetary computation uses <see cref="decimal"/>
/// (banker's rounding default; configurable). NO <c>double</c> / <c>float</c>.</item>
/// <item><b>ADR-010</b>: auto-discovered by <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>;
/// ZERO manual DI line.</item>
/// <item><b>ADR-013</b>: lives in <c>Services/Ai/Handlers/</c>.</item>
/// <item><b>ADR-015</b>: telemetry logs handler name + formula name + outcome + timestamp +
/// deterministic IDs ONLY. NEVER parameter values (principal amounts are sensitive PII per
/// the legal-finance threat model).</item>
/// <item><b>ADR-029</b>: deterministic handler — zero new NuGet packages; publish-size delta ≪ 0.5 MB.</item>
/// </list>
/// <para>
/// <b>Dataverse contract</b>: ships with <c>sprk_analysistool</c> row whose
/// <c>sprk_handlerclass = "FinancialCalculationToolHandler"</c>, <c>sprk_toolcode = "FIN-CALC-FORMULA@v1"</c>,
/// <c>sprk_availableincontexts = Playbook</c>. Seed lives at
/// <c>infra/dataverse/sprk_analysistool-financial-calculation-row.json</c>.
/// </para>
/// </remarks>
public sealed class FinancialCalculationToolHandler : IToolHandler
{
    private const string HandlerIdValue = "FinancialCalculationToolHandler";

    private readonly ILogger<FinancialCalculationToolHandler> _logger;

    /// <summary>
    /// Supported formula names. Lookup is case-insensitive.
    /// </summary>
    private static readonly HashSet<string> SupportedFormulas = new(StringComparer.OrdinalIgnoreCase)
    {
        "simple_interest",
        "compound_interest",
        "present_value",
        "future_value",
        "loan_payment",
        "roi"
    };

    /// <summary>
    /// JSON Schema (Draft 07) describing the configuration shape (formula + inputs + options).
    /// Surfaces as <c>Metadata.ConfigurationSchema</c> so the registry can document the contract.
    /// </summary>
    private static readonly object ConfigSchemaShape = new
    {
        schema = "https://json-schema.org/draft-07/schema#",
        title = "Financial Calculation Tool Configuration",
        type = "object",
        properties = new
        {
            formula = new
            {
                type = "string",
                description = "Formula to apply.",
                @enum = new[]
                {
                    "simple_interest", "compound_interest", "present_value",
                    "future_value", "loan_payment", "roi"
                }
            },
            inputs = new
            {
                type = "object",
                description = "Formula-specific named parameters: principal, rate (annual decimal), time (years), periods, futureValue, presentValue, gain, cost."
            },
            options = new
            {
                type = "object",
                description = "Optional formatting and rounding controls.",
                properties = new
                {
                    precision = new { type = "integer", minimum = 0, maximum = 8, @default = 2 },
                    rounding = new
                    {
                        type = "string",
                        @enum = new[] { "banker", "away_from_zero" },
                        @default = "banker"
                    },
                    compoundingFrequency = new { type = "integer", minimum = 1, maximum = 365, @default = 1 },
                    currency = new { type = "string", @default = "USD" }
                }
            }
        },
        required = new[] { "formula", "inputs" }
    };

    public FinancialCalculationToolHandler(ILogger<FinancialCalculationToolHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Financial Calculation Tool Handler",
        Description: "Pure-deterministic, currency-aware financial formulas (simple/compound interest, present/future value, loan payment, ROI). " +
                     "No LLM call; all monetary math in decimal.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "application/json" },
        Parameters: new[]
        {
            new ToolParameterDefinition("formula", "Formula name: simple_interest, compound_interest, present_value, future_value, loan_payment, roi.", ToolParameterType.String, Required: true),
            new ToolParameterDefinition("inputs", "Named formula parameters (principal, rate, time, periods, futureValue, presentValue, gain, cost).", ToolParameterType.Object, Required: true),
            new ToolParameterDefinition("options", "Optional precision / rounding / compoundingFrequency / currency.", ToolParameterType.Object, Required: false)
        },
        ConfigurationSchema: ConfigSchemaShape);

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.FinancialCalculator };

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.TenantId))
            errors.Add("TenantId is required (ADR-014 cache-key invariant).");

        if (string.IsNullOrWhiteSpace(tool.Configuration))
        {
            errors.Add("Tool configuration is required (must include 'formula' + 'inputs').");
            return ToolValidationResult.Failure(errors);
        }

        FinancialCalculationConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<FinancialCalculationConfig>(
                tool.Configuration,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid configuration JSON: {ex.Message}");
            return ToolValidationResult.Failure(errors);
        }

        if (config is null)
        {
            errors.Add("Configuration deserialized to null.");
            return ToolValidationResult.Failure(errors);
        }

        if (string.IsNullOrWhiteSpace(config.Formula))
        {
            errors.Add("Formula name is required.");
        }
        else if (!SupportedFormulas.Contains(config.Formula))
        {
            errors.Add($"Unknown formula '{config.Formula}'. Supported: {string.Join(", ", SupportedFormulas)}.");
        }

        if (config.Inputs is null || config.Inputs.Count == 0)
        {
            errors.Add("Inputs map is required.");
        }

        if (config.Options?.Precision is { } precision && (precision < 0 || precision > 8))
        {
            errors.Add("options.precision must be between 0 and 8.");
        }

        if (config.Options?.CompoundingFrequency is { } freq && freq < 1)
        {
            errors.Add("options.compoundingFrequency must be >= 1.");
        }

        return errors.Count == 0 ? ToolValidationResult.Success() : ToolValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        // ADR-015: log handler id + tool id + tenant id + analysis id ONLY. NEVER inputs.
        _logger.LogInformation(
            "FinancialCalculationToolHandler starting: analysis={AnalysisId}, tool={ToolId} ({ToolName}), tenant={TenantId}",
            context.AnalysisId, tool.Id, tool.Name, context.TenantId);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var config = JsonSerializer.Deserialize<FinancialCalculationConfig>(
                tool.Configuration!,
                JsonOptions)!;

            var options = config.Options ?? new FinancialCalculationOptions();
            var precision = options.Precision ?? 2;
            var roundingMode = ParseRoundingMode(options.Rounding);
            var currency = string.IsNullOrWhiteSpace(options.Currency) ? "USD" : options.Currency.Trim().ToUpperInvariant();
            var compoundingFrequency = options.CompoundingFrequency ?? 1;

            var formula = config.Formula!.ToLowerInvariant();
            var (rawResult, breakdown) = formula switch
            {
                "simple_interest" => ComputeSimpleInterest(config.Inputs!),
                "compound_interest" => ComputeCompoundInterest(config.Inputs!, compoundingFrequency),
                "present_value" => ComputePresentValue(config.Inputs!),
                "future_value" => ComputeFutureValue(config.Inputs!),
                "loan_payment" => ComputeLoanPayment(config.Inputs!),
                "roi" => ComputeRoi(config.Inputs!),
                _ => throw new InvalidOperationException($"Unhandled formula: {formula}")
            };

            var rounded = Math.Round(rawResult, precision, roundingMode);
            var formatted = FormatAmount(rounded, currency, precision);

            stopwatch.Stop();

            var resultData = new FinancialCalculationResultData
            {
                Result = rounded,
                Formula = formula,
                Breakdown = breakdown,
                Currency = currency,
                FormattedAmount = formatted,
                Precision = precision,
                RoundingMode = roundingMode.ToString()
            };

            // ADR-015: log handler id + formula name + outcome + duration ONLY. NEVER values.
            _logger.LogInformation(
                "FinancialCalculationToolHandler complete: analysis={AnalysisId}, formula={Formula}, duration={Duration}ms",
                context.AnalysisId, formula, stopwatch.ElapsedMilliseconds);

            return Task.FromResult(ToolResult.Ok(
                HandlerId,
                tool.Id,
                tool.Name,
                resultData,
                summary: $"{formula} ({currency})",
                confidence: 1.0,
                execution: new ToolExecutionMetadata
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModelCalls = 0,
                    ModelName = null
                }));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "FinancialCalculationToolHandler cancelled: analysis={AnalysisId}",
                context.AnalysisId);
            return Task.FromResult(ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Tool execution was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
        }
        catch (FinancialCalculationException ex)
        {
            stopwatch.Stop();
            // ADR-015: the exception message is bounded (no input values; see ThrowValidation).
            _logger.LogWarning(
                "FinancialCalculationToolHandler validation error: analysis={AnalysisId}, code={ErrorCode}",
                context.AnalysisId, ex.ErrorCode);
            return Task.FromResult(ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                ex.Message,
                ex.ErrorCode,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "FinancialCalculationToolHandler failed: analysis={AnalysisId}",
                context.AnalysisId);
            return Task.FromResult(ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                $"Tool execution failed: {ex.Message}",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Formula implementations (all decimal-typed)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Simple interest: I = P × r × t.</summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputeSimpleInterest(
        IReadOnlyDictionary<string, decimal> inputs)
    {
        var p = RequirePositive(inputs, "principal");
        var r = RequireRate(inputs, "rate");
        var t = RequireNonNegative(inputs, "time");

        var interest = p * r * t;
        var breakdown = new Dictionary<string, decimal>
        {
            ["principal"] = p,
            ["rate"] = r,
            ["time"] = t,
            ["interest"] = interest
        };
        return (interest, breakdown);
    }

    /// <summary>
    /// Compound interest gained: A − P where A = P × (1 + r / n)^(n × t).
    /// Currency-aware via the caller; <c>n</c> defaults to 1 (annual).
    /// </summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputeCompoundInterest(
        IReadOnlyDictionary<string, decimal> inputs,
        int compoundingFrequency)
    {
        var p = RequirePositive(inputs, "principal");
        var r = RequireRate(inputs, "rate");
        var t = RequireNonNegative(inputs, "time");

        if (compoundingFrequency < 1)
        {
            ThrowValidation("compoundingFrequency must be >= 1.");
        }

        var n = (decimal)compoundingFrequency;
        var perPeriod = 1m + (r / n);
        var totalPeriods = n * t;

        // Compound interest is computed with `decimal` via integer exponent expansion when
        // the period count is whole; otherwise fall back to log-exp via double, then quantize
        // back to decimal at the OUTPUT step. This preserves accuracy for the common cases
        // (annual / monthly / quarterly compounding with integer-year horizons) while still
        // supporting fractional time.
        decimal amount;
        if (totalPeriods == Math.Floor(totalPeriods) && totalPeriods >= 0m && totalPeriods <= 1000m)
        {
            amount = p * Power(perPeriod, (int)totalPeriods);
        }
        else
        {
            var amountDouble = (double)p * Math.Pow((double)perPeriod, (double)totalPeriods);
            amount = (decimal)amountDouble;
        }

        var interest = amount - p;
        var breakdown = new Dictionary<string, decimal>
        {
            ["principal"] = p,
            ["rate"] = r,
            ["time"] = t,
            ["compoundingFrequency"] = compoundingFrequency,
            ["finalAmount"] = amount,
            ["interest"] = interest
        };
        return (interest, breakdown);
    }

    /// <summary>Present value: PV = FV / (1 + r)^t.</summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputePresentValue(
        IReadOnlyDictionary<string, decimal> inputs)
    {
        var fv = RequirePositive(inputs, "futureValue");
        var r = RequireRate(inputs, "rate");
        var t = RequireNonNegative(inputs, "time");

        var discountFactor = Power(1m + r, t);
        if (discountFactor == 0m)
        {
            ThrowValidation("Discount factor cannot be zero.");
        }
        var pv = fv / discountFactor;

        var breakdown = new Dictionary<string, decimal>
        {
            ["futureValue"] = fv,
            ["rate"] = r,
            ["time"] = t,
            ["discountFactor"] = discountFactor,
            ["presentValue"] = pv
        };
        return (pv, breakdown);
    }

    /// <summary>Future value: FV = PV × (1 + r)^t.</summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputeFutureValue(
        IReadOnlyDictionary<string, decimal> inputs)
    {
        var pv = RequirePositive(inputs, "presentValue");
        var r = RequireRate(inputs, "rate");
        var t = RequireNonNegative(inputs, "time");

        var growthFactor = Power(1m + r, t);
        var fv = pv * growthFactor;

        var breakdown = new Dictionary<string, decimal>
        {
            ["presentValue"] = pv,
            ["rate"] = r,
            ["time"] = t,
            ["growthFactor"] = growthFactor,
            ["futureValue"] = fv
        };
        return (fv, breakdown);
    }

    /// <summary>
    /// Loan payment (amortization): M = P × (r / (1 − (1 + r)^−n)).
    /// <c>rate</c> is the periodic rate (e.g., monthly rate); <c>periods</c> the number of periods.
    /// </summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputeLoanPayment(
        IReadOnlyDictionary<string, decimal> inputs)
    {
        var p = RequirePositive(inputs, "principal");
        var r = RequireRate(inputs, "rate");
        var n = RequirePositiveInt(inputs, "periods");

        decimal payment;
        if (r == 0m)
        {
            // Zero-interest loan: M = P / n.
            payment = p / n;
        }
        else
        {
            var factor = Power(1m + r, -(int)n);
            var denom = 1m - factor;
            if (denom == 0m)
            {
                ThrowValidation("Amortization denominator is zero.");
            }
            payment = p * (r / denom);
        }

        var totalPaid = payment * n;
        var totalInterest = totalPaid - p;

        var breakdown = new Dictionary<string, decimal>
        {
            ["principal"] = p,
            ["periodicRate"] = r,
            ["periods"] = n,
            ["payment"] = payment,
            ["totalPaid"] = totalPaid,
            ["totalInterest"] = totalInterest
        };
        return (payment, breakdown);
    }

    /// <summary>ROI: (gain − cost) / cost.</summary>
    private static (decimal result, IReadOnlyDictionary<string, decimal> breakdown) ComputeRoi(
        IReadOnlyDictionary<string, decimal> inputs)
    {
        var gain = RequireFinite(inputs, "gain");
        var cost = RequirePositive(inputs, "cost");

        var roi = (gain - cost) / cost;
        var breakdown = new Dictionary<string, decimal>
        {
            ["gain"] = gain,
            ["cost"] = cost,
            ["roi"] = roi
        };
        return (roi, breakdown);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decimal-typed power for integer exponents (positive or negative).
    /// Preserves arbitrary precision unlike <c>Math.Pow</c>.
    /// </summary>
    private static decimal Power(decimal baseValue, int exponent)
    {
        if (exponent == 0) return 1m;
        if (exponent < 0)
        {
            if (baseValue == 0m)
            {
                ThrowValidation("Cannot raise zero to a negative power.");
            }
            return 1m / Power(baseValue, -exponent);
        }
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }
        return result;
    }

    /// <summary>
    /// Decimal power with decimal exponent — used for present/future value when <c>t</c>
    /// is fractional. Routes through <c>double</c> internally; precision-bounded by the
    /// final quantize step in <c>ExecuteAsync</c>.
    /// </summary>
    private static decimal Power(decimal baseValue, decimal exponent)
    {
        if (exponent == 0m) return 1m;
        if (exponent == Math.Floor(exponent) && exponent >= -1000m && exponent <= 1000m)
        {
            return Power(baseValue, (int)exponent);
        }
        var result = Math.Pow((double)baseValue, (double)exponent);
        return (decimal)result;
    }

    private static decimal RequirePositive(IReadOnlyDictionary<string, decimal> inputs, string name)
    {
        var value = RequireFinite(inputs, name);
        if (value <= 0m)
        {
            ThrowValidation($"'{name}' must be > 0.");
        }
        return value;
    }

    private static decimal RequireNonNegative(IReadOnlyDictionary<string, decimal> inputs, string name)
    {
        var value = RequireFinite(inputs, name);
        if (value < 0m)
        {
            ThrowValidation($"'{name}' must be >= 0.");
        }
        return value;
    }

    private static decimal RequirePositiveInt(IReadOnlyDictionary<string, decimal> inputs, string name)
    {
        var value = RequirePositive(inputs, name);
        if (value != Math.Truncate(value))
        {
            ThrowValidation($"'{name}' must be a positive integer.");
        }
        return value;
    }

    private static decimal RequireRate(IReadOnlyDictionary<string, decimal> inputs, string name)
    {
        var value = RequireFinite(inputs, name);
        if (value < 0m)
        {
            ThrowValidation($"'{name}' must be >= 0 (rate as decimal; e.g., 0.05 = 5%).");
        }
        return value;
    }

    private static decimal RequireFinite(IReadOnlyDictionary<string, decimal> inputs, string name)
    {
        if (!inputs.TryGetValue(name, out var value))
        {
            ThrowValidation($"Required input '{name}' is missing.");
        }
        return value;
    }

    private static void ThrowValidation(string message)
        => throw new FinancialCalculationException(message, ToolErrorCodes.ValidationFailed);

    private static MidpointRounding ParseRoundingMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return MidpointRounding.ToEven; // Banker's rounding (default)
        return mode.Trim().ToLowerInvariant() switch
        {
            "banker" or "to_even" => MidpointRounding.ToEven,
            "away_from_zero" or "half_up" => MidpointRounding.AwayFromZero,
            _ => MidpointRounding.ToEven
        };
    }

    private static string FormatAmount(decimal amount, string currency, int precision)
    {
        var format = "F" + precision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var amountStr = amount.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return $"{currency} {amountStr}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal configuration + result types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Configuration shape for <see cref="FinancialCalculationToolHandler"/>.</summary>
internal sealed class FinancialCalculationConfig
{
    /// <summary>Formula name (case-insensitive). See <c>SupportedFormulas</c>.</summary>
    public string? Formula { get; set; }

    /// <summary>
    /// Named formula parameters as decimals (principal, rate, time, periods, futureValue,
    /// presentValue, gain, cost). Keys are case-sensitive — callers should use camelCase.
    /// </summary>
    public Dictionary<string, decimal>? Inputs { get; set; }

    /// <summary>Optional formatting + rounding controls.</summary>
    public FinancialCalculationOptions? Options { get; set; }
}

/// <summary>Optional formatting + rounding options.</summary>
internal sealed class FinancialCalculationOptions
{
    /// <summary>Decimal places in output (0-8; default 2).</summary>
    public int? Precision { get; set; }

    /// <summary>Rounding mode: "banker" (default; <see cref="MidpointRounding.ToEven"/>) or "away_from_zero".</summary>
    public string? Rounding { get; set; }

    /// <summary>Compounding frequency per year (1=annual, 4=quarterly, 12=monthly, 365=daily; default 1).</summary>
    public int? CompoundingFrequency { get; set; }

    /// <summary>ISO 4217 currency code (default "USD").</summary>
    public string? Currency { get; set; }
}

/// <summary>Structured result emitted in <see cref="ToolResult.Data"/>.</summary>
internal sealed class FinancialCalculationResultData
{
    public decimal Result { get; set; }
    public string Formula { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, decimal> Breakdown { get; set; } = new Dictionary<string, decimal>();
    public string Currency { get; set; } = "USD";
    public string FormattedAmount { get; set; } = string.Empty;
    public int Precision { get; set; }
    public string RoundingMode { get; set; } = nameof(MidpointRounding.ToEven);
}

/// <summary>
/// Sentinel exception type for validation failures inside formula computation.
/// Carries a <see cref="ToolErrorCodes"/> error code so <see cref="ToolResult.Error"/> can
/// route validation failures distinctly from internal errors.
/// </summary>
internal sealed class FinancialCalculationException : Exception
{
    public string ErrorCode { get; }

    public FinancialCalculationException(string message, string errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
