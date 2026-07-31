using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-09 / NFR-05) for
/// <see cref="CommunicationArrivedProducer"/> — the single spine-owned <c>communication-arrived</c> emit. Proves
/// the DoD for task 024: a persisted communication (email OR message, inbound OR outbound) yields a durable
/// outbox row (kind=<c>communication-arrived</c>) written BEFORE a best-effort ping, and a producer failure never
/// propagates. It exercises the REAL composition — the real task-023 <see cref="CommunicationFanOutTargetingService"/>
/// (with the real <see cref="CommunicationAccessFilter"/> + fail-closed <see cref="DenyAllThreadPrivateGrantProvider"/>)
/// and the real task-012 <see cref="OutboxService"/> — doubling ONLY the module boundaries: the Dataverse
/// <see cref="IGenericEntityService"/> (re-read + junction read + outbox create) and the SignalR delivery leg
/// (<see cref="SignalRDeliveryService.PingUserAsync"/>, overridden to record). No assessment/enrichment collaborator
/// is wired anywhere here — the producer fires purely on persistence, which is exactly the FR-09 contract that
/// distinguishes it from task 040's assessment-gated <c>communication_assessed</c> producer (criterion 6).
/// </summary>
public sealed class CommunicationArrivedProducerSeamTests
{
    // sprk_communicationtype (CommunicationType) + sprk_direction (CommunicationDirection) choice integers.
    private const int TypeEmail = 100000000;
    private const int TypeMessage = 100000004;
    private const int DirectionIncoming = 100000000;
    private const int DirectionOutgoing = 100000001;
    private const int PrivacyOpen = 100000000; // ThreadPrivacyState.Open

    /// <summary>Records the real ping calls + appends "ping:{recipient}" to the shared ordering log.</summary>
    private sealed class RecordingDelivery : SignalRDeliveryService
    {
        public readonly List<(Guid OutboxRowId, Guid Recipient, NotificationKind Kind)> Pings = new();
        private readonly List<string> _events;

        public RecordingDelivery(List<string> events)
            : base((ISystemUserIdentityResolver?)null, NullLogger<SignalRDeliveryService>.Instance)
            => _events = events;

        public override Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
        {
            Pings.Add((outboxRowId, recipientSystemUserId, kind));
            _events.Add($"ping:{recipientSystemUserId}");
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public required CommunicationArrivedProducer Producer { get; init; }
        public required RecordingDelivery Delivery { get; init; }
        public required List<DataverseEntity> OutboxWrites { get; init; }
        public required List<CreateNotificationRequest> Notifications { get; init; }
        public required List<string> Events { get; init; }
        public required Guid CommunicationId { get; init; }
        public required Guid ThreadId { get; init; }
        public required Guid Recipient { get; init; }
        public required Guid MatterId { get; init; }
    }

    /// <summary>
    /// Builds the producer over the REAL fan-out + REAL outbox, doubling only the Dataverse + SignalR boundaries.
    /// The persisted communication is a single-internal-participant, Open-thread, non-internal-only row, so the
    /// fan-out yields exactly one recipient — isolating the producer's own outbox-then-ping behavior.
    /// </summary>
    private static Harness BuildHarness(int commType, int direction, bool outboxThrows = false)
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var outboxWrites = new List<DataverseEntity>();
        var events = new List<string>();

        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);

        // Re-read the persisted communication (the producer's projection contract). The TYPED ADR-024 regarding
        // lookup (sprk_regardingmatter) is the reliable source of the Q2 deep-link entity type — the string
        // sprk_regardingrecordtype is a lookup, not a usable type name.
        var message = new DataverseEntity("sprk_communication", communicationId)
        {
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            ["sprk_communicationtype"] = new OptionSetValue(commType),
            ["sprk_direction"] = new OptionSetValue(direction),
            ["sprk_isinternalonly"] = false,
            ["createdon"] = DateTime.UtcNow,
            ["sprk_regardingrecordid"] = matterId.ToString(),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
        };
        entity
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Re-read the thread (Open → fan-out gated only by the internal-only filter, which passes here).
        var thread = new DataverseEntity("sprk_communicationthread", threadId)
        {
            ["sprk_privacystate"] = new OptionSetValue(PrivacyOpen),
        };
        entity
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        // Junction read (fan-out candidate set) — one resolved internal systemuser participant.
        var junction = new List<DataverseEntity>
        {
            new("sprk_communicationparticipant")
            {
                ["sprk_isresolved"] = true,
                ["sprk_systemuser"] = new EntityReference("systemuser", recipient),
            },
        };
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationparticipant"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(junction));

        // Outbox write (OutboxService.WriteAsync → IGenericEntityService.CreateAsync). Records the write + order.
        var create = entity.Setup(s => s.CreateAsync(
            It.Is<DataverseEntity>(e => e.LogicalName == "sprk_notificationoutbox"),
            It.IsAny<CancellationToken>()));
        if (outboxThrows)
        {
            create.ThrowsAsync(new InvalidOperationException("outbox write boom"));
        }
        else
        {
            create.ReturnsAsync((DataverseEntity e, CancellationToken _) =>
            {
                outboxWrites.Add(e);
                events.Add("outbox-write");
                return Guid.NewGuid();
            });
        }

        // Authoritative externality flag: everyone internal (sprk_isexternal=false) → the recipient is eligible.
        var resolver = new Mock<ISystemUserIdentityResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var fanout = new CommunicationFanOutTargetingService(
            entity.Object,
            new CommunicationAccessFilter(NullLogger<CommunicationAccessFilter>.Instance), // REAL filter
            new DenyAllThreadPrivateGrantProvider(),                                        // REAL fail-closed default
            resolver.Object,
            NullLogger<CommunicationFanOutTargetingService>.Instance);

        var outbox = new OutboxService(entity.Object, NullLogger<OutboxService>.Instance); // REAL outbox
        var delivery = new RecordingDelivery(events);

        // IActionSeam boundary (Layer-A facade) — records the Q2 app-notification requests, returns success.
        var notifications = new List<CreateNotificationRequest>();
        var seam = new Mock<IActionSeam>();
        seam
            .Setup(s => s.CreateNotificationAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((req, _) =>
            {
                notifications.Add(req);
                events.Add($"notify:{req.RecipientId}");
            })
            .ReturnsAsync(new CreateNotificationResult(true, Guid.NewGuid(), false, null));

        var producer = new CommunicationArrivedProducer(
            entity.Object, fanout, outbox, delivery, seam.Object, NullLogger<CommunicationArrivedProducer>.Instance);

        return new Harness
        {
            Producer = producer,
            Delivery = delivery,
            OutboxWrites = outboxWrites,
            Notifications = notifications,
            Events = events,
            CommunicationId = communicationId,
            ThreadId = threadId,
            Recipient = recipient,
            MatterId = matterId,
        };
    }

    // ── DoD: both channels × both directions each yield an outbox row (kind=communication-arrived) written
    //    BEFORE a ping — proving capture and send are treated identically across email and message (c1-c4, c7, c8).
    //    No assessment collaborator is wired, so this also proves emit-on-persistence with no assessment
    //    prerequisite (c6) — the boundary vs task 040's communication_assessed producer. ──

    [Theory]
    [InlineData(TypeEmail, DirectionIncoming, "email", "inbound")]
    [InlineData(TypeEmail, DirectionOutgoing, "email", "outbound")]
    [InlineData(TypeMessage, DirectionIncoming, "message", "inbound")]
    [InlineData(TypeMessage, DirectionOutgoing, "message", "outbound")]
    public async Task EmitCommunicationArrived_ForPersistedCommunication_WritesOutboxThenPingsFanOut(
        int commType, int direction, string expectedChannel, string expectedDirection)
    {
        var h = BuildHarness(commType, direction);

        await h.Producer.EmitCommunicationArrivedAsync(h.CommunicationId);

        // Exactly one outbox row, correct kind + envelope shape (IDs + display metadata only).
        h.OutboxWrites.Should().ContainSingle();
        var row = h.OutboxWrites[0];
        row.GetAttributeValue<string>("sprk_kind").Should().Be("communication-arrived");

        var envelope = JsonSerializer.Deserialize<CommunicationEnvelope>(row.GetAttributeValue<string>("sprk_envelope"));
        envelope.Should().NotBeNull();
        envelope!.Kind.Should().Be(NotificationKind.CommunicationArrived);
        envelope.CommunicationId.Should().Be(h.CommunicationId);
        envelope.ThreadId.Should().Be(h.ThreadId);
        envelope.Channel.Should().Be(expectedChannel);
        envelope.Direction.Should().Be(expectedDirection);
        envelope.Snippet.Should().BeNull("NFR-02/03: content is never placed on the spine");

        // Exactly one ping, to the fan-out recipient, backed by a real outbox row id.
        h.Delivery.Pings.Should().ContainSingle();
        var ping = h.Delivery.Pings[0];
        ping.Recipient.Should().Be(h.Recipient);
        ping.Kind.Should().Be(NotificationKind.CommunicationArrived);
        ping.OutboxRowId.Should().NotBe(Guid.Empty, "the ping carries the outbox row id from the prior write");

        // Outbox BEFORE ping — the write-before-ping invariant (ADR-041/043) — then the Q2 app-notification mirror.
        h.Events.Should().Equal("outbox-write", $"ping:{h.Recipient}", $"notify:{h.Recipient}");
    }

    // ── NFR-05: a producer failure (here, the durable outbox write) is non-fatal — it never throws back into the
    //    persist path, and no ping fires without a durable row (write-before-ping) (c5). ──

    [Fact]
    public async Task EmitCommunicationArrived_WhenOutboxWriteThrows_DoesNotThrowAndSkipsPing()
    {
        var h = BuildHarness(TypeEmail, DirectionIncoming, outboxThrows: true);

        Func<Task> act = () => h.Producer.EmitCommunicationArrivedAsync(h.CommunicationId);

        await act.Should().NotThrowAsync(
            "NFR-05: a producer exception must never fail the persist call that triggered it");
        h.Delivery.Pings.Should().BeEmpty(
            "no live ping is sent when the durable outbox write failed (write-before-ping)");
    }

    // ── Q2 (2026-07-28): each fan-out recipient ALSO gets a persistent, clickable Dataverse app-notification,
    //    deep-linked to the regarding record (which hosts the conversation panel), deduped per thread. Proves the
    //    producer composes the IActionSeam request correctly — the sanctioned CommunicationRiActionService mirror
    //    pattern generalized to the fan-out set. ──

    [Fact]
    public async Task EmitCommunicationArrived_MirrorsClickableAppNotification_PerRecipient_DeepLinkedToRegardingRecord()
    {
        var h = BuildHarness(TypeEmail, DirectionIncoming);

        await h.Producer.EmitCommunicationArrivedAsync(h.CommunicationId);

        // One recipient → one bell notification.
        h.Notifications.Should().ContainSingle();
        var n = h.Notifications[0];

        n.RecipientId.Should().Be(h.Recipient);
        n.Category.Should().Be("communication");
        n.RegardingId.Should().Be(h.ThreadId, "idempotency dedups per (recipient + category + thread) — one unread bell per thread");
        n.RegardingType.Should().Be("sprk_communicationthread");
        n.ToastType.Should().Be(200_000_000, "Timed toast emits the clickable Open action (Hidden would suppress it)");
        n.ActionUrl.Should().Be(
            $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={h.MatterId:D}&sprk_openconversation=1",
            "the bell deep-links to the regarding record (resolved from the TYPED lookup) and appends " +
            "sprk_openconversation=1 so the CommunicationConversationPanel PCF auto-opens (messaging-r3 round-7 item 11)");
        n.CorrelationId.Should().Be(h.CommunicationId.ToString());
        n.Title.Should().Be("New email", "signal-only channel label — never an address or content (NFR-02/03)");
    }
}
