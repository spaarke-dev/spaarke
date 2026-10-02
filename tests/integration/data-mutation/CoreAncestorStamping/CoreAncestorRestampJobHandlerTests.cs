using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156 — the <c>CoreAncestorRestamp</c> queue job (ADR-004): a stale row the storage
/// resolver enqueued is re-stamped from its source's CURRENT root; a cascade enqueued by the user-OBO AI tool runs as
/// <see cref="CoreAncestorRestamper.AfterWriteAsync"/>; a payload naming nothing that can move a stamp is dead-lettered.
/// </summary>
public class CoreAncestorRestampJobHandlerTests
{
    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c01");
    private static readonly Guid Todo = Guid.Parse("15600000-0000-0000-0000-000000000101");

    private static StampWorld StaleTodo() => new StampWorld()
        .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
        .Row("sprk_todo", Todo,
            [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
            pairId: Communication.ToString());

    private static JobContract Job(CoreAncestorRestampPayload payload) => new()
    {
        JobType = CoreAncestorRestampJobHandler.JobTypeName,
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    [Fact(DisplayName = "Task 156 job: a child the resolver refused as stale is re-stamped from its source's current root")]
    public async Task ChildPayload_RestampsTheChild()
    {
        var world = StaleTodo();
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

        var outcome = await handler.ProcessAsync(Job(new CoreAncestorRestampPayload("sprk_todo", Todo)), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 job: an after-write payload (the user-OBO AI tool's) runs the cascade for the record it re-filed")]
    public async Task AfterWritePayload_RunsTheCascade()
    {
        var world = StaleTodo();
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

        var outcome = await handler.ProcessAsync(
            Job(new CoreAncestorRestampPayload("sprk_communication", Communication, ["sprk_regardingmatter"])),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 job: a child PATCH that fails is retried (Failed), then dead-lettered at max attempts — never reported complete")]
    public async Task FailedRepair_IsRetried_ThenPoisoned()
    {
        var world = StaleTodo().FailWrite("sprk_todo", Todo);
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);
        var job = Job(new CoreAncestorRestampPayload("sprk_todo", Todo));

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed);

        job.Attempt = job.MaxAttempts;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact(DisplayName = "Task 156 queue: what the storage resolver and the AI tool ENQUEUE is what this handler consumes — a stale child and an after-write cascade both round-trip to a re-stamp")]
    public async Task EnqueuedMessages_RoundTripThroughTheHandler()
    {
        var (queue, submitted) = Queue(new FakeTimeProvider());

        await queue.EnqueueAsync("sprk_todo", Todo);
        await queue.EnqueueAfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        submitted.Should().HaveCount(2).And.OnlyContain(j => j.JobType == CoreAncestorRestampJobHandler.JobTypeName);
        foreach (var job in submitted)
        {
            var world = StaleTodo();
            var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

            (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
            world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
        }
    }

    [Fact(DisplayName = "Task 156 queue: repeated stale refusals of one child in a minute collapse to one message; two after-write cascades of one record never do")]
    public async Task IdempotencyKeys_CollapseRepeatedRefusals_NeverTwoWrites()
    {
        var (queue, submitted) = Queue(new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 30, 5, TimeSpan.Zero)));

        await queue.EnqueueAsync("sprk_todo", Todo);
        await queue.EnqueueAsync("sprk_todo", Todo);
        await queue.EnqueueAfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);
        await queue.EnqueueAfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        submitted[0].IdempotencyKey.Should().Be(submitted[1].IdempotencyKey,
            "a user retrying a stale upload is one repair, not one per click");
        submitted[2].IdempotencyKey.Should().NotBe(submitted[3].IdempotencyKey,
            "two re-files of one record are two cascades — collapsing the second would leave its children on the first root");
    }

    /// <summary>The real queue over a <see cref="JobSubmissionService"/> that records instead of sending (Service Bus is the boundary).</summary>
    private static (CoreAncestorRestampQueue Queue, List<JobContract> Submitted) Queue(TimeProvider time)
    {
        var submitted = new List<JobContract>();
        var submitter = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            Options.Create(new ServiceBusOptions
            {
                QueueName = "test-jobs",
                ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
            }),
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
        submitter
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => submitted.Add(job))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(submitter.Object).BuildServiceProvider();
        return (new CoreAncestorRestampQueue(services, time, NullLogger<CoreAncestorRestampQueue>.Instance), submitted);
    }

    [Theory(DisplayName = "Task 156 job: a payload naming nothing whose stamp can move is dead-lettered without a read")]
    [InlineData("sprk_matter", null)]
    [InlineData("sprk_communication", "sprk_triagesummary")]
    public async Task PayloadThatCannotMoveAStamp_IsPoisoned(string entity, string? column)
    {
        var world = StaleTodo();
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

        var outcome = await handler.ProcessAsync(
            Job(new CoreAncestorRestampPayload(entity, Guid.NewGuid(), column is null ? null : [column])),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        world.Patches.Should().BeEmpty();
    }
}
