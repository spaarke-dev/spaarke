using System.Net;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="WebSearchHandler"/> (R6 Wave 8 — replaces the legacy hardcoded
/// <c>WebSearchTools</c> class previously instantiated in
/// <see cref="Chat.SprkChatAgentFactory"/>).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; happy path with mocked Bing response; graceful mock fallback
/// when no API key configured; scope-guided search query construction (FR-10); citation
/// metadata returned via Wave 7b envelope; concurrency limiting (max 2 per
/// <see cref="WebSearchHandler"/>) preserved from legacy; ADR-015 telemetry hygiene
/// (no query content above Debug); Chat-only dispatch + playbook path returns ValidationFailed.
/// </remarks>
public sealed class WebSearchHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // Sentinel — recognisable string so the ADR-015 telemetry scan can assert it never
    // appears in captured log messages above Debug.
    private const string SentinelQuery = "ACME-CORP-MATTERX2026-CONFIDENTIAL-PATENT-CASE-DETAILS";

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
            typeof(WebSearchHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(WebSearchHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) routes sprk_handlerclass to this handler at runtime");
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
        handler.SupportedToolTypes.Should().Contain(ToolType.Custom);
    }

    [Fact]
    public void SupportedInvocationContexts_IsChatOnly()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Chat,
            because: "R6 Wave 8: WebSearchHandler is chat-only; legacy WebSearchTools was wired into the chat agent factory only");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Validate (playbook) — chat-only handler rejects playbook invocation
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Fails_BecauseChatOnly()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildWebSearchTool();

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeFalse(
            because: "WebSearchHandler is chat-only; playbook invocation is rejected");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_ReturnsValidationFailed()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildWebSearchTool();

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ValidateChat — argument shape
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateChat_Succeeds_WithValidArgs()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"contracts 2026\"}");
        var tool = BuildWebSearchTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\"}",
            tenantId: "");
        var tool = BuildWebSearchTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenArgsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "");
        var tool = BuildWebSearchTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("arguments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenQueryFieldMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildWebSearchTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildWebSearchTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ExecuteChatAsync — happy path with mocked Bing response
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_HappyPath_ReturnsCitationsInMetadata()
    {
        // Arrange — Bing returns 2 web pages
        var bingResponseJson = BuildBingResponseJson(
            ("Acme Quarterly Filing", "https://example.com/acme-q3", "Acme Corp reported Q3 revenue of $42B."),
            ("Acme Patent Suit", "https://example.com/acme-patent", "Widget Industries countersuit filed yesterday."));

        var handlerMock = BuildHttpHandler(HttpStatusCode.OK, bingResponseJson);
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"Acme Corp Q3 patent\"}");
        var tool = BuildWebSearchTool();

        // Act
        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.HandlerId.Should().Be(nameof(WebSearchHandler));
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);

        var citations = ExtractCitations(result);
        citations.Should().HaveCount(2);
        citations[0].ChunkId.Should().Be("https://example.com/acme-q3");
        citations[0].SourceName.Should().Be("Acme Quarterly Filing");
        citations[0].SourceType.Should().Be("web",
            because: "ADR-015: web citations must carry SourceType='web' so frontend renders the [External Source] badge");
        citations[0].Url.Should().Be("https://example.com/acme-q3");
    }

    [Fact]
    public async Task ExecuteChatAsync_HappyPath_RespectsMaxResultsArg()
    {
        // Arrange — Bing returns 5 pages but caller asks for 2
        var bingResponseJson = BuildBingResponseJson(
            ("R1", "https://example.com/1", "S1"),
            ("R2", "https://example.com/2", "S2"),
            ("R3", "https://example.com/3", "S3"),
            ("R4", "https://example.com/4", "S4"),
            ("R5", "https://example.com/5", "S5"));

        var handlerMock = BuildHttpHandler(HttpStatusCode.OK, bingResponseJson);
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\",\"maxResults\":2}");
        var tool = BuildWebSearchTool();

        // Act
        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var citations = ExtractCitations(result);
        citations.Should().HaveCount(2);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Graceful mock fallback when no API key
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_NoApiKey_ReturnsMockResults()
    {
        // Arrange — no API key; handler must NOT call Bing.
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bing should NOT be called when API key is absent"));

        var handler = CreateHandler(handlerMock, apiKey: null);

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"anything\",\"maxResults\":3}");
        var tool = BuildWebSearchTool();

        // Act
        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assert — handler returned mock results without touching Bing
        result.Success.Should().BeTrue();
        var citations = ExtractCitations(result);
        citations.Should().HaveCount(3,
            because: "graceful mock fallback respects maxResults; 3 mock entries returned");

        // Mock results all use example.com / learn.microsoft.com URLs and carry SourceType='web'
        citations.Should().OnlyContain(c => c.SourceType == "web");

        handlerMock.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scope-guided search (FR-10) — query prepended with ScopeSearchGuidance
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_PrependsScopeSearchGuidance_ToBingRequest()
    {
        // Arrange — capture the request URI Bing sees so we can verify the prepended hint
        Uri? capturedUri = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildBingResponseJson(), Encoding.UTF8, "application/json")
            });

        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var baseCtx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"contract enforceability\"}");
        var ctx = baseCtx with
        {
            KnowledgeScope = new ChatKnowledgeScope(
                RagKnowledgeSourceIds: Array.Empty<string>(),
                InlineContent: null,
                SkillInstructions: null,
                ActiveDocumentId: null,
                ScopeSearchGuidance: "Westlaw LexisNexis")
        };
        var tool = BuildWebSearchTool();

        // Act
        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedUri.Should().NotBeNull();

        // Bing URI uses URL-encoded space: "%20" — query string is "Westlaw%20LexisNexis%20contract%20enforceability"
        var query = capturedUri!.Query;
        query.Should().Contain("Westlaw",
            because: "FR-10: ScopeSearchGuidance must be prepended to the LLM's raw query before calling Bing");
        query.Should().Contain("LexisNexis");
        query.Should().Contain("contract");
        query.Should().Contain("enforceability");
    }

    [Fact]
    public void ApplyScopeGuidance_NullGuidance_ReturnsQueryUnchanged()
    {
        var result = WebSearchHandler.ApplyScopeGuidance("foo bar", null);
        result.Should().Be("foo bar");
    }

    [Fact]
    public void ApplyScopeGuidance_WhitespaceGuidance_ReturnsQueryUnchanged()
    {
        var result = WebSearchHandler.ApplyScopeGuidance("foo bar", "   ");
        result.Should().Be("foo bar");
    }

    [Fact]
    public void ApplyScopeGuidance_NonEmptyGuidance_PrependsToQuery()
    {
        var result = WebSearchHandler.ApplyScopeGuidance("foo bar", "Westlaw LexisNexis");
        result.Should().Be("Westlaw LexisNexis foo bar");
    }

    [Fact]
    public async Task ExecuteChatAsync_NoScope_DoesNotPrependGuidance()
    {
        // Arrange — capture the request URI; verify no extra terms are prepended
        Uri? capturedUri = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUri = req.RequestUri)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildBingResponseJson(), Encoding.UTF8, "application/json")
            });

        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"vanilla query\"}");
        var tool = BuildWebSearchTool();

        // Act
        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Assert — exactly the LLM's raw query went to Bing (URL-encoded)
        capturedUri.Should().NotBeNull();
        capturedUri!.Query.Should().Contain("vanilla");
        capturedUri.Query.Should().Contain("query");
        // No accidental prefix from a non-null but empty scope
        capturedUri.Query.Should().NotContain("Westlaw");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Concurrency semaphore (ADR-016) — static gate is preserved
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrencyGate_IsPreserved_StaticAcrossInstances()
    {
        // Verify (via reflection) that the static SemaphoreSlim is configured at (2, 2).
        // The legacy WebSearchTools.s_bingConcurrencyGate was (2, 2); the handler must
        // preserve this per ADR-016 to bound Bing API calls. We assert via reflection so
        // the test pins the contract without requiring exhaustive exhaustion-flow testing.
        var field = typeof(WebSearchHandler).GetField(
            "s_bingConcurrencyGate",
            BindingFlags.Static | BindingFlags.NonPublic);

        field.Should().NotBeNull(
            because: "ADR-016: WebSearchHandler must keep a static SemaphoreSlim concurrency gate");

        var semaphore = field!.GetValue(null) as SemaphoreSlim;
        semaphore.Should().NotBeNull();

        // After construction (no permits taken), CurrentCount should equal the initial count (2).
        semaphore!.CurrentCount.Should().Be(2,
            because: "ADR-016: WebSearchHandler concurrency gate must allow exactly 2 concurrent Bing calls (preserved verbatim from legacy WebSearchTools)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bing failures — graceful degradation
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_BingNonSuccess_ReturnsEmptyResults_DoesNotThrow()
    {
        var handlerMock = BuildHttpHandler(HttpStatusCode.InternalServerError, "Bing exploded");
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildWebSearchTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // Bing 5xx: handler logs + returns empty results (preserves legacy graceful behavior)
        result.Success.Should().BeTrue(
            because: "graceful degradation: HTTP failure becomes empty result set + degradation note");
        ExtractCitations(result).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteChatAsync_BingHttpException_ReturnsEmptyResults_DoesNotThrow()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildWebSearchTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "HTTP exceptions are caught and converted to empty + degradation note (legacy behavior preserved)");
        var payload = result.GetData<WebSearchHandler.WebSearchPayload>();
        payload!.DegradationNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteChatAsync_BingMalformedJson_ReturnsEmptyResults_DoesNotThrow()
    {
        var handlerMock = BuildHttpHandler(HttpStatusCode.OK, "{not json at all");
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildWebSearchTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<WebSearchHandler.WebSearchPayload>();
        payload!.DegradationNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteChatAsync_Cancelled_ReturnsCancelledResult()
    {
        var handlerMock = BuildHttpHandler(HttpStatusCode.OK, BuildBingResponseJson());
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildWebSearchTool();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-015 telemetry — query text must NOT appear in logs above Debug
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Telemetry_RespectsAdr015_NoQueryTextInLogs()
    {
        // Mock fallback path (no API key) is the simplest reproducible path that exercises
        // the start-log + result-log + warning-log surface without depending on HTTP I/O.
        var handlerMock = new Mock<HttpMessageHandler>();
        var handler = CreateHandler(handlerMock, apiKey: null);

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"query\":\"{SentinelQuery}\"}}");
        var tool = BuildWebSearchTool();

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // ADR-015 sentinel: the sentinel query string MUST NOT appear in any captured log.
        // The handler logs queryLen (length) + resultCount + duration only.
        AssertTelemetryRespectsAdr015(SentinelQuery);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_NoResultBodyInLogs_BingPath()
    {
        const string SentinelSnippet = "PROPRIETARY-FORMULA-XJ7K-CONFIDENTIAL-RESULT-BODY-SECRET-12345";

        var bingResponseJson = BuildBingResponseJson(
            ("Public Article Title", "https://example.com/x", SentinelSnippet));

        var handlerMock = BuildHttpHandler(HttpStatusCode.OK, bingResponseJson);
        var handler = CreateHandler(handlerMock, apiKey: "test-bing-key");

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"public articles\"}");
        var tool = BuildWebSearchTool();

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // ADR-015: result snippet content must never leak into logs above Debug.
        AssertTelemetryRespectsAdr015(SentinelSnippet);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Chat-context dispatch via ToolHandlerToAIFunctionAdapter (implicit via
    // SupportedInvocationContexts = Chat)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handler_DeclaresChatInvocation_ForAdapterDispatch()
    {
        // The ToolHandlerToAIFunctionAdapter consults SupportedInvocationContexts at
        // construction; it rejects handlers without InvocationContextKind.Chat. Our row
        // sets sprk_availableincontexts = 100000001 (Chat); the handler returns Chat;
        // adapter accepts. This contract is the basis for runtime dispatch.
        var handler = CreateHandler();
        var supports = handler.SupportedInvocationContexts;

        ((supports & InvocationContextKind.Chat) == InvocationContextKind.Chat)
            .Should().BeTrue(
                because: "ToolHandlerToAIFunctionAdapter rejects handlers without Chat invocation support; the row's sprk_availableincontexts=Chat must align");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Test helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private WebSearchHandler CreateHandler() => CreateHandler(BuildNullHttpHandler(), apiKey: null);

    private WebSearchHandler CreateHandler(Mock<HttpMessageHandler> handlerMock, string? apiKey)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            // The handler enforces a 5s timeout on the inner client per-call, but tests
            // use very fast mocks so a long outer timeout is fine.
            Timeout = TimeSpan.FromSeconds(30)
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(WebSearchHandler.HttpClientName))
            .Returns(httpClient);

        var configData = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            configData["BingSearch:ApiKey"] = apiKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new WebSearchHandler(
            factoryMock.Object,
            configuration,
            CreateLogger<WebSearchHandler>());
    }

    private static Mock<HttpMessageHandler> BuildHttpHandler(HttpStatusCode statusCode, string body)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        return mock;
    }

    private static Mock<HttpMessageHandler> BuildNullHttpHandler()
    {
        // For tests that bypass HTTP entirely (no-API-key fallback path). The setup
        // throws if HTTP is ever called; mock-only tests don't trigger HTTP.
        var mock = new Mock<HttpMessageHandler>();
        return mock;
    }

    private static AnalysisTool BuildWebSearchTool() =>
        BuildAnalysisTool(
            handlerClass: nameof(WebSearchHandler),
            configuration: "{}",
            toolType: ToolType.Custom);

    private static string BuildBingResponseJson(params (string title, string url, string snippet)[] pages)
    {
        if (pages.Length == 0)
        {
            return "{\"webPages\":{\"value\":[]}}";
        }

        var values = string.Join(",", pages.Select(p =>
            $"{{\"name\":\"{EscapeJson(p.title)}\",\"url\":\"{EscapeJson(p.url)}\",\"snippet\":\"{EscapeJson(p.snippet)}\"}}"));
        return $"{{\"webPages\":{{\"value\":[{values}]}}}}";
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IReadOnlyList<ToolResultCitation> ExtractCitations(ToolResult result)
    {
        var raw = result.Metadata![ToolResultMetadataKeys.Citations];
        return raw switch
        {
            IReadOnlyList<ToolResultCitation> list => list,
            IEnumerable<ToolResultCitation> enumerable => enumerable.ToList(),
            _ => throw new InvalidOperationException(
                $"Unexpected citations metadata shape: {raw?.GetType().FullName ?? "<null>"}")
        };
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
