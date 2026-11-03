using System.Data;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 152 (FR-43, NFR-03): every successful lifecycle move queues the invoice's re-index, under a key that belongs to that move.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the key decides whether Service Bus duplicate detection drops a re-index. A fixed per-invoice key (the old
/// "invoice-index-{invoiceId}") would keep every status change after the first out of the index. The Dataverse boundary is a Moq double of
/// <see cref="IFieldMappingDataverseService"/>; the queue is a Moq double of <see cref="JobSubmissionService"/>.
/// </remarks>
public sealed class InvoiceLifecycleIndexEnqueueTests
{
    private static readonly Guid InvoiceId = Guid.Parse("15200000-0000-4000-8000-000000000001");

    private readonly Mock<IFieldMappingDataverseService> _records = new();
    private readonly List<JobContract> _sent = [];
    private Exception? _queueFault;

    private InvoiceLifecycleService CreateService()
    {
        var jobs = new Mock<JobSubmissionService>(
            Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { QueueName = "q", CommunicationQueueName = "c" }),
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, CancellationToken _) =>
            {
                if (_queueFault is { } fault)
                {
                    throw fault;
                }

                _sent.Add(job);
                return Task.CompletedTask;
            });
        return new InvoiceLifecycleService(_records.Object, NullLogger<InvoiceLifecycleService>.Instance, jobs.Object);
    }

    private static Dictionary<string, object?> Row(InvoiceLifecycleStatus status, long version) => new()
    {
        ["statuscode"] = (long)InvoiceStatusCodes.StatusCodeOf(status),
        ["versionnumber"] = version,
    };

    private void ReadsReturn(params Dictionary<string, object?>[] rows)
    {
        var queue = new Queue<Dictionary<string, object?>>(rows);
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
    }

    [Fact(DisplayName = "152 FR-43: a successful move queues one InvoiceIndexing job for the invoice, keyed to the version the move replaced")]
    public async Task Transition_QueuesTheReindex()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        var job = _sent.Should().ContainSingle().Subject;
        job.JobType.Should().Be(InvoiceIndexingJobHandler.JobTypeName);
        job.SubjectId.Should().Be(InvoiceId.ToString());
        job.IdempotencyKey.Should().Be($"invoice-index-{InvoiceId:N}-v7");
        job.Payload!.RootElement.GetProperty("InvoiceId").GetGuid().Should().Be(InvoiceId);
    }

    [Fact(DisplayName = "152 NFR-08: each lifecycle move of an invoice queues a job whose idempotency key differs from the previous one")]
    public async Task EachMove_HasItsOwnKey()
    {
        // In Review -> Approved replaces version 7; Approved -> Sent to AP replaces version 9; Sent to AP -> Paid replaces version 12.
        var sut = CreateService();
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));
        await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);
        ReadsReturn(Row(InvoiceLifecycleStatus.Approved, 9));
        await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.SentToAp);
        ReadsReturn(Row(InvoiceLifecycleStatus.SentToAp, 12));
        await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Paid);

        _sent.Select(j => j.IdempotencyKey).Should().Equal(
            $"invoice-index-{InvoiceId:N}-v7", $"invoice-index-{InvoiceId:N}-v9", $"invoice-index-{InvoiceId:N}-v12");
        _sent.Select(j => j.IdempotencyKey).Should().OnlyHaveUniqueItems();
        // And none of them is the acceptance's key or the retired fixed key.
        _sent.Select(j => j.IdempotencyKey).Should().NotContain(new[] { $"invoice-index-{InvoiceId:N}-accepted", $"invoice-index-{InvoiceId}" });
    }

    [Fact(DisplayName = "152: a move that lost a concurrent write and retried queues once, under the version of the write that landed")]
    public async Task ConcurrentRetry_QueuesOnce_UnderTheLandedVersion()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 5), Row(InvoiceLifecycleStatus.InReview, 6));
        var writes = 0;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, Dictionary<string, object?> _, long _, CancellationToken _) =>
                ++writes == 1 ? Task.FromException(new DBConcurrencyException("changed")) : Task.CompletedTask);

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        _sent.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"invoice-index-{InvoiceId:N}-v6");
    }

    [Fact(DisplayName = "152: no job when nothing moved - already in the target status, not allowed by the matrix, or no such invoice")]
    public async Task NoMove_QueuesNothing()
    {
        var sut = CreateService();

        ReadsReturn(Row(InvoiceLifecycleStatus.Approved, 3));
        (await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved)).Outcome.Should().Be(InvoiceTransitionOutcome.AlreadyInTarget);

        ReadsReturn(Row(InvoiceLifecycleStatus.Paid, 4));
        (await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved)).Outcome.Should().Be(InvoiceTransitionOutcome.Refused);

        ReadsReturn(new Dictionary<string, object?>());
        (await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved)).Reason.Should().Be(InvoiceTransitionReasons.NotFound);

        _sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "152: a lost write queues nothing (the invoice was deleted before the move landed)")]
    public async Task DeletedBeforeTheWrite_QueuesNothing()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(new KeyNotFoundException()));

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Reason.Should().Be(InvoiceTransitionReasons.NotFound);
        _sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "152: a queue fault never undoes or fails the committed move; the move is reported and the status was written")]
    public async Task QueueFault_DoesNotFailTheMove()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));
        _queueFault = new InvalidOperationException("service bus down");

        var result = await CreateService().TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        _records.Verify(
            r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId,
                It.Is<Dictionary<string, object?>>(f => (int)f["statuscode"]! == InvoiceStatusCodes.Approved), 7L, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "152: the enqueue does not depend on the caller's cancellation token (the status is committed)")]
    public async Task Enqueue_IgnoresACancelledCaller()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));
        using var cts = new CancellationTokenSource();
        var sut = CreateService();
        // The write itself succeeds; the caller cancels while the move is being reported.
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });

        var result = await sut.TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved, cts.Token);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
        _sent.Should().ContainSingle("the job is submitted with CancellationToken.None");
    }

    [Fact(DisplayName = "152: without a job submitter (unit-test seam) the move still works and queues nothing")]
    public async Task NoSubmitter_StillMoves()
    {
        ReadsReturn(Row(InvoiceLifecycleStatus.InReview, 7));

        var result = await new InvoiceLifecycleService(_records.Object, NullLogger<InvoiceLifecycleService>.Instance)
            .TransitionAsync(InvoiceId, InvoiceLifecycleStatus.Approved);

        result.Outcome.Should().Be(InvoiceTransitionOutcome.Transitioned);
    }
}
