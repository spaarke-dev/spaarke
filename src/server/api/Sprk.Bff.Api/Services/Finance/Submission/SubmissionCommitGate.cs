// ebilling-r1 task 094 (procedure-and-file-model 9.1 steps 5 and 6; 092 adversarial review items 1 and 2; uac-r2 conditions 1 and 3).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceSubmissionPersister.FlipUploadFileAsync (task 092) flipped ONE file, the upload's, and on a definitive failure
//       left the invoice committed with its file Not yet servable; the resume decided whether to flip by "sprk_document is null". The
//       Submit Invoice source check (InvoiceSubmissionService.CheckSourceFilingAsync, task 094) and the isolation walk
//       (ExistingDocumentIsolationCheck, SecureChildShareSynchronizer.SecureRootsAboveAsync) answer "is the source still filed here".
//       Nothing re-runs the source check after the commit, compares the copy's roots with the source's, or undoes a commit.
//   (2) Extension — the persister is the one creator of the invoice and runs the gate right after its commit; the after-steps' resume must
//       run the SAME gate before anything else (step 6), and the job handler must finish a held undo on a later delivery. One type used by
//       all three keeps one rule; folding it into the persister would make the after-steps and the handler depend on the persister's
//       internals. The flip itself stays StoredFileService's.
//   (3) Cost of doing nothing — the 092 review's two findings: a from-email or Submit Invoice file is never flipped on resume (the gate was
//       keyed on a null document), and a definitive flip failure leaves an invoice with lines, an approval and a duplicate-number claim
//       but no servable file; and model 9.1 step 5 fails: a source re-filed under another record while the job ran keeps its copy
//       committed under the old record, reachable by that record's sharees.
//
// Placement (bff-extensions.md): Services/Finance/Submission, an in-process step of the ADR-004 submission job in the BFF (ADR-052). No
// endpoint, no package, no AI call. DI: FinanceModule (scoped, like the isolation check and the share synchronizer it composes).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What <see cref="SubmissionCommitGate"/> decided.</summary>
public enum SubmissionCommitGateStatus
{
    /// <summary>Every file of the invoice is Servable: the totals and the after-steps may run.</summary>
    Passed,

    /// <summary>The commit was undone completely: the submission is Failed with <see cref="SubmissionCommitGateResult.ReasonCode"/>.</summary>
    Undone,

    /// <summary>The submission is Failed with <see cref="SubmissionCommitGateResult.ReasonCode"/>, but a delete of the undo faulted: what is left
    /// stays Not yet servable, and a later delivery finishes the undo (<see cref="SubmissionCommitGate.CompensateAsync"/>).</summary>
    UndoHeld,
}

/// <summary>The gate's answer. <paramref name="ReasonCode"/> is <see cref="SubmissionReasonCodes.SourceCheckFailed"/> or
/// <see cref="SubmissionReasonCodes.FileUnservable"/> on an undo; null on a pass.</summary>
public sealed record SubmissionCommitGateResult(SubmissionCommitGateStatus Status, string? ReasonCode = null)
{
    /// <summary>Passed.</summary>
    public static SubmissionCommitGateResult Passed { get; } = new(SubmissionCommitGateStatus.Passed);
}

/// <summary>
/// The post-commit gate of every submission channel (procedure-and-file-model 9.1 steps 5 and 6): run right after the commit, before the
/// totals, the Signals, the Decision Record, indexing and the notification, and again first on every resume.
/// </summary>
/// <remarks>
/// <para><b>The files.</b> Found by their parent, the submission's invoice (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>), never by
/// the channel: an upload has one (no source), a Submit Invoice copy one (its source document), task 095's from-email submission one per
/// marked part (the .eml's document). None, or more than <see cref="MaxFiles"/>, is <see cref="SubmissionReasonCodes.FileUnservable"/>.</para>
/// <para><b>Step 6 (never flip twice).</b> When any file is already Servable, a previous run passed step 5 and flipped: step 5 is skipped
/// and the rest are flipped.</para>
/// <para><b>Step 5.</b> For each distinct source document, the source check runs again
/// (<see cref="InvoiceSubmissionService.CheckSourceFilingAsync"/>: present, filed under the core record only, its flags readable, its
/// isolation Allowed), and the source's secure roots must be covered by the copy's (source roots ⊆ file roots). The copy's roots are the
/// invoice's while <c>sprk_storedfile</c> is not a lineage child (<see cref="SecureChildLineage.IsChild"/>; task 086 makes it one, and the
/// file row is then walked itself). Any fault, any verdict but Allowed and any root not covered is
/// <see cref="SubmissionReasonCodes.SourceCheckFailed"/> (uac-r2 condition 1: a check that cannot be made is a failure). Then every file is
/// flipped; a definitive flip failure is <see cref="SubmissionReasonCodes.FileUnservable"/>. A flip that meets a concurrent change is tried
/// once more; a second change, or a fault of the flip or of the file read, throws (the job retries, and the resume runs this gate first).</para>
/// <para><b>The undo (uac-r2 condition 3: complete or held).</b> Failed, the reason code and an "undo pending" administrator note are
/// written FIRST, in one update (a fault throws: nothing was undone and the job retries). Then the compensation
/// (<see cref="CompensateAsync"/>): the approval (its invoice lookup is required), the lines, the files' blobs, then the file rows and the
/// invoice in ONE transaction (the invoice is never left without its file rows, which the external invoice predicate reads as its source
/// gate), and last the submission's commit columns (<c>sprk_invoice</c>, <c>sprk_findingsjson</c>, <c>sprk_firminvoicenumber</c>,
/// <c>sprk_ponumber</c>) with the note switched to "removed". The status, the reason code and the correlation id are kept. A filing of the
/// source communication made by the from-document route (step A) is not undone, and the note says so. A delete that faults leaves the undo
/// held: the submission is Failed with its reason, the note still says the removal is pending, and the handler runs the compensation again
/// on a later delivery instead of its terminal no-op; on the final delivery the handler switches the note to "undo held — operator action"
/// (<see cref="NoteUndoHeldAsync"/>).</para>
/// <para><b>Logs (ADR-015)</b> carry ids, counts and codes only.</para>
/// </remarks>
public class SubmissionCommitGate
{
    /// <summary>The most files one invoice can hold here (an upload or a copy has one; a from-email submission one per marked part).</summary>
    internal const int MaxFiles = 50;

    /// <summary>The most lines the compensation deletes per query (it queries again until none is left).</summary>
    private const int LinePage = 500;

    private const string SubmissionFirmInvoiceNumber = TrackingNumberAllocator.FirmInvoiceNumberColumn;
    private const string SubmissionPoNumber = "sprk_ponumber";
    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";

    /// <summary>The submission columns the gate and the compensation read.</summary>
    internal static readonly string[] SubmissionColumns =
    [
        "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
        InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionInvoice,
        InvoiceSubmissionPersister.SubmissionFindingsJson, InvoiceSubmissionPersister.SubmissionAdminAttention, SubmissionFirmInvoiceNumber,
        SubmissionPoNumber,
    ];

    /// <summary>The reason codes a held undo is stored with: a Failed submission with one of them may still have rows to remove.</summary>
    internal static readonly IReadOnlySet<string> UndoReasonCodes =
        new HashSet<string>(StringComparer.Ordinal) { SubmissionReasonCodes.SourceCheckFailed, SubmissionReasonCodes.FileUnservable };

    /// <summary>
    /// Review round 5 (pass-2 F1-1): a Failed attempt whose undo has not finished - its reason is one of <see cref="UndoReasonCodes"/> and it
    /// still links its invoice (<see cref="CompensateAsync"/> clears the link only once nothing of the commit is left). No new attempt is
    /// made after it until the undo completes (<see cref="InvoiceSubmissionErrors.EarlierAttemptBeingRemoved"/>).
    /// </summary>
    internal static bool UndoUnfinished(InvoiceSubmissionStatus? status, string? reasonCode, Guid? invoiceId) =>
        status == InvoiceSubmissionStatus.Failed
        && reasonCode is not null && UndoReasonCodes.Contains(reasonCode)
        && invoiceId is { } linked && linked != Guid.Empty;

    private readonly IGenericEntityService _entities;
    private readonly StoredFileService _files;
    private readonly ExistingDocumentIsolationCheck _isolation;
    private readonly SecureChildShareSynchronizer _shares;
    private readonly ILogger<SubmissionCommitGate> _logger;

    public SubmissionCommitGate(
        IGenericEntityService entities,
        StoredFileService files,
        ExistingDocumentIsolationCheck isolation,
        SecureChildShareSynchronizer shares,
        ILogger<SubmissionCommitGate> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _isolation = isolation ?? throw new ArgumentNullException(nameof(isolation));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Steps 5 and 6 for <paramref name="submissionId"/>, whose invoice is committed (see the class remarks). Throws for a fault the job
    /// retries (the file read, a flip fault, a second concurrent change, the undo's first write); every other outcome is the result.
    /// </summary>
    public virtual async Task<SubmissionCommitGateResult> RunAsync(Guid submissionId, CancellationToken ct)
    {
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
        var files = await _files.ReadRowsForInvoiceAsync(invoiceId, MaxFiles + 1, ct).ConfigureAwait(false);
        if (files.Count is 0 or > MaxFiles)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: invoice {InvoiceId} has {Count} files, none or too many; undoing the commit",
                submissionId, invoiceId, files.Count);
            return await UndoAsync(submissionId, SubmissionReasonCodes.FileUnservable, ct).ConfigureAwait(false);
        }

        // ── Step 6: a file already Servable means step 5 passed in an earlier run; never run it (or flip) twice ──
        if (!files.Any(f => f.Status == StoredFileStatus.Servable))
        {
            // ── Step 5: the source re-run, every source, before any flip ──
            var sources = files.Select(f => f.SourceDocumentId).OfType<Guid>().Where(id => id != Guid.Empty).Distinct().ToList();
            if (sources.Count > 0)
            {
                bool covered;
                try
                {
                    covered = await SourcesStillCoveredAsync(submissionId, invoiceId, files, sources, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // uac-r2 condition 1: a re-run that cannot be made is a failure, never a pass and never a retry after the commit.
                    _logger.LogWarning("Invoice submission {SubmissionId}: the source re-run faulted ({Fault}); undoing the commit",
                        submissionId, ex.GetType().Name);
                    covered = false;
                }

                if (!covered)
                {
                    return await UndoAsync(submissionId, SubmissionReasonCodes.SourceCheckFailed, ct).ConfigureAwait(false);
                }
            }
        }

        foreach (var file in files.Where(f => f.Status != StoredFileStatus.Servable))
        {
            var flip = await _files.FlipToServableAsync(file.FileId, ct).ConfigureAwait(false);
            if (flip.Outcome == StoredFileFlipOutcome.Concurrency)
            {
                flip = await _files.FlipToServableAsync(file.FileId, ct).ConfigureAwait(false);
            }

            switch (flip.Outcome)
            {
                case StoredFileFlipOutcome.Flipped or StoredFileFlipOutcome.AlreadyServable:
                    continue;
                case StoredFileFlipOutcome.Definitive:
                    _logger.LogWarning("Invoice submission {SubmissionId}: its Stored File {FileId} cannot be made servable ({Code}); undoing the commit",
                        submissionId, file.FileId, flip.RefusalCode);
                    return await UndoAsync(submissionId, SubmissionReasonCodes.FileUnservable, ct).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException(
                        $"The Stored File {file.FileId} of submission {submissionId} changed twice during its flip ({flip.Outcome}); retry.");
            }
        }

        return SubmissionCommitGateResult.Passed;
    }

    /// <summary>
    /// Step 5's re-run: true when every source in <paramref name="sources"/> is still filed under the submission's core record only and its
    /// secure roots are covered by the copy's. A fault propagates (the caller treats it as a failure). Virtual: the seeding proof's seam.
    /// </summary>
    internal virtual async Task<bool> SourcesStillCoveredAsync(
        Guid submissionId, Guid invoiceId, IReadOnlyList<StoredFileRow> files, IReadOnlyList<Guid> sources, CancellationToken ct)
    {
        var submission = await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        if (InvoiceSubmissionService.ToView(submission).CoreRecord is not { } core)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: it regards no single core record; the source check fails", submissionId);
            return false;
        }

        var fileRoots = await FileRootsAsync(invoiceId, files, ct).ConfigureAwait(false);
        if (fileRoots is null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the secure roots of its file could not all be determined; the source check fails", submissionId);
            return false;
        }

        foreach (var source in sources)
        {
            var check = await InvoiceSubmissionService.CheckSourceFilingAsync(_entities, _isolation, source, core, ct).ConfigureAwait(false);
            if (check.Verdict != SourceFilingVerdict.Allowed)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: its source {DocumentId} failed the re-run ({Verdict}: {Detail})",
                    submissionId, source, check.Verdict, check.Detail);
                return false;
            }

            var uncovered = (check.Roots ?? []).Where(r => !fileRoots.Any(f =>
                string.Equals(f.Table, r.Table, StringComparison.OrdinalIgnoreCase) && f.Id == r.Id)).ToList();
            if (uncovered.Count > 0)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: its source {DocumentId} is under secure root {Table} {Id}, which its copy is not under",
                    submissionId, source, uncovered[0].Table, uncovered[0].Id);
                return false;
            }

            _logger.LogInformation(
                "Invoice submission {SubmissionId}: source {DocumentId} re-checked (restricted {Restricted}, internal-only {InternalOnly}, privacy {Privacy}; not stored)",
                submissionId, source, check.Flags?.Restricted, check.Flags?.InternalOnly, check.Flags?.Privacy);
        }

        return true;
    }

    /// <summary>
    /// The copy's secure roots: the file rows' own once <c>sprk_storedfile</c> is a lineage child (task 086), until then the invoice's (the
    /// file's parent, whose roots it inherits). Empty when the environment isolates nothing; null when any is undetermined or the walk
    /// failed. A fault propagates.
    /// </summary>
    private async Task<IReadOnlyList<(string Table, Guid Id)>?> FileRootsAsync(Guid invoiceId, IReadOnlyList<StoredFileRow> files, CancellationToken ct)
    {
        IReadOnlyCollection<Entity> rows;
        if (SecureChildLineage.IsChild(StoredFileSchema.Entity))
        {
            rows = await ReadLineageRowsAsync(StoredFileSchema.Entity, files.Select(f => f.FileId).ToList(), ct).ConfigureAwait(false);
        }
        else
        {
            rows = await ReadLineageRowsAsync(InvoiceReviewService.InvoiceEntity, [invoiceId], ct).ConfigureAwait(false);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var above = await _shares.SecureRootsAboveAsync(rows, ct).ConfigureAwait(false);
        return above.Status switch
        {
            SecureChildShareSyncStatus.NotApplicable => [],
            SecureChildShareSyncStatus.Completed when above.Undetermined.Count == 0 => above.Roots,
            _ => null,
        };
    }

    private async Task<IReadOnlyCollection<Entity>> ReadLineageRowsAsync(string table, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var lineage = SecureChildLineage.Children[table];
        string[] columns = [.. lineage.Lookups.Keys, OwningTeamColumn, OwningUserColumn];
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), NoLock = true };
        query.Criteria.AddCondition(lineage.IdColumn, ConditionOperator.In, ids.Cast<object>().ToArray());
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
    }

    /// <summary>
    /// The undo: Failed, <paramref name="reasonCode"/> and the "undo pending" administrator note first (a fault throws: nothing was undone),
    /// then <see cref="CompensateAsync"/>, which replaces the note with "removed" only once nothing of the commit is left (fix round F2-a).
    /// </summary>
    internal async Task<SubmissionCommitGateResult> UndoAsync(Guid submissionId, string reasonCode, CancellationToken ct)
    {
        var current = await AdminAttentionText.ReadCurrentAsync(_entities, submissionId, ct).ConfigureAwait(false);
        var note = AdminAttentionText.Merge(current, undoneNote: AdminAttentionText.UndoPendingNote(reasonCode));
        var failed = new Dictionary<string, object>
        {
            [InvoiceSubmissionPersister.SubmissionStatus] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode,
        };
        if (note is not null && !string.Equals(note, current, StringComparison.Ordinal))
        {
            failed[InvoiceSubmissionPersister.SubmissionAdminAttention] = note;
        }

        await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, failed, ct).ConfigureAwait(false);
        _logger.LogWarning("Invoice submission {SubmissionId}: Failed ({ReasonCode}); undoing its commit", submissionId, reasonCode);

        return await CompensateAsync(submissionId, ct).ConfigureAwait(false)
            ? new SubmissionCommitGateResult(SubmissionCommitGateStatus.Undone, reasonCode)
            : new SubmissionCommitGateResult(SubmissionCommitGateStatus.UndoHeld, reasonCode);
    }

    /// <summary>
    /// The compensation of an undone commit, idempotent (fix round: undo ordering and F2-a). In order: the invoice's file rows read and
    /// withdrawn (Removed; uac-r2 (b) and F4-1: before any delete, so a held undo never leaves a file served), the approval (its invoice
    /// lookup is required), the lines, the file rows' blobs, then ONE transaction that deletes the file rows AND the invoice together, and
    /// last the submission's commit columns with the "removed" administrator note. The invoice is never left without its file rows: the
    /// rows are the source gate the external invoice predicate (task 098) reads, and <c>sprk_storedfile.sprk_invoice</c> is RemoveLink, so
    /// an invoice deleted first would orphan them, and rows deleted first would leave an invoice that reads as having no source. The columns
    /// are cleared only when something of the commit was left (the link or the findings set, or a row found), so a submission Failed BEFORE
    /// any commit with the same code keeps its number. More than <see cref="MaxFiles"/> file rows hold the undo with nothing deleted (uac-r2 K2);
    /// the rows read are withdrawn first.
    /// True when nothing is left; false when a step faulted (the undo is held; logged with
    /// the fault type only, and the note still says the removal is pending). Only the caller's cancellation propagates.
    /// </summary>
    public virtual async Task<bool> CompensateAsync(Guid submissionId, CancellationToken ct)
    {
        var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
        var step = "read";
        try
        {
            var submission = await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
            var left = submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice) is { Id: var linked } && linked != Guid.Empty
                || !string.IsNullOrEmpty(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson));

            // uac-r2 K2: the file rows are read FIRST. More than one transaction takes (only a data fault gets here) holds the undo
            // before anything is deleted: the read is unordered, so removing a "surplus" could keep a row with no source and drop
            // the rows that carry one.
            step = "files_read";
            var files = await _files.ReadRowsForInvoiceAsync(invoiceId, MaxFiles + 1, ct).ConfigureAwait(false);

            // uac-r2 F4-1: every row read is withdrawn (Removed, never served) straight after the read, BEFORE any delete, so an undo
            // held at any later step (approval, lines, blobs, the transaction) never leaves a Servable file on a Failed submission.
            // The rows read before an over-MaxFiles hold are withdrawn too; nothing is deleted then. Each write is conditioned on the
            // row version read above.
            if (files.Count > 0)
            {
                step = "withdraw";
                await _files.WithdrawForUndoAsync(files, ct).ConfigureAwait(false);
            }

            if (files.Count > MaxFiles)
            {
                step = "files_read";
                throw new InvalidOperationException($"Invoice {invoiceId} has more than {MaxFiles} file rows; the undo is held.");
            }

            step = "approval";
            left |= await DeleteIfPresentAsync(InvoiceSubmissionPersister.ApprovalEntity, InvoiceSubmissionPersister.ApprovalIdFor(submissionId), ct).ConfigureAwait(false);

            step = "lines";
            while (true)
            {
                var query = new QueryExpression(InvoiceSubmissionPersister.LineEntity) { ColumnSet = new ColumnSet(false), TopCount = LinePage, NoLock = true };
                query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
                var lines = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
                if (lines.Count == 0)
                {
                    break;
                }

                left = true;
                foreach (var line in lines)
                {
                    await DeleteIfPresentAsync(InvoiceSubmissionPersister.LineEntity, line.Id, ct).ConfigureAwait(false);
                }
            }

            step = "files";
            if (files.Count > 0)
            {
                left = true;

                // uac-r2 (b): every row was withdrawn above, before any blob goes, so an undo held after a blob delete never leaves a
                // Servable row whose blob is gone.
                step = "blobs";
                foreach (var file in files)
                {
                    await _files.DeleteBlobForUndoAsync(file, ct).ConfigureAwait(false);
                }

                // The rows reference the invoice, so it exists: the rows and the invoice go in one transaction (all or nothing).
                step = "files_and_invoice";
                var deletes = files.Select(f => (OrganizationRequest)new DeleteRequest { Target = new EntityReference(StoredFileSchema.Entity, f.FileId) })
                    .Append(new DeleteRequest { Target = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId) })
                    .ToList();
                await _entities.ExecuteTransactionAsync(deletes, ct).ConfigureAwait(false);
            }
            else
            {
                step = "invoice";
                left |= await DeleteIfPresentAsync(InvoiceReviewService.InvoiceEntity, invoiceId, ct).ConfigureAwait(false);
            }

            var current = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
            if (left || AdminAttentionText.HasUnfinishedUndo(current))
            {
                step = "columns";
                var fields = new Dictionary<string, object>();
                if (left)
                {
                    fields[InvoiceSubmissionPersister.SubmissionInvoice] = DBNull.Value;
                    fields[InvoiceSubmissionPersister.SubmissionFindingsJson] = DBNull.Value;
                    fields[SubmissionFirmInvoiceNumber] = DBNull.Value;
                    fields[SubmissionPoNumber] = DBNull.Value;
                }

                // F2-a: "removed" only now, when every delete above returned.
                var code = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);
                var note = AdminAttentionText.Merge(current, undoneNote: AdminAttentionText.UndoneNote(code));
                if (note is not null && !string.Equals(note, current, StringComparison.Ordinal))
                {
                    fields[InvoiceSubmissionPersister.SubmissionAdminAttention] = note;
                }

                if (fields.Count > 0)
                {
                    await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, fields, ct).ConfigureAwait(false);
                }

                _logger.LogInformation("Invoice submission {SubmissionId}: its commit was undone (invoice {InvoiceId}, its lines, approval and files removed)",
                    submissionId, invoiceId);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Invoice submission {SubmissionId}: the undo of its commit is held at step {Step} ({Fault}); a later delivery finishes it",
                submissionId, step, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// Fix round (F2-a): the undone slot of the administrator note becomes "undo held — operator action" (<see cref="AdminAttentionText.UndoHeldNote"/>).
    /// The job handler calls it when the undo is still held on the job's final delivery, so nothing retries it on its own. Not a status
    /// write; a fault is logged (type only) and swallowed, so the handler's outcome never changes. True when it wrote.
    /// </summary>
    public virtual async Task<bool> NoteUndoHeldAsync(Guid submissionId, string? reasonCode, CancellationToken ct)
    {
        try
        {
            var current = await AdminAttentionText.ReadCurrentAsync(_entities, submissionId, ct).ConfigureAwait(false);
            var note = AdminAttentionText.Merge(current, undoneNote: AdminAttentionText.UndoHeldNote(reasonCode));
            if (note is null || string.Equals(note, current, StringComparison.Ordinal))
            {
                return false;
            }

            await _entities.UpdateAsync(FinanceBaseValues.SubmissionEntity, submissionId, new Dictionary<string, object>
            {
                [InvoiceSubmissionPersister.SubmissionAdminAttention] = note,
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: the held-undo note could not be written ({Fault}); the outcome is unchanged",
                submissionId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Deletes a row by id. True when it was there; false when it was already gone. Any other fault propagates.</summary>
    private async Task<bool> DeleteIfPresentAsync(string table, Guid id, CancellationToken ct)
    {
        try
        {
            await _entities.DeleteAsync(table, id, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }
}
