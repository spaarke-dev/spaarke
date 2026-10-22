// unified-access-control-r2 task 173 (owner rounds 81 and 84; GitHub #1423).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureChildReconciliationJob's recent-changes pass lists changed child rows and walks DOWN from secure
//       records (SecureChildReconciler.Pass.LoadDescendantsAsync), but it reconciles OWNERSHIP of rows under secure records
//       only, and filters out every row the Secure team already owns. CoreAncestorStampReconciliationJob repairs stamp
//       COPIES. Nothing keeps a child's displayed Access Permission equal to its parents'.
//   (2) Extension — It runs INSIDE SecureChildReconciliationJob (ADR-036/052: no new job, no new timer; constraint of the
//       task). It is a class of its own rather than more code in the job or the reconciler because its listing, watermark
//       and failure must stay apart from the secure pass: a fault here must never stall secure isolation (#1378, "do not
//       widen it"). The rule itself is ParentLineageWalk / InheritedAccessPermission — the same code the stamp path calls.
//   (3) Cost of doing nothing — a child re-filed outside the BFF, a child whose matter turns Restricted, a hand edit of a
//       parented child, and every child created before this task keep showing a value their parent does not hold.
//
// Placement (bff-extensions.md; ADR-052): BFF, on the existing 2-minute job. No package, endpoint, column, interface or DI
// registration (the job constructs it per run from its scope).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>What one access-permission pass did (task 173).</summary>
internal sealed record ChildAccessPermissionRun
{
    /// <summary>A listing or descent that could not complete: nothing was decided from it, and the window is looked at again.</summary>
    public string? Failure { get; init; }

    /// <summary>Rows of the listed tables modified since the window start (the seeds of the downward walk).</summary>
    public int RowsChanged { get; init; }

    /// <summary>To Do / Event / Communication / Document rows decided this pass.</summary>
    public int Examined { get; init; }

    public int AlreadyCorrect { get; init; }
    public int Changed { get; init; }
    public int WouldChange { get; init; }

    /// <summary>Rows with no parent: their own value stands, never written (round 81).</summary>
    public int Parentless { get; init; }

    /// <summary>Rows whose parents could not be decided (unreadable, missing, too deep, a loop): not written, carried.</summary>
    public int Undetermined { get; init; }

    /// <summary>Writes that failed: carried, and the run is not a success.</summary>
    public int Failed { get; init; }

    /// <summary>Rows whose value differs but were not written because the run reached its write cap (verifier F3).</summary>
    public int Deferred { get; init; }

    /// <summary>The run stopped writing at its cap: the caller keeps the window, so the next run continues.</summary>
    public bool WriteCapReached { get; init; }

    /// <summary>Up to 50 "table id: reason" lines for undetermined and failed rows.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>Up to 200 changes (made or planned): table, id, from, to.</summary>
    public IReadOnlyList<(string Table, Guid Id, int? From, int To)> Changes { get; init; } = [];

    /// <summary>Rows to look at first next run (undetermined or failed).</summary>
    public IReadOnlyList<(string Table, Guid Id)> Carry { get; init; } = [];

    /// <summary>The sweep window: rows it read, and the last row (null once it reached the end).</summary>
    public int SweepRows { get; init; }

    public (string Table, Guid Id)? SweepResumeAfter { get; init; }

    public bool SweepReachedEnd { get; init; }

    /// <summary>
    /// Tables of the four that lack the column in this environment (nothing is written to them), or (a document, task 189)
    /// lack the own-narrowing record column <see cref="AccessInheritance.Column"/>: their values are only raised to what
    /// their parents give, never lowered, and no record is written (fix round C, K2; <see cref="RaisedOnly"/>).
    /// </summary>
    public IReadOnlyList<string> TablesWithoutColumn { get; init; } = [];

    /// <summary>Task 189: writes that set a document's own-narrowing record (with its value, or alone). Report-only: 0.</summary>
    public int RecordsWritten { get; init; }

    /// <summary>Task 189: documents whose own narrowing holds their value above what their parents give (not widened).</summary>
    public int OwnKept { get; init; }

    /// <summary>
    /// Task 189: documents that changed between this run's read and its write (re-read just before the write). Nothing was
    /// written; they are carried and decided again.
    /// </summary>
    public int ChangedConcurrently { get; init; }

    /// <summary>
    /// Task 189 fix round C (K1+K2): documents whose own narrowing could not be known (an unreadable record, or no record
    /// column here) and whose value was below what their parents give: their value was raised to it, alone, and their
    /// record left as it is. Also counted in <see cref="Changed"/>. Report-only: 0 (they are in <see cref="WouldChange"/>).
    /// </summary>
    public int RaisedOnly { get; init; }
}

/// <summary>
/// unified-access-control-r2 task 173 — keeps <c>sprk_accesspermission</c> on To Do, Event, Communication and Document
/// equal to the most restrictive value of the records at the top of their filing (<see cref="InheritedAccessPermission"/>),
/// within one run of <see cref="SecureChildReconciliationJob"/>. A parentless row is never written (its own value stands).
/// </summary>
/// <remarks>
/// <para><b>What a run looks at.</b> (1) Every row modified since the window start in the tables that can sit on a filing
/// above one of the four (the roots, the four, and the intermediates between: analysis, invoice, agreement, budget, report
/// card, communication thread) — a re-file, a hand edit, a root whose value changed. (2) Their descendants, walked DOWN
/// level by level through the filing lookups (rows of any owner, at most <see cref="ParentLineageWalk.MaxDepth"/> levels):
/// a matter turned Restricted reaches every To Do, Event, Communication and Document filed under it, at any depth. (3) The
/// rows an earlier run could not decide. (4) A capped sweep window over every row of the four tables, while the job's
/// per-instance catch-up has not covered them (the backfill of rows created before this task, and drift a restart's
/// lost window would miss).</para>
/// <para><b>Each row</b> is decided by <see cref="InheritedAccessPermission.ResolveAsync"/> over ONE
/// <see cref="ParentLineageWalk"/> per run (its parents' reads shared), and written only when its stored value differs —
/// one column, nothing else (a document: its value and its record, below). A write changes <c>modifiedon</c>, so the next run
/// sees the row again and finds it correct.</para>
/// <para><b>Fail safe.</b> A listing that cannot complete decides nothing (<see cref="ChildAccessPermissionRun.Failure"/>).
/// An undecidable row is left as it is, logged as a warning and carried. On To Do, Event and Communication the value is
/// display only: no access decision reads it.</para>
/// <para><b>Documents (task 189, D7 T7a, #2126).</b> A document's value is NOT display only. Under D7, task 188's external
/// document narrowing (PR #2170) reads it: a Restricted value this reconciler writes on a document hides that document
/// from partners, and lowering one shows it to them again. So a wrong write here can narrow or widen partner access, and
/// documents fail closed (below). A document's value is max(own, inherited), decided by
/// <see cref="InheritedAccessPermission.DecideDocument"/> from its record (<see cref="AccessInheritance.Column"/>, read with
/// the row). Value and record are written in ONE update, and only when they are still what the decision read: they are
/// re-read just before the write, so a user's concurrent edit is never overwritten. It fails closed: an unknown stored
/// level writes nothing. When a document's own narrowing cannot be known (an unreadable record, or an environment whose
/// documents lack the record column) its value is only RAISED to what its parents give, never lowered, and its record is
/// left as it is (fix round C, K1+K2; <see cref="InheritedAccessPermission.DecideDocumentRaiseOnly"/>). To Do, Event and
/// Communication are reconciled exactly as before.</para>
/// </remarks>
internal sealed class ChildAccessPermissionReconciler
{
    private const int PageSize = SecureChildShareSynchronizer.PageSize;
    private const int MaxPages = SecureChildShareSynchronizer.MaxPages;
    private const int IdsPerQuery = SecureChildShareSynchronizer.DescendantConditionsPerQuery;
    private const string ModifiedOnColumn = "modifiedon";
    private const int MaxListed = 200;
    private const int MaxProblems = 50;

    /// <summary>
    /// The tables a filing above one of the four can pass through: the four, every table one of them can be filed under,
    /// and so on up (the closure over <see cref="ParentLineage.ChildFiling"/>). Their changed rows seed the downward walk;
    /// a table outside it can never be above one of the four, so its rows are not listed.
    /// </summary>
    internal static readonly IReadOnlySet<string> AboveTheFour = Closure();

    private readonly IGenericEntityService _dataverse;
    private readonly CoreAncestorResolver.EntityColumnProbe _probe;
    private readonly ParentLineageWalk _walk;
    private readonly ILogger _logger;

    /// <summary>Task 189: the tables of <see cref="InheritedAccessPermission.RecordTables"/> whose record column exists here.</summary>
    private readonly HashSet<string> _recordTables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Task 189 fix round C (K2): the tables of <see cref="InheritedAccessPermission.RecordTables"/> whose record column does
    /// NOT exist here. Their rows are decided raise-only (<see cref="InheritedAccessPermission.DecideDocumentRaiseOnly"/>),
    /// and the record column is never asked for.
    /// </summary>
    private readonly HashSet<string> _raiseOnlyTables = new(StringComparer.OrdinalIgnoreCase);

    internal ChildAccessPermissionReconciler(
        IGenericEntityService dataverse, CoreAncestorResolver.EntityColumnProbe probe, ILogger logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _walk = new ParentLineageWalk(dataverse, probe, [InheritedAccessPermission.Column]);
    }

    /// <param name="since">The window start (rows modified at or after it are listed).</param>
    /// <param name="carried">Rows an earlier run could not decide.</param>
    /// <param name="sweepAfter">Null: no sweep window this run. Otherwise the window starts after this row
    /// (<c>(null, Guid.Empty)</c>-like start: pass <see cref="SweepFromStart"/>).</param>
    /// <param name="sweepCap">Rows in the sweep window.</param>
    /// <param name="writes">False: decide and report only.</param>
    /// <param name="maxWrites">The most rows this run writes (verifier F3). Past it a differing row is counted
    /// <see cref="ChildAccessPermissionRun.Deferred"/> and left for the next run.</param>
    internal async Task<ChildAccessPermissionRun> RunAsync(
        DateTimeOffset since,
        IReadOnlyCollection<(string Table, Guid Id)> carried,
        (string Table, Guid Id)? sweepAfter,
        int sweepCap,
        bool writes,
        int maxWrites,
        CancellationToken ct)
    {
        var candidates = new Dictionary<(string Table, Guid Id), Entity>();
        var missingColumn = new List<string>();
        int rowsChanged;
        var sweepRows = 0;
        (string Table, Guid Id)? sweepResume = null;
        var sweepEnd = false;
        _recordTables.Clear();
        _raiseOnlyTables.Clear();
        try
        {
            foreach (var table in InheritedAccessPermission.Tables.OrderBy(t => t, StringComparer.Ordinal))
            {
                if (!(await _walk.ColumnsForAsync(table, ct).ConfigureAwait(false)).Contains(InheritedAccessPermission.Column))
                {
                    missingColumn.Add(table);
                }
                else if (InheritedAccessPermission.RecordsOwnNarrowing(table))
                {
                    // Task 189: without its record column, a document's own narrowing cannot be read, so its value is only
                    // raised to what its parents give, never lowered, and no record is written (fix round C, K2), until
                    // scripts/Set-DocumentAccessPermissionSchema.ps1 -Apply adds it.
                    if ((await _probe(table, ct).ConfigureAwait(false)).Contains(AccessInheritance.Column))
                    {
                        _recordTables.Add(table);
                    }
                    else
                    {
                        missingColumn.Add(table);
                        _raiseOnlyTables.Add(table);
                        _logger.LogWarning(
                            "[CHILD-ACCESS-PERMISSION] {Table} has no {Column} (the own-narrowing record) in this environment; " +
                            "its values are only raised to what their parents give, never lowered, and no record is written.",
                            table, AccessInheritance.Column);
                    }
                }
            }

            // (1) The rows changed since the window start.
            var seeds = new List<Entity>();
            foreach (var table in AboveTheFour.OrderBy(t => t, StringComparer.Ordinal))
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = await ColumnSetAsync(table, ct).ConfigureAwait(false),
                    NoLock = true,
                };
                query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
                seeds.AddRange(await ReadAllAsync(query, ct).ConfigureAwait(false));
            }

            rowsChanged = seeds.Count;
            foreach (var row in seeds)
                Take(row, candidates);

            // (2) Everything filed under them, at any depth.
            await DescendAsync(seeds, candidates, ct).ConfigureAwait(false);

            // (3) What earlier runs could not decide, read again by id.
            foreach (var group in carried.Where(r => !candidates.ContainsKey(Key(r.Table, r.Id)))
                         .GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase))
            {
                if (!InheritedAccessPermission.AppliesTo(group.Key))
                    continue;
                foreach (var chunk in group.Select(r => r.Id).Distinct().Chunk(IdsPerQuery))
                {
                    var query = new QueryExpression(group.Key.ToLowerInvariant())
                    {
                        ColumnSet = await ColumnSetAsync(group.Key, ct).ConfigureAwait(false),
                        NoLock = true,
                    };
                    query.Criteria.AddCondition(group.Key.ToLowerInvariant() + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());
                    foreach (var row in (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
                        Take(row, candidates);
                }
            }

            // (4) The sweep window.
            if (sweepAfter is { } after && sweepCap > 0)
            {
                var window = await SweepWindowAsync(after, sweepCap, ct).ConfigureAwait(false);
                sweepRows = window.Rows.Count;
                foreach (var row in window.Rows)
                    Take(row, candidates);
                sweepEnd = window.ReachedEnd;
                sweepResume = window.ReachedEnd || window.Rows.Count == 0
                    ? null
                    : (window.Rows[^1].LogicalName, window.Rows[^1].Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[CHILD-ACCESS-PERMISSION] The changed records since {Since:o} (or the records filed under them) " +
                "could not be listed; nothing was decided, and the window is looked at again next run.", since);
            return new ChildAccessPermissionRun
            {
                Failure = "the changed records (or the records filed under them) could not be listed",
                Carry = carried.ToList(),
                TablesWithoutColumn = missingColumn,
            };
        }

        // Decide each row of the four tables.
        int examined = 0, correct = 0, changed = 0, would = 0, parentless = 0, undetermined = 0, failed = 0, deferred = 0;
        int recordsWritten = 0, ownKept = 0, changedConcurrently = 0, raisedOnly = 0;
        var attempted = 0;
        var problems = new List<string>();
        var changes = new List<(string, Guid, int?, int)>();
        var carry = new List<(string, Guid)>();
        foreach (var ((table, id), row) in candidates.OrderBy(c => c.Key.Table, StringComparer.Ordinal).ThenBy(c => c.Key.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (missingColumn.Contains(table, StringComparer.OrdinalIgnoreCase) && !_raiseOnlyTables.Contains(table))
                continue;
            examined++;

            if (_recordTables.Contains(table) || _raiseOnlyTables.Contains(table))
            {
                await DecideDocumentAsync(table, id, row).ConfigureAwait(false); // task 189
                continue;
            }

            var parents = await _walk.ParentsOfAsync(row, ct).ConfigureAwait(false);
            if (parents.Count == 0)
            {
                parentless++; // round 81: its own value is the user's — never written, even after an un-file
                continue;
            }

            var answer = await InheritedAccessPermission.ResolveAsync(_walk, parents, ct, (table, id)).ConfigureAwait(false);
            if (answer.Status != ParentTopsStatus.Found || answer.Value is not { } inherited)
            {
                undetermined++;
                carry.Add((table, id));
                if (problems.Count < MaxProblems)
                    problems.Add($"{table} {id:D}: {answer.Reason}");
                _logger.LogWarning(
                    "[CHILD-ACCESS-PERMISSION] {Table} {Id}: its inherited Access Permission cannot be decided ({Reason}); " +
                    "its stored value is left as it is.", table, id, answer.Reason);
                continue;
            }

            var stored = row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value;
            if (stored == inherited)
            {
                correct++;
                continue;
            }

            if (writes && attempted >= maxWrites)
            {
                deferred++; // F3: the window is kept, so the next run lists this row again and writes it
                continue;
            }

            if (changes.Count < MaxListed)
                changes.Add((table, id, stored, inherited));

            if (!writes)
            {
                would++;
                _logger.LogInformation(
                    "[CHILD-ACCESS-PERMISSION] plan: {Table} {Id} {From} -> {To} (inherited from {Tops}).",
                    table, id, stored?.ToString() ?? "null", inherited, InheritedAccessPermission.Describe(answer.Tops));
                continue;
            }

            attempted++;
            try
            {
                await _dataverse.UpdateAsync(table, id,
                    new Dictionary<string, object> { [InheritedAccessPermission.Column] = new OptionSetValue(inherited) }, ct)
                    .ConfigureAwait(false);
                changed++;
                _logger.LogInformation(
                    "[CHILD-ACCESS-PERMISSION] set: {Table} {Id} {From} -> {To} (inherited from {Tops}).",
                    table, id, stored?.ToString() ?? "null", inherited, InheritedAccessPermission.Describe(answer.Tops));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                carry.Add((table, id));
                if (problems.Count < MaxProblems)
                    problems.Add($"{table} {id:D}: the write failed ({ex.GetType().Name})");
                _logger.LogError(ex, "[CHILD-ACCESS-PERMISSION] {Table} {Id}: writing its inherited Access Permission {To} failed; " +
                    "it is tried again next run.", table, id, inherited);
            }
        }

        // Task 189 (D7 T7a, #2126): one document. Its value is max(own, inherited), never lowered below its own narrowing.
        // Value and record are written in ONE update, only when both are still as read. When its own narrowing cannot be
        // known (an unreadable record, or no record column here) its value is only raised to the floor (fix round C).
        async Task DecideDocumentAsync(string table, Guid id, Entity row)
        {
            var raiseOnly = _raiseOnlyTables.Contains(table);
            var stored = row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value;
            var recordText = raiseOnly ? null : row.GetAttributeValue<string>(AccessInheritance.Column);
            var parents = await _walk.ParentsOfAsync(row, ct).ConfigureAwait(false);

            InheritedAccessPermission.Answer? answer = null;
            if (parents.Count > 0)
            {
                answer = await InheritedAccessPermission.ResolveAsync(_walk, parents, ct, (table, id)).ConfigureAwait(false);
                if (answer.Status != ParentTopsStatus.Found || answer.Value is null)
                {
                    undetermined++;
                    carry.Add((table, id));
                    if (problems.Count < MaxProblems)
                        problems.Add($"{table} {id:D}: {answer.Reason}");
                    _logger.LogWarning(
                        "[CHILD-ACCESS-PERMISSION] {Table} {Id}: its inherited Access Permission cannot be decided ({Reason}); " +
                        "its stored value is left as it is.", table, id, answer.Reason);
                    return;
                }
            }

            // Owner round 122: a provisional own narrowing (the save-time Restricted of an email that could not be identified)
            // is released once the communication walk finds a link. The walk is made only for such a row; the rule is
            // AccessInheritance.OwnPermissionOf's.
            var communicationLinked = !raiseOnly && parents.Count > 0
                && AccessInheritance.Parse(recordText).Value is { OwnProvisional: true }
                && await CommunicationLinkedAsync(table, id, row, ct).ConfigureAwait(false);
            var decision = raiseOnly
                ? InheritedAccessPermission.DecideDocumentRaiseOnly(stored, parents, answer?.Value, undecided: null)
                : InheritedAccessPermission.DecideDocument(stored, recordText, parents, answer?.Value, communicationLinked);
            if (decision.Undetermined is { } why)
            {
                undetermined++;
                carry.Add((table, id));
                if (problems.Count < MaxProblems)
                    problems.Add($"{table} {id:D}: {why}");
                if (!decision.WriteValue)
                {
                    _logger.LogWarning(
                        "[CHILD-ACCESS-PERMISSION] {Table} {Id}: {Reason}; nothing is written to it (fail closed).", table, id, why);
                    return;
                }

                _logger.LogWarning(
                    "[CHILD-ACCESS-PERMISSION] {Table} {Id}: {Reason}; its record is left as it is, and its value is only " +
                    "raised to what its parents give.", table, id, why);
            }

            if (parents.Count == 0)
                parentless++; // round 81: its value is the user's, never written; a narrowing is RECORDED as its own
            if (decision.OwnKept)
                ownKept++;
            if (!decision.WriteValue && decision.Record is null)
            {
                if (parents.Count > 0)
                    correct++;
                return;
            }

            if (writes && attempted >= maxWrites)
            {
                deferred++; // F3: the window is kept, so the next run lists this row again and writes it
                return;
            }

            if (decision.WriteValue && changes.Count < MaxListed)
                changes.Add((table, id, stored, decision.Target));

            var from = stored?.ToString() ?? "null";
            var inheritedText = answer?.Value?.ToString() ?? "none";
            var tops = answer is null ? "no parent" : InheritedAccessPermission.Describe(answer.Tops);
            var own = decision.RaiseOnly ? "unknown" : decision.Own?.ToString() ?? "none";
            var recordAction = decision.Record is null ? "kept" : "written";
            if (!writes)
            {
                if (decision.WriteValue)
                    would++; // a record-only plan is logged, not counted: report-only writes nothing
                _logger.LogInformation(
                    "[CHILD-ACCESS-PERMISSION] plan: {Table} {Id} {From} -> {To} (inherited {Inherited} from {Tops}; own {Own}; " +
                    "record {Record}).", table, id, from, decision.Target, inheritedText, tops, own, recordAction);
                return;
            }

            attempted++;
            try
            {
                if (!await StillAsReadAsync(table, id, stored, recordText, withRecord: !raiseOnly, ct).ConfigureAwait(false))
                {
                    changedConcurrently++;
                    carry.Add((table, id));
                    _logger.LogInformation(
                        "[CHILD-ACCESS-PERMISSION] {Table} {Id}: it changed while it was being decided; nothing was written, " +
                        "and it is decided again next run.", table, id);
                    return;
                }

                var fields = new Dictionary<string, object>();
                if (decision.WriteValue)
                    fields[InheritedAccessPermission.Column] = new OptionSetValue(decision.Target);
                if (decision.Record is { } record)
                    fields[AccessInheritance.Column] = record.Serialize();
                await _dataverse.UpdateAsync(table, id, fields, ct).ConfigureAwait(false);
                if (decision.WriteValue)
                    changed++;
                if (decision.Record is not null)
                    recordsWritten++;
                if (decision.RaiseOnly)
                    raisedOnly++;
                if (communicationLinked && decision.Own is null)
                {
                    _logger.LogInformation(
                        "[CHILD-ACCESS-PERMISSION] {Table} {Id}: its save-time Restricted (its email could not be identified) " +
                        "was released: it is linked to a communication, so it follows it (owner round 122).", table, id);
                }
                _logger.LogInformation(
                    "[CHILD-ACCESS-PERMISSION] set: {Table} {Id} {From} -> {To} (inherited {Inherited} from {Tops}; own {Own}; " +
                    "record {Record}).", table, id, from, decision.Target, inheritedText, tops, own, recordAction);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                carry.Add((table, id));
                if (problems.Count < MaxProblems)
                    problems.Add($"{table} {id:D}: the write failed ({ex.GetType().Name})");
                _logger.LogError(ex, "[CHILD-ACCESS-PERMISSION] {Table} {Id}: writing its Access Permission {To} (own {Own}) " +
                    "failed; it is tried again next run.", table, id, decision.Target, own);
            }
        }

        return new ChildAccessPermissionRun
        {
            RowsChanged = rowsChanged,
            Examined = examined,
            AlreadyCorrect = correct,
            Changed = changed,
            WouldChange = would,
            Parentless = parentless,
            Undetermined = undetermined,
            Failed = failed,
            Deferred = deferred,
            WriteCapReached = deferred > 0,
            Problems = problems,
            Changes = changes,
            Carry = carry,
            SweepRows = sweepRows,
            SweepResumeAfter = sweepResume,
            SweepReachedEnd = sweepEnd,
            TablesWithoutColumn = missingColumn,
            RecordsWritten = recordsWritten,
            OwnKept = ownKept,
            ChangedConcurrently = changedConcurrently,
            RaisedOnly = raisedOnly,
        };
    }

    /// <summary>
    /// Owner round 122: whether the document <paramref name="row"/> is linked to a communication, by THE communication walk of
    /// the external document narrowing (<see cref="ExternalDocumentNarrowing.FindCommunicationLinksAsync"/>):
    /// its own <c>sprk_relatedcommunication</c>, or the first one up its <c>sprk_parentdocument</c> chain within 3 hops. Its
    /// parents come from this run's walk, which has usually read them already for the floor. Fail closed: a chain that
    /// cannot be known, or a read that faults, answers false (the narrowing stays), with a warning.
    /// </summary>
    private async Task<bool> CommunicationLinkedAsync(string table, Guid id, Entity row, CancellationToken ct)
    {
        try
        {
            var subject = ExternalDocumentNarrowing.FromEntity(row);
            var found = await ExternalDocumentNarrowing.FindCommunicationLinksAsync(
                [subject],
                async (ids, c) => (await _walk.RowsAsync(table, ids, c).ConfigureAwait(false))
                    .Select(ExternalDocumentNarrowing.FromEntity).ToList(),
                ct).ConfigureAwait(false);
            if (found.TryGetValue(subject, out var link) && link.Unknowable is { } why)
            {
                _logger.LogWarning(
                    "[CHILD-ACCESS-PERMISSION] {Table} {Id}: whether it is linked to a communication cannot be known ({Reason}); " +
                    "its save-time Restricted stays.", table, id,
                    DocumentNarrowingResult.CodeOf(why));
            }

            return found.TryGetValue(subject, out var result) && result.IsLinked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[CHILD-ACCESS-PERMISSION] {Table} {Id}: its communication link could not be read; its save-time Restricted " +
                "stays.", table, id);
            return false;
        }
    }

    /// <summary>The position a sweep starts from: before the first row of the first table.</summary>
    internal static readonly (string Table, Guid Id) SweepFromStart = (string.Empty, Guid.Empty);

    private async Task<(IReadOnlyList<Entity> Rows, bool ReachedEnd)> SweepWindowAsync(
        (string Table, Guid Id) after, int cap, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var table in InheritedAccessPermission.Tables.OrderBy(t => t, StringComparer.Ordinal))
        {
            if (string.CompareOrdinal(table, after.Table) < 0)
                continue;
            var remaining = cap - rows.Count;
            if (remaining <= 0)
                return (rows, false);

            var query = new QueryExpression(table)
            {
                ColumnSet = await ColumnSetAsync(table, ct).ConfigureAwait(false),
                NoLock = true,
                TopCount = remaining + 1,
            };
            query.AddOrder(table + "id", OrderType.Ascending);
            if (string.Equals(table, after.Table, StringComparison.Ordinal) && after.Id != Guid.Empty)
                query.Criteria.AddCondition(table + "id", ConditionOperator.GreaterThan, after.Id);

            var page = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
            rows.AddRange(page.Take(remaining));
            if (page.Count > remaining)
                return (rows, false);
        }

        return (rows, true);
    }

    private async Task DescendAsync(
        IReadOnlyList<Entity> seeds, Dictionary<(string Table, Guid Id), Entity> candidates, CancellationToken ct)
    {
        var seen = new HashSet<(string, Guid)>(seeds.Select(s => Key(s.LogicalName, s.Id)));
        IReadOnlyList<(string Table, Guid Id)> frontier = seen.ToList();
        for (var level = 1; level <= ParentLineageWalk.MaxDepth && frontier.Count > 0; level++)
        {
            var idsByTable = frontier
                .GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
            var next = new List<(string, Guid)>();

            foreach (var table in AboveTheFour.Where(t => ParentLineage.ChildFiling.ContainsKey(t)).OrderBy(t => t, StringComparer.Ordinal))
            {
                var present = await _walk.FilingLookupsOfAsync(table, ct).ConfigureAwait(false);
                var map = ParentLineage.ChildFiling[table];
                var pairs = present
                    .Where(column => idsByTable.ContainsKey(map[column]))
                    .SelectMany(column => idsByTable[map[column]].Select(id => (Column: column, Id: id)))
                    .ToArray();

                foreach (var chunk in pairs.Chunk(IdsPerQuery))
                {
                    var query = new QueryExpression(table)
                    {
                        ColumnSet = await ColumnSetAsync(table, ct).ConfigureAwait(false),
                        NoLock = true,
                    };
                    var anyParent = new FilterExpression(LogicalOperator.Or);
                    foreach (var column in chunk.GroupBy(p => p.Column, StringComparer.OrdinalIgnoreCase))
                        anyParent.AddCondition(column.Key, ConditionOperator.In, column.Select(p => (object)p.Id).ToArray());
                    query.Criteria.AddFilter(anyParent);

                    foreach (var row in await ReadAllAsync(query, ct).ConfigureAwait(false))
                    {
                        if (!seen.Add(Key(row.LogicalName, row.Id)))
                            continue;
                        Take(row, candidates);
                        next.Add(Key(row.LogicalName, row.Id));
                    }
                }
            }

            frontier = next;
        }
    }

    /// <summary>Remembers a listed row for the walk, and keeps it as a candidate when it is one of the four.</summary>
    private void Take(Entity row, Dictionary<(string Table, Guid Id), Entity> candidates)
    {
        _walk.Remember(row);
        if (InheritedAccessPermission.AppliesTo(row.LogicalName))
            candidates[Key(row.LogicalName, row.Id)] = row;
    }

    private async Task<ColumnSet> ColumnSetAsync(string table, CancellationToken ct)
    {
        var columns = await _walk.ColumnsForAsync(table, ct).ConfigureAwait(false);
        if (_recordTables.Contains(table))
            columns = [.. columns, AccessInheritance.Column]; // task 189: the document's own-narrowing record, read with the row
        return columns.Count > 0 ? new ColumnSet(columns.ToArray()) : new ColumnSet(table.ToLowerInvariant() + "id");
    }

    /// <summary>
    /// Task 189: whether a document still holds what its decision read: its value and its record, re-read just before the
    /// write. This is task 175's pattern (verifier c), because the app-only writer has no conditional update. A row that is
    /// gone answers false. <paramref name="withRecord"/> false (no record column here, fix round C K2): the value alone.
    /// </summary>
    private async Task<bool> StillAsReadAsync(
        string table, Guid id, int? stored, string? record, bool withRecord, CancellationToken ct)
    {
        var query = new QueryExpression(table.ToLowerInvariant())
        {
            ColumnSet = withRecord
                ? new ColumnSet(InheritedAccessPermission.Column, AccessInheritance.Column)
                : new ColumnSet(InheritedAccessPermission.Column),
        };
        query.Criteria.AddCondition(table.ToLowerInvariant() + "id", ConditionOperator.Equal, id);
        var now = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return now is not null
               && now.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value == stored
               && (!withRecord
                   || string.Equals(now.GetAttributeValue<string>(AccessInheritance.Column) ?? string.Empty,
                       record ?? string.Empty, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<Entity>> ReadAllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
        var rows = new List<Entity>();
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException(
                    $"{query.EntityName} still had rows after {MaxPages} pages; the window is not reconciled in part.");
            var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
                return rows;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    private static (string Table, Guid Id) Key(string table, Guid id) => (table.ToLowerInvariant(), id);

    private static IReadOnlySet<string> Closure()
    {
        var set = new HashSet<string>(InheritedAccessPermission.Tables, StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<string>(set);
        while (frontier.Count > 0)
        {
            var table = frontier.Dequeue();
            if (!ParentLineage.ChildFiling.TryGetValue(table, out var lookups))
                continue;
            foreach (var target in lookups.Values)
            {
                if (set.Add(target))
                    frontier.Enqueue(target);
            }
        }

        return set;
    }
}
