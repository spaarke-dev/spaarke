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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// spaarkeai-word-add-in-r1 task 033 — the index-name defect fix for
/// <c>POST /api/ai/rag/send-to-index</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> <c>RagEndpoints.SendToIndex</c> resolved the AI Search index ONCE, before
/// the per-document loop, via <c>searchIndexNameResolver.GetDefaultIndexName()</c> — the tenant
/// default only. A document whose own <c>sprk_searchindexname</c> (or a parent/BU's "AI Search
/// Index" lookup) named a DIFFERENT index was silently routed to the tenant default: not just the
/// Dataverse tracking stamp was wrong, the file index REQUEST itself
/// (<c>FileIndexRequest.SearchIndexName</c>, plumbed end-to-end by multi-container-multi-index-r1)
/// never carried the per-record value at all — it was omitted from the request entirely.
/// </para>
/// <para>
/// <b>The fix</b> (in-handler only, no new resolver, no signature change): per document, call the
/// ALREADY-INJECTED <see cref="ISearchIndexNameResolver.ResolveAsync"/> — the same document → parent
/// → parent's-BU chain <c>RagIndexingJobHandler</c> already uses — and fall back to
/// <see cref="ISearchIndexNameResolver.GetDefaultIndexName"/> only when the chain resolves nothing.
/// </para>
/// <para>
/// <b>Module boundaries substituted</b> (ADR-038 "mock at module boundaries"; nothing here is
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, a DI-registration assertion, or a ctor null-check):
/// <see cref="IDocumentDataverseService"/> (what Dataverse answers for the document row),
/// <see cref="IFileIndexingService"/> (the OBO download + chunk + embed + write pipeline — an
/// external module this endpoint layer does not own), and <see cref="IGenericEntityService"/> (what
/// Dataverse answers for the resolver's own FetchXml lookups). <see cref="ISearchIndexNameResolver"/>
/// itself is the REAL <c>SearchIndexNameResolver</c> — that is the class under test, one boundary in
/// from the endpoint.
/// </para>
/// <para>
/// <b>Local fixture</b> (project boundary rule): this file owns its own
/// <see cref="WebApplicationFactory{TEntryPoint}"/> subclass and its own fake auth handler. No shared
/// test fixture, factory, or auth-handler file was modified — a concurrent task (046) is also
/// touching this test tree.
/// </para>
/// </remarks>
[Trait("category", "contract")]
public sealed class RagSendToIndexIndexNameContractTests : IClassFixture<RagSendToIndexFixture>
{
    private readonly RagSendToIndexFixture _fixture;

    public RagSendToIndexIndexNameContractTests(RagSendToIndexFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    private static readonly Guid DocumentId = Guid.Parse("55555555-0000-0000-0000-000000000001");

    private static DocumentEntity IndexableDocument(string? searchIndexName = null) => new()
    {
        Id = DocumentId.ToString(),
        Name = "Master Services Agreement.docx",
        FileName = "Master Services Agreement.docx",
        GraphDriveId = "b!drive-id",
        GraphItemId = "item-id",
        SearchIndexName = searchIndexName,
    };

    /// <summary>
    /// Builds a single-row FetchXml result carrying the LEGACY text column
    /// (<c>sprk_searchindexname</c>) so <c>SearchIndexNameResolver.ExtractIndexName</c> takes its
    /// migration-safety fallback path — the resolver's step 1 (document-level) resolution.
    /// </summary>
    private static EntityCollection DocumentLevelIndexNameRow(string indexName)
    {
        var entity = new Entity("sprk_document", DocumentId);
        entity["sprk_searchindexname"] = indexName;
        return new EntityCollection(new List<Entity> { entity }) { EntityName = "sprk_document" };
    }

    private static EntityCollection NoRows() => new(new List<Entity>());

    [Fact]
    public async Task WhenDocumentHasPerRecordSearchIndexName_TheWriteAndTheStampBothUseIt()
    {
        // Arrange: the document's own sprk_searchindexname names a DIFFERENT index than the
        // configured tenant default ("tenant-default-index", set by the fixture).
        const string perRecordIndex = "matter-42-knowledge-index";

        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexableDocument());

        _fixture.EntityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLevelIndexNameRow(perRecordIndex));

        FileIndexRequest? capturedIndexRequest = null;
        _fixture.FileIndexingMock
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) => capturedIndexRequest = req)
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 4, duration: TimeSpan.FromSeconds(1), documentId: DocumentId.ToString()));

        UpdateDocumentRequest? capturedUpdateRequest = null;
        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(DocumentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, req, _) => capturedUpdateRequest = req)
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = [DocumentId.ToString()],
            TenantId = RagSendToIndexFixture.TestTenantId,
        });

        // Assert — the HTTP contract
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();
        body.Should().NotBeNull();
        body!.SuccessCount.Should().Be(1);
        body.Results.Should().HaveCount(1);
        body.Results[0].Success.Should().BeTrue();
        body.Results[0].ChunksIndexed.Should().Be(4);
        body.Results[0].IndexName.Should().Be(perRecordIndex,
            "the response must report the index the file actually landed in, not the tenant default");

        // Assert — the ACTUAL WRITE was routed to the per-record index, not just the reported name.
        // This is the defect: previously FileIndexRequest.SearchIndexName was never set at all, so
        // IRagService fell through to the tenant default regardless of what this assertion checks
        // on the response DTO above.
        capturedIndexRequest.Should().NotBeNull();
        capturedIndexRequest!.SearchIndexName.Should().Be(perRecordIndex,
            "the file index request sent to the indexing pipeline must carry the per-record index name");
        capturedIndexRequest.SearchIndexName.Should().NotBe(RagSendToIndexFixture.TenantDefaultIndexName,
            "a per-record value must not be silently replaced by the tenant default");

        // Assert — the Dataverse tracking stamp matches what was actually used.
        capturedUpdateRequest.Should().NotBeNull();
        capturedUpdateRequest!.SearchIndexName.Should().Be(perRecordIndex);
        capturedUpdateRequest.SearchIndexed.Should().BeTrue();
    }

    [Fact]
    public async Task WhenDocumentHasNoPerRecordSearchIndexName_FallsBackToTheTenantDefault()
    {
        // Arrange: nothing in the resolver chain (no document lookup row, no parent — this document
        // has no MatterId/ProjectId/InvoiceId) resolves a value, so GetDefaultIndexName() must be the
        // fallback used for BOTH the write and the stamp. Proves the fix did not remove the fallback.
        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexableDocument());

        _fixture.EntityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NoRows());

        FileIndexRequest? capturedIndexRequest = null;
        _fixture.FileIndexingMock
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) => capturedIndexRequest = req)
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 2, duration: TimeSpan.FromSeconds(1), documentId: DocumentId.ToString()));

        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(DocumentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = [DocumentId.ToString()],
            TenantId = RagSendToIndexFixture.TestTenantId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();
        body!.Results[0].IndexName.Should().Be(RagSendToIndexFixture.TenantDefaultIndexName,
            "with no per-record value anywhere in the chain, the tenant default remains the fallback");

        // The request sent to the pipeline carries null (not the tenant default explicitly) — it
        // falls through to IRagService's own tenant-default chain, byte-for-byte backward compatible
        // with every other caller of FileIndexRequest.
        capturedIndexRequest!.SearchIndexName.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Unauthenticated_IsRejectedByTheExistingJwtFilter_NotAnUnhandledException()
    {
        // No IDocumentDataverseService / IFileIndexingService setup at all — if auth did not reject
        // this first, the test would fail on an unconfigured-mock exception rather than assert 401.
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = [DocumentId.ToString()],
            TenantId = RagSendToIndexFixture.TestTenantId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the group's RequireAuthorization() must reject a caller with no bearer token before the " +
            "tenant filter or the handler ever runs");
    }
}

/// <summary>
/// Local <see cref="WebApplicationFactory{TEntryPoint}"/> for this file only (project boundary rule
/// — no shared fixture/factory file is modified). Boots the real <c>Program</c> with the module
/// boundaries named in the class remarks substituted; everything else (routing, the endpoint filter
/// pipeline, rate limiting, and the REAL <see cref="SearchIndexNameResolver"/>) is production code.
/// </summary>
public sealed class RagSendToIndexFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-rag-send-to-index-033";
    public const string TenantDefaultIndexName = "tenant-default-index";

    public Mock<IDocumentDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IFileIndexingService> FileIndexingMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new(MockBehavior.Loose);

    public void ResetBoundaries()
    {
        DataverseMock.Reset();
        FileIndexingMock.Reset();
        EntityServiceMock.Reset();
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
                // The tenant-default fallback this test file asserts against explicitly.
                ["AiSearch:KnowledgeIndexName"] = TenantDefaultIndexName,
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
                options.DefaultAuthenticateScheme = RagSendToIndexFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RagSendToIndexFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, RagSendToIndexFakeAuthHandler>(
                RagSendToIndexFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = RagSendToIndexFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RagSendToIndexFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            // Mock IDataverseService (health probes / session cold storage — unrelated to this route).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── The module boundaries this file substitutes. ──
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IFileIndexingService>();
            services.AddSingleton(FileIndexingMock.Object);

            // ISearchIndexNameResolver stays REAL (SearchIndexNameResolver) — only the Dataverse
            // read underneath it is substituted, so the resolver's actual chain logic is exercised.
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>
/// Fake auth handler local to this file — authenticates any request carrying an Authorization header
/// and emits <c>oid</c> + <c>tid</c> claims; a missing header fails authentication so
/// <c>RequireAuthorization()</c> rejects with 401 before the tenant filter or the handler runs.
/// </summary>
internal sealed class RagSendToIndexFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RagSendToIndexFakeAuth";

    public RagSendToIndexFakeAuthHandler(
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
            new("oid", "rag-send-to-index-test-user"),
            new("tid", RagSendToIndexFixture.TestTenantId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "rag-send-to-index-test-user"),
            new(System.Security.Claims.ClaimTypes.Name, "RagSendToIndex Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
