using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="AcsEventGridIngressService"/> — the public ACS Event Grid webhook ingress
/// (task 030 / FR-02). These protect the SECURITY BOUNDARY that would regress silently: only a validated
/// delivery enqueues. Specifically: (1) the subscription-validation handshake echoes the validationCode and
/// enqueues nothing; (2) a validated ChatMessageReceivedInThread enqueues EXACTLY ONE job onto the existing
/// communication queue carrying the ACS message id + thread id task 031 needs; (3) a wrong-topic delivery,
/// (4) a malformed payload, (5) an unconfigured allow-list (fail-closed), and (6) a bad shared secret each
/// enqueue NOTHING. The Service Bus boundary (<see cref="JobSubmissionService"/>) is mocked — its
/// <c>SubmitCommunicationJobAsync</c> is the honest module seam; nothing else observes the enqueue.
/// </summary>
public class AcsEventGridIngressServiceTests
{
    private const string Topic =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-spaarke-dev/providers/Microsoft.Communication/communicationServices/spaarke-acs-dev";
    private const string ThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";
    private const string AcsMessageId = "1784231661455"; // live ACS ids are numeric strings, not GUIDs

    private readonly Mock<JobSubmissionService> _jobSubmissionMock;
    private readonly List<JobContract> _enqueued = new();

    public AcsEventGridIngressServiceTests()
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            CommunicationQueueName = "test-comms",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });

        _jobSubmissionMock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);

        _jobSubmissionMock
            .Setup(j => j.SubmitCommunicationJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => _enqueued.Add(job))
            .Returns(Task.CompletedTask);
    }

    private AcsEventGridIngressService CreateSut(
        string[]? allowedTopics = null, string? validationKey = null)
    {
        var monitor = new Mock<IOptionsMonitor<AcsEventGridIngressOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new AcsEventGridIngressOptions
        {
            AllowedTopics = allowedTopics ?? new[] { Topic },
            ValidationKey = validationKey
        });

        return new AcsEventGridIngressService(
            _jobSubmissionMock.Object, monitor.Object, Mock.Of<ILogger<AcsEventGridIngressService>>());
    }

    private static string SubscriptionValidationBatch(string validationCode) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "val-1",
                topic = Topic,
                eventType = "Microsoft.EventGrid.SubscriptionValidationEvent",
                eventTime = "2026-07-16T12:00:00Z",
                data = new { validationCode, validationUrl = "https://rp-eastus.eventgrid.azure.net/..." }
            }
        });

    private static string ChatMessageReceivedBatch(string topic = Topic, string messageId = AcsMessageId) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "evt-1",
                topic,
                subject = $"thread/{ThreadId}",
                eventType = "Microsoft.Communication.ChatMessageReceivedInThread",
                eventTime = "2026-07-16T12:00:01Z",
                data = new
                {
                    messageId,
                    threadId = ThreadId,
                    messageBody = "Hello from ACS",
                    messageType = "Text",
                    senderCommunicationIdentifier = new { rawId = "8:acs:sender" }
                }
            }
        });

    [Fact]
    public async Task ProcessAsync_SubscriptionValidationEvent_EchoesValidationCode_AndEnqueuesNothing()
    {
        var sut = CreateSut();

        var outcome = await sut.ProcessAsync(SubscriptionValidationBatch("ABC-123-VALIDATE"), providedSecret: null);

        outcome.Action.Should().Be(AcsEventGridIngressAction.ValidationHandshake);
        outcome.ValidationCode.Should().Be("ABC-123-VALIDATE");
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ChatMessageReceivedInThread_EnqueuesExactlyOneJob_CarryingAcsIds()
    {
        var sut = CreateSut();

        var outcome = await sut.ProcessAsync(ChatMessageReceivedBatch(), providedSecret: null);

        outcome.Action.Should().Be(AcsEventGridIngressAction.Accepted);
        outcome.EnqueuedCount.Should().Be(1);
        _enqueued.Should().ContainSingle();

        var job = _enqueued.Single();
        job.JobType.Should().Be(AcsEventGridIngressService.JobTypeIncomingMessaging);
        job.Payload.Should().NotBeNull();

        var payload = JsonSerializer.Deserialize<IncomingMessagingJobPayload>(
            job.Payload!.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        payload.AcsMessageId.Should().Be(AcsMessageId);
        payload.AcsThreadId.Should().Be(ThreadId);
        payload.EventType.Should().Be("Microsoft.Communication.ChatMessageReceivedInThread");
        payload.Topic.Should().Be(Topic);
        // Idempotency key seeds Service Bus duplicate detection off the ACS message id (defense in depth; full
        // echo-dedup is task 031). It must include the ACS message id so 031 can dedupe on sprk_acsmessageid.
        job.IdempotencyKey.Should().Contain(AcsMessageId);
    }

    [Fact]
    public async Task ProcessAsync_WrongTopic_DoesNotEnqueue_AndRejects()
    {
        var sut = CreateSut(allowedTopics: new[] { Topic });

        var outcome = await sut.ProcessAsync(
            ChatMessageReceivedBatch(topic: "/subscriptions/x/providers/Microsoft.Communication/communicationServices/attacker-acs"),
            providedSecret: null);

        outcome.Action.Should().Be(AcsEventGridIngressAction.Rejected);
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_MalformedJson_ReturnsBadRequest_AndEnqueuesNothing()
    {
        var sut = CreateSut();

        var outcome = await sut.ProcessAsync("{ not valid json ", providedSecret: null);

        outcome.Action.Should().Be(AcsEventGridIngressAction.BadRequest);
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_AllowedTopicsUnconfigured_FailsClosed_AndEnqueuesNothing()
    {
        var sut = CreateSut(allowedTopics: Array.Empty<string>());

        var outcome = await sut.ProcessAsync(ChatMessageReceivedBatch(), providedSecret: null);

        outcome.Action.Should().Be(AcsEventGridIngressAction.Misconfigured);
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_InvalidSharedSecret_WhenValidationKeyConfigured_Rejects_AndEnqueuesNothing()
    {
        var sut = CreateSut(validationKey: "the-real-secret");

        var outcome = await sut.ProcessAsync(ChatMessageReceivedBatch(), providedSecret: "wrong-secret");

        outcome.Action.Should().Be(AcsEventGridIngressAction.Rejected);
        _enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ValidSharedSecret_WhenValidationKeyConfigured_EnqueuesJob()
    {
        var sut = CreateSut(validationKey: "the-real-secret");

        var outcome = await sut.ProcessAsync(ChatMessageReceivedBatch(), providedSecret: "the-real-secret");

        outcome.Action.Should().Be(AcsEventGridIngressAction.Accepted);
        _enqueued.Should().ContainSingle();
    }
}
