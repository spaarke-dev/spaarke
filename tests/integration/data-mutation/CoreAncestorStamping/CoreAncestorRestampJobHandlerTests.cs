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
/// resolver enqueued is re-stamped from its source's CURRENT root; a payload naming nothing whose stamp can move is
/// dead-lettered. (Since owner decisions round 8 item 1 the user-OBO AI update tool re-stamps inline through
/// <see cref="CoreAncestorAfterWriteRestamp"/>, so the queue carries no after-write cascade any more.)
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

    [Fact(DisplayName = "Task 156 (verifier round 2 item 13): a cascade stopped ONLY by its bound is done, not retried — a retry stops at the same bound, so Failed would only end in a dead letter; the reconciliation job finishes the rest")]
    public async Task TruncatedOnlyCascade_IsCompleted_NeverRetriedOrPoisoned()
    {
        var todos = new[]
        {
            Guid.Parse("15600000-0000-0000-0000-000000000111"),
            Guid.Parse("15600000-0000-0000-0000-000000000112"),
            Guid.Parse("15600000-0000-0000-0000-000000000113"),
        };
        // A STALE event (its communication moved to matter B; the event still copies A) with three to-dos under it. The
        // resolver's stale refusal enqueues the event; its re-stamp then cascades to the to-dos.
        var staleEvent = Guid.Parse("15600000-0000-0000-0000-000000000e11");
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_event", staleEvent,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        foreach (var todo in todos)
        {
            world.Row("sprk_todo", todo,
                [("sprk_regardingevent", "sprk_event", staleEvent), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: staleEvent.ToString());
        }

        // One child per page, two per source: the third child is past the bound on every attempt.
        var handler = new CoreAncestorRestampJobHandler(
            world.RestamperWith(childPageSize: 1, childrenPerSourceBound: 2), NullLogger<CoreAncestorRestampJobHandler>.Instance);
        var job = Job(new CoreAncestorRestampPayload("sprk_event", staleEvent));

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed);

        job.Attempt = job.MaxAttempts;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Completed,
            "a truncation is not a failure, so it is never dead-lettered");

        world.Lookup("sprk_event", staleEvent, "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_todo", todos[0], "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_todo", todos[1], "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_todo", todos[2], "sprk_regardingmatter").Should().Be(MatterA,
            "past the bound on every attempt — left to the reconciliation job (the resolver refuses it as stale meanwhile)");
    }

    [Fact(DisplayName = "Task 156 queue: what the storage resolver ENQUEUES is what this handler consumes — a stale child round-trips to a re-stamp")]
    public async Task EnqueuedMessages_RoundTripThroughTheHandler()
    {
        var (queue, submitted) = Queue(new FakeTimeProvider());

        await queue.EnqueueAsync("sprk_todo", Todo);

        submitted.Should().ContainSingle().Which.JobType.Should().Be(CoreAncestorRestampJobHandler.JobTypeName);
        var world = StaleTodo();
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

        (await handler.ProcessAsync(submitted[0], CancellationToken.None)).Status.Should().Be(JobStatus.Completed);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 queue: repeated stale refusals of one child in a minute collapse to one message; a refusal in a later minute enqueues again")]
    public async Task IdempotencyKeys_CollapseRepeatedRefusals_WithinAMinute()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 30, 5, TimeSpan.Zero));
        var (queue, submitted) = Queue(time);

        await queue.EnqueueAsync("sprk_todo", Todo);
        await queue.EnqueueAsync("sprk_todo", Todo);
        time.Advance(TimeSpan.FromMinutes(1));
        await queue.EnqueueAsync("sprk_todo", Todo);

        submitted[0].IdempotencyKey.Should().Be(submitted[1].IdempotencyKey,
            "a user retrying a stale upload is one repair, not one per click");
        submitted[2].IdempotencyKey.Should().NotBe(submitted[0].IdempotencyKey,
            "a refusal after the minute is a new repair request, not a duplicate");
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

    [Theory(DisplayName = "Task 156 job: a payload naming a table that carries no stamp is dead-lettered without a read")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_invoice")]
    public async Task PayloadThatCannotMoveAStamp_IsPoisoned(string entity)
    {
        var world = StaleTodo();
        var handler = new CoreAncestorRestampJobHandler(world.Restamper, NullLogger<CoreAncestorRestampJobHandler>.Instance);

        var outcome = await handler.ProcessAsync(
            Job(new CoreAncestorRestampPayload(entity, Guid.NewGuid())),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        world.Patches.Should().BeEmpty();
    }
}
