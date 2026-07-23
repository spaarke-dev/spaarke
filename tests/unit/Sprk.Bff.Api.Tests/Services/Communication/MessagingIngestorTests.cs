using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="MessagingIngestor"/> — the FIRST implementation of the net-new ADR-045 inbound
/// ingestor seam (task 021 / FR-02). These protect the branched contract that would regress silently:
/// (1) an inbound <see cref="NormalizedMessage"/> is persisted as exactly ONE <c>sprk_communication</c>
/// (type=Message, Direction=Incoming) with the ACS dedupe key mapped to the as-built <c>sprk_acsmessageid</c>,
/// and (2) enrichment is BEST-EFFORT — a thrown enrichment error MUST NOT fail the persist (NFR-02). The
/// canonical <see cref="IGenericEntityService"/> + <see cref="ICommunicationEnrichmentService"/> are mocked at
/// the module boundary (no live Dataverse/ACS is provisioned in R1); the persist side effect + the non-fatal
/// branch have no other observable surface.
/// </summary>
public class MessagingIngestorTests
{
    private const string AcsMessageId = "1700000000123";
    private const string AcsThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";

    private static ChannelIngestRequest MessageRequest(
        string? acsMessageId = AcsMessageId,
        string? acsThreadId = AcsThreadId) =>
        new()
        {
            Message = new NormalizedMessage
            {
                Direction = CommunicationDirection.Incoming,
                From = "8:acs:sender-mri",
                To = new[] { "8:acs:recipient-mri" },
                Subject = "Kickoff",
                BodyText = "hello there",
                SentAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z")
            },
            ProviderMessageId = acsMessageId,
            ProviderThreadId = acsThreadId,
            CorrelationId = "corr-021"
        };

    private static MessagingIngestor CreateSut(
        Mock<IGenericEntityService> genericEntityService,
        Mock<ICommunicationEnrichmentService>? enrichment = null) =>
        new(
            genericEntityService.Object,
            (enrichment ?? new Mock<ICommunicationEnrichmentService>()).Object,
            Mock.Of<ILogger<MessagingIngestor>>());

    [Fact]
    public void SupportedType_IsMessage()
    {
        var sut = CreateSut(new Mock<IGenericEntityService>());

        sut.SupportedType.Should().Be(CommunicationType.Message);
    }

    [Fact]
    public async Task IngestAsync_WithNormalizedMessage_PersistsExactlyOneIncomingMessageCommunication()
    {
        var newId = Guid.NewGuid();
        Entity? captured = null;
        var generic = new Mock<IGenericEntityService>();
        generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(newId);

        var enrichment = new Mock<ICommunicationEnrichmentService>();
        var sut = CreateSut(generic, enrichment);

        var result = await sut.IngestAsync(MessageRequest());

        // Persisted exactly one sprk_communication record, whose id is returned.
        generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        result.CommunicationId.Should().Be(newId);
        result.WasDuplicate.Should().BeFalse();

        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("sprk_communication");
        ((OptionSetValue)captured["sprk_communicationtype"]).Value.Should().Be((int)CommunicationType.Message);
        ((OptionSetValue)captured["sprk_direction"]).Value.Should().Be((int)CommunicationDirection.Incoming);
        // AS-BUILT dedupe key + denormalized thread id — the contract tasks 031/040 read.
        captured["sprk_acsmessageid"].Should().Be(AcsMessageId);
        captured["sprk_acsthreadid"].Should().Be(AcsThreadId);
        captured["sprk_subject"].Should().Be("Kickoff");

        // Shared enrichment entry point invoked once, inbound direction (not forked from the email path).
        enrichment.Verify(
            e => e.EnrichAsync(newId, CommunicationDirection.Incoming, It.IsAny<NormalizedMessage>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestAsync_WhenEnrichmentThrows_StillReturnsPersistedIdAndDoesNotFail()
    {
        var newId = Guid.NewGuid();
        var generic = new Mock<IGenericEntityService>();
        generic
            .Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        var enrichment = new Mock<ICommunicationEnrichmentService>();
        enrichment
            .Setup(e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        var sut = CreateSut(generic, enrichment);

        // NFR-02: enrichment failure MUST NOT fail the persist — the record already exists.
        var result = await sut.IngestAsync(MessageRequest());

        result.CommunicationId.Should().Be(newId);
        generic.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
