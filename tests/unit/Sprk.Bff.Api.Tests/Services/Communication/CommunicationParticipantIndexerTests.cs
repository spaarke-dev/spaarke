using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="CommunicationParticipantIndexer"/> — the write side of the queryable participant
/// index (task 050 / FR-08 / ADR-048). These protect the coupled write invariants that would regress silently:
/// (1) a resolved person sets EXACTLY ONE typed lookup (systemuser XOR contact) + isresolved=true; (2) an
/// unresolved external address writes a first-class row (both lookups null, isresolved=false, addresstext set,
/// Q-D) — never skipped; (3) NO row ever sets both person lookups; (4) the write is best-effort/non-fatal (a
/// junction-write failure never propagates to fail the send or drop the captured message, NFR-02); and (5) the
/// write is idempotent per message (re-processing writes no duplicate rows). Resolution REUSES
/// <see cref="ICommunicationDataverseService.QueryContactByEmailAsync"/> (no second resolver). The Dataverse
/// boundary (<see cref="IGenericEntityService"/> + the resolver) is mocked at the module boundary — the row
/// side effects are the only observable surface (the junction schema live-apply is owner-deferred, task 003).
/// </summary>
public class CommunicationParticipantIndexerTests
{
    private const int RoleFrom = 100000000;
    private const int RoleTo = 100000001;
    private const int RoleCc = 100000002;

    private static CommunicationParticipantIndexer CreateSut(
        Mock<IGenericEntityService> generic,
        Mock<ICommunicationDataverseService>? resolver = null) =>
        new(
            (resolver ?? new Mock<ICommunicationDataverseService>()).Object,
            generic.Object,
            Mock.Of<ILogger<CommunicationParticipantIndexer>>());

    /// <summary>
    /// A generic-entity mock that captures created rows and returns an EMPTY existing-row set (so idempotency
    /// pre-query is a no-op unless a test overrides it). Without this, RetrieveMultipleAsync returns null and
    /// the top-level best-effort guard swallows the whole write — the default must be "no existing rows".
    /// </summary>
    private static Mock<IGenericEntityService> CapturingGeneric(List<Entity> created, EntityCollection? existing = null)
    {
        var generic = new Mock<IGenericEntityService>();
        generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        generic
            .Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new EntityCollection());
        return generic;
    }

    private static Entity ContactRow(int role, string address)
    {
        var e = new Entity("sprk_communicationparticipant", Guid.NewGuid());
        e["sprk_role"] = new OptionSetValue(role);
        e["sprk_addresstext"] = address;
        return e;
    }

    // ── (2) resolved contact — email→contact resolver reused, contact lookup set, isresolved=true ──
    [Fact]
    public async Task WriteParticipantsAsync_ResolvedContactRecipient_SetsContactLookupAndIsResolvedTrue()
    {
        var contactId = Guid.NewGuid();
        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync("counsel@firm.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("contact", contactId));

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet { To = new[] { "counsel@firm.com" } },
            default);

        created.Should().ContainSingle();
        var row = created[0];
        ((EntityReference)row["sprk_contact"]).Id.Should().Be(contactId);
        ((EntityReference)row["sprk_contact"]).LogicalName.Should().Be("contact");
        row.Contains("sprk_systemuser").Should().BeFalse();
        row["sprk_isresolved"].Should().Be(true);
        row["sprk_addresstext"].Should().Be("counsel@firm.com");
        ((OptionSetValue)row["sprk_role"]).Value.Should().Be(RoleTo);
        ((EntityReference)row["sprk_communication"]).LogicalName.Should().Be("sprk_communication");
    }

    // ── (1) resolved systemuser — a caller-supplied typed identity sets systemuser lookup, no contact ──
    [Fact]
    public async Task WriteParticipantsAsync_PreResolvedSystemUserSender_SetsSystemUserLookupAndIsResolvedTrue()
    {
        var systemUserId = Guid.NewGuid();
        var resolver = new Mock<ICommunicationDataverseService>();
        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet
            {
                FromAddress = "agent@spaarke.com",
                FromResolved = ParticipantReference.SystemUser(systemUserId),
            },
            default);

        created.Should().ContainSingle();
        var row = created[0];
        ((EntityReference)row["sprk_systemuser"]).Id.Should().Be(systemUserId);
        ((EntityReference)row["sprk_systemuser"]).LogicalName.Should().Be("systemuser");
        row.Contains("sprk_contact").Should().BeFalse();
        row["sprk_isresolved"].Should().Be(true);
        ((OptionSetValue)row["sprk_role"]).Value.Should().Be(RoleFrom);
        // A pre-resolved sender is NOT looked up via the email→contact resolver.
        resolver.Verify(r => r.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── (2/Q-D) unresolved external address — row with both lookups null, isresolved=false, addresstext set ──
    [Fact]
    public async Task WriteParticipantsAsync_UnresolvedExternalAddress_WritesRowWithBothLookupsNullAndIsResolvedFalse()
    {
        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet { To = new[] { "stranger@external.com" } },
            default);

        created.Should().ContainSingle();
        var row = created[0];
        row.Contains("sprk_systemuser").Should().BeFalse();
        row.Contains("sprk_contact").Should().BeFalse();
        row["sprk_isresolved"].Should().Be(false);
        row["sprk_addresstext"].Should().Be("stranger@external.com"); // back-fillable + surfaced by participant=
    }

    // ── (3) NEGATIVE — no row ever sets BOTH person lookups ──
    [Fact]
    public async Task WriteParticipantsAsync_MixedResolution_NeverSetsBothPersonLookupsOnAnyRow()
    {
        var contactId = Guid.NewGuid();
        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync("known@firm.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("contact", contactId));
        resolver
            .Setup(r => r.QueryContactByEmailAsync("unknown@external.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet
            {
                FromAddress = "agent@spaarke.com",
                FromResolved = ParticipantReference.SystemUser(Guid.NewGuid()), // systemuser
                To = new[] { "known@firm.com" },                                 // contact
                Cc = new[] { "unknown@external.com" },                           // unresolved
            },
            default);

        created.Should().HaveCount(3);
        created.Should().OnlyContain(row => !(row.Contains("sprk_systemuser") && row.Contains("sprk_contact")));
    }

    // ── grain — one row per (message × address × role) with the message as the parent ──
    [Fact]
    public async Task WriteParticipantsAsync_WithFromToCc_WritesOneRowPerAddressAndRoleAtMessageGrain()
    {
        var communicationId = Guid.NewGuid();
        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            communicationId,
            new CommunicationParticipantSet
            {
                FromAddress = "from@x.com",
                To = new[] { "to1@x.com", "to2@x.com" },
                Cc = new[] { "cc1@x.com" },
            },
            default);

        created.Should().HaveCount(4);
        created.Should().OnlyContain(row => ((EntityReference)row["sprk_communication"]).Id == communicationId);
        created.Count(r => ((OptionSetValue)r["sprk_role"]).Value == RoleFrom).Should().Be(1);
        created.Count(r => ((OptionSetValue)r["sprk_role"]).Value == RoleTo).Should().Be(2);
        created.Count(r => ((OptionSetValue)r["sprk_role"]).Value == RoleCc).Should().Be(1);
    }

    // ── (4) best-effort / non-fatal — a junction-write exception never propagates ──
    [Fact]
    public async Task WriteParticipantsAsync_WhenCreateThrows_DoesNotThrow()
    {
        var generic = new Mock<IGenericEntityService>();
        generic
            .Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("junction write boom (e.g. entity not applied live yet)"));

        var sut = CreateSut(generic);

        // The indexer is the derived index; a write failure MUST NOT surface to the caller (NFR-02 / ADR-048).
        var act = async () => await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet { To = new[] { "someone@x.com" } },
            default);

        await act.Should().NotThrowAsync();
    }

    // ── (5) idempotent — re-processing a message writes no duplicate rows ──
    [Fact]
    public async Task WriteParticipantsAsync_WhenRowAlreadyExistsForRoleAndAddress_WritesOnlyTheMissingRow()
    {
        // Existing index already has the (To, already@x.com) row from a prior pass.
        var existing = new EntityCollection();
        existing.Entities.Add(ContactRow(RoleTo, "already@x.com"));

        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created, existing), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet { To = new[] { "already@x.com", "new@x.com" } },
            default);

        // Only the not-yet-present address is written; the duplicate is skipped.
        created.Should().ContainSingle();
        created[0]["sprk_addresstext"].Should().Be("new@x.com");
    }

    // ── (5) idempotent — case-insensitive de-dup within a single call ──
    [Fact]
    public async Task WriteParticipantsAsync_WithSameAddressTwiceInRole_WritesOneRow()
    {
        var resolver = new Mock<ICommunicationDataverseService>();
        resolver
            .Setup(r => r.QueryContactByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var created = new List<Entity>();
        var sut = CreateSut(CapturingGeneric(created), resolver);

        await sut.WriteParticipantsAsync(
            Guid.NewGuid(),
            new CommunicationParticipantSet { To = new[] { "dup@x.com", "DUP@X.com" } },
            default);

        created.Should().ContainSingle();
    }

    // ── capture path — the inbound chat ingestor writes participant rows and survives an indexer failure ──
    [Fact]
    public async Task MessagingIngestor_WithIndexer_WritesParticipantRowsForInboundMessage()
    {
        var created = new List<Entity>();
        var generic = new Mock<IGenericEntityService>();
        generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        generic
            .Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var indexer = new CommunicationParticipantIndexer(
            new Mock<ICommunicationDataverseService>().Object,
            generic.Object,
            Mock.Of<ILogger<CommunicationParticipantIndexer>>());

        var sut = new MessagingIngestor(
            generic.Object,
            new Mock<ICommunicationEnrichmentService>().Object,
            Mock.Of<ILogger<MessagingIngestor>>(),
            participantIndexer: indexer);

        await sut.IngestAsync(new ChannelIngestRequest
        {
            Message = new NormalizedMessage
            {
                Direction = CommunicationDirection.Incoming,
                From = "8:acs:sender-mri",
                To = new[] { "8:acs:recipient-mri" },
                Subject = "Kickoff",
                BodyText = "hi",
                SentAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            },
            ProviderMessageId = "1700000000123",
            ProviderThreadId = "19:acs:thread@thread.v2",
            CorrelationId = "corr-050",
        });

        // One sprk_communication + participant rows for From + To (ACS MRIs → unresolved, Q-D).
        var participantRows = created.Where(e => e.LogicalName == "sprk_communicationparticipant").ToList();
        participantRows.Should().HaveCount(2);
        participantRows.Should().OnlyContain(r => r["sprk_isresolved"].Equals(false));
        participantRows.Count(r => ((OptionSetValue)r["sprk_role"]).Value == RoleFrom).Should().Be(1);
        participantRows.Count(r => ((OptionSetValue)r["sprk_role"]).Value == RoleTo).Should().Be(1);
    }

    // ── capture path — an indexer failure never drops the captured message (best-effort at the call site) ──
    [Fact]
    public async Task MessagingIngestor_WhenParticipantWriteFails_StillReturnsPersistedMessageId()
    {
        var communicationId = Guid.NewGuid();
        var generic = new Mock<IGenericEntityService>();
        // The message persists; the participant-row create throws (e.g. junction not applied live yet).
        generic
            .Setup(g => g.CreateAsync(It.Is<Entity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(communicationId);
        generic
            .Setup(g => g.CreateAsync(It.Is<Entity>(e => e.LogicalName == "sprk_communicationparticipant"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("junction not applied live yet"));
        generic
            .Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var indexer = new CommunicationParticipantIndexer(
            new Mock<ICommunicationDataverseService>().Object,
            generic.Object,
            Mock.Of<ILogger<CommunicationParticipantIndexer>>());

        var sut = new MessagingIngestor(
            generic.Object,
            new Mock<ICommunicationEnrichmentService>().Object,
            Mock.Of<ILogger<MessagingIngestor>>(),
            participantIndexer: indexer);

        var result = await sut.IngestAsync(new ChannelIngestRequest
        {
            Message = new NormalizedMessage
            {
                Direction = CommunicationDirection.Incoming,
                From = "8:acs:sender-mri",
                To = new[] { "8:acs:recipient-mri" },
                Subject = "Kickoff",
                SentAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
            },
            ProviderMessageId = "1700000000999",
            CorrelationId = "corr-050b",
        });

        result.CommunicationId.Should().Be(communicationId); // NFR-02: capture not dropped by a junction failure
    }
}
