using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="RiskDetectorHandler"/> (R6 Pillar 2, FR-15, Wave 2).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; positive (mixed categories → deterministic severity scoring);
/// error (invalid LLM JSON, internal error); config-driven (riskCategories filter, severityLevels
/// filter, categoryWeights override); cache hit/miss (second identical call doesn't re-invoke LLM);
/// telemetry (no input text or risk descriptions in logs per ADR-015); chat path; per-tenant
/// cache isolation (ADR-014); deterministic severity property (same LLM output → byte-identical
/// severity output).
/// </remarks>
public sealed class RiskDetectorHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly Dictionary<string, byte[]> _cacheStore = new(StringComparer.Ordinal);

    public RiskDetectorHandlerTests()
    {
        // Wire the cache mock to an in-memory store so we can verify hit/miss behavior.
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _cacheStore.TryGetValue(key, out var bytes) ? bytes : null!);

        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, byte[] bytes, DistributedCacheEntryOptions _, CancellationToken _) =>
            {
                _cacheStore[key] = bytes;
                return Task.CompletedTask;
            });
    }

    private RiskDetectorHandler CreateHandler() => new(
        OpenAiClientMock.Object,
        _cacheMock.Object,
        Options.Create(new ModelSelectorOptions
        {
            ToolHandlerModel = "gpt-4o-mini",
            DefaultModel = "gpt-4o-mini"
        }),
        CreateLogger<RiskDetectorHandler>());

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (per HandlerContractTestTemplate, retargeted)
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
            typeof(RiskDetectorHandler),
            because: "auto-discovery in ToolFrameworkExtensions.AddToolHandlersFromAssembly must register the handler with ZERO per-handler DI lines (R6 Pillar 2 + ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(RiskDetectorHandler),
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
        handler.SupportedToolTypes.Should().Contain(ToolType.RiskDetector);
    }

    [Fact]
    public void SupportedInvocationContexts_IncludesBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "FR-15 requires RiskDetectorHandler to be invocable from both playbook orchestration and chat-driven function calling (AvailableInContexts = Both)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContext()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Force-majeure clause is missing.");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExtractedTextIsEmpty()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("extracted text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text", tenantId: "");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenUnsupportedRiskCategoryInConfig()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"riskCategories\":[\"chemical\"]}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chemical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenUnsupportedSeverityLevelInConfig()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"severityLevels\":[\"showstopper\"]}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("showstopper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenConfidenceThresholdOutOfRange()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"confidenceThreshold\":1.5}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("confidenceThreshold", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Fails_WhenCategoryWeightOutOfRange()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"categoryWeights\":{\"legal\":1.5}}");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("categoryWeights", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat (chat invocation context)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithTextArgument()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{\"text\":\"Contract has missing force-majeure clause.\"}");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTextMissing()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildAnalysisTool(handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — positive (LLM identifies risks → code assigns severity)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_IdentifiesMixedCategoryRisks_AssignsSeveritiesDeterministically()
    {
        // LLM returns 3 risks across legal / financial / operational. Code-side rubric:
        // legal weight=1.0 × conf=0.95 = 0.95 → critical
        // financial weight=0.9 × conf=0.65 = 0.585 → high
        // operational weight=0.7 × conf=0.6 = 0.42 → medium
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Missing indemnification clause exposes party to unlimited liability.", "legal", 0.95, 0, 50),
            ("Payment terms allow indefinite extension without penalty.", "financial", 0.65, 51, 100),
            ("Delivery schedule lacks defined milestones.", "operational", 0.60, 101, 150)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 200));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Stats.TotalCount.Should().Be(3);
        data.Stats.ByCategory.Should().ContainKeys("legal", "financial", "operational");
        data.Stats.BySeverity.Should().ContainKey("critical").WhoseValue.Should().Be(1);
        data.Stats.BySeverity.Should().ContainKey("high").WhoseValue.Should().Be(1);
        data.Stats.BySeverity.Should().ContainKey("medium").WhoseValue.Should().Be(1);

        // Verify per-risk severity assignment matches the rubric exactly
        var legal = data.Risks.Single(r => r.Category == "legal");
        legal.Severity.Should().Be("critical");
        legal.Score.Should().BeApproximately(0.95, 0.001);

        var financial = data.Risks.Single(r => r.Category == "financial");
        financial.Severity.Should().Be("high");
        financial.Score.Should().BeApproximately(0.585, 0.001);

        var operational = data.Risks.Single(r => r.Category == "operational");
        operational.Severity.Should().Be("medium");
        operational.Score.Should().BeApproximately(0.42, 0.001);
    }

    [Fact]
    public async Task ExecuteAsync_OutputIsOrderedBySeverityDescending()
    {
        // LLM emits in random order; output must be ordered critical → high → medium → low.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Operational risk A.", "operational", 0.6, 0, 10),       // medium (0.42)
            ("Legal risk B.", "legal", 0.95, 11, 20),                  // critical (0.95)
            ("Financial risk C.", "financial", 0.65, 21, 30)           // high (0.585)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 50));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Risks.Should().HaveCount(3);
        data.Risks[0].Severity.Should().Be("critical");
        data.Risks[1].Severity.Should().Be("high");
        data.Risks[2].Severity.Should().Be("medium");
    }

    [Fact]
    public async Task ExecuteAsync_FiltersBelowConfidenceThreshold()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("High-confidence legal risk.", "legal", 0.95, 0, 30),
            ("Low-confidence noise.", "operational", 0.40, 31, 50) // below default 0.6
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 60));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Risks.Should().HaveCount(1);
        data.Risks[0].Category.Should().Be("legal");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigRiskCategoriesFilter_KeepsOnlySubset()
    {
        // LLM returns 3 categories; config restricts to legal only.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 10),
            ("Financial risk.", "financial", 0.9, 11, 20),
            ("Operational risk.", "operational", 0.9, 21, 30)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 50));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"riskCategories\":[\"legal\"]}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Risks.Should().OnlyContain(r => r.Category == "legal");
        data.Risks.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_ConfigSeverityLevelsFilter_DropsBelowThreshold()
    {
        // 3 risks at low / medium / high; config restricts to "high" + "critical" only.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal critical.", "legal", 0.95, 0, 10),         // critical (0.95)
            ("Reputational med.", "reputational", 0.65, 11, 20), // medium (0.455 → medium)
            ("Operational low.", "operational", 0.7, 21, 30)    // medium (0.49 → medium)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 50));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"severityLevels\":[\"high\",\"critical\"]}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Risks.Should().OnlyContain(r => r.Severity == "high" || r.Severity == "critical");
        data.Risks.Should().ContainSingle();
        data.Risks[0].Severity.Should().Be("critical");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigCategoryWeights_OverrideDefaults_AffectsSeverity()
    {
        // Override operational weight from 0.7 → 1.0; same confidence 0.7 now should yield
        // high (0.7 × 1.0 = 0.7) instead of medium (0.7 × 0.7 = 0.49).
        // Test pivots on the high/medium boundary (0.5) — moves a risk from medium to high
        // because of the weight override.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Operational risk.", "operational", 0.7, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var contextDefault = BuildToolExecutionContext(extractedText: new string('a', 30));
        var contextOverride = BuildToolExecutionContext(extractedText: new string('a', 30));
        var toolDefault = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector);
        var toolOverride = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"categoryWeights\":{\"operational\":1.0}}");

        var defaultResult = await handler.ExecuteAsync(contextDefault, toolDefault, CancellationToken.None);
        // Clear cache to force re-evaluation under the override config.
        _cacheStore.Clear();
        var overrideResult = await handler.ExecuteAsync(contextOverride, toolOverride, CancellationToken.None);

        var defaultData = GetData(defaultResult);
        var overrideData = GetData(overrideResult);

        defaultData.Risks.Should().ContainSingle();
        defaultData.Risks[0].Severity.Should().Be("medium",
            because: "default operational weight 0.7 × confidence 0.7 = 0.49 → medium bucket");
        defaultData.Risks[0].Score.Should().BeApproximately(0.49, 0.001);

        overrideData.Risks.Should().ContainSingle();
        overrideData.Risks[0].Severity.Should().Be("high",
            because: "overridden operational weight 1.0 × confidence 0.7 = 0.7 → high bucket");
        overrideData.Risks[0].Score.Should().BeApproximately(0.7, 0.001);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesIdenticalRisks()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.95, 0, 10),
            ("Legal risk.", "legal", 0.95, 0, 10),    // exact dup
            ("Legal risk.", "legal", 0.95, 20, 30)    // distinct span
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 50));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Risks.Should().HaveCount(2);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // FR-15 binding — deterministic severity scoring
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Determinism_SameLlmResponse_ProducesByteIdenticalSeverityOutput()
    {
        // FR-15 binding — severity is deterministic post-LLM. Same LLM output + same config
        // → byte-identical structured output across runs. We bypass the cache by clearing
        // it between calls so each run goes LLM → scoring → output.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Critical legal risk.", "legal", 0.95, 0, 30),
            ("Compliance flag.", "compliance", 0.7, 31, 60),
            ("Operational concern.", "operational", 0.6, 61, 90)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 100));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var first = await handler.ExecuteAsync(context, tool, CancellationToken.None);
        _cacheStore.Clear(); // force LLM call on second run
        var second = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();

        var firstJson = first.Data!.Value.GetRawText();
        var secondJson = second.Data!.Value.GetRawText();

        firstJson.Should().Be(secondJson,
            because: "FR-15 binding — same LLM response + same config MUST produce byte-identical " +
                     "severity output; severity scoring is the deterministic layer downstream of the LLM");
    }

    [Fact]
    public void Determinism_BucketSeverity_BoundariesAreExact()
    {
        // FR-15 binding — bucket boundaries are exact (no off-by-one).
        RiskDetectorHandler.BucketSeverity(0.0).Should().Be("low");
        RiskDetectorHandler.BucketSeverity(0.249999).Should().Be("low");
        RiskDetectorHandler.BucketSeverity(0.25).Should().Be("medium");
        RiskDetectorHandler.BucketSeverity(0.499999).Should().Be("medium");
        RiskDetectorHandler.BucketSeverity(0.5).Should().Be("high");
        RiskDetectorHandler.BucketSeverity(0.749999).Should().Be("high");
        RiskDetectorHandler.BucketSeverity(0.75).Should().Be("critical");
        RiskDetectorHandler.BucketSeverity(1.0).Should().Be("critical");
    }

    [Fact]
    public async Task ExecuteAsync_Determinism_StressedRepetition_IdenticalOutput()
    {
        // Run the same LLM response 25 times (bypassing cache each time). Every run must
        // produce byte-identical structured output. 25 runs is enough to surface any
        // hidden non-determinism (dictionary iteration order, sort instability, etc.)
        // without making the test suite slow.
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Risk 1.", "legal", 0.85, 0, 10),
            ("Risk 2.", "financial", 0.70, 11, 20),
            ("Risk 3.", "compliance", 0.80, 21, 30),
            ("Risk 4.", "data-privacy", 0.65, 31, 40),
            ("Risk 5.", "contract", 0.75, 41, 50)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: new string('a', 60));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        string? canonical = null;
        for (var i = 0; i < 25; i++)
        {
            _cacheStore.Clear();
            var r = await handler.ExecuteAsync(context, tool, CancellationToken.None);
            r.Success.Should().BeTrue();
            var json = r.Data!.Value.GetRawText();
            if (canonical is null) canonical = json;
            else json.Should().Be(canonical,
                because: $"FR-15 — severity output MUST be byte-identical across runs (run {i + 1})");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — error cases
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReturnsModelError_OnMalformedLlmJson()
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{not valid json");

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some contract text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ModelError);
        result.Execution.ModelCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteAsync(context, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsInternalError_OnLlmException()
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upstream timeout"));

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-014: per-tenant cache hit/miss + isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_SecondIdenticalCall_HitsCache_NotLlm()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var first = await handler.ExecuteAsync(context, tool, CancellationToken.None);
        var second = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        first.Success.Should().BeTrue();
        first.Execution.CacheHit.Should().BeFalse();
        first.Execution.ModelCalls.Should().Be(1);

        second.Success.Should().BeTrue();
        second.Execution.CacheHit.Should().BeTrue();
        second.Execution.ModelCalls.Should().Be(0);

        OpenAiClientMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(),
            It.IsAny<BinaryData>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CacheKey_IsTenantScoped_NoCrossTenantHits()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var ctxA = BuildToolExecutionContext(extractedText: "Same input.", tenantId: "tenant-A");
        var ctxB = BuildToolExecutionContext(extractedText: "Same input.", tenantId: "tenant-B");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        await handler.ExecuteAsync(ctxA, tool, CancellationToken.None);
        var resultB = await handler.ExecuteAsync(ctxB, tool, CancellationToken.None);

        resultB.Execution.CacheHit.Should().BeFalse(
            because: "ADR-014: cache key MUST be per-tenant — tenant B cannot read tenant A's entries");

        OpenAiClientMock.Verify(c => c.GetStructuredCompletionRawAsync(
            It.IsAny<string>(),
            It.IsAny<BinaryData>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _cacheStore.Keys.Should().Contain(k => k.Contains(":tenant-A:"));
        _cacheStore.Keys.Should().Contain(k => k.Contains(":tenant-B:"));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat invocation path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_DetectsRisksFromTextArgument()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: "{\"text\":\"Contract section text.\"}");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = GetData(result);
        data.Stats.TotalCount.Should().Be(1);
        data.Risks[0].Category.Should().Be("legal");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry compliance
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_DoesNotLogInputTextOrRiskDescriptions()
    {
        const string secretInputText =
            "ConfidentialContract-XYZ123 contains PrivilegedTermABC456 with FlaggedClauseDEF789.";
        const string secretRiskDescription =
            "FlaggedClauseDEF789 exposes ConfidentialClient-PQR789 to liability.";

        var llmResponse = BuildLlmResponse(new[]
        {
            (secretRiskDescription, "legal", 0.95, 0, 50)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: secretInputText);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // ADR-015: NEITHER input text NOR risk descriptions may appear in logs.
        AssertTelemetryRespectsAdr015(
            secretInputText,
            secretRiskDescription,
            "PrivilegedTermABC456",
            "FlaggedClauseDEF789",
            "ConfidentialClient-PQR789");
    }

    [Fact]
    public async Task Telemetry_ChatPath_DoesNotLogInputTextOrRiskDescriptions()
    {
        const string secretInputText =
            "ConfidentialContract-XYZ123 with PrivilegedTermABC456 clause.";
        const string secretRiskDescription =
            "PrivilegedTermABC456 creates regulatory exposure for ConfidentialClient-PQR789.";

        var llmResponse = BuildLlmResponse(new[]
        {
            (secretRiskDescription, "compliance", 0.9, 0, 50)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildChatInvocationContext(
            toolArgumentsJson: JsonSerializer.Serialize(new { text = secretInputText }));
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(
            secretInputText,
            secretRiskDescription,
            "PrivilegedTermABC456",
            "ConfidentialClient-PQR789");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync — model + execution metadata
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ReportsOneModelCall_AndModelName()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler), toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Execution.ModelCalls.Should().Be(1);
        result.Execution.ModelName.Should().NotBeNullOrWhiteSpace();
        result.Execution.CacheHit.Should().BeFalse();
        result.Execution.InputTokens.Should().NotBeNull();
        result.Execution.OutputTokens.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfigModelDeployment_WhenSpecified()
    {
        var llmResponse = BuildLlmResponse(new[]
        {
            ("Legal risk.", "legal", 0.9, 0, 20)
        });

        SetupLlmResponse(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: "Some text.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            configuration: "{\"modelDeployment\":\"gpt-4o\"}");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Execution.ModelName.Should().Be("gpt-4o");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private void SetupLlmResponse(string responseJson)
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseJson);
    }

    private static string BuildLlmResponse(
        IEnumerable<(string Description, string Category, double Confidence, int Start, int End)> risks)
    {
        var sb = new StringBuilder();
        sb.Append("{\"risks\":[");
        var first = true;
        foreach (var (description, category, confidence, start, end) in risks)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append($"\"description\":{JsonSerializer.Serialize(description)},");
            sb.Append($"\"category\":\"{category}\",");
            sb.Append($"\"confidence\":{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"evidenceSpan\":{{\"start\":{start},\"end\":{end}}}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static RiskDetectorHandler.RiskDetectionResult GetData(ToolResult result)
    {
        result.Data.Should().NotBeNull("ToolResult.Data must be populated on success");
        var parsed = result.GetData<RiskDetectorHandler.RiskDetectionResult>();
        parsed.Should().NotBeNull();
        return parsed!;
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
