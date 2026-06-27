using Spaarke.Scheduling;

namespace Spaarke.Scheduling.Tests;

/// <summary>
/// Test double <see cref="IScheduledJob"/>. Counts invocations, captures the last context,
/// optionally blocks until a manual reset is signalled (cancellation propagation tests).
/// </summary>
internal sealed class FakeScheduledJob : IScheduledJob
{
    private readonly Func<JobRunContext, CancellationToken, Task<JobRunResult>>? _impl;

    public FakeScheduledJob(string jobId)
    {
        JobId = jobId;
    }

    public FakeScheduledJob(string jobId, Func<JobRunContext, CancellationToken, Task<JobRunResult>> impl)
    {
        JobId = jobId;
        _impl = impl;
    }

    public string JobId { get; }
    public string DisplayName => $"Fake {JobId}";
    public string Description => $"Fake test job '{JobId}'";

    public int InvocationCount;
    public JobRunContext? LastContext;
    public CancellationToken LastToken;

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref InvocationCount);
        LastContext = context;
        LastToken = cancellationToken;

        if (_impl is not null)
        {
            return await _impl(context, cancellationToken).ConfigureAwait(false);
        }

        return new JobRunResult(true, null, 1, TimeSpan.FromMilliseconds(1));
    }
}
