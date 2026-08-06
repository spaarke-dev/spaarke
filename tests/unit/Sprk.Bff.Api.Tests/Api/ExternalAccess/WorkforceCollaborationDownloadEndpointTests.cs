// teams-app-r1 Task 030 — Broker-only workforce collaboration document download tests.
//
// Protects the highest-consequence property of the collaboration surface: authz-before-stream,
// broker-only (app-only SPE), no Graph pointer to the client — for ALL THREE principal planes
// (systemuser, contact-with-grant, contact-with-standing-grant). Two layers are exercised:
//
//   1. The ENFORCEMENT GATE (AccessibleRecordSetAuthorizationFilter, attached by the endpoint):
//      a non-member of the accessible set is DENIED 403 with zero bytes and the handler is never
//      reached — proven per principal plane by substituting IAccessibleRecordSetService.
//   2. The DOWNLOAD HANDLER (WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent): an
//      authorized caller receives the document bytes via the APP-ONLY SpeFileStore.DownloadFileAsync
//      (never OBO), document→project scoping denies a cross-project document 403 BEFORE any SPE
//      pointer resolution, and NO driveId/itemId (Graph pointer) appears in any response.
//
// Regression protection (why these tests are maintain-class): a mistake in this gate leaks documents
// to a principal outside the accessible set, or exposes a Graph pointer that lets the client bypass
// the broker. That is a security failure, not a coverage number.

using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public class WorkforceCollaborationDownloadEndpointTests
{
    private const string EntityType = "sprk_project";
    private const string RouteKey = "projectId";

    private static readonly Guid ProjectId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid DocumentId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherProjectId = Guid.Parse("c0000000-0000-0000-0000-000000000003");

    // The load-bearing Graph pointers — they must NEVER surface to the client in any response.
    private const string DriveId = "b!SENSITIVE_DRIVE_POINTER_DO_NOT_LEAK";
    private const string ItemId = "01ITEM_SENSITIVE_POINTER_DO_NOT_LEAK";
    private const string DocumentName = "contract.pdf";

    // ── The three principal planes (design §5) ───────────────────────────────────────────────────
    private static readonly WorkforcePrincipal SystemUser = new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ContactId = Guid.Parse("11111111-1111-1111-1111-1111111111c1"),
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    };

    // A contact whose access derives from an explicit sprk_externalrecordaccess grant.
    private static readonly WorkforcePrincipal ContactWithGrant = new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    };

    // A contact whose access derives from a standing grant (runtime membership).
    private static readonly WorkforcePrincipal ContactWithStandingGrant = new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    };

    public static IEnumerable<object[]> AllThreePrincipals()
    {
        yield return new object[] { SystemUser };
        yield return new object[] { ContactWithGrant };
        yield return new object[] { ContactWithStandingGrant };
    }

    // =============================================================================================
    // LAYER 1 — ENFORCEMENT GATE: non-member → 403, zero bytes, handler never reached (per plane).
    // This is the authz-before-stream boundary: the accessible-record-set filter runs to completion
    // BEFORE the download handler, so a denied request never resolves an SPE pointer or streams.
    // =============================================================================================

    [Theory]
    [MemberData(nameof(AllThreePrincipals))]
    public async Task Gate_MemberOfAccessibleSet_AllowsThroughToHandler(WorkforcePrincipal principal)
    {
        var service = new Mock<IAccessibleRecordSetService>();
        service
            .Setup(s => s.IsRecordAccessibleAsync(principal, EntityType, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sentinel = new object();
        var (context, tracker) = BuildFilterContext(principal, ProjectId.ToString(), sentinel);
        var filter = new AccessibleRecordSetAuthorizationFilter(
            service.Object, NullLogger<AccessibleRecordSetAuthorizationFilter>.Instance, EntityType, RouteKey);

        var result = await filter.InvokeAsync(context, tracker.Next);

        tracker.WasCalled.Should().BeTrue("a member must be allowed through to the download handler");
        result.Should().BeSameAs(sentinel);
    }

    [Theory]
    [MemberData(nameof(AllThreePrincipals))]
    public async Task Gate_NonMemberOfAccessibleSet_Denies403WithZeroBytesAndNeverReachesHandler(
        WorkforcePrincipal principal)
    {
        var service = new Mock<IAccessibleRecordSetService>();
        service
            .Setup(s => s.IsRecordAccessibleAsync(principal, EntityType, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var (context, tracker) = BuildFilterContext(principal, ProjectId.ToString(), new object());
        var filter = new AccessibleRecordSetAuthorizationFilter(
            service.Object, NullLogger<AccessibleRecordSetAuthorizationFilter>.Instance, EntityType, RouteKey);

        var result = await filter.InvokeAsync(context, tracker.Next);

        tracker.WasCalled.Should().BeFalse(
            "a non-member must be denied BEFORE the download handler runs — no bytes, no SPE read");
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // =============================================================================================
    // LAYER 2 — DOWNLOAD HANDLER positive path: authorized caller receives bytes via APP-ONLY
    // SpeFileStore.DownloadFileAsync (never OBO), for every principal plane.
    // =============================================================================================

    [Theory]
    [MemberData(nameof(AllThreePrincipals))]
    public async Task Download_AuthorizedPrincipal_StreamsDocumentBytesAppOnly(WorkforcePrincipal principal)
    {
        var bytes = Encoding.UTF8.GetBytes("hello-collaboration-document");
        var dataService = BuildDataService(documentProjectId: ProjectId, documentName: DocumentName);
        var storageResolver = new Mock<IDocumentStorageResolver>();
        storageResolver
            .Setup(r => r.GetSpePointersAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriveId, ItemId));
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        fileStore
            .Setup(f => f.DownloadFileAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(bytes));

        var httpContext = BuildHttpContext(principal);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        // Bytes flow — the app-only download path was taken.
        var fileResult = result.Should().BeOfType<FileStreamHttpResult>().Subject;
        fileResult.ContentType.Should().Be("application/octet-stream");
        // The download name is the document's display name — NOT a Graph pointer.
        fileResult.FileDownloadName.Should().Be(DocumentName);
        fileResult.FileDownloadName.Should().NotContain(DriveId).And.NotContain(ItemId);

        // The OBO path MUST NOT be used for any principal — including a workforce systemuser.
        fileStore.Verify(f => f.DownloadFileAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "collaboration download is app-only (broker-only, NFR-02) — never OBO");
        fileStore.Verify(f => f.DownloadFileAsync(DriveId, ItemId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============================================================================================
    // LAYER 2 — DOWNLOAD HANDLER negative path: document→project scoping denies a cross-project
    // document 403 BEFORE any SPE pointer resolution (authz-before-stream), for every principal plane.
    // =============================================================================================

    [Theory]
    [MemberData(nameof(AllThreePrincipals))]
    public async Task Download_DocumentNotInProject_Denies403BeforeAnyPointerResolutionOrStream(
        WorkforcePrincipal principal)
    {
        // The document belongs to a DIFFERENT project than the one requested.
        var dataService = BuildDataService(documentProjectId: OtherProjectId, documentName: DocumentName);
        // Strict mocks: any SPE pointer resolution or byte read is an authz-before-stream violation.
        var storageResolver = new Mock<IDocumentStorageResolver>(MockBehavior.Strict);
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);

        var httpContext = BuildHttpContext(principal);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // No SPE pointer was resolved and no byte was read — the strict mocks would have thrown.
        storageResolver.Verify(r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fileStore.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllThreePrincipals))]
    public async Task Download_NonExistentDocument_Denies403WithoutLeakingExistence(WorkforcePrincipal principal)
    {
        // GetDocumentProjectAndNameAsync returns (null, null) for a document that does not exist.
        var dataService = BuildDataService(documentProjectId: null, documentName: null);
        var storageResolver = new Mock<IDocumentStorageResolver>(MockBehavior.Strict);
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);

        var httpContext = BuildHttpContext(principal);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        // Uniform 403 (same as cross-project) — do not leak document existence via a 404.
        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        fileStore.VerifyNoOtherCalls();
    }

    // =============================================================================================
    // NO GRAPH POINTER — neither the success nor the denied response exposes driveId/itemId.
    // =============================================================================================

    [Fact]
    public async Task Download_SuccessResponse_ContainsNoGraphPointer()
    {
        var dataService = BuildDataService(documentProjectId: ProjectId, documentName: DocumentName);
        var storageResolver = new Mock<IDocumentStorageResolver>();
        storageResolver
            .Setup(r => r.GetSpePointersAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriveId, ItemId));
        var fileStore = new Mock<ISpeFileOperations>();
        fileStore
            .Setup(f => f.DownloadFileAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("bytes")));

        var httpContext = BuildHttpContext(SystemUser);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileStreamHttpResult>().Subject;
        // The only client-visible metadata is the file name — assert no pointer leaks through it.
        (fileResult.FileDownloadName ?? string.Empty).Should().NotContain(DriveId).And.NotContain(ItemId);
    }

    [Fact]
    public async Task Download_DeniedResponse_ContainsNoGraphPointer()
    {
        var dataService = BuildDataService(documentProjectId: OtherProjectId, documentName: DocumentName);
        var storageResolver = new Mock<IDocumentStorageResolver>(MockBehavior.Strict);
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);

        var httpContext = BuildHttpContext(SystemUser);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        var serialized = $"{problem.ProblemDetails.Title} {problem.ProblemDetails.Detail} " +
                         string.Join(" ", problem.ProblemDetails.Extensions.Select(kv => $"{kv.Key}={kv.Value}"));
        serialized.Should().NotContain(DriveId).And.NotContain(ItemId,
            "a denied response must never carry a Graph pointer");
    }

    // =============================================================================================
    // Pipeline misconfiguration — no resolved principal ⇒ fail closed (500), no bytes.
    // =============================================================================================

    [Fact]
    public async Task Download_NoResolvedPrincipal_FailsClosedWithoutStreaming()
    {
        var dataService = BuildDataService(documentProjectId: ProjectId, documentName: DocumentName);
        var storageResolver = new Mock<IDocumentStorageResolver>(MockBehavior.Strict);
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);

        var httpContext = new DefaultHttpContext(); // no WorkforcePrincipal on Items

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        fileStore.VerifyNoOtherCalls();
    }

    // =============================================================================================
    // Post-authorization storage problem — resolver throws SdapProblemException ⇒ surfaced code/status,
    // still no Graph pointer.
    // =============================================================================================

    [Fact]
    public async Task Download_StorageResolverThrowsSdapProblem_SurfacesCodeWithoutPointer()
    {
        var dataService = BuildDataService(documentProjectId: ProjectId, documentName: DocumentName);
        var storageResolver = new Mock<IDocumentStorageResolver>();
        storageResolver
            .Setup(r => r.GetSpePointersAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SdapProblemException("mapping_missing_drive", "Conflict",
                "Document storage mapping is incomplete.", StatusCodes.Status409Conflict));
        var fileStore = new Mock<ISpeFileOperations>(MockBehavior.Strict);

        var httpContext = BuildHttpContext(ContactWithGrant);

        var result = await WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent(
            ProjectId, DocumentId, httpContext, dataService.Object, storageResolver.Object,
            fileStore.Object, NullLogger<Program>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions.Should().ContainKey("code");
        problem.ProblemDetails.Extensions["code"].Should().Be("mapping_missing_drive");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static Mock<ExternalDataService> BuildDataService(Guid? documentProjectId, string? documentName)
    {
        // GetDocumentProjectAndNameAsync is virtual — mock it directly. Other ctor deps are inert.
        var mock = new Mock<ExternalDataService>(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            Mock.Of<TokenCredential>(),
            NullLogger<ExternalDataService>.Instance)
        {
            CallBase = false
        };
        mock
            .Setup(d => d.GetDocumentProjectAndNameAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((documentProjectId, documentName));
        return mock;
    }

    private static DefaultHttpContext BuildHttpContext(WorkforcePrincipal principal)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] = principal;
        return httpContext;
    }

    private static (EndpointFilterInvocationContext Context, NextTracker Tracker) BuildFilterContext(
        WorkforcePrincipal principal, string rawRecordId, object sentinel)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] = principal;
        httpContext.Request.RouteValues[RouteKey] = rawRecordId;
        var context = EndpointFilterInvocationContext.Create(httpContext);
        return (context, new NextTracker(sentinel));
    }

    private sealed class NextTracker
    {
        private readonly object _sentinel;
        public bool WasCalled { get; private set; }
        public NextTracker(object sentinel) => _sentinel = sentinel;
        public ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            WasCalled = true;
            return ValueTask.FromResult<object?>(_sentinel);
        }
    }
}
