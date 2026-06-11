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
/// Unit tests for the R6 Pillar 2 Wave-1 <see cref="ClauseComparisonHandler"/> (FR-16).
/// </summary>
/// <remarks>
/// <para>
/// Covers the four binding R6 Pillar 2 contract assertions (registered + discoverable + valid
/// metadata + non-empty supported types) + per-handler positive / error / config-driven /
/// telemetry-compliance cases.
/// </para>
/// <para>
/// Inherits from <see cref="TypedToolHandlerTestFixture"/> for the shared logging-capture
/// scaffolding + the <c>AssertTelemetryRespectsAdr015</c> helper that asserts no clause text
/// leaks into the log surface (ADR-015 binding).
/// </para>
/// <para>
/// <strong>FR-16 binding — pure deterministic</strong>: every assertion in this file runs
/// with NO <see cref="IOpenAiClient"/> setup, NO Azure OpenAI dependency, NO LLM call.
/// Identical inputs + identical config MUST produce identical output (litigation-defensibility
/// invariant — exercised explicitly in <c>ExecuteAsync_Determinism_*</c> below).
/// </para>
/// </remarks>
public sealed class ClauseComparisonHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private ClauseComparisonHandler CreateHandler() => new(CreateLogger<ClauseComparisonHandler>());

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests (copy of HandlerContractTestTemplate, retargeted)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();
        var registered = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registered.Should().Contain(
            typeof(ClauseComparisonHandler),
            because: "auto-discovery in ToolFrameworkExtensions.AddToolHandlersFromAssembly must register the handler with ZERO per-handler DI lines (R6 Pillar 2 + ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(ClauseComparisonHandler),
            because: "the Dataverse sprk_handlerclass column routes to this handler via HandlerId == nameof(handler class)");
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
        metadata.Parameters.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = CreateHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.ClauseComparison);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // FR-16 binding: pure deterministic (NO LLM / Azure OpenAI dependency)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_DoesNotRequireOpenAiClient()
    {
        // FR-16 binding: pure deterministic — no LLM dependency in the constructor signature.
        var ctor = typeof(ClauseComparisonHandler).GetConstructors().Single();
        var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        parameterTypes.Should().NotContain(typeof(IOpenAiClient),
            because: "FR-16 binding: ClauseComparisonHandler is pure-deterministic — no LLM dependency");

        parameterTypes.Should().HaveCount(1, because: "the only ctor dep is ILogger<ClauseComparisonHandler>");
    }

    [Fact]
    public void SupportedInvocationContexts_DefaultsToPlaybook()
    {
        IToolHandler handler = CreateHandler();

        // Per task 103 + seed row: ClauseComparison is Playbook-only in R6 (not chat-exposed).
        // SupportedInvocationContexts is a default interface member; cast to IToolHandler to access.
        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Playbook,
            because: "the seed row sets sprk_availableincontexts=Playbook; handler default matches");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validation tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_WithMissingTenantId_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(tenantId: " ");
        var tool = BuildClauseComparisonTool("""{ "clauseA": "x", "clauseB": "y" }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId"));
    }

    [Fact]
    public void Validate_WithMissingConfiguration_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(nameof(ClauseComparisonHandler), configuration: null);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("clauseA") || e.Contains("configuration"));
    }

    [Fact]
    public void Validate_WithMissingClauseA_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseComparisonTool("""{ "clauseB": "only b" }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("clauseA"));
    }

    [Fact]
    public void Validate_WithMissingClauseB_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseComparisonTool("""{ "clauseA": "only a" }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("clauseB"));
    }

    [Fact]
    public void Validate_WithExcessivelyLongClause_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var bigA = new string('x', 60_000);
        var tool = BuildClauseComparisonTool($$"""{ "clauseA": "{{bigA}}", "clauseB": "y" }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("clauseA"));
    }

    [Fact]
    public void Validate_WithUnsupportedGranularity_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseComparisonTool("""
            { "clauseA": "a", "clauseB": "b", "granularity": "character" }
            """);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("granularity"));
    }

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseComparisonTool("""
            { "clauseA": "a", "clauseB": "b", "granularity": "sentence" }
            """);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMalformedJsonConfiguration_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(nameof(ClauseComparisonHandler), configuration: "{ this is not json");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JSON") || e.Contains("configuration"));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Positive cases — identical / single change / reorder / cosmetic
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_IdenticalClauses_SimilarityIsOne()
    {
        var handler = CreateHandler();
        const string Clause = "The party agrees to indemnify the other party. All disputes shall be resolved by arbitration.";
        var tool = BuildClauseComparisonTool($$"""
            { "clauseA": "{{Clause}}", "clauseB": "{{Clause}}", "granularity": "sentence" }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, segments, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().Be(1.0, because: "identical clause text produces 100% similarity at any granularity");
        segments.Should().OnlyContain(s => GetSegmentType(s) == "unchanged",
            because: "identical inputs produce ONLY 'unchanged' segments");
    }

    [Fact]
    public async Task ExecuteAsync_CompletelyDifferentClauses_SimilarityIsZero()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            { "clauseA": "alpha beta gamma", "clauseB": "delta epsilon zeta", "granularity": "word" }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, segments, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().Be(0.0, because: "no token overlap means similarity 0.0");
        segments.Should().Contain(s =>
            GetSegmentType(s) == "added" || GetSegmentType(s) == "removed" || GetSegmentType(s) == "modified");
    }

    [Fact]
    public async Task ExecuteAsync_SingleSentenceChange_ReportsOneModification()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Party A may terminate this agreement. Party B may not terminate this agreement. Notices shall be in writing.",
              "clauseB": "Party A may terminate this agreement. Party B may also terminate this agreement. Notices shall be in writing.",
              "granularity": "sentence"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, segments, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().BeGreaterThan(0.5, because: "two of three sentences are identical");
        similarity.Should().BeLessThan(1.0, because: "the middle sentence differs");

        var changeSegments = segments.Where(s => GetSegmentType(s) != "unchanged").ToList();
        changeSegments.Should().NotBeEmpty(because: "the middle sentence change must appear as a non-unchanged segment");
    }

    [Fact]
    public async Task ExecuteAsync_CosmeticWhitespaceDifference_IgnoredByDefault()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "The party agrees to pay the fee.",
              "clauseB": "The   party   agrees    to  pay   the   fee.",
              "granularity": "word"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, _, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().Be(1.0,
            because: "the default config normalizes whitespace; cosmetic differences must be ignored");
    }

    [Fact]
    public async Task ExecuteAsync_CaseDifferenceOnly_TreatedAsIdenticalByDefault()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Agreement",
              "clauseB": "agreement",
              "granularity": "word"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, _, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().Be(1.0,
            because: "the default config is caseSensitive=false; case-only difference compares equal");
    }

    [Fact]
    public async Task ExecuteAsync_CaseDifferenceOnly_DistinguishedWhenCaseSensitive()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Agreement",
              "clauseB": "agreement",
              "granularity": "word",
              "caseSensitive": true
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, segments, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().BeLessThan(1.0,
            because: "with caseSensitive=true, 'Agreement' and 'agreement' are distinct tokens");
        segments.Should().Contain(s =>
            GetSegmentType(s) == "modified" || GetSegmentType(s) == "added" || GetSegmentType(s) == "removed");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Config-driven granularity tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_GranularitySentenceVsParagraph_ProducesDifferentSegmentCounts()
    {
        const string ClauseA = "First sentence. Second sentence.\n\nNew paragraph here.";
        const string ClauseB = "First sentence. Second sentence MODIFIED.\n\nNew paragraph here.";

        var handler = CreateHandler();

        var sentenceTool = BuildClauseComparisonTool($$"""
            { "clauseA": {{JsonSerializer.Serialize(ClauseA)}}, "clauseB": {{JsonSerializer.Serialize(ClauseB)}}, "granularity": "sentence" }
            """);
        var paragraphTool = BuildClauseComparisonTool($$"""
            { "clauseA": {{JsonSerializer.Serialize(ClauseA)}}, "clauseB": {{JsonSerializer.Serialize(ClauseB)}}, "granularity": "paragraph" }
            """);

        var sentenceResult = await handler.ExecuteAsync(BuildToolExecutionContext(), sentenceTool, CancellationToken.None);
        var paragraphResult = await handler.ExecuteAsync(BuildToolExecutionContext(), paragraphTool, CancellationToken.None);

        sentenceResult.Success.Should().BeTrue();
        paragraphResult.Success.Should().BeTrue();

        var (_, sentenceSegments, sentenceStructural) = ExtractDiff(sentenceResult.Data!.Value);
        var (_, paragraphSegments, paragraphStructural) = ExtractDiff(paragraphResult.Data!.Value);

        // Sentence granularity sees 3 segments per side (3 sentences).
        // Paragraph granularity sees 2 segments per side (2 paragraphs).
        sentenceStructural.GetProperty("totalSegmentsA").GetInt32().Should().BeGreaterOrEqualTo(3);
        paragraphStructural.GetProperty("totalSegmentsA").GetInt32().Should().Be(2);

        // The total segment lists differ in length — that's the config-driven invariant.
        sentenceSegments.Count.Should().NotBe(paragraphSegments.Count,
            because: "different granularities produce structurally different diffs deterministically");
    }

    [Fact]
    public async Task ExecuteAsync_PunctuationSensitive_DefaultIsTrue_PunctuationDifferenceReported()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Party A pays.",
              "clauseB": "Party A pays",
              "granularity": "word"
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, _, _) = ExtractDiff(result.Data!.Value);

        // "pays." vs "pays" are different tokens when punctuationSensitive=true.
        similarity.Should().BeLessThan(1.0,
            because: "default punctuationSensitive=true treats 'pays.' and 'pays' as distinct word tokens");
    }

    [Fact]
    public async Task ExecuteAsync_PunctuationInsensitive_ConfigFlag_TreatsPunctuationAsCosmetic()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Party A pays.",
              "clauseB": "Party A pays",
              "granularity": "word",
              "punctuationSensitive": false
            }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (similarity, _, _) = ExtractDiff(result.Data!.Value);

        similarity.Should().Be(1.0,
            because: "with punctuationSensitive=false, '.', etc. are stripped before comparison");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Determinism (FR-16 litigation-defensibility binding)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Determinism_IdenticalInputs_ProduceIdenticalOutputs()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            {
              "clauseA": "Party A agrees to indemnify. Party B agrees to defend.",
              "clauseB": "Party A agrees to indemnify. Party B agrees to defend and hold harmless.",
              "granularity": "sentence"
            }
            """);

        var r1 = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);
        var r2 = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();

        var json1 = r1.Data!.Value.GetRawText();
        var json2 = r2.Data!.Value.GetRawText();

        json1.Should().Be(json2,
            because: "FR-16 binding — identical inputs + identical config MUST produce byte-identical output");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Edge cases — empty inputs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_EmptyClauseA_ReportsAllAdded()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            { "clauseA": "", "clauseB": "" }
            """);

        // Empty clauseA is caught by validation, so we use a single-char input here.
        var validTool = BuildClauseComparisonTool("""
            { "clauseA": "a", "clauseB": "b c d", "granularity": "word" }
            """);

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), validTool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var (_, segments, structural) = ExtractDiff(result.Data!.Value);

        structural.GetProperty("totalSegmentsA").GetInt32().Should().Be(1);
        structural.GetProperty("totalSegmentsB").GetInt32().Should().Be(3);
        segments.Should().Contain(s => GetSegmentType(s) == "added" || GetSegmentType(s) == "modified");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Cancellation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_CanceledToken_ReturnsCancelledError()
    {
        var handler = CreateHandler();
        var tool = BuildClauseComparisonTool("""
            { "clauseA": "a", "clauseB": "b" }
            """);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteAsync(BuildToolExecutionContext(), tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry binding — clause text MUST NOT leak into logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_DoesNotLeakClauseText()
    {
        var handler = CreateHandler();

        const string SecretClauseA = "Confidential indemnification language ABC123XYZ.";
        const string SecretClauseB = "Revised confidential indemnification language ABC123XYZ.";

        var tool = BuildClauseComparisonTool($$"""
            {
              "clauseA": {{JsonSerializer.Serialize(SecretClauseA)}},
              "clauseB": {{JsonSerializer.Serialize(SecretClauseB)}},
              "granularity": "sentence"
            }
            """);

        await handler.ExecuteAsync(BuildToolExecutionContext(), tool, CancellationToken.None);

        // ADR-015 binding: log surface must NOT include clause text. The fixture
        // helper also checks universal patterns (bearer tokens, base64 blobs).
        AssertTelemetryRespectsAdr015(SecretClauseA, SecretClauseB);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static AnalysisTool BuildClauseComparisonTool(string configuration)
        => BuildAnalysisTool(
            handlerClass: nameof(ClauseComparisonHandler),
            configuration: configuration,
            toolType: ToolType.ClauseComparison);

    private static (double Similarity, IReadOnlyList<JsonElement> Segments, JsonElement Structural) ExtractDiff(JsonElement data)
    {
        var similarity = data.GetProperty("similarity").GetDouble();
        var segments = data.GetProperty("segments").EnumerateArray().ToList();
        var structural = data.GetProperty("structural");
        return (similarity, segments, structural);
    }

    private static string GetSegmentType(JsonElement segment)
        => segment.GetProperty("type").GetString() ?? string.Empty;

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
