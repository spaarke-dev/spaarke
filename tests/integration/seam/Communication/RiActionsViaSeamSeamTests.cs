using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-13) for the comms-RI action
/// convergence — the point where an AUTHORIZED assessed communication becomes an observable end-user outcome. It
/// drives the REAL composed chain from the public seam entry point
/// (<see cref="RuleGatedAssessedConsumer.PublishAsync"/>, the <see cref="ICommunicationAssessedProducer"/> seam):
/// real <see cref="CommunicationRuleGate"/> (task 041) → real <see cref="CommunicationRiActionService"/> (task 042)
/// → real <see cref="ActionSeam"/> (task 031 Layer-A facade, ADR-013) → real <see cref="OutboxService"/> (task 012)
/// → real <see cref="NotificationService"/> (the appnotification mirror), doubling ONLY the two true external
/// boundaries: the Dataverse <see cref="IGenericEntityService"/> and the Azure-SignalR delivery leg
/// (<see cref="SignalRDeliveryService.PingUserAsync"/>, overridden to record).
/// </summary>
/// <remarks>
/// Proves the task-042 DoD both ways:
/// <list type="bullet">
///   <item><b>Authorize</b> — the domain record is created via the Layer-A seam (a <c>task</c>, NOT a direct
///     Dataverse write), a <c>kind=communication-assessed</c> outbox row is written BEFORE the best-effort ping,
///     the outbox envelope carries IDs + minimal display metadata + <c>regardingRecordId</c> only, and an
///     appnotification is created via an explicit <see cref="NotificationService.CreateNotificationAsync"/> call
///     (visible via the Daily-Briefing read path) — in that exact order.</item>
///   <item><b>Deny</b> — a below-threshold confidence yields NO seam call, NO outbox row, NO ping, and NO
///     appnotification (the short-circuit is structural: the RI service is only reached inside the gate's
///     authorize branch).</item>
/// </list>
/// </remarks>
public sealed class RiActionsViaSeamSeamTests
{
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
        public required RuleGatedAssessedConsumer Consumer { get; init; }
        public required RecordingDelivery Delivery { get; init; }
        public required List<DataverseEntity> Creates { get; init; }
        public required List<string> Events { get; init; }
        public required Guid CommunicationId { get; init; }
        public required Guid ThreadId { get; init; }
        public required Guid MatterId { get; init; }
        public required Guid OwnerId { get; init; }
    }

    /// <summary>
    /// Composes the REAL chain over a doubled Dataverse boundary. The assessed communication is owned by
    /// <c>OwnerId</c> (the RI recipient), regards <c>MatterId</c>, and a single enabled <c>sprk_communicationrule</c>
    /// matches that matter at threshold <paramref name="ruleThreshold"/>. All three write entity types
    /// (<c>task</c> / <c>sprk_notificationoutbox</c> / <c>appnotification</c>) record their creation into the shared
    /// ordering log so the seam→outbox→ping→mirror ordering is assertable.
    /// </summary>
    private static Harness BuildHarness(decimal ruleThreshold)
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var creates = new List<DataverseEntity>();
        var events = new List<string>();

        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);

        // Re-read of the assessed communication — serves BOTH the consumer's matter read (sprk_regardingmatter)
        // AND the RI service's owner/thread/channel/regarding read. One rich row satisfies each caller's projection.
        var communication = new DataverseEntity("sprk_communication", communicationId)
        {
            ["ownerid"] = new EntityReference("systemuser", ownerId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["sprk_regardingrecordid"] = matterId.ToString(),
            ["sprk_regardingrecordtype"] = "sprk_matter",
        };
        entity
            .Setup(s => s.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);

        // Rule store: one enabled rule matching the matter at the given threshold.
        var rule = new DataverseEntity("sprk_communicationrule", ruleId)
        {
            ["sprk_enabled"] = true,
            ["sprk_flagprivilege"] = false,
            ["sprk_priority"] = 100,
            ["sprk_matter"] = new EntityReference("sprk_matter", matterId),
            ["sprk_confidencethreshold"] = ruleThreshold,
        };
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationrule"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { rule }));

        // Three distinct create paths, each recording into the shared ordering log. With MockBehavior.Strict a
        // create that the code does NOT make (e.g. on the deny path) simply never fires — proving "no side effect".
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "task"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("seam-task-create"); return Guid.NewGuid(); });
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_notificationoutbox"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("outbox-write"); return Guid.NewGuid(); });
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) => { creates.Add(e); events.Add("appnotification-create"); return Guid.NewGuid(); });

        var logger = NullLoggerFactory.Instance;
        var gate = new CommunicationRuleGate(
            entity.Object,
            Options.Create(new CommsPolicyOptions { DefaultConfidenceThreshold = 0.8 }),
            NullLogger<CommunicationRuleGate>.Instance);

        // Real Layer-A facade (ADR-013). Only CreateTaskAsync is exercised → the field-mapping/scope deps are
        // never touched; loose doubles satisfy the ctor.
        var seam = new ActionSeam(
            entity.Object,
            new Mock<IFieldMappingDataverseService>().Object,
            new Mock<IServiceScopeFactory>().Object,
            NullLogger<ActionSeam>.Instance);

        var outbox = new OutboxService(entity.Object, NullLogger<OutboxService>.Instance);       // REAL outbox
        var delivery = new RecordingDelivery(events);                                             // SignalR boundary
        var notifications = new NotificationService(entity.Object, NullLogger<NotificationService>.Instance); // REAL mirror

        var riAction = new CommunicationRiActionService(
            seam, outbox, delivery, notifications, entity.Object, NullLogger<CommunicationRiActionService>.Instance);

        var consumer = new RuleGatedAssessedConsumer(
            entity.Object, gate, riAction, NullLogger<RuleGatedAssessedConsumer>.Instance);

        return new Harness
        {
            Consumer = consumer,
            Delivery = delivery,
            Creates = creates,
            Events = events,
            CommunicationId = communicationId,
            ThreadId = threadId,
            MatterId = matterId,
            OwnerId = ownerId,
        };
    }

    private static CommunicationAssessedSignal Signal(Guid communicationId, double confidence) =>
        new(communicationId, CommunicationDirection.Incoming, Subject: "Settlement terms", From: "counsel@firm.example", RecipientCount: 2, Confidence: confidence);

    // ── Authorize: seam record → outbox (kind=communication-assessed) → ping → appnotification, IN THAT ORDER. ──
    [Fact]
    public async Task PublishAsync_WhenGateAuthorizes_CreatesSeamTaskThenOutboxThenPingsThenMirrorsAppnotification()
    {
        var h = BuildHarness(ruleThreshold: 0.7m);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.9));

        // (1) The domain record was created via the Layer-A seam — a `task` regarding the communication, owned by
        //     the recipient — NOT a direct Dataverse write of some other shape.
        var task = h.Creates.SingleOrDefault(e => e.LogicalName == "task");
        task.Should().NotBeNull("the RI action is created via the Layer-A seam (ADR-013), not a direct write");
        task!.GetAttributeValue<string>("subject").Should().Contain("Settlement terms");
        task.GetAttributeValue<EntityReference>("regardingobjectid")!.Id.Should().Be(h.CommunicationId);
        task.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(h.OwnerId);

        // (2) One outbox row, kind=communication-assessed, IDs + minimal display metadata + regardingRecordId only.
        var outboxRow = h.Creates.SingleOrDefault(e => e.LogicalName == "sprk_notificationoutbox");
        outboxRow.Should().NotBeNull();
        outboxRow!.GetAttributeValue<string>("sprk_kind").Should().Be("communication-assessed");
        var envelope = JsonSerializer.Deserialize<CommunicationEnvelope>(outboxRow.GetAttributeValue<string>("sprk_envelope"));
        envelope.Should().NotBeNull();
        envelope!.Kind.Should().Be(NotificationKind.CommunicationAssessed);
        envelope.CommunicationId.Should().Be(h.CommunicationId);
        envelope.ThreadId.Should().Be(h.ThreadId);
        envelope.Channel.Should().Be("email");
        envelope.Direction.Should().Be("inbound");
        envelope.RegardingRecordId.Should().Be(h.MatterId.ToString());
        envelope.Snippet.Should().BeNull("NFR-02/03: content is never placed on the spine");

        // (3) One best-effort ping to the recipient, backed by the outbox row id.
        h.Delivery.Pings.Should().ContainSingle();
        h.Delivery.Pings[0].Recipient.Should().Be(h.OwnerId);
        h.Delivery.Pings[0].Kind.Should().Be(NotificationKind.CommunicationAssessed);
        h.Delivery.Pings[0].OutboxRowId.Should().NotBe(Guid.Empty);

        // (4) The appnotification mirror was created via the explicit NotificationService call, owned by the recipient.
        var appnotification = h.Creates.SingleOrDefault(e => e.LogicalName == "appnotification");
        appnotification.Should().NotBeNull("the RI action mirrors to appnotification for Daily-Briefing visibility");
        appnotification!.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(h.OwnerId);

        // Ordering: seam record → outbox BEFORE ping → appnotification mirror (ADR-041/043 outbox-before-ping;
        // seam-before-outbox per the task-042 escalation guard).
        h.Events.Should().Equal("seam-task-create", "outbox-write", $"ping:{h.OwnerId}", "appnotification-create");
    }

    // ── Deny: below-threshold confidence → NO seam call, NO outbox, NO ping, NO appnotification. ──
    [Fact]
    public async Task PublishAsync_WhenGateDenies_ProducesNoSeamCallNoOutboxNoPingNoAppnotification()
    {
        var h = BuildHarness(ruleThreshold: 0.9m);

        await h.Consumer.PublishAsync(Signal(h.CommunicationId, confidence: 0.5)); // 0.5 < 0.9 → deny

        h.Creates.Should().BeEmpty("a denied assessment creates no task, no outbox row, and no appnotification");
        h.Delivery.Pings.Should().BeEmpty("a denied assessment fires no Layer-C ping");
        h.Events.Should().BeEmpty("the RI action path is never entered on deny (structural short-circuit)");
    }
}
