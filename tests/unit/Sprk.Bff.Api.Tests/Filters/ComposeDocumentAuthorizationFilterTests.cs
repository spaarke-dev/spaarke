using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// unified-access-control-r2 task 171 — <see cref="ComposeDocumentAuthorizationFilter"/> ties a Compose route's
/// client-chosen <c>{documentSpeId}</c> to its <c>sprk_document</c> before any byte moves app-only, and
/// <see cref="ComposeSpeAccess"/> picks the identity from exactly that decision.
/// </summary>
/// <remarks>Drives the REAL filter over the REAL <see cref="AuthorizationService"/> (with the production
/// <see cref="OperationAccessRule"/>) and the REAL pointer check (<see cref="TestRecordContainerResolver"/>); the
/// substitutions are the access data source (the caller's rights, stated), the Dataverse row lookup, and the SPE facade
/// (ADR-038 §4).</remarks>
public class ComposeDocumentAuthorizationFilterTests
{
    private const string ItemId = "01ITEMCOMPOSE171";
    private const string RowDrive = "b!row-drive-171";
    private const string ClientDrive = "b!client-chosen-drive";
    private static readonly Guid DocumentId = Guid.Parse("17120000-0000-4000-8000-000000000001");

    private readonly Mock<IGenericEntityService> _entities = new();

    private void RowExists()
        => _entities.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", DocumentId) { ["sprk_graphdriveid"] = RowDrive });

    private async Task<(object? Result, bool Reached, HttpContext Http)> InvokeAsync(
        AccessRights rights, string operation = "read", RecordContainerResolver? resolver = null)
    {
        var authorization = new AuthorizationService(
            new StubAccessDataSource(rights),
            [new OperationAccessRule(NullLogger<OperationAccessRule>.Instance)],
            NullLogger<AuthorizationService>.Instance);
        var pointerCheck = resolver ?? TestRecordContainerResolver.ForBusinessUnitContainers(c => c.StartsWith("b!", StringComparison.Ordinal));
        var filter = new ComposeDocumentAuthorizationFilter(
            () => authorization,
            _entities.Object,
            () => pointerCheck,
            NullLogger<ComposeDocumentAuthorizationFilter>.Instance,
            operation);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", Guid.NewGuid().ToString())], "Test")),
        };
        http.Request.Headers.Authorization = "Bearer caller-token";
        http.Request.RouteValues["documentSpeId"] = ItemId;

        var reached = false;
        var result = await filter.InvokeAsync(EndpointFilterInvocationContext.Create(http), _ =>
        {
            reached = true;
            return ValueTask.FromResult<object?>("handler");
        });
        return (result, reached, http);
    }

    [Fact(DisplayName = "Task 171: a Compose item WITH a row and a caller holding Read reaches the handler, marked with the ROW's drive")]
    public async Task RowBacked_Allowed_MarksTheRowsDriveAndItem()
    {
        RowExists();

        var (result, reached, http) = await InvokeAsync(AccessRights.Read);

        reached.Should().BeTrue();
        result.Should().Be("handler");
        ComposeBrokeredDocument.From(http).Should().Be(new ComposeBrokeredDocument(DocumentId, RowDrive, ItemId));
        ComposeBrokeredDocument.Covers(http, ClientDrive, ItemId).Should().BeFalse(
            "only the ROW's drive is authorized — a client-chosen drive keeps the caller's own (OBO) identity");
    }

    [Fact(DisplayName = "Task 171: a caller with NO rights on the row is refused 403 and the handler never runs")]
    public async Task RowBacked_Denied_Is403_AndNeverReachesTheHandler()
    {
        RowExists();

        var (result, reached, http) = await InvokeAsync(AccessRights.None);

        reached.Should().BeFalse();
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
        ComposeBrokeredDocument.From(http).Should().BeNull();
    }

    [Fact(DisplayName = "Task 171: Read is not enough for a Compose SAVE — write is required")]
    public async Task RowBacked_ReadOnlyCallerOnSave_Is403()
    {
        RowExists();

        var (result, reached, _) = await InvokeAsync(AccessRights.Read, operation: "write");

        reached.Should().BeFalse();
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact(DisplayName = "Task 171: an item with NO row (Path B) passes through UNMARKED — SPE's OBO answer stays the decision")]
    public async Task NoRow_PassesThroughUnmarked()
    {
        var (_, reached, http) = await InvokeAsync(AccessRights.None);

        reached.Should().BeTrue();
        ComposeBrokeredDocument.From(http).Should().BeNull("nothing authorized this item, so nothing may run app-only");
    }

    [Fact(DisplayName = "Task 171: a row lookup that FAULTS is a 503 — never a pass-through to the unmarked path")]
    public async Task LookupFault_Is503()
    {
        _entities.Setup(e => e.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var (result, reached, _) = await InvokeAsync(AccessRights.Read);

        reached.Should().BeFalse();
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(503);
    }

    [Fact(DisplayName = "Task 171: an allowed caller whose row pointer cannot be verified is NOT marked — the bytes keep the caller's own (OBO) identity, never app-only")]
    public async Task RowBacked_UnverifiablePointer_IsNotMarked()
    {
        RowExists();

        var (_, reached, http) = await InvokeAsync(AccessRights.Read, resolver: TestRecordContainerResolver.ForBusinessUnitContainers());

        reached.Should().BeTrue("the Dataverse decision allowed; only the identity of the byte calls is affected");
        ComposeBrokeredDocument.From(http).Should().BeNull(
            "an unverified pointer must never be followed as the application — SPE decides for the caller, as before");
    }

    // ── ComposeSpeAccess — one identity rule ───────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 171: a Compose read of the AUTHORIZED item runs app-only; any other item runs as the caller")]
    public async Task ComposeSpeAccess_ChoosesIdentityFromTheAuthorization()
    {
        var spe = new Mock<ISpeFileOperations>();
        var http = new DefaultHttpContext();
        http.Items[ComposeBrokeredDocument.ItemKey] = new ComposeBrokeredDocument(DocumentId, RowDrive, ItemId);

        await spe.Object.DownloadForComposeAsync(http, RowDrive, ItemId, CancellationToken.None);
        await spe.Object.ReplaceForComposeAsync(http, RowDrive, ItemId, Stream.Null, "\"etag\"", CancellationToken.None);
        await spe.Object.DownloadForComposeAsync(http, ClientDrive, ItemId, CancellationToken.None);
        await spe.Object.GetMetadataForComposeAsync(new DefaultHttpContext(), RowDrive, ItemId, CancellationToken.None);

        spe.Verify(s => s.DownloadFileAsync(RowDrive, ItemId, It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.ReplaceFileContentAsync(RowDrive, ItemId, It.IsAny<Stream>(), "\"etag\"", It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.DownloadFileAsUserAsync(http, ClientDrive, ItemId, It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), RowDrive, ItemId, It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.GetFileMetadataUncachedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class StubAccessDataSource(AccessRights rights) : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
            => Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = rights });

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
            => throw new NotSupportedException("The Compose filter authorizes the DOCUMENT row.");
    }
}
