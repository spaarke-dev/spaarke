using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Nightly drift-detection job for the playbook-embeddings AI Search index
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
/// <b>Open scoping gaps (flagged for main session — see task 034 report)</b>:
/// </para>
/// <list type="number">
///   <item><description><b>Active-playbook enumeration gap</b>: <see cref="IPlaybookService"/>
///   currently exposes only user-scoped (<c>ListUserPlaybooksAsync</c>) and public-only
///   (<c>ListPublicPlaybooksAsync</c>) enumeration. Neither returns ALL active playbooks
///   irrespective of owner/IsPublic, which is what FR-13 requires. The marked TODO at
///   <see cref="EnumerateActivePlaybooksAsync"/> documents the contract a future
///   <c>ListAllActivePlaybooksAsync</c> method must satisfy.</description></item>
///   <item><description><b>Tracking fields not surfaced on <see cref="PlaybookResponse"/></b>:
///   the model does NOT expose <c>sprk_indexstatus</c>, <c>sprk_indexhash</c>, or
///   <c>sprk_lastindexedat</c>. Comparison + skip-on-Pending/Failed/NotIndexed therefore
///   cannot run end-to-end until the model is extended. Task 035 (admin view) shares
///   this dependency.</description></item>
///   <item><description><b>No per-tenant scoping in existing handlers</b>: tenancy in this
///   codebase flows via <see cref="JobContract.SubjectId"/> + <see cref="JobContract.CorrelationId"/>;
///   <see cref="IPlaybookService"/> does not currently take a tenant parameter. The producer
///   is expected to enqueue one job per tenant if multi-tenant scoping is required.</description></item>
///   <item><description><b>Write-back path</b>: <see cref="IPlaybookService"/> has no
///   public method to write <c>sprk_indexstatus</c> / <c>sprk_lastindexerror</c> on the
///   playbook row. The marked TODO at <see cref="MarkStaleAsync"/> documents the
///   contract a future <c>UpdateIndexStatusAsync(playbookId, statusCode, error?)</c> method
///   must satisfy.</description></item>
/// </list>
/// <para>
/// Until these gaps are closed, the job runs with the loop wired to the existing
/// <see cref="IPlaybookService.ListPublicPlaybooksAsync"/> placeholder enumeration. The
/// shape is correct — counts + telemetry + handler contract — so once the gaps are
/// closed, the job runs end-to-end without further refactor.
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
    /// Enumerates active playbooks for drift evaluation.
    /// </summary>
    /// <remarks>
    /// TODO (task 034 follow-up): replace this with a dedicated
    /// <c>IPlaybookService.ListAllActivePlaybooksAsync(CancellationToken)</c> overload that:
    /// <list type="bullet">
    ///   <item><description>Pages through ALL <c>statecode=0</c> playbooks (no owner / IsPublic filter)</description></item>
    ///   <item><description>Includes <c>sprk_indexstatus</c>, <c>sprk_indexhash</c>, <c>sprk_lastindexedat</c> in the projection (see Open scoping gap #2)</description></item>
    ///   <item><description>Streams via <see cref="IAsyncEnumerable{T}"/> to bound memory on large tenants</description></item>
    /// </list>
    /// Placeholder uses <see cref="IPlaybookService.ListPublicPlaybooksAsync"/> (single page,
    /// 100 max) so the job is runnable for smoke tests and the shape stays correct.
    /// </remarks>
    private async IAsyncEnumerable<PlaybookResponse> EnumerateActivePlaybooksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var query = new PlaybookQueryParameters { Page = 1, PageSize = 100 };
        var page = await _playbookService.ListPublicPlaybooksAsync(query, ct);

        foreach (var summary in page.Items)
        {
            ct.ThrowIfCancellationRequested();
            var full = await _playbookService.GetPlaybookAsync(summary.Id, ct);
            if (full is not null)
            {
                yield return full;
            }
        }
    }

    /// <summary>
    /// Reads the <c>sprk_indexstatus</c> numeric option code from the playbook.
    /// </summary>
    /// <remarks>
    /// TODO (task 034 follow-up — Open scoping gap #2): <see cref="PlaybookResponse"/>
    /// does not yet expose <c>sprk_indexstatus</c>. Until it does, this method returns
    /// <see cref="IndexStatusNotIndexed"/> so the skip-on-non-Indexed guard fires
    /// (no false stale flagging). Extending the model is a one-line PlaybookDto change
    /// + a one-line projection change in <see cref="PlaybookService"/>.
    /// </remarks>
    private static int GetIndexStatusCode(PlaybookResponse playbook)
    {
        _ = playbook;
        return IndexStatusNotIndexed;
    }

    /// <summary>
    /// Reads the <c>sprk_indexhash</c> string from the playbook.
    /// </summary>
    /// <remarks>
    /// TODO (task 034 follow-up — Open scoping gap #2): see <see cref="GetIndexStatusCode"/>.
    /// </remarks>
    private static string? GetStoredIndexHash(PlaybookResponse playbook)
    {
        _ = playbook;
        return null;
    }

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
    /// Writes <c>sprk_indexstatus = Stale</c> on the playbook row.
    /// </summary>
    /// <remarks>
    /// TODO (task 034 follow-up — Open scoping gap #4): <see cref="IPlaybookService"/> does
    /// not currently expose a tracking-field write. Until it does, this method only logs
    /// (ADR-015 safe — playbook ID only, no content) and returns. The contract for the
    /// future <c>UpdateIndexStatusAsync(Guid playbookId, int statusCode, string? lastError, CancellationToken)</c>
    /// method is: PATCH <c>sprk_indexstatus</c> + <c>sprk_lastindexerror</c> + (optionally)
    /// <c>sprk_lastindexedat</c> via the existing OData PATCH path used by
    /// <see cref="IPlaybookService.UpdatePlaybookAsync"/>.
    /// </remarks>
    private Task MarkStaleAsync(Guid playbookId, CancellationToken ct)
    {
        _logger.LogInformation(
            "Drift detected for playbook {PlaybookId} — would set sprk_indexstatus={StaleCode} (write-back gap; see task 034 report)",
            playbookId, IndexStatusStale);
        return Task.CompletedTask;
    }
}
