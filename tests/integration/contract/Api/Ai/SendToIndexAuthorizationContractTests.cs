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
/// <b>The NFR-02 gate for <c>POST /api/ai/rag/send-to-index</c></b> — spaarkeai-word-add-in-r1 task
/// 063, Fable review finding <b>F2</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong — two independent holes on one route.</b>
/// </para>
/// <para>
/// (1) <b>The caller chose the tenant partition.</b> <c>TenantAuthorizationFilter.ExtractTenantId</c>
/// matched <c>RagSearchRequest</c>, <c>KnowledgeDocument</c>, <c>EmbeddingRequest</c> and the query
/// string — but not <see cref="SendToIndexRequest"/>. An unmatched body means the filter returns
/// "no tenant requested" and passes the request through unexamined, and the handler then used
/// <c>request.TenantId</c> verbatim as the AI Search partition key. Any authenticated caller could
/// write chunks into any tenant partition string they cared to type.
/// </para>
/// <para>
/// (2) <b>The row was stamped with no write authorization.</b> The document row was read app-only and
/// <b>written</b> app-only (<c>sprk_searchindexed</c> / <c>sprk_searchindexedon</c> /
/// <c>sprk_searchindexcompletedon</c> / <c>sprk_searchindexname</c>). Only the file BYTES were
/// downloaded OBO, so the effective gate was "can you read this document's SPE container" — coarser
/// than per-document Dataverse rights, and unrelated to whether the caller may modify the row. On
/// success the response also disclosed the row's parent matter/project/invoice id.
/// </para>
/// <para>
/// <b>Module boundaries substituted</b> (ADR-038 "mock at module boundaries"; nothing here is
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, a DI-registration assertion, or a ctor null-check):
/// <see cref="IDocumentDataverseService"/> (the document row read + the stamping write),
/// <see cref="IFileIndexingService"/> (the OBO download → extract → chunk → embed → write pipeline),
/// <see cref="IGenericEntityService"/> (the index-name resolver's own Dataverse reads), and
/// <see cref="IAccessDataSource"/> (what Dataverse would answer for this caller on this record,
/// <b>deny-by-default</b>). Everything between is shipped code: the real route, the real
/// <c>TenantAuthorizationFilter</c>, the real <c>AuthorizationService.GetCallerRecordAccessAsync</c>,
/// the real <c>AccessRights.Write</c> comparison, and the real handler.
/// </para>
/// <para>
/// <b>Verified to fail against the unfixed code, in both directions.</b> A negative test that has not
/// been seen to fail proves nothing. The tenant binding and the per-document write check were each
/// disabled and these tests re-run; all four observations are recorded verbatim in
/// <c>projects/spaarkeai-word-add-in-r1/notes/063-send-to-index-authz.md</c> §4.
/// </para>
/// </remarks>
[Trait("category", "authorization")]
public sealed class SendToIndexAuthorizationContractTests
    : IClassFixture<SendToIndexAuthorizationFixture>
{
    private readonly SendToIndexAuthorizationFixture _fixture;

    public SendToIndexAuthorizationContractTests(SendToIndexAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    /// <summary>A document the caller may write, so a passing test is never an empty-response artefact.</summary>
    private static readonly Guid WritableDocument = Guid.Parse("63000000-0000-0000-0000-000000000001");

    /// <summary>A document the caller can READ but not write — the row they must not be able to stamp.</summary>
    private static readonly Guid ReadOnlyDocument = Guid.Parse("63000000-0000-0000-0000-000000000002");

    /// <summary>The partition a caller would choose if the body were trusted. Never their own <c>tid</c>.</summary>
    private const string ForeignTenantPartition = "victim-tenant-partition-not-mine";

    // =====================================================================================
    // HOLE 1 — the tenant partition must come from the token, never from the body
    // =====================================================================================

    [Fact]
    public async Task TenantIdThatIsNotTheCallersTid_IsRejected_AndNothingIsIndexedOrStamped()
    {
        // THE GATE for hole 1. Pre-fix this returned 200 and wrote the caller's chunks into
        // "victim-tenant-partition-not-mine": a cross-tenant index write in a shared stamp, and
        // index poisoning generally.
        ArrangeIndexableDocument(WritableDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);

        var response = await PostAsync(ForeignTenantPartition, WritableDocument);

        // Asserted on the PARTITION KEYS rather than on the request objects, so that a failure names
        // the partition that was written to. That string is the finding.
        _fixture.CapturedIndexRequests.Select(r => r.TenantId).Should().BeEmpty(
            "nothing may be written to ANY partition on the refusing path, and above all not to a "
            + "partition the caller named in the body");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the tenant partition is decided by the token. A body value that disagrees is REJECTED "
            + "with a problem response, never silently corrected — a caller who believes they are "
            + "writing into partition X must not be told 'done' when the write went to partition Y");

        _fixture.DataverseMock.Verify(
            d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "and no row may be stamped");
    }

    [Fact]
    public async Task TheIndexedPartitionIsTheTokensTid_NotTheBodyValue()
    {
        // The positive half, and the one that pins WHERE the partition comes from. The body carries
        // the caller's own tenant — but UPPERCASED. The mismatch check is case-insensitive, so the
        // request is accepted; the partition, however, must be the token's exact value. That makes
        // this test falsifiable: a handler that reads the body would write chunks under a partition
        // key that differs by case from every other write in the tenant, which in a case-sensitive
        // key space is a silently separate partition. GUID casing genuinely varies between clients,
        // so this is the realistic shape of the bug, not a contrived one.
        ArrangeIndexableDocument(WritableDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);

        var response = await PostAsync(
            SendToIndexAuthorizationFixture.CallerTenantId.ToUpperInvariant(), WritableDocument);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.CapturedIndexRequests.Should().ContainSingle()
            .Which.TenantId.Should().Be(SendToIndexAuthorizationFixture.CallerTenantId,
                "the partition key handed to the indexing pipeline must be derived from the 'tid' "
                + "claim. Reading it from the body — even a body that happens to match — leaves the "
                + "route one detached filter away from being caller-chosen again");
    }

    // =====================================================================================
    // HOLE 2 — every document is authorized for WRITE before its row is stamped
    // =====================================================================================

    [Fact]
    public async Task CallerWithReadButNotWrite_IsRefused_AndTheRowIsNotStamped()
    {
        // THE GATE for hole 2. Read is what the SPE download needs; Write is what the STAMP needs.
        // Pre-fix, holding neither was enough — the row was read and written app-only.
        ArrangeIndexableDocument(ReadOnlyDocument);
        _fixture.Access.Grant(ReadOnlyDocument, AccessRights.Read);

        var response = await PostAsync(SendToIndexAuthorizationFixture.CallerTenantId, ReadOnlyDocument);

        // The ROW STATE first: it is the security property, and a status code cannot show it.
        _fixture.DataverseMock.Verify(
            d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the sprk_searchindex* stamp is the write this task exists to authorize");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a caller who may read a document but not modify it must not be able to drive a write "
            + "to that row. Read access is not consent to be stamped");

        _fixture.CapturedIndexRequests.Should().BeEmpty(
            "and the file is not indexed either — the refusal is of the operation, not a cosmetic "
            + "suppression of its last step");
    }

    [Fact]
    public async Task WhenNoRequestedDocumentIsWritable_Returns403_AndNeverReadsAnyRow()
    {
        // Fail-closed, and closed EARLY. Authorization is decided before the first Dataverse read, so
        // a denied caller learns nothing — not even whether the id exists.
        _fixture.Access.Grant(ReadOnlyDocument, AccessRights.Read);
        // WritableDocument is granted NOTHING here: the stub denies by default, which is the point —
        // a stub that allowed unstated cases would reproduce the defect inside the harness.

        var response = await PostAsync(
            SendToIndexAuthorizationFixture.CallerTenantId, ReadOnlyDocument, WritableDocument);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "when the caller may write none of the requested documents, a 200 reporting '0 of 2 "
            + "succeeded' is indistinguishable from an indexing outage — and both in-repo callers "
            + "(the Find pane and the Dataverse ribbon) would render it as 'try again'");

        _fixture.DataverseMock.Verify(
            d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no row is read on the refusing path: 'Document not found' versus 'no file attached' is "
            + "itself an existence oracle over ids the caller may not touch");
    }

    [Fact]
    public async Task PartialPermission_IndexesAndStampsOnlyThePermittedDocument()
    {
        // THE PARTIAL-PERMISSION CONTRACT, asserted rather than described: one permitted id and one
        // denied id in the same request yields 200 with a per-document split. The permitted document
        // is indexed and stamped exactly as before; the denied one is refused in place, and the
        // request is not failed as a whole (which would let one unauthorized id deny service to the
        // rest of a legitimate batch).
        ArrangeIndexableDocument(WritableDocument);
        ArrangeIndexableDocument(ReadOnlyDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);
        _fixture.Access.Grant(ReadOnlyDocument, AccessRights.Read);

        var response = await PostAsync(
            SendToIndexAuthorizationFixture.CallerTenantId, WritableDocument, ReadOnlyDocument);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();

        body!.TotalRequested.Should().Be(2);
        body.SuccessCount.Should().Be(1);
        body.FailedCount.Should().Be(1);

        var permitted = body.Results.Single(r => r.DocumentId == WritableDocument.ToString());
        permitted.Success.Should().BeTrue("a denied sibling must not suppress a legitimate index");
        permitted.ChunksIndexed.Should().Be(3);

        var denied = body.Results.Single(r => r.DocumentId == ReadOnlyDocument.ToString());
        denied.Success.Should().BeFalse();
        denied.ChunksIndexed.Should().Be(0);
        denied.ParentEntityType.Should().BeNull();
        denied.ParentEntityId.Should().BeNull(
            "on success this route returns the row's parent matter/project/invoice id. A denied "
            + "document must disclose nothing about its parent — that identifier was one of the "
            + "things finding F2 said the caller should not be able to learn");

        // The resulting ROW STATE, which is the half a status code cannot show.
        _fixture.CapturedIndexRequests.Should().ContainSingle()
            .Which.DocumentId.Should().Be(WritableDocument.ToString());

        _fixture.DataverseMock.Verify(
            d => d.UpdateDocumentAsync(WritableDocument.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _fixture.DataverseMock.Verify(
            d => d.UpdateDocumentAsync(ReadOnlyDocument.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the denied row is left exactly as it was — not stamped, not partially stamped");

        _fixture.DataverseMock.Verify(
            d => d.GetDocumentAsync(ReadOnlyDocument.ToString(), It.IsAny<CancellationToken>()),
            Times.Never,
            "and the denied row is never even read");
    }

    [Fact]
    public async Task AccessIsEvaluatedAsTheCaller_AgainstTheDocumentRecord_WithTheirBearerToken()
    {
        // The mechanism itself, asserted as OBSERVED CALLS rather than as the presence of a method
        // call in the source. An app-only evaluation on this surface answers "yes" for everyone, so
        // the caller's token reaching the data source IS the security property.
        ArrangeIndexableDocument(WritableDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);

        await PostAsync(SendToIndexAuthorizationFixture.CallerTenantId, WritableDocument);

        _fixture.Access.RecordChecks.Should().ContainSingle();
        var check = _fixture.Access.RecordChecks[0];

        check.EntitySetName.Should().Be("sprk_documents");
        check.RecordId.Should().Be(WritableDocument);
        check.UserId.Should().Be(SendToIndexAuthorizationFixture.CallerObjectId);
        check.UserAccessToken.Should().NotBeNullOrWhiteSpace(
            "the check must be evaluated OBO as the caller. Without the caller's token the only "
            + "available evaluation is app-only, which is the hole rather than the fix");
    }

    [Fact]
    public async Task TheSameDocumentTwice_IsAuthorizedOnce()
    {
        // Memoization, asserted as observed calls. A batch that repeats an id (the ribbon's
        // multi-select over a grid can) must not pay a Dataverse round trip per repeat.
        ArrangeIndexableDocument(WritableDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);

        await PostAsync(SendToIndexAuthorizationFixture.CallerTenantId, WritableDocument, WritableDocument);

        _fixture.Access.RecordChecks.Should().HaveCount(1,
            "two rows, one distinct document: the verdict is decided once and reused");
    }

    // =====================================================================================
    // THE SHIPPED SURFACE — Run Index must still work (acceptance criterion 7, server half)
    // =====================================================================================

    [Fact]
    public async Task RunIndex_ForAnAuthorizedUserInTheirOwnTenant_StillIndexesAndStamps()
    {
        // The Find view's exact request shape: ONE lowercased brace-free document id plus the MSAL
        // account's tenantId, which is the same value as the token's 'tid'. This is the surface this
        // project shipped (FindView.tsx "Run Index"); a security fix that breaks it is not a fix.
        ArrangeIndexableDocument(WritableDocument);
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write);

        UpdateDocumentRequest? stamp = null;
        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(WritableDocument.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, req, _) => stamp = req)
            .Returns(Task.CompletedTask);

        var response = await PostAsync(SendToIndexAuthorizationFixture.CallerTenantId, WritableDocument);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();

        body!.SuccessCount.Should().Be(1);
        body.Results[0].Success.Should().BeTrue();
        body.Results[0].ChunksIndexed.Should().Be(3,
            "FindView treats ChunksIndexed == 0 as a failure even under HTTP 200, so a chunk count "
            + "is part of this surface's contract, not an incidental field");
        body.Results[0].IndexName.Should().NotBeNullOrWhiteSpace();

        stamp.Should().NotBeNull("the pane re-reads sprk_searchindexed immediately after this call");
        stamp!.SearchIndexed.Should().BeTrue();
        stamp.SearchIndexCompletedOn.Should().NotBeNull();
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private void ArrangeIndexableDocument(Guid documentId)
    {
        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = documentId.ToString(),
                Name = $"doc-{documentId}.docx",
                FileName = $"doc-{documentId}.docx",
                GraphDriveId = "b!test-drive-id",
                GraphItemId = $"01ITEM{documentId:N}",
                MatterId = MatterIdFor(documentId).ToString(),
                MatterName = "Acme v. Zenith",
            });

        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(documentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>A stable per-document parent id, so the "denied rows disclose no parent" assertion has a parent to withhold.</summary>
    private static Guid MatterIdFor(Guid documentId) =>
        Guid.Parse($"63aaaaaa-0000-0000-0000-{documentId.ToString("N")[^12..]}");

    private async Task<HttpResponseMessage> PostAsync(string bodyTenantId, params Guid[] documentIds)
    {
        using var client = _fixture.CreateAuthenticatedClient();

        return await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = documentIds.Select(id => id.ToString()).ToList(),
            TenantId = bodyTenantId,
        });
    }
}

/// <summary>
/// Local <see cref="WebApplicationFactory{TEntryPoint}"/> for this file only (project boundary rule
/// — no shared test fixture, factory or auth handler is modified; sibling agents are active in this
/// test tree). Boots the real <c>Program</c> with only the module boundaries named in the test
/// class's remarks substituted.
/// </summary>
public sealed class SendToIndexAuthorizationFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// The 'tid' the fake auth handler issues, and the ONLY partition this caller may write to.
    /// <b>Contains hex letters deliberately</b>: <c>TheIndexedPartitionIsTheTokensTid_NotTheBodyValue</c>
    /// uppercases this value in the request body to prove the partition came from the token. With an
    /// all-numeric constant that uppercasing is a no-op and the test passes vacuously — which is what
    /// it did until the seeded control exposed it.
    /// </summary>
    public const string CallerTenantId = "63333333-aaaa-5555-bbbb-7777cccc7777";

    public const string CallerObjectId = "63888888-9999-aaaa-bbbb-cccccccccccc";

    public const string TenantDefaultIndexName = "tenant-default-index";

    public Mock<IDocumentDataverseService> DataverseMock { get; } = new(MockBehavior.Loose);

    public Mock<IGenericEntityService> EntityServiceMock { get; } = new(MockBehavior.Loose);

    /// <summary>Deny-by-default stand-in for what Dataverse would answer for this caller.</summary>
    public ProgrammableRecordAccessSource Access { get; } = new();

    /// <summary>Every <see cref="FileIndexRequest"/> that actually reached the indexing pipeline.</summary>
    public List<FileIndexRequest> CapturedIndexRequests { get; } = [];

    public void ResetBoundaries()
    {
        DataverseMock.Reset();
        EntityServiceMock.Reset();
        Access.Reset();
        CapturedIndexRequests.Clear();

        // The index-name resolver's Dataverse reads resolve nothing, so the tenant default applies.
        EntityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>()));
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
                ["AiSearch:KnowledgeIndexName"] = TenantDefaultIndexName,
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

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SendToIndexAuthorizationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SendToIndexAuthorizationFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SendToIndexAuthorizationFakeAuthHandler>(
                SendToIndexAuthorizationFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = SendToIndexAuthorizationFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = SendToIndexAuthorizationFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── The module boundaries this file substitutes. ──
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);

            // The indexing pipeline: records what it was asked to write, and to WHICH partition.
            var indexing = new Mock<IFileIndexingService>(MockBehavior.Loose);
            indexing
                .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, _, _) => CapturedIndexRequests.Add(req))
                .ReturnsAsync((FileIndexRequest req, HttpContext _, CancellationToken _) =>
                    FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromMilliseconds(120), documentId: req.DocumentId));
            services.RemoveAll<IFileIndexingService>();
            services.AddSingleton(indexing.Object);

            // What Dataverse would answer about this caller's rights. Replaces the whole
            // DataverseAccessDataSource → CachedAccessDataSource chain; the real AuthorizationService
            // sits on top of it unmodified.
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
        return client;
    }
}

/// <summary>
/// A programmable <see cref="IAccessDataSource"/>: a test states what Dataverse would answer, and
/// anything unstated is <see cref="AccessRights.None"/>. Deny-by-default is deliberate — a stub that
/// allowed unstated cases would reproduce the very defect inside the harness, and every negative test
/// here would pass for the wrong reason.
/// </summary>
public sealed class ProgrammableRecordAccessSource : IAccessDataSource
{
    private readonly Dictionary<Guid, AccessRights> _rights = [];

    /// <summary>Every record-scoped question actually asked, in order.</summary>
    public List<(string UserId, string EntitySetName, Guid RecordId, string? UserAccessToken)> RecordChecks { get; } = [];

    public void Grant(Guid recordId, AccessRights rights) => _rights[recordId] = rights;

    public void Reset()
    {
        _rights.Clear();
        RecordChecks.Clear();
    }

    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
    {
        RecordChecks.Add((userId, entitySetName, recordId, userAccessToken));

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = recordId.ToString(),
            AccessRights = _rights.TryGetValue(recordId, out var rights) ? rights : AccessRights.None,
        });
    }

    /// <summary>
    /// The document-scoped sibling, hard-wired to <c>sprk_documents</c>. This route's subject IS a
    /// <c>sprk_document</c>, so either seam would answer — but the record-scoped one is the canonical
    /// caller-evaluated evaluator the sibling surfaces use, and pinning the choice here means a
    /// regression that silently swaps seams fails loudly instead of quietly changing which code path
    /// makes the security decision.
    /// </summary>
    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "send-to-index authorizes through AuthorizationService.GetCallerRecordAccessAsync "
            + "(IAccessDataSource.GetRecordAccessAsync), the entity-generic caller-evaluated seam.");
}

/// <summary>
/// Fake auth handler local to this file — authenticates any request carrying an Authorization header
/// and emits the <c>oid</c> + <c>tid</c> claims the route's filter and handler read.
/// </summary>
internal sealed class SendToIndexAuthorizationFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SendToIndexAuthorizationFakeAuth";

    public SendToIndexAuthorizationFakeAuthHandler(
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
            new("oid", SendToIndexAuthorizationFixture.CallerObjectId),
            new("tid", SendToIndexAuthorizationFixture.CallerTenantId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, SendToIndexAuthorizationFixture.CallerObjectId),
            new(System.Security.Claims.ClaimTypes.Name, "SendToIndex Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
