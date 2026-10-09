using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <b>/api/ai/rag route authorization</b> — unified-access-control-r2 task 163 (sweep findings #4, #5, #6,
/// #30; takes over GitHub #1041). Every test drives the REAL <c>MapRagEndpoints</c> through the in-process
/// host in <see cref="RouteSweepAuthorizationFixture"/>; only module boundaries are substituted.
/// </summary>
/// <remarks>
/// Covered, one block per route: POST /search (per-row trim as the caller, parent Read gate, SessionId
/// refusal, token tenant), POST /index and DELETE /{documentId} (SystemAdmin + token partition), POST
/// /index-file (tenant from the token, Write on the named document, AppendTo on the named parent, the row
/// comparisons, the knowledge-source refusal), the fixed 500 details (ADR-019), and the
/// TenantAuthorizationFilter batch case (no route binds that body any more — POST /index/batch was deleted
/// — so that one case is exercised on the filter directly).
/// </remarks>
[Trait("category", "authorization")]
public sealed class RagEndpointsAuthorizationContractTests : IClassFixture<RouteSweepAuthorizationFixture>
{
    private readonly RouteSweepAuthorizationFixture _fixture;

    private static readonly Guid ReadableDocument = Guid.Parse("16300000-0000-0000-0000-00000000d001");
    private static readonly Guid UnreadableDocument = Guid.Parse("16300000-0000-0000-0000-00000000d002");
    private static readonly Guid SecondReadableDocument = Guid.Parse("16300000-0000-0000-0000-00000000d003");
    private static readonly Guid ParentMatter = Guid.Parse("16300000-0000-0000-0000-0000000a0001");
    private static readonly Guid WritableDocument = Guid.Parse("16300000-0000-0000-0000-00000000e001");
    private static readonly Guid WorkAssignment = Guid.Parse("16300000-0000-0000-0000-0000000b0001");
    private static readonly Guid NonExistent = Guid.Parse("16300000-0000-0000-0000-0000000fffff");

    private const string Drive = "b!drive-163";
    private const string Item = "01ITEM163";
    private const string ForeignTenant = "victim-tenant-partition";
    private const string SystemAdmin = "SystemAdmin";

    public RagEndpointsAuthorizationContractTests(RouteSweepAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    // =====================================================================================================
    // POST /api/ai/rag/search — finding #5
    // =====================================================================================================

    [Fact]
    public async Task Search_CallerWhoCanReadNoneOfTheRows_Gets200EmptyAndZeroCount()
    {
        ArrangeSearchResults(Row("c1", ReadableDocument), Row("c2", UnreadableDocument));

        var response = await PostSearchAsync(Options());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RagSearchResponse>();
        body!.Results.Should().BeEmpty("the caller holds Read on none of the documents");
        body.TotalCount.Should().Be(0, "the count must describe what was returned, never the index");
    }

    [Fact]
    public async Task Search_KeepsOnlyRowsWhoseDocumentTheCallerCanRead_DropsUnusableIds_CountMatches()
    {
        _fixture.Access.Grant(ReadableDocument, AccessRights.Read);
        ArrangeSearchResults(
            Row("c1", ReadableDocument),
            Row("c2", UnreadableDocument),
            Row("c3", ReadableDocument),
            Row("c4", null),
            Row("c5", "orphan-file-not-a-guid"),
            Row("c6", Guid.Empty.ToString()));

        var response = await PostSearchAsync(Options());

        var body = await response.Content.ReadFromJsonAsync<RagSearchResponse>();
        body!.Results.Select(r => r.Id).Should().Equal("c1", "c3");
        body.TotalCount.Should().Be(2);

        _fixture.AiAuthorization.Calls.Should().ContainSingle("ONE authorization call per page")
            .Which.Should().BeEquivalentTo(new[] { ReadableDocument, UnreadableDocument },
                "the distinct parsed ids only — null, non-GUID and empty ids are never sent");
    }

    [Fact]
    public async Task Search_WhenNoRowHasAUsableDocumentId_ReturnsNothing_AndNeverCallsTheSeam()
    {
        ArrangeSearchResults(Row("c1", null), Row("c2", "knowledge-source-chunk"));

        var response = await PostSearchAsync(Options());

        var body = await response.Content.ReadFromJsonAsync<RagSearchResponse>();
        body!.Results.Should().BeEmpty("a row with no sprk_document id has no record to evaluate and is dropped");
        body.TotalCount.Should().Be(0);
        _fixture.AiAuthorization.Calls.Should().BeEmpty("the seam throws on an empty list, so it is not called");
    }

    [Fact]
    public async Task Search_CallerWhoCanReadEveryRow_GetsTheSameRowsAsBefore()
    {
        _fixture.Access.Grant(ReadableDocument, AccessRights.Read).Grant(SecondReadableDocument, AccessRights.Read);
        ArrangeSearchResults(Row("c1", ReadableDocument), Row("c2", SecondReadableDocument), Row("c3", ReadableDocument));

        var response = await PostSearchAsync(Options());

        var body = await response.Content.ReadFromJsonAsync<RagSearchResponse>();
        body!.Results.Select(r => r.Id).Should().Equal("c1", "c2", "c3");
        body.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Search_OptionsReachingTheService_CarryTheTokensTenant_AndTheCaller()
    {
        RagSearchOptions? captured = null;
        _fixture.Rag
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, o, _) => captured = o)
            .ReturnsAsync(new RagSearchResponse { Results = [] });

        // The body tenant matches case-insensitively but differs in case: the token's exact value must win.
        await PostSearchAsync(Options(tenant: RouteSweepAuthorizationFixture.CallerTenantId.ToUpperInvariant()));

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(RouteSweepAuthorizationFixture.CallerTenantId);
        captured.CallerPrincipal.Should().NotBeNull();
        captured.CallerPrincipal!.FindFirst("oid")!.Value.Should().Be(RouteSweepAuthorizationFixture.CallerObjectId);
    }

    [Fact]
    public async Task Search_BodyTenantNotTheTokens_Is403()
    {
        var response = await PostSearchAsync(Options(tenant: ForeignTenant));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        VerifySearchNeverRan();
    }

    [Fact]
    public async Task Search_WithSessionId_Is400_AndNoSearchRuns()
    {
        var response = await PostSearchAsync(Options() with { SessionId = "someone-elses-session" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorCodeAsync(response, "RAG_SESSION_ID_NOT_ACCEPTED");
        VerifySearchNeverRan();
    }

    [Theory]
    [InlineData("matter", "unreadable")]
    [InlineData("matter", "non-existent")]
    [InlineData("not-a-type", "readable")]
    [InlineData("matter", "not-a-guid")]
    public async Task Search_WithANamedParentTheCallerCannotUse_IsTheUniform404_AndNoSearchRuns(string type, string parent)
    {
        _fixture.Access.Grant(ParentMatter, AccessRights.Read);
        var parentId = parent switch
        {
            "unreadable" => UnreadableDocument.ToString(),
            "non-existent" => NonExistent.ToString(),
            "readable" => ParentMatter.ToString(),
            _ => "not-a-guid",
        };

        var response = await PostSearchAsync(Options() with { ParentEntityType = type, ParentEntityId = parentId });

        await AssertUniformNotFoundAsync(response);
        VerifySearchNeverRan();
    }

    [Fact]
    public async Task Search_WithAReadableParent_RunsAndAsksAsTheCallerOnTheParentsSet()
    {
        _fixture.Access.Grant(ParentMatter, AccessRights.Read);
        ArrangeSearchResults();

        var response = await PostSearchAsync(Options() with { ParentEntityType = "matter", ParentEntityId = ParentMatter.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Access.RecordChecks.Should().ContainSingle()
            .Which.Should().Be((RouteSweepAuthorizationFixture.CallerObjectId, "sprk_matters", ParentMatter, RouteSweepAuthorizationFixture.BearerToken));
    }

    [Fact]
    public async Task Search_UnreadableAndNonExistentParents_AreByteIdenticalApartFromIds_AndNeverEchoTheId()
    {
        var unreadable = await PostSearchAsync(Options() with { ParentEntityType = "matter", ParentEntityId = UnreadableDocument.ToString() });
        var absent = await PostSearchAsync(Options() with { ParentEntityType = "matter", ParentEntityId = NonExistent.ToString() });

        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent));
        (await unreadable.Content.ReadAsStringAsync()).Should().NotContain(UnreadableDocument.ToString());
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task Search_ParentGate_FailsClosed(string mode)
    {
        _fixture.Access.Grant(ParentMatter, AccessRights.Read);
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await PostSearchAsync(
            Options() with { ParentEntityType = "matter", ParentEntityId = ParentMatter.ToString() },
            _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        await AssertUniformNotFoundAsync(response);
        VerifySearchNeverRan();
    }

    [Fact]
    public async Task Search_ParentGate_WithNoOid_Is401_AndNoSearchRuns()
    {
        var response = await PostSearchAsync(
            Options() with { ParentEntityType = "matter", ParentEntityId = ParentMatter.ToString() },
            _fixture.CreateCallerClient(withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        VerifySearchNeverRan();
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task Search_Trim_FailsClosed_NoRowIsReturned(string mode)
    {
        _fixture.Access.Grant(ReadableDocument, AccessRights.Read);
        _fixture.AiAuthorization.ThrowOnCall = mode == "seam-throws";
        ArrangeSearchResults(Row("c1", ReadableDocument));

        var response = await PostSearchAsync(Options(), _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RagSearchResponse>();
        body!.Results.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Search_ServiceFault_Is500WithAFixedDetail()
    {
        _fixture.Rag
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET-internal-detail"));

        var response = await PostSearchAsync(Options());

        await AssertFixedServerErrorAsync(response);
    }

    // =====================================================================================================
    // POST /api/ai/rag/index — finding #6 (operator surface)
    // =====================================================================================================

    [Fact]
    public async Task Index_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsIndexed()
    {
        var response = await _fixture.CreateCallerClient().PostAsJsonAsync("/api/ai/rag/index", Chunk(RouteSweepAuthorizationFixture.CallerTenantId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Rag.Verify(r => r.IndexDocumentAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_AdminNamingAnotherTenant_Is403_AndNothingIsIndexed()
    {
        var response = await _fixture.CreateCallerClient(roles: SystemAdmin).PostAsJsonAsync("/api/ai/rag/index", Chunk(ForeignTenant));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Rag.Verify(r => r.IndexDocumentAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_Admin_WritesIntoTheTokensPartition()
    {
        KnowledgeDocument? written = null;
        _fixture.Rag
            .Setup(r => r.IndexDocumentAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .Callback<KnowledgeDocument, CancellationToken>((d, _) => written = d)
            .ReturnsAsync((KnowledgeDocument d, CancellationToken _) => d);

        var response = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .PostAsJsonAsync("/api/ai/rag/index", Chunk(RouteSweepAuthorizationFixture.CallerTenantId.ToUpperInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        written!.TenantId.Should().Be(RouteSweepAuthorizationFixture.CallerTenantId, "the partition is the token's exact value");
    }

    [Fact]
    public async Task Index_ServiceFault_Is500WithAFixedDetail()
    {
        _fixture.Rag
            .Setup(r => r.IndexDocumentAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET-internal-detail"));

        var response = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .PostAsJsonAsync("/api/ai/rag/index", Chunk(RouteSweepAuthorizationFixture.CallerTenantId));

        await AssertFixedServerErrorAsync(response);
    }

    // =====================================================================================================
    // DELETE /api/ai/rag/{documentId} — finding #30 (operator surface; chunk key)
    // =====================================================================================================

    [Fact]
    public async Task DeleteChunk_SignedInCallerWithoutSystemAdmin_Is403_AndNothingIsDeleted()
    {
        var response = await _fixture.CreateCallerClient()
            .DeleteAsync($"/api/ai/rag/chunk-key-1?tenantId={RouteSweepAuthorizationFixture.CallerTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Rag.Verify(r => r.DeleteDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteChunk_AdminNamingAnotherTenant_Is403_AndNothingIsDeleted()
    {
        var response = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .DeleteAsync($"/api/ai/rag/chunk-key-1?tenantId={ForeignTenant}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Rag.Verify(r => r.DeleteDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteChunk_Admin_DeletesOnlyInTheTokensPartition_AndAnotherTenantsKeyLooksAbsent()
    {
        // The service returns false both for an absent key and for a key in another tenant's partition
        // (RagServiceTenantGuardTests pins that half); the route must answer the two identically.
        _fixture.Rag
            .Setup(r => r.DeleteDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var otherTenantsKey = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .DeleteAsync($"/api/ai/rag/other-tenant-chunk?tenantId={RouteSweepAuthorizationFixture.CallerTenantId}");
        var absentKey = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .DeleteAsync($"/api/ai/rag/absent-chunk?tenantId={RouteSweepAuthorizationFixture.CallerTenantId}");

        otherTenantsKey.StatusCode.Should().Be(HttpStatusCode.OK);
        (await otherTenantsKey.Content.ReadAsStringAsync()).Should().Be(await absentKey.Content.ReadAsStringAsync());
        _fixture.Rag.Verify(r => r.DeleteDocumentAsync("other-tenant-chunk", RouteSweepAuthorizationFixture.CallerTenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteChunk_ServiceFault_Is500WithAFixedDetail()
    {
        _fixture.Rag
            .Setup(r => r.DeleteDocumentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET-internal-detail"));

        var response = await _fixture.CreateCallerClient(roles: SystemAdmin)
            .DeleteAsync($"/api/ai/rag/chunk-key-1?tenantId={RouteSweepAuthorizationFixture.CallerTenantId}");

        await AssertFixedServerErrorAsync(response);
    }

    // =====================================================================================================
    // POST /api/ai/rag/index-file — finding #4, GitHub #1041
    // =====================================================================================================

    [Fact]
    public async Task IndexFile_BodyTenantNotTheTokens_Is403_AndNothingIsIndexedOrStamped()
    {
        var response = await PostIndexFileAsync(IndexFile(tenant: ForeignTenant));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.IndexedFiles.Should().BeEmpty();
        VerifyNoStamp();
    }

    [Fact]
    public async Task IndexFile_ThePartitionIsTheTokensTenant()
    {
        await PostIndexFileAsync(IndexFile(tenant: RouteSweepAuthorizationFixture.CallerTenantId.ToUpperInvariant()));

        _fixture.IndexedFiles.Should().ContainSingle().Which.TenantId.Should().Be(RouteSweepAuthorizationFixture.CallerTenantId);
    }

    [Theory]
    [InlineData("no-write")]
    [InlineData("non-existent")]
    [InlineData("not-a-guid")]
    public async Task IndexFile_NamedDocumentTheCallerCannotWrite_IsTheUniform404_NothingIndexedOrStamped(string mode)
    {
        _fixture.Access.Grant(WritableDocument, AccessRights.Read);
        var documentId = mode switch
        {
            "no-write" => WritableDocument.ToString(),
            "non-existent" => NonExistent.ToString(),
            _ => "not-a-guid",
        };

        var response = await PostIndexFileAsync(IndexFile(documentId: documentId));

        await AssertUniformNotFoundAsync(response);
        _fixture.IndexedFiles.Should().BeEmpty();
        VerifyNoStamp();
        _fixture.Documents.Verify(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("matter", "no-appendto")]
    [InlineData("not-a-type", "granted")]
    [InlineData("matter", "not-a-guid")]
    public async Task IndexFile_NamedParentTheCallerCannotAppendTo_IsTheUniform404_NothingIndexed(string type, string mode)
    {
        _fixture.Access.Grant(ParentMatter, mode == "no-appendto" ? AccessRights.Read : AccessRights.AppendTo);

        var response = await PostIndexFileAsync(IndexFile(parent: new ParentEntityContext(
            type, mode == "not-a-guid" ? "not-a-guid" : ParentMatter.ToString(), "Acme v. Zenith")));

        await AssertUniformNotFoundAsync(response);
        _fixture.IndexedFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no-file")]
    [InlineData("drive-mismatch")]
    [InlineData("item-mismatch")]
    [InlineData("parent-mismatch")]
    public async Task IndexFile_RequestThatDoesNotMatchTheDocumentRow_Is409_NothingIndexedOrStamped(string mode)
    {
        GrantWriteAndParent();
        ArrangeRow(new DocumentEntity
        {
            Id = WritableDocument.ToString(),
            Name = "contract.docx",
            GraphDriveId = mode == "no-file" ? null : Drive,
            GraphItemId = mode == "no-file" ? null : Item,
            MatterId = ParentMatter.ToString(),
            MatterName = "Acme v. Zenith",
        });

        var request = IndexFile(documentId: WritableDocument.ToString(), parent: new ParentEntityContext("matter", ParentMatter.ToString(), "Acme"));
        request = mode switch
        {
            "drive-mismatch" => request with { DriveId = "b!another-drive" },
            "item-mismatch" => request with { ItemId = "01ANOTHERITEM" },
            "parent-mismatch" => request with { ParentEntity = new ParentEntityContext("project", Guid.NewGuid().ToString(), "x") },
            _ => request,
        };
        if (mode == "parent-mismatch")
        {
            _fixture.Access.Grant(Guid.Parse(request.ParentEntity!.EntityId), AccessRights.AppendTo);
        }

        var response = await PostIndexFileAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorCodeAsync(response, mode switch
        {
            "no-file" => "INDEX_FILE_DOCUMENT_HAS_NO_FILE",
            "parent-mismatch" => "INDEX_FILE_PARENT_MISMATCH",
            _ => "INDEX_FILE_ITEM_MISMATCH",
        });
        _fixture.IndexedFiles.Should().BeEmpty();
        VerifyNoStamp();
    }

    [Fact]
    public async Task IndexFile_RowWithAMatter_ChunksCarryTheRowsParent()
    {
        GrantWriteAndParent();
        ArrangeRow(new DocumentEntity
        {
            Id = WritableDocument.ToString(), Name = "contract.docx", GraphDriveId = Drive, GraphItemId = Item,
            MatterId = ParentMatter.ToString(), MatterName = "Row Matter Name",
        });

        var response = await PostIndexFileAsync(IndexFile(
            documentId: WritableDocument.ToString(),
            parent: new ParentEntityContext("sprk_matter", ParentMatter.ToString().ToUpperInvariant(), "Body Name")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var indexed = _fixture.IndexedFiles.Should().ContainSingle().Subject;
        indexed.ParentEntity.Should().Be(new ParentEntityContext("matter", ParentMatter.ToString(), "Row Matter Name"));
    }

    [Fact]
    public async Task IndexFile_RowWithNoParentLookup_ChunksCarryTheAuthorizedBodyParent()
    {
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write)
            .Grant(WorkAssignment, AccessRights.AppendTo);
        ArrangeRow(new DocumentEntity { Id = WritableDocument.ToString(), Name = "brief.pdf", GraphDriveId = Drive, GraphItemId = Item });
        var parent = new ParentEntityContext("workassignment", WorkAssignment.ToString(), "Draft brief");

        var response = await PostIndexFileAsync(IndexFile(documentId: WritableDocument.ToString(), parent: parent));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.IndexedFiles.Should().ContainSingle().Which.ParentEntity.Should().Be(parent);
        _fixture.Access.RecordChecks.Select(c => c.EntitySetName).Should().BeEquivalentTo("sprk_documents", "sprk_workassignments");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    public async Task IndexFile_KnowledgeSourceFields_Are400_NothingIndexed(string field)
    {
        var request = field == "id"
            ? IndexFile() with { KnowledgeSourceId = "kb-1" }
            : IndexFile() with { KnowledgeSourceName = "Clause Library" };

        var response = await PostIndexFileAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.IndexedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexFile_WithNoDocumentAndNoParent_IsIndexedUnattributed_WithNoDataverseWrite()
    {
        var response = await PostIndexFileAsync(IndexFile());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.IndexedFiles.Should().ContainSingle().Which.ParentEntity.Should().BeNull();
        VerifyNoStamp();
        _fixture.Access.RecordChecks.Should().BeEmpty("nothing names a record, so there is no rights question");
    }

    [Fact]
    public async Task IndexFile_DocumentUploadWizardShape_FromAWriterWhoCanAppendToTheMatter_Is200_AndStamps()
    {
        GrantWriteAndParent();
        ArrangeRow(new DocumentEntity
        {
            Id = WritableDocument.ToString(), Name = "contract.docx", GraphDriveId = Drive, GraphItemId = Item,
            MatterId = ParentMatter.ToString(), MatterName = "Acme v. Zenith",
        });

        // uploadOrchestrator.ts triggerRagIndexing: driveId, itemId, fileName, tenantId, documentId,
        // parentEntity { entityType "matter" (sprk_ stripped) }, searchIndexName.
        var response = await _fixture.CreateCallerClient().PostAsJsonAsync("/api/ai/rag/index-file", new
        {
            driveId = Drive,
            itemId = Item,
            fileName = "contract.docx",
            tenantId = RouteSweepAuthorizationFixture.CallerTenantId,
            documentId = WritableDocument.ToString(),
            parentEntity = new { entityType = "matter", entityId = ParentMatter.ToString(), entityName = "Acme v. Zenith" },
            searchIndexName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Documents.Verify(d => d.UpdateDocumentAsync(
            WritableDocument.ToString(),
            It.Is<UpdateDocumentRequest>(u => u.SearchIndexed == true && u.SearchIndexCompletedOn != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexFile_EntityCreationServiceShape_WithoutDocumentId_Is200_WithNoDataverseWrite()
    {
        _fixture.Access.Grant(WorkAssignment, AccessRights.AppendTo);

        var response = await _fixture.CreateCallerClient().PostAsJsonAsync("/api/ai/rag/index-file", new
        {
            driveId = Drive,
            itemId = Item,
            fileName = "brief.pdf",
            tenantId = RouteSweepAuthorizationFixture.CallerTenantId,
            parentEntity = new { entityType = "workassignment", entityId = WorkAssignment.ToString(), entityName = "Draft brief" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        VerifyNoStamp();
    }

    [Theory]
    [InlineData("no-bearer")]
    [InlineData("seam-throws")]
    public async Task IndexFile_NamedDocument_FailsClosed(string mode)
    {
        GrantWriteAndParent();
        _fixture.Access.ThrowOnCheck = mode == "seam-throws";

        var response = await PostIndexFileAsync(
            IndexFile(documentId: WritableDocument.ToString()),
            _fixture.CreateCallerClient(withBearer: mode != "no-bearer"));

        await AssertUniformNotFoundAsync(response);
        _fixture.IndexedFiles.Should().BeEmpty();
        VerifyNoStamp();
    }

    [Fact]
    public async Task IndexFile_NamedDocument_WithNoOid_Is401()
    {
        var response = await PostIndexFileAsync(IndexFile(documentId: WritableDocument.ToString()), _fixture.CreateCallerClient(withOid: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.IndexedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexFile_UnwritableAbsentAndDeletedAfterTheCheck_AreIdenticalApartFromIds()
    {
        _fixture.Access.Grant(WritableDocument, AccessRights.Read).Grant(SecondReadableDocument, AccessRights.Write);
        // SecondReadableDocument passes the Write check but its row is gone when the handler reads it.
        _fixture.Documents.Setup(d => d.GetDocumentAsync(SecondReadableDocument.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        var unwritable = await PostIndexFileAsync(IndexFile(documentId: WritableDocument.ToString()));
        var absent = await PostIndexFileAsync(IndexFile(documentId: NonExistent.ToString()));
        var deleted = await PostIndexFileAsync(IndexFile(documentId: SecondReadableDocument.ToString()));

        var expected = await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unwritable);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(absent)).Should().Be(expected);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(deleted)).Should().Be(expected);
        (await unwritable.Content.ReadAsStringAsync()).Should().NotContain(WritableDocument.ToString());
    }

    [Fact]
    public async Task IndexFile_PipelineFault_Is500WithAFixedDetail()
    {
        _fixture.FileIndexing
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET-internal-detail"));

        var response = await PostIndexFileAsync(IndexFile());

        await AssertFixedServerErrorAsync(response);
    }

    // =====================================================================================================
    // TenantAuthorizationFilter — the batch case (no route binds it after task 163; filter-level test)
    // =====================================================================================================

    [Theory]
    [InlineData("differs")]
    [InlineData("empty")]
    public async Task TenantFilter_BatchWhoseLaterItemNamesAnotherOrNoTenant_Is403(string mode)
    {
        var documents = new List<KnowledgeDocument>
        {
            new() { Id = "a", TenantId = RouteSweepAuthorizationFixture.CallerTenantId },
            new() { Id = "b", TenantId = mode == "differs" ? ForeignTenant : string.Empty },
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", RouteSweepAuthorizationFixture.CallerTenantId)], "test")),
        };
        var reached = false;

        var result = await new TenantAuthorizationFilter(NullLogger<TenantAuthorizationFilter>.Instance).InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext, (IEnumerable<KnowledgeDocument>)documents),
            _ => { reached = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        reached.Should().BeFalse("item 2 of the batch would have landed in another (or no) partition");
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task TenantFilter_BatchWhoseEveryItemIsTheCallersTenant_PassesThrough()
    {
        var documents = new List<KnowledgeDocument>
        {
            new() { Id = "a", TenantId = RouteSweepAuthorizationFixture.CallerTenantId },
            new() { Id = "b", TenantId = RouteSweepAuthorizationFixture.CallerTenantId },
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", RouteSweepAuthorizationFixture.CallerTenantId)], "test")),
        };
        var reached = false;

        await new TenantAuthorizationFilter(NullLogger<TenantAuthorizationFilter>.Instance).InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext, (IEnumerable<KnowledgeDocument>)documents),
            _ => { reached = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        reached.Should().BeTrue();
    }

    // =====================================================================================================
    // Harness
    // =====================================================================================================

    private static RagSearchOptions Options(string? tenant = null) =>
        new() { TenantId = tenant ?? RouteSweepAuthorizationFixture.CallerTenantId, TopK = 10 };

    private static RagSearchResult Row(string chunkId, Guid documentId) => Row(chunkId, documentId.ToString());

    private static RagSearchResult Row(string chunkId, string? documentId) =>
        new() { Id = chunkId, DocumentId = documentId, DocumentName = $"{chunkId}.docx", Content = $"text of {chunkId}" };

    private void ArrangeSearchResults(params RagSearchResult[] rows) =>
        _fixture.Rag
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse { Query = "q", Results = rows, TotalCount = 1000 });

    private Task<HttpResponseMessage> PostSearchAsync(RagSearchOptions options, HttpClient? client = null) =>
        (client ?? _fixture.CreateCallerClient()).PostAsJsonAsync("/api/ai/rag/search", new RagSearchRequest { Query = "termination", Options = options });

    private void VerifySearchNeverRan() =>
        _fixture.Rag.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()), Times.Never);

    private static KnowledgeDocument Chunk(string tenant) =>
        new() { Id = "chunk-1", TenantId = tenant, Content = "synthetic", SpeFileId = "file-1", DocumentId = ReadableDocument.ToString() };

    private static FileIndexRequest IndexFile(string? tenant = null, string? documentId = null, ParentEntityContext? parent = null) =>
        new()
        {
            DriveId = Drive,
            ItemId = Item,
            FileName = "contract.docx",
            TenantId = tenant ?? RouteSweepAuthorizationFixture.CallerTenantId,
            DocumentId = documentId,
            ParentEntity = parent,
        };

    private Task<HttpResponseMessage> PostIndexFileAsync(FileIndexRequest request, HttpClient? client = null) =>
        (client ?? _fixture.CreateCallerClient()).PostAsJsonAsync("/api/ai/rag/index-file", request);

    private void GrantWriteAndParent() =>
        _fixture.Access.Grant(WritableDocument, AccessRights.Read | AccessRights.Write)
            .Grant(ParentMatter, AccessRights.Read | AccessRights.AppendTo);

    private void ArrangeRow(DocumentEntity row)
    {
        _fixture.Documents.Setup(d => d.GetDocumentAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        // Task 177: the index-parent decision reads the links DocumentEntity cannot carry (work assignment, related event);
        // this row has neither.
        _fixture.Entities
            .Setup(e => e.RetrieveAsync("sprk_document", Guid.Parse(row.Id), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_document", Guid.Parse(row.Id)));
    }

    private void VerifyNoStamp() =>
        _fixture.Documents.Verify(
            d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    internal static async Task AssertUniformNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        body.Should().Contain("The requested record was not found.");
    }

    private static async Task AssertFixedServerErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SECRET-internal-detail",
            "ADR-019: a 500 carries a fixed detail; the exception goes to the server log");
        await AssertErrorCodeAsync(response, "RAG_INTERNAL_ERROR");
    }

    /// <summary>ADR-019: the problem carries the stable <c>errorCode</c> (and the file's <c>code</c> key, same value).</summary>
    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be(expected);
        doc.RootElement.GetProperty("code").GetString().Should().Be(expected);
    }
}
