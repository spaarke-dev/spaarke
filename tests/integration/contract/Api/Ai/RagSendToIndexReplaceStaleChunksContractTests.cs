using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Task 048 (spaarkeai-word-add-in-r1) — <c>POST /api/ai/rag/send-to-index</c> is the "manual Run Index"
/// path named explicitly in the task brief: it re-indexes a document by Dataverse id (the ribbon
/// button), which by definition already exists and may already carry chunks from an earlier index. Before
/// this task, a re-index that shrank the document left the previous index's tail chunks behind.
/// </summary>
/// <remarks>
/// <para><b>Module boundaries substituted</b> (ADR-038 "mock at module boundaries" — nothing here is
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, a DI-registration assertion, or a ctor null-check):
/// <see cref="IDocumentDataverseService"/> and <see cref="IFileIndexingService"/>, the same two boundaries
/// task 033's <c>RagSendToIndexIndexNameContractTests.cs</c> already substitutes for this exact route.
/// </para>
/// <para><b>Local fixture</b> (project boundary rule): this file owns its own
/// <see cref="WebApplicationFactory{TEntryPoint}"/> subclass and fake auth handler rather than extending
/// task 033's fixture, so a concurrent PR touching that file cannot conflict with this one.</para>
/// </remarks>
[Trait("category", "contract")]
public sealed class RagSendToIndexReplaceStaleChunksContractTests : IClassFixture<RagSendToIndexReplaceStaleChunksFixture>
{
    private readonly RagSendToIndexReplaceStaleChunksFixture _fixture;

    public RagSendToIndexReplaceStaleChunksContractTests(RagSendToIndexReplaceStaleChunksFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    private static readonly Guid DocumentId = Guid.Parse("55555555-0000-0000-0000-000000000048");

    private static DocumentEntity IndexableDocument() => new()
    {
        Id = DocumentId.ToString(),
        Name = "Shrunk Master Services Agreement.docx",
        FileName = "Shrunk Master Services Agreement.docx",
        GraphDriveId = "b!drive-id-048",
        GraphItemId = "item-id-048",
    };

    [Fact]
    public async Task SendToIndex_ReindexingAnExistingDocument_SetsReplaceStaleChunksTrueOnTheFileIndexRequest()
    {
        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexableDocument());

        FileIndexRequest? capturedIndexRequest = null;
        _fixture.FileIndexingMock
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) => capturedIndexRequest = req)
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1), documentId: DocumentId.ToString()));

        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(DocumentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = [DocumentId.ToString()],
            TenantId = RagSendToIndexReplaceStaleChunksFixture.TestTenantId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();
        body!.Results[0].Success.Should().BeTrue();

        capturedIndexRequest.Should().NotBeNull();
        capturedIndexRequest!.ReplaceStaleChunks.Should().BeTrue(
            "Send to Index re-indexes a document that already exists in Dataverse and may already carry " +
            "chunks from an earlier index — a shorter re-index must not leave a stale tail behind " +
            "(task 029's DeleteChunksBeyondCountAsync trim, applied here for the first time by task 048)");
    }
}

/// <summary>
/// Local <see cref="WebApplicationFactory{TEntryPoint}"/> for this file only — mirrors task 033's
/// <c>RagSendToIndexFixture</c> host configuration verbatim (same required settings to boot
/// <c>Program</c>), with its own auth scheme name and mock instances so the two files never share state.
/// </summary>
public sealed class RagSendToIndexReplaceStaleChunksFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-rag-send-to-index-048";

    public Mock<IDocumentDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IFileIndexingService> FileIndexingMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// What Dataverse answers about this caller's rights. Added by task 063 (finding F2): the route
    /// now authorizes every document for <c>Write</c> before it stamps the row, so a fixture that
    /// grants nothing gets a correct 403 and this file's stale-chunk assertions never reach their
    /// subject. The authorization contract itself is pinned by
    /// <c>SendToIndexAuthorizationContractTests</c>; the grant below is narrow rather than a blanket
    /// allow — only this file's document, only the rights the route needs.
    /// </summary>
    public ProgrammableRecordAccessSource Access { get; } = new();

    public void ResetBoundaries()
    {
        DataverseMock.Reset();
        FileIndexingMock.Reset();

        Access.Reset();
        Access.Grant(Guid.Parse("55555555-0000-0000-0000-000000000048"), AccessRights.Read | AccessRights.Write);
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
                options.DefaultAuthenticateScheme = RagSendToIndexReplaceStaleChunksFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RagSendToIndexReplaceStaleChunksFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RagSendToIndexReplaceStaleChunksFakeAuthHandler>(
                RagSendToIndexReplaceStaleChunksFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = RagSendToIndexReplaceStaleChunksFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RagSendToIndexReplaceStaleChunksFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IFileIndexingService>();
            services.AddSingleton(FileIndexingMock.Object);

            // Task 063: the per-document Write check runs through the REAL AuthorizationService over
            // this deny-by-default source. See the remarks on the Access property.
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
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
/// Fake auth handler local to this file — authenticates on the presence of any Authorization header.
/// </summary>
internal sealed class RagSendToIndexReplaceStaleChunksFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RagSendToIndexReplaceStaleChunksFakeAuth";

    public RagSendToIndexReplaceStaleChunksFakeAuthHandler(
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
            new("oid", "rag-send-to-index-048-test-user"),
            new("tid", RagSendToIndexReplaceStaleChunksFixture.TestTenantId),
            new(ClaimTypes.NameIdentifier, "rag-send-to-index-048-test-user"),
            new(ClaimTypes.Name, "RagSendToIndex ReplaceStaleChunks Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
