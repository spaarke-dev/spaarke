using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Enqueues ONE child for re-stamping (task 156) — the "enqueue" half of the storage resolver's
/// <c>container_ancestor_stale</c> refusal. ADR-052 places queue-driven work in the BFF as an ADR-004
/// <see cref="IJobHandler"/>: <see cref="CoreAncestorRestampJobHandler"/> on the shared <c>sdap-jobs</c> queue, which
/// gives it the queue's retry, dead-letter and duplicate-detection machinery rather than a second pipeline.
/// </summary>
/// <remarks>
/// <para><b>Never changes the refusal.</b> The resolver refuses FIRST; enqueuing is best effort. A failure to enqueue is
/// logged and swallowed, because the reconciliation job finds the same stale copy within one 5-minute cycle anyway — the
/// enqueue only makes the user's retry succeed in seconds instead of minutes.</para>
/// <para><b>Lazy.</b> <see cref="JobSubmissionService"/> is resolved on first use, not at construction: the resolver that
/// owns this queue is constructed on every upload, and must not fail to construct where Service Bus is not configured
/// (the refusal must still happen there).</para>
/// <para>Duplicate refusals for one child within the same minute collapse to one message (the idempotency key is the
/// Service Bus MessageId); a later refusal enqueues again. An after-write cascade is never collapsed: each write is its
/// own message.</para>
/// </remarks>
public class CoreAncestorRestampQueue
{
    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CoreAncestorRestampQueue> _logger;

    public CoreAncestorRestampQueue(
        IServiceProvider services,
        TimeProvider timeProvider,
        ILogger<CoreAncestorRestampQueue> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Enqueue a re-stamp of <paramref name="childEntity"/> <paramref name="childId"/>. Returns false (logged) when it could not.</summary>
    /// <remarks>Virtual so a test can observe the enqueue without Service Bus (the <see cref="JobSubmissionService"/> idiom).</remarks>
    public virtual Task<bool> EnqueueAsync(string childEntity, Guid childId, CancellationToken ct = default)
        => SubmitAsync(new CoreAncestorRestampPayload(childEntity, childId), "its copy disagreed with its intermediate's live root", ct);

    /// <summary>
    /// Enqueue the cascade for a write that a USER-OBO-only caller made (task 156): the job runs
    /// <see cref="CoreAncestorRestamper.AfterWriteAsync"/> for <paramref name="writtenColumns"/> on
    /// <paramref name="entity"/> <paramref name="id"/>. For a caller bound to hold no app-only Dataverse client
    /// (<c>DataverseUpdateRecordHandler</c>): the re-stamp — a server-owned invariant — runs as the BFF's background job,
    /// seconds later, and the storage resolver refuses a stale copy in that window.
    /// </summary>
    public virtual Task<bool> EnqueueAfterWriteAsync(
        string entity, Guid id, IReadOnlyList<string> writtenColumns, CancellationToken ct = default)
        => SubmitAsync(new CoreAncestorRestampPayload(entity, id, writtenColumns), "a write moved what it is filed under or its root", ct);

    private async Task<bool> SubmitAsync(CoreAncestorRestampPayload payload, string reason, CancellationToken ct)
    {
        var (childEntity, childId) = (payload.Entity, payload.Id);
        try
        {
            var submitter = _services.GetRequiredService<JobSubmissionService>();
            var afterWrite = payload.WrittenColumns is { Count: > 0 };

            // A stale refusal is retried by its user, so repeats within a minute collapse to one message. An after-write
            // cascade is one per WRITE: two re-files of one record in the same minute are two cascades, and collapsing
            // the second would leave its children on the first one's root until the reconciliation job.
            var discriminator = afterWrite
                ? Guid.NewGuid().ToString("N")
                : _timeProvider.GetUtcNow().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

            await submitter.SubmitJobAsync(
                new JobContract
                {
                    JobType = CoreAncestorRestampJobHandler.JobTypeName,
                    SubjectId = $"{childEntity}:{childId:D}",
                    CorrelationId = Guid.NewGuid().ToString("D"),
                    IdempotencyKey =
                        $"core-ancestor-restamp:{(afterWrite ? "after-write" : "child")}:{childEntity}:{childId:N}:{discriminator}",
                    Payload = JsonSerializer.SerializeToDocument(payload),
                },
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "{Prefix} Enqueued a re-stamp of {Entity} {Id} ({Reason}).",
                CoreAncestorRestamper.LogPrefix, childEntity, childId, reason);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "{Prefix} Could not enqueue a re-stamp of {Entity} {Id}; the reconciliation job repairs it within one cycle.",
                CoreAncestorRestamper.LogPrefix, childEntity, childId);
            return false;
        }
    }
}

/// <summary>
/// The <c>CoreAncestorRestamp</c> job payload: the record to repair — and, for a cascade enqueued after a write, the columns
/// that write set (<see langword="null"/> = repair the record itself from its source's current root).
/// </summary>
public sealed record CoreAncestorRestampPayload(string Entity, Guid Id, IReadOnlyList<string>? WrittenColumns = null);
