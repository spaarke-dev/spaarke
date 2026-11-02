using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 152: a <see cref="JobSubmissionService"/> double for the producers of an invoice's re-index (line decisions, adjustments,
/// corrections, overrides). It records the jobs submitted and can fault, so a producer's test proves the key it queues under and that a failed
/// enqueue never fails the change it follows.
/// </summary>
public sealed class IndexQueueDouble
{
    public IndexQueueDouble()
    {
        var jobs = new Mock<JobSubmissionService>(
            Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { QueueName = "q", CommunicationQueueName = "c" }),
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, CancellationToken _) =>
            {
                if (Fault is { } fault)
                {
                    throw fault;
                }

                Sent.Add(job);
                return Task.CompletedTask;
            });
        Submitter = jobs.Object;
    }

    /// <summary>The jobs the producer queued, in order.</summary>
    public List<JobContract> Sent { get; } = [];

    /// <summary>When set, every submit throws it (a Service Bus outage).</summary>
    public Exception? Fault { get; set; }

    public JobSubmissionService Submitter { get; }
}
