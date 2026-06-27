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
/// Unit tests for the R6 Pillar 2 Wave-1 <see cref="FinancialCalculatorHandler"/> (FR-18).
/// </summary>
/// <remarks>
/// <para>
/// Covers the four binding R6 Pillar 2 contract assertions (registered + discoverable + valid
/// metadata + non-empty supported types) + per-handler positive / error / config-driven /
/// telemetry-compliance cases.
/// </para>
/// <para>
/// Inherits from <see cref="TypedToolHandlerTestFixture"/> for the shared logging-capture
/// scaffolding + the <c>AssertTelemetryRespectsAdr015</c> helper that asserts no monetary
/// values leak into the log surface (ADR-015 binding).
/// </para>
/// <para>
/// <strong>Decimal-only assertion</strong>: every monetary assertion uses <see cref="decimal"/>
/// equality directly so floating-point regressions surface as test failures, not approximations.
/// </para>
/// </remarks>
public sealed class FinancialCalculatorHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // ─────────────────────────────────────────────────────────────────────────────
    // (1) Contract test #1 — registration via auto-discovery (ADR-010).
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();
        var registered = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registered.Should().Contain(
            typeof(FinancialCalculatorHandler),
            because: "auto-discovery in ToolFrameworkExtensions.AddToolHandlersFromAssembly must register the handler with ZERO per-handler DI lines (R6 Pillar 2 + ADR-010)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (2) Contract test #2 — HandlerId routes from sprk_handlerclass.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());

        handler.HandlerId.Should().Be(
            nameof(FinancialCalculatorHandler),
            because: "the Dataverse sprk_handlerclass column routes to this handler via HandlerId == nameof(handler class)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (3) Contract test #3 — metadata well-formed.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        metadata.Parameters.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (4) Contract test #4 — SupportedToolTypes non-empty so registry can index it.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.FinancialCalculator);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (5) Determinism + decimal precision invariant — FR-18 binding.
    //     Asserts `0.1m + 0.2m == 0.3m` exactly (floating-point would fail).
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DecimalPrecision_SumsPointOneAndPointTwoExactly()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 0.10, "currency": "USD" },
                { "amount": 0.20, "currency": "USD" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.Result.Should().Be(
            0.30m,
            because: "FR-18 decimal binding: 0.10 + 0.20 must equal exactly 0.30 (decimal); IEEE-754 double would yield 0.30000000000000004");
        data.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task ExecuteAsync_Determinism_IdenticalInputProducesIdenticalOutput()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 123.45, "currency": "USD" },
                { "amount": 67.89, "currency": "USD" }
              ]
            }
            """);

        var first = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);
        var second = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        ParseData(first).Result.Should().Be(ParseData(second).Result);
        ParseData(first).FormattedAmount.Should().Be(ParseData(second).FormattedAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (6) Positive cases — Sum / Tax / Discount / FxConvert / WeightedAvg / Aggregate.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Sum_AddsAllOperands()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 100.50, "currency": "USD" },
                { "amount": 200.25, "currency": "USD" },
                { "amount": 50.00,  "currency": "USD" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.Result.Should().Be(350.75m);
        data.Currency.Should().Be("USD");
        data.FormattedAmount.Should().Be("USD 350.75");
    }

    [Fact]
    public async Task ExecuteAsync_Tax_AppliesRateToSubtotal()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Tax",
              "rate": 0.0825,
              "operands": [
                { "amount": 100.00, "currency": "USD" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.Subtotal.Should().Be(100.00m);
        data.TaxAmount.Should().Be(8.25m); // 100 * 0.0825 = 8.25 exactly
        data.Result.Should().Be(108.25m);
        data.AppliedRate.Should().Be(0.0825m);
    }

    [Fact]
    public async Task ExecuteAsync_Discount_SubtractsRateFromSubtotal()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Discount",
              "rate": 0.10,
              "operands": [
                { "amount": 250.00, "currency": "EUR" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.DiscountAmount.Should().Be(25.00m);
        data.Result.Should().Be(225.00m);
        data.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task ExecuteAsync_FxConvert_UsesConfiguredRateTable()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "FxConvert",
              "amount": { "amount": 100.00, "currency": "USD" },
              "targetCurrency": "EUR",
              "config": {
                "fxRateTable": { "USD/EUR": 0.92 }
              }
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.Result.Should().Be(92.00m); // 100 * 0.92 = 92.00
        data.Currency.Should().Be("EUR");
        data.SourceCurrency.Should().Be("USD");
        data.AppliedRate.Should().Be(0.92m);
    }

    [Fact]
    public async Task ExecuteAsync_FxConvert_SameCurrencyIsIdentity()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "FxConvert",
              "amount": { "amount": 42.42, "currency": "USD" },
              "targetCurrency": "USD"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        ParseData(result).Result.Should().Be(42.42m);
        ParseData(result).AppliedRate.Should().Be(1m);
    }

    [Fact]
    public async Task ExecuteAsync_WeightedAvg_ComputesWeightedMean()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "WeightedAvg",
              "operands": [
                { "amount": 100.00, "currency": "USD", "weight": 1 },
                { "amount": 200.00, "currency": "USD", "weight": 3 }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        // (100*1 + 200*3) / 4 = 700/4 = 175
        ParseData(result).Result.Should().Be(175.00m);
    }

    [Fact]
    public async Task ExecuteAsync_AggregateByCategory_GroupsAndSums()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "AggregateByCategory",
              "operands": [
                { "amount": 50.00,  "currency": "USD", "category": "Travel" },
                { "amount": 75.00,  "currency": "USD", "category": "Travel" },
                { "amount": 200.00, "currency": "USD", "category": "Lodging" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = ParseData(result);
        data.Result.Should().Be(325.00m);
        data.Buckets.Should().NotBeNull();
        data.Buckets!.Should().HaveCount(2);
        data.Buckets!.Single(b => b.Category == "Travel").Amount.Should().Be(125.00m);
        data.Buckets!.Single(b => b.Category == "Lodging").Amount.Should().Be(200.00m);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (7) Error cases — mixed currency / unknown currency / negative rate / div-by-zero
    //     / missing FX rate / unsupported rounding mode.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RejectsMixedCurrencyOperands()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 100.00, "currency": "USD" },
                { "amount": 50.00,  "currency": "EUR" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("single currency");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnsupportedCurrency()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 100.00, "currency": "XYZ" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("XYZ");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNegativeTaxRate()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Tax",
              "rate": -0.05,
              "operands": [ { "amount": 100.00, "currency": "USD" } ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingFxRate()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "FxConvert",
              "amount": { "amount": 100.00, "currency": "USD" },
              "targetCurrency": "EUR"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("fxRateTable");
    }

    [Fact]
    public async Task ExecuteAsync_WeightedAvg_RejectsZeroTotalWeight()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "WeightedAvg",
              "operands": [
                { "amount": 100.00, "currency": "USD", "weight": 0 },
                { "amount": 200.00, "currency": "USD", "weight": 0 }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("divide by zero");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (8) Config-driven test — rounding mode swap produces different deterministic outputs.
    //     2.125 rounded to 2 dp:
    //       BankersRounding (ToEven)       → 2.12 (tie to nearest even)
    //       MidpointAwayFromZero           → 2.13
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BankersRounding_RoundsTieToEven()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [ { "amount": 2.125, "currency": "USD" } ],
              "config": { "defaultRoundingMode": "BankersRounding" }
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        ParseData(result).Result.Should().Be(2.12m, because: "Banker's rounding ties to the nearest even digit (2.12, not 2.13)");
    }

    [Fact]
    public async Task ExecuteAsync_MidpointAwayFromZero_RoundsTieAway()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [ { "amount": 2.125, "currency": "USD" } ],
              "config": { "defaultRoundingMode": "MidpointAwayFromZero" }
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        ParseData(result).Result.Should().Be(2.13m, because: "MidpointAwayFromZero rounds halves away from zero — opposite Banker's tie-to-even");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnknownRoundingMode()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [ { "amount": 1.00, "currency": "USD" } ],
              "config": { "defaultRoundingMode": "PoliticalFavoriteMode" }
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidConfiguration);
    }

    [Fact]
    public async Task ExecuteAsync_JpyDefaultsToZeroDecimalPrecision()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 1234.56, "currency": "JPY" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        ParseData(result).Result.Should().Be(
            1235m,
            because: "JPY conventionally has 0 fractional digits — the handler defaults the precision per-currency");
        ParseData(result).FormattedAmount.Should().Be("JPY 1235");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (9) Telemetry compliance (ADR-015) — handler must not log monetary values.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Telemetry_RespectsAdr015()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        const decimal sensitiveAmount = 9876543.21m;
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: $$"""
            {
              "operation": "Sum",
              "operands": [
                { "amount": {{sensitiveAmount}}, "currency": "USD" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);
        result.Success.Should().BeTrue();

        // No log message may contain the raw amount or its formatted representation.
        AssertTelemetryRespectsAdr015(
            sensitiveAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "USD 9876543.21");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (10) Validation — Validate() rejects missing TenantId.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsMissingTenantId()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var context = BuildToolExecutionContext(tenantId: "");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: """{"operation":"Sum","operands":[{"amount":1,"currency":"USD"}]}""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId"));
    }

    [Fact]
    public void Validate_RejectsMissingConfiguration()
    {
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            configuration: null);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("configuration JSON"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (11) FR-18 binding — NO LLM dependency on the handler ctor.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_HasNoLlmDependency()
    {
        var ctors = typeof(FinancialCalculatorHandler).GetConstructors();
        ctors.Should().HaveCount(1);

        var ctor = ctors[0];
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        paramTypes.Should().NotContain(typeof(IOpenAiClient),
            because: "FR-18: FinancialCalculatorHandler MUST be pure deterministic — NO Azure OpenAI / LLM dependency");
    }

    [Fact]
    public void SourceFile_HasNoOpenAiReference()
    {
        // Defensive: even if a future refactor accidentally introduces an LLM call
        // (e.g., via a transitive service helper), a string check on the source guards FR-18.
        var sourcePath = ResolveSourcePath("Services/Ai/Handlers/FinancialCalculatorHandler.cs");
        File.Exists(sourcePath).Should().BeTrue(because: "handler source must be present in the repo");

        var contents = File.ReadAllText(sourcePath);
        contents.Should().NotContain("IOpenAiClient",
            because: "FR-18 binding: pure deterministic handler — no LLM client may appear in the source");
        contents.Should().NotContain("OpenAiClient",
            because: "FR-18 binding: pure deterministic handler — no LLM client may appear in the source");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }

    private static ResultPayload ParseData(ToolResult result)
    {
        result.Data.Should().NotBeNull("a successful FinancialCalculatorHandler invocation must produce a structured Data payload");
        var json = result.Data!.Value.GetRawText();
        return JsonSerializer.Deserialize<ResultPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    private static string ResolveSourcePath(string relativeFromRepoRoot)
    {
        // Tests may run from various publish-bin shapes. Walk up looking for the canonical
        // marker `src/server/api/Sprk.Bff.Api/Program.cs`, which uniquely identifies the
        // repo root regardless of test runner layout.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs");
            if (File.Exists(probe))
                return Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api",
                    relativeFromRepoRoot.Replace('/', Path.DirectorySeparatorChar));
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root from test base directory '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Mirror of <c>FinancialCalculatorHandler.CalculatorResult</c> for deserialization in tests.
    /// (We don't expose the internal DTO publicly.)
    /// </summary>
    private sealed class ResultPayload
    {
        public string Operation { get; set; } = string.Empty;
        public decimal Result { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string FormattedAmount { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public decimal? Subtotal { get; set; }
        public decimal? TaxAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? AppliedRate { get; set; }
        public string? SourceCurrency { get; set; }
        public List<CategoryBucketPayload>? Buckets { get; set; }
    }

    private sealed class CategoryBucketPayload
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string FormattedAmount { get; set; } = string.Empty;
    }
}
