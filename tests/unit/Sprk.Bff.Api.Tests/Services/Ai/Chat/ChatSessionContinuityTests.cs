using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Session continuity tests — verification of FR-56 binding invariant:
/// <c>ChatSession.UploadedFiles[]</c> MUST be retained across multi-turn chat conversations
/// without per-turn drop.
///
/// Architecture binding (stateful-chat-architecture.md §6.1):
///   "<c>ChatSession.UploadedFiles[]</c> is the binding storage for in-session attachments;
///    FR-56 makes the no-eviction-during-session-TTL guarantee binding."
///
/// Architecture binding (stateful-chat-architecture.md §11.2):
///   "<c>StoredSession</c> shape extended: Add <c>UploadedFiles[N].SummaryText</c>,
///    <c>.ClassifiedDocType</c>, <c>.Sections</c>, <c>.TableMetadata</c>, <c>.Citations</c>."
///
/// Verifies (chat-routing-redesign-r1 task 118a):
///   1. <see cref="ChatHistoryManager.AddMessageAsync"/> preserves <c>UploadedFiles</c>
///      across turn-completion writes (the dominant happy-path lifecycle).
///   2. The <c>ChatSession</c> JSON roundtrip used by Redis persistence preserves all 14
///      <see cref="ChatSessionFile"/> fields (6 R5 + 8 enrichment).
///   3. Multi-file sessions (2 files) preserve every file's enrichment across 5+ turns.
///   4. <see cref="ChatSessionManager.GetSessionAsync"/> on a Redis HIT returns the
///      cached <c>ChatSession</c> with <c>UploadedFiles</c> intact every turn.
///   5. Cold-recovery edge case (Cosmos fallback) is documented — <c>ChatSessionManager</c>
///      maps <c>StoredSession</c> → <c>ChatSession</c> with <c>UploadedFiles = empty</c>
///      (see <c>MapStoredSessionToChatSession</c>) — flagged as a P2 cold-recovery gap.
///      The happy-path (Redis warm; the dominant pattern within the 24h sliding TTL window)
///      preserves <c>UploadedFiles</c> correctly per architecture §6.1.
///
/// Per-turn drop SCOPE: this test exercises the IN-SESSION continuity path that matters for
/// the user's experience — files uploaded once, then preserved across 5+ chat turns while
/// the session is hot in Redis. This is the path FR-56 binds.
///
/// Pivot from POML: per POML Step 6, falls back to a unit-test approach when integration
/// test infrastructure is not pre-staged in <c>tests/integration/Sprk.Bff.Api.IntegrationTests/Ai/Chat/</c>
/// (no fixture or test class exists for chat there as of task 118a). Tests target the SAME
/// invariant (no per-turn drop) at the SAVE → LOAD roundtrip layer + per-turn add-message
/// path. The continuity rigor matches integration-equivalent assertions without the test
/// infrastructure overhead.
/// </summary>
public class ChatSessionContinuityTests
{
    private const string TenantId = "tenant-118a";
    private const string SessionId = "session-118a";
    private static readonly Guid PlaybookId = Guid.Parse("11118888-1111-1111-1111-111111111111");

    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly Mock<ILogger<ChatSessionManager>> _sessionManagerLoggerMock;
    private readonly Mock<ILogger<ChatHistoryManager>> _historyLoggerMock;
    private readonly ChatSessionManager _sessionManager;
    private readonly ChatHistoryManager _historyManager;

    public ChatSessionContinuityTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _repoMock = new Mock<IChatDataverseRepository>();
        _sessionManagerLoggerMock = new Mock<ILogger<ChatSessionManager>>();
        _historyLoggerMock = new Mock<ILogger<ChatHistoryManager>>();

        _sessionManager = new ChatSessionManager(
            _cacheMock.Object,
            _repoMock.Object,
            _sessionManagerLoggerMock.Object);

        _historyManager = new ChatHistoryManager(
            _sessionManager,
            _repoMock.Object,
            _historyLoggerMock.Object);
    }

    // =========================================================================
    // Test 1 (FR-56 happy path): 5+ turn conversation retains uploaded files
    //
    // Architecture §6.1: "<c>SessionPersistenceService.UpdateUploadedFilesAsync</c>
    // persists enriched <c>ChatSessionFile</c> to Redis hot + Cosmos warm."
    // §11.2: "Persistence: rides the existing triple-tier flow."
    // FR-56: no implicit eviction during session TTL.
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_Across5Turns_PreservesUploadedFiles_SingleFile()
    {
        // Arrange — session has 1 enriched uploaded file
        var initialFile = BuildEnrichedFile("f-001", "nda.pdf", classifiedDocType: "NDA");
        var session = BuildSessionWithFiles(new[] { initialFile });

        SetupCacheSetSuccess();
        _repoMock
            .Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ChatSession current = session;

        // Act — simulate 5 user turns (user message + assistant response each)
        for (int turn = 1; turn <= 5; turn++)
        {
            var userMessage = new ChatMessage(
                MessageId: $"msg-user-{turn}",
                SessionId: SessionId,
                Role: ChatMessageRole.User,
                Content: $"turn {turn}: what document is attached?",
                TokenCount: 12,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: (turn - 1) * 2);
            current = await _historyManager.AddMessageAsync(current, userMessage);

            var assistantMessage = new ChatMessage(
                MessageId: $"msg-asst-{turn}",
                SessionId: SessionId,
                Role: ChatMessageRole.Assistant,
                Content: $"assistant response {turn}",
                TokenCount: 10,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: (turn - 1) * 2 + 1);
            current = await _historyManager.AddMessageAsync(current, assistantMessage);

            // ASSERT after EVERY turn — FR-56 invariant
            current.UploadedFiles.Should().NotBeNull(
                $"FR-56: UploadedFiles MUST persist across turn {turn} (no implicit eviction)");
            current.UploadedFiles!.Should().HaveCount(1,
                $"FR-56: per-turn drop forbidden — turn {turn} must still have 1 file");
            current.UploadedFiles![0].FileId.Should().Be("f-001",
                $"FR-56: file ID must be stable across turn {turn}");
            current.UploadedFiles![0].FileName.Should().Be("nda.pdf");

            // Architecture §11.2 / FR-26 enriched fields preserved per turn
            current.UploadedFiles![0].SummaryText.Should().Be(initialFile.SummaryText,
                $"FR-26: SummaryText must not be wiped on turn {turn}");
            current.UploadedFiles![0].ClassifiedDocType.Should().Be("NDA",
                $"FR-26: ClassifiedDocType must not be wiped on turn {turn}");
            current.UploadedFiles![0].ClassifiedConfidence.Should().Be(initialFile.ClassifiedConfidence,
                $"FR-26: ClassifiedConfidence must not be wiped on turn {turn}");
            current.UploadedFiles![0].Sections.Should().BeEquivalentTo(initialFile.Sections,
                $"FR-26: Sections must not be wiped on turn {turn}");
            current.UploadedFiles![0].PageCount.Should().Be(initialFile.PageCount,
                $"FR-26: PageCount must not be wiped on turn {turn}");
            current.UploadedFiles![0].Language.Should().Be("en",
                $"FR-26: Language must not be wiped on turn {turn}");
        }

        // Final assert — after 10 message-additions (5 user + 5 assistant), file is unchanged
        current.Messages.Should().HaveCount(10);
        current.UploadedFiles!.Should().HaveCount(1);
    }

    // =========================================================================
    // Test 2 (FR-56 multi-file): 2 uploaded files preserved across 5 turns
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_Across5Turns_PreservesUploadedFiles_MultipleFiles()
    {
        // Arrange — session has 2 enriched uploaded files
        var fileA = BuildEnrichedFile("f-A", "contract.pdf", classifiedDocType: "contract");
        var fileB = BuildEnrichedFile("f-B", "memo.pdf", classifiedDocType: "memo");
        var session = BuildSessionWithFiles(new[] { fileA, fileB });

        SetupCacheSetSuccess();
        _repoMock
            .Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ChatSession current = session;

        // Act — 5 turns
        for (int turn = 1; turn <= 5; turn++)
        {
            var msg = new ChatMessage(
                MessageId: $"msg-{turn}",
                SessionId: SessionId,
                Role: ChatMessageRole.User,
                Content: $"turn {turn}",
                TokenCount: 5,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: turn - 1);
            current = await _historyManager.AddMessageAsync(current, msg);

            // ASSERT — both files preserved with stable IDs and enrichment
            current.UploadedFiles.Should().NotBeNull();
            current.UploadedFiles!.Should().HaveCount(2,
                $"FR-56 multi-file: both files must persist through turn {turn}");
            current.UploadedFiles![0].FileId.Should().Be("f-A",
                $"file A's ID must be stable + index unchanged on turn {turn}");
            current.UploadedFiles![1].FileId.Should().Be("f-B",
                $"file B's ID must be stable + index unchanged on turn {turn}");
            current.UploadedFiles![0].ClassifiedDocType.Should().Be("contract");
            current.UploadedFiles![1].ClassifiedDocType.Should().Be("memo");
        }
    }

    // =========================================================================
    // Test 3 (FR-56 binding): ChatSession JSON roundtrip preserves enriched fields
    //
    // This is the Redis hot-tier serialization path — every per-turn save serializes the
    // full ChatSession via System.Text.Json. If a field is dropped at serialization, the
    // next GetSessionAsync (Redis HIT) returns a session with that field wiped.
    // =========================================================================

    [Fact]
    public void ChatSession_JsonRoundtrip_PreservesAllEnrichedFields()
    {
        // Arrange — fully enriched ChatSession with one file
        var file = BuildEnrichedFile("f-rt", "policy.pdf", classifiedDocType: "policy");
        var session = BuildSessionWithFiles(new[] { file });

        // Act — JSON serialize + deserialize (mirrors Redis path in ChatSessionManager.CacheSessionAsync)
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        var restored = JsonSerializer.Deserialize<ChatSession>(bytes);

        // Assert — all 14 fields survive roundtrip
        restored.Should().NotBeNull();
        restored!.UploadedFiles.Should().NotBeNull();
        restored.UploadedFiles!.Should().HaveCount(1);
        var rt = restored.UploadedFiles![0];

        // 6 R5 fields
        rt.FileId.Should().Be(file.FileId);
        rt.FileName.Should().Be(file.FileName);
        rt.ContentType.Should().Be(file.ContentType);
        rt.SizeBytes.Should().Be(file.SizeBytes);
        rt.SearchDocumentIdsCsv.Should().Be(file.SearchDocumentIdsCsv);
        rt.UploadedAt.Should().BeCloseTo(file.UploadedAt, TimeSpan.FromSeconds(1));

        // 8 enrichment fields (FR-26)
        rt.SummaryText.Should().Be(file.SummaryText);
        rt.ClassifiedDocType.Should().Be(file.ClassifiedDocType);
        rt.ClassifiedConfidence.Should().Be(file.ClassifiedConfidence);
        rt.PageCount.Should().Be(file.PageCount);
        rt.Language.Should().Be(file.Language);
        rt.Sections.Should().BeEquivalentTo(file.Sections);
        rt.TableMetadata.Should().BeEquivalentTo(file.TableMetadata);
        rt.Citations.Should().BeEquivalentTo(file.Citations);
    }

    // =========================================================================
    // Test 4 (FR-56 binding): Redis HIT path returns UploadedFiles intact
    //
    // Architecture §11.1 (FR-45 wiring): ChatSession.UploadedFiles is the binding T2
    // (session memory) substrate read every turn by the agent factory. The Redis HIT
    // path is the dominant lifecycle within the 24h sliding TTL window (NFR-07).
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_RedisHit_ReturnsUploadedFilesIntact()
    {
        // Arrange — Redis holds a session with 1 enriched file
        var file = BuildEnrichedFile("f-hit", "brief.pdf", classifiedDocType: "brief");
        var cachedSession = BuildSessionWithFiles(new[] { file });
        var cachedBytes = JsonSerializer.SerializeToUtf8Bytes(cachedSession);
        var cacheKey = ChatSessionManager.BuildCacheKey(TenantId, SessionId);

        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act — simulate 5 sequential turns: each turn GetSession is called by the chat endpoint
        for (int turn = 1; turn <= 5; turn++)
        {
            var result = await _sessionManager.GetSessionAsync(TenantId, SessionId);

            // Assert — FR-56 binding: file persists every turn through the Redis HIT path
            result.Should().NotBeNull($"GetSessionAsync must return the session on turn {turn}");
            result!.UploadedFiles.Should().NotBeNull($"turn {turn}: UploadedFiles must not be null");
            result.UploadedFiles!.Should().HaveCount(1,
                $"FR-56 turn {turn}: file count must be stable across Redis HIT loads");
            result.UploadedFiles![0].FileId.Should().Be("f-hit",
                $"FR-56 turn {turn}: FileId must be stable");
            result.UploadedFiles![0].ClassifiedDocType.Should().Be("brief",
                $"FR-26 turn {turn}: enriched ClassifiedDocType must survive Redis roundtrip");
            result.UploadedFiles![0].PageCount.Should().Be(file.PageCount,
                $"FR-26 turn {turn}: PageCount must survive Redis roundtrip");
        }

        // Dataverse must never be consulted on Redis HIT (ADR-009 Redis-first)
        _repoMock.Verify(
            r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Redis HIT path must not consult Dataverse — UploadedFiles continuity rides Redis");
    }

    // =========================================================================
    // Test 5 (FR-56 binding): full add-message cycle preserves files through Redis serialization
    //
    // This is the closest unit-test analog to an integration test: simulate the full
    // chat-turn lifecycle that writes the updated session to Redis (CacheSessionAsync
    // serializes the ChatSession to bytes) and verify the next-turn read deserializes
    // a session with UploadedFiles intact.
    // =========================================================================

    [Fact]
    public async Task FullTurnCycle_AddMessage_ThenGetSession_PreservesUploadedFiles()
    {
        // Arrange — session has 1 file
        var file = BuildEnrichedFile("f-cycle", "agreement.pdf", classifiedDocType: "agreement");
        var session = BuildSessionWithFiles(new[] { file });
        var cacheKey = ChatSessionManager.BuildCacheKey(TenantId, SessionId);

        byte[]? lastWrittenBytes = null;
        _cacheMock
            .Setup(c => c.SetAsync(
                cacheKey,
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, _, _) => lastWrittenBytes = bytes)
            .Returns(Task.CompletedTask);

        // When GetSessionAsync is called, the mock returns whatever was last written
        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => lastWrittenBytes);
        _cacheMock
            .Setup(c => c.RefreshAsync(cacheKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repoMock
            .Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Seed the cache with the initial session (sim: file just uploaded)
        var initialBytes = JsonSerializer.SerializeToUtf8Bytes(session);
        lastWrittenBytes = initialBytes;

        ChatSession current = session;

        // Act — 5-turn cycle: add user message → save (writes to "Redis") → load → verify
        for (int turn = 1; turn <= 5; turn++)
        {
            // Add a user message — this triggers UpdateSessionCacheAsync which serializes
            // the full ChatSession (including UploadedFiles) to Redis
            var msg = new ChatMessage(
                MessageId: $"m-{turn}",
                SessionId: SessionId,
                Role: ChatMessageRole.User,
                Content: $"turn {turn} query",
                TokenCount: 5,
                CreatedAt: DateTimeOffset.UtcNow,
                SequenceNumber: turn - 1);
            current = await _historyManager.AddMessageAsync(current, msg);

            // Simulate the next chat-endpoint hit re-loading the session from "Redis"
            var loaded = await _sessionManager.GetSessionAsync(TenantId, SessionId);

            // ASSERT — FR-56 invariant: the file survives the full SAVE → LOAD cycle
            loaded.Should().NotBeNull($"turn {turn}: GetSession must succeed");
            loaded!.UploadedFiles.Should().NotBeNull(
                $"FR-56 turn {turn}: post-Redis-roundtrip UploadedFiles must not be null");
            loaded.UploadedFiles!.Should().HaveCount(1,
                $"FR-56 turn {turn}: per-turn drop forbidden through Redis serialization");
            loaded.UploadedFiles![0].FileId.Should().Be("f-cycle",
                $"FR-56 turn {turn}: FileId must be stable post-roundtrip");
            loaded.UploadedFiles![0].ClassifiedDocType.Should().Be("agreement",
                $"FR-26 turn {turn}: enriched ClassifiedDocType must survive Redis roundtrip");
            loaded.UploadedFiles![0].SummaryText.Should().NotBeNull(
                $"FR-26 turn {turn}: SummaryText must survive Redis roundtrip");
            loaded.UploadedFiles![0].Sections.Should().HaveCountGreaterThan(0,
                $"FR-26 turn {turn}: Sections must survive Redis roundtrip");
            loaded.UploadedFiles![0].PageCount.Should().Be(file.PageCount,
                $"FR-26 turn {turn}: PageCount must survive Redis roundtrip");

            // Message count grows
            loaded.Messages.Should().HaveCount(turn);
        }
    }

    // =========================================================================
    // Helpers — build enriched ChatSessionFile + ChatSession fixtures
    // =========================================================================

    private static ChatSessionFile BuildEnrichedFile(
        string fileId,
        string fileName,
        string classifiedDocType)
    {
        return new ChatSessionFile(
            FileId: fileId,
            FileName: fileName,
            ContentType: "application/pdf",
            SizeBytes: 4096,
            SearchDocumentIdsCsv: $"sd-{fileId}-1,sd-{fileId}-2",
            UploadedAt: DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            // 8 enriched fields per architecture §6.1 / §11.2
            SummaryText = $"summary for {fileId} (NOT authoritative)",
            ClassifiedDocType = classifiedDocType,
            ClassifiedConfidence = 0.88,
            PageCount = 7,
            Language = "en",
            Sections = new[]
            {
                new SectionInfo("Recitals", 0, 400, 1, 1),
                new SectionInfo("Definitions", 400, 1200, 1, 2)
            },
            TableMetadata = new[]
            {
                new TableInfo("Schedule A", 800, 2)
            },
            Citations = new[]
            {
                new CitationReference($"sd-{fileId}-1", "quoted text", 1)
            }
        };
    }

    private static ChatSession BuildSessionWithFiles(IReadOnlyList<ChatSessionFile> files)
    {
        var now = DateTimeOffset.UtcNow;
        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: "doc-118a",
            PlaybookId: PlaybookId,
            CreatedAt: now.AddMinutes(-5),
            LastActivity: now,
            Messages: new List<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }

    private void SetupCacheSetSuccess()
    {
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
