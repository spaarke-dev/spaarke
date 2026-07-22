using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 / spec FR-22 / NFR-02 / NFR-03) for the FR-22 emit path
/// <see cref="CommunicationArrivalNotifier"/> — the "producer (task 024)" that composes the notification
/// spine's three real primitives to emit <see cref="NotificationKind.CommunicationArrived"/>:
/// <list type="bullet">
///   <item>REAL <see cref="CommunicationFanOutTargetingService"/> (with REAL <see cref="CommunicationAccessFilter"/>
///     + REAL <see cref="DenyAllThreadPrivateGrantProvider"/>) — the eligible-recipient composition;</item>
///   <item>REAL <see cref="OutboxService"/> — the durable Layer-B write (query/serialize construction exercised);</item>
///   <item>a recording <see cref="SignalRDeliveryService"/> double — captures the live ping WITHOUT a real
///     Azure SignalR resource (ADR-038 "mock at module boundaries"); ONLY the Dataverse boundary
///     (<see cref="IGenericEntityService"/>) is doubled.</item>
/// </list>
/// Proves the FR-22 emit CONTRACT: (1) a new communication drives a communication-arrived emit to the eligible
/// recipient (SC-10); (2) write-before-ping (the ping carries the outbox row id the write returned); (3) the
/// envelope is AWARENESS-ONLY — IDs + display metadata + badgeDelta, NEVER a message body / content / token
/// (NFR-02/03 — the spine is not the content channel).
/// </summary>
public sealed class CommunicationArrivalNotifierSeamTests
{
    private const int PrivacyOpen = 100000000;

    // Substrings that would signal a message body / privileged content / action token slipped onto the wire.
    private static readonly string[] ForbiddenEnvelopeSubstrings =
    {
        "\"body\"", "\"content\"", "\"messageBody\"", "\"token\"", "\"authorization\"", "\"html\"", "\"payload\"",
    };

    // ── recording SignalR double: captures pings, no live Azure SignalR resource ──────────────────────────
    private sealed class RecordingSignalRDeliveryService : SignalRDeliveryService
    {
        public readonly List<(Guid OutboxRowId, Guid Recipient, NotificationKind Kind)> Pings = new();

        public RecordingSignalRDeliveryService()
            : base(identityResolver: null, logger: NullLogger<SignalRDeliveryService>.Instance)
        {
        }

        public override Task PingUserAsync(Guid outboxRowId, Guid recipientSystemUserId, NotificationKind kind, CancellationToken ct = default)
        {
            Pings.Add((outboxRowId, recipientSystemUserId, kind));
            return Task.CompletedTask;
        }
    }

    // ── seam wiring ───────────────────────────────────────────────────────────────────────────────────────
    private static (CommunicationArrivalNotifier Sut, RecordingSignalRDeliveryService Signal, List<DataverseEntity> OutboxRows)
        CreateService(
            DataverseEntity message,
            DataverseEntity? thread,
            IReadOnlyList<DataverseEntity> junctionRows,
            IReadOnlyCollection<Guid>? externalSystemUsers = null)
    {
        var outboxRows = new List<DataverseEntity>();

        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);

        // Message retrieve.
        entity
            .Setup(s => s.RetrieveAsync("sprk_communication", message.Id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Thread retrieve (only when a thread is supplied).
        if (thread is not null)
        {
            entity
                .Setup(s => s.RetrieveAsync("sprk_communicationthread", thread.Id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(thread);
        }

        // Participant junction read (fan-out candidate set).
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationparticipant"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(junctionRows.ToList()));

        // Outbox write — capture the row + return a fresh id (the write-before-ping token).
        entity
            .Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_notificationoutbox"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity e, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                e.Id = id;
                outboxRows.Add(e);
                return id;
            });

        var external = externalSystemUsers ?? Array.Empty<Guid>();
        var resolver = new Mock<ISystemUserIdentityResolver>(MockBehavior.Strict);
        resolver
            .Setup(r => r.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => Task.FromResult(external.Contains(id)));

        var fanOut = new CommunicationFanOutTargetingService(
            entity.Object,
            new CommunicationAccessFilter(NullLogger<CommunicationAccessFilter>.Instance),
            new DenyAllThreadPrivateGrantProvider(),
            resolver.Object,
            NullLogger<CommunicationFanOutTargetingService>.Instance);

        var outbox = new OutboxService(entity.Object, NullLogger<OutboxService>.Instance);
        var signal = new RecordingSignalRDeliveryService();

        var sut = new CommunicationArrivalNotifier(
            entity.Object, fanOut, outbox, signal, NullLogger<CommunicationArrivalNotifier>.Instance);

        return (sut, signal, outboxRows);
    }

    // ── entity builders ────────────────────────────────────────────────────────────────────────────────────
    private static DataverseEntity Message(Guid id, Guid threadId, Guid? regardingMatterId = null) =>
        new("sprk_communication", id)
        {
            ["createdon"] = DateTime.UtcNow,
            ["sprk_isinternalonly"] = false,
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["sprk_from"] = "alice@firm.com",
            ["sprk_regardingmatter"] = regardingMatterId is { } m ? new EntityReference("sprk_matter", m) : null!,
        };

    private static DataverseEntity OpenThread(Guid id) =>
        new("sprk_communicationthread", id) { ["sprk_privacystate"] = new OptionSetValue(PrivacyOpen) };

    private static DataverseEntity SystemUserParticipant(Guid systemUserId) =>
        new("sprk_communicationparticipant")
        {
            ["sprk_isresolved"] = true,
            ["sprk_systemuser"] = new EntityReference("systemuser", systemUserId),
        };

    // ── (a) POSITIVE: a new communication drives a communication-arrived emit to the eligible recipient ─────
    [Fact]
    public async Task NotifyArrival_OpenThreadInternalParticipant_EmitsCommunicationArrivedToRecipient()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var (sut, signal, outboxRows) = CreateService(
            Message(communicationId, threadId, matterId),
            OpenThread(threadId),
            new[] { SystemUserParticipant(recipient) });

        await sut.NotifyArrivalAsync(communicationId);

        // Durable outbox row written for the recipient, kind = communication-arrived.
        outboxRows.Should().ContainSingle();
        var row = outboxRows[0];
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(recipient);
        row.GetAttributeValue<string>("sprk_kind").Should().Be("communication-arrived");

        // Live ping fired to the SAME recipient with communication-arrived.
        signal.Pings.Should().ContainSingle();
        signal.Pings[0].Recipient.Should().Be(recipient);
        signal.Pings[0].Kind.Should().Be(NotificationKind.CommunicationArrived);
    }

    // ── (b) write-before-ping: the ping carries the outbox row id the write returned ───────────────────────
    [Fact]
    public async Task NotifyArrival_Always_PingsWithTheOutboxRowIdTheWriteReturned()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var (sut, signal, outboxRows) = CreateService(
            Message(communicationId, threadId),
            OpenThread(threadId),
            new[] { SystemUserParticipant(recipient) });

        await sut.NotifyArrivalAsync(communicationId);

        signal.Pings.Should().ContainSingle();
        signal.Pings[0].OutboxRowId.Should().NotBe(Guid.Empty, "write-before-ping — an empty row id would mean ping-before-write");
        signal.Pings[0].OutboxRowId.Should().Be(outboxRows[0].Id, "the ping MUST carry the durable outbox row id the write returned");
    }

    // ── (c) AWARENESS-ONLY: the emitted envelope carries no message body / content / token (NFR-02/03) ─────
    [Fact]
    public async Task NotifyArrival_Always_EmitsEnvelopeWithNoMessageBodyOrContent()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var (sut, _, outboxRows) = CreateService(
            Message(communicationId, threadId, matterId),
            OpenThread(threadId),
            new[] { SystemUserParticipant(recipient) });

        await sut.NotifyArrivalAsync(communicationId);

        var envelopeJson = outboxRows.Should().ContainSingle().Subject.GetAttributeValue<string>("sprk_envelope");

        // Round-trips to a valid CommunicationEnvelope with the awareness fields set and Snippet null.
        var envelope = JsonSerializer.Deserialize<CommunicationEnvelope>(envelopeJson);
        envelope!.Kind.Should().Be(NotificationKind.CommunicationArrived);
        envelope.CommunicationId.Should().Be(communicationId);
        envelope.ThreadId.Should().Be(threadId);
        envelope.BadgeDelta.Should().Be(1);
        envelope.RegardingRecordId.Should().Be(matterId.ToString());
        envelope.Snippet.Should().BeNull("the spine is not the content channel — no snippet/body ships (NFR-03)");
        envelope.SenderDisplay.Should().NotContain("@", "sender is a DISPLAY token, never a routable address");

        // The serialized wire form contains no body/content/token field at all (NFR-02).
        foreach (var forbidden in ForbiddenEnvelopeSubstrings)
        {
            envelopeJson.Should().NotContain(forbidden, $"the awareness envelope must not carry {forbidden} (NFR-02/03)");
        }
    }

    // ── (d) no eligible recipients → no emit (fan-out excludes the external user; nothing to signal) ───────
    [Fact]
    public async Task NotifyArrival_InternalOnlyMessageWithNoInternalRecipient_EmitsNothing()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var externalLicensedUser = Guid.NewGuid(); // a systemuser but sprk_isexternal = true

        var message = Message(communicationId, threadId);
        message["sprk_isinternalonly"] = true; // internal-only message

        var (sut, signal, outboxRows) = CreateService(
            message,
            OpenThread(threadId),
            new[] { SystemUserParticipant(externalLicensedUser) },
            externalSystemUsers: new[] { externalLicensedUser });

        await sut.NotifyArrivalAsync(communicationId);

        outboxRows.Should().BeEmpty("an internal-only message reaches no external-licensed systemuser → nothing to emit");
        signal.Pings.Should().BeEmpty();
    }

    // ── (e) no resolved thread → skip quietly (never throws; content poll still surfaces it) ───────────────
    [Fact]
    public async Task NotifyArrival_MessageWithNoResolvedThread_EmitsNothingAndDoesNotThrow()
    {
        var communicationId = Guid.NewGuid();
        var message = new DataverseEntity("sprk_communication", communicationId)
        {
            ["createdon"] = DateTime.UtcNow,
            ["sprk_isinternalonly"] = false,
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["sprk_from"] = "alice@firm.com",
            // no sprk_communicationthread lookup
        };

        var (sut, signal, outboxRows) = CreateService(message, thread: null, junctionRows: Array.Empty<DataverseEntity>());

        var act = async () => await sut.NotifyArrivalAsync(communicationId);

        await act.Should().NotThrowAsync();
        outboxRows.Should().BeEmpty();
        signal.Pings.Should().BeEmpty();
    }
}
