using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <b>The NFR-02 gate for the content-similarity surface</b> — spaarkeai-word-add-in-r1 task 032,
/// plan.md finding F-b.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> <c>VisualizationService.SearchRelatedDocumentsAsync</c> filtered its cosine-KNN
/// neighbours by <c>tenantId eq '…' and documentId ne '&lt;source&gt;'</c> — tenant scoping and
/// self-exclusion, and nothing else. <c>VisualizationAuthorizationFilter</c> authorized the SOURCE
/// document only. So Read on one document served that document's neighbours from anywhere in the tenant,
/// which is the same failure mode as the unified-access-control-r2 finding where a caller denied Read on
/// all 442 documents of a matter still saw and downloaded them. <c>POST /related-from-content</c> carried
/// no authorization filter at all.
/// </para>
/// <para>
/// <b>Why these tests exercise the handler directly.</b> The handlers are <c>internal</c> for exactly this
/// reason — see the remarks on <see cref="VisualizationEndpoints.GetRelatedDocuments"/>: a test that
/// exercises a re-implementation of the branch proves nothing about the code that ships. The only things
/// substituted are the two module boundaries: <see cref="IVisualizationService"/> (the search engine,
/// standing in for what the index returns) and <see cref="IAccessDataSource"/> (standing in for what
/// Dataverse would answer). Everything between them is the real code —
/// <see cref="AiAuthorizationService"/>, the real <c>AccessRights.Read</c> comparison, the real
/// fail-closed token handling, and the endpoint's real trimming. Mocking at the module boundary is what
/// ADR-038 permits; the banned shapes (<c>Mock&lt;HttpMessageHandler&gt;</c>, DI-registration assertions,
/// ctor null-checks) appear nowhere here.
/// </para>
/// <para>
/// <b>Verified to fail against the unfixed code.</b> A negative test that passes without the fix proves
/// nothing, so the row check was disabled and these were re-run; the recorded output is in
/// <c>projects/spaarkeai-word-add-in-r1/notes/032-authorization-hardening.md</c> §4.
/// </para>
/// </remarks>
[Trait("category", "authorization")]
public sealed class VisualizationRowAuthorizationContractTests
{
    private const string TenantId = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string CallerOid = "cccccccc-9999-8888-7777-666666666666";

    /// <summary>The source document; the filter authorized it before the handler ran.</summary>
    private static readonly Guid SourceDocument = Guid.Parse("11111111-0000-0000-0000-000000000001");

    /// <summary>Three documents of a matter the caller is denied. None may be served.</summary>
    private static readonly Guid DeniedMatterDocumentA = Guid.Parse("22222222-0000-0000-0000-00000000000a");
    private static readonly Guid DeniedMatterDocumentB = Guid.Parse("22222222-0000-0000-0000-00000000000b");
    private static readonly Guid DeniedMatterDocumentC = Guid.Parse("22222222-0000-0000-0000-00000000000c");

    /// <summary>One neighbour the caller may read, so a passing test cannot be an empty-response artefact.</summary>
    private static readonly Guid PermittedDocument = Guid.Parse("33333333-0000-0000-0000-000000000001");

    private static readonly Guid MatterId = Guid.Parse("44444444-0000-0000-0000-000000000001");

    // =====================================================================================
    // THE GATE — a caller denied Read on a matter sees none of that matter's documents
    // =====================================================================================

    [Fact]
    public async Task Related_WhenCallerIsDeniedReadOnAMatter_ServesNoneOfThatMattersDocuments()
    {
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, SourceDocument, AccessRights.Read);
        access.GrantDocument(CallerOid, PermittedDocument, AccessRights.Read);
        // The three DeniedMatter* documents are granted NOTHING — the stub denies by default, which is
        // the point: a stub that allowed unstated cases would reproduce the defect inside the harness.

        var (result, _) = await InvokeRelatedAsync(GraphWithMatterHub(), access);

        var body = OkBody(result);

        body.Nodes.Select(n => n.Id).Should().NotContain(
            [
                DeniedMatterDocumentA.ToString(),
                DeniedMatterDocumentB.ToString(),
                DeniedMatterDocumentC.ToString(),
            ],
            "a caller denied Read on a matter must not receive that matter's documents through the "
            + "similarity surface — the neighbours were reachable purely because they share a tenant "
            + "with a document the caller may read");

        body.Nodes.Select(n => n.Id).Should().Contain(PermittedDocument.ToString(),
            "and the trim must be a trim, not an outage: a neighbour the caller CAN read is still served");

        body.Metadata.TotalResults.Should().Be(1,
            "the count must describe the rows in this response. Reporting 4 would disclose how many "
            + "documents the denied matter holds — a smaller leak than the documents, and the same kind");
    }

    [Fact]
    public async Task Related_WhenCallerIsDeniedReadOnEveryNeighbour_ServesNoRowsAndAZeroCount()
    {
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, SourceDocument, AccessRights.Read);

        var (result, _) = await InvokeRelatedAsync(GraphWithMatterHub(), access);
        var body = OkBody(result);

        body.Nodes.Where(IsResultRow).Should().BeEmpty(
            "every neighbour belonged to a matter the caller cannot read");

        body.Metadata.TotalResults.Should().Be(0);

        body.Nodes.Should().NotContain(n => n.Type == NodeTypes.Matter,
            "a hub is only created when a neighbour of that relationship type was found, so a hub left "
            + "behind after all its documents were withheld would announce the count the rows no longer "
            + "show");

        body.Edges.Should().BeEmpty("no edge may point at a node that was withheld");
    }

    [Fact]
    public async Task Related_WithWriteButNotRead_ServesNothing()
    {
        // AccessRights is a flag set. Serving on "has any rights at all" rather than on Read
        // specifically is a plausible mis-implementation that every other test here would still pass.
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, SourceDocument, AccessRights.Read);
        access.GrantDocument(CallerOid, DeniedMatterDocumentA, AccessRights.Write | AccessRights.Delete);

        var (result, _) = await InvokeRelatedAsync(GraphWithMatterHub(), access);

        OkBody(result).Nodes.Select(n => n.Id)
            .Should().NotContain(DeniedMatterDocumentA.ToString(),
                "Read is the right that governs disclosure; Write without Read must not serve the row");
    }

    // =====================================================================================
    // THE COUNT — countOnly must not become a side channel
    // =====================================================================================

    [Fact]
    public async Task Related_CountOnly_ReportsThePermittedCount_NotThePreTrimTotal()
    {
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, SourceDocument, AccessRights.Read);
        access.GrantDocument(CallerOid, PermittedDocument, AccessRights.Read);

        var (result, service) = await InvokeRelatedAsync(
            GraphWithMatterHub(), access, new VisualizationQueryParameters { CountOnly = true });

        var body = OkBody(result);

        body.Nodes.Should().BeEmpty("countOnly's response contract is metadata with no graph");
        body.Metadata.TotalResults.Should().Be(1,
            "the count-only fast path used to return a total computed from rows nobody authorized, with "
            + "no nodes to trim — a pure count leak: on a matter the caller cannot open it answered "
            + "'how many documents resemble this one in there'");

        service.LastOptions!.CountOnly.Should().BeFalse(
            "the handler must not take the service's count-only shortcut, because that shortcut "
            + "materialises no rows and therefore leaves nothing to authorize");
    }

    // =====================================================================================
    // THE FORCING FUNCTION — no obligation published means refuse, on BOTH routes
    // =====================================================================================

    [Fact]
    public async Task Related_WithNoAuthorizationSignal_Returns500AndNeverReachesTheSearch()
    {
        var service = new RecordingVisualizationService(GraphWithMatterHub());
        var httpContext = CallerContext(withRowObligation: false);

        var result = await VisualizationEndpoints.GetRelatedDocuments(
            SourceDocument,
            new VisualizationQueryParameters(),
            httpContext,
            service,
            BuildAuthorization(new ProgrammableAccessDataSource()),
            NullLogger<Program>.Instance,
            CancellationToken.None);

        StatusOf(result).Should().Be(500,
            "an absent decision means the filter did not run. Refusing is the forcing function: detaching "
            + "AddVisualizationAuthorizationFilter must make this route go silent rather than quietly "
            + "revert to the tenant-wide neighbour enumeration it used to be");

        service.CallCount.Should().Be(0,
            "and it must refuse BEFORE searching — a 500 rendered after the rows were assembled would "
            + "still have to be trusted not to have sent them");
    }

    [Fact]
    public async Task Related_WithASignalThatDoesNotRequireRowAuthorization_Returns500()
    {
        // The flag is asserted on, not merely the presence of an object. Publishing a decision that
        // permits nothing must not read as permission.
        var service = new RecordingVisualizationService(GraphWithMatterHub());
        var httpContext = CallerContext(withRowObligation: false);
        httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
        {
            RequiresPerRowDocumentAuthorization = false,
            Subject = VisualizationAuthorizationSubject.SourceDocument,
        };

        var result = await VisualizationEndpoints.GetRelatedDocuments(
            SourceDocument,
            new VisualizationQueryParameters(),
            httpContext,
            service,
            BuildAuthorization(new ProgrammableAccessDataSource()),
            NullLogger<Program>.Instance,
            CancellationToken.None);

        StatusOf(result).Should().Be(500);
        service.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task IndexTemporaryContent_WithNoAuthorizationSignal_Returns500AndIndexesNothing()
    {
        var service = new RecordingVisualizationService(GraphWithMatterHub());
        var httpContext = UploadContext(withRowObligation: false);

        var result = await VisualizationEndpoints.IndexTemporaryContent(
            httpContext, service, NullLogger<Program>.Instance, CancellationToken.None);

        StatusOf(result).Should().Be(500,
            "this route returns no rows of its own, but it is the ENTRY to the surface whose exit leaked "
            + "them: it writes an embedding into the tenant's search partition and mints the documentId "
            + "the leaking route consumes. Both routes assert the same signal so neither can be un-gated "
            + "in isolation without going silent");

        service.IndexCallCount.Should().Be(0,
            "and nothing may be written to the tenant's search partition on the refusing path");
    }

    [Fact]
    public async Task IndexTemporaryContent_WithTheSignal_SucceedsAndDisclosesOnlyTheCallersOwnUpload()
    {
        // The positive half, and the honest statement of this route's row-authorization contract: its
        // response body is a ContentUploadResult carrying a temporary id the CALLER's own upload just
        // minted. There is no neighbour row here to trim — the trimming happens on GET /related/{id},
        // where this id is spent. Recorded as a test rather than a comment so that a future change which
        // starts returning neighbour rows from this route fails here instead of shipping untrimmed.
        var service = new RecordingVisualizationService(GraphWithMatterHub());
        var httpContext = UploadContext(withRowObligation: true);

        var result = await VisualizationEndpoints.IndexTemporaryContent(
            httpContext, service, NullLogger<Program>.Instance, CancellationToken.None);

        service.IndexCallCount.Should().Be(1);

        var body = result.Should().BeAssignableTo<IValueHttpResult>().Which.Value
            .Should().BeOfType<ContentUploadResult>().Subject;

        body.Success.Should().BeTrue();
        body.DocumentId.Should().NotBeNullOrWhiteSpace();

        // NOT asserted here: that ContentUploadResult carries no collection of rows. That is a
        // STRUCTURAL invariant, and ADR-038 gives structural invariants a home in
        // tests/Spaarke.ArchTests/** rather than inside a behavioural contract test (a reflection
        // assertion over a record's properties here reads as B16/B17). It is recorded instead in
        // notes/032-authorization-hardening.md §6.1 as the residual: if this route ever starts
        // returning neighbour rows, it needs the row check that only GET /related/{documentId} does.
    }

    // =====================================================================================
    // FAIL-CLOSED PROPERTIES
    // =====================================================================================

    [Fact]
    public async Task Related_WithNoCallerToken_ResolvesNoAccess_AndNeverFallsBackToAppOnly()
    {
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, PermittedDocument, AccessRights.Read);

        var httpContext = CallerContext(withRowObligation: true, withBearerToken: false);

        var result = await VisualizationEndpoints.GetRelatedDocuments(
            SourceDocument,
            new VisualizationQueryParameters(),
            httpContext,
            new RecordingVisualizationService(GraphWithMatterHub()),
            BuildAuthorization(access),
            NullLogger<Program>.Instance,
            CancellationToken.None);

        OkBody(result).Nodes.Where(IsResultRow).Should().BeEmpty(
            "without the caller's bearer token, access can only be evaluated as the APPLICATION — which "
            + "on this surface is always yes. Resolving to None instead is what makes the whole pattern "
            + "fail closed, and it must not be traded away for a result set");

        access.Consulted.Should().BeEmpty(
            "and the data source must not be consulted at all: reaching it without a caller token is "
            + "precisely the app-only evaluation that is forbidden here");
    }

    [Fact]
    public async Task Related_OrphanFileNodes_AreNeverServed()
    {
        // An orphan node's id is an SPE file id, not a document GUID: there is no Dataverse row against
        // which to evaluate access. "No answer" must not resolve to "serve it".
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, PermittedDocument, AccessRights.Read);

        var graph = new DocumentGraphResponse
        {
            Nodes =
            [
                Node(SourceDocument.ToString(), NodeTypes.Source, 0),
                Node("01ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", NodeTypes.Orphan, 1),
                Node(PermittedDocument.ToString(), NodeTypes.Related, 1),
            ],
            Edges = [],
            Metadata = new GraphMetadata { SourceDocumentId = SourceDocument.ToString(), TotalResults = 2 },
        };

        var (result, _) = await InvokeRelatedAsync(graph, access);
        var body = OkBody(result);

        body.Nodes.Should().NotContain(n => n.Type == NodeTypes.Orphan);
        body.Metadata.TotalResults.Should().Be(1);
    }

    [Fact]
    public async Task Related_AsksDataverseOncePerDistinctDocument_NotOncePerRow()
    {
        // Memoization, asserted as OBSERVED CALLS rather than as the presence of a dictionary. The graph
        // below repeats one document id, which is what a search returning the same document through two
        // relationship types produces.
        var access = new ProgrammableAccessDataSource();
        access.GrantDocument(CallerOid, PermittedDocument, AccessRights.Read);

        var graph = new DocumentGraphResponse
        {
            Nodes =
            [
                Node(SourceDocument.ToString(), NodeTypes.Source, 0),
                Node(PermittedDocument.ToString(), NodeTypes.Related, 1),
                Node(PermittedDocument.ToString(), NodeTypes.Related, 1),
                Node(DeniedMatterDocumentA.ToString(), NodeTypes.Related, 1),
            ],
            Edges = [],
            Metadata = new GraphMetadata { SourceDocumentId = SourceDocument.ToString(), TotalResults = 3 },
        };

        var (result, _) = await InvokeRelatedAsync(graph, access);

        access.Consulted.Should().HaveCount(2,
            "three rows, two distinct documents: the per-record verdict is decided once and reused");

        access.Consulted.Should().OnlyContain(id => id != SourceDocument.ToString(),
            "the source was authorized by the filter before the handler ran; re-checking it would be a "
            + "second round trip for an answer already held");

        OkBody(result).Metadata.TotalResults.Should().Be(2,
            "both surviving rows are served — deduplicating the CHECK must not deduplicate the ROWS");
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static bool IsResultRow(DocumentNode node) =>
        node.Type != NodeTypes.Source && !NodeTypes.IsParentHub(node.Type);

    /// <summary>
    /// The shape <c>BuildGraphResponseWithHubTopology</c> produces: the source, a matter hub, the three
    /// documents of the denied matter hanging off it, and one semantically related document the caller
    /// may read wired straight to the source.
    /// </summary>
    private static DocumentGraphResponse GraphWithMatterHub()
    {
        var hubId = $"matter-{MatterId}";

        return new DocumentGraphResponse
        {
            Nodes =
            [
                Node(SourceDocument.ToString(), NodeTypes.Source, 0),
                Node(hubId, NodeTypes.Matter, 1),
                Node(DeniedMatterDocumentA.ToString(), NodeTypes.Related, 2),
                Node(DeniedMatterDocumentB.ToString(), NodeTypes.Related, 2),
                Node(DeniedMatterDocumentC.ToString(), NodeTypes.Related, 2),
                Node(PermittedDocument.ToString(), NodeTypes.Related, 1),
            ],
            Edges =
            [
                Edge($"{SourceDocument}-{hubId}", SourceDocument.ToString(), hubId),
                Edge($"{DeniedMatterDocumentA}-{hubId}", DeniedMatterDocumentA.ToString(), hubId),
                Edge($"{DeniedMatterDocumentB}-{hubId}", DeniedMatterDocumentB.ToString(), hubId),
                Edge($"{DeniedMatterDocumentC}-{hubId}", DeniedMatterDocumentC.ToString(), hubId),
                Edge($"{SourceDocument}-{PermittedDocument}", SourceDocument.ToString(), PermittedDocument.ToString()),
            ],
            Metadata = new GraphMetadata
            {
                SourceDocumentId = SourceDocument.ToString(),
                TenantId = TenantId,
                TotalResults = 4,
                NodesPerLevel = [1, 1, 4],
                MaxDepthReached = 2,
            },
        };
    }

    private static DocumentNode Node(string id, string type, int depth) => new()
    {
        Id = id,
        Type = type,
        Depth = depth,
        Data = new DocumentNodeData { Label = id, DocumentType = "Contract" },
    };

    private static DocumentEdge Edge(string id, string source, string target) => new()
    {
        Id = id,
        Source = source,
        Target = target,
        Data = new DocumentEdgeData { RelationshipType = RelationshipTypes.SameMatter },
    };

    private static async Task<(IResult Result, RecordingVisualizationService Service)> InvokeRelatedAsync(
        DocumentGraphResponse graph,
        ProgrammableAccessDataSource access,
        VisualizationQueryParameters? query = null)
    {
        var service = new RecordingVisualizationService(graph);

        var result = await VisualizationEndpoints.GetRelatedDocuments(
            SourceDocument,
            query ?? new VisualizationQueryParameters(),
            CallerContext(withRowObligation: true),
            service,
            BuildAuthorization(access),
            NullLogger<Program>.Instance,
            CancellationToken.None);

        return (result, service);
    }

    /// <summary>
    /// The REAL authorization service over a programmable data source. Nothing between the endpoint and
    /// the <c>AccessRights.Read</c> comparison is substituted.
    /// </summary>
    private static IAiAuthorizationService BuildAuthorization(ProgrammableAccessDataSource access) =>
        new AiAuthorizationService(access, NullLogger<AiAuthorizationService>.Instance);

    private static DefaultHttpContext CallerContext(bool withRowObligation, bool withBearerToken = true)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim("oid", CallerOid)], "Test")),
        };

        if (withBearerToken)
        {
            httpContext.Request.Headers.Authorization = "Bearer test-caller-token";
        }

        if (withRowObligation)
        {
            httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
            {
                RequiresPerRowDocumentAuthorization = true,
                Subject = VisualizationAuthorizationSubject.SourceDocument,
            };
        }

        return httpContext;
    }

    private static DefaultHttpContext UploadContext(bool withRowObligation)
    {
        var httpContext = CallerContext(withRowObligation: false);

        httpContext.Request.ContentType = "multipart/form-data; boundary=----test";
        httpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection
            {
                new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 0, 5, "file", "a.txt")
                {
                    Headers = new HeaderDictionary(),
                },
            });

        if (withRowObligation)
        {
            httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
            {
                RequiresPerRowDocumentAuthorization = true,
                Subject = VisualizationAuthorizationSubject.UploadedContent,
            };
        }

        return httpContext;
    }

    private static DocumentGraphResponse OkBody(IResult result)
    {
        StatusOf(result).Should().Be(200, "the trim is a trim; a denial of some rows is not a denial of the request");
        return result.Should().BeAssignableTo<IValueHttpResult>().Which.Value
            .Should().BeOfType<DocumentGraphResponse>().Subject;
    }

    private static int? StatusOf(IResult result) =>
        result as IStatusCodeHttpResult is { } coded ? coded.StatusCode : null;

    /// <summary>
    /// A programmable <see cref="IAccessDataSource"/>: tests state what Dataverse would answer, and
    /// anything not stated is <see cref="AccessRights.None"/>. Deny-by-default is deliberate — a stub
    /// that allowed unstated cases would reproduce the very defect inside the harness, and every
    /// negative test here would pass for the wrong reason.
    /// </summary>
    private sealed class ProgrammableAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<string, AccessRights> _documents = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every document id this source was actually asked about, in order.</summary>
        public List<string> Consulted { get; } = [];

        public void GrantDocument(string userId, Guid documentId, AccessRights rights) =>
            _documents[$"{userId}|{documentId}"] = rights;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
            Consulted.Add(resourceId);

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = _documents.TryGetValue($"{userId}|{resourceId}", out var rights)
                    ? rights
                    : AccessRights.None,
            });
        }

        /// <summary>
        /// The record-scoped sibling. This surface's rows ARE documents, so the row check must not route
        /// here. Throwing makes a regression that does so fail loudly rather than deny silently.
        /// </summary>
        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Visualization rows are sprk_document rows and must be authorized as documents.");
    }

    /// <summary>
    /// Stands in for the search engine at the module boundary, and records what the handler asked it for.
    /// </summary>
    private sealed class RecordingVisualizationService(DocumentGraphResponse graph) : IVisualizationService
    {
        public int CallCount { get; private set; }

        public int IndexCallCount { get; private set; }

        public VisualizationOptions? LastOptions { get; private set; }

        public Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
            Guid documentId, VisualizationOptions options, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOptions = options;
            return Task.FromResult(graph);
        }

        public Task<ContentUploadResult> IndexTemporaryContentAsync(
            Stream fileStream, string fileName, string tenantId, CancellationToken cancellationToken = default)
        {
            IndexCallCount++;
            return Task.FromResult(new ContentUploadResult
            {
                Success = true,
                DocumentId = Guid.NewGuid().ToString(),
            });
        }
    }
}
