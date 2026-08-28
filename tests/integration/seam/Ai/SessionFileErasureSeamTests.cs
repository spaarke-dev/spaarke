using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice-seam KEEP category (ADR-038 §2).
/// spaarkeai-compose-r8 FR-B06 (task 063): after a session deletion or a GDPR erasure, no byte of that
/// session's files exists in any location — and a partial failure says so instead of reporting success.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this suite exists to catch.</b> Erasure fails by OMISSION. The session record
/// disappears, History shows nothing, the manifest is gone — and the original documents are still in
/// blob storage. Nothing in the product surfaces that gap, and no functional test notices, because
/// every visible thing behaved correctly. The only way to catch it is to observe the STORES after a
/// deletion, which is what every test below does.
/// </para>
/// <para>
/// <b>What is real and what is substituted.</b> The upload half runs through the real wire
/// (<c>POST /api/ai/chat/sessions/{id}/documents</c> against <see cref="ChatDocumentEndpointsTestFixture"/>),
/// so the durable blob and the four <c>doc-upload-*</c> cache entries are written by production code
/// using production key construction. The erasure half is the production
/// <see cref="ChatSessionManager.DeleteSessionAsync"/> driving the production
/// <see cref="SessionFileEraser"/> and <see cref="SessionFileBlobStore"/>. Two boundaries are
/// substituted, both genuinely external: the Azure Blob SDK
/// (<see cref="InMemorySessionFileBlobGateway"/>, which resolves names the way Blob Storage does —
/// opaque, ordinal, exact-match, prefix = ordinal <c>StartsWith</c>) and Redis (a real in-memory
/// <see cref="ITenantCache"/>). Nothing about enumeration, deletion, verification or key construction
/// is re-implemented by the test.
/// </para>
/// <para>
/// <b>Why the cache assertions use the fixture's own cache.</b> The eraser must compose the SAME key
/// the writer composed. Asserting that against keys the test itself built would prove only that the
/// test agrees with the eraser. Running it against the entries a real upload left behind is what makes
/// the assertion load-bearing.
/// </para>
/// <para>
/// <b>Observed to fail before it passed.</b> Recorded in
/// <c>projects/spaarkeai-compose-r8/notes/track-b-erasure-surface.md</c> §"Verification record" —
/// including the deliberate break that drops the tenant segment from the erasure prefix, and the one
/// that treats a failed delete as success.
/// </para>
/// </remarks>
public sealed class SessionFileErasureSeamTests : IClassFixture<ChatDocumentEndpointsTestFixture>
{
    private const string TenantA = "00000000-0000-0000-0000-000000000abc";
    private const string TenantB = "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb";

    private readonly ChatDocumentEndpointsTestFixture _fx;

    public SessionFileErasureSeamTests(ChatDocumentEndpointsTestFixture fx) => _fx = fx;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Completeness — every enumerated location, observed empty afterwards.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingASession_ErasesTheDurableBytesAndTheFourHourCacheCopiesOfTheSameFile()
    {
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);

        (await UploadAsync(sessionId, "settlement.pdf", "PRIVILEGED — must not survive an erasure."))
            .IsSuccessStatusCode.Should().BeTrue();

        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // POSITIVE CONTROLS FIRST. Every negative below is meaningless without proof the bytes were
        // there to miss — a cross-store "it's gone" assertion passes vacuously against an empty store.
        _fx.DurableBlobs.Count.Should().Be(1, "positive control: the durable copy must exist to be erased");
        (await ReadCachedBinaryAsync(sessionId, fileId)).Should().NotBeNull(
            "positive control: the 4-hour hot copy of the ORIGINAL bytes must exist to be erased");
        (await ReadCachedTextAsync(sessionId, fileId)).Should().NotBeNullOrEmpty(
            "positive control: the 4-hour hot copy of the EXTRACTED TEXT must exist to be erased");

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.Erased);
        erasure.BlobsDeleted.Should().Be(1);
        erasure.Reason.Should().Be(SessionFileEraser.ReasonComplete);

        _fx.DurableBlobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, sessionId, fileId), out _)
            .Should().BeFalse("the durable byte copy is the location a session deletion previously missed entirely");
        (await ReadCachedBinaryAsync(sessionId, fileId)).Should().BeNull(
            "the original bytes also live in Redis for four hours — bounded is not the same as erased");
        (await ReadCachedTextAsync(sessionId, fileId)).Should().BeNull(
            "the extracted text is the file's content in another form and must go with it");
    }

    [Fact]
    public async Task Erasure_EnumeratesByPrefix_SoItReachesABlobNoManifestNames()
    {
        // THE property that decides the architecture. The durable write lands BEFORE the (deliberately
        // non-fatal) manifest write, and the Cosmos manifest expires at 90 days while the blobs have no
        // TTL at all — so a manifest-driven erasure would leave orphans behind permanently AND make
        // them invisible to every future erasure. Prefix enumeration reaches them. (Task 060 notes,
        // open item 6.)
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);

        (await UploadAsync(sessionId, "named.pdf", "named by the manifest")).IsSuccessStatusCode.Should().BeTrue();
        var manifestFileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // An orphan: durably stored under the session's prefix, named by no manifest anywhere.
        const string OrphanFileId = "0rphan00000000000000000000000000";
        await _fx.DurableFileStore.WriteAsync(
            TenantA, sessionId, OrphanFileId, BinaryData.FromString("orphaned bytes"), "application/pdf");

        _fx.DurableBlobs.Count.Should().Be(2, "positive control: one manifest-named blob and one orphan");

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.Erased);
        erasure.BlobsDeleted.Should().Be(2,
            "erasure enumerates the blob PREFIX, so it reaches a copy no manifest names — a manifest " +
            "walk would have deleted one of these two and reported success");
        erasure.FileIds.Should().Contain(OrphanFileId);

        _fx.DurableBlobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, sessionId, OrphanFileId), out _)
            .Should().BeFalse();
        _fx.DurableBlobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, sessionId, manifestFileId), out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Erasure_TouchesOnlyTheSessionBeingDeleted_NotTheTenantsOtherSessions()
    {
        // The prefix is per-SESSION, not per-tenant. Deleting one conversation must not erase the
        // files of every other conversation the same user has open.
        var doomed = NewSessionId();
        var survivor = NewSessionId();

        _fx.Reset();
        _fx.Sessions.Session = BuildSession(doomed);
        (await UploadAsync(doomed, "doomed.pdf", "goes")).IsSuccessStatusCode.Should().BeTrue();

        _fx.Sessions.Session = BuildSession(survivor);
        _fx.Sessions.PersistedSession = null;
        (await UploadAsync(survivor, "keeper.pdf", "stays")).IsSuccessStatusCode.Should().BeTrue();
        var survivorFileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        _fx.DurableBlobs.Count.Should().Be(2);

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, doomed);

        erasure.BlobsDeleted.Should().Be(1);
        _fx.DurableBlobs.Count.Should().Be(1);
        (await _fx.DurableFileStore.ReadAsync(TenantA, survivor, survivorFileId))!.Content.ToString()
            .Should().Be("stays", "another session of the SAME tenant is a different prefix");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Idempotency — a repeat succeeds, and a partial prior erasure completes.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Erasure_IsIdempotent_ASecondDeleteSucceedsWithNothingLeftToDo()
    {
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "once.pdf", "content")).IsSuccessStatusCode.Should().BeTrue();

        var manager = BuildManager();

        (await manager.DeleteSessionAsync(TenantA, sessionId)).BlobsDeleted.Should().Be(1);

        var second = await manager.DeleteSessionAsync(TenantA, sessionId);

        second.State.Should().Be(SessionFileErasureState.Erased,
            "a repeated erasure request must succeed — retention passes, user retries and concurrent " +
            "erasures overlap by design (the memory-items precedent swallows the already-gone case too)");
        second.BlobsDeleted.Should().Be(0);
        second.Failures.Should().Be(0);
    }

    [Fact]
    public async Task Erasure_CompletesAPartiallyErasedSession()
    {
        // The state a transient failure leaves behind. It must converge on retry, and it does WITHOUT
        // any manifest — which is the whole reason the caller can fail closed cheaply.
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);

        (await UploadAsync(sessionId, "one.pdf", "first")).IsSuccessStatusCode.Should().BeTrue();
        var firstFileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // A second durable copy under the same session, then a hand-made "partial erasure": the first
        // file is already gone, the second is not.
        const string SecondFileId = "second00000000000000000000000000";
        await _fx.DurableFileStore.WriteAsync(
            TenantA, sessionId, SecondFileId, BinaryData.FromString("second"), "application/pdf");
        (await _fx.DurableFileStore.DeleteAsync(TenantA, sessionId, firstFileId)).Should().BeTrue();

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.Erased);
        erasure.BlobsDeleted.Should().Be(1, "exactly the residue — the already-gone copy is not re-deleted");
        _fx.DurableBlobs.Count.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 3. Partial failure — the mirror of task 062's "a Cosmos outage must not read as expired".
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFailedDelete_ReportsIncomplete_AndLeavesTheSessionRecordIntactSoTheRetryConverges()
    {
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "stubborn.pdf", "these bytes refuse to go")).IsSuccessStatusCode.Should().BeTrue();
        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        var dataverse = new Mock<IChatDataverseRepository>();
        var signal = new RecordingCleanupSignal();
        var manager = BuildManager(dataverse.Object, signal);

        _fx.DurableBlobs.FailNextDelete = true;

        var erasure = await manager.DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.Incomplete,
            "a transient failure mid-erase must NOT report success — an erasure that silently skipped " +
            "bytes is a compliance failure that looks exactly like a completed one");
        erasure.Failures.Should().Be(1);
        erasure.BlobsRemaining.Should().Be(1, "the verification re-enumeration must SEE the residue");
        erasure.Reason.Should().Be(SessionFileEraser.ReasonDeleteFailed);

        // The record is untouched, so the user still sees the conversation and the natural retry fixes it.
        dataverse.Verify(
            r => r.ArchiveSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the session must not be archived when its bytes may still exist — a vanished session with " +
            "surviving documents is the invisible failure this ordering exists to prevent");
        signal.Signals.Should().BeEmpty(
            "and the hot-index eviction must not fire either: nothing about this session was deleted");

        // Convergence: the same call, once the transient condition clears, completes the erasure.
        var retry = await manager.DeleteSessionAsync(TenantA, sessionId);
        retry.State.Should().Be(SessionFileErasureState.Erased);
        _fx.DurableBlobs.TryPeek(SessionFileBlobStore.BuildBlobName(TenantA, sessionId, fileId), out _)
            .Should().BeFalse();
        dataverse.Verify(
            r => r.ArchiveSessionAsync(TenantA, sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailedEnumeration_ReportsIncomplete_NotErased()
    {
        // The most dangerous shape: nothing threw at the delete loop because the delete loop never ran.
        // "Zero blobs found" and "could not look" must not be the same answer.
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "unreachable.pdf", "content")).IsSuccessStatusCode.Should().BeTrue();

        _fx.DurableBlobs.FailNextList = true;

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.Incomplete);
        erasure.Reason.Should().Be(SessionFileEraser.ReasonEnumerationFailed);
        _fx.DurableBlobs.Count.Should().Be(1, "and nothing was destroyed on the way to that verdict");
    }

    [Fact]
    public async Task AnIncompleteErasure_IsA500WithAStableErrorCode_NeverA204()
    {
        // The user-visible half of the contract, asserted against the real endpoint handler.
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "stubborn.pdf", "content")).IsSuccessStatusCode.Should().BeTrue();

        var manager = BuildManager(seedSessionInCache: true, sessionId: sessionId);
        _fx.DurableBlobs.FailNextDelete = true;

        var result = await ChatEndpoints.DeleteSessionAsync(
            sessionId, manager, BuildHttpContext(TenantA),
            NullLogger<ChatSessionManager>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "204 would assert the session and its files are gone, and that assertion would be false");
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode");
        problem.ProblemDetails.Extensions["errorCode"].Should()
            .Be(ChatEndpoints.DurableErasureIncompleteErrorCode);

        // Positive control in the same body: the identical call, with no injected failure, is a 204.
        var ok = await ChatEndpoints.DeleteSessionAsync(
            sessionId, manager, BuildHttpContext(TenantA),
            NullLogger<ChatSessionManager>.Instance, CancellationToken.None);

        ok.Should().BeOfType<NoContent>(
            "without this control the 500 assertion would also pass if the handler 500'd unconditionally");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Tenant isolation on the ERASURE path (ADR-014 / ADR-015).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ErasingOneTenantsSession_LeavesAnotherTenantsIdenticallyIdentifiedFilesAlone()
    {
        var sessionId = NewSessionId();
        _fx.Reset();

        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "a.pdf", "tenant A content")).IsSuccessStatusCode.Should().BeTrue();
        var fileIdA = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // Tenant B holds the SAME session id — the realistic case, since session ids travel in URLs,
        // manifests, telemetry and client state.
        _fx.Auth.TenantId = TenantB;
        _fx.Sessions.Session = BuildSession(sessionId, TenantB);
        _fx.Sessions.PersistedSession = null;
        (await UploadAsync(sessionId, "b.pdf", "tenant B content")).IsSuccessStatusCode.Should().BeTrue();
        var fileIdB = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        _fx.DurableBlobs.Count.Should().Be(2, "positive control: both tenants' copies exist");

        var erasure = await BuildManager().DeleteSessionAsync(TenantA, sessionId);

        erasure.BlobsDeleted.Should().Be(1,
            "an erasure is scoped to the CALLING tenant's prefix — an identically-identified session " +
            "in another tenant is not part of it");
        (await _fx.DurableFileStore.ReadAsync(TenantA, sessionId, fileIdA)).Should().BeNull();
        (await _fx.DurableFileStore.ReadAsync(TenantB, sessionId, fileIdB))!.Content.ToString()
            .Should().Be("tenant B content", "tenant B's bytes must survive tenant A's erasure");
    }

    [Fact]
    public async Task ErasureRequestedByAnotherTenant_DestroysNothing()
    {
        // The highest-consequence form of "identifiers are not authority": a cross-tenant read leaks,
        // a cross-tenant ERASURE is unrecoverable data loss.
        var sessionId = NewSessionId();
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(sessionId);
        (await UploadAsync(sessionId, "privileged.pdf", "PRIVILEGED — tenant A only.")).IsSuccessStatusCode.Should().BeTrue();
        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        var manager = BuildManager();

        // Tenant B, holding tenant A's exact session id, asks for that session to be erased.
        var crossTenant = await manager.DeleteSessionAsync(TenantB, sessionId);

        crossTenant.BlobsDeleted.Should().Be(0);
        crossTenant.State.Should().Be(SessionFileErasureState.Erased,
            "from tenant B's side the prefix is genuinely empty — another tenant's session is not " +
            "merely undeletable, it is invisible");
        (await _fx.DurableFileStore.ReadAsync(TenantA, sessionId, fileId))!.Content.ToString()
            .Should().Be("PRIVILEGED — tenant A only.", "tenant A's bytes must be untouched");

        // POSITIVE CONTROL: the owning tenant CAN erase it. Without this, the assertions above pass
        // against an erasure that never deletes anything at all.
        (await manager.DeleteSessionAsync(TenantA, sessionId)).BlobsDeleted.Should().Be(1);
        _fx.DurableBlobs.Count.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 5. The disabled store — today's state in every deployment.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheDurableStoreIsDisabled_TheDeleteStillProceeds_AndReportsStoreDisabledNotErased()
    {
        // SessionFileStore:BlobEndpoint is empty everywhere until the operator arms it. A delete must
        // still work — but it must not CLAIM the durable bytes were erased, because it did not look.
        var sessionId = NewSessionId();
        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();
        var dataverse = new Mock<IChatDataverseRepository>();

        var manager = new ChatSessionManager(
            cache: cache,
            dataverseRepository: dataverse.Object,
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null,
            durableFileStore: new SessionFileBlobStore(gateway: null, NullLogger<SessionFileBlobStore>.Instance));

        var erasure = await manager.DeleteSessionAsync(TenantA, sessionId);

        erasure.State.Should().Be(SessionFileErasureState.StoreDisabled,
            "'the store is not configured' and 'the bytes are confirmed gone' are different answers, " +
            "and collapsing them would be a success claim about a store that was never consulted");
        erasure.BytesConfirmedGone.Should().BeFalse();
        dataverse.Verify(
            r => r.ArchiveSessionAsync(TenantA, sessionId, It.IsAny<CancellationToken>()), Times.Once,
            "an unarmed durable store must never block session deletion — that would break every " +
            "deployment as it stands today");
    }

    [Fact]
    public async Task WhenTheDurableStoreIsDisabled_TheFourHourCacheCopiesAreStillEvictedViaTheManifest()
    {
        // The path that carries every deployment today: no durable blob exists, so the blob prefix
        // names nothing and the session manifest is the ONLY source of the file ids whose hot copies
        // must go. Neither id source is a superset of the other, which is why both are used.
        var sessionId = NewSessionId();
        const string FileId = "manifest0000000000000000000000ab";

        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();
        var session = BuildSession(sessionId) with
        {
            UploadedFiles =
            [
                new ChatSessionFile(
                    FileId: FileId,
                    FileName: "legacy.pdf",
                    ContentType: "application/pdf",
                    SizeBytes: 12,
                    SearchDocumentIdsCsv: $"{FileId}_s_0",
                    UploadedAt: DateTimeOffset.UtcNow)
            ]
        };

        await cache.SetAsync(
            TenantA, ChatSessionManager.CacheResource, sessionId, ChatSessionManager.CacheVersion, session);
        await cache.SetAsync(
            TenantA, SessionUploadCacheKeys.BinaryResource,
            SessionUploadCacheKeys.CacheId(sessionId, FileId), SessionUploadCacheKeys.Version,
            Encoding.UTF8.GetBytes("original bytes"));

        var manager = new ChatSessionManager(
            cache: cache,
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null,
            durableFileStore: new SessionFileBlobStore(gateway: null, NullLogger<SessionFileBlobStore>.Instance));

        (await cache.GetAsync<byte[]>(
                TenantA, SessionUploadCacheKeys.BinaryResource,
                SessionUploadCacheKeys.CacheId(sessionId, FileId), SessionUploadCacheKeys.Version))
            .Should().NotBeNull("positive control: the hot copy must exist to be evicted");

        await manager.DeleteSessionAsync(TenantA, sessionId);

        (await cache.GetAsync<byte[]>(
                TenantA, SessionUploadCacheKeys.BinaryResource,
                SessionUploadCacheKeys.CacheId(sessionId, FileId), SessionUploadCacheKeys.Version))
            .Should().BeNull(
                "with no durable copy to enumerate, the manifest is the only thing that names this " +
                "file — dropping that source would leave the bytes in Redis for four hours after a " +
                "deletion in every deployment that has not armed the durable store");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A fresh, blob-name-safe session id per test — the fixture's cache is not reset between them.</summary>
    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// A PRODUCTION <see cref="ChatSessionManager"/> over the same cache and the same durable store the
    /// wire upload just wrote to. Dataverse and the cleanup signal are substituted at their own
    /// boundaries; nothing on the erasure path is.
    /// </summary>
    private ChatSessionManager BuildManager(
        IChatDataverseRepository? dataverse = null,
        ISessionFilesCleanupSignal? cleanupSignal = null,
        bool seedSessionInCache = false,
        string? sessionId = null)
    {
        if (seedSessionInCache && sessionId is not null)
        {
            _fx.Cache.SetAsync(
                TenantA, ChatSessionManager.CacheResource, sessionId, ChatSessionManager.CacheVersion,
                BuildSession(sessionId)).GetAwaiter().GetResult();
        }

        return new ChatSessionManager(
            cache: _fx.Cache,
            dataverseRepository: dataverse ?? Mock.Of<IChatDataverseRepository>(),
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: cleanupSignal,
            durableFileStore: _fx.DurableFileStore);
    }

    private static DefaultHttpContext BuildHttpContext(string tenantId)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("tid", tenantId)], "test"))
        };

        return context;
    }

    private async Task<HttpResponseMessage> UploadAsync(string sessionId, string filename, string content)
    {
        var client = _fx.CreateAuthenticatedClient();
        using var form = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(byteContent, "file", filename);

        return await client.PostAsync($"/api/ai/chat/sessions/{sessionId}/documents", form);
    }

    private Task<byte[]?> ReadCachedBinaryAsync(string sessionId, string fileId)
        => _fx.Cache.GetAsync<byte[]>(
            TenantA, SessionUploadCacheKeys.BinaryResource,
            SessionUploadCacheKeys.CacheId(sessionId, fileId), SessionUploadCacheKeys.Version);

    private Task<string?> ReadCachedTextAsync(string sessionId, string fileId)
        => _fx.Cache.GetAsync<string>(
            TenantA, SessionUploadCacheKeys.TextResource,
            SessionUploadCacheKeys.CacheId(sessionId, fileId), SessionUploadCacheKeys.Version);

    private static ChatSession BuildSession(string sessionId, string tenantId = TenantA)
        => new(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: []) { OwnerOid = TestSessionOwner.Oid };

    /// <summary>Captures cleanup signals so a refused delete can be shown NOT to have fired one.</summary>
    private sealed class RecordingCleanupSignal : ISessionFilesCleanupSignal
    {
        public List<(string TenantId, string SessionId)> Signals { get; } = [];

        public void SignalSessionEnded(string tenantId, string sessionId)
            => Signals.Add((tenantId, sessionId));
    }
}
