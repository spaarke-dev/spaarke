// FR-33 compaction over ledger + cross-version persistence (task 062, design.md §8). Scope (per
// POML §acceptance-criteria):
//   1. Compose session state is keyed by DocumentId + MatterId, independent of DOCX version.
//   2. A DOCX version change (Word save) does not reset or fork the session.
//   3. Long sessions render from the compacted digest over ledger outputs; digest keys survive
//      verbatim — proved against the EXISTING generalized ChatHistoryManager digest (ADR-040 /
//      redesign-r1 task 002), which Compose CONSUMES without inventing a parallel path.
//   4. Workspace-scope memory items are unaffected by conversation-window compaction (ADR-015) —
//      proved structurally: the compaction surface never references a Memory Service type.
//   5. Reopening after a Word round-trip restores prior decisions (task 061 ledger query) +
//      annotations (task 060) into the resumed LoadAsync view.
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries"): ChatSessionManager (virtual
// GetSessionAsync/UpdateSessionCacheAsync) and IChatDataverseRepository are the genuine external
// boundaries mocked here — same pattern as AnchoredAnnotationPersistenceTests /
// ActionHistoryLedgerQueryTests. The ChatSessionManager mock is backed by an in-memory dictionary
// so LoadAsync resume-vs-mint tests are genuine behavior round-trips, not interaction-only
// Verify() assertions (tests/CLAUDE.md B7).
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler> (B1), no
// DI-registration test (B3), no ctor null-check test (B4). Each test names a concrete production
// behavior that breaks if the test is deleted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class CrossVersionSessionPersistenceTests
{
    private const string Tenant = "tenant-aad-062";
    private const string DocumentSpeId = "spe-item-062";
    private const string DriveId = "drive-062";
    private const string MatterId = "11111111-1111-1111-1111-111111111111";
    private const string OtherMatterId = "22222222-2222-2222-2222-222222222222";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ITenantCache> _cache = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions;

    // In-memory fake session store keyed by sessionId — makes GetSessionAsync/
    // UpdateSessionCacheAsync a genuine round trip rather than an interaction-only mock.
    private readonly Dictionary<string, ChatSession> _store = new(StringComparer.Ordinal);

    public CrossVersionSessionPersistenceTests()
    {
        // NOTE: ChatSessionManager.CreateSessionAsync is NOT virtual (unlike GetSessionAsync /
        // UpdateSessionCacheAsync), so it cannot be Setup()'d on the class mock — calling it on
        // _sessions.Object runs the REAL method body. That body's own dependencies (_cache,
        // the Dataverse repository) are the loose/captured doubles below, so the real
        // CreateSessionAsync executes safely; the _cache capture below mirrors the created
        // session into the SAME _store dictionary GetSessionAsync reads from, keeping the fake
        // session store consistent regardless of which path created the session.
        _cache
            .Setup(c => c.SetSlidingAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (_, _, _, _, session, _, _, _) => _store[session.SessionId] = session)
            .Returns(Task.CompletedTask);

        _sessions = new Mock<ChatSessionManager>(
            _cache.Object,
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string sessionId, CancellationToken _) =>
                _store.TryGetValue(sessionId, out var session) && session.TenantId == tenantId ? session : null);

        _sessions
            .Setup(s => s.UpdateSessionCacheAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns((ChatSession session, CancellationToken _) =>
            {
                _store[session.SessionId] = session;
                return Task.CompletedTask;
            });
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private ChatSession SeedSession(
        string sessionId,
        string documentId,
        ChatHostContext? hostContext = null,
        IReadOnlyList<AnchoredAnnotation>? annotations = null,
        IReadOnlyList<SessionOutput>? outputs = null,
        string tenantId = Tenant)
    {
        var session = new ChatSession(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: documentId,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: hostContext)
        {
            AnchoredAnnotations = annotations,
            Outputs = outputs,
        };
        _store[sessionId] = session;
        return session;
    }

    private static AnchoredAnnotation NewAnnotation(string id) => new()
    {
        Id = id,
        Type = "comment",
        Anchor = new AnchoredAnnotationAnchor { TextPattern = "the Effective Date", ParagraphHint = 3, SpanId = "span-1" },
        Body = "Confirm this date with the client.",
        Author = "ai",
        Timestamp = DateTimeOffset.UtcNow,
        Source = "ai",
    };

    private static SessionOutput NewOutput(string bindingId, int turn, string disposition = "compose") => new()
    {
        Key = SessionLedger.BuildOutputKey(bindingId, turn),
        BindingId = bindingId,
        UcId = "compose-explain-clause",
        Turn = turn,
        Disposition = disposition,
        Payload = JsonSerializer.SerializeToElement(new { note = $"decision-{turn}" }),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private void ArrangeSpeLoad(string etag = "\"etag-v1\"")
    {
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: DocumentSpeId,
                Name: "contract.docx",
                ParentId: null,
                Size: 4,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: etag,
                IsFolder: false,
                WebUrl: "https://spe/web",
                DriveId: DriveId));

        _spe.Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));
        // FR-06 (task 027): LoadAsync resolves the load-time version id best-effort.
        _spe.Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-1");
    }

    // ── Acceptance #2: a DOCX version change (new SPE ETag — simulating a Word save producing a
    //    new version) does NOT reset or fork the session; the SAME SessionId + annotations persist. ─
    [Fact]
    public async Task LoadAsync_AfterDocxVersionChange_ResumesSameSessionWithoutResettingState()
    {
        var priorSessionId = Guid.NewGuid().ToString();
        SeedSession(priorSessionId, documentId: DocumentSpeId, annotations: new[] { NewAnnotation("anno-1") });
        var sut = CreateSut();

        // Word save #1 — client re-opens with the SessionId; SPE reports one ETag/version.
        ArrangeSpeLoad(etag: "\"etag-v1\"");
        var firstReload = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
        }, new DefaultHttpContext(), CancellationToken.None);

        // Word save #2 — a NEW DOCX version (different ETag) is now on SPE. The document identity
        // (DocumentSpeId) is unchanged, which is exactly what makes the binding version-independent.
        _spe.Invocations.Clear();
        ArrangeSpeLoad(etag: "\"etag-v2-after-word-save\"");
        var secondReload = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
        }, new DefaultHttpContext(), CancellationToken.None);

        firstReload.SessionId.Should().Be(priorSessionId);
        secondReload.SessionId.Should().Be(priorSessionId,
            "a DOCX version change (new SPE ETag from a Word save) must NOT reset or fork the session");
        secondReload.ETag.Should().Be("\"etag-v2-after-word-save\"", "the new version's bytes/ETag load normally");
        secondReload.AnchoredAnnotations.Should().ContainSingle().Which.Id.Should().Be("anno-1",
            "prior annotations must survive the version change, not just the SessionId");
    }

    // ── Acceptance #1: keyed by DocumentId + MatterId — matching Matter resumes the session ──────
    [Fact]
    public async Task LoadAsync_WithMatterIdMatchingSessionsHostContext_ResumesSessionAcrossVersionChange()
    {
        var priorSessionId = Guid.NewGuid().ToString();
        SeedSession(
            priorSessionId,
            documentId: DocumentSpeId,
            hostContext: new ChatHostContext(EntityType: "matter", EntityId: MatterId),
            annotations: new[] { NewAnnotation("anno-matter-bound") });
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
            MatterId = MatterId,
        }, new DefaultHttpContext(), CancellationToken.None);

        result.SessionId.Should().Be(priorSessionId,
            "the caller-supplied MatterId matches the session's bound Matter — the DocumentId+MatterId key resolves to the SAME session");
        result.AnchoredAnnotations.Should().ContainSingle().Which.Id.Should().Be("anno-matter-bound");
    }

    // ── Negative: a session bound to the SAME document but a DIFFERENT Matter must not be reused —
    //    guards the "+MatterId" half of the key, not just DocumentId. ────────────────────────────
    [Fact]
    public async Task LoadAsync_WithMatterIdMismatchAgainstSessionsHostContext_MintsFreshSessionInstead()
    {
        var otherMatterSessionId = Guid.NewGuid().ToString();
        SeedSession(
            otherMatterSessionId,
            documentId: DocumentSpeId,
            hostContext: new ChatHostContext(EntityType: "matter", EntityId: OtherMatterId),
            annotations: new[] { NewAnnotation("anno-belongs-to-other-matter") });
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId, // SAME document as the seeded session
            TenantId = Tenant,
            SessionId = otherMatterSessionId,
            MatterId = MatterId, // DIFFERENT matter than the seeded session is bound to
        }, new DefaultHttpContext(), CancellationToken.None);

        result.SessionId.Should().NotBe(otherMatterSessionId,
            "the same DocumentId but a DIFFERENT MatterId must not be treated as the same cross-version binding");
        result.AnchoredAnnotations.Should().BeEmpty("the other matter's annotations must not leak onto this load");
    }

    // ── Backward compatibility: no MatterId supplied preserves the FR-29 DocumentId-only match,
    //    even when the candidate session already carries a Matter HostContext. ──────────────────
    [Fact]
    public async Task LoadAsync_WithoutMatterIdOnRequest_StillResumesOnDocumentIdAloneForBackwardCompatibility()
    {
        var priorSessionId = Guid.NewGuid().ToString();
        SeedSession(
            priorSessionId,
            documentId: DocumentSpeId,
            hostContext: new ChatHostContext(EntityType: "matter", EntityId: MatterId),
            annotations: new[] { NewAnnotation("anno-1") });
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
            MatterId = null, // R1/FR-29 caller — predates FR-33
        }, new DefaultHttpContext(), CancellationToken.None);

        result.SessionId.Should().Be(priorSessionId,
            "a null MatterId must preserve the existing FR-29 DocumentId-only resume match");
    }

    // ── Acceptance #1: a freshly-minted session seeds its HostContext from MatterId so the NEXT
    //    Load (post-Word-round-trip) can resume via the same key. ──────────────────────────────
    [Fact]
    public async Task LoadAsync_WithMatterIdAndNoPriorSession_SeedsSessionHostContextForFutureResume()
    {
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            MatterId = MatterId,
        }, new DefaultHttpContext(), CancellationToken.None);

        _store.Should().ContainKey(result.SessionId);
        var created = _store[result.SessionId];
        created.HostContext.Should().NotBeNull();
        created.HostContext!.EntityType.Should().Be("matter");
        created.HostContext.EntityId.Should().Be(MatterId);
    }

    // ── Acceptance #5: reopening restores prior DECISIONS (task 061 ledger query) alongside
    //    annotations (task 060) into the resumed LoadAsync view. ───────────────────────────────
    [Fact]
    public async Task LoadAsync_ResumingSessionWithLedgerOutputs_RestoresActionHistoryIntoTheView()
    {
        var priorSessionId = Guid.NewGuid().ToString();
        SeedSession(
            priorSessionId,
            documentId: DocumentSpeId,
            outputs: new[] { NewOutput("compose-explain-clause", turn: 1) });
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
        }, new DefaultHttpContext(), CancellationToken.None);

        result.ActionHistory.Should().ContainSingle();
        var entry = result.ActionHistory[0];
        entry.OutputRef.Should().Be("compose-explain-clause@t1");
        entry.BindingId.Should().Be("compose-explain-clause");
        entry.Disposition.Should().Be("compose");
        entry.IsSuperseded.Should().BeFalse();
    }

    // ── Negative: a freshly-minted session (no prior ledger) restores an empty action history —
    //    never null, never throws. ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task LoadAsync_WithFreshSession_RestoresEmptyActionHistory()
    {
        var sut = CreateSut();
        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
        }, new DefaultHttpContext(), CancellationToken.None);

        result.ActionHistory.Should().NotBeNull().And.BeEmpty();
    }

    // ── Acceptance #3: long sessions render from the COMPACTED DIGEST over ledger outputs, and
    //    digest keys survive VERBATIM — proved against the EXISTING generalized
    //    ChatHistoryManager mechanism (ADR-040 / redesign-r1 task 002). Compose does not invent a
    //    parallel compaction path; a "compose"-disposition output rides the SAME generic digest as
    //    every other disposition, with zero Compose-specific code in ChatHistoryManager. ─────────
    [Fact]
    public async Task ChatHistoryManager_SummarisationOverComposeDispositionOutput_EmbedsLedgerKeyVerbatim()
    {
        var dataverseRepo = new Mock<IChatDataverseRepository>(MockBehavior.Strict);
        string? capturedSummary = null;
        dataverseRepo
            .Setup(r => r.UpdateSessionSummaryAsync(Tenant, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string summary, CancellationToken _) =>
            {
                capturedSummary = summary;
                return Task.CompletedTask;
            });

        var manager = new ChatHistoryManager(_sessions.Object, dataverseRepo.Object, NullLogger<ChatHistoryManager>.Instance);

        var ledgerKey = SessionLedger.BuildOutputKey("compose-explain-clause", turn: 3);
        var session = new ChatSession(
            SessionId: "session-digest-1",
            TenantId: Tenant,
            DocumentId: DocumentSpeId,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>())
        {
            Outputs = new[]
            {
                new SessionOutput
                {
                    Key = ledgerKey,
                    BindingId = "compose-explain-clause",
                    UcId = "compose-explain-clause",
                    Turn = 3,
                    Disposition = "compose",
                    Payload = JsonSerializer.SerializeToElement(new { summary = "Explains the indemnification clause." }),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            },
        };

        await manager.TriggerSummarisationAsync(session, CancellationToken.None);

        capturedSummary.Should().NotBeNull();
        capturedSummary.Should().Contain(ledgerKey,
            "the {bindingId}@t{n} ledger ref must survive the compaction digest VERBATIM (ADR-040) so a compose action remains addressable post-compaction");
        capturedSummary.Should().Contain("compose",
            "the compose disposition rides the SAME generic digest section as every other disposition — no Compose-specific compaction path exists");
    }

    // ── Acceptance #4: workspace-scope memory items are unaffected by conversation-window
    //    compaction (ADR-015) — proved structurally: the compaction surface (ChatHistoryManager)
    //    has no dependency on any Memory Service type, so nothing it does can touch memory. ──────
    [Fact]
    public void ChatHistoryManager_HasNoMemoryServiceDependency_SoCompactionCannotTouchWorkspaceScopeMemory()
    {
        var compactionSurfaceTypes = new[] { typeof(ChatHistoryManager) };

        foreach (var type in compactionSurfaceTypes)
        {
            var ctor = type.GetConstructors().Single();
            var parameterTypeNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();

            parameterTypeNames.Should().NotContain(n => n.Contains("MemoryComposition", StringComparison.OrdinalIgnoreCase)
                || n.Contains("PinnedContext", StringComparison.OrdinalIgnoreCase)
                || n.Contains("MemoryItem", StringComparison.OrdinalIgnoreCase),
                $"{type.Name} is the conversation-window compaction surface — it must have zero dependency on the Memory Service " +
                "(ADR-015 workspace-scope memory items live outside the conversation window and are unaffected by compaction)");
        }
    }
}
