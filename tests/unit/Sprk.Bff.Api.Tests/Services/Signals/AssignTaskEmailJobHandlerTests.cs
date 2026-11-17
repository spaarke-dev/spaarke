using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> / LogEntry (internal, same assembly)
using Sprk.Bff.Api.Tests.TestInfrastructure; // ReleaseFaultingCache
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// <see cref="AssignTaskEmailJobHandler"/> and its processing lock (the gate Decision Record job's task 196 rule, applied to the same
/// defect here): a job refused the task's processing lock is a <c>Failure</c>, never a <c>Success</c>, because a refusal does not prove
/// the email was sent. The REAL handler, notifier and idempotency service (over an in-memory cache) run; doubled are only the Graph send
/// (<see cref="AssignTaskNotifier.SendAsSystemAsync"/>), the recipient gate's decision and Dataverse. The handler's send paths are covered
/// end to end in <c>AssignTaskNotifierTests</c> (data-mutation); this file covers only the lock.
/// </summary>
public sealed class AssignTaskEmailJobHandlerTests
{
    private static readonly Guid TaskId = Guid.Parse("c1960000-0000-4000-8000-000000000001");
    private static readonly Guid Contact = Guid.Parse("c1960000-0000-4000-8000-000000000002");
    private const string Address = "pat.assignee@firm.example";

    private sealed class Rig
    {
        public CapturingLogger<AssignTaskEmailJobHandler> Log { get; } = new();
        public IIdempotencyService Idempotency { get; }
        public AssignTaskEmailJobHandler Handler { get; }
        public List<SendCommunicationRequest> Sends { get; } = new();
        public int GateAsked { get; private set; }

        /// <summary>What the recipient gate answers (an eligible internal assignee unless a test says otherwise).</summary>
        public AssignTaskRecipient Recipient { get; set; } = AssignTaskRecipient.ForInternal(Guid.NewGuid());

        /// <summary>Awaited inside the Graph send, so a test can hold a send open.</summary>
        public Func<Task>? DuringSend { get; set; }

        public Rig(IDistributedCache? cache = null, TimeProvider? time = null)
        {
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection()); // no email recorded for the task yet
            entities.Setup(e => e.RetrieveAsync("sprk_event", TaskId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_event", TaskId)
                {
                    ["sprk_description"] = "Call the client",
                    ["statecode"] = new OptionSetValue(0),
                    ["sprk_assignedto"] = new EntityReference("contact", Contact),
                });
            entities.Setup(e => e.RetrieveAsync("contact", Contact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("contact", Contact) { ["emailaddress1"] = Address });

            var config = new ConfigurationBuilder().Build();
            var customer = new CustomerIdentity(Options.Create(new CustomerOptions { Id = "acme" }), config, NullLogger<CustomerIdentity>.Instance);
            var gate = new Mock<AssignTaskRecipientGate>(
                Mock.Of<IContactIdentityStore>(), new AssignedAccessTestDoubles.FakeAssignedAccessStore(), null!, null!, null!, null!, null!,
                NullLogger<AssignTaskRecipientGate>.Instance);
            gate.Setup(g => g.DecideAsync(It.IsAny<Guid>(), It.IsAny<DecisionCoreRecord?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    GateAsked++;
                    return Task.FromResult(Recipient);
                });
            var facts = new Mock<InquiryRecordFacts>(
                Mock.Of<IFieldMappingDataverseService>(), Mock.Of<IContactIdentityStore>(), Mock.Of<ISystemUserIdentityResolver>(), customer, config,
                Options.Create(new DataverseOptions { EnvironmentUrl = "https://org.crm.dynamics.com" }), NullLogger<InquiryRecordFacts>.Instance);
            var jobs = new Mock<JobSubmissionService>(
                MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }), NullLogger<JobSubmissionService>.Instance,
                new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);

            var notifier = new Mock<AssignTaskNotifier>(
                null!, new NotificationService(entities.Object, NullLogger<NotificationService>.Instance), entities.Object, gate.Object, facts.Object,
                jobs.Object, Options.Create(new CommunicationOptions { DefaultMailbox = "noreply@firm.example" }), customer, config,
                Options.Create(new DataverseOptions { EnvironmentUrl = "https://org.crm.dynamics.com" }), NullLogger<AssignTaskNotifier>.Instance)
            { CallBase = true };
            notifier.Setup(n => n.SendAsSystemAsync(It.IsAny<SendCommunicationRequest>(), It.IsAny<HttpContext?>(), It.IsAny<CancellationToken>()))
                .Returns(async (SendCommunicationRequest request, HttpContext? _, CancellationToken _) =>
                {
                    Sends.Add(request);
                    if (DuringSend is { } during) await during();
                });

            Idempotency = new IdempotencyService(
                cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance, time);
            Handler = new AssignTaskEmailJobHandler(notifier.Object, Idempotency, Log);
        }

        public Task<JobOutcome> RunAsync(JobContract job) => Handler.ProcessAsync(job, CancellationToken.None);
    }

    private static JobContract Job(Guid? jobId = null, int attempt = 1, int deliveryCount = 1) => new()
    {
        JobId = jobId ?? Guid.NewGuid(),
        JobType = AssignTaskNotifier.JobTypeName,
        IdempotencyKey = $"assign-task-{TaskId:N}",
        Attempt = attempt,
        DeliveryCount = deliveryCount,
        Payload = JsonSerializer.SerializeToDocument(new AssignTaskEmailPayload(TaskId, Contact, null, null)),
    };

    [Fact]
    public async Task ARedeliveryRefusedByItsOwnLeftoverLock_IsAFailure_SendsNothing_LogsAWarning_AndLeavesTheLockInPlace()
    {
        // The silent-loss path: attempt 1 fails (the recipient could not be verified), its lock release hits a cache fault (swallowed), and
        // the broker redelivers the SAME job (same id = same lock owner) inside OwnerTakeoverAge. It used to be refused and completed.
        var cache = new ReleaseFaultingCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var rig = new Rig(cache, new FakeTimeProvider())
        {
            Recipient = AssignTaskRecipient.Refused(AssignTaskRefusal.Unverified),
        };
        var job = Job();
        cache.FailRemove = true;

        var attempt1 = await rig.RunAsync(job);
        attempt1.Status.Should().Be(JobStatus.Failed);
        attempt1.ErrorMessage.Should().Be("recipient_unverified");
        cache.FailRemove = false;
        rig.Recipient = AssignTaskRecipient.ForInternal(Guid.NewGuid()); // the gate would now let the email through, were the job to run
        var before = rig.Log.Entries.Count;
        var gateAsked = rig.GateAsked;

        var redelivered = await rig.RunAsync(job);

        redelivered.Status.Should().Be(JobStatus.Failed, "the lock is its own leftover but younger than the takeover age: not proof the email went");
        redelivered.ErrorMessage.Should().Be(AssignTaskEmailJobHandler.LockRefusedError);
        rig.Sends.Should().BeEmpty();
        rig.GateAsked.Should().Be(gateAsked, "the refused job never reaches the notifier");
        var warning = rig.Log.Entries.Skip(before).Should().ContainSingle().Subject;
        warning.EventId.Should().Be(AssignTaskNotifierEvents.SystemEmailJob);
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(TaskId.ToString()).And.NotContain(Address);
        (await rig.Idempotency.TryAcquireProcessingLockAsync(AssignTaskEmailJobHandler.KeyFor(TaskId), "another-owner", TimeSpan.FromMinutes(1)))
            .Should().BeFalse("the refused job did not release a lock it does not hold");
    }

    [Fact]
    public async Task AJobWhileAnotherJobHoldsTheTask_SendsNothing_AndFails_AndItsRedeliveryAfterwardsFindsTheEmailSent()
    {
        var rig = new Rig();
        var sendStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        rig.DuringSend = async () =>
        {
            sendStarted.SetResult();
            await release.Task;
        };

        var first = rig.RunAsync(Job());
        await sendStarted.Task;
        var secondJob = Job();
        var second = await rig.RunAsync(secondJob);

        second.Status.Should().Be(JobStatus.Failed, "a refused lock is a retry, never a completion");
        second.ErrorMessage.Should().Be(AssignTaskEmailJobHandler.LockRefusedError);
        rig.Sends.Should().ContainSingle("only the first job is sending");

        release.SetResult();
        (await first).Status.Should().Be(JobStatus.Completed);
        rig.Sends.Should().ContainSingle();

        var redelivered = await rig.RunAsync(secondJob);

        redelivered.Status.Should().Be(JobStatus.Completed);
        rig.Sends.Should().ContainSingle("the redelivery found the email already sent (the idempotency entry) and sent nothing");
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 4, false)]
    [InlineData(1, JobContract.MaxDeliveryCount, true)]
    [InlineData(3, 1, true)] // attempts exhausted: the processor dead-letters on this delivery too
    public async Task ARefusedLock_IsLoggedAtWarningOnANonFinalDelivery_AndAtErrorOnlyOnTheFinalOne(int attempt, int deliveryCount, bool isFinal)
    {
        var rig = new Rig();
        (await rig.Idempotency.TryAcquireProcessingLockAsync(AssignTaskEmailJobHandler.KeyFor(TaskId), "another-job", AssignTaskEmailJobHandler.LockDuration))
            .Should().BeTrue();
        var job = Job(attempt: attempt, deliveryCount: deliveryCount);
        job.IsFinalDelivery.Should().Be(isFinal);

        var outcome = await rig.RunAsync(job);

        outcome.Status.Should().Be(JobStatus.Failed);
        rig.Sends.Should().BeEmpty();
        var entry = rig.Log.Entries.Should().ContainSingle().Subject;
        entry.EventId.Should().Be(AssignTaskNotifierEvents.SystemEmailJob);
        entry.Message.Should().Contain(job.JobId.ToString()).And.Contain(TaskId.ToString()).And.NotContain(Address);
        if (isFinal)
        {
            entry.Level.Should().Be(LogLevel.Error);
            entry.Message.Should().Contain("dead-lettered").And.NotContain("will be redelivered");
        }
        else
        {
            entry.Level.Should().Be(LogLevel.Warning);
            entry.Message.Should().Contain("will be redelivered");
        }
    }
}
