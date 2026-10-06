using System.Text.Json;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// unified-access-control-r2 task 166 f1 (owner round 21 item 1 (ii), round 26 item 3) — the LEGACY MIGRATION of document
/// files into their derived containers, and the gate the strict document-pointer rule is flipped behind.
/// </summary>
/// <remarks>
/// <para><b>What it does per document</b> (every <c>sprk_document</c> that carries a pointer, in id order):
/// <see cref="DocumentContainerRelocator.RelocateIfMisplacedAsync"/> with <see cref="RelocationPurpose.LegacyMigration"/>
/// — report-only unless writes are enabled — and then the two document-pointer rules on the (possibly new) pointer:
/// the round-23 INTERIM rule that is in force and the STRICT derived-container rule waiting behind
/// <see cref="RecordContainerResolver.StrictDerivedContainerKey"/>.</para>
/// <para><b>The flip gate.</b> A document the interim rule SERVES and the strict rule would REFUSE is a document the flip
/// would newly refuse (<c>wouldNewlyRefuse</c>). A pass with zero of those, zero planned moves, zero failures, zero
/// relocations still owing a step (<c>pending</c>: a source delete, a re-key or the index — task 166 f1-v1, owner round
/// 37) and zero relocated files the rule in force refuses (<c>relocatedButRefused</c>; round 37 item 3 makes every
/// relocated file servable under the interim rule) is the evidence that the flag may be flipped:
/// <c>scripts/Invoke-DocumentContainerMigration.ps1 -Verify</c> exits 0 only then. Documents BOTH rules refuse (a file that
/// is not verifiably the row's own, a missing item) are listed for an administrator; the flip changes nothing for them.
/// Documents the interim rule refuses and the strict rule would serve (<c>servedOnlyAfterFlip</c> — the census classes the
/// interim rule refuses by design) are counted and listed, so no disagreement between the two rules is invisible. Every
/// source kept because rows of other records still use it is listed (<c>sourceKeptForOtherRecords</c>). Two STATED
/// outcomes of owner round 45 are counted and listed without blocking a clean run: a source edited after its move
/// (<c>sourceChangedAfterMove</c>, with the row id — the relocation re-copies it itself; one it could not close yet is
/// also <c>pending</c>) and a moved file's history the target container's version limit truncated
/// (<c>versionsTruncated</c>, with counts).</para>
/// <para><b>Re-entry.</b> Each document's relocation ledger is settled when the pass reaches it (write mode): a source
/// whose delete failed, a source kept for a row that is now gone, a re-key or an index step that did not complete — so a
/// repeat pass completes what an earlier one (or a Make Secure call) left owing.</para>
/// <para><b>The script decides nothing</b> (148's <c>Invoke-SecureChildBackfill.ps1</c> precedent): it triggers this job
/// through <c>POST /api/admin/jobs/document-container-migration/trigger</c> (SystemAdmin) and reads the run reports. No
/// Graph or Dataverse logic lives in PowerShell.</para>
/// <para><b>Writes</b> only when <see cref="WritesEnabledKey"/> is true (an App Service setting the script sets for
/// <c>-Apply</c> and removes in a <c>finally</c>). Registered DISABLED: it runs only when triggered.</para>
/// <para><b>Batches.</b> One run examines at most <see cref="MaxDocumentsPerRunKey"/> documents (default
/// <see cref="DefaultMaxDocumentsPerRun"/>) after a per-instance cursor; a run that reaches the last document reports
/// <c>passComplete</c> and resets the cursor. Each report says where it started (<c>startAfter</c>, null = the first
/// document) and ended (<c>endAt</c>), so the script can prove a pass was contiguous.</para>
/// <para><b>ADR-036 A1.</b> Rule 1: the scheduler's lease — one run at a time. Rule 3: every step is keyed on observed
/// state (a relocated file is InPlace next time), so a re-run completes and repeats nothing. Rule 4: an enumeration fault
/// throws (retryable); a per-document fault is recorded as Failed and the run continues. Rule 5: one heartbeat per
/// attempt. Rule 6: <c>AddScheduledJob</c> in <c>DocumentsModule</c>.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — the work IS the BFF's
/// container decisions and its app-only SPE and Dataverse identity, low volume (one pass per environment), operator
/// triggered. No new package, store or host.</para>
/// </remarks>
public sealed class DocumentContainerMigrationJob : IScheduledJob
{
    /// <summary>Stable job id — the admin trigger and history routes key off it.</summary>
    public const string JobIdConstant = "document-container-migration";

    /// <summary>Never fires on its own: registered disabled; the schedule is only the scheduler's required shape.</summary>
    internal const string DefaultCronSchedule = "0 3 * * *";

    /// <summary>The App Service setting that lets a run WRITE (<c>DocumentContainerMigration__WritesEnabled</c>).</summary>
    public const string WritesEnabledKey = "DocumentContainerMigration:WritesEnabled";

    /// <summary>Per-run batch size (<c>DocumentContainerMigration__MaxDocumentsPerRun</c>).</summary>
    public const string MaxDocumentsPerRunKey = "DocumentContainerMigration:MaxDocumentsPerRun";

    internal const int DefaultMaxDocumentsPerRun = 100;

    /// <summary>The report lists at most this many rows (the admin history surface stays small); all are counted.</summary>
    internal const int MaxListedPerRun = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentContainerMigrationJob> _logger;

    /// <summary>The last document of the previous run of this pass; null = the next run starts the pass.</summary>
    private Guid? _cursor;

    public DocumentContainerMigrationJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<DocumentContainerMigrationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Document Container Migration";

    /// <inheritdoc />
    public string Description =>
        "Moves each document's file into the container derived for its record (copy, verify, re-point, delete source) and "
        + "reports, per document, whether the strict document-pointer rule would refuse anything the interim rule serves. "
        + "Report-only unless DocumentContainerMigration:WritesEnabled is true. Triggered by "
        + "scripts/Invoke-DocumentContainerMigration.ps1 (task 166).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var writes = bool.TryParse(_configuration[WritesEnabledKey], out var w) && w;
        var batch = int.TryParse(_configuration[MaxDocumentsPerRunKey], out var b) && b > 0 ? b : DefaultMaxDocumentsPerRun;
        var startAfter = _cursor;

        using var scope = _scopeFactory.CreateScope();
        var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
        var relocator = scope.ServiceProvider.GetRequiredService<DocumentContainerRelocator>();
        var resolver = scope.ServiceProvider.GetRequiredService<RecordContainerResolver>();

        IReadOnlyList<Guid> documentIds;
        try
        {
            documentIds = await ReadBatchAsync(dataverse, startAfter, batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[DOCUMENT-MIGRATION] heartbeat status=error documents could not be enumerated attempt={Attempt} "
                + "correlationId={CorrelationId}", context.Attempt, context.CorrelationId);
            throw new InvalidOperationException("The documents to migrate could not be enumerated.", ex);
        }

        var report = new MigrationReport(writes ? "write" : "report-only", startAfter);
        foreach (var documentId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExamineAsync(relocator, resolver, documentId, writes, report, cancellationToken).ConfigureAwait(false);
        }

        var passComplete = documentIds.Count < batch;
        _cursor = passComplete ? null : documentIds[^1];
        report.EndAt = documentIds.Count > 0 ? documentIds[^1] : startAfter;
        report.PassComplete = passComplete;

        var duration = _timeProvider.GetElapsedTime(started);
        // "Nothing left to do" for this batch: no planned move, no failure, nothing still owed by a move, no relocated file
        // the rule in force refuses, and no document the flip would newly refuse.
        var clean = report.IsClean;

        // THE HEARTBEAT (ADR-036 A1 rule 5).
        _logger.Log(
            clean ? LogLevel.Information : LogLevel.Warning,
            "[DOCUMENT-MIGRATION] heartbeat mode={Mode} examined={Examined} wouldNewlyRefuse={WouldNewlyRefuse} "
            + "relocatedButRefused={RelocatedButRefused} pending={Pending} servedOnlyAfterFlip={ServedOnlyAfterFlip} "
            + "sourceChangedAfterMove={SourceChangedAfterMove} versionsTruncated={VersionsTruncated} "
            + "counts={Counts} passComplete={PassComplete} attempt={Attempt} durationMs={DurationMs} trigger={Trigger} "
            + "runId={RunId} correlationId={CorrelationId}",
            report.Mode, report.Examined, report.WouldNewlyRefuse, report.RelocatedButRefused, report.Pending,
            report.ServedOnlyAfterFlip, report.SourceChangedAfterMove, report.VersionsTruncated,
            JsonSerializer.Serialize(report.Counts), passComplete,
            context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: clean,
            ErrorMessage: clean ? null : $"{report.WouldNewlyRefuse} document(s) the strict rule would newly refuse; "
                                         + $"{report.RelocatedButRefused} relocated file(s) the rule in force refuses; "
                                         + $"{report.Pending} relocation(s) still owing a step; "
                                         + $"{report.Counts.GetValueOrDefault(nameof(RelocationState.WouldRelocate))} planned "
                                         + $"move(s); {report.Counts.GetValueOrDefault(nameof(RelocationState.Failed))} failed.",
            ProcessedItems: report.Examined,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(report.ToJson()));
    }

    /// <summary>One document: relocate (or plan to), then evaluate both rules on the pointer it ends with.</summary>
    internal static async Task ExamineAsync(
        DocumentContainerRelocator relocator, RecordContainerResolver resolver, Guid documentId, bool writes,
        MigrationReport report, CancellationToken ct)
    {
        DocumentRelocationOutcome outcome;
        try
        {
            outcome = await relocator.RelocateIfMisplacedAsync(documentId, writes, RelocationPurpose.LegacyMigration, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            outcome = new DocumentRelocationOutcome(
                documentId, RelocationState.Failed, null, null, null, null, $"relocation faulted ({ex.GetType().Name})");
        }

        bool? interim = null;
        bool? strict = null;
        if (outcome.State is not (RelocationState.NoFile or RelocationState.Failed))
        {
            // The pointer the row ends with: the new one after a move, the current one otherwise.
            var (drive, item) = outcome.FinalPointer;
            interim = await resolver.IsAllowedUnderInterimRuleAsync(documentId, drive, item, ct).ConfigureAwait(false);
            // A planned move ends in the derived container by construction, so the strict rule is asked of the pointer
            // only when nothing is planned.
            strict = outcome.State == RelocationState.WouldRelocate
                || await resolver.IsAllowedUnderStrictRuleAsync(documentId, drive, item, ct).ConfigureAwait(false);
        }

        report.Add(outcome, interim, strict, resolver.StrictDerivedContainerMode);
    }

    /// <summary>The next batch of pointered documents after <paramref name="after"/>, in id order.</summary>
    internal static async Task<IReadOnlyList<Guid>> ReadBatchAsync(
        IGenericEntityService dataverse, Guid? after, int batch, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet("sprk_documentid"),
            TopCount = batch,
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions = { new ConditionExpression("sprk_graphitemid", ConditionOperator.NotNull) },
            },
        };
        if (after is { } cursor)
        {
            query.Criteria.Conditions.Add(new ConditionExpression("sprk_documentid", ConditionOperator.GreaterThan, cursor));
        }

        query.AddOrder("sprk_documentid", OrderType.Ascending);

        var results = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return results?.Entities?.Select(e => e.Id).Where(id => id != Guid.Empty).ToList()
               ?? throw new InvalidOperationException("The document query returned no result set.");
    }

    /// <summary>The run report — counted in full, listed up to <see cref="MaxListedPerRun"/>.</summary>
    internal sealed class MigrationReport
    {
        public MigrationReport(string mode, Guid? startAfter)
        {
            Mode = mode;
            StartAfter = startAfter;
        }

        public string Mode { get; }
        public Guid? StartAfter { get; }
        public Guid? EndAt { get; set; }
        public bool PassComplete { get; set; }
        public int Examined { get; private set; }

        /// <summary>The interim rule serves it, the strict rule would refuse it — the flip would break it.</summary>
        public int WouldNewlyRefuse { get; private set; }

        /// <summary>Both rules refuse it (an administrator's repair; the flip changes nothing for it).</summary>
        public int RefusedByBoth { get; private set; }

        /// <summary>
        /// The interim rule refuses it, the strict rule would serve it: the census classes the interim rule refuses by
        /// design (task 166 note §20.5). Counted and listed so no such disagreement is invisible (task 166 f1-v1, F1).
        /// </summary>
        public int ServedOnlyAfterFlip { get; private set; }

        /// <summary>
        /// A file this relocation (or an earlier one) moved, which the rule IN FORCE refuses. Owner round 37 item 3 makes
        /// a relocated file servable under the interim rule; one that is not is a defect, and the run is not clean.
        /// </summary>
        public int RelocatedButRefused { get; private set; }

        /// <summary>Documents whose move still owes a step (source delete, re-key, index) — the next pass settles them.</summary>
        public int Pending { get; private set; }

        /// <summary>Other rows moved along with a document because they named the same file.</summary>
        public int MovedAlong { get; private set; }

        /// <summary>
        /// Sources edited after their move (owner round 45 item 4: <c>source-changed-after-move</c>, with the row id) —
        /// STATED: the relocation closes each one itself (re-copy, verify, re-point); one it could not close yet is also in
        /// <see cref="Pending"/>.
        /// </summary>
        public int SourceChangedAfterMove { get; private set; }

        /// <summary>Moved files whose history the target container truncated (round 45 item 1: stated, never silent).</summary>
        public int VersionsTruncated { get; private set; }

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
        public List<object> Rows { get; } = [];
        public List<object> SourceKeptForOtherRecords { get; } = [];
        public List<object> SourceChangedAfterMoveRows { get; } = [];
        public List<object> VersionsTruncatedRows { get; } = [];
        public int RowsTotal { get; private set; }

        /// <summary>Nothing left for the migration to do in this batch, and nothing the flip would newly refuse.</summary>
        public bool IsClean
            => WouldNewlyRefuse == 0 && RelocatedButRefused == 0 && Pending == 0
               && Counts.GetValueOrDefault(nameof(RelocationState.Failed)) == 0
               && Counts.GetValueOrDefault(nameof(RelocationState.WouldRelocate)) == 0;

        public void Add(DocumentRelocationOutcome outcome, bool? interim, bool? strict, bool strictInForce = false)
        {
            Examined++;
            var key = outcome.State.ToString();
            Counts[key] = Counts.GetValueOrDefault(key) + 1;

            var newlyRefused = interim == true && strict == false;
            if (newlyRefused)
            {
                WouldNewlyRefuse++;
            }

            if (interim == false && strict == false)
            {
                RefusedByBoth++;
            }

            var servedOnlyAfterFlip = interim == false && strict == true;
            if (servedOnlyAfterFlip)
            {
                ServedOnlyAfterFlip++;
            }

            var moved = outcome.State is RelocationState.Relocated or RelocationState.RelocatedSourceKeptForOtherRecords
                or RelocationState.RelocationPending;
            var refusedInForce = (strictInForce ? strict : interim) == false;
            var relocatedButRefused = moved && refusedInForce;
            if (relocatedButRefused)
            {
                RelocatedButRefused++;
            }

            var owes = outcome.Pending.Count > 0
                       || outcome.MovedAlong.Any(m => !DocumentRelocationBatchResult.IsSettled(m));
            if (owes)
            {
                Pending++;
            }

            MovedAlong += outcome.MovedAlong.Count;
            foreach (var kept in outcome.KeptForOtherRecords.Concat(outcome.MovedAlong.SelectMany(m => m.KeptForOtherRecords)))
            {
                if (SourceKeptForOtherRecords.Count < MaxListedPerRun)
                {
                    SourceKeptForOtherRecords.Add(new
                    {
                        documentId = kept.DocumentId,
                        sourceDrive = kept.SourceDrive,
                        sourceItem = kept.SourceItem,
                        keptFor = kept.KeptFor,
                    });
                }
            }

            foreach (var changed in outcome.SourceChangedAfterMove.Concat(outcome.MovedAlong.SelectMany(m => m.SourceChangedAfterMove)))
            {
                SourceChangedAfterMove++;
                if (SourceChangedAfterMoveRows.Count < MaxListedPerRun)
                {
                    SourceChangedAfterMoveRows.Add(new
                    {
                        documentId = changed.DocumentId,
                        sourceDrive = changed.SourceDrive,
                        sourceItem = changed.SourceItem,
                        carriedVersions = changed.CarriedVersions,
                        newItem = changed.NewItem,
                        editIsCurrent = changed.EditIsCurrent,
                    });
                }
            }

            foreach (var truncated in outcome.VersionsTruncated.Concat(outcome.MovedAlong.SelectMany(m => m.VersionsTruncated)))
            {
                VersionsTruncated++;
                if (VersionsTruncatedRows.Count < MaxListedPerRun)
                {
                    VersionsTruncatedRows.Add(new
                    {
                        documentId = truncated.DocumentId,
                        item = truncated.Item,
                        replayedVersions = truncated.ReplayedVersions,
                        keptVersions = truncated.KeptVersions,
                        unrecordedAuthors = truncated.UnrecordedAuthors,
                    });
                }
            }

            if (outcome.State is RelocationState.InPlace && !newlyRefused && interim == true && !owes
                && outcome.KeptForOtherRecords.Count == 0 && outcome.SourceChangedAfterMove.Count == 0)
            {
                return; // healthy: counted, not listed
            }

            RowsTotal++;
            if (Rows.Count < MaxListedPerRun)
            {
                Rows.Add(new
                {
                    documentId = outcome.DocumentId,
                    state = key,
                    interim,
                    strict,
                    wouldNewlyRefuse = newlyRefused,
                    servedOnlyAfterFlip,
                    relocatedButRefused,
                    sourceDrive = outcome.SourceDrive,
                    sourceItem = outcome.SourceItem,
                    targetDrive = outcome.TargetDrive,
                    targetItem = outcome.TargetItem,
                    pending = outcome.Pending,
                    movedAlong = outcome.MovedAlong.Select(m => new { documentId = m.DocumentId, state = m.State.ToString(), m.TargetItem }),
                    detail = outcome.Detail,
                });
            }
        }

        public object ToJson() => new
        {
            mode = Mode,
            startAfter = StartAfter,
            endAt = EndAt,
            passComplete = PassComplete,
            examined = Examined,
            wouldNewlyRefuse = WouldNewlyRefuse,
            refusedByBoth = RefusedByBoth,
            servedOnlyAfterFlip = ServedOnlyAfterFlip,
            relocatedButRefused = RelocatedButRefused,
            pending = Pending,
            movedAlong = MovedAlong,
            sourceChangedAfterMove = SourceChangedAfterMove,
            versionsTruncated = VersionsTruncated,
            counts = Counts,
            sourceKeptForOtherRecords = SourceKeptForOtherRecords,
            sourceChangedAfterMoveRows = SourceChangedAfterMoveRows,
            versionsTruncatedRows = VersionsTruncatedRows,
            rowsTotal = RowsTotal,
            rowsListed = Rows.Count,
            rows = Rows,
        };
    }
}
