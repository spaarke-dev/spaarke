using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Nightly drift-detection job for the spaarke-playbook-embeddings AI Search index
/// (chat-routing-redesign-r1 FR-13). Iterates active <c>sprk_analysisplaybook</c>
/// rows, recomputes the canonical embed-input hash via
/// <see cref="IPlaybookEmbeddingHashCalculator"/>, compares it to the stored
/// <c>sprk_indexhash</c>, and flips <c>sprk_indexstatus</c> to <c>Stale</c>
/// (numeric option <c>100000003</c>, per task 030 verification) on mismatch.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-004 compliance</b>: this job is processed by the existing
/// <see cref="ServiceBusJobProcessor"/>. A separate scheduler component is responsible
/// for enqueuing a <see cref="JobContract"/> with <see cref="JobType"/> = <c>"PlaybookIndexDriftDetection"</c>
/// nightly (e.g., Service Bus scheduled message or an Azure Function timer trigger).
/// This handler does NOT contain a Timer — see <see cref="JobType"/> below for the
/// scheduling contract.
/// </para>
/// <para>
/// <b>ADR-015 telemetry contract</b>: the only fields logged are <c>scannedCount</c>,
/// <c>driftCount</c>, <c>durationMs</c>, and <c>tenantId</c>. No playbook content,
/// hash inputs, JSON payloads, or user data are surfaced. The stored hash is a
/// deterministic fingerprint of playbook metadata and is safe to log if needed for
/// debugging, but the default logging path keeps even that out.
/// </para>
/// <para>
/// <b>Wired data path (task 034 follow-up bundle, 2026-06-22)</b>: The four data-path
/// gaps originally flagged with the task 034 scaffolding are now closed:
/// </para>
/// <list type="number">
///   <item><description><b>Gap 1 — Tenant-wide enumeration</b>: this job uses
///   <see cref="IPlaybookService.ListAllActivePlaybooksAsync"/>, which pages through
///   ALL <c>statecode=0</c> playbooks with the FR-13 tracking-field projection.</description></item>
///   <item><description><b>Gap 2 — Tracking fields on PlaybookResponse</b>: the model now
///   exposes <c>IndexStatusCode</c>, <c>IndexHash</c>, <c>LastIndexedAt</c>
///   (defaults <c>IndexStatusCode</c> to <see cref="IndexStatusNotIndexed"/> when Dataverse
///   returns null so consumers don't null-check).</description></item>
///   <item><description><b>Gap 3 — Write-back path</b>: this job calls
///   <see cref="IPlaybookService.UpdateIndexStatusAsync"/> on drift detection, which
///   PATCHes the four tracking columns on the playbook row.</description></item>
///   <item><description><b>Gap 5 — Hash-on-index</b>: <see cref="PlaybookIndexingService"/>
///   writes <c>sprk_indexhash</c> at successful index completion (Indexed transition) and
///   <see cref="IndexStatusFailed"/> with truncated error on the failure path.</description></item>
/// </list>
/// <para>
/// <b>Gap 4 (deferred)</b>: a Service Bus producer for the nightly drift schedule is not
/// yet in place. The producer is expected to enqueue a <see cref="JobContract"/> with
/// <see cref="JobType"/> = <c>"PlaybookIndexDriftDetection"</c> per tenant nightly, setting
/// <see cref="JobContract.SubjectId"/> to the tenant identifier. Implementation deferred
/// to a follow-up infrastructure task.
/// </para>
/// </remarks>
internal sealed class PlaybookIndexDriftDetectionJob : IJobHandler
{
    /// <summary>
    /// Job-type discriminator used by <see cref="ServiceBusJobProcessor"/> to route incoming
    /// Service Bus messages to this handler. The producer (Service Bus scheduled message or
    /// timer-trigger Function) MUST set <see cref="JobContract.JobType"/> to this exact value.
    /// </summary>
    public const string JobTypeName = "PlaybookIndexDriftDetection";

    // Per task 030 evidence (notes/handoffs/030-schema-verification-evidence.md):
    // sprk_indexstatus option codes are stable numeric values, NOT string labels.
    private const int IndexStatusNotIndexed = 100_000_000;
    private const int IndexStatusPending = 100_000_001;
    private const int IndexStatusIndexed = 100_000_002;
    private const int IndexStatusStale = 100_000_003;
    private const int IndexStatusFailed = 100_000_004;

    private readonly IPlaybookEmbeddingHashCalculator _hashCalculator;
    private readonly IPlaybookService _playbookService;
    private readonly ILogger<PlaybookIndexDriftDetectionJob> _logger;

    public PlaybookIndexDriftDetectionJob(
        IPlaybookEmbeddingHashCalculator hashCalculator,
        IPlaybookService playbookService,
        ILogger<PlaybookIndexDriftDetectionJob> logger)
    {
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string JobType => JobTypeName;

    /// <inheritdoc/>
    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        ct.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var tenantId = job.SubjectId; // Producer is expected to set SubjectId = tenant identifier.

        var scannedCount = 0;
        var driftCount = 0;
        var skippedCount = 0;

        try
        {
            await foreach (var playbook in EnumerateActivePlaybooksAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                scannedCount++;

                // FR-13: only flag drift on currently-Indexed rows. Pending / Failed /
                // NotIndexed are owned by other state machines and MUST NOT be overwritten.
                var status = GetIndexStatusCode(playbook);
                if (status != IndexStatusIndexed)
                {
                    skippedCount++;
                    continue;
                }

                var storedHash = GetStoredIndexHash(playbook);
                if (string.IsNullOrWhiteSpace(storedHash))
                {
                    // Indexed but no hash — anomalous. Skip rather than overwrite; the
                    // admin-view (task 035) will surface this for manual triage.
                    skippedCount++;
                    continue;
                }

                var recomputed = _hashCalculator.ComputeHash(BuildDocumentFromPlaybook(playbook));

                if (!string.Equals(recomputed, storedHash, StringComparison.OrdinalIgnoreCase))
                {
                    await MarkStaleAsync(playbook.Id, ct);
                    driftCount++;
                }
            }

            stopwatch.Stop();

            // ADR-015 tier-1 telemetry: counts + durations + tenantId only.
            _logger.LogInformation(
                "PlaybookIndexDriftDetection completed: scannedCount={ScannedCount}, " +
                "driftCount={DriftCount}, skippedCount={SkippedCount}, durationMs={DurationMs}, tenantId={TenantId}",
                scannedCount, driftCount, skippedCount, stopwatch.ElapsedMilliseconds, tenantId);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "PlaybookIndexDriftDetection cancelled: scannedCount={ScannedCount}, " +
                "driftCount={DriftCount}, durationMs={DurationMs}, tenantId={TenantId}",
                scannedCount, driftCount, stopwatch.ElapsedMilliseconds, tenantId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "PlaybookIndexDriftDetection failed after scanning {ScannedCount} playbooks " +
                "(driftCount={DriftCount}, tenantId={TenantId}): {Error}",
                scannedCount, driftCount, tenantId, ex.Message);

            // Drift detection is idempotent; classify as Failure (retryable) until max attempts.
            return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Enumerates active playbooks for drift evaluation. Delegates to
    /// <see cref="IPlaybookService.ListAllActivePlaybooksAsync"/> (chat-routing-redesign-r1
    /// task 034 follow-up — Gap 1 closed). Pages through ALL <c>statecode=0</c> playbooks
    /// across the tenant, projection includes the three FR-13 tracking fields
    /// (<c>sprk_indexstatus</c>, <c>sprk_indexhash</c>, <c>sprk_lastindexedat</c>).
    /// </summary>
    private IAsyncEnumerable<PlaybookResponse> EnumerateActivePlaybooksAsync(CancellationToken ct) =>
        _playbookService.ListAllActivePlaybooksAsync(ct);

    /// <summary>
    /// Reads the <c>sprk_indexstatus</c> numeric option code from the playbook.
    /// Surfaces the field exposed on <see cref="PlaybookResponse"/> by the task 034
    /// follow-up (Gap 2 closed). Null in Dataverse is projected to
    /// <see cref="IndexStatusNotIndexed"/> at the service layer.
    /// </summary>
    private static int GetIndexStatusCode(PlaybookResponse playbook) => playbook.IndexStatusCode;

    /// <summary>
    /// Reads the <c>sprk_indexhash</c> string from the playbook. Null until the playbook
    /// has been successfully indexed (Gap 5 closure populates this at index time).
    /// </summary>
    private static string? GetStoredIndexHash(PlaybookResponse playbook) => playbook.IndexHash;

    /// <summary>
    /// Builds the <see cref="PlaybookEmbeddingDocument"/> used to feed the hash calculator.
    /// Mirrors the projection done by <see cref="PlaybookIndexingService"/> at index time so
    /// the hash composition stays identical.
    /// </summary>
    private static PlaybookEmbeddingDocument BuildDocumentFromPlaybook(PlaybookResponse playbook)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(playbook.RecordType))
        {
            tags.Add(playbook.RecordType);
        }
        if (!string.IsNullOrWhiteSpace(playbook.EntityType))
        {
            tags.Add(playbook.EntityType);
        }
        if (playbook.Capabilities is { Length: > 0 })
        {
            tags.AddRange(playbook.Capabilities);
        }

        return new PlaybookEmbeddingDocument
        {
            Id = playbook.Id.ToString(),
            PlaybookId = playbook.Id.ToString(),
            PlaybookName = playbook.Name,
            Description = playbook.Description ?? string.Empty,
            TriggerPhrases = playbook.TriggerPhrases?.ToList() ?? [],
            RecordType = playbook.RecordType ?? string.Empty,
            EntityType = playbook.EntityType ?? string.Empty,
            Tags = tags,
            JpsMatchingMetadata = playbook.JpsMatchingMetadata,
        };
    }

    /// <summary>
    /// Writes <c>sprk_indexstatus = Stale</c> on the playbook row via
    /// <see cref="IPlaybookService.UpdateIndexStatusAsync"/> (chat-routing-redesign-r1
    /// task 034 follow-up — Gap 3 closed). Passes <c>indexHash: null</c> so the existing
    /// fingerprint is intentionally preserved on Dataverse (per WhenWritingNull policy in
    /// PlaybookService.JsonOptions — admins want to see what the hash WAS at last
    /// successful index time). Passes <c>lastError: null</c> so the lastError column is
    /// cleared (drift is not an error per se — it's an expected content-shift signal).
    /// </summary>
    /// <remarks>
    /// ADR-015 safe: only the playbook ID + status code are logged here; the underlying
    /// service is responsible for keeping its own logs ADR-015-clean.
    /// </remarks>
    private async Task MarkStaleAsync(Guid playbookId, CancellationToken ct)
    {
        await _playbookService.UpdateIndexStatusAsync(
            playbookId,
            statusCode: IndexStatusStale,
            indexHash: null,
            lastError: null,
            cancellationToken: ct);

        _logger.LogInformation(
            "Drift detected for playbook {PlaybookId} — flipped sprk_indexstatus={StaleCode}",
            playbookId, IndexStatusStale);
    }
}
