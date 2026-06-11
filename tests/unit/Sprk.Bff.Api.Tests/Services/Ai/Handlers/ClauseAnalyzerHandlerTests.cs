using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for the R6 Pillar 2 Wave-2 <see cref="ClauseAnalyzerHandler"/> (FR-14).
/// </summary>
/// <remarks>
/// <para>
/// Covers the 4-point handler contract + LLM-assisted behavior:
/// </para>
/// <list type="bullet">
/// <item>Contract: registered + discoverable + valid metadata + non-empty supported types.</item>
/// <item>Positive: mocked LLM → structured clauses across multiple types.</item>
/// <item>Span validation: non-overlapping spans accepted; overlap pushed to unresolved.</item>
/// <item>Type filter: clauseTypes subset filters output.</item>
/// <item>Options: extractObligations + extractParties emit / suppress fields.</item>
/// <item>Cache: identical input → second call hits cache (zero LLM call).</item>
/// <item>Error: clauseType not in allow-list rejected at config validate.</item>
/// <item>Error: LLM returns malformed JSON → ModelError.</item>
/// <item>ADR-015: clause text + party names + structured-field values never leak to logs.</item>
/// </list>
/// </remarks>
public sealed class ClauseAnalyzerHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private ClauseAnalyzerHandler CreateHandler(
        IDistributedCache? cache = null,
        Mock<IOpenAiClient>? openAi = null,
        ModelSelectorOptions? options = null)
    {
        var oai = openAi ?? OpenAiClientMock;
        var dc = cache ?? new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var opts = Options.Create(options ?? new ModelSelectorOptions
        {
            ToolHandlerModel = "gpt-4o-mini"
        });
        return new ClauseAnalyzerHandler(
            oai.Object,
            dc,
            opts,
            CreateLogger<ClauseAnalyzerHandler>());
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
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
            typeof(ClauseAnalyzerHandler),
            because: "auto-discovery in ToolFrameworkExtensions.AddToolHandlersFromAssembly must register the handler with ZERO per-handler DI lines (R6 Pillar 2 + ADR-010)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();
        handler.HandlerId.Should().Be(
            nameof(ClauseAnalyzerHandler),
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
        handler.SupportedToolTypes.Should().Contain(ToolType.ClauseAnalyzer);
    }

    [Fact]
    public void SupportedInvocationContexts_IsBoth()
    {
        IToolHandler handler = CreateHandler();
        // FR-14: handler invocable from both playbook nodes + chat surface
        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "the seed row sets sprk_availableincontexts=Both; handler default must match per task 106");
    }

    [Fact]
    public void Constructor_InjectsIOpenAiClient()
    {
        // FR-14 binding: LLM-assisted handler — IOpenAiClient is a required ctor dep.
        var ctor = typeof(ClauseAnalyzerHandler).GetConstructors().Single();
        var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        parameterTypes.Should().Contain(typeof(IOpenAiClient),
            because: "FR-14 LLM-assisted handlers depend on IOpenAiClient (Azure OpenAI structured outputs)");
        parameterTypes.Should().Contain(typeof(IDistributedCache),
            because: "ADR-014 per-tenant caching requires IDistributedCache");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Configuration validation tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_WithMissingTenantId_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext(tenantId: " ");
        var tool = BuildClauseAnalyzerTool(null);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId"));
    }

    [Fact]
    public void Validate_WithNoConfiguration_ReturnsSuccess()
    {
        // Configuration is optional; defaults to all clause types, no extras
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseAnalyzerTool(null);

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithUnsupportedClauseType_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseAnalyzerTool("""{ "clauseTypes": ["bogus-type"] }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("bogus-type"));
    }

    [Fact]
    public void Validate_WithMalformedJsonConfiguration_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(nameof(ClauseAnalyzerHandler), configuration: "{ this is not json");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JSON") || e.Contains("parse"));
    }

    [Fact]
    public void Validate_WithValidSubsetClauseTypes_ReturnsSuccess()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseAnalyzerTool("""{ "clauseTypes": ["indemnity", "termination"] }""");

        var result = handler.Validate(context, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_WithMissingTextField_ReturnsFailure()
    {
        var handler = CreateHandler();
        var context = BuildChatInvocationContext(toolArgumentsJson: """{ "other": "x" }""");
        var tool = BuildClauseAnalyzerTool(null);

        var result = handler.ValidateChat(context, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("text"));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Positive: mocked LLM returns structured clauses
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WithLlmReturningClauses_ReturnsStructuredResult()
    {
        const string InputText =
            "Party A shall indemnify Party B against all losses. " +
            "Either party may terminate on thirty days notice.";

        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "Party A shall indemnify Party B against all losses.",
                  "startSpan": 0,
                  "endSpan": 50,
                  "structuredFields": { "indemnitor": "Party A", "indemnitee": "Party B" },
                  "confidence": 0.92
                },
                {
                  "type": "termination",
                  "text": "Either party may terminate on thirty days notice.",
                  "startSpan": 51,
                  "endSpan": {{InputText.Length}},
                  "structuredFields": { "noticeDays": 30 },
                  "confidence": 0.88
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool(null);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var data = result.Data!.Value;
        var clauses = data.GetProperty("clauses").EnumerateArray().ToList();
        clauses.Should().HaveCount(2);
        clauses[0].GetProperty("type").GetString().Should().Be("indemnity");
        clauses[1].GetProperty("type").GetString().Should().Be("termination");

        var stats = data.GetProperty("stats");
        stats.GetProperty("totalCount").GetInt32().Should().Be(2);
        stats.GetProperty("byType").GetProperty("indemnity").GetInt32().Should().Be(1);
        stats.GetProperty("byType").GetProperty("termination").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithClauseTypeFilter_FiltersOutNonMatchingTypes()
    {
        const string InputText = "Contract text here.";

        // LLM returns a "payment" clause, but config filter only accepts "indemnity"
        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "payment",
                  "text": "Payment terms",
                  "startSpan": 0,
                  "endSpan": 13,
                  "structuredFields": { "amount": 100 },
                  "confidence": 0.9
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool("""{ "clauseTypes": ["indemnity"] }""");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        var clauses = data.GetProperty("clauses").EnumerateArray().ToList();
        clauses.Should().BeEmpty(
            because: "the LLM returned a 'payment' clause but config restricts to 'indemnity' — post-processor filters it");
    }

    [Fact]
    public async Task ExecuteAsync_WithExtractObligations_IncludesObligationsField()
    {
        const string InputText = "Indemnity clause text here long enough.";
        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "Indemnity clause text here long enough.",
                  "startSpan": 0,
                  "endSpan": {{InputText.Length}},
                  "structuredFields": { "scope": "all losses" },
                  "obligations": ["Indemnify Party B", "Defend Party B"],
                  "confidence": 0.91
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool("""{ "options": { "extractObligations": true } }""");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var clause = result.Data!.Value.GetProperty("clauses").EnumerateArray().First();
        clause.TryGetProperty("obligations", out var obligationsNode).Should().BeTrue();
        var obligations = obligationsNode.EnumerateArray().Select(o => o.GetString()).ToList();
        obligations.Should().BeEquivalentTo(new[] { "Indemnify Party B", "Defend Party B" });
    }

    [Fact]
    public async Task ExecuteAsync_WithExtractParties_IncludesPartiesField()
    {
        const string InputText = "Indemnity clause text long enough for offset.";
        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "Indemnity clause text long enough for offset.",
                  "startSpan": 0,
                  "endSpan": {{InputText.Length}},
                  "structuredFields": { },
                  "parties": ["Party A", "Party B"],
                  "confidence": 0.85
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool("""{ "options": { "extractParties": true } }""");

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var clause = result.Data!.Value.GetProperty("clauses").EnumerateArray().First();
        clause.TryGetProperty("parties", out var partiesNode).Should().BeTrue();
        var parties = partiesNode.EnumerateArray().Select(p => p.GetString()).ToList();
        parties.Should().BeEquivalentTo(new[] { "Party A", "Party B" });
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Span validation tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WithOverlappingSpans_AcceptsFirstAndUnresolvesSecond()
    {
        const string InputText = "First clause text. Second clause text overlaps first.";

        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "First clause text.",
                  "startSpan": 0,
                  "endSpan": 30,
                  "structuredFields": {},
                  "confidence": 0.9
                },
                {
                  "type": "liability",
                  "text": "Second clause text overlaps first.",
                  "startSpan": 20,
                  "endSpan": 50,
                  "structuredFields": {},
                  "confidence": 0.8
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool(null);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        var clauses = data.GetProperty("clauses").EnumerateArray().ToList();
        clauses.Should().HaveCount(1, because: "second clause overlaps first; first wins (earlier start)");
        clauses[0].GetProperty("type").GetString().Should().Be("indemnity");

        var unresolved = data.GetProperty("unresolvedSpans").EnumerateArray().ToList();
        unresolved.Should().HaveCount(1);
        unresolved[0].GetProperty("type").GetString().Should().Be("liability");
        unresolved[0].GetProperty("reason").GetString().Should().Be("overlapping-span");
    }

    [Fact]
    public async Task ExecuteAsync_WithOutOfBoundsSpan_PushesToUnresolved()
    {
        const string InputText = "Short text.";

        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "payment",
                  "text": "out of bounds",
                  "startSpan": 0,
                  "endSpan": 999999,
                  "structuredFields": {},
                  "confidence": 0.9
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: InputText);
        var tool = BuildClauseAnalyzerTool(null);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var data = result.Data!.Value;
        data.GetProperty("clauses").GetArrayLength().Should().Be(0);
        var unresolved = data.GetProperty("unresolvedSpans").EnumerateArray().ToList();
        unresolved.Should().HaveCount(1);
        unresolved[0].GetProperty("reason").GetString().Should().Be("out-of-bounds-or-empty");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Cache tests (ADR-014)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_SecondIdenticalCall_HitsCache_DoesNotCallLlm()
    {
        const string InputText = "Indemnity clause: Party A shall indemnify Party B.";
        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "{{InputText}}",
                  "startSpan": 0,
                  "endSpan": {{InputText.Length}},
                  "structuredFields": { "indemnitor": "Party A", "indemnitee": "Party B" },
                  "confidence": 0.9
                }
              ]
            }
            """;

        var llmCallCount = 0;
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                llmCallCount++;
                return llmResponse;
            });

        // Shared cache across two calls
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var handler = CreateHandler(cache: sharedCache);

        var context1 = BuildToolExecutionContext(extractedText: InputText);
        var r1 = await handler.ExecuteAsync(context1, BuildClauseAnalyzerTool(null), CancellationToken.None);
        var context2 = BuildToolExecutionContext(extractedText: InputText);
        var r2 = await handler.ExecuteAsync(context2, BuildClauseAnalyzerTool(null), CancellationToken.None);

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        llmCallCount.Should().Be(1, because: "the second call must serve from cache, not re-invoke the LLM");
        r2.Execution.CacheHit.Should().BeTrue();
        r2.Execution.ModelCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_DifferentTenants_NeverShareCache_Adr014()
    {
        const string InputText = "Indemnity clause text for cache key test.";
        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "{{InputText}}",
                  "startSpan": 0,
                  "endSpan": {{InputText.Length}},
                  "structuredFields": { },
                  "confidence": 0.9
                }
              ]
            }
            """;

        var llmCallCount = 0;
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                llmCallCount++;
                return llmResponse;
            });

        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var handler = CreateHandler(cache: sharedCache);

        await handler.ExecuteAsync(
            BuildToolExecutionContext(extractedText: InputText, tenantId: "tenant-A"),
            BuildClauseAnalyzerTool(null),
            CancellationToken.None);
        await handler.ExecuteAsync(
            BuildToolExecutionContext(extractedText: InputText, tenantId: "tenant-B"),
            BuildClauseAnalyzerTool(null),
            CancellationToken.None);

        llmCallCount.Should().Be(2,
            because: "ADR-014: per-tenant cache key — different tenants MUST NEVER share cache entries");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Error path: malformed LLM response
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WithMalformedLlmJson_ReturnsModelError()
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ this is not valid json");

        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseAnalyzerTool(null);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ModelError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Cancellation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeStart_ReturnsCancelledError()
    {
        var handler = CreateHandler();
        var context = BuildToolExecutionContext();
        var tool = BuildClauseAnalyzerTool(null);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteAsync(context, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat invocation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_WithValidArguments_ReturnsStructuredResult()
    {
        const string InputText = "Termination on thirty days notice. Payment within ninety days.";

        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "termination",
                  "text": "Termination on thirty days notice.",
                  "startSpan": 0,
                  "endSpan": 34,
                  "structuredFields": { "noticeDays": 30 },
                  "confidence": 0.9
                },
                {
                  "type": "payment",
                  "text": "Payment within ninety days.",
                  "startSpan": 35,
                  "endSpan": 62,
                  "structuredFields": { "termDays": 90 },
                  "confidence": 0.85
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var argsJson = JsonSerializer.Serialize(new { text = InputText });
        var context = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var tool = BuildClauseAnalyzerTool(null);

        var result = await handler.ExecuteChatAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Value.GetProperty("clauses").GetArrayLength().Should().Be(2);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry binding — clause text + party + structured field values MUST NOT leak
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_DoesNotLeakClauseText()
    {
        const string SecretText = "CONFIDENTIAL clause Alpha-XYZ-9999 between Party A and Party B.";
        const string SecretParty = "ACME-PRIVILEGED-CORP-9999";
        const string SecretField = "Confidential-Scope-1234567890";

        var llmResponse = $$"""
            {
              "clauses": [
                {
                  "type": "indemnity",
                  "text": "{{SecretText}}",
                  "startSpan": 0,
                  "endSpan": {{SecretText.Length}},
                  "structuredFields": { "scope": "{{SecretField}}" },
                  "parties": ["{{SecretParty}}"],
                  "confidence": 0.9
                }
              ]
            }
            """;

        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var handler = CreateHandler();
        var context = BuildToolExecutionContext(extractedText: SecretText);
        var tool = BuildClauseAnalyzerTool("""{ "options": { "extractParties": true } }""");

        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // ADR-015 binding: clause text + party + structured-field values MUST NOT appear in logs.
        // The fixture helper also checks universal patterns (bearer tokens, base64 blobs).
        AssertTelemetryRespectsAdr015(SecretText, SecretParty, SecretField);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private static AnalysisTool BuildClauseAnalyzerTool(string? configuration)
        => BuildAnalysisTool(
            handlerClass: nameof(ClauseAnalyzerHandler),
            configuration: configuration,
            toolType: ToolType.ClauseAnalyzer);

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
