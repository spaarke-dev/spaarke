// Compose body-scoped session + claim-tenant authorization — unified-access-control-r2 task 166.
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Compose/**`
//   - Justification: the route-authorization sweep (notes/route-authorization-sweep-2026-10-02.md) found the
//     Compose routes that take a SESSION id in the BODY (upload S-64, save / create-on-save amendment d) or a
//     TENANT in the body (annotations S-80) acting on whatever the body named. Session ownership on those routes
//     is a handler-level decision (no route filter can see a body id), so only the REAL route, the REAL
//     ChatSessionManager and the REAL handler prove it.
//
// Doubles are module boundaries only (ADR-038 §4): the SPE facade, the Dataverse entity service and the indexing
// enqueuer of the shared ComposeFidelitySeamFixture; ChatSessionManager's virtual GetSessionAsync for the pure
// decision-table section. No Mock<HttpMessageHandler>, no DI-registration assertion, no ctor null-check.
//
// Check-changes (S-65) is pinned in ComposeWordShuttlePollEndpointContractTests (its fixture owns that route's
// SPE delta boundary).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Compose;

public sealed class ComposeSessionAndContainerAuthorizationContractTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private const string DocBinaryResource = "doc-upload-binary";
    private const int DocCacheVersion = 1;
    private const string Tenant = ComposeFidelitySeamFixture.TestTenantId;

    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeSessionAndContainerAuthorizationContractTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    // =============================================================================================
    // 1. The ONE body-scoped ownership decision — ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync.
    //    Shared by /upload, /save and /create-on-save, so its truth table is pinned once, here.
    // =============================================================================================

    private const string CallerOid = "11111111-0000-4000-8000-000000000166";
    private const string OtherOid = "22222222-0000-4000-8000-000000000166";

    private static (Mock<ChatSessionManager> Manager, Dictionary<(string Tenant, string Id), ChatSession> Store) SessionStore()
    {
        var store = new Dictionary<(string, string), ChatSession>();
        var manager = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(), Mock.Of<IChatDataverseRepository>(), NullLogger<ChatSessionManager>.Instance, null!, null!);
        manager
            .Setup(m => m.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenant, string id, CancellationToken _) => store.TryGetValue((tenant, id), out var s) ? s : null);
        return (manager, store);
    }

    private static ChatSession Session(string id, string? ownerOid) =>
        new(SessionId: id, TenantId: Tenant, DocumentId: null, PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow, LastActivity: DateTimeOffset.UtcNow, Messages: [])
        { OwnerOid = ownerOid };

    [Fact]
    public async Task ResolveOwned_TheCallersOwnSession_IsReturnedWithTheSpellingItWasFoundUnder()
    {
        var (manager, store) = SessionStore();
        var id = Guid.NewGuid();
        store[(Tenant, id.ToString("N"))] = Session(id.ToString("N"), CallerOid);

        // Sent in the "D" spelling; stored under "N" — the tolerance the upload path always applied.
        var (session, key) = await ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, id.ToString("D"), CallerOid, CancellationToken.None);

        session.Should().NotBeNull();
        key.Should().Be(id.ToString("N"));
    }

    public static TheoryData<string, string?> NotOwnedShapes => new()
    {
        { "someone-elses", OtherOid },
        { "unowned-null", null },
        { "unowned-empty", "" },
        { "not-found", "n/a" },
    };

    [Theory]
    [MemberData(nameof(NotOwnedShapes))]
    public async Task ResolveOwned_AnythingButTheCallersOwnSession_IsTheSameNull(string shape, string? owner)
    {
        var (manager, store) = SessionStore();
        var id = Guid.NewGuid().ToString("N");
        if (shape != "not-found")
        {
            store[(Tenant, id)] = Session(id, owner);
        }

        var result = await ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, id, CallerOid, CancellationToken.None);

        result.Session.Should().BeNull("not-yours, unowned (pre-#863) and not-found are ONE answer");
        result.Key.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ResolveOwned_ACallerWithNoOid_OwnsNothing_AndTheStoreIsNotAsked(string? callerOid)
    {
        var (manager, store) = SessionStore();
        store[(Tenant, "s1")] = Session("s1", OtherOid);

        var result = await ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, "s1", callerOid, CancellationToken.None);

        result.Session.Should().BeNull();
        manager.Verify(m => m.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveOwned_IsTenantScoped_TheSameIdInAnotherTenantIsNotFound()
    {
        var (manager, store) = SessionStore();
        store[("another-tenant", "s1")] = Session("s1", CallerOid) with { TenantId = "another-tenant" };

        var result = await ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, "s1", CallerOid, CancellationToken.None);

        result.Session.Should().BeNull("the lookup runs under the CLAIM tenant the handler passes");
    }

    [Fact]
    public async Task ResolveOwned_TheFirstSpellingFoundDecides_ALaterSpellingIsNeverTriedToFindAnOwnedOne()
    {
        var (manager, store) = SessionStore();
        var id = Guid.NewGuid();
        store[(Tenant, id.ToString("D"))] = Session(id.ToString("D"), OtherOid);   // found first — someone else's
        store[(Tenant, id.ToString("N"))] = Session(id.ToString("N"), CallerOid);  // the caller's, under a later spelling

        var result = await ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, id.ToString("D"), CallerOid, CancellationToken.None);

        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwned_AStoreFaultPropagates_SoTheRouteFoldsItIntoItsOwnRefusal_NeverOwned()
    {
        var (manager, _) = SessionStore();
        manager
            .Setup(m => m.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("redis timed out"));

        var act = () => ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync(
            manager.Object, Tenant, "s1", CallerOid, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // =============================================================================================
    // 2. POST /api/compose/upload (S-64) — the retained bytes go to the session's OWNER only.
    // =============================================================================================

    private async Task<string> CreateSessionAsync(string ownerOid)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        return (await sessions.CreateSessionAsync(Tenant, ownerOid, documentId: null)).SessionId;
    }

    private async Task SeedUploadAsync(string sessionKey, string documentId, byte[] bytes)
    {
        var cache = _fixture.Services.GetRequiredService<ITenantCache>();
        await cache.SetAsync(Tenant, DocBinaryResource, $"{sessionKey}:{documentId}", DocCacheVersion, bytes, TimeSpan.FromHours(1));
    }

    private static readonly byte[] SecretBytes = "SECRET-CONTRACT-BYTES"u8.ToArray();

    [Fact]
    public async Task Upload_AnotherUsersSession_IsTheSame404AsExpiredBytes_AndReturnsNoneOfTheirBytes()
    {
        _fixture.ResetBoundaries();
        var othersSession = await CreateSessionAsync(TestSessionOwner.OtherOid);
        var documentId = $"doc-{Guid.NewGuid():N}";
        await SeedUploadAsync(othersSession, documentId, SecretBytes);

        var ownSession = await CreateSessionAsync(TestSessionOwner.Oid);
        using var client = _fixture.CreateAuthenticatedClient();

        var stolen = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = othersSession, documentId });
        var expired = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = ownSession, documentId = "doc-never-uploaded" });

        stolen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        expired.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stolenBody = await stolen.Content.ReadAsStringAsync();
        Normalize(stolenBody).Should().Be(Normalize(await expired.Content.ReadAsStringAsync()),
            "another user's session must be indistinguishable from bytes that expired");
        stolenBody.Should().NotContain("SECRET-CONTRACT-BYTES").And.NotContain(Convert.ToBase64String(SecretBytes));
    }

    [Fact]
    public async Task Upload_AnUnknownSessionId_IsTheSameUniform404()
    {
        _fixture.ResetBoundaries();
        var unknown = Guid.NewGuid().ToString("N");
        var documentId = $"doc-{Guid.NewGuid():N}";
        await SeedUploadAsync(unknown, documentId, SecretBytes); // bytes exist under a session nobody owns

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = unknown, documentId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Uploaded File Not Available")
            .And.NotContain("SECRET-CONTRACT-BYTES");
    }

    [Fact]
    public async Task Upload_TheOwnersSessionSentInTheOtherGuidSpelling_StillMounts()
    {
        _fixture.ResetBoundaries();
        var ownSessionN = await CreateSessionAsync(TestSessionOwner.Oid);
        var asSentD = Guid.ParseExact(ownSessionN, "N").ToString("D");
        var documentId = $"doc-{Guid.NewGuid():N}";
        await SeedUploadAsync(asSentD, documentId, SecretBytes); // the chat upload keyed it by the spelling it was sent

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = asSentD, documentId });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the ownership lookup tolerates the N/D spellings exactly as the byte lookup always did");
        var body = await response.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        body!.Content.Should().Equal(SecretBytes);
    }

    /// <summary>
    /// S-64, both outcomes in one test (the route ledger's "ProvenByTest" credit): the body sessionId must be a session
    /// the caller owns (ResolveOwnedSessionAsync). Another user's session holding bytes is the uniform 404 and returns
    /// none of them; the caller's own session holding bytes is 200 and mounts exactly those bytes.
    /// </summary>
    [Fact]
    public async Task ProvenByTest_Upload_AnotherUsersSessionIs404_TheOwnersSessionIs200()
    {
        _fixture.ResetBoundaries();
        var othersSession = await CreateSessionAsync(TestSessionOwner.OtherOid);
        var ownSession = await CreateSessionAsync(TestSessionOwner.Oid);
        var documentId = $"doc-{Guid.NewGuid():N}";
        var ownBytes = "OWNERS-PROOF-BYTES"u8.ToArray();
        await SeedUploadAsync(othersSession, documentId, SecretBytes);
        await SeedUploadAsync(ownSession, documentId, ownBytes);
        using var client = _fixture.CreateAuthenticatedClient();

        var stolen = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = othersSession, documentId });

        stolen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var stolenBody = await stolen.Content.ReadAsStringAsync();
        stolenBody.Should().NotContain("SECRET-CONTRACT-BYTES").And.NotContain(Convert.ToBase64String(SecretBytes));

        var own = await client.PostAsJsonAsync("/api/compose/upload", new { sessionId = ownSession, documentId });

        own.StatusCode.Should().Be(HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
        var mounted = await own.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        mounted!.Content.Should().Equal(ownBytes, "the owner's session mounts the owner's bytes, never the other user's");
    }

    // =============================================================================================
    // 3. POST /api/compose/sessions/{sessionId}/annotations (S-80) — the write uses the CLAIM tenant the
    //    ownership filter authorized, never the body's.
    // =============================================================================================

    [Fact]
    public async Task SaveAnnotations_AForeignBodyTenant_IsIgnored_TheWriteLandsOnTheAuthorizedSession()
    {
        _fixture.ResetBoundaries();
        var ownSession = await CreateSessionAsync(TestSessionOwner.Oid);
        using var client = _fixture.CreateAuthenticatedClient();

        var save = await client.PostAsJsonAsync($"/api/compose/sessions/{ownSession}/annotations", new
        {
            tenantId = "a-different-tenant-the-filter-never-checked",
            definedTermsTracking = new[] { new { term = "Effective Date", definition = "as defined in 1.2", source = "human" } },
        });

        save.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ChatSessionManager>()
            .GetSessionAsync(Tenant, ownSession, CancellationToken.None);
        stored!.DefinedTermsTracking.Should().ContainSingle(t => t.Term == "Effective Date",
            "the write went to (CLAIM tenant, sessionId) — the record the ownership filter authorized");
    }

    [Fact]
    public async Task SaveAnnotations_ABodyWithNoTenantAtAll_IsAccepted()
    {
        _fixture.ResetBoundaries();
        var ownSession = await CreateSessionAsync(TestSessionOwner.Oid);
        using var client = _fixture.CreateAuthenticatedClient();

        var save = await client.PostAsJsonAsync($"/api/compose/sessions/{ownSession}/annotations", new
        {
            definedTermsTracking = Array.Empty<object>(),
        });

        save.StatusCode.Should().Be(HttpStatusCode.OK, "tenantId is obsolete on the body — no longer required, never read");
    }

    // =============================================================================================
    // 4. POST /api/compose/documents/create-on-save (amendment d) — a session the caller does not own is NOT
    //    bound: no rebind of its document id, no session-derived container, and the body tenant is ignored.
    // =============================================================================================

    private Guid ArrangeCreateOnSaveBoundary()
    {
        var newDocumentId = Guid.NewGuid();
        const string driveId = "drive-uac166-create";
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(driveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<Sprk.Bff.Api.Models.ConflictBehavior>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: $"spe-item-uac166-{newDocumentId:N}", Name: "draft.docx", ParentId: null, Size: 0,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1\"", IsFolder: false, WebUrl: null, DriveId: driveId));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((newDocumentId, true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        return newDocumentId;
    }

    private static object CreateOnSaveBody(string sessionId, string bodyTenant) => new
    {
        containerId = "b!container-uac166",
        tenantId = bodyTenant,
        sessionId,
        displayName = "Agreement.docx",
        contentModel = new { blocks = new object[] { new { kind = "paragraph", runs = new[] { new { text = "Body." } } } } },
    };

    private async Task<ChatSession?> ReadSessionAsync(string sessionId)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ChatSessionManager>()
            .GetSessionAsync(Tenant, sessionId, CancellationToken.None);
    }

    [Fact]
    public async Task CreateOnSave_AnotherUsersSession_IsNotRebound_TheSaveRunsUnbound_AndEchoesTheSentId()
    {
        _fixture.ResetBoundaries();
        var othersSession = await CreateSessionAsync(TestSessionOwner.OtherOid);
        ArrangeCreateOnSaveBoundary();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", CreateOnSaveBody(othersSession, Tenant));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "not-yours is treated exactly as not-found (the Compose Load #863 rule): the user's work still saves");
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result!.SessionId.Should().Be(othersSession, "the wire shape is unchanged — the sent id is echoed");
        (await ReadSessionAsync(othersSession))!.DocumentId.Should().BeNull(
            "another user's session must never have its document id rebound by this caller's save");
    }

    [Fact]
    public async Task CreateOnSave_TheCallersOwnSession_IsRebound_EvenWhenTheBodyNamesAForeignTenant()
    {
        _fixture.ResetBoundaries();
        var ownSession = await CreateSessionAsync(TestSessionOwner.Oid);
        var newDocumentId = ArrangeCreateOnSaveBoundary();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save", CreateOnSaveBody(ownSession, "a-foreign-body-tenant"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadSessionAsync(ownSession))!.DocumentId.Should().Be(newDocumentId.ToString(),
            "the session is looked up under the CLAIM tenant — the body tenant is never read — so the owner's own "
            + "session is bound (the FR-07 rebind) exactly as before");
    }

    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }
}
