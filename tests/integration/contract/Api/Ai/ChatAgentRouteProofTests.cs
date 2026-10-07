// unified-access-control-r2 — "ProvenByTest" route-authorization proofs for the chat and agent family, over the REAL app.
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Ai/**`
//   - Justification: the route ledger credits a route as "ProvenByTest" only when ONE test drives the REAL app
//     (WebApplicationFactory<Program>) and shows BOTH outcomes on that route: the refusal for a caller who may not, and
//     success for one who may. ChatContextAuthorizationContractTests pins the full deny/allow tables, but over a SLIM
//     host (WebApplication.CreateBuilder + the real mappers), which is not the real Program pipeline. This file is the
//     real-Program counterpart for S-24, S-18 and S-78.
//
// What is REAL: Program's whole pipeline — RequireAuthorization, AgentAuthorizationFilter, AiAuthorizationFilter's
// chat-context evaluation, AiAuthorizationService, the handlers, ChatSessionManager over the host's tenant cache.
// Substituted (module boundaries only, ADR-038 §4): what Dataverse answers about the caller's rights (CallerAccessSeam),
// the chat session repository, the agent construction seam (SprkChatAgentFactory → a stub agent: the LLM boundary) and
// the playbook run-status store (IPlaybookOrchestrationService). No Mock<HttpMessageHandler>, no DI-registration
// assertion, no ctor null-check.

using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Both outcomes of S-24 (<c>POST /api/ai/chat/sessions</c>), S-18 (<c>POST /api/agent/message</c>) and S-78
/// (<c>GET /api/agent/playbooks/status/{jobId:guid}</c>), each in one test, over the REAL app.
/// </summary>
public sealed class ChatAgentRouteProofTests : IClassFixture<ChatAgentRouteProofFixture>
{
    private readonly ChatAgentRouteProofFixture _fixture;

    public ChatAgentRouteProofTests(ChatAgentRouteProofFixture fixture) => _fixture = fixture;

    /// <summary>
    /// S-24: AiAuthorizationFilter's chat-context evaluation decides the body document AS THE CALLER before the session
    /// is created. A document the caller cannot read is the uniform 403 and no session is stored; a reader gets 201 and
    /// the session carries that document.
    /// </summary>
    [Fact]
    public async Task ProvenByTest_ChatSessionCreate_UnreadableDocumentIs403_ReadableDocumentIs201()
    {
        _fixture.ResetBoundaries();
        var unreadable = Guid.NewGuid();
        var readable = Guid.NewGuid();
        _fixture.Access.Grant(unreadable, AccessRights.AppendTo); // a right, but not Read
        _fixture.Access.Grant(readable, AccessRights.Read);
        using var client = _fixture.CreateCallerClient();

        var denied = await client.PostAsJsonAsync("/api/ai/chat/sessions", new { documentId = unreadable.ToString() });

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deniedBody = await denied.Content.ReadAsStringAsync();
        JsonNode.Parse(deniedBody)!["detail"]!.GetValue<string>().Should().Be(AiAuthorizationFilter.ChatContextAccessDeniedDetail);
        deniedBody.Should().NotContain(unreadable.ToString());
        _fixture.Repo.Verify(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Never(),
            "a denied create never reaches the session store");

        var allowed = await client.PostAsJsonAsync("/api/ai/chat/sessions", new { documentId = readable.ToString() });

        allowed.StatusCode.Should().Be(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        _fixture.Repo.Verify(r => r.CreateSessionAsync(
            It.Is<ChatSession>(s => s.DocumentId == readable.ToString() && s.OwnerOid == RouteSweepAuthorizationFixture.CallerObjectId),
            It.IsAny<CancellationToken>()), Times.Once());
        _fixture.Access.DocumentChecks.Select(c => c.ResourceId).Should().Contain([unreadable.ToString(), readable.ToString()],
            "both documents were decided as the caller through the access data boundary");
    }

    /// <summary>
    /// S-18: the same chat-context evaluation decides the agent body's DocumentId. An unreadable document is the uniform
    /// 403 before any session is created or any agent turn runs; a readable one runs the turn end to end — 200 with the
    /// agent's answer and a session minted on that document.
    /// </summary>
    [Fact]
    public async Task ProvenByTest_AgentMessage_UnreadableBodyDocumentIs403_ReadableOneRunsTheTurnAnd200()
    {
        _fixture.ResetBoundaries();
        var unreadable = Guid.NewGuid();
        var readable = Guid.NewGuid();
        _fixture.Access.Grant(readable, AccessRights.Read);
        using var client = _fixture.CreateCallerClient();

        var denied = await client.PostAsJsonAsync("/api/agent/message", new { message = "Summarize this", documentId = unreadable });

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain(unreadable.ToString());
        _fixture.Repo.Verify(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Never());
        _fixture.Agents.CreatedFor.Should().BeEmpty("no agent turn runs for a refused caller");

        var allowed = await client.PostAsJsonAsync("/api/agent/message", new { message = "Summarize this", documentId = readable });

        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        var answer = JsonNode.Parse(await allowed.Content.ReadAsStringAsync())!;
        answer["responseText"]!.GetValue<string>().Should().Be(ChatAgentRouteProofFixture.AgentAnswer, "the turn ran");
        _fixture.Agents.CreatedFor.Should().Equal([readable.ToString()], "the agent was built on the authorized document");
        _fixture.Repo.Verify(r => r.CreateSessionAsync(
            It.Is<ChatSession>(s => s.DocumentId == readable.ToString()), It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>
    /// S-78: the handler compares the run's recorded owner (PlaybookRunStatus.StartedByOid) with the caller. Another
    /// user's run is the uniform 404 that never echoes the job id; the caller's own run is 200 with its status.
    /// </summary>
    [Fact]
    public async Task ProvenByTest_AgentPlaybookStatus_AnotherUsersRunIs404_TheCallersOwnRunIs200()
    {
        _fixture.ResetBoundaries();
        var foreignRun = Guid.NewGuid();
        var ownRun = Guid.NewGuid();
        _fixture.Runs.Setup(o => o.GetRunStatusAsync(foreignRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookRunStatus
            {
                RunId = foreignRun, State = PlaybookRunState.Completed, StartedAt = DateTimeOffset.UtcNow,
                StartedByOid = "ffffffff-0000-4000-8000-0000000000aa",
            });
        _fixture.Runs.Setup(o => o.GetRunStatusAsync(ownRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookRunStatus
            {
                RunId = ownRun, State = PlaybookRunState.Completed, StartedAt = DateTimeOffset.UtcNow,
                StartedByOid = RouteSweepAuthorizationFixture.CallerObjectId,
            });
        using var client = _fixture.CreateCallerClient();

        var denied = await client.GetAsync($"/api/agent/playbooks/status/{foreignRun}");

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain(foreignRun.ToString());

        var allowed = await client.GetAsync($"/api/agent/playbooks/status/{ownRun}");

        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        var status = JsonNode.Parse(await allowed.Content.ReadAsStringAsync())!;
        status["jobId"]!.GetValue<Guid>().Should().Be(ownRun);
        status["status"]!.GetValue<string>().Should().Be("Completed");
        _fixture.Runs.Verify(o => o.GetRunStatusAsync(foreignRun, It.IsAny<CancellationToken>()), Times.Once(),
            "the refusal came from the owner comparison on a loaded run, not from a missing run");
    }
}

/// <summary>
/// The agent construction seam: records which document each agent was built on and returns a stub agent that answers
/// <see cref="ChatAgentRouteProofFixture.AgentAnswer"/> (the LLM boundary — nothing beyond it is exercised here).
/// </summary>
public sealed class RecordingStubAgentFactory : SprkChatAgentFactory
{
    public RecordingStubAgentFactory(ILogger<SprkChatAgentFactory> logger)
        : base(logger)
    {
    }

    public List<string> CreatedFor { get; } = [];

    public override Task<ISprkChatAgent> CreateAgentAsync(
        string sessionId,
        string documentId,
        Guid? playbookId,
        string tenantId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<string>? additionalDocumentIds = null,
        HttpContext? httpContext = null,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        string? latestUserMessage = null,
        IReadOnlyList<string>? previousTurnToolNames = null,
        IReadOnlyList<ChatSessionFile>? uploadedFiles = null,
        IReadOnlyList<SessionOutput>? ledgerOutputs = null,
        string? activeSessionFileId = null,
        AiModelTier? modelTierOverride = null,
        string? activeContextTabId = null,
        IReadOnlyList<WorkspaceTab>? liveTabs = null,
        WorkspaceActiveItemHandle? activeItem = null,
        IReadOnlyCollection<string>? advisoryToolAllowList = null,
        string? advisorySystemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        CreatedFor.Add(documentId);
        return Task.FromResult<ISprkChatAgent>(new StubAgent());
    }

    private sealed class StubAgent : ISprkChatAgent
    {
        public ChatContext Context => null!;

        public CitationContext? Citations => null;

        public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
            string message,
            IReadOnlyList<AiChatMessage> history,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, ChatAgentRouteProofFixture.AgentAnswer);
        }
    }
}

/// <summary>
/// Boots the REAL Program for the chat / agent proofs. Authenticates as
/// <see cref="RouteSweepAuthorizationFixture.CallerObjectId"/> in <see cref="RouteSweepAuthorizationFixture.CallerTenantId"/>
/// through the task 163 fake scheme, and substitutes only module boundaries.
/// </summary>
public sealed class ChatAgentRouteProofFixture : WebApplicationFactory<Program>
{
    public const string AgentAnswer = "proof-agent-answer";

    /// <summary>What Dataverse would answer about this caller's rights — deny by default.</summary>
    public CallerAccessSeam Access { get; } = new();

    /// <summary>The chat session store (Dataverse persistence of sessions).</summary>
    public Mock<IChatDataverseRepository> Repo { get; } = new(MockBehavior.Loose);

    /// <summary>The playbook run-status store.</summary>
    public Mock<IPlaybookOrchestrationService> Runs { get; } = new(MockBehavior.Loose);

    /// <summary>The agent construction seam (the LLM boundary).</summary>
    public RecordingStubAgentFactory Agents { get; } = new(Mock.Of<ILogger<SprkChatAgentFactory>>());

    public void ResetBoundaries()
    {
        Access.Reset();
        Repo.Reset();
        Runs.Reset();
        Agents.CreatedFor.Clear();
    }

    public HttpClient CreateCallerClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RouteSweepAuthorizationFixture.BearerToken);
        return client;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["Graph:ManagedIdentity:Enabled"] = "false",
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
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
            });
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
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RouteSweepFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RouteSweepFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RouteSweepFakeAuthHandler>(RouteSweepFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = RouteSweepFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RouteSweepFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverseService = new Mock<IDataverseService>();
            dataverseService.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseService.Object);

            // ── The module boundaries this fixture substitutes. ──
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);

            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(Repo.Object);

            services.RemoveAll<IPlaybookOrchestrationService>();
            services.AddSingleton(Runs.Object);

            services.RemoveAll<SprkChatAgentFactory>();
            services.AddSingleton<SprkChatAgentFactory>(Agents);
        });
    }
}
