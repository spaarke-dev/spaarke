using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ChatHistoryManager"/>.
///
/// Verifies:
/// - <see cref="ChatHistoryManager.AddMessageAsync"/> persists to Dataverse and refreshes Redis.
/// - Summarisation triggers when message count &gt;= 15 (<see cref="ChatHistoryManager.SummarisationThreshold"/>).
/// - Archive triggers when message count &gt;= 50 (<see cref="ChatHistoryManager.ArchiveThreshold"/>).
/// - <see cref="ChatHistoryManager.GetHistoryAsync"/> returns from the Redis hot path.
/// - Ledger-output digest coverage (ADR-040 / FR-P0-02): compaction digests include
///   per-output summaries with preserved <c>{bindingId}@t{n}</c> keys, on both the
///   summarisation and archive paths; sessions without outputs keep the pre-ledger
///   digest shape unchanged.
/// - Confirmed-vs-fire-and-forget Cosmos write selection (FR-D2, task 030): messages[0]
///   requests the confirmed write; every later turn keeps the fire-and-forget contract.
///   The deeper end-to-end proof (genuine await + eviction survival) lives in
///   <c>tests/integration/regression/Ai/FirstTurnCosmosWriteSurvivesEvictionTests.cs</c>.
/// </summary>
public class ChatHistoryManagerTests
{
    private const string TenantId = "tenant-hist";
    private const string SessionId = "session-hist";
    private const string DocumentId = "doc-hist";
    private static readonly Guid PlaybookId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

    // =========================================================================
    // Fake session manager for test isolation
    // =========================================================================

    /// <summary>
    /// Test double for ChatSessionManager — avoids sealing issues with Moq.
    /// Stores a pre-configured session returned by GetSessionAsync, and records
    /// calls to UpdateSessionCacheAsync.
    /// </summary>
    private sealed class FakeChatSessionManager : ChatSessionManager
    {
        private ChatSession? _storedSession;
        public ChatSession? LastCachedSession { get; private set; }

        /// <summary>
        /// FR-D2 (task 030): records the <c>awaitCosmosWrite</c> flag the caller passed on the
        /// most recent <see cref="UpdateSessionCacheAsync"/> invocation, so tests can assert
        /// <see cref="ChatHistoryManager.AddMessageAsync"/> requests a confirmed write for
        /// messages[0] and a fire-and-forget write for every later turn.
        /// </summary>
        public bool LastAwaitCosmosWrite { get; private set; }

        public FakeChatSessionManager(
            ITenantCache cache,
            IChatDataverseRepository repo,
            ILogger<ChatSessionManager> logger)
            : base(cache, repo, logger)
        {
        }

        public void SetSession(ChatSession session) => _storedSession = session;

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(_storedSession);

        internal override Task UpdateSessionCacheAsync(
            ChatSession session,
            CancellationToken ct = default,
            bool awaitCosmosWrite = false)
        {
            LastCachedSession = session;
            LastAwaitCosmosWrite = awaitCosmosWrite;
            return Task.CompletedTask;
        }
    }

    // =========================================================================
    // Test setup
    // =========================================================================

    private readonly FakeChatSessionManager _fakeSessionManager;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly ChatHistoryManager _sut;

    public ChatHistoryManagerTests()
    {
        var sessionRepoMock = new Mock<IChatDataverseRepository>();
        var sessionLoggerMock = new Mock<ILogger<ChatSessionManager>>();

        _fakeSessionManager = new FakeChatSessionManager(
            new InMemoryTenantCache(),
            sessionRepoMock.Object,
            sessionLoggerMock.Object);

        _repoMock = new Mock<IChatDataverseRepository>();
        var histLoggerMock = new Mock<ILogger<ChatHistoryManager>>();

        _sut = new ChatHistoryManager(
            _fakeSessionManager,
            _repoMock.Object,
            histLoggerMock.Object);
    }

    // =========================================================================
    // FR-D4 (task 032) — asymmetric-registration regression guard
    // =========================================================================
    //
    // bff-extensions.md §F.1 / §F.1-runtime: ChatHistoryManager is registered UNCONDITIONALLY
    // (AnalysisServicesModule.AddUnconditionalChatAndNotificationServices, plain
    // `services.AddScoped<ChatHistoryManager>();` — no factory), but IOpenAiClient is registered
    // CONDITIONALLY (only when the DocumentIntelligence compound gate is ON —
    // AnalysisServicesModule.cs:137). If the new title-gen ctor param made IOpenAiClient a
    // REQUIRED dependency, resolving ChatHistoryManager via the real DI container with the AI
    // gate OFF would throw at first use — breaking message-adding entirely, not just title-gen.
    // This test proves the container-level (not just C#-default-param-level) resolution survives
    // an unregistered IOpenAiClient, mirroring the runtime-verifiable pattern bff-extensions.md
    // §F.1-runtime recommends for exactly this failure class.

    [Fact]
    public void ChatHistoryManager_ResolvesViaRealDiContainer_WhenIOpenAiClientIsUnregistered()
    {
        // Arrange — a minimal real ServiceCollection wired the same way
        // AddUnconditionalChatAndNotificationServices registers these types (plain AddScoped<T>,
        // no factory lambda) — deliberately WITHOUT registering IOpenAiClient, simulating the
        // DocumentIntelligence compound gate OFF.
        var services = new ServiceCollection();
        services.AddSingleton<ITenantCache>(new InMemoryTenantCache());
        services.AddSingleton(new Mock<IChatDataverseRepository>().Object);
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
        services.AddScoped<ChatSessionManager>();
        services.AddScoped<ChatHistoryManager>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var act = () => scope.ServiceProvider.GetRequiredService<ChatHistoryManager>();

        // Assert
        act.Should().NotThrow(
            "ChatHistoryManager is registered unconditionally (§F.1) — an unregistered " +
            "IOpenAiClient (AI gate OFF) must resolve the optional ctor param to null, not throw");
    }

    // =========================================================================
    // AddMessageAsync — basic persistence and session update
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_PersistsMessageToDataverse()
    {
        // Arrange
        var session = CreateTestSession(messageCount: 0);
        var message = CreateTestMessage(session.SessionId, 0);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, message);

        // Assert
        _repoMock.Verify(r => r.AddMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_ReturnsUpdatedSession_WithNewMessageAppended()
    {
        // Arrange
        var session = CreateTestSession(messageCount: 2);
        var newMessage = CreateTestMessage(session.SessionId, 2);
        SetupRepoDefaults();

        // Act
        var updatedSession = await _sut.AddMessageAsync(session, newMessage);

        // Assert
        updatedSession.Messages.Should().HaveCount(3);
        updatedSession.Messages.Last().Should().Be(newMessage);
    }

    [Fact]
    public async Task AddMessageAsync_UpdatesLastActivity()
    {
        // Arrange
        var oldActivity = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = CreateTestSession(messageCount: 0) with { LastActivity = oldActivity };
        var message = CreateTestMessage(session.SessionId, 0);
        SetupRepoDefaults();

        // Act
        var updatedSession = await _sut.AddMessageAsync(session, message);

        // Assert — LastActivity should be updated to close to UtcNow
        updatedSession.LastActivity.Should().BeAfter(oldActivity);
        updatedSession.LastActivity.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AddMessageAsync_RefreshesRedisCache()
    {
        // Arrange
        var session = CreateTestSession(messageCount: 0);
        var message = CreateTestMessage(session.SessionId, 0);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, message);

        // Assert — cache was updated with the new message via UpdateSessionCacheAsync
        _fakeSessionManager.LastCachedSession.Should().NotBeNull();
        _fakeSessionManager.LastCachedSession!.Messages.Should().HaveCount(1);
    }

    // =========================================================================
    // Confirmed vs fire-and-forget Cosmos write (FR-D2, task 030)
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_FirstMessageOfSession_RequestsConfirmedCosmosWrite()
    {
        // Arrange — a brand-new session (no messages yet); this message becomes messages[0].
        var session = CreateTestSession(messageCount: 0);
        var message = CreateTestMessage(session.SessionId, 0);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, message);

        // Assert — messages[0] must request the CONFIRMED (awaited) Cosmos write so the transcript
        // survives a Redis eviction (spec FR-D2 acceptance; see also the end-to-end regression test
        // at tests/integration/regression/Ai/FirstTurnCosmosWriteSurvivesEvictionTests.cs).
        _fakeSessionManager.LastAwaitCosmosWrite.Should().BeTrue();
    }

    [Fact]
    public async Task AddMessageAsync_LaterMessageOfSession_KeepsFireAndForgetCosmosWrite()
    {
        // Arrange — session already has 3 messages; the new message is a later turn.
        var session = CreateTestSession(messageCount: 3);
        var message = CreateTestMessage(session.SessionId, 3);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, message);

        // Assert — turns after messages[0] must NOT request the confirmed write (NFR-03 — no
        // latency regression on turns 2+; keeps the original D-06 fire-and-forget contract).
        _fakeSessionManager.LastAwaitCosmosWrite.Should().BeFalse();
    }

    // =========================================================================
    // Summarisation trigger at 15 messages
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_TriggersSummarisation_WhenMessageCountReaches15()
    {
        // Arrange — session already has 14 messages; adding one more reaches the threshold
        var session = CreateTestSession(messageCount: 14);
        var fifteenthMessage = CreateTestMessage(session.SessionId, 14);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, fifteenthMessage);

        // Assert — summarisation should be triggered (UpdateSessionSummaryAsync called)
        _repoMock.Verify(r => r.UpdateSessionSummaryAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_DoesNotTriggerSummarisation_WhenMessageCountBelow15()
    {
        // Arrange — session has 13 messages; after adding, count is 14 (below threshold)
        var session = CreateTestSession(messageCount: 13);
        var message = CreateTestMessage(session.SessionId, 13);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, message);

        // Assert — summarisation should NOT be triggered
        _repoMock.Verify(r => r.UpdateSessionSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddMessageAsync_TriggersArchive_WhenMessageCountReaches50()
    {
        // Arrange — session already has 49 messages; adding one more reaches the archive threshold
        var session = CreateTestSession(messageCount: 49);
        var fiftieth = CreateTestMessage(session.SessionId, 49);
        SetupRepoDefaults();

        // Act
        await _sut.AddMessageAsync(session, fiftieth);

        // Assert — archive (UpdateSessionSummaryAsync) was called.
        // At count=50, BOTH summarisation (threshold 15) AND archiving (threshold 50) trigger.
        _repoMock.Verify(r => r.UpdateSessionSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // =========================================================================
    // Threshold constants
    // =========================================================================

    [Fact]
    public void SummarisationThreshold_Is15()
    {
        ChatHistoryManager.SummarisationThreshold.Should().Be(15);
    }

    [Fact]
    public void ArchiveThreshold_Is50()
    {
        ChatHistoryManager.ArchiveThreshold.Should().Be(50);
    }

    // =========================================================================
    // GetHistoryAsync
    // =========================================================================

    [Fact]
    public async Task GetHistoryAsync_ReturnsAllMessages_WhenCountBelowMax()
    {
        // Arrange
        var session = CreateTestSession(messageCount: 5);
        _fakeSessionManager.SetSession(session);

        // Act
        var history = await _sut.GetHistoryAsync(TenantId, SessionId, maxMessages: 50);

        // Assert
        history.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMostRecentN_WhenCountExceedsMax()
    {
        // Arrange — session with 20 messages; request max=10 → should return last 10
        var session = CreateTestSession(messageCount: 20);
        _fakeSessionManager.SetSession(session);

        // Act
        var history = await _sut.GetHistoryAsync(TenantId, SessionId, maxMessages: 10);

        // Assert
        history.Should().HaveCount(10);
        // The last message in the result should be message index 19 (most recent)
        history.Last().SequenceNumber.Should().Be(19);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmptyList_WhenSessionNotFound()
    {
        // Arrange — session manager returns null
        _fakeSessionManager.SetSession(null!);

        // Act
        var history = await _sut.GetHistoryAsync(TenantId, "nonexistent");

        // Assert
        history.Should().BeEmpty();
    }

    // =========================================================================
    // Ledger-output digest (ADR-040 / FR-P0-02)
    // =========================================================================

    [Fact]
    public async Task TriggerSummarisationAsync_WithLedgerOutputs_DigestIncludesOutputKeysDispositionsAndSnippets()
    {
        // Arrange — session at summarisation size with two ledger outputs
        var session = CreateTestSession(messageCount: 15) with
        {
            Outputs = new[]
            {
                CreateTestOutput("summarize-binding", turn: 1, disposition: "work_product",
                    payloadJson: """{"summary":"Key obligations: renewal auto-extends unless cancelled."}"""),
                CreateTestOutput("loop", turn: 3, disposition: "informational",
                    payloadJson: """ "The venue clause favors the counterparty." """)
            }
        };
        var capturedSummary = SetupRepoDefaultsCapturingSummary();

        // Act
        await _sut.TriggerSummarisationAsync(session);

        // Assert — each output line preserves the addressable {bindingId}@t{n} key
        // and carries disposition + uc id + content snippet
        capturedSummary().Should().NotBeNull();
        var digest = capturedSummary()!;
        digest.Should().Contain("summarize-binding@t1");
        digest.Should().Contain("[work_product]");
        digest.Should().Contain("Key obligations: renewal auto-extends unless cancelled.");
        digest.Should().Contain("loop@t3");
        digest.Should().Contain("[informational]");
        digest.Should().Contain("The venue clause favors the counterparty.");
        digest.Should().Contain("uc.test.capability");
    }

    [Fact]
    public async Task TriggerSummarisationAsync_WithoutLedgerOutputs_DigestShapeUnchanged()
    {
        // Arrange — pre-ledger session (Outputs null): additive change must leave
        // the existing digest shape untouched (spec FR-P0-02 acceptance)
        var session = CreateTestSession(messageCount: 15);
        var capturedSummary = SetupRepoDefaultsCapturingSummary();

        // Act
        await _sut.TriggerSummarisationAsync(session);

        // Assert
        var digest = capturedSummary();
        digest.Should().NotBeNull();
        digest.Should().StartWith("[Summary of ");
        digest.Should().NotContain("Ledger outputs");
    }

    [Fact]
    public async Task AddMessageAsync_AtSummarisationThreshold_PersistedDigestIncludesOutputSummaries()
    {
        // Arrange — full public path: 14 messages + one ledger output; the 15th
        // message triggers compaction, and the persisted digest must keep the
        // output addressable
        var session = CreateTestSession(messageCount: 14) with
        {
            Outputs = new[]
            {
                CreateTestOutput("draft-binding", turn: 2, disposition: "email",
                    payloadJson: """{"title":"Draft to opposing counsel"}""")
            }
        };
        var fifteenth = CreateTestMessage(session.SessionId, 14);
        var capturedSummary = SetupRepoDefaultsCapturingSummary();

        // Act
        await _sut.AddMessageAsync(session, fifteenth);

        // Assert
        var digest = capturedSummary();
        digest.Should().NotBeNull();
        digest.Should().Contain("draft-binding@t2");
        digest.Should().Contain("[email]");
        digest.Should().Contain("Draft to opposing counsel");
    }

    [Fact]
    public async Task ArchiveHistoryAsync_WithLedgerOutputs_ArchiveDigestIncludesOutputKeys()
    {
        // Arrange — archive is the second compaction event; outputs must survive it too
        var session = CreateTestSession(messageCount: 50) with
        {
            Outputs = new[]
            {
                CreateTestOutput("summarize-binding", turn: 7, disposition: "work_product",
                    payloadJson: """{"summary":"Archived-session summary output."}""")
            }
        };
        var capturedSummary = SetupRepoDefaultsCapturingSummary();

        // Act
        await _sut.ArchiveHistoryAsync(session);

        // Assert
        var digest = capturedSummary();
        digest.Should().NotBeNull();
        digest.Should().StartWith("[ARCHIVED");
        digest.Should().Contain("summarize-binding@t7");
        digest.Should().Contain("Archived-session summary output.");
    }

    [Fact]
    public async Task TriggerSummarisationAsync_WithOversizedOutputPayload_SnippetIsCappedNotFullPayload()
    {
        // Arrange — the digest summarizes; the ledger entry remains the full payload.
        // A payload far beyond the snippet cap must not be embedded verbatim.
        var longText = new string('x', 5000);
        var session = CreateTestSession(messageCount: 15) with
        {
            Outputs = new[]
            {
                CreateTestOutput("summarize-binding", turn: 1, disposition: "work_product",
                    payloadJson: JsonSerializer.Serialize(new { summary = longText }))
            }
        };
        var capturedSummary = SetupRepoDefaultsCapturingSummary();

        // Act
        await _sut.TriggerSummarisationAsync(session);

        // Assert — key preserved, payload capped
        var digest = capturedSummary();
        digest.Should().NotBeNull();
        digest.Should().Contain("summarize-binding@t1");
        digest.Should().NotContain(longText);
        digest!.Length.Should().BeLessThan(1000);
    }

    // Task 053 (FR-B-04): the live-turn ledger-outputs context tests moved to
    // ContextSliceProducersTests (BuildLedgerOutputsContext moved to
    // ContextSliceProducers.ConversationContextProducer). The compaction-DIGEST tests stay here.

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static ChatSession CreateTestSession(int messageCount)
    {
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => CreateTestMessage(SessionId, i))
            .ToList()
            .AsReadOnly();

        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: DocumentId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: messages) { OwnerOid = TestSessionOwner.Oid };
    }

    private static ChatMessage CreateTestMessage(string sessionId, int sequenceNumber)
        => new ChatMessage(
            MessageId: $"MSG-{sequenceNumber:D6}",
            SessionId: sessionId,
            Role: ChatMessageRole.User,
            Content: $"Test message {sequenceNumber}",
            TokenCount: 10,
            CreatedAt: DateTimeOffset.UtcNow,
            SequenceNumber: sequenceNumber);

    /// <summary>
    /// Creates a <see cref="SessionOutput"/> with the canonical <c>{bindingId}@t{n}</c>
    /// key built via <see cref="SessionLedger.BuildOutputKey"/> (ADR-040).
    /// </summary>
    private static SessionOutput CreateTestOutput(
        string bindingId, int turn, string disposition, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            UcId = "uc.test.capability",
            Turn = turn,
            Disposition = disposition,
            Payload = doc.RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Standard repo defaults + captures the digest text passed to
    /// <c>UpdateSessionSummaryAsync</c>. Returns an accessor for the captured value
    /// (accessor, not raw string, so the assertion reads the post-Act state).
    /// </summary>
    private Func<string?> SetupRepoDefaultsCapturingSummary()
    {
        string? captured = null;
        SetupRepoDefaults();
        _repoMock.Setup(r => r.UpdateSessionSummaryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, summary, _) => captured = summary)
            .Returns(Task.CompletedTask);
        return () => captured;
    }

    private void SetupRepoDefaults()
    {
        _repoMock.Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        _repoMock.Setup(r => r.UpdateSessionActivityAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        _repoMock.Setup(r => r.UpdateSessionSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
    }
}
