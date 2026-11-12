using System.Collections.Immutable;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191b (#2179 design v2 §3.3): THE predicate that decides whether a lineage row is externally
/// visible to a flagged-external systemuser. It decides a batch of rows as <see cref="ExternalVisibilityVerdict.Visible"/>,
/// <see cref="ExternalVisibilityVerdict.Hidden"/> or <see cref="ExternalVisibilityVerdict.Undecided"/>, over app-only reads.
/// It grants, revokes and writes nothing; its callers (the team-share job, the Office edit check, the SPA lists, the AI tools,
/// retrieval) come in later tasks, and every one treats Undecided as hidden.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> A row is visible only when all three hold:</para>
/// <list type="number">
/// <item><b>No linked root is Restricted</b>, effective through filing (<see cref="EffectiveRootFlagReader"/>, bound to
/// <c>ExternalParticipationService.GetEffectiveRootRecordFlagsAsync</c>: the root's own flags folded with every record it is
/// filed under). A child's I-16 display copy of its root's permission is never read. A root whose flags cannot be read is
/// Undecided (owner Q4, 2026-11-11: Restricted is internal-only; Secure is decided per user, elsewhere).</item>
/// <item><b>Its own marker passes</b> (<see cref="ExternalMarker"/>, declared on every <see cref="SecureChildLineage.Table"/>):
/// <see cref="ExternalMarkerKind.Never"/> hides; <see cref="ExternalOwnRule.Communication065"/> is THE task 065 rule
/// (<see cref="CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync"/>); <see cref="ExternalOwnRule.ThreadOpen"/> is its
/// thread half (<see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/>); <see cref="ExternalOwnRule.DocumentNarrowing"/>
/// is task 188's narrowing through its app-only seam (<see cref="AppOnlyDocumentNarrowing"/>), plus a legacy Procedure Library
/// document (its drive is <see cref="Readers.ProcedureLibraryDriveId"/>, the rule <c>RuleSetService</c> applies) hides;
/// <see cref="ExternalOwnRule.InvoiceSource"/> is ebilling (a′) (<see cref="InvoiceDecider"/>, bound to
/// <c>ExternalInvoiceVisibility.DecideAsync</c>). Each rule is called, never re-implemented.</item>
/// <item><b>Taint closure</b> (owner Q3, round 114): no lineage parent, through ANY registry lookup or a Signal's or Decision
/// Record's core record pair, is hidden by 1 or 2, walked upward to the roots.</item>
/// </list>
/// <para><b>The walk.</b> Bounded like the secure-child lineage walk (<see cref="MaxLineageDepth"/>, the same bound and the same
/// root-first check as <c>SecureChildShareSynchronizer.LineageOfAsync</c>): a row whose filing runs deeper is Undecided. A parent
/// already on the path is skipped, as there: the document ↔ current-version pair (<c>sprk_currentversionid</c> /
/// <c>sprk_fileversion.sprk_document</c>) names every versioned document twice, and a closure over a finite set gains nothing
/// from visiting a row again. One batched read per level and table (<see cref="CommunicationOpenThreadGate.ChunkSize"/> ids per
/// <c>In</c> condition, <see cref="CommunicationOpenThreadGate.MaxChunksInFlight"/> chunks in flight), never one read per row.</para>
/// <para><b>The document chain keeps task 188's bound.</b> 188's own walk owns a document's parent-document chain (its
/// Restricted ancestors and its first communication link, at most <see cref="ExternalDocumentNarrowing.MaxHops"/> hops; a deeper
/// chain or a cycle is <see cref="DocumentNarrowingReason.ChainTooLong"/> / <see cref="DocumentNarrowingReason.Cycle"/>, Undecided
/// here). A document the walk reaches through a <c>sprk_parentdocument</c> edge (a chain member) contributes everything EXCEPT
/// its own 188 verdict: its lookups, its communication link included, are lineage edges like any other, so an attachment under
/// the .eml of a hidden communication is hidden with it (design §3.3 rule 3: no hidden lineage parent). A chain longer than
/// <see cref="ExternalDocumentNarrowing.MaxHops"/> is Undecided. Every other edge into a document (an analysis's source, a
/// to-do's regarding document, a canonical copy) asks that document's own 188 verdict.</para>
/// <para><b>Own rules are dispatched on the declared marker.</b> Each row's own rule is the <see cref="ExternalOwnRule"/> its
/// table declares; <see cref="RuleTables"/> names the one table each rule's implementation reads, and a rule declared on any other
/// table, or a rule with no implementation, faults the decision (fail closed) rather than being skipped.</para>
/// <para><b>Fail closed.</b> A row or parent that does not come back, a lookup value that is not a reference, a malformed core
/// pair or a type the catalog maps to no core table, a table the registry does not know, an unreadable root, a 188 chain that
/// cannot be known, an invoice decision that failed and a closure that reaches no root (a parentless row, a rootless cycle) are
/// Undecided, never Visible. A hard-deny table (<see cref="ExternalMarkerKind.HardDeny"/>) and a Never table are Hidden. A read
/// that throws makes EVERY row Undecided; a rule that answers faulted keeps the rows already Hidden (a hide decided by a sound
/// rule stays sound) and makes every other row Undecided. Either way the result is
/// <see cref="ExternalVisibilityResult.Faulted"/>; cancellation propagates. A row the decision was not asked about is
/// Undecided. Hidden beats Undecided, which beats Visible.</para>
/// <para><b>Whose reads.</b> Every reader in <see cref="Readers"/> must be the BFF's app-only one (the external plane's): this is a
/// security check, so a column field security masks for a user can never read as "no parent" or "no link".</para>
/// <para><b>Logs (ADR-015).</b> Counts per verdict and reason code only; a fault logs its exception type only.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalDocumentNarrowing"/> (documents),
/// <see cref="CommunicationOpenThreadGate"/> (065), <see cref="EffectiveRootFlags"/> (Restricted through filing),
/// <c>ExternalInvoiceVisibility</c> (ebilling (a′)) and <c>ExternalCallerCommunicationGuard</c>'s two table lists. All are
/// called as they are; the two lists now derive from the registry markers. (2) Extension — each existing rule decides one table
/// family; this predicate spans every registry table and walks the lineage between them, so it cannot live inside any one of
/// them. (3) Cost of doing nothing — the share job, the secure mirror, the SPA lists and the AI tools would each re-derive
/// "internal-only", and the copies drift (#2189).</para>
/// </remarks>
internal static class ExternalVisibility
{
    /// <summary>The deepest filing the walk follows: the secure-child lineage walk's own bound.</summary>
    internal const int MaxLineageDepth = SecureChildShareSynchronizer.MaxLineageDepth;

    /// <summary>A document's drive (<c>sprk_graphdriveid</c>), read to find a legacy Procedure Library document.</summary>
    internal const string DriveColumn = "sprk_graphdriveid";

    /// <summary>Log event name of the decision summary and of a fault (counts and codes only).</summary>
    internal const string LogEvent = "sdap.external.visibility";

    /// <summary>
    /// The one table each own-row rule's implementation reads: the 065 rule reads communications, the thread rule threads, task
    /// 188 documents and (a′) invoices. Rows are dispatched on the rule their table DECLARES (<see cref="ExternalMarker.Rule"/>);
    /// a declared rule missing here, or declared on another table, faults the decision. Pinned against the registry's declared
    /// (table, rule) pairs by <c>ExternalVisibilityTests</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<ExternalOwnRule, string> RuleTables = new Dictionary<ExternalOwnRule, string>
    {
        [ExternalOwnRule.Communication065] = "sprk_communication",
        [ExternalOwnRule.ThreadOpen] = "sprk_communicationthread",
        [ExternalOwnRule.DocumentNarrowing] = ExternalDocumentNarrowing.DocumentTable,
        [ExternalOwnRule.InvoiceSource] = "sprk_invoice",
    };

    /// <summary>
    /// ebilling (a′) over a batch of invoices with no caller (no firm rule): <c>ExternalInvoiceVisibility.DecideAsync</c> over the
    /// external plane's app-only readers. A failed read THROWS or comes back <see cref="InvoiceVisibilityResult.Faulted"/>.
    /// </summary>
    internal delegate Task<InvoiceVisibilityResult> InvoiceDecider(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);

    /// <summary>What the predicate reads through. Every reader must be the BFF's app-only one.</summary>
    /// <param name="Entities">Lineage rows by id (their lookups and core pair; a document's drive) and the record-type catalog.</param>
    /// <param name="Query">The external plane's OData reader, for the 065 rule and the thread privacy read; a failed read THROWS.</param>
    /// <param name="Documents">Task 188's narrowing through its app-only seam (a hide decision only).</param>
    /// <param name="RootFlags">The effective root flags (Restricted through filing).</param>
    /// <param name="Invoices">ebilling (a′), caller-less.</param>
    /// <param name="ProcedureLibraryDriveId"><c>Finance:ProcedureLibraryContainerId</c>; <c>null</c> or blank: the environment has
    /// no Procedure Library, so no document is one.</param>
    /// <param name="Logger">For the summary and the fault line (counts and codes only).</param>
    internal sealed record Readers(
        IGenericEntityService Entities,
        CommunicationOpenThreadGate.RowQuery Query,
        AppOnlyDocumentNarrowing Documents,
        EffectiveRootFlagReader RootFlags,
        InvoiceDecider Invoices,
        string? ProcedureLibraryDriveId,
        ILogger Logger);

    /// <summary>
    /// Decides each of <paramref name="rows"/> (table logical name, id). Duplicates and empty ids are ignored (an empty id is
    /// answered <see cref="ExternalVisibilityReason.NotAsked"/>). A failed read makes every row Undecided
    /// (<see cref="ExternalVisibilityResult.Faulted"/>); cancellation propagates.
    /// </summary>
    internal static async Task<ExternalVisibilityResult> DecideAsync(
        IEnumerable<(string Table, Guid Id)> rows, Readers readers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(readers);
        var asked = rows
            .Where(r => r.Id != Guid.Empty && !string.IsNullOrWhiteSpace(r.Table))
            .Select(r => new Node(r.Table, r.Id))
            .Distinct()
            .ToList();
        if (asked.Count == 0)
            return new ExternalVisibilityResult(new Dictionary<Node, ExternalVisibilityDecision>(), faulted: false);

        try
        {
            var run = new Run(readers, ct);
            var decided = await run.DecideAsync(asked).ConfigureAwait(false);
            if (run.Faulted)
            {
                // A rule answered "my read failed" rather than throwing (188's DocumentNarrowingResult.ReadFailed, an (a′)
                // decision with a failed read). A row already Hidden by a sound rule (a Restricted root, a Never or hard-deny
                // table, the 065 rule) stays Hidden; every other row is undecided, and the result says Faulted.
                var failed = Failed(asked, decided);
                readers.Logger.LogWarning(
                    "[EXTERNAL-VISIBILITY] {LogEvent} a rule's read failed; {Undecided} of the {Rows} rows are undecided (the rest stay hidden).",
                    LogEvent, asked.Count(n => !failed.For(n.Table, n.Id).IsHidden), asked.Count);
                return failed;
            }

            readers.Logger.LogDebug(
                "[EXTERNAL-VISIBILITY] {LogEvent} decided {Rows} rows: visible={Visible} hidden={Hidden} undecided={Undecided}; {Reasons}",
                LogEvent, decided.Count,
                decided.Values.Count(d => d.Verdict == ExternalVisibilityVerdict.Visible),
                decided.Values.Count(d => d.Verdict == ExternalVisibilityVerdict.Hidden),
                decided.Values.Count(d => d.Verdict == ExternalVisibilityVerdict.Undecided),
                ExternalVisibilityResult.Summary(decided.Values));
            return new ExternalVisibilityResult(decided, faulted: false)
            {
                RootsReached = run.RootsReached,
                EffectivelySecureRoots = run.EffectivelySecureRoots,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            readers.Logger.LogWarning(
                "[EXTERNAL-VISIBILITY] {LogEvent} a read failed ({ErrorType}); every one of the {Rows} rows is undecided.",
                LogEvent, ex.GetType().Name, asked.Count);
            return Failed(asked, decided: null);
        }
    }

    /// <summary>A faulted decision: every asked row <see cref="ExternalVisibilityReason.ReadFailed"/>, except a row
    /// <paramref name="decided"/> already holds as Hidden (kept).</summary>
    private static ExternalVisibilityResult Failed(
        IReadOnlyList<Node> asked, IReadOnlyDictionary<Node, ExternalVisibilityDecision>? decided) =>
        new(asked.ToDictionary(n => n, n =>
                decided is not null && decided.TryGetValue(n, out var d) && d.Verdict == ExternalVisibilityVerdict.Hidden
                    ? d
                    : new ExternalVisibilityDecision(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed, n.Table, n.Id)),
            faulted: true);

    /// <summary>The 188 reasons that say the document cannot be decided (Undecided), rather than that the rule hides it.</summary>
    internal static bool IsUndecidable(DocumentNarrowingReason reason) => reason is
        DocumentNarrowingReason.ChainTooLong or DocumentNarrowingReason.Cycle or DocumentNarrowingReason.ParentUnreadable
        or DocumentNarrowingReason.ParentNotFound or DocumentNarrowingReason.LinkUnreadable or DocumentNarrowingReason.OwnUnreadable
        or DocumentNarrowingReason.AncestorUnreadable or DocumentNarrowingReason.ReadFailed;

    /// <summary>The (a′) reasons that say the invoice cannot be decided (Undecided), rather than that the rule hides it.</summary>
    internal static bool IsUndecidable(InvoiceHiddenReason reason) => reason is
        InvoiceHiddenReason.ReadFailed or InvoiceHiddenReason.SourcesTruncated or InvoiceHiddenReason.SourceLinkUnreadable
        or InvoiceHiddenReason.SourceNotFound or InvoiceHiddenReason.RootUnresolved or InvoiceHiddenReason.SourceKindUnknown
        or InvoiceHiddenReason.OwnSubmissionMissing;

    /// <summary>A row: its table (lower case) and id.</summary>
    internal readonly record struct Node
    {
        internal Node(string table, Guid id)
        {
            Table = table.Trim().ToLowerInvariant();
            Id = id;
        }

        internal string Table { get; }

        internal Guid Id { get; }

        public override string ToString() => $"{Table}:{Id:D}";
    }

    /// <summary>One step of the walk: the row, its run of parent-document hops, the path that reached it and its depth.</summary>
    private sealed record State(Subject Subject, Node Node, int Run, ImmutableHashSet<Node> Path, int Depth);

    /// <summary>One asked row's closure: what it reached, and what decided it.</summary>
    private sealed class Subject(Node node)
    {
        internal Node Node { get; } = node;

        /// <summary>Every (row, parent-document run) the walk has queued, so each is walked once.</summary>
        internal HashSet<(Node, int)> Seen { get; } = new();

        /// <summary>Rows reached with run 0: their own rule applies in full.</summary>
        internal HashSet<Node> Full { get; } = new();

        /// <summary>Every row reached (chain members included).</summary>
        internal HashSet<Node> All { get; } = new();

        /// <summary>The roots reached.</summary>
        internal HashSet<Node> Roots { get; } = new();

        /// <summary>The structural findings of the walk (a hidden marker, an undecidable parent), in the order found.</summary>
        internal List<ExternalVisibilityDecision> Found { get; } = new();

        internal void Add(ExternalVisibilityVerdict verdict, ExternalVisibilityReason reason, Node source) =>
            Found.Add(new ExternalVisibilityDecision(verdict, reason, source.Table, source.Id));
    }

    /// <summary>One decision: its row cache, its catalog cache and the own-rule verdicts per row.</summary>
    private sealed class Run(Readers readers, CancellationToken ct)
    {
        private readonly Dictionary<Node, Entity?> _rows = new();
        private readonly Dictionary<Guid, string?> _coreTables = new();

        /// <summary>A rule came back faulted instead of throwing: the whole decision is a fault.</summary>
        internal bool Faulted { get; private set; }

        internal async Task<Dictionary<Node, ExternalVisibilityDecision>> DecideAsync(IReadOnlyList<Node> asked)
        {
            var subjects = asked.Select(n => new Subject(n)).ToList();
            var frontier = new List<State>();
            foreach (var subject in subjects)
            {
                var node = subject.Node;
                if (SecureChildLineage.IsRoot(node.Table))
                {
                    subject.Roots.Add(node); // a root is decided by its own effective flags
                }
                else if (!SecureChildLineage.IsChild(node.Table))
                {
                    // Outside the lineage: Never (communication content) hides; anything else is not the registry's to decide.
                    subject.Add(
                        SecureChildLineage.ExternalMarkerOf(node.Table)?.Kind == ExternalMarkerKind.Never
                            ? ExternalVisibilityVerdict.Hidden : ExternalVisibilityVerdict.Undecided,
                        SecureChildLineage.ExternalMarkerOf(node.Table)?.Kind == ExternalMarkerKind.Never
                            ? ExternalVisibilityReason.NeverExternal : ExternalVisibilityReason.UnknownTable,
                        node);
                }
                else
                {
                    subject.Seen.Add((node, 0));
                    frontier.Add(new State(subject, node, 0, ImmutableHashSet.Create(node), 0));
                }
            }

            while (frontier.Count > 0)
            {
                await ReadRowsAsync(frontier.Select(s => s.Node)).ConfigureAwait(false);
                var next = new List<State>();
                foreach (var state in frontier)
                    await ExpandAsync(state, next).ConfigureAwait(false);
                frontier = next;
            }

            RootsReached = subjects.ToDictionary(s => s.Node, s => (IReadOnlyCollection<Node>)s.Roots.ToArray());
            var (own, narrowed) = await OwnVerdictsAsync(subjects).ConfigureAwait(false);
            return subjects.ToDictionary(s => s.Node, s => Combine(s, own, narrowed));
        }

        /// <summary>uac-r2 191c: the roots each asked row's closure reached (a root reaches itself), for a caller that must also
        /// keep a secure family out (the External-team share); empty until <see cref="DecideAsync"/> has walked.</summary>
        internal IReadOnlyDictionary<Node, IReadOnlyCollection<Node>> RootsReached { get; private set; } =
            new Dictionary<Node, IReadOnlyCollection<Node>>();

        /// <summary>uac-r2 191c: the reached roots whose EFFECTIVE flags (<see cref="Readers.RootFlags"/>: Secure through filing)
        /// say Secure, or could not be read — the roots of a secure family, whatever the root's own stored flag says yet.</summary>
        internal HashSet<Node> EffectivelySecureRoots { get; } = new();

        /// <summary>One row's parents, queued (or decided: a root, an undecidable parent).</summary>
        private async Task ExpandAsync(State state, List<State> next)
        {
            var subject = state.Subject;
            var node = state.Node;
            if (_rows[node] is not { } row)
            {
                subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.RowNotFound, node);
                return;
            }

            var table = SecureChildLineage.Children[node.Table];
            var chainMember = state.Run > 0;
            subject.All.Add(node);
            if (!chainMember)
                subject.Full.Add(node);
            if (table.External.Kind == ExternalMarkerKind.Never)
                subject.Add(ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.NeverExternal, node);
            if (table.External.Kind == ExternalMarkerKind.HardDeny)
                subject.Add(ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.HardDeny, node);

            // Every lookup is a lineage edge, a chain member's communication link included (design §3.3 rule 3).
            var isDocument = table.External.Rule == ExternalOwnRule.DocumentNarrowing;
            var parents = new List<(Node Parent, bool ChainEdge)>();
            foreach (var column in table.LookupOrder)
            {
                if (!row.Attributes.TryGetValue(column, out var value) || value is null)
                    continue;
                if (value is not EntityReference reference)
                {
                    subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.LinkUnreadable, node);
                    continue;
                }

                if (reference.Id == Guid.Empty)
                    continue;
                var chainEdge = isDocument
                    && string.Equals(column, ExternalDocumentNarrowing.ParentDocumentAttribute, StringComparison.OrdinalIgnoreCase);
                parents.Add((new Node(table.Lookups[column], reference.Id), chainEdge));
            }

            if (table.HoldsCorePair && await CorePairParentAsync(row).ConfigureAwait(false) is { } pair)
            {
                if (pair.Undecidable)
                    subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.CorePairUndecided, node);
                else if (pair.Core is { } core)
                    parents.Add((core, false));
            }

            foreach (var (parent, chainEdge) in parents.Distinct())
            {
                if (state.Path.Contains(parent))
                    continue; // a cycle (a document's current version points back at the document): nothing new to visit

                if (SecureChildLineage.IsRoot(parent.Table))
                {
                    subject.Roots.Add(parent);
                    continue;
                }

                if (!SecureChildLineage.IsChild(parent.Table))
                {
                    subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.UnknownTable, parent);
                    continue;
                }

                var run = chainEdge ? state.Run + 1 : 0;
                if (run > ExternalDocumentNarrowing.MaxHops)
                {
                    subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ChainTooLong, parent);
                    continue;
                }

                if (state.Depth + 1 >= MaxLineageDepth)
                {
                    subject.Add(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.TooDeep, parent);
                    continue;
                }

                if (subject.Seen.Add((parent, run)))
                    next.Add(new State(subject, parent, run, state.Path.Add(parent), state.Depth + 1));
            }
        }

        /// <summary>
        /// The parent a Signal's or Decision Record's core record pair names (<see cref="SecureChildLineage.CorePairParent"/>):
        /// <c>(null, false)</c> for no pair or a service-request core; <c>(null, true)</c> for a malformed pair or a type the
        /// catalog maps to no core table. <c>null</c> when the row has no pair at all. A catalog fault propagates.
        /// </summary>
        private async Task<(Node? Core, bool Undecidable)?> CorePairParentAsync(Entity row)
        {
            var pair = RecordOwnershipContext.CorePairOf(row);
            if (pair.Malformed)
                return (null, true);
            if (!pair.IsWhole)
                return null;

            var type = pair.TypeRef!.Value;
            if (!_coreTables.TryGetValue(type, out var coreTable))
            {
                coreTable = await CoreAncestorResolver.CoreTableOfRecordTypeAsync(readers.Entities, type, ct).ConfigureAwait(false);
                _coreTables[type] = coreTable;
            }

            if (coreTable is null)
                return (null, true);
            return SecureChildLineage.CorePairParent(coreTable, pair.CoreId!.Value) is { } core
                ? (new Node(core.Table, core.Id), false)
                : (null, false);
        }

        /// <summary>Reads the rows not read yet, one batched read per table (chunks of ids, in flight together). A row that does not
        /// come back is cached as <c>null</c>. A failed read THROWS.</summary>
        private async Task ReadRowsAsync(IEnumerable<Node> nodes)
        {
            foreach (var group in nodes.Where(n => !_rows.ContainsKey(n)).Distinct().GroupBy(n => n.Table))
            {
                var table = SecureChildLineage.Children[group.Key];
                var columns = table.LineageColumns.ToList();
                if (table.External.Rule == ExternalOwnRule.DocumentNarrowing)
                    columns.Add(DriveColumn);
                var ids = group.Select(n => n.Id).ToList();
                var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(ids, (chunk, c) =>
                {
                    var query = new QueryExpression(table.LogicalName) { ColumnSet = new ColumnSet(columns.ToArray()) };
                    query.Criteria.AddCondition(table.IdColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
                    return readers.Entities.RetrieveMultipleAsync(query, c);
                }, ct).ConfigureAwait(false);

                foreach (var id in ids)
                    _rows[new Node(group.Key, id)] = null;
                foreach (var (chunk, collection) in chunks)
                {
                    foreach (var entity in collection.Entities)
                    {
                        if (entity.Id != Guid.Empty && chunk.Contains(entity.Id))
                            _rows[new Node(group.Key, entity.Id)] = entity;
                    }
                }
            }
        }

        /// <summary>
        /// The own-rule and root verdicts of every row the walks reached, each rule asked once over its distinct rows. Task 188's
        /// verdicts come back apart (<c>Narrowed</c>): they count only for a subject that reached the document in full, never for
        /// one that reached it as a chain member (the same document can be both, for two subjects of one batch).
        /// </summary>
        private async Task<(Dictionary<Node, ExternalVisibilityDecision> Own, Dictionary<Node, ExternalVisibilityDecision> Narrowed)>
            OwnVerdictsAsync(IReadOnlyList<Subject> subjects)
        {
            var verdicts = new Dictionary<Node, ExternalVisibilityDecision>();
            var narrowed = new Dictionary<Node, ExternalVisibilityDecision>();
            void Set(Node node, ExternalVisibilityVerdict verdict, ExternalVisibilityReason reason) =>
                verdicts[node] = new ExternalVisibilityDecision(verdict, reason, node.Table, node.Id);
            // Each reached row's own rule is the one its table DECLARES (ExternalMarker.Rule). A rule with no implementation, or
            // declared on a table its implementation does not read, faults the decision (fail closed); it is never skipped.
            var byRule = subjects.SelectMany(s => s.All).Distinct()
                .GroupBy(n => SecureChildLineage.Children[n.Table].External.Rule)
                .Where(g => g.Key != ExternalOwnRule.None)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var (rule, nodes) in byRule)
            {
                if (!RuleTables.TryGetValue(rule, out var ruleTable) || nodes.Any(n => n.Table != ruleTable))
                    throw new InvalidOperationException($"The own-row rule {rule} is declared on a table its implementation does not read.");
            }

            var handled = new HashSet<ExternalOwnRule>();
            List<Node> NodesOf(ExternalOwnRule rule, bool fullOnly)
            {
                handled.Add(rule);
                if (!byRule.TryGetValue(rule, out var nodes))
                    return new List<Node>();
                return fullOnly ? nodes.Where(n => subjects.Any(s => s.Full.Contains(n))).ToList() : nodes;
            }

            // 1. Roots: Restricted through filing (effective), an unreadable root is undecidable.
            foreach (var group in subjects.SelectMany(s => s.Roots).Distinct().GroupBy(n => n.Table))
            {
                var ids = group.Select(n => n.Id).ToList();
                var flags = await readers.RootFlags(group.Key, ids, ct).ConfigureAwait(false);
                foreach (var node in group)
                {
                    if (!flags.TryGetValue(node.Id, out var secureCheck) || secureCheck.IsUnreadable || secureCheck.IsSecure)
                        EffectivelySecureRoots.Add(node);
                    if (!flags.TryGetValue(node.Id, out var f) || f.IsUnreadable)
                        Set(node, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.RootUnreadable);
                    else if (f.IsRestricted)
                        Set(node, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.RootRestricted);
                }
            }

            // 2. Communication065: THE 065 rule.
            var communications = NodesOf(ExternalOwnRule.Communication065, fullOnly: false);
            if (communications.Count > 0)
            {
                var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
                    communications.Select(n => n.Id).ToList(), readers.Query, ct).ConfigureAwait(false);
                foreach (var node in communications.Where(n => !visible.Contains(n.Id)))
                    Set(node, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.CommunicationHidden);
            }

            // 3. ThreadOpen: explicitly Open now.
            var threads = NodesOf(ExternalOwnRule.ThreadOpen, fullOnly: false);
            if (threads.Count > 0)
            {
                var open = await CommunicationOpenThreadGate.ReadOpenThreadIdsAsync(
                    threads.Select(n => n.Id).ToList(), (odata, c) => readers.Query(CommunicationOpenThreadGate.ThreadSet, odata, c), ct)
                    .ConfigureAwait(false);
                foreach (var node in threads.Where(n => !open.Contains(n.Id)))
                    Set(node, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.ThreadNotOpen);
            }

            // 4. DocumentNarrowing: a legacy Procedure Library document (any document reached), then task 188 (documents reached
            //    in full).
            var library = readers.ProcedureLibraryDriveId;
            foreach (var node in NodesOf(ExternalOwnRule.DocumentNarrowing, fullOnly: false))
            {
                if (!string.IsNullOrWhiteSpace(library) && _rows.TryGetValue(node, out var row) && row is not null
                    && string.Equals(row.GetAttributeValue<string>(DriveColumn)?.Trim(), library.Trim(), StringComparison.OrdinalIgnoreCase))
                    Set(node, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.ProcedureLibraryDocument);
            }

            var documents = NodesOf(ExternalOwnRule.DocumentNarrowing, fullOnly: true);
            if (documents.Count > 0)
            {
                var narrowing = await readers.Documents(documents.Select(n => n.Id).ToList(), ct).ConfigureAwait(false);
                Faulted |= narrowing.Faulted;
                foreach (var node in documents)
                {
                    var reason = narrowing.Faulted ? DocumentNarrowingReason.ReadFailed : narrowing.ReasonFor(node.Id);
                    if (reason == DocumentNarrowingReason.Visible)
                        continue;
                    narrowed[node] = new ExternalVisibilityDecision(
                        IsUndecidable(reason) ? ExternalVisibilityVerdict.Undecided : ExternalVisibilityVerdict.Hidden,
                        IsUndecidable(reason) ? ExternalVisibilityReason.DocumentUndecided : ExternalVisibilityReason.DocumentHidden,
                        node.Table, node.Id);
                }
            }

            // 5. InvoiceSource: ebilling (a′).
            var invoices = NodesOf(ExternalOwnRule.InvoiceSource, fullOnly: false);
            if (invoices.Count > 0)
            {
                var decision = await readers.Invoices(invoices.Select(n => n.Id).ToList(), ct).ConfigureAwait(false);
                Faulted |= decision.AnyFaulted;
                foreach (var node in invoices.Where(n => !decision.IsVisible(n.Id)))
                {
                    var undecidable = decision.IsFaultedFor(node.Id) || decision.HiddenReason(node.Id) is not { } reason
                        || IsUndecidable(reason);
                    Set(node,
                        undecidable ? ExternalVisibilityVerdict.Undecided : ExternalVisibilityVerdict.Hidden,
                        undecidable ? ExternalVisibilityReason.InvoiceUndecided : ExternalVisibilityReason.InvoiceHidden);
                }
            }

            // Every declared rule reached must have been asked: a rule added to RuleTables without its step faults, never skips.
            if (byRule.Keys.Except(handled).Any())
                throw new InvalidOperationException("An own-row rule was declared on a reached row but has no step here.");

            return (verdicts, narrowed);
        }

        /// <summary>A subject's answer: the first Hidden finding, else the first Undecided, else Visible. The subject's own
        /// findings come first, then its parents' in the order the walk reached them. A row whose closure reaches no root is
        /// Undecided (<see cref="ExternalVisibilityReason.NoRoot"/>): external access is granted on roots only, so a closure
        /// that reaches none (a parentless row, a cycle with no way out) is never Visible.</summary>
        private static ExternalVisibilityDecision Combine(
            Subject subject, IReadOnlyDictionary<Node, ExternalVisibilityDecision> own,
            IReadOnlyDictionary<Node, ExternalVisibilityDecision> narrowed)
        {
            var findings = new List<ExternalVisibilityDecision>(subject.Found);
            foreach (var node in new[] { subject.Node }.Concat(subject.All).Concat(subject.Roots).Distinct())
            {
                if (own.TryGetValue(node, out var verdict))
                    findings.Add(verdict);
                if (subject.Full.Contains(node) && narrowed.TryGetValue(node, out var document))
                    findings.Add(document);
            }

            // Searched by index, not FirstOrDefault: the default decision IS Undecided (fail closed), so "none found" and
            // "an Undecided found" must not look alike.
            var hidden = findings.FindIndex(f => f.Verdict == ExternalVisibilityVerdict.Hidden);
            if (hidden >= 0)
                return findings[hidden];
            var undecided = findings.FindIndex(f => f.Verdict != ExternalVisibilityVerdict.Visible);
            if (undecided >= 0)
                return findings[undecided];
            return subject.Roots.Count == 0
                ? new ExternalVisibilityDecision(
                    ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.NoRoot, subject.Node.Table, subject.Node.Id)
                : new ExternalVisibilityDecision(
                    ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible, subject.Node.Table, subject.Node.Id);
        }
    }
}

/// <summary>Task 191b: the predicate's three answers. Every caller treats <see cref="Undecided"/> as hidden.</summary>
internal enum ExternalVisibilityVerdict
{
    /// <summary>Not visible: what the predicate cannot decide (fail closed). The default.</summary>
    Undecided = 0,

    /// <summary>Externally visible.</summary>
    Visible = 1,

    /// <summary>A rule hides it.</summary>
    Hidden = 2,
}

/// <summary>Task 191b: why a row has its verdict (logged as counts per code only).</summary>
internal enum ExternalVisibilityReason
{
    /// <summary>Visible.</summary>
    Visible = 0,

    // ── Hidden ──
    /// <summary>A root it is filed under is Restricted, effective through filing.</summary>
    RootRestricted,

    /// <summary>A table marked <see cref="ExternalMarkerKind.Never"/> (communication content).</summary>
    NeverExternal,

    /// <summary>A hard-deny table (<see cref="ExternalMarkerKind.HardDeny"/>: policy, policy version, Procedure assignment, rule
    /// set, Stored File), or a row filed under one.</summary>
    HardDeny,

    /// <summary>A communication the 065 rule hides (internal-only, a blank or unreadable flag, a Private or missing thread).</summary>
    CommunicationHidden,

    /// <summary>A thread that does not read explicitly Open.</summary>
    ThreadNotOpen,

    /// <summary>A document task 188's narrowing hides.</summary>
    DocumentHidden,

    /// <summary>A legacy Procedure Library document (its drive is the Procedure Library container).</summary>
    ProcedureLibraryDocument,

    /// <summary>An invoice ebilling (a′) hides.</summary>
    InvoiceHidden,

    // ── Undecided ──
    /// <summary>A root whose flags could not be read.</summary>
    RootUnreadable,

    /// <summary>A document task 188 cannot decide (its chain is too long, a cycle, a parent or link unreadable or missing).</summary>
    DocumentUndecided,

    /// <summary>An invoice ebilling (a′) cannot decide (a failed read, an unreadable or missing source).</summary>
    InvoiceUndecided,

    /// <summary>The row, or a parent it names, does not come back.</summary>
    RowNotFound,

    /// <summary>A lookup value that is not a reference.</summary>
    LinkUnreadable,

    /// <summary>A malformed core record pair, or a type the catalog maps to no core table.</summary>
    CorePairUndecided,

    /// <summary>A parent-document chain longer than task 188's bound.</summary>
    ChainTooLong,

    /// <summary>A filing deeper than <see cref="ExternalVisibility.MaxLineageDepth"/>.</summary>
    TooDeep,

    /// <summary>A table the registry does not know.</summary>
    UnknownTable,

    /// <summary>The row's closure reaches no root (a parentless row, or a cycle with no way out): nothing grants it.</summary>
    NoRoot,

    /// <summary>A row the decision was not asked about.</summary>
    NotAsked,

    /// <summary>A read failed; every row of the decision is undecided.</summary>
    ReadFailed,
}

/// <summary>Task 191b: one row's answer, and the row that decided it (the row itself, or a parent: taint).</summary>
internal readonly record struct ExternalVisibilityDecision(
    ExternalVisibilityVerdict Verdict, ExternalVisibilityReason Reason, string SourceTable, Guid SourceId)
{
    /// <summary>Visible: the only answer that shows the row.</summary>
    internal bool IsVisible => Verdict == ExternalVisibilityVerdict.Visible;

    /// <summary>A rule hides the row.</summary>
    internal bool IsHidden => Verdict == ExternalVisibilityVerdict.Hidden;
}

/// <summary>Task 191b: the answer for one batch (<see cref="ExternalVisibility.DecideAsync"/>).</summary>
internal sealed class ExternalVisibilityResult
{
    private readonly IReadOnlyDictionary<ExternalVisibility.Node, ExternalVisibilityDecision> _decided;

    internal ExternalVisibilityResult(IReadOnlyDictionary<ExternalVisibility.Node, ExternalVisibilityDecision> decided, bool faulted)
    {
        _decided = decided;
        Faulted = faulted;
    }

    /// <summary>A read failed: every row is <see cref="ExternalVisibilityReason.ReadFailed"/> (a list then reports <c>Truncated</c>).</summary>
    internal bool Faulted { get; }

    /// <summary>The answer for one row; <see cref="ExternalVisibilityReason.NotAsked"/> (Undecided) for a row not asked about.</summary>
    internal ExternalVisibilityDecision For(string table, Guid id) =>
        id != Guid.Empty && !string.IsNullOrWhiteSpace(table) && _decided.TryGetValue(new ExternalVisibility.Node(table, id), out var decision)
            ? decision
            : new ExternalVisibilityDecision(ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.NotAsked, table, id);

    /// <summary>Whether the row is visible (only <see cref="ExternalVisibilityVerdict.Visible"/> is).</summary>
    internal bool IsVisible(string table, Guid id) => For(table, id).IsVisible;

    /// <summary>uac-r2 191c: the roots the row's closure reached when it was decided without a fault (a root reaches itself);
    /// <c>null</c> when they are not known (a faulted decision, a row not asked). A caller that needs them treats <c>null</c> as
    /// "the family cannot be told" (fail closed).</summary>
    internal IReadOnlyCollection<(string Table, Guid Id)>? RootsOf(string table, Guid id) =>
        !Faulted && id != Guid.Empty && !string.IsNullOrWhiteSpace(table)
        && RootsReached.TryGetValue(new ExternalVisibility.Node(table, id), out var roots)
            ? roots.Select(r => (r.Table, r.Id)).ToArray()
            : null;

    /// <summary>uac-r2 191c: set by <see cref="ExternalVisibility.DecideAsync"/> on a decision that did not fault.</summary>
    internal IReadOnlyDictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>> RootsReached { get; init; } =
        new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();

    /// <summary>uac-r2 191c: the reached roots whose effective flags say Secure (Secure through filing) or could not be read.</summary>
    internal IReadOnlySet<ExternalVisibility.Node> EffectivelySecureRoots { get; init; } = new HashSet<ExternalVisibility.Node>();

    /// <summary>uac-r2 191c: whether a reached root of the row is effectively Secure (or unreadable); <c>true</c> when the roots
    /// are not known (fail closed).</summary>
    internal bool ReachesEffectivelySecureRoot(string table, Guid id) =>
        RootsOf(table, id) is not { } roots || roots.Any(r => EffectivelySecureRoots.Contains(new ExternalVisibility.Node(r.Table, r.Id)));

    /// <summary>uac-r2 191c: one answer from several that did not fault (disjoint batches of one pass, asked separately).</summary>
    internal static ExternalVisibilityResult Merge(IReadOnlyList<ExternalVisibilityResult> parts)
    {
        if (parts.Any(p => p.Faulted))
            throw new ArgumentException("Only answers that did not fault are merged.", nameof(parts));
        if (parts.Count == 1)
            return parts[0];

        var decided = new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>();
        var roots = new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();
        var secure = new HashSet<ExternalVisibility.Node>();
        foreach (var part in parts)
        {
            foreach (var (node, decision) in part._decided)
                decided[node] = decision;
            foreach (var (node, reached) in part.RootsReached)
                roots[node] = reached;
            secure.UnionWith(part.EffectivelySecureRoots);
        }

        return new ExternalVisibilityResult(decided, faulted: false) { RootsReached = roots, EffectivelySecureRoots = secure };
    }

    /// <summary>The not-visible count per reason code (one log line; codes and counts only).</summary>
    internal static string Summary(IEnumerable<ExternalVisibilityDecision> decisions) =>
        string.Join(",", decisions
            .Where(d => d.Verdict != ExternalVisibilityVerdict.Visible)
            .GroupBy(d => d.Reason)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()}"));
}
