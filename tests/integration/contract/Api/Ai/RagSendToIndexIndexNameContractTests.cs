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
/// The index routing of <c>POST /api/ai/rag/send-to-index</c> and <c>POST /api/ai/rag/index-file</c>, and the search route's
/// allow-list. Since field-mapping-server-write-path-r1 task 041 (FR-40, #2502) the index follows the content type, never the
/// record: a document's own index columns, its parent's and its business unit's are not read, and the caller's
/// <c>searchIndexName</c> on index-file is ignored. Every file is written to, and stamped with,
/// <see cref="ISearchIndexNameResolver.GetDefaultIndexName"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>History.</b> spaarkeai-word-add-in-r1 task 033 routed send-to-index per record (document → parent → business unit).
/// Task 041 removed that chain; must-fire 3 below inverts the test that pinned it.
/// </para>
/// <para>
/// <b>Module boundaries substituted</b> (ADR-038 "mock at module boundaries"; nothing here is
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, a DI-registration assertion, or a ctor null-check):
/// <see cref="IDocumentDataverseService"/> (what Dataverse answers for the document row),
/// <see cref="IFileIndexingService"/> (the OBO download + chunk + embed + write pipeline — an
/// external module this endpoint layer does not own), and <see cref="IGenericEntityService"/> (it would answer a catalog-link
/// fetch; the tests prove none is issued). <see cref="ISearchIndexNameResolver"/> is the REAL <c>SearchIndexNameResolver</c>.
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
    /// Builds a single-row FetchXml result carrying the document's own text column (<c>sprk_searchindexname</c>) — what the
    /// pre-041 resolver read first. Since task 041 nothing asks for it.
    /// </summary>
    private static EntityCollection DocumentLevelIndexNameRow(string indexName)
    {
        var entity = new Entity("sprk_document", DocumentId);
        entity["sprk_searchindexname"] = indexName;
        return new EntityCollection(new List<Entity> { entity }) { EntityName = "sprk_document" };
    }

    private static EntityCollection NoRows() => new(new List<Entity>());

    /// <summary>
    /// The index a request handed to the indexing pipeline names, read from its serialized form so the check holds whatever
    /// the request type carries (FR-40 removed the field; a request that names none reads as <c>null</c>).
    /// </summary>
    private static string? IndexNamedBy(FileIndexRequest request) =>
        JsonSerializer.SerializeToElement(request).TryGetProperty("SearchIndexName", out var named)
        && named.ValueKind == JsonValueKind.String
            ? named.GetString()
            : null;

    /// <summary>
    /// FR-40 must-fire 3 (inverts task 033's per-record routing): the document's own text column, its lookup, its parent and
    /// the parent's business unit all name another index. Send-to-index reads none of them: no catalog-link fetch is issued,
    /// the request reaching the pipeline names no index (the files index), and the stamp is the default.
    /// </summary>
    [Fact]
    public async Task SendToIndex_WhenTheDocumentParentOrBusinessUnitNamesAnotherIndex_TheWriteAndTheStampAreTheDefault()
    {
        const string otherIndex = "matter-42-knowledge-index";

        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexableDocument(searchIndexName: otherIndex));

        // Every record and business-unit lookup the old resolver chain made would answer the other index.
        _fixture.EntityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentLevelIndexNameRow(otherIndex));

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

        var response = await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = [DocumentId.ToString()],
            TenantId = RagSendToIndexFixture.TestTenantId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();
        body!.SuccessCount.Should().Be(1);
        body.Results[0].IndexName.Should().Be(RagSendToIndexFixture.TenantDefaultIndexName,
            "the index follows the content type (FR-40), never the record, its parent or its business unit");

        IndexNamedBy(capturedIndexRequest!).Should().BeNull("the pipeline writes the files index: no index is named");
        capturedUpdateRequest!.SearchIndexName.Should().Be(RagSendToIndexFixture.TenantDefaultIndexName,
            "the stamp equals the index written");
        _fixture.EntityServiceMock.Verify(
            e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()), Times.Never,
            "no record or business-unit index column is read on the indexing path");
    }

    [Fact]
    public async Task WhenDocumentHasNoPerRecordSearchIndexName_FallsBackToTheTenantDefault()
    {
        // Arrange: no record names an index, so GetDefaultIndexName() is used for BOTH the write and the stamp.
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

        // The request sent to the pipeline names no index; IRagService's own tenant-default chain decides.
        IndexNamedBy(capturedIndexRequest!).Should().BeNullOrEmpty();
    }

    // ── POST /api/ai/rag/index-file (FR-40 must-fire 1 and 2, must-not-fire 1) ───────────────────────────────────────

    /// <summary>
    /// FR-40: index-file ignores a body <c>searchIndexName</c> — allow-listed, not allow-listed or absent. The pipeline gets a
    /// request naming no index (the files index), the stamp is the default, and a non-allow-listed value is 200, never 400
    /// <c>INDEX_NOT_ALLOWED</c>. The pipeline double rejects a named non-allow-listed index as the real one does
    /// (<see cref="IFileIndexingService"/>: INDEX_NOT_ALLOWED → 400), so the old pass-through would surface as 400 here.
    /// </summary>
    [Theory]
    [InlineData("spaarke-invoices-index")]
    [InlineData("not-an-allowed-index")]
    [InlineData(null)]
    public async Task IndexFile_ABodySearchIndexName_IsIgnored_TheWriteAndTheStampAreTheDefault(string? bodyIndex)
    {
        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndexableDocument());
        _fixture.EntityServiceMock
            .Setup(e => e.RetrieveAsync("sprk_document", DocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", DocumentId));

        FileIndexRequest? capturedIndexRequest = null;
        _fixture.FileIndexingMock
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) =>
            {
                capturedIndexRequest = req;
                if (IndexNamedBy(req) is { Length: > 0 } named && !RagSendToIndexFixture.AllowedIndexes.Contains(named))
                {
                    throw new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                        code: "INDEX_NOT_ALLOWED", title: "AI Search index not allowed", statusCode: 400);
                }
            })
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1), documentId: DocumentId.ToString()));

        UpdateDocumentRequest? capturedUpdateRequest = null;
        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(DocumentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, req, _) => capturedUpdateRequest = req)
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/index-file", new
        {
            driveId = "b!drive-id",
            itemId = "item-id",
            fileName = "Master Services Agreement.docx",
            tenantId = RagSendToIndexFixture.TestTenantId,
            documentId = DocumentId.ToString(),
            searchIndexName = bodyIndex,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a body index is ignored, so it is never refused (FR-40)");
        IndexNamedBy(capturedIndexRequest!).Should().BeNull("the files index is written whatever the body names");
        capturedUpdateRequest!.SearchIndexName.Should().Be(RagSendToIndexFixture.TenantDefaultIndexName,
            "the stamp equals the index written");
    }

    // ── POST /api/ai/rag/search: the allow-list still refuses a request-named index (AC10, UAC-r2) ──────────────────────

    /// <summary>
    /// Must-not-fire (UAC-r2, 2026-11-02): search-time selection is unchanged. A request-named index outside the allow-list is
    /// refused 400 <c>INDEX_NOT_ALLOWED</c> on the search route by the REAL check: the real <c>RagService</c> and
    /// <c>KnowledgeDeploymentService</c> over the configured <c>AiSearch:AllowedIndexes</c> (the catalog read fails on the
    /// substituted entity service, so the provider falls back to the configured list, as it does in production on that fault).
    /// </summary>
    [Fact]
    public async Task Search_ARequestNamedIndexOutsideTheAllowList_Is400IndexNotAllowed_ByTheRealAllowListCheck()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/rag/search", new
        {
            query = "termination clause",
            options = new { tenantId = RagSendToIndexFixture.TestTenantId, useVectorSearch = false, useSemanticRanking = false },
            searchIndexName = "not-an-allowed-index",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("extensions").GetProperty("code").GetString().Should().Be("INDEX_NOT_ALLOWED");
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

    /// <summary>The configured allow-list (<c>AiSearch:AllowedIndexes</c>) the search route checks a request-named index against.</summary>
    public static readonly IReadOnlySet<string> AllowedIndexes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TenantDefaultIndexName, "spaarke-invoices-index" };

    public Mock<IDocumentDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IFileIndexingService> FileIndexingMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// What Dataverse answers about this caller's rights. Added by task 063 (finding F2): the route
    /// now authorizes every document for <c>Write</c> before it stamps the row, so a fixture that
    /// grants nothing gets a correct 403 and these index-name tests never reach their subject. This
    /// file's subject is index-name resolution, not authorization — the authorization contract is
    /// pinned by <c>SendToIndexAuthorizationContractTests</c> — so the grant below is explicit and
    /// narrow rather than a blanket allow: only <c>DocumentId</c>, only the rights the route needs.
    /// Task 041: <see cref="CallerAccessSeam"/> answers both seams, because index-file decides the document on the
    /// Document path (<c>GetUserAccessAsync</c>) where send-to-index uses the Record path.
    /// </summary>
    public CallerAccessSeam Access { get; } = new();

    public void ResetBoundaries()
    {
        DataverseMock.Reset();
        FileIndexingMock.Reset();
        EntityServiceMock.Reset();

        Access.Reset();
        Access.Grant(Guid.Parse("55555555-0000-0000-0000-000000000001"), AccessRights.Read | AccessRights.Write);
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
                ["AiSearch:AllowedIndexes:0"] = TenantDefaultIndexName,
                ["AiSearch:AllowedIndexes:1"] = "spaarke-invoices-index",
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

            // Task 063: the per-document Write check runs through the REAL AuthorizationService over
            // this deny-by-default source. See the remarks on the Access property.
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);

            // Task 188 round 4d (#2200): send-to-index now applies THE external document narrowing; it is not under test
            // here (SendToIndexDocumentNarrowingTests), so the all-visible double (Option A, the full-host convention).
            Sprk.Bff.Api.Tests.TestInfrastructure.AllVisibleDocumentNarrowing.UseAllVisibleDocumentNarrowing(services);

            // Task 041 (AC10): the search route resolves the caller's privilege groups through Graph before it asks for a
            // search client; that boundary answers "no groups" here. The allow-list check after it is the real one.
            var privilegeGroups = new Mock<Sprk.Bff.Api.Services.Ai.Security.IPrivilegeGroupResolver>();
            privilegeGroups
                .Setup(p => p.ResolveGroupIdsAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<string>());
            services.RemoveAll<Sprk.Bff.Api.Services.Ai.Security.IPrivilegeGroupResolver>();
            services.AddSingleton(privilegeGroups.Object);
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
