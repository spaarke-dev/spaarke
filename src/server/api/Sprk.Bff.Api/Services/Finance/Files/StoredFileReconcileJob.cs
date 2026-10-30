using System.Text.Json;
using Azure;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Finance.Files;

/// <summary>Retention constants of the Finance file store shared by the job and the start-up validator.</summary>
public static class StoredFileRetention
{
    /// <summary>
    /// The invoice submission job's longest retry horizon: every delivery the broker makes (<see cref="JobContract.MaxDeliveryCount"/>)
    /// may hold its per-submission lock for <see cref="InvoiceSubmissionProcessingJobHandler.LockDuration"/>. A staged blob younger than
    /// this may still gain its row, so <c>Finance:FileStore:OrphanGraceMinutes</c> may not be shorter (start-up validation).
    /// </summary>
    public static TimeSpan SubmissionRetryHorizon { get; } =
        TimeSpan.FromTicks(InvoiceSubmissionProcessingJobHandler.LockDuration.Ticks * JobContract.MaxDeliveryCount);
}

/// <summary>
/// The Finance file store's ONE scheduled job (ebilling-r1 task 090; procedure-and-file-model section 4.2; ADR-036 A1, ADR-052):
/// (A) a Stored File row whose parent lookups are all null (the parent was deleted: RemoveLink; confirmed by a not-found read of the
/// parent its path names) or whose parent read is a successful not-found → its blob (only one carrying this environment's marker),
/// then the row, are deleted; (B) a blob with no row (a successful not-found) older than the grace → deleted,
/// unless its metadata names a submission that is not yet terminal; (C) the detector: rows still Not yet servable after
/// <c>NotServableAlertMinutes</c> → one warning event with the count and ids, no repair.
/// </summary>
/// <remarks>
/// <para><b>Fail safe.</b> Every delete needs a SUCCESSFUL answer that the thing is gone; a fault skips the unit (counted), and the
/// next hourly run revisits it. A blob whose name is not this tenant's stored-file name is never touched.</para>
/// <para><b>ADR-036 A1.</b> Registered through <c>AddScheduledJob</c> (rule 6); one dispatch per tick under the host's lease (rule 1);
/// a scope per run; one heartbeat per attempt with its counts (rule 5); it THROWS when no progress is possible (the row query or the
/// blob listing faults, rule 4), otherwise counts unit failures and completes. Rule 3: each unit's side effects are deletes, ordered
/// blob-then-row and idempotent (a delete of something already gone succeeds), so a re-run under the lease converges and a second
/// run over the same state does nothing new; no claim row is needed. The eBilling flag is read by the job itself (ADR-018), and so is the dedicated <c>Ebilling:StoredFileReconcileEnabled</c> switch (default off; hard rule: off until F4 is merged and DEF-044 is fixed).</para>
/// </remarks>
public sealed class StoredFileReconcileJob : IScheduledJob
{
    public const string JobIdConstant = "finance-stored-file-reconcile";

    /// <summary>Hourly, at minute 23 (off the top of the hour).</summary>
    internal const string DefaultCronSchedule = "23 * * * *";

    /// <summary>The detector's warning event.</summary>
    public static readonly EventId NotServableDetected = new(9091, "StoredFileNotServableDetected");

    /// <summary>The per-attempt heartbeat.</summary>
    public static readonly EventId Heartbeat = new(9092, "StoredFileReconcileHeartbeat");

    /// <summary>Rows per page of the row read (a test may lower it to exercise paging).</summary>
    internal int PageSize { get; init; } = 5000;

    /// <summary>Most pages read per run; later rows wait for the next run (a test may lower it).</summary>
    internal int MaxPages { get; init; } = 50;

    internal const int ParentBatchSize = 200;
    internal const int MaxIdsInDetectorEvent = 50;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StoredFileBlobs _blobs;
    private readonly IOptions<EbillingOptions> _ebilling;
    private readonly IOptions<FinanceOptions> _finance;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StoredFileReconcileJob> _logger;

    public StoredFileReconcileJob(
        IServiceScopeFactory scopeFactory,
        StoredFileBlobs blobs,
        IOptions<EbillingOptions> ebilling,
        IOptions<FinanceOptions> finance,
        TimeProvider timeProvider,
        ILogger<StoredFileReconcileJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _finance = finance ?? throw new ArgumentNullException(nameof(finance));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Finance Stored File Reconcile";

    public string Description =>
        "Deletes the blob and row of a Stored File whose parent is gone, deletes blobs with no row after the grace (never a "
        + "non-terminal submission's), and reports rows still Not yet servable (ebilling-r1 task 090).";

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var counts = new ReconcileCounts();

        // The dedicated switch is read first, before anything else: this job deletes, and a standing hard rule keeps it off until
        // fix F4 is merged and DEF-044 is fixed (EbillingOptions.StoredFileReconcileEnabled).
        if (!_ebilling.Value.StoredFileReconcileEnabled || !_ebilling.Value.Enabled || !_blobs.IsConfigured)
        {
            counts.SkippedReason = !_ebilling.Value.StoredFileReconcileEnabled
                ? "reconcile-disabled"
                : !_ebilling.Value.Enabled ? "ebilling-disabled" : "store-not-configured";
            LogHeartbeat(context, counts);
            return new JobRunResult(true, null, 0, _timeProvider.GetElapsedTime(started), Serialize(counts), Skipped: true);
        }

        var fileStore = _finance.Value.FileStore ?? new FinanceFileStoreOptions();
        var now = _timeProvider.GetUtcNow();
        var grace = TimeSpan.FromMinutes(fileStore.OrphanGraceMinutes);
        var alertAfter = TimeSpan.FromMinutes(fileStore.NotServableAlertMinutes);

        try
        {
            // The name segment must be this environment's OrganizationId before anything is judged: a mismatch (or a fault reading
            // it) means no progress is possible (A1-4: throw).
            await _blobs.EnsureEnvironmentAsync(cancellationToken).ConfigureAwait(false);

            using var scope = _scopeFactory.CreateScope();
            var entities = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();

            // Rows first: a fault here means no progress is possible this attempt (ADR-036 A1-4: throw, the host retries).
            var rows = await ReadAllRowsAsync(entities, counts, cancellationToken).ConfigureAwait(false);

            // (A) rows whose parent is gone.
            await ReconcileRowsAsync(entities, rows, counts, cancellationToken).ConfigureAwait(false);

            // (C) the detector, over the rows read (no extra query).
            DetectNotServable(rows, now, alertAfter, counts, context);

            // (B) blobs with no row. A listing fault also throws (A1-4).
            var rowIds = rows.Select(r => r.Id).ToHashSet();
            await ReconcileBlobsAsync(entities, rowIds, now, grace, counts, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A1-5: one heartbeat per attempt, the failed one included, then the host's retry.
            counts.Failed = true;
            LogHeartbeat(context, counts);
            throw;
        }

        LogHeartbeat(context, counts);
        return new JobRunResult(
            true,
            counts.UnitFaults > 0 ? $"{counts.UnitFaults} units faulted and were skipped; the next run revisits them." : null,
            counts.RowsDeleted + counts.BlobsDeleted,
            _timeProvider.GetElapsedTime(started),
            Serialize(counts));
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // (A) rows
    // ------------------------------------------------------------------------------------------------------------------------------

    private async Task<List<RowFacts>> ReadAllRowsAsync(IGenericEntityService entities, ReconcileCounts counts, CancellationToken ct)
    {
        var query = new QueryExpression(StoredFileSchema.Entity)
        {
            ColumnSet = new ColumnSet(
                StoredFileSchema.IdColumn, StoredFileSchema.Purpose, StoredFileSchema.Status, StoredFileSchema.BlobPath, StoredFileSchema.CreatedOn,
                StoredFileSchema.ProcedureVersionTable, StoredFileSchema.InvoiceTable, StoredFileSchema.BillingExportTable),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
        };
        query.AddOrder(StoredFileSchema.IdColumn, OrderType.Ascending);

        var rows = new List<RowFacts>();
        for (var page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities.Select(RowFacts.From));
            if (!result.MoreRecords)
            {
                break;
            }

            if (page >= MaxPages)
            {
                // Not a failure of the rows read: (A) and (C) cover what was read, and (B) re-reads each candidate row anyway.
                counts.RowsTruncated = true;
                _logger.LogWarning("Stored file reconcile read {Pages} pages of {PageSize} rows and stopped; later rows wait for the next run", MaxPages, PageSize);
                break;
            }

            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }

        counts.RowsExamined = rows.Count;
        return rows;
    }

    private async Task ReconcileRowsAsync(IGenericEntityService entities, List<RowFacts> rows, ReconcileCounts counts, CancellationToken ct)
    {
        // A row whose always-written columns did not come back was not READABLE to this identity (field security masks a column
        // the caller may not read as absent, not as an error): its "no parent" proves nothing. Skipped, counted, and reported.
        var readable = rows.Where(r => r.Readable).ToList();
        counts.RowsUnreadable = rows.Count - readable.Count;
        if (counts.RowsUnreadable > 0)
        {
            _logger.LogWarning(
                "Stored file reconcile: {Count} rows came back without their status, purpose or blob path (field security?); they are never treated as parentless",
                counts.RowsUnreadable);
        }

        // Which set parents are confirmed gone. A batched query per table finds the candidates (a parent the query did not return),
        // but a set query also omits rows the app identity cannot SEE, so each candidate is confirmed by a read of its own: only
        // that read's not-found (0x80040217) means gone. A fault (batch or read) leaves the parent "unknown" (kept).
        var gone = new HashSet<(string Table, Guid Id)>();
        var unknown = new HashSet<(string Table, Guid Id)>();
        foreach (var table in StoredFileSchema.ParentLookups)
        {
            var ids = readable.SelectMany(r => r.Parents).Where(p => p.Table == table).Select(p => p.Id).Distinct().ToList();
            foreach (var batch in ids.Chunk(ParentBatchSize))
            {
                ct.ThrowIfCancellationRequested();
                List<Guid> candidates;
                try
                {
                    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(table + "id") };
                    query.Criteria.AddCondition(table + "id", ConditionOperator.In, batch.Cast<object>().ToArray());
                    var found = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.Select(e => e.Id).ToHashSet();
                    candidates = batch.Where(id => !found.Contains(id)).ToList();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    counts.UnitFaults += batch.Length;
                    foreach (var id in batch)
                    {
                        unknown.Add((table, id));
                    }

                    _logger.LogWarning(ex, "Stored file reconcile: the {Table} existence check faulted for {Count} parents; their files are kept this run", table, batch.Length);
                    continue;
                }

                foreach (var id in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await entities.RetrieveAsync(table, id, [table + "id"], ct).ConfigureAwait(false);

                        // Readable by id: it exists (the set query did not return it); never "gone".
                    }
                    catch (Exception ex) when (StoredFileNotFound.Is(ex))
                    {
                        gone.Add((table, id));
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        counts.UnitFaults++;
                        unknown.Add((table, id));
                        _logger.LogWarning(ex, "Stored file reconcile: confirming {Table} {ParentId} is gone faulted; its files are kept this run", table, id);
                    }
                }
            }
        }

        foreach (var row in readable)
        {
            ct.ThrowIfCancellationRequested();
            var parentless = row.Parents.Count == 0;
            var parentsGone = row.Parents.Count > 0 && row.Parents.All(p => gone.Contains((p.Table, p.Id)));
            if (!parentless && !parentsGone)
            {
                continue;
            }

            if (row.Parents.Any(p => unknown.Contains((p.Table, p.Id))))
            {
                continue;
            }

            try
            {
                var pathParsed = StoredFileBlobName.TryParseForTenant(row.BlobPath, _blobs.TenantId, out var pathParent, out var pathFile);

                // uac-r2 review round 1: "every lookup null" is RemoveLink's mark, but it is not itself an answer that the parent is
                // gone. The parent the row's own path names is read first, as the parent-gone pass does: only its not-found deletes.
                // A path that names no parent of this tenant gives nothing to confirm, so the row is kept (fail safe).
                if (parentless && (!pathParsed || !await ParentConfirmedGoneAsync(entities, pathParent, ct).ConfigureAwait(false)))
                {
                    counts.RowsParentlessKept++;
                    _logger.LogWarning(
                        "Stored file reconcile: file {FileId} has no parent lookup but its path's parent is not confirmed gone; kept", row.Id);
                    continue;
                }

                // Blob first, then the row: a crash in between leaves a row whose blob is gone, which the next run deletes again
                // (the blob delete of something already gone succeeds). The reverse order could orphan bytes with no row to find them.
                // Only the row's OWN blob (its file id in the name): a path naming another file is never followed to a delete.
                if (pathParsed && pathFile == row.Id)
                {
                    // uac-r2 review round 1 F1: the same environment check as the row-less pass. Another environment sharing the
                    // account and the name segment (a copy of this environment's rows) may still serve these bytes: a blob without
                    // THIS deployment's marker is never deleted, and neither is the row. The row is kept on purpose: it is the only
                    // record of the blob, it cannot be served (its parent is gone), and deleting it would leave bytes no pass may
                    // ever judge again (the row-less pass refuses an unmarked blob too); kept, it is counted on every run.
                    var container = ContainerOfTable(pathParent.Kind);
                    var properties = await _blobs.GetPropertiesAsync(container, row.BlobPath!, ct).ConfigureAwait(false);
                    if (properties is not null && !_blobs.IsThisEnvironments(properties.Metadata))
                    {
                        counts.BlobsOfOtherEnvironments++;
                        _logger.LogWarning(
                            "Stored file reconcile: file {FileId} (parent gone) has a blob without this environment's marker; blob and row kept",
                            row.Id);
                        continue;
                    }

                    // uac-r2 round 2 (K1): the delete is pinned to the ETag the marker was judged on. A blob rewritten in between
                    // (412) is not the blob that was judged: blob and row are kept, counted, and judged again next run.
                    try
                    {
                        if (properties is not null && await _blobs.DeleteAsync(container, row.BlobPath!, properties.ETag, ct).ConfigureAwait(false))
                        {
                            counts.BlobsDeleted++;
                        }
                    }
                    catch (RequestFailedException ex) when (ChangedSinceJudged(ex))
                    {
                        counts.BlobsChangedSinceJudged++;
                        _logger.LogWarning(
                            "Stored file reconcile: file {FileId} (parent gone): its blob changed after it was judged (412); blob and row kept for the next run",
                            row.Id);
                        continue;
                    }
                }
                else
                {
                    counts.RowsWithForeignPath++;
                }

                await DeleteRowAsync(entities, row.Id, ct).ConfigureAwait(false);
                counts.RowsDeleted++;
                _logger.LogInformation("Stored file reconcile deleted file {FileId}: its parent is gone", row.Id);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                counts.UnitFaults++;
                _logger.LogWarning(ex, "Stored file reconcile: deleting file {FileId} (parent gone) faulted; kept for the next run", row.Id);
            }
        }
    }

    /// <summary>True only on a successful not-found read of <paramref name="parent"/>; it exists → false; a fault throws (unit skipped).</summary>
    private static async Task<bool> ParentConfirmedGoneAsync(IGenericEntityService entities, StoredFileParent parent, CancellationToken ct)
    {
        try
        {
            await entities.RetrieveAsync(parent.Table, parent.Id, [parent.Table + "id"], ct).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return true;
        }
    }

    private static async Task DeleteRowAsync(IGenericEntityService entities, Guid id, CancellationToken ct)
    {
        try
        {
            await entities.DeleteAsync(StoredFileSchema.Entity, id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            // Already gone: the unit is done.
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // (C) detector
    // ------------------------------------------------------------------------------------------------------------------------------

    private void DetectNotServable(List<RowFacts> rows, DateTimeOffset now, TimeSpan alertAfter, ReconcileCounts counts, JobRunContext context)
    {
        var stuck = rows
            .Where(r => r.Status == (int)StoredFileStatus.NotYetServable && r.CreatedOn is { } created && now - created > alertAfter)
            .Select(r => r.Id)
            .ToList();
        counts.NotServableDetected = stuck.Count;
        if (stuck.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            NotServableDetected,
            "Stored files still Not yet servable after {Minutes} minutes: {Count} (first ids {FileIds}); no repair is made. correlationId={CorrelationId}",
            (int)alertAfter.TotalMinutes, stuck.Count, string.Join(",", stuck.Take(MaxIdsInDetectorEvent)), context.CorrelationId);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // (B) blobs
    // ------------------------------------------------------------------------------------------------------------------------------

    private async Task ReconcileBlobsAsync(
        IGenericEntityService entities, HashSet<Guid> rowIds, DateTimeOffset now, TimeSpan grace, ReconcileCounts counts, CancellationToken ct)
    {
        await foreach (var blob in _blobs.ListAllAsync(ct).ConfigureAwait(false))
        {
            counts.BlobsExamined++;
            if (rowIds.Contains(blob.FileId))
            {
                continue;
            }

            var age = blob.LastModified ?? blob.CreatedOn;
            if (age is not { } stamp || now - stamp < grace)
            {
                counts.BlobsInGrace++;
                continue;
            }

            // Another environment sharing the account (and tenant id) has rows this one cannot see: its blobs are never ours to
            // judge. Only a blob staged by THIS deployment (its environment marker) may be deleted as row-less.
            if (!_blobs.IsThisEnvironments(blob.Metadata))
            {
                counts.BlobsOfOtherEnvironments++;
                continue;
            }

            try
            {
                // The row read above may be a page old: ask again, and act only on a successful not-found.
                if (await RowExistsAsync(entities, blob.FileId, ct).ConfigureAwait(false))
                {
                    continue;
                }

                if (!await SubmissionAllowsDeleteAsync(entities, blob, ct).ConfigureAwait(false))
                {
                    counts.BlobsOfOpenSubmissions++;
                    continue;
                }

                // uac-r2 round 2 (K1): the delete is pinned to the ETag the listing judged (age, marker, submission). A listing
                // without one gives nothing to pin to, so the blob is kept.
                if (blob.ETag is not { } judged)
                {
                    counts.UnitFaults++;
                    _logger.LogWarning("Stored file reconcile: blob {Container}/{BlobName} was listed without an ETag; kept for the next run", blob.Container, blob.BlobPath);
                    continue;
                }

                if (await _blobs.DeleteAsync(blob.Container, blob.BlobPath, judged, ct).ConfigureAwait(false))
                {
                    counts.BlobsDeleted++;
                    _logger.LogInformation("Stored file reconcile deleted row-less blob {Container}/{BlobName}", blob.Container, blob.BlobPath);
                }
            }
            catch (RequestFailedException ex) when (ChangedSinceJudged(ex))
            {
                counts.BlobsChangedSinceJudged++;
                _logger.LogWarning(
                    "Stored file reconcile: row-less blob {Container}/{BlobName} changed after it was listed (412); kept for the next run",
                    blob.Container, blob.BlobPath);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                counts.UnitFaults++;
                _logger.LogWarning(ex, "Stored file reconcile: blob {Container}/{BlobName} could not be judged; kept for the next run", blob.Container, blob.BlobPath);
            }
        }
    }

    /// <summary>A delete refused because the blob's ETag is no longer the one judged (If-Match failed): kept, not a fault.</summary>
    private static bool ChangedSinceJudged(RequestFailedException ex) => ex.Status == 412;

    private static async Task<bool> RowExistsAsync(IGenericEntityService entities, Guid fileId, CancellationToken ct)
    {
        try
        {
            await entities.RetrieveAsync(StoredFileSchema.Entity, fileId, [StoredFileSchema.IdColumn], ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// True when the blob's submission (metadata) is terminal or gone, or the blob names none. An unparseable submission id keeps the
    /// blob (fail safe); a fault throws (the unit is skipped).
    /// </summary>
    private static async Task<bool> SubmissionAllowsDeleteAsync(IGenericEntityService entities, StoredBlobListing blob, CancellationToken ct)
    {
        if (!blob.Metadata.TryGetValue(StoredFileBlobs.MetadataSubmission, out var raw))
        {
            return true;
        }

        if (!Guid.TryParse(raw, out var submissionId) || submissionId == Guid.Empty)
        {
            return false;
        }

        try
        {
            var submission = await entities.RetrieveAsync(
                FinanceBaseValues.SubmissionEntity, submissionId, [InvoiceSubmissionPersister.SubmissionStatus], ct).ConfigureAwait(false);
            var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
            return status is (int)InvoiceSubmissionStatus.Accepted or (int)InvoiceSubmissionStatus.Rejected or (int)InvoiceSubmissionStatus.Failed;
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return true;
        }
    }

    private static string ContainerOfTable(StoredFileParentKind kind) => kind switch
    {
        StoredFileParentKind.ProcedureVersion => StoredFileSchema.ContainerOf(StoredFilePurpose.ProcedureSource),
        StoredFileParentKind.Invoice => StoredFileSchema.ContainerOf(StoredFilePurpose.InvoicePdf),
        _ => StoredFileSchema.ContainerOf(StoredFilePurpose.ExportFile),
    };

    private void LogHeartbeat(JobRunContext context, ReconcileCounts counts) =>
        _logger.LogInformation(
            Heartbeat,
            "Stored file reconcile attempt {Attempt}: failed={Failed} skipped={Skipped} rows={Rows} unreadable={Unreadable} rowsDeleted={RowsDeleted} "
            + "blobs={Blobs} blobsDeleted={BlobsDeleted} inGrace={InGrace} openSubmissions={OpenSubmissions} otherEnvironments={OtherEnvironments} "
            + "changedSinceJudged={ChangedSinceJudged} notServable={NotServable} unitFaults={UnitFaults} truncated={Truncated} correlationId={CorrelationId}",
            context.Attempt, counts.Failed, counts.SkippedReason ?? "no", counts.RowsExamined, counts.RowsUnreadable, counts.RowsDeleted,
            counts.BlobsExamined, counts.BlobsDeleted, counts.BlobsInGrace, counts.BlobsOfOpenSubmissions, counts.BlobsOfOtherEnvironments,
            counts.BlobsChangedSinceJudged, counts.NotServableDetected, counts.UnitFaults, counts.RowsTruncated, context.CorrelationId);

    private static string Serialize(ReconcileCounts counts) => JsonSerializer.Serialize(counts, ResultJsonOptions);

    /// <summary>The run's counts (ResultJson).</summary>
    internal sealed class ReconcileCounts
    {
        public string? SkippedReason { get; set; }
        public bool Failed { get; set; }
        public int RowsExamined { get; set; }
        public int RowsUnreadable { get; set; }
        public bool RowsTruncated { get; set; }
        public int RowsDeleted { get; set; }
        public int RowsWithForeignPath { get; set; }
        public int RowsParentlessKept { get; set; }
        public int BlobsExamined { get; set; }
        public int BlobsDeleted { get; set; }
        public int BlobsInGrace { get; set; }
        public int BlobsOfOpenSubmissions { get; set; }
        public int BlobsOfOtherEnvironments { get; set; }

        /// <summary>Deletes refused with 412: the blob changed after it was judged (uac-r2 round 2, K1). Kept, not a fault.</summary>
        public int BlobsChangedSinceJudged { get; set; }
        public int NotServableDetected { get; set; }
        public int UnitFaults { get; set; }
    }

    private sealed record RowFacts(
        Guid Id, int? Status, int? Purpose, string? BlobPath, DateTimeOffset? CreatedOn, IReadOnlyList<(string Table, Guid Id)> Parents)
    {
        /// <summary>
        /// The columns every row is created with (BuildRow) came back. A field-secured column the app identity may not read comes back
        /// ABSENT, so a row missing any of them is not evidence of a cleared parent lookup.
        /// </summary>
        public bool Readable => Status is not null && Purpose is not null && !string.IsNullOrEmpty(BlobPath);

        public static RowFacts From(Entity entity)
        {
            var parents = StoredFileSchema.ParentLookups
                .Select(column => (Table: column, Ref: entity.GetAttributeValue<EntityReference>(column)))
                .Where(x => x.Ref is not null && x.Ref.Id != Guid.Empty)
                .Select(x => (x.Table, x.Ref!.Id))
                .ToList();
            var created = entity.GetAttributeValue<DateTime?>(StoredFileSchema.CreatedOn);
            return new RowFacts(
                entity.Id,
                entity.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status)?.Value,
                entity.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose)?.Value,
                entity.GetAttributeValue<string>(StoredFileSchema.BlobPath),
                created is { } c ? new DateTimeOffset(DateTime.SpecifyKind(c, DateTimeKind.Utc)) : null,
                parents);
        }
    }
}
