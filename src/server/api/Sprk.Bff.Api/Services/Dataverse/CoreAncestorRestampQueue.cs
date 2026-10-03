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
/// Service Bus MessageId); a later refusal enqueues again.</para>
/// <para><b>Stale refusals only.</b> Until owner decisions round 8 item 1 the user-OBO AI update tool also enqueued its
/// after-write cascade here. It now re-stamps inline through <see cref="CoreAncestorAfterWriteRestamp"/> (§6.5 path B), so
/// that message type, its payload field and its job branch are gone: every BFF re-file path re-stamps in the same
/// operation, and this queue carries only the resolver's stale children.</para>
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

    private async Task<bool> SubmitAsync(CoreAncestorRestampPayload payload, string reason, CancellationToken ct)
    {
        var (childEntity, childId) = (payload.Entity, payload.Id);
        try
        {
            var submitter = _services.GetRequiredService<JobSubmissionService>();

            // A stale refusal is retried by its user, so repeats within a minute collapse to one message.
            var discriminator = _timeProvider.GetUtcNow().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

            await submitter.SubmitJobAsync(
                new JobContract
                {
                    JobType = CoreAncestorRestampJobHandler.JobTypeName,
                    SubjectId = $"{childEntity}:{childId:D}",
                    CorrelationId = Guid.NewGuid().ToString("D"),
                    IdempotencyKey = $"core-ancestor-restamp:child:{childEntity}:{childId:N}:{discriminator}",
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
/// The <c>CoreAncestorRestamp</c> job payload: the stale record to repair from its source's current root.
/// </summary>
public sealed record CoreAncestorRestampPayload(string Entity, Guid Id);
