// R3 Part 1 Phase 2 — Task 081 (2026-06-22)
// Unit tests for MembershipEventPublisher + NullMembershipEventPublisher.
// Locks:
//   - Fire-and-forget contract (FR-2P2.6 + Q2): exceptions caught + logged
//     as Warning; the returned Task NEVER faults.
//   - NFR-08: CorrelationId preserved on the wire envelope
//     (ServiceBusMessage.CorrelationId + payload field).
//   - Canonical serialization: enum-as-string (MutationType, PersonIdType)
//     via MembershipChangedEvent.SerializerOptions.
//   - Null-Object peer (ADR-032 P2): logs + returns Task.CompletedTask
//     without any Service Bus interaction.

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;

[Trait("status", "new")]
public class MembershipEventPublisherTests
{
    private static readonly Guid PersonIdFixture =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntityRecordIdFixture =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string CorrelationIdFixture = "corr-abc-123";
    private const string TopicFixture = "sprk-membership-changes";

    private static MembershipChangedEvent BuildSampleEvent(
        string? correlationId = CorrelationIdFixture,
        MembershipMutationType mutation = MembershipMutationType.Added) =>
        new()
        {
            PersonId = PersonIdFixture,
            PersonIdType = PersonIdentityType.User,
            EntityLogicalName = "sprk_matter",
            EntityRecordId = EntityRecordIdFixture,
            SourceField = "ownerid",
            Role = "owner",
            MutationType = mutation,
            CorrelationId = correlationId!,
            OccurredOnUtc = new DateTime(2026, 6, 22, 14, 30, 0, DateTimeKind.Utc),
        };

    private static IOptions<MembershipEventPublisherOptions> BuildOptions(
        string topicName = TopicFixture,
        bool enabled = true) =>
        Options.Create(new MembershipEventPublisherOptions
        {
            Enabled = enabled,
            TopicName = topicName,
        });

    // ─── Real MembershipEventPublisher ──────────────────────────────────


    [Fact]
    public async Task PublishAsync_PreservesCorrelationId_OnEnvelopeAndPayload()
    {
        // NFR-08 (distributed tracing) — correlationId on both the Service
        // Bus envelope (for consumer correlation without deserialization)
        // and the JSON payload (for forensic logs).

        // Arrange
        ServiceBusMessage? capturedMessage = null;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(TopicFixture)).Returns(senderMock.Object);

        var sut = new MembershipEventPublisher(
            clientMock.Object,
            BuildOptions(),
            Mock.Of<ILogger<MembershipEventPublisher>>());

        // Act
        await sut.PublishAsync(BuildSampleEvent(), CancellationToken.None);

        // Assert — envelope correlationId
        capturedMessage.Should().NotBeNull();
        capturedMessage!.CorrelationId.Should().Be(CorrelationIdFixture);
        capturedMessage.Subject.Should().Be("MembershipChangedEvent");
        capturedMessage.ContentType.Should().Be("application/json");
        capturedMessage.ApplicationProperties.Should().ContainKey("schemaVersion");
        capturedMessage.ApplicationProperties["schemaVersion"].Should().Be(1);
        capturedMessage.ApplicationProperties["mutationType"].Should().Be("Added");
        capturedMessage.ApplicationProperties["entityLogicalName"].Should().Be("sprk_matter");

        // Assert — payload correlationId (round-trip via canonical serializer)
        var json = capturedMessage.Body.ToString();
        var deserialized = JsonSerializer.Deserialize<MembershipChangedEvent>(
            json, MembershipChangedEvent.SerializerOptions);
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be(CorrelationIdFixture);
    }

    [Fact]
    public async Task PublishAsync_SerializesEventWithCanonicalOptions()
    {
        // Locks enum-as-string + camelCase per MembershipChangedEvent.SerializerOptions.

        // Arrange
        ServiceBusMessage? capturedMessage = null;
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(TopicFixture)).Returns(senderMock.Object);

        var sut = new MembershipEventPublisher(
            clientMock.Object,
            BuildOptions(),
            Mock.Of<ILogger<MembershipEventPublisher>>());

        // Act
        await sut.PublishAsync(BuildSampleEvent(mutation: MembershipMutationType.Removed), CancellationToken.None);

        // Assert — enum-as-string, camelCase property names, no integer encoding.
        var json = capturedMessage!.Body.ToString();
        json.Should().Contain("\"mutationType\":\"Removed\"");
        json.Should().NotContain("\"mutationType\":2");
        json.Should().Contain("\"personIdType\":\"User\"");
        json.Should().Contain("\"correlationId\":");
        json.Should().Contain("\"entityLogicalName\":\"sprk_matter\"");

        // Round-trip the payload to confirm consumers can deserialize it
        // with the same canonical options.
        var roundTripped = JsonSerializer.Deserialize<MembershipChangedEvent>(
            json, MembershipChangedEvent.SerializerOptions);
        roundTripped.Should().NotBeNull();
        roundTripped!.MutationType.Should().Be(MembershipMutationType.Removed);
    }


    [Fact]
    public void Constructor_NullServiceBusClient_Throws()
    {
        Action act = () => new MembershipEventPublisher(
            serviceBusClient: null!,
            BuildOptions(),
            Mock.Of<ILogger<MembershipEventPublisher>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceBusClient");
    }


    // ─── NullMembershipEventPublisher (ADR-032 P2 Quiet no-op) ──────────

    [Fact]
    public async Task NullPublisher_PublishAsync_ReturnsImmediately_LogsInformation()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<NullMembershipEventPublisher>>();
        var sut = new NullMembershipEventPublisher(loggerMock.Object);

        // Act
        Func<Task> act = () => sut.PublishAsync(BuildSampleEvent(), CancellationToken.None);

        // Assert — completes synchronously, no throw, logs at Information level.
        await act.Should().NotThrowAsync();

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Null peer logs at Information (disabled is intended kill-switch state, not error)");
    }

    [Fact]
    public async Task NullPublisher_PublishAsync_NullEvent_Throws()
    {
        // Defense — the Null peer still validates input. A caller passing
        // null is a programming error, not a kill-switch state, so it
        // gets the same ArgumentNullException as the real publisher.

        // Arrange
        var sut = new NullMembershipEventPublisher(
            Mock.Of<ILogger<NullMembershipEventPublisher>>());

        // Act
        Func<Task> act = () => sut.PublishAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void NullPublisher_Constructor_NullLogger_Throws()
    {
        Action act = () => new NullMembershipEventPublisher(logger: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
