using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
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
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Task 048 (spaarkeai-word-add-in-r1) — <c>POST /api/ai/knowledge/indexes/reindex/{documentId}</c>
/// ("Triggers a background re-indexing job for the specified document") is a manual admin re-index route
/// this task's audit found in addition to the two named in the task brief. Its idempotency key
/// (<c>rag-reindex-{documentId}-{unixSeconds}</c>) is unique per call, so it always reaches the job
/// payload — before this task the payload never asked for the stale-tail trim.
/// </summary>
/// <remarks>
/// <para><b>Module boundary substituted</b> (ADR-038): <see cref="JobSubmissionService"/> — a concrete
/// singleton (<c>services.AddSingleton&lt;JobSubmissionService&gt;()</c>, <c>JobProcessingModule</c>) with
/// a <c>virtual</c> <c>SubmitJobAsync</c>, substituted the same way the pre-existing
/// <c>PostUploadIndexingEnqueuerTests.cs</c> mocks it. Nothing here is
/// <c>Mock&lt;HttpMessageHandler&gt;</c>; this route needs no Dataverse or SPE read at all (it trusts the
/// caller-supplied <c>DriveId</c>/<c>FileName</c>).</para>
/// <para><b>Local fixture</b> (project boundary rule): this file owns its own
/// <see cref="WebApplicationFactory{TEntryPoint}"/> subclass and fake auth handler.</para>
/// </remarks>
[Trait("category", "contract")]
public sealed class KnowledgeBaseReindexReplaceStaleChunksContractTests : IClassFixture<KnowledgeBaseReindexReplaceStaleChunksFixture>
{
    private readonly KnowledgeBaseReindexReplaceStaleChunksFixture _fixture;

    public KnowledgeBaseReindexReplaceStaleChunksContractTests(KnowledgeBaseReindexReplaceStaleChunksFixture fixture)
    {
        _fixture = fixture;
        _fixture.JobSubmissionMock.Reset();
    }

    private static readonly Guid DocumentId = Guid.Parse("66666666-0000-0000-0000-000000000048");

    [Fact]
    public async Task ReindexDocument_ExistingDocument_SetsReplaceStaleChunksTrueOnTheSubmittedPayload()
    {
        JobContract? capturedJob = null;
        _fixture.JobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/knowledge/indexes/reindex/{DocumentId}",
            new KnowledgeReindexRequest { DriveId = "drive-kb-048", FileName = "kb-doc.pdf" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        capturedJob.Should().NotBeNull();
        capturedJob!.JobType.Should().Be(RagIndexingJobHandler.JobTypeName);

        var payload = JsonSerializer.Deserialize<RagIndexingJobPayload>(
            capturedJob.Payload!.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        payload!.ReplaceStaleChunks.Should().BeTrue(
            "this route's entire purpose is re-indexing a document that may already be indexed — a " +
            "shorter re-index must not leave a stale tail behind");
    }
}

/// <summary>
/// Local <see cref="WebApplicationFactory{TEntryPoint}"/> for this file only — mirrors the same required
/// host configuration used by the sibling RAG contract-test fixtures in this directory.
/// </summary>
public sealed class KnowledgeBaseReindexReplaceStaleChunksFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-kb-reindex-048";

    public Mock<JobSubmissionService> JobSubmissionMock { get; }

    public KnowledgeBaseReindexReplaceStaleChunksFixture()
    {
        var sbOptions = new Mock<IOptions<Sprk.Bff.Api.Configuration.ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new Sprk.Bff.Api.Configuration.ServiceBusOptions
        {
            QueueName = "test-jobs",
            CommunicationQueueName = "test-comms",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        JobSubmissionMock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
    }

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
                ["AiSearch:KnowledgeIndexName"] = "tenant-default-index",
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
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler>(
                KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // The module boundary this file substitutes — no Dataverse/SPE read is needed for this route.
            services.RemoveAll<JobSubmissionService>();
            services.AddSingleton(JobSubmissionMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>
/// Fake auth handler local to this file — authenticates on the presence of any Authorization header and
/// emits the <c>tid</c> claim <see cref="TenantResolution"/> reads.
/// </summary>
internal sealed class KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "KnowledgeBaseReindexReplaceStaleChunksFakeAuth";

    public KnowledgeBaseReindexReplaceStaleChunksFakeAuthHandler(
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

        var claims = new List<Claim>
        {
            new("oid", "kb-reindex-048-test-user"),
            new("tid", KnowledgeBaseReindexReplaceStaleChunksFixture.TestTenantId),
            new(ClaimTypes.NameIdentifier, "kb-reindex-048-test-user"),
            new(ClaimTypes.Name, "KnowledgeBaseReindex ReplaceStaleChunks Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
