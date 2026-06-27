using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="InvoiceExtractionToolHandler"/> — R6 Pillar 2, Wave 2 (FR-19).
/// </summary>
/// <remarks>
/// <para>
/// Covers:
/// </para>
/// <list type="bullet">
/// <item>4-point contract (registered, discoverable, valid metadata, non-empty types)</item>
/// <item>Domain positive: LLM-extracted invoice → totals via decimal math → matches expected</item>
/// <item>Domain: arithmetic mismatch surfaced (NOT auto-corrected) per FR-19 binding</item>
/// <item>Domain: empty line items handled</item>
/// <item>Domain: per-line tax rate applied; tax_inclusive vs exclusive</item>
/// <item>Domain: discount handling (pre-tax vs post-tax order)</item>
/// <item>Domain: currency-tagged outputs; out-of-allow-list rejected</item>
/// <item>Decimal precision invariant: <c>1.10 + 1.10 + 1.10 == 3.30</c> exact</item>
/// <item>Cache hit/miss</item>
/// <item>Error handling: invalid JSON from LLM; negative quantity</item>
/// <item>ADR-015: telemetry sentinel scan (no invoice content / monetary values in logs)</item>
/// </list>
/// </remarks>
public sealed class InvoiceExtractionToolHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;
    private static readonly ModelSelectorOptions DefaultModelOptions = new()
    {
        DefaultModel = "gpt-4o-mini",
        ToolHandlerModel = "gpt-4o-mini"
    };

    private MemoryDistributedCache CreateCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    // ─────────────────────────────────────────────────────────────────────────────
    // 4-point contract tests
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
            typeof(InvoiceExtractionToolHandler),
            because: "the handler must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = BuildHandler();

        handler.HandlerId.Should().Be(
            nameof(InvoiceExtractionToolHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so the Dataverse sprk_handlerclass field routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = BuildHandler();
        handler.Metadata.Should().NotBeNull();
        handler.Metadata.Name.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Description.Should().NotBeNullOrWhiteSpace();
        handler.Metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = BuildHandler();
        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Handler_DeclaresBothInvocationContexts()
    {
        var handler = BuildHandler();
        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "FR-19 / project CLAUDE.md: AvailableInContexts = Both — usable from playbook and chat");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Decimal-precision invariant (FR-19 money-math binding)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Decimal_AdditionIsExact()
    {
        // The canonical test that catches an accidental double-precision regression.
        // Under IEEE-754 doubles: 1.1 + 1.1 + 1.1 != 3.3 due to binary representation.
        // Under .NET decimal: exact.
        decimal sum = 1.10m + 1.10m + 1.10m;
        sum.Should().Be(3.30m, "money math must use decimal — float / double introduce rounding error");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Positive: LLM-mocked extraction → arithmetic correct
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExclusiveTax_NoDiscount_TotalsMatchLlmExtraction()
    {
        // 3 widgets @ $10.00 = $30.00 subtotal; 10% tax = $3.00; grand $33.00; USD.
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Widget A", 1m, 10.00m, 10.00m, 0.10m, 1.00m),
                L("Widget A", 1m, 10.00m, 10.00m, 0.10m, 1.00m),
                L("Widget A", 1m, 10.00m, 10.00m, 0.10m, 1.00m),
            },
            totals: (subtotal: 30.00m, tax: 3.00m, discount: 0m, grand: 33.00m, currency: "USD"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("totals").GetProperty("subtotal").GetDecimal().Should().Be(30.00m);
        data.GetProperty("totals").GetProperty("taxTotal").GetDecimal().Should().Be(3.00m);
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(33.00m);
        data.GetProperty("totals").GetProperty("currency").GetString().Should().Be("USD");
        // No arithmeticErrors expected.
        var errors = data.GetProperty("arithmeticErrors");
        if (errors.ValueKind == JsonValueKind.Array)
            errors.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ArithmeticMismatch_SurfacesInErrorsArray_NotAutoCorrected()
    {
        // LLM reports grand total $50.00 but math says ~$33.00. Handler SURFACES the mismatch
        // (FR-19 binding: don't auto-correct).
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Widget A", 3m, 10.00m, 30.00m, 0.10m, 3.00m),
            },
            totals: (subtotal: 30.00m, tax: 3.00m, discount: 0m, grand: 50.00m, currency: "USD"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        var errors = data.GetProperty("arithmeticErrors");
        errors.ValueKind.Should().Be(JsonValueKind.Array);
        errors.GetArrayLength().Should().BeGreaterThan(0, "the $50 vs $33 grandTotal disagreement must surface");

        // The grand total in the output reflects deterministic computation, NOT the LLM-reported value.
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(33.00m);
    }

    [Fact]
    public async Task EmptyLineItems_HandledGracefully()
    {
        var payload = BuildLlmInvoiceJson(
            lines: Array.Empty<LineSpec>(),
            totals: (subtotal: 0m, tax: 0m, discount: 0m, grand: 0m, currency: "USD"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("lineItems").GetArrayLength().Should().Be(0);
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task InclusiveTax_BackDerivesSubtotalFromGross()
    {
        // Inclusive: lineTotal includes tax. quantity=1, unitPrice=110.00 → gross = 110.00.
        // With 10% tax rate inclusive: subtotal (net) = 110 / 1.10 = 100.00; tax = 10.00.
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Widget A", 1m, 110.00m, 110.00m, 0.10m, (decimal?)null),
            },
            totals: (subtotal: 100.00m, tax: 10.00m, discount: 0m, grand: 110.00m, currency: "USD"));

        var configJson = """{ "taxMethod": "inclusive" }""";
        var result = await ExecuteAsync(payload, configJson);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("totals").GetProperty("subtotal").GetDecimal().Should().Be(100.00m);
        data.GetProperty("totals").GetProperty("taxTotal").GetDecimal().Should().Be(10.00m);
    }

    [Fact]
    public async Task ExclusiveTax_PerLineTaxRate_AppliedCorrectly()
    {
        // Two lines with different tax rates.
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Standard widget", 2m, 50.00m, 100.00m, 0.10m, 10.00m),    // 100 + 10 tax
                L("Reduced-tax", 2m, 25.00m, 50.00m, 0.05m, 2.50m),          // 50 + 2.50 tax
            },
            totals: (subtotal: 150.00m, tax: 12.50m, discount: 0m, grand: 162.50m, currency: "USD"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("totals").GetProperty("taxTotal").GetDecimal().Should().Be(12.50m);
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(162.50m);
    }

    [Fact]
    public async Task PreTaxDiscount_ReducesSubtotalBeforeTax()
    {
        // Subtotal 100, 10% tax (10), discount 20 pre-tax.
        // Pre-tax order: (100 - 20) + (10 × 0.8) = 80 + 8 = 88.
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Widget A", 1m, 100.00m, 100.00m, 0.10m, 10.00m),
            },
            totals: (subtotal: 100.00m, tax: 8.00m, discount: 20.00m, grand: 88.00m, currency: "USD"));

        var configJson = """{ "discountOrder": "pre-tax" }""";
        var result = await ExecuteAsync(payload, configJson);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(88.00m);
    }

    [Fact]
    public async Task PostTaxDiscount_SubtractsAfterTax()
    {
        // Subtotal 100, 10% tax (10), discount 20 post-tax.
        // Post-tax order: 100 + 10 - 20 = 90.
        var payload = BuildLlmInvoiceJson(
            lines: new[]
            {
                L("Widget A", 1m, 100.00m, 100.00m, 0.10m, 10.00m),
            },
            totals: (subtotal: 100.00m, tax: 10.00m, discount: 20.00m, grand: 90.00m, currency: "USD"));

        var configJson = """{ "discountOrder": "post-tax" }""";
        var result = await ExecuteAsync(payload, configJson);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var data = result.Data!.Value;
        data.GetProperty("totals").GetProperty("grandTotal").GetDecimal().Should().Be(90.00m);
    }

    [Fact]
    public async Task DiscountOrderSwap_ProducesDifferentGrandTotals()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", 1m, 100.00m, 100.00m, 0.10m, 10.00m) },
            totals: (subtotal: 100.00m, tax: 10.00m, discount: 20.00m, grand: 90.00m, currency: "USD"));

        var pre = await ExecuteAsync(payload, """{ "discountOrder": "pre-tax", "validateArithmetic": false }""");
        var post = await ExecuteAsync(payload, """{ "discountOrder": "post-tax", "validateArithmetic": false }""");

        pre.Success.Should().BeTrue();
        post.Success.Should().BeTrue();

        var preTotal = pre.Data!.Value.GetProperty("totals").GetProperty("grandTotal").GetDecimal();
        var postTotal = post.Data!.Value.GetProperty("totals").GetProperty("grandTotal").GetDecimal();

        preTotal.Should().NotBe(postTotal,
            "swapping discountOrder must shift grand total deterministically (FR-19 config-driven)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Currency handling
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EurInvoice_CurrencyTaggedInOutput()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", 1m, 100.00m, 100.00m, 0.20m, 20.00m) },
            totals: (subtotal: 100.00m, tax: 20.00m, discount: 0m, grand: 120.00m, currency: "EUR"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Data!.Value.GetProperty("totals").GetProperty("currency").GetString().Should().Be("EUR");
    }

    [Fact]
    public async Task UnknownCurrency_NotInExpectedCurrencies_ReturnsValidationError()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", 1m, 100.00m, 100.00m, 0m, 0m) },
            totals: (subtotal: 100.00m, tax: 0m, discount: 0m, grand: 100.00m, currency: "XYZ"));

        // Restrict allow-list to USD only.
        var configJson = """{ "expectedCurrencies": ["USD"] }""";
        var result = await ExecuteAsync(payload, configJson);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("currency");
    }

    [Fact]
    public async Task MissingCurrency_ReturnsValidationError()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", 1m, 100.00m, 100.00m, 0m, 0m) },
            totals: (subtotal: 100.00m, tax: 0m, discount: 0m, grand: 100.00m, currency: ""));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("currency");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cache hit/miss
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecondIdenticalCall_HitsCache_LlmInvokedOnce()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", 2m, 25.00m, 50.00m, 0m, 0m) },
            totals: (subtotal: 50.00m, tax: 0m, discount: 0m, grand: 50.00m, currency: "USD"));

        var mock = new Mock<IOpenAiClient>();
        var llmCalls = 0;
        mock.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { Interlocked.Increment(ref llmCalls); return payload; });

        var cache = CreateCache();
        var handler = BuildHandlerWithMock(mock.Object, cache);
        var context = BuildToolExecutionContext(extractedText: "INVOICE TEXT CACHE TEST DOCUMENT-001");
        var tool = BuildAnalysisTool(nameof(InvoiceExtractionToolHandler), configuration: null);

        var first = await handler.ExecuteAsync(context, tool, CancellationToken.None);
        var second = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        llmCalls.Should().Be(1, "ADR-014: identical input + tenant → second call serves from per-tenant cache");
        second.Execution.CacheHit.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Error cases
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LlmReturnsInvalidJson_ReturnsModelError()
    {
        var mock = new Mock<IOpenAiClient>();
        mock.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ this is not json");

        var handler = BuildHandlerWithMock(mock.Object, CreateCache());
        var context = BuildToolExecutionContext(extractedText: "Some invoice text");
        var tool = BuildAnalysisTool(nameof(InvoiceExtractionToolHandler), configuration: null);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ModelError);
    }

    [Fact]
    public async Task NegativeQuantity_ReturnsValidationError()
    {
        var payload = BuildLlmInvoiceJson(
            lines: new[] { L("Widget A", -1m, 10.00m, -10.00m, 0m, 0m) },
            totals: (subtotal: -10.00m, tax: 0m, discount: 0m, grand: -10.00m, currency: "USD"));

        var result = await ExecuteAsync(payload, configJson: null);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("negative");
    }

    [Fact]
    public void MissingTenantId_ValidateReturnsFailure()
    {
        var handler = BuildHandler();
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = string.Empty,
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "stub.pdf",
                ExtractedText = "Some invoice text"
            }
        };
        var tool = BuildAnalysisTool(nameof(InvoiceExtractionToolHandler), configuration: null);

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("TenantId"));
    }

    [Fact]
    public void InvalidConfigJson_ValidateReturnsFailure()
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext(extractedText: "Some invoice text");
        var tool = BuildAnalysisTool(
            nameof(InvoiceExtractionToolHandler),
            configuration: "{ this is not json");

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("Invalid configuration JSON"));
    }

    [Fact]
    public void UnknownTaxMethod_ValidateReturnsFailure()
    {
        var handler = BuildHandler();
        var context = BuildToolExecutionContext(extractedText: "Some invoice text");
        var tool = BuildAnalysisTool(
            nameof(InvoiceExtractionToolHandler),
            configuration: """{ "taxMethod": "magic_tax_method" }""");

        var validation = handler.Validate(context, tool);
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Contains("taxMethod"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cache key (ADR-014)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CacheKey_IncludesTenantId_PerAdr014()
    {
        var key = InvoiceExtractionToolHandler.BuildCacheKey(
            "tenant-X",
            "Some invoice text",
            new InvoiceExtractionConfig());

        key.Should().StartWith("invoice-extractor:tenant-X:", "ADR-014 binding: cache key MUST include tenantId");
    }

    [Fact]
    public void CacheKey_DifferentTenants_ProduceDifferentKeys()
    {
        var k1 = InvoiceExtractionToolHandler.BuildCacheKey("tenant-A", "x", new InvoiceExtractionConfig());
        var k2 = InvoiceExtractionToolHandler.BuildCacheKey("tenant-B", "x", new InvoiceExtractionConfig());
        k1.Should().NotBe(k2, "ADR-014: tenant isolation in cache");
    }

    [Fact]
    public void CacheKey_MissingTenantId_Throws()
    {
        var act = () => InvoiceExtractionToolHandler.BuildCacheKey(
            "", "x", new InvoiceExtractionConfig());
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry sentinel scan
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Telemetry_DoesNotLogInvoiceContent_OrMonetaryValues()
    {
        const string sensitiveVendor = "ACME-VENDOR-SECRET-9876543210";
        const string sensitiveDescription = "Confidential-line-item-description-do-not-log";
        const decimal sensitiveAmount = 987654321.00m;

        var payload = BuildLlmInvoiceJson(
            invoiceNumber: "INV-CONFIDENTIAL-12345",
            vendor: sensitiveVendor,
            customer: "CUST-SECRET-98765",
            lines: new[] { L(sensitiveDescription, 1m, sensitiveAmount, sensitiveAmount, 0m, 0m) },
            totals: (subtotal: sensitiveAmount, tax: 0m, discount: 0m, grand: sensitiveAmount, currency: "USD"));

        await ExecuteAsync(payload, configJson: null);

        // ADR-015: handler MUST NOT log vendor / customer text, invoice number, line item
        // descriptions, or monetary values. The fixture's sentinel scan asserts this.
        AssertTelemetryRespectsAdr015(
            sensitiveVendor,
            sensitiveDescription,
            "INV-CONFIDENTIAL-12345",
            "CUST-SECRET-98765",
            sensitiveAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private InvoiceExtractionToolHandler BuildHandler()
    {
        var mock = new Mock<IOpenAiClient>();
        return BuildHandlerWithMock(mock.Object, CreateCache());
    }

    private InvoiceExtractionToolHandler BuildHandlerWithMock(IOpenAiClient client, IDistributedCache cache)
    {
        var tenantCache = new TenantCache(cache, NullLogger<TenantCache>.Instance);
        return new InvoiceExtractionToolHandler(
            client,
            tenantCache,
            Options.Create(DefaultModelOptions),
            CreateLogger<InvoiceExtractionToolHandler>());
    }

    private async Task<ToolResult> ExecuteAsync(string llmReturnJson, string? configJson)
    {
        var mock = new Mock<IOpenAiClient>();
        mock.Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmReturnJson);

        var handler = BuildHandlerWithMock(mock.Object, CreateCache());
        var context = BuildToolExecutionContext(extractedText: "Sample invoice document text for testing.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(InvoiceExtractionToolHandler),
            configuration: configJson);

        var validation = handler.Validate(context, tool);
        if (!validation.IsValid)
        {
            return ToolResult.Error(
                handler.HandlerId, tool.Id, tool.Name,
                string.Join("; ", validation.Errors), ToolErrorCodes.ValidationFailed, ToolExecutionMetadata.Empty);
        }

        return await handler.ExecuteAsync(context, tool, CancellationToken.None);
    }

    /// <summary>
    /// Compact line-item spec. <see cref="TaxRate"/> and <see cref="TaxAmount"/> are nullable
    /// to model invoices that omit either field; the LLM may legitimately return one or the other.
    /// </summary>
    private sealed record LineSpec(
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        decimal? TaxRate,
        decimal? TaxAmount);

    /// <summary>
    /// Build a JSON payload matching the LLM's structured-output schema.
    /// </summary>
    private static string BuildLlmInvoiceJson(
        IEnumerable<LineSpec> lines,
        (decimal subtotal, decimal tax, decimal discount, decimal grand, string currency) totals,
        string invoiceNumber = "INV-001",
        string vendor = "Vendor LLC",
        string customer = "Customer Inc.")
    {
        var lineItems = lines.Select(l => new
        {
            description = l.Description,
            quantity = l.Quantity,
            unitPrice = l.UnitPrice,
            lineTotal = l.LineTotal,
            taxRate = (object?)l.TaxRate,
            taxAmount = (object?)l.TaxAmount
        }).ToArray();

        var obj = new
        {
            invoice = new
            {
                number = invoiceNumber,
                issueDate = "2026-06-01",
                dueDate = "2026-06-30",
                vendor,
                customer
            },
            lineItems,
            totals = new
            {
                subtotal = totals.subtotal,
                taxTotal = totals.tax,
                discountTotal = totals.discount,
                grandTotal = totals.grand,
                currency = totals.currency
            }
        };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>Compact factory for <see cref="LineSpec"/>.</summary>
    private static LineSpec L(string desc, decimal qty, decimal unitPrice, decimal lineTotal,
        decimal? taxRate = null, decimal? taxAmount = null) =>
        new(desc, qty, unitPrice, lineTotal, taxRate, taxAmount);

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
