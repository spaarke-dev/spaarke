using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="LegalResearchHandler"/> (R6 Wave 8 — replaces the legacy
/// hardcoded <c>LegalResearchTools</c> class previously registered on
/// <c>SprkChatAgentFactory</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentServiceClient</c> is <c>sealed</c> and cannot be mocked with Moq directly.
/// Tests use the test-only <see cref="TestableLegalResearchHandler"/> subclass below which
/// overrides the <c>internal virtual RunBingGroundingAsync</c> seam — this isolates the
/// handler's orchestration (kill switch, sanitization, citation building, error mapping)
/// without requiring a live Foundry endpoint or mocking sealed SDK types.
/// </para>
/// <para>
/// Coverage:
/// <list type="bullet">
/// <item>4-point contract (R6 Pillar 2): assembly discovery, HandlerId, Metadata, SupportedToolTypes</item>
/// <item>SupportedInvocationContexts = Chat (NOT Both) — preserves pre-R6 chat-only registration</item>
/// <item>ValidateChat per method discriminator (ResearchLegal needs 'topic', LookupCase needs 'citation')</item>
/// <item>ADR-018 kill switch: Enabled=false short-circuits BEFORE any Bing call (no RunBingGroundingAsync invocation)</item>
/// <item>ADR-015 PII sanitization: query containing "Client:" / "Matter NNNN" / emails gets sanitized BEFORE being forwarded to RunBingGroundingAsync (assert sanitized text reaches the seam)</item>
/// <item>Happy path: ResearchLegal returns citations envelope + populated payload</item>
/// <item>Happy path: LookupCase wraps the citation in a site:-anchored query before forwarding</item>
/// <item>Citation envelope: SourceType="BingGrounding", URL populated</item>
/// <item>ADR-015 telemetry: query text / citation text / result URLs do NOT leak into logs (sentinel scan via TypedToolHandlerTestFixture)</item>
/// <item>Cancellation → ToolResult.Error(Cancelled)</item>
/// <item>RunBingGroundingAsync throws → ToolResult.Error(InternalError)</item>
/// <item>ConcurrencyLimitExceededException → graceful degradation</item>
/// </list>
/// </para>
/// </remarks>
public sealed class LegalResearchHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // Sentinel strings for ADR-015 telemetry assertions — recognisable and not present
    // in any handler log format string.
    //
    // Note on `PiiTopicWithClient` shape: the legacy sanitizer's `ClientPrefixPattern`
    // matches `Client: <name>` up to a `,`, `;`, `:`, or newline — em-dashes don't
    // terminate the match, so a query like "Client: AcmeCorp — what GDPR..." would have
    // the WHOLE remainder stripped. The realistic phrasing the regex expects is a
    // comma-terminated client prefix, e.g. "Client: AcmeCorp, what GDPR..." — and that's
    // what we use here so the test asserts the sanitizer's actual behavior contract.
    private const string PiiTopicWithClient =
        "Client: AcmeCorpFortressIndustries, what GDPR cross-border transfer rules apply to EU subsidiaries?";
    private const string PiiCitation =
        "I am tracking Matter 2099-0187. The court in Roe v. Wade, 410 U.S. 113 (1973) held that...";
    private const string SecretResultUrl = "https://example.test/secret/CASE-PRIVATE-XYZ-123";

    // ─────────────────────────────────────────────────────────────────────────────
    // Handler construction helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static AgentServiceClient BuildDummyAgentServiceClient()
    {
        // AgentServiceClient is sealed but the constructor only requires IOptions +
        // IDistributedCache + TokenCredential + ILogger — none of which we invoke in
        // tests (RunBingGroundingAsync is overridden in TestableLegalResearchHandler).
        // The dummy options have Enabled=false so any accidental call to the underlying
        // SDK would throw FeatureDisabledException quickly.
        var options = Options.Create(new AgentServiceOptions { Enabled = false, AgentId = "test", Endpoint = new Uri("https://test.invalid") });
        // FR-05 redis remediation r1: AgentServiceClient now takes ITenantCache.
        var dc = new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
            Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions()));
        var tenantCache = new Sprk.Bff.Api.Infrastructure.Cache.TenantCache(
            dc,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Infrastructure.Cache.TenantCache>());
        var credential = new Azure.Identity.DefaultAzureCredential();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentServiceClient>();
        return new AgentServiceClient(options, tenantCache, credential, logger);
    }

    private TestableLegalResearchHandler CreateHandler(
        bool bingEnabled = true,
        Func<string, CancellationToken, Task<List<LegalResearchHandler.GroundingResult>>>? groundingOverride = null,
        BingGroundingOptions? overrideOptions = null)
    {
        var options = Options.Create(overrideOptions ?? new BingGroundingOptions
        {
            Enabled = bingEnabled,
            BingConnectionName = "test-conn",
            MaxConcurrency = 3,
            MaxResultsPerQuery = 5
        });

        return new TestableLegalResearchHandler(
            BuildDummyAgentServiceClient(),
            options,
            CreateLogger<LegalResearchHandler>(),
            groundingOverride);
    }

    private static AnalysisTool BuildLegalTool(string method) =>
        BuildAnalysisTool(
            handlerClass: nameof(LegalResearchHandler),
            configuration: $"{{\"method\":\"{method}\"}}",
            toolType: ToolType.Custom);

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
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
            typeof(LegalResearchHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(LegalResearchHandler),
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
            because: "R6 Wave 8: LegalResearchHandler is chat-only — the pre-R6 hardcoded LegalResearchTools registration was chat-only, no playbook node executor exists for legal_research per NFR-08");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape per method
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithTopic_ResearchLegal()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"GDPR compliance\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithCitation_LookupCase()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"citation\":\"123 F.3d 456\"}");
        var tool = BuildLegalTool("LookupCase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenTopicMissing_ResearchLegal()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("topic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenCitationMissing_LookupCase()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildLegalTool("LookupCase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("citation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"x\"}", tenantId: "");
        var tool = BuildLegalTool("ResearchLegal");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"x\"}");
        var tool = BuildLegalTool("CallTheLawyersImmediately");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }


    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-018 kill switch — BingGroundingOptions.Enabled = false short-circuits
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ResearchLegal_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall()
    {
        var groundingCalled = false;
        var handler = CreateHandler(
            bingEnabled: false,
            groundingOverride: (_, _) =>
            {
                groundingCalled = true;
                return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
            });

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"GDPR\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be(LegalResearchHandler.ResearchLegalDisabledMessage);
        groundingCalled.Should().BeFalse(
            because: "ADR-018: kill switch MUST short-circuit BEFORE any Bing call");
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupCase_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall()
    {
        var groundingCalled = false;
        var handler = CreateHandler(
            bingEnabled: false,
            groundingOverride: (_, _) =>
            {
                groundingCalled = true;
                return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
            });

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"citation\":\"123 F.3d 456\"}");
        var tool = BuildLegalTool("LookupCase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be(LegalResearchHandler.LookupCaseDisabledMessage);
        groundingCalled.Should().BeFalse(
            because: "ADR-018: kill switch MUST short-circuit BEFORE any Bing call");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 PII sanitization — sanitizer runs BEFORE Bing call
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ResearchLegal_SanitizesQuery_BeforeForwardingToBing()
    {
        var capturedQuery = (string?)null;
        var handler = CreateHandler(
            bingEnabled: true,
            groundingOverride: (q, _) =>
            {
                capturedQuery = q;
                return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: System.Text.Json.JsonSerializer.Serialize(new { topic = PiiTopicWithClient }));
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedQuery.Should().NotBeNull(because: "Bing path should have been reached after sanitization");
        capturedQuery.Should().NotContain("Client:",
            because: "ADR-015: 'Client:' prefix MUST be sanitized BEFORE the query is forwarded to Bing");
        capturedQuery.Should().NotContain("AcmeCorpFortressIndustries",
            because: "ADR-015: client name in 'Client: X' prefix MUST be stripped before the query leaves the BFF");
        capturedQuery.Should().Contain("GDPR",
            because: "the sanitizer must preserve the legal-research-relevant portion of the query");
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupCase_SanitizesCitationPreamble_WhilePreservingCitation()
    {
        var capturedQuery = (string?)null;
        var handler = CreateHandler(
            bingEnabled: true,
            groundingOverride: (q, _) =>
            {
                capturedQuery = q;
                return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
            });

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: System.Text.Json.JsonSerializer.Serialize(new { citation = PiiCitation }));
        var tool = BuildLegalTool("LookupCase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedQuery.Should().NotBeNull();
        capturedQuery.Should().Contain("[MATTER-REF]",
            because: "ADR-015: 'Matter NNNN-NNNN' MUST be replaced before the query leaves the BFF");
        capturedQuery.Should().NotContain("2099-0187",
            because: "ADR-015: matter reference digits MUST not appear in the outbound Bing query");
        capturedQuery.Should().Contain("Roe v. Wade",
            because: "the sanitizer must preserve the case citation itself — it IS the search target");
        capturedQuery.Should().Contain("law.justia.com",
            because: "LookupCase MUST anchor the Bing query to authoritative legal databases via site: filters");
    }

    [Fact]
    public void QuerySanitizer_RemovesClientPrefix_ReplacesMatterRefs_AndEmails()
    {
        var input = "Client: AcmeCorp, matter 2024-0187 — email from john.doe@client.com about NDA";
        var sanitized = InvokeSanitizer(input);

        sanitized.Should().NotContain("Client:");
        sanitized.Should().NotContain("AcmeCorp");
        sanitized.Should().Contain("[MATTER-REF]");
        sanitized.Should().NotContain("2024-0187");
        sanitized.Should().Contain("[EMAIL]");
        sanitized.Should().NotContain("john.doe@client.com");
    }

    /// <summary>
    /// Invokes the handler's internal QuerySanitizer via reflection — the sanitizer lives
    /// inside the handler as an internal nested class with internal static Sanitize. Reflection
    /// keeps the test target stable without requiring the sanitizer to be public.
    /// </summary>
    private static string InvokeSanitizer(string input)
    {
        var nested = typeof(LegalResearchHandler).GetNestedType("QuerySanitizer", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("QuerySanitizer nested type not found");
        var method = nested.GetMethod("Sanitize", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("QuerySanitizer.Sanitize method not found");
        return (string)method.Invoke(null, new object[] { input })!;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Happy paths — citations metadata + result formatting
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ResearchLegal_ReturnsCitations_AndPayload()
    {
        var groundingResults = new List<LegalResearchHandler.GroundingResult>
        {
            new("GDPR overview", "https://eur-lex.europa.eu/gdpr", string.Empty, 1),
            new("Article 49 transfer derogations", "https://www.justia.com/gdpr-49", string.Empty, 2)
        };

        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(groundingResults));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"GDPR transfer rules\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);

        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IReadOnlyList<ToolResultCitation>
                        ?? (result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>)?.ToList();
        citations.Should().NotBeNull();
        citations!.Should().HaveCount(2);
        citations![0].SourceName.Should().Be("GDPR overview");
        citations![0].Url.Should().Be("https://eur-lex.europa.eu/gdpr");
        citations![0].SourceType.Should().Be("BingGrounding",
            because: "ADR-015 governance: Bing-grounded citations carry the 'BingGrounding' source-type discriminator");

        var payload = result.GetData<LegalResearchHandler.LegalResearchPayload>();
        payload!.Method.Should().Be("ResearchLegal");
        payload.ResultCount.Should().Be(2);
        payload.Content.Should().Contain("[Legal Source]");
    }

    [Fact]
    public async Task ExecuteChatAsync_LookupCase_ReturnsCaseHoldingAndSourceUrl()
    {
        var groundingResults = new List<LegalResearchHandler.GroundingResult>
        {
            new("Roe v. Wade, 410 U.S. 113 (1973)", "https://law.justia.com/cases/federal/us/410/113", string.Empty, 1)
        };

        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(groundingResults));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"citation\":\"Roe v. Wade, 410 U.S. 113 (1973)\"}");
        var tool = BuildLegalTool("LookupCase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IReadOnlyList<ToolResultCitation>
                        ?? (result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>)?.ToList();
        citations.Should().NotBeNull().And.HaveCount(1);
        citations![0].Url.Should().Be("https://law.justia.com/cases/federal/us/410/113");
        citations![0].SourceType.Should().Be("BingGrounding");

        var payload = result.GetData<LegalResearchHandler.LegalResearchPayload>();
        payload!.Method.Should().Be("LookupCase");
        payload.Content.Should().Contain("Roe v. Wade");
    }

    [Fact]
    public async Task ExecuteChatAsync_ResearchLegal_EmptyResults_StillSuccessful()
    {
        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(new List<LegalResearchHandler.GroundingResult>()));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"obscure topic\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<LegalResearchHandler.LegalResearchPayload>();
        payload!.ResultCount.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validation / cancellation / failure paths
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationError_WhenTopicMissing_ResearchLegal()
    {
        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(new List<LegalResearchHandler.GroundingResult>()));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("topic");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler(
            groundingOverride: (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
            });

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"x\"}");
        var tool = BuildLegalTool("ResearchLegal");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenGroundingThrows()
    {
        var handler = CreateHandler(
            groundingOverride: (_, _) => throw new InvalidOperationException("Foundry endpoint unreachable"));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"x\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsDegradation_WhenConcurrencyLimitExceeded()
    {
        var handler = CreateHandler(
            groundingOverride: (_, _) => throw new ConcurrencyLimitExceededException("gate timed out"));

        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"topic\":\"x\"}");
        var tool = BuildLegalTool("ResearchLegal");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "ADR-016 concurrency gate timeouts MUST degrade gracefully — never bubble 429 to the LLM");
        result.Summary.Should().Be(LegalResearchHandler.ResearchCapacityMessage);
    }

    [Fact]
    public void ExecuteAsync_PlaybookContext_Throws_NotSupportedException()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildLegalTool("ResearchLegal");

        var act = () => handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        act.Should().ThrowAsync<NotSupportedException>(
            because: "LegalResearchHandler is chat-only — invoking via the playbook path is a programming error");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — sentinel scan
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_DoesNotLogQueryText_OrResultUrls()
    {
        const string secretTopic = "TOPIC-WITH-CLIENT-AcmeFortressCorp-GDPR-RULES";
        const string secretSnippetSentinel = "EXCERPT-WITH-PRIVILEGED-CONTENT-Trade-Secret-Formula-XJ7K";

        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(new List<LegalResearchHandler.GroundingResult>
            {
                new("Result", SecretResultUrl, secretSnippetSentinel, 1)
            }));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: System.Text.Json.JsonSerializer.Serialize(new { topic = secretTopic }));
        var tool = BuildLegalTool("ResearchLegal");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretTopic, secretSnippetSentinel, SecretResultUrl);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_LookupCase_DoesNotLogCitationText()
    {
        const string secretCitation = "CITATION-WITH-CONFIDENTIAL-CASE-NAME-SmithVJonesPrivateMatter";

        var handler = CreateHandler(
            groundingOverride: (_, _) => Task.FromResult(new List<LegalResearchHandler.GroundingResult>()));

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: System.Text.Json.JsonSerializer.Serialize(new { citation = secretCitation }));
        var tool = BuildLegalTool("LookupCase");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretCitation);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DI bootstrap helper
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Test-only handler subclass — overrides the internal-virtual RunBingGroundingAsync
    // seam so tests don't need a live Foundry endpoint or sealed-type mocking.
    // ═════════════════════════════════════════════════════════════════════════════

    private sealed class TestableLegalResearchHandler : LegalResearchHandler
    {
        private readonly Func<string, CancellationToken, Task<List<LegalResearchHandler.GroundingResult>>>? _groundingOverride;

        public TestableLegalResearchHandler(
            AgentServiceClient agentServiceClient,
            IOptions<BingGroundingOptions> options,
            ILogger<LegalResearchHandler> logger,
            Func<string, CancellationToken, Task<List<LegalResearchHandler.GroundingResult>>>? groundingOverride)
            : base(agentServiceClient, options, logger)
        {
            _groundingOverride = groundingOverride;
        }

        internal override Task<List<LegalResearchHandler.GroundingResult>> RunBingGroundingAsync(
            string sanitizedQuery,
            CancellationToken cancellationToken)
        {
            if (_groundingOverride is not null)
                return _groundingOverride(sanitizedQuery, cancellationToken);

            // Default: empty results (lets kill-switch tests verify NO call to base.RunBingGrounding).
            return Task.FromResult(new List<LegalResearchHandler.GroundingResult>());
        }
    }
}
