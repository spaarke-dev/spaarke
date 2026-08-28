// ai-advanced-capabilities-agreements-r1 task 021 (FR-08 pack binding) — vertical-slice seam test
// (KEEP path: tests/integration/seam/** — end-to-end across the dispatch -> registry-lookup seam
// with production types; ADR-038 §E-40 / tests/CLAUDE.md).
//
// THE GAP THIS CLOSES: task 003 found ActionRunner.RetrieveReferenceGroundingAsync never scoped
// reference-knowledge search to a classified agreement type (whole-corpus retrieval crowds out the
// matched standard). Task 021 threads an optional `subDomain` dispatch arg through
// SessionDispatchOrchestrator -> a sprk_agreementtype registry lookup -> LinearRunContext.
// KnowledgeSourceIds. This test proves that threading THROUGH THE WIRE over the REAL app
// (WebApplicationFactory<Program>, REAL ChatSessionManager + ContextBinder + OutputRouter), with
// only the routing/action/registry-reader/executor module boundaries doubled (ADR-038):
//   (a) a dispatch carrying `subDomain` matching a registry row with a KnowledgePackRef threads
//       EXACTLY that pack ref into the executor's LinearRunContext.KnowledgeSourceIds;
//   (b) a dispatch with NO `subDomain` arg (every pre-021 caller) threads KnowledgeSourceIds=null —
//       the additive-safety regression pin;
//   (c) a dispatch with a `subDomain` that matches NO registry row degrades to null (never throws).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Classification;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// FR-08 pack-binding seam tests over the REAL app. See file header for the gap this closes.
/// </summary>
public sealed class AgreementReviewKnowledgeScopeSeamTests : IClassFixture<AgreementReviewKnowledgeScopeSeamFixture>
{
    private readonly AgreementReviewKnowledgeScopeSeamFixture _fx;

    public AgreementReviewKnowledgeScopeSeamTests(AgreementReviewKnowledgeScopeSeamFixture fx) => _fx = fx;

    private async Task<string> SeedSessionAsync(string fileId = "review-file-1")
    {
        var sessionId = Guid.NewGuid().ToString("D");
        await _fx.Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: sessionId,
            TenantId: AgreementReviewKnowledgeScopeSeamFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[]
            {
                new ChatSessionFile(
                    FileId: fileId, FileName: "acme-nda.pdf", ContentType: "application/pdf",
                    SizeBytes: 64, SearchDocumentIdsCsv: $"{fileId}_s_0",
                    UploadedAt: DateTimeOffset.UtcNow)
                {
                    ExtractedText = "This Mutual NDA governs...",
                },
            }) { OwnerOid = TestSessionOwner.Oid });
        return sessionId;
    }

    [Fact]
    public async Task Dispatch_WithSubDomainMatchingRegistryRow_ThreadsKnowledgePackRefIntoLinearRunContext()
    {
        _fx.SeedAgreementReviewBinding();
        _fx.RegistryReaderMock
            .Setup(r => r.GetRowsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AgreementTypeRow("nda", "NDA", null, false, null, KnowledgePackRef: "KNW-011"),
                new AgreementTypeRow("general", "General", null, true, null, KnowledgePackRef: "KNW-012"),
            });

        var sessionId = await SeedSessionAsync();
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "review-file-1" }, subDomain = "nda" } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.CapturedContext.Should().NotBeNull("the REAL SessionDispatchOrchestrator must have invoked the mocked IActionRunner");
        _fx.CapturedContext!.KnowledgeSourceIds.Should().BeEquivalentTo(new[] { "KNW-011" },
            "the classified subDomain 'nda' resolves to its registry row's sprk_knowledgepackref — " +
            "scoping ActionRunner's reference-knowledge search instead of the whole corpus (task 003 finding)");
    }

    [Fact]
    public async Task Dispatch_WithNoSubDomainArg_ThreadsNullKnowledgeSourceIds_AdditiveSafetyPin()
    {
        _fx.SeedAgreementReviewBinding();
        _fx.RegistryReaderMock
            .Setup(r => r.GetRowsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AgreementTypeRow("nda", "NDA", null, false, null, KnowledgePackRef: "KNW-011") });

        var sessionId = await SeedSessionAsync();
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "review-file-1" } } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.CapturedContext.Should().NotBeNull();
        _fx.CapturedContext!.KnowledgeSourceIds.Should().BeNull(
            "every pre-021 dispatch (no subDomain arg) must run UNSCOPED — byte-identical to prior behavior");
        _fx.RegistryReaderMock.Verify(r => r.GetRowsAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the registry is not even READ when no subDomain arg is present (no wasted Dataverse call)");
    }

    [Fact]
    public async Task Dispatch_WithSubDomainMatchingNoRegistryRow_DegradesToNullNeverThrows()
    {
        _fx.SeedAgreementReviewBinding();
        _fx.RegistryReaderMock
            .Setup(r => r.GetRowsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AgreementTypeRow("nda", "NDA", null, false, null, KnowledgePackRef: "KNW-011") });

        var sessionId = await SeedSessionAsync();
        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/dispatch",
            new { bindingId = AgreementReviewKnowledgeScopeSeamFixture.ReviewBindingId.ToString(), args = new { fileIds = new[] { "review-file-1" }, subDomain = "employment" } });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK,
            "an unmatched subDomain degrades to unscoped grounding — it must never fail the dispatch");
        _fx.CapturedContext.Should().NotBeNull();
        _fx.CapturedContext!.KnowledgeSourceIds.Should().BeNull();
    }
}

/// <summary>
/// Boots the REAL app (WebApplicationFactory&lt;Program&gt;) with module-boundary doubles for
/// routing/action-resolution/executor/registry-reader (ADR-038) — mirrors
/// ComposeDocSessionDispatchSeamFixture's config + fake-auth pattern. <see cref="IActionRunner"/> is
/// mocked directly (rather than left real, as the sibling fixture does) because the assertion under
/// test is EXACTLY the <see cref="LinearRunContext"/> SessionDispatchOrchestrator constructs and
/// hands to it — capturing that argument is the most direct proof of the new registry-lookup logic,
/// without depending on live Azure AI Search connectivity for ReferenceRetrievalService.
/// </summary>
public sealed class AgreementReviewKnowledgeScopeSeamFixture : WebApplicationFactory<Program>
{
    public const string TenantId = "00000000-0000-0000-0000-0000000000ee";

    internal static readonly Guid ReviewBindingId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    internal static readonly Guid ReviewActionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    public ChatSessionManager Sessions { get; } = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IScopeResolverService> ScopeResolverMock { get; } = new();
    public Mock<IAgreementTypeRegistryReader> RegistryReaderMock { get; } = new();
    public Mock<IActionRunner> ActionRunnerMock { get; } = new();

    /// <summary>The LinearRunContext captured off the mocked IActionRunner's most recent invocation.</summary>
    public LinearRunContext? CapturedContext { get; private set; }

    /// <summary>
    /// The AnalysisAction captured off the mocked IActionRunner's most recent invocation — ADDED by
    /// ai-advanced-capabilities-agreements-r1 task 070 so <c>AgreementReviewDepthModelTierSeamTests</c>
    /// (a sibling test class reusing THIS fixture, §11 reuse-first — no second WebApplicationFactory
    /// boilerplate) can assert the effective <see cref="AnalysisAction.ModelTier"/> the
    /// `reviewDepth` dispatch arg resolves to. Purely additive — the existing capture of
    /// <see cref="CapturedContext"/> and every existing assertion in this file are unchanged.
    /// </summary>
    public AnalysisAction? CapturedAction { get; private set; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AgreementReviewSeamFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AgreementReviewSeamFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AgreementReviewSeamFakeAuthHandler>(
                AgreementReviewSeamFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = AgreementReviewSeamFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = AgreementReviewSeamFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            services.RemoveAll<IComposeService>();
            services.AddSingleton(Mock.Of<IComposeService>());

            services.RemoveAll<ChatSessionManager>();
            services.AddSingleton(Sessions);

            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(ConsumerRoutingMock.Object);
            services.RemoveAll<IScopeResolverService>();
            services.AddSingleton(ScopeResolverMock.Object);

            // task 021: the registry reader is a module boundary — its LIVE Dataverse read is
            // doubled here; the reader's own contract (columns, mapping) is covered by
            // AgreementTypeRegistryPromptAssemblerTests / the task-020 catalog contract tests.
            services.RemoveAll<IAgreementTypeRegistryReader>();
            services.AddSingleton(RegistryReaderMock.Object);

            // Capture the LinearRunContext SessionDispatchOrchestrator constructs — the assertion
            // under test. Returns a minimal well-formed completion so OutputRouter can store it.
            ActionRunnerMock
                .Setup(r => r.RunAsync(
                    It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
                .Callback<AnalysisAction, BoundInputs, LinearRunContext, CancellationToken>(
                    (action, _, ctx, _) => { CapturedAction = action; CapturedContext = ctx; })
                .ReturnsAsync(JsonDocument.Parse("""{"overallRisk":"low","flaggedSections":[]}""").RootElement.Clone());
            services.RemoveAll<IActionRunner>();
            services.AddSingleton(ActionRunnerMock.Object);
        });
    }

    /// <summary>Seeds the nda-review Binding (Informational; AllowsKnowledge — the FR-08 pack-binding target) + its Action.</summary>
    public void SeedAgreementReviewBinding()
    {
        ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(ReviewBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ReviewBindingId,
                ConsumerType = "nda-review",
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ReviewActionId,
                ActionKind = ActionKind.Prompted,
                Disposition = BindingDisposition.Informational,
            });

        ScopeResolverMock
            .Setup(s => s.GetActionAsync(ReviewActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ReviewActionId,
                Name = "Agreement Review",
                SystemPrompt = "Review the document against the retrieved standard.",
                OutputSchemaJson = """{"type":"object","additionalProperties":false,"required":["overallRisk","flaggedSections"],"properties":{"overallRisk":{"type":"string"},"flaggedSections":{"type":"array"}}}""",
                AllowsKnowledge = true,
            });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler emitting BOTH tid (tenant) + oid (user) claims.</summary>
internal sealed class AgreementReviewSeamFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AgreementReviewSeamFakeAuth";

    public AgreementReviewSeamFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(oid)) oid = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", AgreementReviewKnowledgeScopeSeamFixture.TenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Agreement Review Seam Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
