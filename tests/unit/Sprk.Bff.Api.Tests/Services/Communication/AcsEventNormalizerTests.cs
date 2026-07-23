using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="AcsEventNormalizer"/> — the ACS analog of <c>GraphMessageNormalizer</c> and the SINGLE
/// ACS→envelope boundary (task 031 / FR-02 / ADR-045). These protect the mapping contract that would regress
/// silently: an ACS <c>ChatMessageReceivedInThread</c> event maps to an INBOUND <see cref="NormalizedMessage"/>
/// whose <see cref="NormalizedMessage.ConversationId"/> is the ACS thread id, whose body/sender/compose-time come
/// off the event, and — because the mapper reads raw JSON rather than deserializing ACS SDK types — no
/// <c>Azure.Communication.*</c> type is produced downstream. Pure mapper: no mocks, no I/O.
/// </summary>
public class AcsEventNormalizerTests
{
    private const string ThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";
    private const string MessageId = "1784231661455"; // live ACS ids are numeric strings

    private static IncomingMessagingJobPayload PayloadFor(
        string messageId = MessageId,
        string threadId = ThreadId,
        string? senderDisplayName = "Alice Attorney",
        string? rawId = "8:acs:sender-raw",
        string? messageBody = "Hello from ACS",
        string? composeTime = "2026-07-16T12:00:01Z")
    {
        var evt = new Dictionary<string, object?>
        {
            ["id"] = "evt-1",
            ["topic"] = "/subscriptions/x/providers/Microsoft.Communication/communicationServices/spaarke-acs-dev",
            ["subject"] = $"thread/{threadId}",
            ["eventType"] = "Microsoft.Communication.ChatMessageReceivedInThread",
            ["eventTime"] = "2026-07-16T12:00:02Z",
            ["data"] = new Dictionary<string, object?>
            {
                ["messageId"] = messageId,
                ["threadId"] = threadId,
                ["messageBody"] = messageBody,
                ["messageType"] = "Text",
                ["senderDisplayName"] = senderDisplayName,
                ["senderCommunicationIdentifier"] = new Dictionary<string, object?> { ["rawId"] = rawId },
                ["composeTime"] = composeTime,
            }
        };

        return new IncomingMessagingJobPayload
        {
            AcsMessageId = messageId,
            AcsThreadId = threadId,
            EventType = "Microsoft.Communication.ChatMessageReceivedInThread",
            EventId = "evt-1",
            EventTime = DateTimeOffset.Parse("2026-07-16T12:00:02Z"),
            RawEvent = JsonSerializer.SerializeToElement(evt),
        };
    }

    [Fact]
    public void Normalize_ChatMessageReceivedInThread_MapsToIncomingEnvelope_WithThreadIdAsConversationId()
    {
        var sut = new AcsEventNormalizer();

        var result = sut.Normalize(PayloadFor());

        result.Direction.Should().Be(CommunicationDirection.Incoming);
        result.ConversationId.Should().Be(ThreadId);
        result.BodyText.Should().Be("Hello from ACS");
        result.BodyHtml.Should().BeNull();
        result.From.Should().Be("Alice Attorney");
        result.SentAt.Should().Be(DateTimeOffset.Parse("2026-07-16T12:00:01Z"));
        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_WhenNoDisplayName_FallsBackToRawAcsIdentifier()
    {
        var sut = new AcsEventNormalizer();

        var result = sut.Normalize(PayloadFor(senderDisplayName: null, rawId: "8:acs:only-raw"));

        result.From.Should().Be("8:acs:only-raw");
    }

    [Fact]
    public void Normalize_WhenComposeTimeAbsent_FallsBackToEventGridEventTime()
    {
        var sut = new AcsEventNormalizer();

        var result = sut.Normalize(PayloadFor(composeTime: null));

        // composeTime missing → SentAt falls back to the payload's Event Grid eventTime.
        result.SentAt.Should().Be(DateTimeOffset.Parse("2026-07-16T12:00:02Z"));
    }
}
