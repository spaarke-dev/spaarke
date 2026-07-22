using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Notifications;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-15 / NFR-03) for
/// <see cref="DailyBriefingSuggestionProducer"/> — the proactive <c>kind=suggestion</c> producer. Proves the
/// grounded+gated-BEFORE-write invariant end to end through the REAL task-012 <see cref="OutboxService"/>,
/// doubling ONLY the Dataverse (<see cref="IGenericEntityService"/>) + SignalR (<see cref="SignalRDeliveryService"/>)
/// boundaries. Per <c>tests/CLAUDE.md</c> (integration-first; B7/B15 mock-heavy-unit bans), the grounded /
/// ungrounded / ungated cases live here against the real outbox rather than in a separate mock-only unit file.
/// </summary>
public sealed class DailyBriefingSuggestionProducerSeamTests
{
    /// <summary>Records the real ping calls so the outbox-before-ping ordering + best-effort delivery are observable.</summary>
    private sealed class RecordingDelivery : SignalRDeliveryService
    {
        public readonly List<(Guid OutboxRowId, Guid Recipient, NotificationKind Kind)> Pings = new();
        public RecordingDelivery() : base((ISystemUserIdentityResolver?)null, NullLogger<SignalRDeliveryService>.Instance) { }
        public override Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
        {
            Pings.Add((outboxRowId, recipientSystemUserId, kind));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public required DailyBriefingSuggestionProducer Producer { get; init; }
        public required RecordingDelivery Delivery { get; init; }
        public required List<DataverseEntity> OutboxWrites { get; init; }
        public required Guid Recipient { get; init; }
    }

    /// <summary>Builds the producer over the REAL OutboxService, doubling only the Dataverse + SignalR boundaries.</summary>
    private static Harness BuildHarness(SuggestionGateOptions options)
    {
        var recipient = Guid.NewGuid();
        var outboxWrites = new List<DataverseEntity>();

        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.CreateAsync(
                It.Is<DataverseEntity>(e => e.LogicalName == "sprk_notificationoutbox"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { outboxWrites.Add(e); return Guid.NewGuid(); });

        var outbox = new OutboxService(entity.Object, NullLogger<OutboxService>.Instance); // REAL outbox
        var delivery = new RecordingDelivery();

        var producer = new DailyBriefingSuggestionProducer(
            outbox, delivery, Options.Create(options), NullLogger<DailyBriefingSuggestionProducer>.Instance);

        return new Harness { Producer = producer, Delivery = delivery, OutboxWrites = outboxWrites, Recipient = recipient };
    }

    private static HighPriorityItemDto Item(
        string entityType, string entityId, string name, bool highPriority = true, bool monitor = false) =>
        new() { EntityType = entityType, EntityId = entityId, Name = name, HighPriority = highPriority, Monitor = monitor };

    private static SuggestionGateOptions GateOptions(bool enabled, int maxPerRun = 3, int ttlHours = 24) =>
        new() { Enabled = enabled, MaxPerRun = maxPerRun, TtlHours = ttlHours };

    // ── Grounded + proactive-gate-enabled → exactly one kind=suggestion row, correct envelope, then a ping. ──
    [Fact]
    public async Task ProduceAsync_GroundedItem_GateEnabled_WritesOneSuggestionRowWithEnvelopeThenPings()
    {
        var h = BuildHarness(GateOptions(enabled: true));
        var recordId = Guid.NewGuid();
        var item = Item("sprk_matter", recordId.ToString(), "Acme v. Beta");

        var written = await h.Producer.ProduceAsync(h.Recipient, new[] { item });

        written.Should().Be(1);
        h.OutboxWrites.Should().ContainSingle();
        var row = h.OutboxWrites[0];
        row.GetAttributeValue<string>("sprk_kind").Should().Be("suggestion");
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(recordId.ToString());
        row.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("sprk_matter");

        var envelope = JsonSerializer.Deserialize<SuggestionEnvelope>(row.GetAttributeValue<string>("sprk_envelope"));
        envelope.Should().NotBeNull();
        envelope!.Kind.Should().Be(NotificationKind.Suggestion);
        envelope.SuggestionId.Should().NotBe(Guid.Empty);
        envelope.Source.Should().Be("daily-briefing");
        envelope.RegardingRecordId.Should().Be(recordId.ToString());
        envelope.Title.Should().Be("Review Acme v. Beta");
        envelope.ActionHint.Should().Be("review", "actionHint drives the renderer, never a pre-authorized token");
        envelope.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow, "expiresAt is populated per the task-013 contract");
        envelope.Snippet.Should().BeNull("NFR-02/03: content is never placed on the spine");

        // outbox-before-ping: the ping carries the row id the write returned.
        h.Delivery.Pings.Should().ContainSingle();
        h.Delivery.Pings[0].Recipient.Should().Be(h.Recipient);
        h.Delivery.Pings[0].Kind.Should().Be(NotificationKind.Suggestion);
        h.Delivery.Pings[0].OutboxRowId.Should().NotBe(Guid.Empty);
    }

    // ── Ungrounded (unparseable record id) → ZERO rows, regardless of gate. ──
    [Fact]
    public async Task ProduceAsync_UngroundedItem_WritesZeroRows()
    {
        var h = BuildHarness(GateOptions(enabled: true));
        var ungrounded = Item("sprk_matter", entityId: "not-a-guid", name: "Phantom");

        var written = await h.Producer.ProduceAsync(h.Recipient, new[] { ungrounded });

        written.Should().Be(0, "an ungrounded candidate (ADR-039) yields no outbox row even with the gate enabled");
        h.OutboxWrites.Should().BeEmpty();
        h.Delivery.Pings.Should().BeEmpty();
    }

    // ── Grounded but proactive gate DISABLED (kill-switch off) → ZERO rows. ──
    [Fact]
    public async Task ProduceAsync_GroundedItem_GateDisabled_WritesZeroRows()
    {
        var h = BuildHarness(GateOptions(enabled: false)); // deny-by-default
        var item = Item("sprk_matter", Guid.NewGuid().ToString(), "Acme v. Beta");

        var written = await h.Producer.ProduceAsync(h.Recipient, new[] { item });

        written.Should().Be(0, "the ADR-041 proactive gate is off → no row (NFR-03: nothing ungated reaches the spine)");
        h.OutboxWrites.Should().BeEmpty();
        h.Delivery.Pings.Should().BeEmpty();
    }

    // ── Grounded + gate enabled but NOT confirm-worthy (declared reason absent) → ZERO rows. ──
    [Fact]
    public async Task ProduceAsync_GroundedItem_NotHighPriorityOrMonitor_WritesZeroRows()
    {
        var h = BuildHarness(GateOptions(enabled: true));
        var item = Item("sprk_matter", Guid.NewGuid().ToString(), "Routine matter", highPriority: false, monitor: false);

        var written = await h.Producer.ProduceAsync(h.Recipient, new[] { item });

        written.Should().Be(0, "the proactive gate admits only items confirm-worthy by declared reason (HighPriority|Monitor)");
        h.OutboxWrites.Should().BeEmpty();
    }

    // ── Volume cap: MaxPerRun bounds the rows written even when many candidates pass both gates. ──
    [Fact]
    public async Task ProduceAsync_ManyGroundedGatedCandidates_CapsAtMaxPerRun()
    {
        var h = BuildHarness(GateOptions(enabled: true, maxPerRun: 2));
        var items = Enumerable.Range(0, 5)
            .Select(i => Item("sprk_matter", Guid.NewGuid().ToString(), $"Matter {i}"))
            .ToArray();

        var written = await h.Producer.ProduceAsync(h.Recipient, items);

        written.Should().Be(2, "MaxPerRun caps the per-run suggestion volume");
        h.OutboxWrites.Should().HaveCount(2);
    }
}
