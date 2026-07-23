using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="IncomingMessagingJobHandler"/> — the idempotent inbound ACS-message capture
/// (task 031 / FR-02, FR-04, NFR-02, NFR-03). This is the correctness crux of the inbound spine: it defends the
/// invariant that would silently double-persist or drop messages under Event Grid's at-least-once delivery.
/// A vertical slice is exercised — the REAL normalizer + REAL <see cref="MessagingIngestor"/> (through the REAL
/// <see cref="CommunicationChannelDispatcher"/>) — mocking only the Dataverse persist boundary
/// (<see cref="IGenericEntityService"/>) and enrichment; the idempotency service is an honest in-memory fake
/// (no wiring mocks). The persisted-record count is <c>IGenericEntityService.CreateAsync</c> call count.
///
/// Invariants proven:
///   (1) single inbound event            → exactly one persist;
///   (2) duplicate delivery of same id    → still exactly one persist (dedupe);
///   (3) our own outbound echo (pre-marked)→ no-op, zero persists (FR-04);
///   (4) concurrent delivery holds the lock→ this delivery no-ops the persist and retries (lock race);
///   (5) repeated processing failure       → JobStatus.Poisoned (processor dead-letters — never dropped);
///   (6) enrichment throws                 → the sprk_communication is STILL persisted (NFR-02).
/// </summary>
public class IncomingMessagingJobHandlerTests
{
    private const string ThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";
    private const string MessageId = "1784231661455";

    /// <summary>Honest in-memory <see cref="IIdempotencyService"/> — real processed-set + lock semantics.</summary>
    private sealed class FakeIdempotencyService : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new();
        private readonly HashSet<string> _locks = new();

        public void SeedProcessed(string key) => _processed.Add(key);
        public void SeedLockHeld(string key) => _locks.Add(key);
        public bool IsProcessed(string key) => _processed.Contains(key);

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken ct = default)
            => Task.FromResult(_processed.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken ct = default)
        {
            _processed.Add(eventId);
            return Task.CompletedTask;
        }

        // HashSet.Add returns false when the key is already present → models "lock already held".
        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken ct = default)
            => Task.FromResult(_locks.Add(eventId));

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken ct = default)
        {
            _locks.Remove(eventId);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public Mock<IGenericEntityService> Generic { get; } = new();
        public Mock<ICommunicationEnrichmentService> Enrichment { get; } = new();
        public FakeIdempotencyService Idempotency { get; } = new();
        public IncomingMessagingJobHandler Handler { get; }

        public Harness()
        {
            Generic
                .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

            var ingestor = new MessagingIngestor(
                Generic.Object, Enrichment.Object, Mock.Of<ILogger<MessagingIngestor>>());

            var dispatcher = new CommunicationChannelDispatcher(
                senders: Enumerable.Empty<ICommunicationChannelSender>(),
                archivers: Enumerable.Empty<ICommunicationArchiver>(),
                ingestors: new ICommunicationChannelIngestor[] { ingestor });

            Handler = new IncomingMessagingJobHandler(
                new AcsEventNormalizer(),
                dispatcher,
                Idempotency,
                Mock.Of<ILogger<IncomingMessagingJobHandler>>());
        }

        public void VerifyPersistCount(int times) =>
            Generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Exactly(times));
    }

    private static JobContract JobFor(
        string? messageId = MessageId,
        string threadId = ThreadId,
        string eventType = "Microsoft.Communication.ChatMessageReceivedInThread",
        int attempt = 1,
        int maxAttempts = 3)
    {
        var data = new Dictionary<string, object?>
        {
            ["messageId"] = messageId,
            ["threadId"] = threadId,
            ["messageBody"] = "Hello from ACS",
            ["messageType"] = "Text",
            ["senderCommunicationIdentifier"] = new Dictionary<string, object?> { ["rawId"] = "8:acs:sender" },
            ["composeTime"] = "2026-07-16T12:00:01Z",
        };
        var evt = new Dictionary<string, object?>
        {
            ["id"] = "evt-1",
            ["eventType"] = eventType,
            ["subject"] = $"thread/{threadId}",
            ["eventTime"] = "2026-07-16T12:00:02Z",
            ["data"] = data,
        };

        var payload = new IncomingMessagingJobPayload
        {
            AcsMessageId = messageId,
            AcsThreadId = threadId,
            EventType = eventType,
            EventId = "evt-1",
            EventTime = DateTimeOffset.Parse("2026-07-16T12:00:02Z"),
            RawEvent = JsonSerializer.SerializeToElement(evt),
        };

        return new JobContract
        {
            JobType = IncomingMessagingJobHandler.JobTypeName,
            SubjectId = messageId ?? "unknown",
            CorrelationId = "corr-031",
            IdempotencyKey = $"Messaging:{eventType}:{messageId}",
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            Payload = JsonSerializer.SerializeToDocument(payload),
        };
    }

    // ── (1) single inbound event → exactly one persist ──
    [Fact]
    public async Task ProcessAsync_SingleInboundEvent_PersistsExactlyOneCommunication()
    {
        var h = new Harness();

        var outcome = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.VerifyPersistCount(1);
        h.Idempotency.IsProcessed(IncomingMessagingJobHandler.IdempotencyKeyFor(MessageId)).Should().BeTrue();
    }

    // ── (2) duplicate delivery of the same ACS message id → still exactly one persist (NFR-03) ──
    [Fact]
    public async Task ProcessAsync_SameEventDeliveredTwice_PersistsExactlyOneCommunication()
    {
        var h = new Harness();

        var first = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);
        var second = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Completed);
        second.Status.Should().Be(JobStatus.Completed); // deduped no-op, not a failure
        h.VerifyPersistCount(1);
    }

    // ── (3) our own outbound echo (id already marked by task 051 persist-on-send) → no-op (FR-04) ──
    [Fact]
    public async Task ProcessAsync_OwnOutboundEcho_AlreadyMarked_IsNoOp_ZeroPersists()
    {
        var h = new Harness();
        // Simulate task 051 outbound persist-on-send having recorded the ACS message id.
        h.Idempotency.SeedProcessed(IncomingMessagingJobHandler.IdempotencyKeyFor(MessageId));

        var outcome = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.VerifyPersistCount(0);
    }

    // ── (4) concurrent delivery holds the lock → this delivery no-ops the persist and retries (lock race) ──
    [Fact]
    public async Task ProcessAsync_WhenLockHeldByConcurrentDelivery_DoesNotPersist_AndReturnsRetryable()
    {
        var h = new Harness();
        // A peer delivery of the SAME id is mid-flight (holds the processing lock, not yet marked processed).
        h.Idempotency.SeedLockHeld(IncomingMessagingJobHandler.IdempotencyKeyFor(MessageId));

        var outcome = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);

        // Lost the race → no persist; retryable so the redelivery dedupes once the peer marks it processed.
        outcome.Status.Should().Be(JobStatus.Failed);
        h.VerifyPersistCount(0);
    }

    // ── (5) repeated processing failure → Poisoned (processor dead-letters; never silently dropped) ──
    [Fact]
    public async Task ProcessAsync_WhenPersistFailsAtMaxAttempts_ReturnsPoisoned_ForDeadLetter()
    {
        var h = new Harness();
        h.Generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse persist boom"));

        var outcome = await h.Handler.ProcessAsync(
            JobFor(attempt: 3, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        // The idempotency mark was NOT set (persist never completed) → a redrive can legitimately retry.
        h.Idempotency.IsProcessed(IncomingMessagingJobHandler.IdempotencyKeyFor(MessageId)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_WhenTransientFailureBeforeMaxAttempts_ReturnsFailed_ForRetry()
    {
        var h = new Harness();
        h.Generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse 503"));

        var outcome = await h.Handler.ProcessAsync(
            JobFor(attempt: 1, maxAttempts: 3), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
    }

    // ── (6) enrichment throws → the sprk_communication is STILL persisted (NFR-02) ──
    [Fact]
    public async Task ProcessAsync_WhenEnrichmentThrows_StillPersistsAndSucceeds()
    {
        var h = new Harness();
        h.Enrichment
            .Setup(e => e.EnrichAsync(
                It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        var outcome = await h.Handler.ProcessAsync(JobFor(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.VerifyPersistCount(1);
    }

    // ── Participant events carry no message id → no-op success (nothing to persist) ──
    [Fact]
    public async Task ProcessAsync_EventWithNoMessageId_IsNoOpSuccess()
    {
        var h = new Harness();

        var outcome = await h.Handler.ProcessAsync(
            JobFor(messageId: null, eventType: "Microsoft.Communication.ParticipantAddedToThread"),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.VerifyPersistCount(0);
    }
}
