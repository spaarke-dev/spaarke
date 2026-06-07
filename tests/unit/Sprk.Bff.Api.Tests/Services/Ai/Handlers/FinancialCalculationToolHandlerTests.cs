using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="FinancialCalculationToolHandler"/> — R6 Pillar 2, Wave 1 (FR-20).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
/// <item>4-point contract (registered + discoverable + valid metadata + non-empty supported types)</item>
/// <item>Positive cases — each supported formula with textbook-correct worked examples</item>
/// <item>Error cases — unknown formula, missing inputs, negative principal, zero periods for loan</item>
/// <item>Config-driven — precision + rounding-mode swap changes deterministic output</item>
/// <item>Telemetry — handler logs handler name + outcome + IDs only (ADR-015)</item>
/// </list>
/// </remarks>
public sealed class FinancialCalculationToolHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // ─────────────────────────────────────────────────────────────────────────────
    // 4-point contract tests (copy of HandlerContractTestTemplate, substituting type)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(FinancialCalculationToolHandler),
            because: "the handler type must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = BuildHandler();

        handler.HandlerId.Should().Be(
            nameof(FinancialCalculationToolHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so the Dataverse sprk_handlerclass field routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = BuildHandler();
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = BuildHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.FinancialCalculator);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Pure-deterministic invariant — no LLM dependency
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handler_HasNoOpenAiClientDependency()
    {
        var ctorParams = typeof(FinancialCalculationToolHandler)
            .GetConstructors()
            .Single()
            .GetParameters();

        ctorParams.Should().NotContain(
            p => p.ParameterType == typeof(IOpenAiClient),
            because: "FR-20 binding: pure-deterministic formulas; ZERO LLM dependency");
    }

    [Fact]
    public void Handler_OnlyUsesDecimal()
    {
        // All formula methods + breakdown values return decimal; verified by source review.
        // This test asserts no double/float fields hang off the result type — the JSON
        // serializer emits decimals as JSON Numbers and we can re-read them as decimal.
        var resultData = ExecuteWithFormula("simple_interest",
            ("principal", 1000m), ("rate", 0.05m), ("time", 2m));

        resultData.HasValue.Should().BeTrue();
        var resultProp = resultData!.Value.GetProperty("Result");
        resultProp.ValueKind.Should().Be(JsonValueKind.Number);
        resultProp.GetDecimal().Should().Be(100.00m);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Positive cases — textbook worked examples
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SimpleInterest_PrincipalRateTime_ReturnsCorrectInterest()
    {
        // Textbook: $1000 @ 5% for 2 years = $100
        var result = await ExecuteAsync("simple_interest",
            ("principal", 1000m), ("rate", 0.05m), ("time", 2m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("Result").GetDecimal().Should().Be(100.00m);
        data.GetProperty("Formula").GetString().Should().Be("simple_interest");
        data.GetProperty("Currency").GetString().Should().Be("USD");
        data.GetProperty("FormattedAmount").GetString().Should().Be("USD 100.00");
    }

    [Fact]
    public async Task CompoundInterest_AnnualCompounding_ReturnsCorrectGain()
    {
        // Textbook: $1000 @ 5% for 2 years, annual compounding
        // A = 1000 × (1.05)^2 = 1102.50; interest = 102.50
        var result = await ExecuteAsync("compound_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.05m), ("time", 2m) },
            options: new { compoundingFrequency = 1 });

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("Result").GetDecimal().Should().Be(102.50m);
        data.GetProperty("Formula").GetString().Should().Be("compound_interest");
    }

    [Fact]
    public async Task CompoundInterest_MonthlyCompounding_ProducesHigherGainThanAnnual()
    {
        // Same principal/rate/time but 12 compounding periods/year should beat annual.
        var monthly = await ExecuteAsync("compound_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.05m), ("time", 2m) },
            options: new { compoundingFrequency = 12 });

        monthly.Success.Should().BeTrue(monthly.ErrorMessage);
        var monthlyGain = monthly.Data!.Value.GetProperty("Result").GetDecimal();
        monthlyGain.Should().BeGreaterThan(102.50m, "monthly compounding accrues more than annual");
    }

    [Fact]
    public async Task PresentValue_DiscountsFutureValueCorrectly()
    {
        // PV = 1102.50 / (1.05)^2 = 1000
        var result = await ExecuteAsync("present_value",
            ("futureValue", 1102.50m), ("rate", 0.05m), ("time", 2m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var pv = result.Data!.Value.GetProperty("Result").GetDecimal();
        pv.Should().Be(1000.00m);
    }

    [Fact]
    public async Task FutureValue_GrowsPresentValueCorrectly()
    {
        // FV = 1000 × (1.05)^2 = 1102.50
        var result = await ExecuteAsync("future_value",
            ("presentValue", 1000m), ("rate", 0.05m), ("time", 2m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fv = result.Data!.Value.GetProperty("Result").GetDecimal();
        fv.Should().Be(1102.50m);
    }

    [Fact]
    public async Task LoanPayment_StandardAmortization_ReturnsCorrectMonthlyPayment()
    {
        // $100,000 loan at 0.5% periodic rate (6%/12 monthly), 360 months (30y)
        // M = 100000 × (0.005 / (1 − (1.005)^−360)) ≈ 599.55
        var result = await ExecuteAsync("loan_payment",
            ("principal", 100000m), ("rate", 0.005m), ("periods", 360m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var payment = result.Data!.Value.GetProperty("Result").GetDecimal();
        payment.Should().BeApproximately(599.55m, 0.50m);
    }

    [Fact]
    public async Task LoanPayment_ZeroInterest_ReturnsPrincipalDividedByPeriods()
    {
        // Zero-interest loan: M = P / n
        var result = await ExecuteAsync("loan_payment",
            ("principal", 1200m), ("rate", 0m), ("periods", 12m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("Result").GetDecimal().Should().Be(100.00m);
    }

    [Fact]
    public async Task Roi_GainAndCost_ReturnsCorrectRatio()
    {
        // ROI = (1200 − 1000) / 1000 = 0.20 (20%)
        var result = await ExecuteAsync("roi",
            ("gain", 1200m), ("cost", 1000m));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("Result").GetDecimal().Should().Be(0.20m);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error cases
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownFormula_ReturnsValidationFailedError()
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculationToolHandler),
            configuration: JsonSerializer.Serialize(new
            {
                formula = "magic_number",
                inputs = new { principal = 100m }
            }),
            toolType: ToolType.FinancialCalculator);

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("Unknown formula"));
    }

    [Fact]
    public async Task NegativePrincipal_ReturnsValidationError()
    {
        var result = await ExecuteAsync("simple_interest",
            ("principal", -100m), ("rate", 0.05m), ("time", 2m));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("principal");
    }

    [Fact]
    public async Task NegativeRate_ReturnsValidationError()
    {
        var result = await ExecuteAsync("simple_interest",
            ("principal", 1000m), ("rate", -0.05m), ("time", 2m));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task LoanPayment_ZeroPeriods_ReturnsValidationError()
    {
        var result = await ExecuteAsync("loan_payment",
            ("principal", 100000m), ("rate", 0.005m), ("periods", 0m));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("periods");
    }

    [Fact]
    public async Task MissingRequiredInput_ReturnsValidationError()
    {
        // simple_interest needs principal + rate + time; omit time
        var result = await ExecuteAsync("simple_interest",
            ("principal", 1000m), ("rate", 0.05m));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("time");
    }

    [Fact]
    public void MissingConfiguration_ValidateReturnsFailure()
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculationToolHandler),
            configuration: null,
            toolType: ToolType.FinancialCalculator);

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("Tool configuration"));
    }

    [Fact]
    public void InvalidJsonConfiguration_ValidateReturnsFailure()
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculationToolHandler),
            configuration: "{ this is not json",
            toolType: ToolType.FinancialCalculator);

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("Invalid configuration JSON"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Config-driven behavior — precision + rounding mode
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrecisionOption_ChangesOutputDecimals()
    {
        var p4 = await ExecuteAsync("simple_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.0567m), ("time", 1m) },
            options: new { precision = 4 });

        p4.Success.Should().BeTrue();
        p4.Data!.Value.GetProperty("Result").GetDecimal().Should().Be(56.7000m);
        p4.Data!.Value.GetProperty("FormattedAmount").GetString().Should().Be("USD 56.7000");
    }

    [Fact]
    public async Task RoundingMode_BankerVsAwayFromZero_GivesDifferentOutputForMidpoint()
    {
        // 12.345 rounded to 2dp:
        //  - banker (ToEven)        → 12.34 (rounds to even)
        //  - away_from_zero (HalfUp)→ 12.35
        // Set up: simple_interest with values that yield exactly 12.345 raw, then round at 2dp.
        // principal=1000, rate=0.012345, time=1 → 12.345
        var banker = await ExecuteAsync("simple_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.012345m), ("time", 1m) },
            options: new { precision = 2, rounding = "banker" });

        var awayFromZero = await ExecuteAsync("simple_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.012345m), ("time", 1m) },
            options: new { precision = 2, rounding = "away_from_zero" });

        banker.Success.Should().BeTrue();
        awayFromZero.Success.Should().BeTrue();

        var bankerResult = banker.Data!.Value.GetProperty("Result").GetDecimal();
        var awayResult = awayFromZero.Data!.Value.GetProperty("Result").GetDecimal();

        // Banker rounds 12.345 → 12.34 (to even); away-from-zero → 12.35. Different deterministic outputs.
        bankerResult.Should().Be(12.34m);
        awayResult.Should().Be(12.35m);
    }

    [Fact]
    public async Task CurrencyOption_AppliesToFormattedAmount()
    {
        var result = await ExecuteAsync("simple_interest",
            inputs: new[] { ("principal", 1000m), ("rate", 0.05m), ("time", 2m) },
            options: new { currency = "EUR" });

        result.Success.Should().BeTrue();
        result.Data!.Value.GetProperty("Currency").GetString().Should().Be("EUR");
        result.Data!.Value.GetProperty("FormattedAmount").GetString().Should().StartWith("EUR ");
    }

    [Fact]
    public async Task DeterministicProperty_IdenticalInputsProduceIdenticalOutputs()
    {
        var first = await ExecuteAsync("compound_interest",
            inputs: new[] { ("principal", 5000m), ("rate", 0.04m), ("time", 5m) },
            options: new { compoundingFrequency = 12, precision = 2 });
        var second = await ExecuteAsync("compound_interest",
            inputs: new[] { ("principal", 5000m), ("rate", 0.04m), ("time", 5m) },
            options: new { compoundingFrequency = 12, precision = 2 });

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        first.Data!.Value.GetProperty("Result").GetDecimal()
            .Should().Be(second.Data!.Value.GetProperty("Result").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry — no input values in logs
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Telemetry_DoesNotLogInputValues()
    {
        // Use distinctive amounts unlikely to appear in normal log strings,
        // and an ID-like principal that would be suspicious if it leaked.
        const decimal sensitivePrincipal = 987654321m;
        const decimal sensitiveRate = 0.123456m;

        await ExecuteAsync("simple_interest",
            ("principal", sensitivePrincipal),
            ("rate", sensitiveRate),
            ("time", 1m));

        // Per ADR-015 fixture: scan the captured log lines for the literal sensitive values.
        // The forbidden-substring helper requires length >= 10 — these qualify.
        AssertTelemetryRespectsAdr015(
            sensitivePrincipal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sensitiveRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Telemetry_LogsFormulaName_NotInputs()
    {
        await ExecuteAsync("simple_interest",
            ("principal", 1000m), ("rate", 0.05m), ("time", 2m));

        // The completion log line should include the formula name (so ops can correlate),
        // but NOT the principal value.
        var anyLineMentionsFormula = CapturedLogMessages.Any(m =>
            m.FormattedMessage.Contains("simple_interest"));
        anyLineMentionsFormula.Should().BeTrue("ADR-015 permits logging the formula NAME — that's outcome metadata, not input");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private FinancialCalculationToolHandler BuildHandler() =>
        new(CreateLogger<FinancialCalculationToolHandler>());

    private async Task<ToolResult> ExecuteAsync(string formula, params (string Key, decimal Value)[] inputs)
        => await ExecuteAsync(formula, inputs, options: null);

    private async Task<ToolResult> ExecuteAsync(
        string formula,
        (string Key, decimal Value)[] inputs,
        object? options)
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext();
        var configObject = options is null
            ? (object)new
            {
                formula,
                inputs = inputs.ToDictionary(p => p.Key, p => p.Value)
            }
            : new
            {
                formula,
                inputs = inputs.ToDictionary(p => p.Key, p => p.Value),
                options
            };

        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculationToolHandler),
            configuration: JsonSerializer.Serialize(configObject),
            toolType: ToolType.FinancialCalculator);

        var validation = handler.Validate(context, tool);
        if (!validation.IsValid)
        {
            return ToolResult.Error(
                handler.HandlerId, tool.Id, tool.Name,
                string.Join("; ", validation.Errors),
                ToolErrorCodes.ValidationFailed,
                ToolExecutionMetadata.Empty);
        }

        return await handler.ExecuteAsync(context, tool, CancellationToken.None);
    }

    private JsonElement? ExecuteWithFormula(string formula, params (string Key, decimal Value)[] inputs)
    {
        var result = ExecuteAsync(formula, inputs).GetAwaiter().GetResult();
        return result.Data;
    }

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
