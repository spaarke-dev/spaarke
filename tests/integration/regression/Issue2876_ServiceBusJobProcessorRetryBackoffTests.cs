using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2876 (ISS-025) and #2600 (DEF-055): <see cref="ServiceBusJobProcessor"/> abandoned a message whose handler returned a retryable
/// Failure, so the broker redelivered it at once; all <see cref="JobContract.MaxDeliveryCount"/> deliveries were spent within seconds
/// and a dependency outage of a minute dead-lettered the job. The processor now sends a copy of the message scheduled
/// <see cref="JobContract.RetryDelay"/> later, carrying the logical delivery count in
/// <see cref="JobContract.PriorDeliveryCountProperty"/>, and then completes the original.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, regression). Drives the REAL processor's message callback with a real handler resolved from DI; only the
/// Service Bus boundary is doubled (<see cref="ServiceBusClient"/>/<see cref="ServiceBusSender"/> and the settlement calls of
/// <see cref="ProcessMessageEventArgs"/>), and time is a <see cref="FakeTimeProvider"/>. Fail-before: the old processor abandons the
/// message and sends nothing, and a handler sees the broker's count rather than the logical one.
/// </remarks>
public sealed class Issue2876_ServiceBusJobProcessorRetryBackoffTests
{
    private const string QueueName = "sdap-jobs";
    private const string JobType = "iss025-test-job";
    private const string IdempotencyKey = "iss025-idempotency-key";
    private const string CorrelationId = "iss025-correlation";
    private const string SessionId = "iss025-session";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "A retryable failure on the first delivery sends a copy scheduled 1 minute later with delivery count 1, then completes the original")]
    public async Task RetryableFailure_SchedulesCopy_ThenCompletesOriginal()
    {
        var h = new Harness(_ => HandlerResult.Fail);

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        var copy = h.Sent.Should().ContainSingle().Subject;
        copy.ScheduledEnqueueTime.Should().Be(Now + TimeSpan.FromMinutes(1));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(1);
        h.Calls.Should().Equal("send", "complete");
        h.Args.Verify(a => a.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "The back-off grows with the logical delivery: 1, 2, 4, 8 minutes, and the copy carries the delivery it follows")]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 4)]
    [InlineData(3, 1, 8)]
    [InlineData(0, 3, 4)] // an original redelivered by the broker (e.g. after a fallback abandon) counts its own deliveries
    public async Task Backoff_GrowsWithLogicalDelivery(int priorDeliveries, int brokerDeliveryCount, int expectedMinutes)
    {
        var h = new Harness(_ => HandlerResult.Fail);

        await h.ProcessAsync(h.Message(brokerDeliveryCount, priorDeliveries));

        var copy = h.Sent.Should().ContainSingle().Subject;
        copy.ScheduledEnqueueTime.Should().Be(Now + TimeSpan.FromMinutes(expectedMinutes));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(priorDeliveries + brokerDeliveryCount);
        h.Calls.Should().Equal("send", "complete");
    }

    [Fact(DisplayName = "The schedule: 1, 2, 4, 8 minutes for deliveries 1-4, capped at 15, a 15-minute window in all")]
    public void Schedule_IsExponentialWithACap()
    {
        Enumerable.Range(1, 4).Select(JobContract.RetryDelay).Should()
            .Equal(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8));
        JobContract.RetryDelay(5).Should().Be(TimeSpan.FromMinutes(15));
        JobContract.RetryDelay(40).Should().Be(TimeSpan.FromMinutes(15));
        JobContract.RetryDelay(0).Should().Be(TimeSpan.FromMinutes(1));
        JobContract.RetryWindow.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Theory(DisplayName = "The final logical delivery dead-letters MaxRetriesExceeded as before, also on a copy whose broker count is 1")]
    [InlineData(0, 5)] // the original, delivered five times by the broker (unchanged behaviour)
    [InlineData(4, 1)] // the fourth retry copy, on its first broker delivery
    public async Task FinalDelivery_DeadLettersMaxRetriesExceeded(int priorDeliveries, int brokerDeliveryCount)
    {
        var h = new Harness(_ => HandlerResult.Fail);

        await h.ProcessAsync(h.Message(brokerDeliveryCount, priorDeliveries));

        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "MaxRetriesExceeded", "transient outage", It.IsAny<CancellationToken>()), Times.Once);
        h.Sent.Should().BeEmpty();
        h.Calls.Should().Equal("deadletter");
    }

    [Fact(DisplayName = "A poisoned outcome dead-letters Poisoned on the first delivery as before, with no retry copy")]
    public async Task Poisoned_DeadLettersPoisoned()
    {
        var h = new Harness(_ => HandlerResult.Poison);

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "Poisoned", "bad payload", It.IsAny<CancellationToken>()), Times.Once);
        h.Sent.Should().BeEmpty();
        h.Calls.Should().Equal("deadletter");
    }

    [Fact(DisplayName = "The copy keeps the body, MessageId, CorrelationId, SessionId, Subject, ContentType and every application property")]
    public async Task Copy_PreservesMessage()
    {
        var h = new Harness(_ => HandlerResult.Fail);
        var original = h.Message(brokerDeliveryCount: 2, priorDeliveries: 1);

        await h.ProcessAsync(original);

        var copy = h.Sent.Should().ContainSingle().Subject;
        copy.Body.ToString().Should().Be(original.Body.ToString());
        copy.MessageId.Should().Be(IdempotencyKey);
        copy.CorrelationId.Should().Be(CorrelationId);
        copy.SessionId.Should().Be(SessionId);
        copy.Subject.Should().Be(JobType);
        copy.ContentType.Should().Be("application/json");
        copy.ApplicationProperties["JobType"].Should().Be(JobType);
        copy.ApplicationProperties["Attempt"].Should().Be(1);
        copy.ApplicationProperties["CreatedAt"].Should().Be("2026-11-24T08:00:00.0000000+00:00");
        copy.ApplicationProperties["Custom"].Should().Be("kept");
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(3);
        copy.ApplicationProperties.Should().HaveCount(5);
    }

    [Fact(DisplayName = "A failed send falls back to abandoning the original (immediate redelivery) and does not complete it")]
    public async Task SendFails_FallsBackToAbandon()
    {
        var h = new Harness(_ => HandlerResult.Fail) { SendFault = new ServiceBusException("send refused", ServiceBusFailureReason.ServiceBusy) };

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        h.Calls.Should().Equal("send", "abandon");
        h.Args.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Could not schedule the retry"));
    }

    [Fact(DisplayName = "A failed complete after the send is logged and does not throw; the original is neither abandoned nor dead-lettered")]
    public async Task CompleteFailsAfterSend_IsLoggedNotThrown()
    {
        var h = new Harness(_ => HandlerResult.Fail) { CompleteFault = new ServiceBusException("lock lost", ServiceBusFailureReason.MessageLockLost) };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("send", "complete");
        h.Sent.Should().ContainSingle();
        h.Args.Verify(a => a.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be completed") && e.Message.Contains("may run twice"));
    }

    [Theory(DisplayName = "A handler sees the logical delivery count and final-delivery flag on a retry copy")]
    [InlineData(2, 1, 3, false)]
    [InlineData(4, 1, 5, true)]
    [InlineData(0, 1, 1, false)]
    public async Task Handler_SeesLogicalDelivery(int priorDeliveries, int brokerDeliveryCount, int expectedDelivery, bool expectedFinal)
    {
        var h = new Harness(_ => HandlerResult.Succeed);

        await h.ProcessAsync(h.Message(brokerDeliveryCount, priorDeliveries));

        var seen = h.Seen.Should().ContainSingle().Subject;
        seen.DeliveryCount.Should().Be(expectedDelivery);
        seen.IsFinalDelivery.Should().Be(expectedFinal);
        h.Calls.Should().Equal("complete");
    }

    [Fact(DisplayName = "An unexpected error before the third logical delivery is retried with back-off, like a handler failure")]
    public async Task UnexpectedError_BeforeThirdDelivery_SchedulesCopy()
    {
        var h = new Harness(_ => throw new InvalidOperationException("handler threw"));

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1, priorDeliveries: 1));

        var copy = h.Sent.Should().ContainSingle().Subject;
        copy.ScheduledEnqueueTime.Should().Be(Now + TimeSpan.FromMinutes(2));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(2);
        h.Calls.Should().Equal("send", "complete");
    }

    [Fact(DisplayName = "An unexpected error on the third logical delivery dead-letters ProcessingError as before, also on a copy")]
    public async Task UnexpectedError_OnThirdDelivery_DeadLettersProcessingError()
    {
        var h = new Harness(_ => throw new InvalidOperationException("handler threw"));

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1, priorDeliveries: 2));

        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "ProcessingError", "Unexpected error: handler threw", It.IsAny<CancellationToken>()), Times.Once);
        h.Sent.Should().BeEmpty();
    }

    [Theory(DisplayName = "The prior-delivery property is read as a whole number: absent, malformed or negative counts as 0")]
    [InlineData(null, 0)]
    [InlineData("abc", 0)]
    [InlineData(-3, 0)]
    [InlineData("2", 2)]
    [InlineData(4L, 4)]
    public void PriorDeliveryCount_IsReadDefensively(object? raw, int expected)
    {
        var properties = new Dictionary<string, object>();
        if (raw is not null)
        {
            properties[JobContract.PriorDeliveryCountProperty] = raw;
        }

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(properties: properties, deliveryCount: 1);

        ServiceBusJobProcessor.ReadPriorDeliveryCount(message).Should().Be(expected);
    }

    private enum HandlerResult
    {
        Succeed,
        Fail,
        Poison
    }

    private sealed record Observation(int DeliveryCount, bool IsFinalDelivery);

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class Harness
    {
        private readonly Func<JobContract, HandlerResult> _behaviour;
        private readonly FakeTimeProvider _time = new(Now);

        public Harness(Func<JobContract, HandlerResult> behaviour) => _behaviour = behaviour;

        public List<ServiceBusMessage> Sent { get; } = [];
        public List<string> Calls { get; } = [];
        public List<Observation> Seen { get; } = [];
        public ConcurrentQueue<LogEntry> Logs { get; } = new();
        public Exception? SendFault { get; init; }
        public Exception? CompleteFault { get; init; }
        public Mock<ProcessMessageEventArgs> Args { get; private set; } = null!;

        public ServiceBusReceivedMessage Message(int brokerDeliveryCount, int? priorDeliveries = null)
        {
            var job = new JobContract
            {
                JobId = Guid.Parse("2876aaaa-0000-0000-0000-000000000025"),
                JobType = JobType,
                SubjectId = "subject-1",
                CorrelationId = CorrelationId,
                IdempotencyKey = IdempotencyKey,
                CreatedAt = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero)
            };
            var body = JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // The properties JobSubmissionService sets, plus one a sender might add.
            var properties = new Dictionary<string, object>
            {
                ["JobType"] = JobType,
                ["Attempt"] = 1,
                ["CreatedAt"] = job.CreatedAt.ToString("o"),
                ["Custom"] = "kept"
            };
            if (priorDeliveries is { } prior)
            {
                properties[JobContract.PriorDeliveryCountProperty] = prior;
            }

            return ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString(body),
                messageId: IdempotencyKey,
                sessionId: SessionId,
                correlationId: CorrelationId,
                subject: JobType,
                contentType: "application/json",
                properties: properties,
                deliveryCount: brokerDeliveryCount);
        }

        public async Task ProcessAsync(ServiceBusReceivedMessage message)
        {
            var sender = new Mock<ServiceBusSender>();
            sender.Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                .Returns((ServiceBusMessage m, CancellationToken _) =>
                {
                    Calls.Add("send");
                    if (SendFault is not null)
                    {
                        throw SendFault;
                    }

                    Sent.Add(m);
                    return Task.CompletedTask;
                });

            var client = new Mock<ServiceBusClient>();
            client.Setup(c => c.CreateSender(QueueName)).Returns(sender.Object);

            Args = new Mock<ProcessMessageEventArgs>(message, new Mock<ServiceBusReceiver>().Object, CancellationToken.None);
            Args.Setup(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Calls.Add("complete");
                    return CompleteFault is null ? Task.CompletedTask : throw CompleteFault;
                });
            Args.Setup(a => a.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Calls.Add("abandon");
                    return Task.CompletedTask;
                });
            Args.Setup(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Calls.Add("deadletter");
                    return Task.CompletedTask;
                });

            var services = new ServiceCollection();
            services.AddScoped<IJobHandler>(_ => new TestHandler(this));
            using var provider = services.BuildServiceProvider();

            var processor = new ServiceBusJobProcessor(
                client.Object,
                provider,
                new CapturingLogger(Logs),
                Options.Create(new ServiceBusOptions { QueueName = QueueName, MaxConcurrentCalls = 1 }),
                _time);

            await processor.ProcessMessageAsync(Args.Object);
        }

        private sealed class TestHandler(Harness harness) : IJobHandler
        {
            public string JobType => Issue2876_ServiceBusJobProcessorRetryBackoffTests.JobType;

            public Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
            {
                harness.Seen.Add(new Observation(job.DeliveryCount, job.IsFinalDelivery));
                return Task.FromResult(harness._behaviour(job) switch
                {
                    HandlerResult.Succeed => JobOutcome.Success(job.JobId, JobType, TimeSpan.Zero),
                    HandlerResult.Poison => JobOutcome.Poisoned(job.JobId, JobType, "bad payload", job.Attempt, TimeSpan.Zero),
                    _ => JobOutcome.Failure(job.JobId, JobType, "transient outage", job.Attempt, TimeSpan.Zero)
                });
            }
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger<ServiceBusJobProcessor>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
    }
}
