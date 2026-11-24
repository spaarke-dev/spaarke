using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2876 (ISS-025) and #2600 (DEF-055): <see cref="ServiceBusJobProcessor"/> abandoned a message whose handler returned a retryable
/// Failure, so the broker redelivered it at once; all <see cref="JobContract.MaxDeliveryCount"/> deliveries were spent within seconds
/// and a dependency outage of a minute dead-lettered the job. Both ADR-004 processors now schedule a copy of the message
/// <see cref="JobContract.RetryDelay"/> later on their own queue, carrying the logical delivery count in
/// <see cref="JobContract.PriorDeliveryCountProperty"/>, and then complete the original (<see cref="JobMessageRetry"/>).
/// Fix round 1: <see cref="CommunicationJobProcessor"/> sets the logical <see cref="JobContract.DeliveryCount"/> (it never set it, so
/// the inbound HOLD's final branch never ran and a held email was dead-lettered with no alert) and shares the retry; a failed
/// complete cancels the scheduled copy; settlement failures and a lost lock never become a retry copy.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, regression). Drives the REAL processors' message callbacks with handlers resolved from DI; only the Service
/// Bus boundary is doubled (<see cref="ServiceBusClient"/>/<see cref="ServiceBusSender"/> and the settlement calls of
/// <see cref="ProcessMessageEventArgs"/>), and time is a <see cref="FakeTimeProvider"/>. On the communication queue the handler is the
/// REAL <see cref="IncomingCommunicationJobHandler.HoldAsync"/> (with its real <see cref="NotificationService"/> over a capturing
/// entity service): its Graph-backed processing step is replaced by "the record's owner is unresolved", the HOLD's trigger, because
/// <see cref="IncomingCommunicationProcessor"/> is sealed and fetches from Graph. Fail-before: the old processors abandon the message
/// and schedule nothing, and a handler sees the broker's count (on the communication queue: none) rather than the logical one.
/// </remarks>
public sealed class Issue2876_ServiceBusJobProcessorRetryBackoffTests
{
    private const string JobsQueue = "sdap-jobs";
    private const string CommunicationQueue = "sdap-communication";
    private const string JobType = "iss025-test-job";
    private const string IdempotencyKey = "iss025-idempotency-key";
    private const string CorrelationId = "iss025-correlation";
    private const string SessionId = "iss025-session";
    private const long SequenceNumber = 4242;

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    // =====================================================================================
    // ServiceBusJobProcessor (sdap-jobs)
    // =====================================================================================

    [Fact(DisplayName = "A retryable failure on the first delivery schedules a copy 1 minute later on sdap-jobs with delivery count 1, then completes the original")]
    public async Task RetryableFailure_SchedulesCopy_ThenCompletesOriginal()
    {
        var h = new Harness(_ => HandlerResult.Fail);

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        var (copy, dueAt) = h.Scheduled.Should().ContainSingle().Subject;
        dueAt.Should().Be(Now + TimeSpan.FromMinutes(1));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(1);
        h.SenderQueues.Should().Equal(JobsQueue);
        h.Calls.Should().Equal("schedule", "complete");
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

        var (copy, dueAt) = h.Scheduled.Should().ContainSingle().Subject;
        dueAt.Should().Be(Now + TimeSpan.FromMinutes(expectedMinutes));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(priorDeliveries + brokerDeliveryCount);
        h.Calls.Should().Equal("schedule", "complete");
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
        h.Scheduled.Should().BeEmpty();
        h.Calls.Should().Equal("deadletter");
    }

    [Fact(DisplayName = "A poisoned outcome dead-letters Poisoned on the first delivery as before, with no retry copy")]
    public async Task Poisoned_DeadLettersPoisoned()
    {
        var h = new Harness(_ => HandlerResult.Poison);

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "Poisoned", "bad payload", It.IsAny<CancellationToken>()), Times.Once);
        h.Scheduled.Should().BeEmpty();
        h.Calls.Should().Equal("deadletter");
    }

    [Fact(DisplayName = "The copy keeps the body, MessageId, CorrelationId, SessionId, PartitionKey, Subject, ContentType, To, ReplyTo, TTL and every application property")]
    public async Task Copy_PreservesMessage()
    {
        var h = new Harness(_ => HandlerResult.Fail);
        var original = h.Message(brokerDeliveryCount: 2, priorDeliveries: 1);

        await h.ProcessAsync(original);

        var (copy, _) = h.Scheduled.Should().ContainSingle().Subject;
        copy.Body.ToString().Should().Be(original.Body.ToString());
        copy.MessageId.Should().Be(IdempotencyKey);
        copy.CorrelationId.Should().Be(CorrelationId);
        copy.SessionId.Should().Be(SessionId);
        copy.PartitionKey.Should().Be(SessionId);
        copy.Subject.Should().Be(JobType);
        copy.ContentType.Should().Be("application/json");
        copy.To.Should().Be("iss025-to");
        copy.ReplyTo.Should().Be("iss025-reply-to");
        copy.TimeToLive.Should().Be(TimeSpan.FromDays(3));
        copy.ApplicationProperties["JobType"].Should().Be(JobType);
        copy.ApplicationProperties["Attempt"].Should().Be(1);
        copy.ApplicationProperties["CreatedAt"].Should().Be("2026-11-24T08:00:00.0000000+00:00");
        copy.ApplicationProperties["Custom"].Should().Be("kept");
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(3);
        copy.ApplicationProperties.Should().HaveCount(5);
    }

    [Fact(DisplayName = "A failed schedule falls back to abandoning the original (immediate redelivery) and does not complete it")]
    public async Task ScheduleFails_FallsBackToAbandon()
    {
        var h = new Harness(_ => HandlerResult.Fail) { ScheduleFault = new ServiceBusException("send refused", ServiceBusFailureReason.ServiceBusy) };

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        h.Calls.Should().Equal("schedule", "abandon");
        h.Logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Could not schedule the retry"));
    }

    [Fact(DisplayName = "A failed schedule followed by a failed abandon is logged, not thrown, and never schedules a second copy")]
    public async Task ScheduleFails_ThenAbandonThrows_NoSecondSchedule()
    {
        var h = new Harness(_ => HandlerResult.Fail)
        {
            ScheduleFault = new ServiceBusException("send refused", ServiceBusFailureReason.ServiceBusy),
            AbandonFault = new ServiceBusException("abandon refused", ServiceBusFailureReason.ServiceCommunicationProblem),
        };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("schedule", "abandon");
        h.Logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Could not abandon message"));
    }

    [Fact(DisplayName = "A failed complete after the schedule cancels the scheduled copy by its sequence number, and does not throw")]
    public async Task CompleteFailsAfterSchedule_CancelsCopy()
    {
        var h = new Harness(_ => HandlerResult.Fail) { CompleteFault = new ServiceBusException("lock lost", ServiceBusFailureReason.MessageLockLost) };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("schedule", "complete", $"cancel:{SequenceNumber}");
    }

    [Fact(DisplayName = "A failed cancel after a failed complete is logged at Error (a duplicate may run) and does not throw")]
    public async Task CancelFails_IsLoggedAtError()
    {
        var h = new Harness(_ => HandlerResult.Fail)
        {
            CompleteFault = new ServiceBusException("lock lost", ServiceBusFailureReason.MessageLockLost),
            CancelFault = new ServiceBusException("cancel refused", ServiceBusFailureReason.ServiceTimeout),
        };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("schedule", "complete", $"cancel:{SequenceNumber}");
        h.Logs.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Could not cancel retry copy") && e.Message.Contains("may both run"));
    }

    [Fact(DisplayName = "A Completed outcome whose complete fails is logged and returns: no retry copy, no abandon, no dead-letter")]
    public async Task CompletedOutcome_CompleteFails_NoCopy()
    {
        var h = new Harness(_ => HandlerResult.Succeed) { CompleteFault = new ServiceBusException("timeout", ServiceBusFailureReason.ServiceTimeout) };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("complete");
        h.Scheduled.Should().BeEmpty();
        h.Logs.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("Could not complete message"));
    }

    [Fact(DisplayName = "A dead-letter that fails is logged and returns: no retry copy and no ProcessingError dead-letter")]
    public async Task DeadLetterFails_NoCopy()
    {
        var h = new Harness(_ => HandlerResult.Poison) { DeadLetterFault = new ServiceBusException("timeout", ServiceBusFailureReason.ServiceTimeout) };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("deadletter");
        h.Scheduled.Should().BeEmpty();
    }

    [Fact(DisplayName = "A lost message lock reaching the catch is not retried: no copy, no settlement")]
    public async Task LockLostInCatch_NoCopy()
    {
        var h = new Harness(_ => throw new ServiceBusException("lock lost", ServiceBusFailureReason.MessageLockLost));

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1));

        h.Calls.Should().BeEmpty();
        h.Scheduled.Should().BeEmpty();
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

        var (copy, dueAt) = h.Scheduled.Should().ContainSingle().Subject;
        dueAt.Should().Be(Now + TimeSpan.FromMinutes(2));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(2);
        h.Calls.Should().Equal("schedule", "complete");
    }

    [Fact(DisplayName = "An unexpected error on the third logical delivery dead-letters ProcessingError as before, also on a copy")]
    public async Task UnexpectedError_OnThirdDelivery_DeadLettersProcessingError()
    {
        var h = new Harness(_ => throw new InvalidOperationException("handler threw"));

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1, priorDeliveries: 2));

        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "ProcessingError", "Unexpected error: handler threw", It.IsAny<CancellationToken>()), Times.Once);
        h.Scheduled.Should().BeEmpty();
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

        JobMessageRetry.ReadPriorDeliveryCount(message).Should().Be(expected);
    }

    // =====================================================================================
    // CommunicationJobProcessor (sdap-communication) — fix round 1, F1-a
    // =====================================================================================

    [Fact(DisplayName = "Communication queue: a HELD email on the logical final delivery (a copy at broker count 1) alerts the administrators and dead-letters Poisoned")]
    public async Task Communication_HoldOnLogicalFinalDelivery_AlertsAndDeadLetters()
    {
        var admins = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var h = new Harness(_ => HandlerResult.Fail) { Communication = true, Admins = admins };

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1, priorDeliveries: 4, jobType: IncomingCommunicationJobHandler.JobTypeName));

        h.Notifications.Select(n => n.GetAttributeValue<EntityReference>("ownerid").Id).Should().BeEquivalentTo(admins);
        h.HandlerLogs.Should().Contain(e => e.Level == LogLevel.Critical && e.Message.Contains("HELD"));
        h.Args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "Poisoned", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Calls.Should().Equal("deadletter");
        h.Scheduled.Should().BeEmpty();
    }

    [Fact(DisplayName = "Communication queue: a HELD email before the final delivery is retried by a copy on sdap-communication with back-off, without alerting")]
    public async Task Communication_HoldBeforeFinal_SchedulesCopyOnItsOwnQueue()
    {
        var h = new Harness(_ => HandlerResult.Fail) { Communication = true, Admins = [Guid.NewGuid()] };

        await h.ProcessAsync(h.Message(brokerDeliveryCount: 1, priorDeliveries: 1, jobType: IncomingCommunicationJobHandler.JobTypeName));

        var (copy, dueAt) = h.Scheduled.Should().ContainSingle().Subject;
        dueAt.Should().Be(Now + TimeSpan.FromMinutes(2));
        copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty].Should().Be(2);
        h.SenderQueues.Should().Equal(CommunicationQueue);
        h.Calls.Should().Equal("schedule", "complete");
        h.Notifications.Should().BeEmpty();
    }

    [Fact(DisplayName = "Communication queue: a failed complete after the schedule cancels the copy, as on sdap-jobs")]
    public async Task Communication_CompleteFailsAfterSchedule_CancelsCopy()
    {
        var h = new Harness(_ => HandlerResult.Fail)
        {
            Communication = true,
            Admins = [Guid.NewGuid()],
            CompleteFault = new ServiceBusException("lock lost", ServiceBusFailureReason.MessageLockLost),
        };

        var act = () => h.ProcessAsync(h.Message(brokerDeliveryCount: 1, jobType: IncomingCommunicationJobHandler.JobTypeName));

        await act.Should().NotThrowAsync();
        h.Calls.Should().Equal("schedule", "complete", $"cancel:{SequenceNumber}");
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

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

        /// <summary>True: drive <see cref="CommunicationJobProcessor"/> with the HOLD handler; false: <see cref="ServiceBusJobProcessor"/>.</summary>
        public bool Communication { get; init; }
        public Guid[] Admins { get; init; } = [];

        public List<(ServiceBusMessage Message, DateTimeOffset DueAt)> Scheduled { get; } = [];
        public List<string> SenderQueues { get; } = [];
        public List<string> Calls { get; } = [];
        public List<Observation> Seen { get; } = [];
        public List<Entity> Notifications { get; } = [];
        public ConcurrentQueue<LogEntry> Logs { get; } = new();
        public ConcurrentQueue<LogEntry> HandlerLogs { get; } = new();
        public Exception? ScheduleFault { get; init; }
        public Exception? CancelFault { get; init; }
        public Exception? CompleteFault { get; init; }
        public Exception? AbandonFault { get; init; }
        public Exception? DeadLetterFault { get; init; }
        public Mock<ProcessMessageEventArgs> Args { get; private set; } = null!;

        public ServiceBusReceivedMessage Message(int brokerDeliveryCount, int? priorDeliveries = null, string jobType = JobType)
        {
            var job = new JobContract
            {
                JobId = Guid.Parse("2876aaaa-0000-0000-0000-000000000025"),
                JobType = jobType,
                SubjectId = "subject-1",
                CorrelationId = CorrelationId,
                IdempotencyKey = IdempotencyKey,
                CreatedAt = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero)
            };
            var body = JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // The properties JobSubmissionService sets, plus one a sender might add.
            var properties = new Dictionary<string, object>
            {
                ["JobType"] = jobType,
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
                partitionKey: SessionId,
                sessionId: SessionId,
                timeToLive: TimeSpan.FromDays(3),
                correlationId: CorrelationId,
                subject: jobType,
                to: "iss025-to",
                contentType: "application/json",
                replyTo: "iss025-reply-to",
                properties: properties,
                deliveryCount: brokerDeliveryCount);
        }

        public async Task ProcessAsync(ServiceBusReceivedMessage message)
        {
            var sender = new Mock<ServiceBusSender>();
            sender.Setup(s => s.ScheduleMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns((ServiceBusMessage m, DateTimeOffset dueAt, CancellationToken _) =>
                {
                    Calls.Add("schedule");
                    if (ScheduleFault is not null)
                    {
                        throw ScheduleFault;
                    }

                    Scheduled.Add((m, dueAt));
                    return Task.FromResult(SequenceNumber);
                });
            sender.Setup(s => s.CancelScheduledMessageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Returns((long sequence, CancellationToken _) =>
                {
                    Calls.Add($"cancel:{sequence}");
                    return CancelFault is null ? Task.CompletedTask : throw CancelFault;
                });

            var client = new Mock<ServiceBusClient>();
            client.Setup(c => c.CreateSender(It.IsAny<string>()))
                .Returns((string queue) =>
                {
                    SenderQueues.Add(queue);
                    return sender.Object;
                });

            Args = new Mock<ProcessMessageEventArgs>(message, new Mock<ServiceBusReceiver>().Object, CancellationToken.None);
            Args.Setup(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
                .Returns(() => Record("complete", CompleteFault));
            Args.Setup(a => a.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns(() => Record("abandon", AbandonFault));
            Args.Setup(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => Record("deadletter", DeadLetterFault));

            var options = Options.Create(new ServiceBusOptions
            {
                QueueName = JobsQueue,
                CommunicationQueueName = CommunicationQueue,
                MaxConcurrentCalls = 1
            });

            var services = new ServiceCollection();
            if (Communication)
            {
                services.AddScoped<IncomingCommunicationJobHandler>(_ => HoldingHandler.Create(this));
                using var provider = services.BuildServiceProvider();
                var processor = new CommunicationJobProcessor(
                    client.Object, provider, new CapturingLogger<CommunicationJobProcessor>(Logs), options, _time);
                await processor.ProcessMessageAsync(Args.Object);
            }
            else
            {
                services.AddScoped<IJobHandler>(_ => new TestHandler(this));
                using var provider = services.BuildServiceProvider();
                var processor = new ServiceBusJobProcessor(
                    client.Object, provider, new CapturingLogger<ServiceBusJobProcessor>(Logs), options, _time);
                await processor.ProcessMessageAsync(Args.Object);
            }
        }

        private Task Record(string call, Exception? fault)
        {
            Calls.Add(call);
            return fault is null ? Task.CompletedTask : throw fault;
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

        /// <summary>
        /// The REAL <see cref="IncomingCommunicationJobHandler"/> whose processing step reports what the Graph-backed processor reports
        /// when the email's record has no resolvable owner — so <see cref="IncomingCommunicationJobHandler.HoldAsync"/> runs as in
        /// production. Re-implements <see cref="IJobHandler"/> because the base <c>ProcessAsync</c> is not virtual.
        /// </summary>
        private sealed class HoldingHandler : IncomingCommunicationJobHandler, IJobHandler
        {
            private HoldingHandler(IncomingCommunicationProcessor processor, NotificationService notifications,
                IOptions<CommunicationOptions> options, ILogger<IncomingCommunicationJobHandler> logger)
                : base(processor, notifications, options, logger)
            {
            }

            public static HoldingHandler Create(Harness harness)
            {
                var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
                entities
                    .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                    .Callback<Entity, CancellationToken>((e, _) => harness.Notifications.Add(e))
                    .ReturnsAsync(Guid.NewGuid);

                // Never invoked: this handler's ProcessAsync goes straight to the HOLD (the processor is sealed and fetches from Graph).
                var processor = (IncomingCommunicationProcessor)RuntimeHelpers.GetUninitializedObject(typeof(IncomingCommunicationProcessor));
                return new HoldingHandler(
                    processor,
                    new NotificationService(entities.Object, NullLogger<NotificationService>.Instance),
                    Options.Create(new CommunicationOptions { OwnershipHoldAlertUserIds = harness.Admins }),
                    new CapturingLogger<IncomingCommunicationJobHandler>(harness.HandlerLogs));
            }

            public new Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct) =>
                HoldAsync(job, new RecordOwnerUnresolvedException("sprk_communication",
                    RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "the matter is marked secure but is not isolated")),
                    TimeSpan.Zero, ct);
        }
    }

    private sealed class CapturingLogger<T>(ConcurrentQueue<LogEntry> entries) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
    }
}
