using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// A <see cref="CoreAncestorRestampQueue"/> that records what it is asked to enqueue instead of submitting to Service Bus
/// (unified-access-control-r2 task 156) — the <see cref="Sprk.Bff.Api.Services.Jobs.JobSubmissionService"/> virtual-method
/// idiom. Its base is built over an EMPTY provider, so nothing here can reach a real queue.
/// </summary>
internal sealed class RecordingRestampQueue : CoreAncestorRestampQueue
{
    public RecordingRestampQueue()
        : base(new ServiceCollection().BuildServiceProvider(), TimeProvider.System,
            NullLogger<CoreAncestorRestampQueue>.Instance)
    {
    }

    /// <summary>Each child the resolver enqueued for repair (entity, id).</summary>
    public List<(string Entity, Guid Id)> Children { get; } = [];

    public override Task<bool> EnqueueAsync(string childEntity, Guid childId, CancellationToken ct = default)
    {
        Children.Add((childEntity, childId));
        return Task.FromResult(true);
    }
}
