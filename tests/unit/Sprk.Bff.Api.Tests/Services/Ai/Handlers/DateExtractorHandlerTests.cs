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
/// Unit tests for <see cref="DateExtractorHandler"/> (R6 Pillar 2, FR-17, Wave 1).
/// </summary>
/// <remarks>
/// <para>
/// Inherits from <see cref="TypedToolHandlerTestFixture"/> for shared mocks + ADR-015 telemetry
/// assertions. Includes the 4 contract tests from <c>HandlerContractTestTemplate</c> + per-handler
/// tests covering: positive cases (ISO 8601, Q-notation, named months, US/EU short formats,
/// relative phrases), error cases (empty input, malformed config, missing TenantId),
/// configuration-driven behavior (referenceDate shift, confidenceThreshold filter, locale),
/// determinism, and ADR-015 telemetry compliance.
/// </para>
/// </remarks>
public sealed class DateExtractorHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private DateExtractorHandler CreateHandler() => new(CreateLogger<DateExtractorHandler>());

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (copy of HandlerContractTestTemplate, retargeted)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(DateExtractorHandler),
            because: "the handler type must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(DateExtractorHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so the Dataverse sprk_handlerclass field routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = CreateHandler();
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = CreateHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.DateExtractor);
    }

    [Fact]
    public void SupportedInvocationContexts_IncludesBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "FR-17 requires DateExtractorHandler to be invocable from both playbook orchestration and chat-driven function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // FR-17 binding: pure-deterministic (NO LLM / Azure OpenAI dependency)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_DoesNotRequireOpenAiClient()
    {
        // FR-17 binding: pure-deterministic — no LLM dependency in the constructor signature.
        var ctor = typeof(DateExtractorHandler).GetConstructors().Single();
        var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        parameterTypes.Should().NotContain(
            typeof(IOpenAiClient),
            because: "FR-17 forbids LLM dependency; DateExtractorHandler is pure-deterministic");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContextAndConfig()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "March 15 contract effective.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExtractedTextIsEmpty()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("extracted text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text", tenantId: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfigReferenceDateIsMalformed()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"not-a-date\"}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("referenceDate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfigLocaleIsUnsupported()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"locale\":\"JP\"}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("locale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfigConfidenceThresholdOutOfRange()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"confidenceThreshold\":1.5}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("confidenceThreshold", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat (chat invocation context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithTextArgument()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"Q3 2026\"}");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenToolArgumentsJsonIsMissingText()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenToolArgumentsJsonIsMalformed()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — positive cases (playbook path)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ExtractsIso8601Date()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "The deadline is 2026-03-15.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().ContainSingle();
        dates[0].NormalizedDate.Should().Be("2026-03-15");
        dates[0].Confidence.Should().Be(1.0);
        dates[0].Kind.Should().Be("absolute");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsQuarterNotation()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Filing scheduled for Q4 2026.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().ContainSingle();
        dates[0].NormalizedDate.Should().Be("2026-10-01"); // Q4 2026 starts October
        dates[0].Kind.Should().Be("range");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsNamedMonthWithExplicitYear()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Hearing on March 15, 2026.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-03-15" && d.Confidence == 0.95);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsNamedMonthInfersYearFromReferenceDate()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Hearing on March 15.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2027-01-15T00:00:00Z\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        // Year inferred from referenceDate=2027
        dates.Should().Contain(d => d.NormalizedDate == "2027-03-15");
        dates.First(d => d.NormalizedDate == "2027-03-15").Confidence.Should().Be(0.75);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsRelativePhraseTomorrow()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Filing due tomorrow.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-03-15T00:00:00Z\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-03-16" && d.Kind == "relative");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsRelativePhraseInThreeWeeks()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Response in 3 weeks.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-03-15T00:00:00Z\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-04-05" && d.Kind == "relative");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsRelativeNextQuarter()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Plan for next quarter.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-04-15T00:00:00Z\"}"); // Q2 2026 → next quarter is Q3

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-07-01" && d.Kind == "relative");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractsUsLocaleShortDate()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Effective 03/15/2026.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"locale\":\"US\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-03-15");
    }

    [Fact]
    public async Task ExecuteAsync_LocaleDisambiguatesAmbiguousShortDate()
    {
        var handlerUs = CreateHandler();
        var handlerEu = CreateHandler();
        var contextUs = BuildToolExecutionContext(extractedText: "Filed 01/02/2026.");
        var contextEu = BuildToolExecutionContext(extractedText: "Filed 01/02/2026.");
        var toolUs = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"locale\":\"US\"}");
        var toolEu = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"locale\":\"EU\"}");

        var resultUs = await handlerUs.ExecuteAsync(contextUs, toolUs, CancellationToken.None);
        var resultEu = await handlerEu.ExecuteAsync(contextEu, toolEu, CancellationToken.None);

        var datesUs = GetExtractedDates(resultUs);
        var datesEu = GetExtractedDates(resultEu);

        datesUs.Should().Contain(d => d.NormalizedDate == "2026-01-02"); // US: Jan 2
        datesEu.Should().Contain(d => d.NormalizedDate == "2026-02-01"); // EU: Feb 1
    }

    [Fact]
    public async Task ExecuteAsync_HandlesMultipleDatesInOrder()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(
            extractedText: "Effective 2026-01-15, expires 2026-12-31, with review in Q3 2026.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().HaveCountGreaterThanOrEqualTo(3);

        // Ordered by appearance (StartIndex)
        dates[0].NormalizedDate.Should().Be("2026-01-15");
        dates[1].NormalizedDate.Should().Be("2026-12-31");
        dates[2].NormalizedDate.Should().Be("2026-07-01"); // Q3 start
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — configuration-driven behavior
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReferenceDateShiftsRelativeOutput()
    {
        var handler1 = CreateHandler();
        var handler2 = CreateHandler();
        var ctx1 = BuildToolExecutionContext(extractedText: "Filing in 30 days.");
        var ctx2 = BuildToolExecutionContext(extractedText: "Filing in 30 days.");
        var tool1 = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-01-01T00:00:00Z\"}");
        var tool2 = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-06-01T00:00:00Z\"}");

        var r1 = await handler1.ExecuteAsync(ctx1, tool1, CancellationToken.None);
        var r2 = await handler2.ExecuteAsync(ctx2, tool2, CancellationToken.None);

        var d1 = GetExtractedDates(r1);
        var d2 = GetExtractedDates(r2);

        d1.Should().Contain(d => d.NormalizedDate == "2026-01-31");
        d2.Should().Contain(d => d.NormalizedDate == "2026-07-01");
    }

    [Fact]
    public async Task ExecuteAsync_ConfidenceThresholdFiltersResults()
    {
        var handler = CreateHandler();
        // "March 15" inferred year → confidence 0.75; "2026-03-15" ISO → confidence 1.0
        var context = BuildToolExecutionContext(extractedText: "March 15 and 2026-03-15.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"confidenceThreshold\":0.9}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().OnlyContain(d => d.Confidence >= 0.9);
        dates.Should().Contain(d => d.NormalizedDate == "2026-03-15" && d.Confidence == 1.0);
        dates.Should().NotContain(d => d.Confidence < 0.9);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — error cases
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenInputHasNoDates()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "This text contains nothing date-like at all.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().BeEmpty();
        result.Confidence.Should().Be(0.0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "March 15.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteAsync(context, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresMalformedIsoDate()
    {
        var handler = CreateHandler();
        // "2026-13-45" — invalid month + day; regex rejects it
        var context = BuildToolExecutionContext(extractedText: "Date: 2026-13-45.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Determinism + chat path + ADR-015 telemetry
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ProducesIdenticalOutputForIdenticalInput()
    {
        var handler1 = CreateHandler();
        var handler2 = CreateHandler();
        const string input = "Filing in Q3 2026, then again on March 15, 2027, with a follow-up in 30 days.";
        var ctx1 = BuildToolExecutionContext(extractedText: input);
        var ctx2 = BuildToolExecutionContext(extractedText: input);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            configuration: "{\"referenceDate\":\"2026-01-01T00:00:00Z\"}");

        var r1 = await handler1.ExecuteAsync(ctx1, tool, CancellationToken.None);
        var r2 = await handler2.ExecuteAsync(ctx2, tool, CancellationToken.None);

        var d1 = GetExtractedDates(r1);
        var d2 = GetExtractedDates(r2);

        d1.Should().HaveCount(d2.Count);
        for (int i = 0; i < d1.Count; i++)
        {
            d1[i].OriginalText.Should().Be(d2[i].OriginalText);
            d1[i].NormalizedDate.Should().Be(d2[i].NormalizedDate);
            d1[i].Confidence.Should().Be(d2[i].Confidence);
            d1[i].Kind.Should().Be(d2[i].Kind);
            d1[i].StartIndex.Should().Be(d2[i].StartIndex);
        }
    }

    [Fact]
    public async Task ExecuteChatAsync_ExtractsDatesFromTextArgument()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"Effective 2026-03-15.\"}");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dates = GetExtractedDates(result);
        dates.Should().Contain(d => d.NormalizedDate == "2026-03-15");
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_ForExecuteAsync()
    {
        var handler = CreateHandler();
        const string secretInput = "Effective 2026-03-15 for ConfidentialClientName-12345 with privileged content.";
        var context = BuildToolExecutionContext(extractedText: secretInput);
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // ADR-015: input text content (and any secret-like substring) must NEVER appear in logs.
        AssertTelemetryRespectsAdr015(
            "ConfidentialClientName-12345",
            "privileged content",
            secretInput);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_ForExecuteChatAsync()
    {
        var handler = CreateHandler();
        const string secretInput = "Effective 2026-03-15 for ConfidentialClientName-12345 with privileged content.";
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = secretInput }));
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            "ConfidentialClientName-12345",
            "privileged content",
            secretInput);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsZeroModelCalls()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Effective 2026-03-15.");
        var tool = BuildAnalysisTool(handlerClass: nameof(DateExtractorHandler), toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // FR-17 binding: pure-deterministic — zero LLM calls
        result.Execution.ModelCalls.Should().Be(0);
        result.Execution.ModelName.Should().BeNull();
        result.Execution.InputTokens.Should().BeNull();
        result.Execution.OutputTokens.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<DateExtractorHandler.ExtractedDate> GetExtractedDates(ToolResult result)
    {
        result.Data.Should().NotBeNull("ToolResult.Data must be populated on success");
        var parsed = result.GetData<DateExtractorHandler.DateExtractionResult>();
        parsed.Should().NotBeNull();
        return parsed!.Dates;
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
