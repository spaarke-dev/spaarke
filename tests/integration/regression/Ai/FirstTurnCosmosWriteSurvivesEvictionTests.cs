using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ai;

/// <summary>
/// Regression — FR-D2 (spaarkeai-assistant-enhancements-r2, task 030): the Cosmos transcript write
/// was fire-and-forget for EVERY turn (<c>ChatSessionManager.FireAndForgetCosmosPersist</c> /
/// <c>SessionPersistenceService.UpsertToCosmosAsync</c>), so a session's first turn could be lost
/// to a Redis eviction that happened before the detached background write finished — reopening the
/// session showed a blank pane instead of the transcript.
///
/// Fix: <see cref="ChatHistoryManager.AddMessageAsync"/> now requests a CONFIRMED (awaited) Cosmos
/// write ONLY for <c>messages[0]</c> — the first message added to a brand-new session, which also
/// seeds the History title (FR-D4). Every later turn keeps the original fire-and-forget contract
/// (D-06) so per-turn latency is unaffected (NFR-03).
///
/// These tests wire the REAL <see cref="ChatSessionManager"/> + <see cref="ChatHistoryManager"/>
/// against an <see cref="InMemoryTenantCache"/> (a genuine <c>ITenantCache</c> implementation
/// standing in for Redis) and a mocked <see cref="ISessionPersistenceService"/> (standing in for
/// Cosmos, which has no in-process fake) — the production write-through wiring is exercised
/// end-to-end, not re-implemented in the test.
///
/// No <c>Task.Delay</c> / wall-clock waiting is used (ADR-038 ban): the ordering proof in (a) relies
/// on the fact that every collaborator up to the Cosmos call resolves synchronously (in-memory cache,
/// mocked Dataverse repo), so the async state machine only genuinely suspends at the gated Cosmos
/// mock — <c>addTask.IsCompleted</c> is deterministic immediately after the call returns.
///
/// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — "every bug fix = one
/// regression test". Compiled into Sprk.Bff.Api.Tests via the
/// <c>..\..\integration\regression\**\*.cs</c> glob.
/// </summary>
public class FirstTurnCosmosWriteSurvivesEvictionTests
{
    private const string TenantId = "tenant-fr-d2";
    private const string DocumentId = "doc-fr-d2";

    private readonly InMemoryTenantCache _cache = new();
    private readonly Mock<IChatDataverseRepository> _dataverseRepoMock = new();
    private readonly Mock<ISessionPersistenceService> _persistenceMock = new();
    private readonly ChatSessionManager _sessionManager;
    private readonly ChatHistoryManager _historyManager;

    public FirstTurnCosmosWriteSurvivesEvictionTests()
    {
        _dataverseRepoMock
            .Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dataverseRepoMock
            .Setup(r => r.AddMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dataverseRepoMock
            .Setup(r => r.UpdateSessionActivityAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sessionManager = new ChatSessionManager(
            _cache,
            _dataverseRepoMock.Object,
            NullLogger<ChatSessionManager>.Instance,
            persistence: _persistenceMock.Object);

        _historyManager = new ChatHistoryManager(
            _sessionManager,
            _dataverseRepoMock.Object,
            NullLogger<ChatHistoryManager>.Instance);
    }

    // =========================================================================
    // (a) messages[0] — the Cosmos write is CONFIRMED (awaited), not fire-and-forget
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_FirstMessageOfNewSession_AwaitsCosmosWriteBeforeReturning()
    {
        // Arrange — a brand-new session (no messages yet); `userMessage` will become messages[0].
        var session = NewSession(sessionId: "session-first-turn", messageCount: 0);
        var userMessage = NewMessage(session.SessionId, sequenceNumber: 0);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? observedAwaitFlag = null;
        StoredSession? capturedStoredSession = null;

        _persistenceMock
            .Setup(p => p.PersistSessionAsync(It.IsAny<StoredSession>(), It.IsAny<CancellationToken>(), true))
            .Callback<StoredSession, CancellationToken, bool>((s, _, flag) =>
            {
                capturedStoredSession = s;
                observedAwaitFlag = flag;
            })
            .Returns(gate.Task);

        // Act — start AddMessageAsync but do NOT await it yet. Every collaborator reached before the
        // Cosmos call (Dataverse mock, InMemoryTenantCache) resolves synchronously, so if the Cosmos
        // write is genuinely awaited, the returned Task is deterministically still incomplete here —
        // no Task.Delay / yield required (ADR-038 bans wall-clock waits in tests).
        var addTask = _historyManager.AddMessageAsync(session, userMessage);

        // Assert — the FIRST-turn write must be a genuine await: the outer Task must not be able to
        // complete until the gated Cosmos mock resolves. This is what distinguishes the FR-D2 fix
        // from the original fire-and-forget (`_ = _persistence.PersistSessionAsync(...)`), which
        // would have let addTask complete regardless of the mock's task state.
        addTask.IsCompleted.Should().BeFalse(
            "FR-D2 requires the messages[0] Cosmos write to be AWAITED, not fire-and-forget — " +
            "the caller must not observe completion before the durable write finishes");

        // Release the gate — simulates the Cosmos upsert completing.
        gate.SetResult();
        var updatedSession = await addTask;

        // Assert — the write that WAS awaited is the confirmed-write overload, carrying messages[0].
        observedAwaitFlag.Should().BeTrue("messages[0] must request the confirmed (awaited) write");
        capturedStoredSession.Should().NotBeNull();
        capturedStoredSession!.Messages.Should().ContainSingle();
        capturedStoredSession.Messages[0].Content.Should().Be(userMessage.Content);
        updatedSession.Messages.Should().ContainSingle();
    }

    // =========================================================================
    // (b) Later turns — the Cosmos write stays fire-and-forget (NFR-03, no latency regression)
    // =========================================================================

    [Fact]
    public async Task AddMessageAsync_SecondMessageOfSession_DoesNotAwaitCosmosWrite()
    {
        // Arrange — a session that already has one message (messages[0] already landed); the new
        // message becomes messages[1] — a later turn, which must NOT block on Cosmos.
        var session = NewSession(sessionId: "session-second-turn", messageCount: 1);
        var secondMessage = NewMessage(session.SessionId, sequenceNumber: 1);

        // The mock NEVER completes this task. If AddMessageAsync awaited it, `await`ing the method
        // below would hang and the test would fail on xUnit's default timeout — proving the write
        // is genuinely fire-and-forget without resorting to a wall-clock delay.
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? observedAwaitFlag = null;

        _persistenceMock
            .Setup(p => p.PersistSessionAsync(It.IsAny<StoredSession>(), It.IsAny<CancellationToken>(), false))
            .Callback<StoredSession, CancellationToken, bool>((_, _, flag) => observedAwaitFlag = flag)
            .Returns(neverCompletes.Task);

        // Act
        var updatedSession = await _historyManager.AddMessageAsync(session, secondMessage);

        // Assert
        observedAwaitFlag.Should().BeFalse(
            "turns after messages[0] must keep the D-06 fire-and-forget contract (NFR-03 — no latency regression)");
        updatedSession.Messages.Should().HaveCount(2);
    }

    // =========================================================================
    // (c) End-to-end: messages[0] survives a simulated Redis eviction (spec FR-D2 acceptance)
    // =========================================================================

    [Fact]
    public async Task FirstMessage_SurvivesSimulatedRedisEviction_ReopenShowsTranscriptNotBlankPane()
    {
        // Arrange — brand-new session; the confirmed write means Cosmos genuinely has messages[0]
        // by the time AddMessageAsync returns (no gating needed here — Returns(Task.CompletedTask)
        // resolves synchronously, matching a successful real Cosmos upsert).
        var session = NewSession(sessionId: "session-evicted", messageCount: 0);
        var userMessage = NewMessage(session.SessionId, sequenceNumber: 0, content: "What are the key terms?");

        StoredSession? cosmosDocument = null;
        _persistenceMock
            .Setup(p => p.PersistSessionAsync(It.IsAny<StoredSession>(), It.IsAny<CancellationToken>(), true))
            .Callback<StoredSession, CancellationToken, bool>((s, _, _) => cosmosDocument = s)
            .Returns(Task.CompletedTask);

        await _historyManager.AddMessageAsync(session, userMessage);
        cosmosDocument.Should().NotBeNull("the confirmed write must have reached Cosmos by the time AddMessageAsync returns");

        // Act — simulate a Redis eviction: remove the session from the hot-tier cache directly.
        // InMemoryTenantCache is a real ITenantCache implementation, so this exercises the same
        // eviction path Redis's TTL / memory-pressure eviction would trigger.
        await _cache.RemoveAsync(TenantId, ChatSessionManager.CacheResource, session.SessionId, ChatSessionManager.CacheVersion);

        // Cosmos (the persistence mock) now stands in as the durable warm tier holding exactly what
        // the confirmed write persisted.
        _persistenceMock
            .Setup(p => p.LoadSessionAsync(TenantId, session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cosmosDocument);

        // Assert — reopening the session (GetSessionAsync) must restore the transcript from Cosmos,
        // not return a blank/empty session (the FR-D2 regression this test guards against).
        var reopened = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);

        reopened.Should().NotBeNull();
        reopened!.Messages.Should().ContainSingle("the first turn must survive the Redis eviction");
        reopened.Messages[0].Content.Should().Be("What are the key terms?");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static ChatSession NewSession(string sessionId, int messageCount)
    {
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => NewMessage(sessionId, i))
            .ToList()
            .AsReadOnly();

        return new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: DocumentId,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: messages);
    }

    private static ChatMessage NewMessage(string sessionId, int sequenceNumber, string? content = null)
        => new ChatMessage(
            MessageId: $"MSG-{sequenceNumber:D6}",
            SessionId: sessionId,
            Role: ChatMessageRole.User,
            Content: content ?? $"Test message {sequenceNumber}",
            TokenCount: 10,
            CreatedAt: DateTimeOffset.UtcNow,
            SequenceNumber: sequenceNumber);
}
