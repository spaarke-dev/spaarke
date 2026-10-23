using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// Ontology platform R1 task 038 (D-15, FR-24, FR-54; D-34..D-36, D-42, D-44; D-129/D-137): the ONE way the worklist
// reads Signals. It runs the worklist configuration's FetchXML AS THE CALLER, drops every row whose core record the
// caller cannot read, keeps a Do-lane row only when its subject is the caller's own work, and orders each lane by D-42.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: SignalCoreRecordAccess (task 036) decides ONE Signal (a Signal-keyed route); RecordRouteAccessAuthorizationFilter
//     (uac-r2) authorizes one record id per route. Neither filters a LIST by each row's core record. EventOwnershipScope
//     (task 097) is "my events" as an OData clause; OwnedChildWrite.ForPersonColumns names the person column per table.
//   Extension: SignalCoreRecordAccess answers per id with two reads each (Signal + core); calling it per row is one query per
//     row, which the POML's escalation trigger forbids. This service applies the SAME rules (caller-identity reads, the
//     catalog-named core table, uac-r2's core taxonomy, no-core = Do lane + caller reads the subject) as ONE batched read per
//     core type per 100 ids, and reuses the two existing person mechanisms for "mine".
//   Cost of doing nothing: no route reads sprk_signal, and reading as the caller alone shows every Console User in the BU
//     tree the Signals (sentence and evidence) on matters they cannot open and every colleague's overdue work (#17).
// =====================================================================================================================

/// <summary>Which worklist set the route returns (task 050's row: the two lanes, or one of the three secondary sets).</summary>
public enum WorklistSet
{
    /// <summary>The Decide and Do lanes: open and acknowledged, not suppressed. Ordered by D-42.</summary>
    Lanes,

    /// <summary>Resolved today in each item's own time zone (D-25 as amended by D-129).</summary>
    ClosedToday,

    /// <summary>Open or acknowledged Signals whose suppression date is in the future.</summary>
    Suppressed,

    /// <summary>Resolved within the configured window but not today in the item's own time zone.</summary>
    ClosedEarlier,

    /// <summary>
    /// Task 045 (D-47): the Decision Record tab. Every Signal a person resolved through a Decision Record whose record the caller
    /// can read, within <see cref="SignalReadService.DecidedWindowDays"/> days, newest first on the client. Membership is the
    /// configuration's <c>closedEarlier</c> query with its window widened and a "has a Decision Record" condition; an item whose
    /// record the caller cannot read is absent.
    /// </summary>
    Decided,
}

/// <summary>What <see cref="SignalReadService.ReadAsync"/> decided.</summary>
public enum SignalReadOutcome
{
    /// <summary>The caller's visible Signals (possibly none).</summary>
    Ok,

    /// <summary>The caller could not be resolved to a Dataverse user: #1312's single 403 (D-29).</summary>
    CallerUnresolved,

    /// <summary>A fault (configuration, Dataverse, volume cap): 503. Never a partial list and never the empty list.</summary>
    Unavailable,
}

/// <summary>The route's answer.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Response">The body when <see cref="Outcome"/> is <see cref="SignalReadOutcome.Ok"/>.</param>
/// <param name="Reason">A short machine reason for an <see cref="SignalReadOutcome.Unavailable"/> answer (logged, not row data).</param>
public sealed record SignalReadResult(SignalReadOutcome Outcome, WorklistResponse? Response, string? Reason = null)
{
    public static readonly SignalReadResult CallerUnresolved = new(SignalReadOutcome.CallerUnresolved, null);

    public static SignalReadResult Unavailable(string reason) => new(SignalReadOutcome.Unavailable, null, reason);
}

/// <summary>The worklist body: always both lanes, each a list of core-record groups (task 051's <c>WorklistCore</c> cards).</summary>
public sealed record WorklistResponse(string Set, IReadOnlyList<WorklistLaneGroups> Lanes);

/// <summary>One lane's groups, in display order.</summary>
public sealed record WorklistLaneGroups(string Lane, IReadOnlyList<WorklistGroup> Groups);

/// <summary>A core record's items (D-34/D-36), or the caller's "Not filed" items (D-35) when <see cref="Core"/> is null.</summary>
public sealed record WorklistGroup(WorklistCoreRecord? Core, bool NotFiled, IReadOnlyList<WorklistItemRow> Items);

/// <summary>The catalog-driven identity of a core record (task 051's <c>WorklistCore</c>). No field is type-specific (D-36).</summary>
public sealed record WorklistCoreRecord(string RecordType, Guid RecordId, string? TypeLabel, string? Name, string? Number);

/// <summary>One Work Item: display columns only (D-23, task 051 #1513, task 052 #1536 F4).</summary>
/// <param name="EvidenceRefs">The stored witness lines (a JSON array, as <see cref="SignalWriter"/> writes them) after the read-time
/// witness rules (D-123, D-142): a line whose witness the caller cannot read, or whose witness is not in the Signal's own known secure
/// family, has no <c>text</c> or <c>source</c> and carries <c>"withheld": true</c>, so the UI can say the detail is withheld rather
/// than absent. A billing Duplicate whose earlier invoice the caller cannot read (D-176, task 157) also loses every
/// <c>duplicateOf.*</c> line, and every other line is withheld. Null when nothing is stored or the
/// stored value is not a JSON array.</param>
/// <param name="Sentence">The Signal's stored rule sentence (<c>sprk_sentence</c>), returned as stored. FR-48/D-23 bind what it may
/// assert when it is written: the rule's literal text, with its tokens filled from the subject's own tested fields
/// (<c>SignalWriter.RenderSentence</c>), never a witness value. It adds nothing beyond the caller's own read of the Signal row (the
/// column is not field-secured); a non-core subject the caller cannot read is DI-89 (#1913). Null when empty. Task 154 (#1959 item 1).
/// Exception (D-176, task 157): a billing Duplicate whose earlier invoice the caller cannot read carries
/// <see cref="EarlierInvoiceReadability.WithheldSentence"/> instead.</param>
/// <param name="SubjectRecordType">Do lane only (task 154, #1958): the subject's table (logical name), the table whose caller-scoped
/// "mine" read returned it (<see cref="SignalReadService.DoLaneMineRules"/>), so the caller has already been proven to read it. Null on
/// a Decide-lane row.</param>
/// <param name="SubjectRecordId">Do lane only: the subject's id, with <see cref="SubjectRecordType"/>. Null on a Decide-lane row.</param>
/// <param name="Decision">Closed today, Closed earlier and Decided sets only (task 045, #1959 item 2): the Decision Record that closed
/// the item, read AS THE CALLER. Null when the item was closed by the system (no record), or when the caller cannot read the record
/// (omitted, never withheld by name).</param>
public sealed record WorklistItemRow(
    Guid SignalId,
    string Lane,
    string? Status,
    string? Title,
    string? SubjectName,
    string? RuleShortName,
    string? WorkType,
    string PastDueWording,
    DateTimeOffset? RaisedOn,
    string? DueDate,
    string? EvidenceRefs,
    Guid? PolicyVersionId,
    DateTimeOffset? ResolvedOn = null,
    int? ResolutionType = null,
    DateTimeOffset? SuppressedUntil = null,
    string? Sentence = null,
    string? SubjectRecordType = null,
    Guid? SubjectRecordId = null,
    WorklistDecisionRecord? Decision = null);

/// <summary>
/// The Signal read route's service (task 038). Every row-visibility question is asked AS THE CALLER through
/// <see cref="IDataverseUserClient"/>; only configuration and metadata (the worklist row, the record-type catalog, entity-set
/// names, time zones) are read with the BFF's own <see cref="IGenericEntityService"/>, and nothing read that way is returned
/// unless the caller's own read admitted the row it belongs to.
/// </summary>
/// <remarks>
/// <para><b>Membership and base order</b> are the worklist <c>sprk_gridconfiguration</c> row's (task 050, id
/// <see cref="WorklistConfigurationId"/>), executed verbatim except for paging attributes. The route never re-implements the
/// predicate and never adds a user filter to it.</para>
/// <para><b>Visibility, per row, by the row's OWN lane</b> (a configuration that puts a Do row in the Decide query cannot widen
/// anything): (1) Dataverse trims the Signal read itself (the Signal's business unit and secure-child shares). (2) A row with a
/// complete core pair is kept only when the caller's own batched read of the catalog-named core table returns its
/// <c>sprk_corerecordid</c>; the table must be in uac-r2's <see cref="CoreAncestorResolver.CoreRecordEntities"/> (K2, as in
/// <see cref="SignalCoreRecordAccess"/>). (3) A half pair is dropped. (4) A row with no core record is Do-lane only (D-35).
/// (5) A Do-lane row is kept only when the caller's own read of its subject, filtered to "mine", returns it
/// (<see cref="DoLaneMineRules"/>); that read also proves the caller can read the subject (D-35 as amended by D-129). A dropped
/// row leaves no trace: no count, no placeholder, no group.</para>
/// <para><b>What a row carries (task 154).</b> Every returned value belongs to a row the caller's own reads admitted. The rule sentence
/// is a column of the Signal row the caller read. The subject reference (<see cref="WorklistItemRow.SubjectRecordType"/>,
/// <see cref="WorklistItemRow.SubjectRecordId"/>) is returned on Do-lane rows only, from the step (5) read that returned the subject to
/// the caller; a Decide-lane row has no such read and returns neither. Neither adds a request.</para>
/// <para><b>Group order.</b> The lanes follow D-42 and the secondary sets the configuration's order; in the Do lane the "Not filed"
/// group (D-35) is always emitted last (HANDOFF 1.2, task 154).</para>
/// <para><b>Fail closed, honestly.</b> A row the caller cannot read is dropped; a FAULT (a 5xx, throttling, an unparseable
/// configuration, more than <see cref="MaxPages"/> pages) is <see cref="SignalReadOutcome.Unavailable"/>, never a shorter list
/// presented as complete (uac-r2 F4-b: a fault is not a denial).</para>
/// <para><b>Bounded queries</b> (POML escalation trigger 2): per page of Signals, one caller read per core type per
/// <see cref="IdChunk"/> ids, one per Do-subject table per chunk, one per Decide-subject table per chunk, plus one
/// catalog query; and, only for returned rows whose evidence carries detail, one caller read per witness table per
/// <see cref="WitnessIdChunk"/> ids (D-142) and the app-only secure-family placement of those Signals and the witnesses the caller
/// reads (<see cref="SignalSuppressionService.WitnessDetailShownOnReadAsync"/>: it withholds, never adds); and, only for billing
/// Duplicate rows (D-176, task 157), at most one billing-event read, one current-invoice read, one submission read per chunk of 200
/// distinct tracking numbers and one earlier-invoice read per chunk of 200 invoice ids (<see cref="EarlierInvoiceReadability"/>: it withholds, never adds).
/// Nothing is cached across callers.</para>
/// </remarks>
public sealed class SignalReadService
{
    /// <summary>The worklist <c>sprk_gridconfiguration</c> row (task 050; live on spaarkedev1 per D-138).</summary>
    internal static readonly Guid WorklistConfigurationId = Guid.Parse("00000000-0000-4000-8000-000000005010");

    /// <summary>Pages of Signals read per set before the route refuses (fails loudly) rather than ranking a partial list.</summary>
    internal const int MaxPages = 5;

    /// <summary>How far back the <see cref="WorklistSet.Decided"/> set reaches (task 045). One number; the set is refused (503) past
    /// <see cref="MaxPages"/> pages rather than truncated.</summary>
    internal const int DecidedWindowDays = 90;

    /// <summary>The Decision Record table's entity set.</summary>
    internal const string DecisionRecordEntitySet = "sprk_decisionrecords";

    /// <summary>Ids per batched caller read (keeps the GET URL well under Dataverse's limit).</summary>
    internal const int IdChunk = 100;

    /// <summary>Witness ids per batched caller read (task 151, D-142: In lists of at most 200 ids).</summary>
    internal const int WitnessIdChunk = 200;

    internal const string SignalEntitySet = "sprk_signals";
    internal const string HighPriorityColumn = "sprk_highpriority";

    /// <summary><c>sprk_todo.sprk_notes</c>: it carries the inquiry hint of an inquiry-outcome To Do (task 071, task 154).</summary>
    internal const string TodoNotesColumn = "sprk_notes";

    /// <summary><c>sprk_todo</c>'s matter lookup as a Web API name: the inquiry-outcome To Do's matter fallback reads that matter's
    /// open inquiries (task 154, pass 2 F1).</summary>
    internal const string TodoMatterColumn = "_sprk_regardingmatter_value";
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string ConfigurationEntity = "sprk_gridconfiguration";
    private const string PolicyEntity = "sprk_policy";

    /// <summary>A Do-lane subject table's "mine" rule. Each part is OR-ed, each compared with the caller.</summary>
    /// <param name="Table">The subject table.</param>
    /// <param name="OwnerIsCaller">The row's <c>ownerid</c> is the caller (a user-owned personal record).</param>
    /// <param name="PersonColumn">The contact lookup naming who the record is FOR (<see cref="OwnedChildWrite.ForPersonColumns"/>),
    /// compared with the caller's linked contact.</param>
    /// <param name="CreatorIsCaller"><c>sprk_createdbyperson</c> is the caller.</param>
    internal sealed record DoLaneMineRule(string Table, bool OwnerIsCaller, string PersonColumn, bool CreatorIsCaller);

    /// <summary>
    /// "The caller's own work" per Do-lane subject (D-15 as amended by D-44; D-129, D-137). A subject table not listed has no
    /// "mine" and its Do rows are never returned.
    /// <list type="bullet">
    /// <item><c>sprk_event</c>: task 097 owner decision B, the same three parts as <see cref="EventOwnershipScope"/> (owner,
    /// assigned to the caller's contact via <c>sprk_assignedto</c> per D-137 Q22, or created by the caller).</item>
    /// <item><c>sprk_todo</c>: the POML's owner rule (a personal To Do stays user-owned, D-130) OR its Assigned To
    /// (<c>sprk_assignedto</c>; D-129: a team-owned To Do names its person there).</item>
    /// <item><c>sprk_workassignment</c>: D-44, both the assigner (<c>sprk_createdbyperson</c>) and the assignee, the Assigned To
    /// Internal contact (<c>sprk_assignedtointernal</c>, D-129; the column <see cref="OwnedChildWrite.ForPersonColumns"/> names).</item>
    /// </list>
    /// </summary>
    internal static readonly FrozenDictionary<string, DoLaneMineRule> DoLaneMineRules = new[]
    {
        new DoLaneMineRule("sprk_event", OwnerIsCaller: true, OwnedChildWrite.ForPersonColumns["sprk_event"].Column, CreatorIsCaller: true),
        new DoLaneMineRule("sprk_todo", OwnerIsCaller: true, OwnedChildWrite.ForPersonColumns["sprk_todo"].Column, CreatorIsCaller: false),
        new DoLaneMineRule("sprk_workassignment", OwnerIsCaller: false, OwnedChildWrite.ForPersonColumns["sprk_workassignment"].Column, CreatorIsCaller: true),
    }.ToFrozenDictionary(r => r.Table, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subject tables that carry <c>sprk_highpriority</c>, D-42's second key (spec Q1: the flag on the SUBJECT; the same seven
    /// tables the Daily Briefing reads it from). Pinned to the solution source by the route's tests. A subject table not listed
    /// ranks as not flagged.
    /// </summary>
    internal static readonly FrozenSet<string> HighPriorityTables = new[]
    {
        "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_event", "sprk_todo", "sprk_document", "sprk_invoice",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>sprk_policy.sprk_worktype</c> option → the camelCase key <c>WorklistItem.workType</c> expects (task 052 F4).</summary>
    internal static readonly FrozenDictionary<int, string> WorkTypeKeys = new Dictionary<int, string>
    {
        [100000000] = "askOutsideCounsel",
        [100000001] = "approveOrRebudget",
        [100000002] = "chaseReply",
        [100000003] = "finishOrReschedule",
        [100000004] = "comingDue",
        [100000005] = "chaseResponse",
    }.ToFrozenDictionary();

    /// <summary>The work type whose past-due wording is "late" (a work assignment's response, v4 W-3); every other is "overdue".</summary>
    internal const string LateWorkType = "chaseResponse";

    /// <summary><c>sprk_policy.sprk_severity</c> (D-30): Critical first.</summary>
    internal const int SeverityCritical = 100000002;
    internal const int SeverityWarning = 100000001;
    internal const int SeverityInfo = 100000000;

    private static readonly FrozenDictionary<int, string> StatusLabels = new Dictionary<int, string>
    {
        [100000000] = "Open",
        [100000001] = "Acknowledged",
        [100000002] = "Resolved",
    }.ToFrozenDictionary();

    private readonly IDataverseUserClient _user;
    private readonly IGenericEntityService _entities;
    private readonly IIdentityNormalizationService _identity;
    private readonly TimeProvider _clock;
    private readonly ILogger<SignalReadService> _logger;
    private readonly SignalSuppressionService _families;
    private readonly DecisionRecordWriter _records;
    private readonly EarlierInvoiceReadability _earlierInvoices;

    public SignalReadService(
        IDataverseUserClient user,
        IGenericEntityService entities,
        IIdentityNormalizationService identity,
        SignalSuppressionService families,
        DecisionRecordWriter records,
        EarlierInvoiceReadability earlierInvoices,
        TimeProvider clock,
        ILogger<SignalReadService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _earlierInvoices = earlierInvoices ?? throw new ArgumentNullException(nameof(earlierInvoices));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _families = families ?? throw new ArgumentNullException(nameof(families));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The caller's visible Signals in <paramref name="set"/>, grouped and ordered.</summary>
    public async Task<SignalReadResult> ReadAsync(WorklistSet set, CancellationToken ct)
    {
        // (1) + (2) Who the caller is and their linked contact: the inputs of the Do-lane "mine" rule.
        var caller = await ResolveMineCallerAsync(_user, _identity, _logger, ct).ConfigureAwait(false);
        if (caller.Failure is { } callerFailure)
        {
            return callerFailure;
        }

        var callerId = caller.CallerId;
        var contactId = caller.ContactId;

        try
        {
            // (3) The configuration (task 050's row): which FetchXML runs for this set.
            var fetches = await ReadFetchesAsync(set, ct).ConfigureAwait(false);
            if (fetches is null)
            {
                return SignalReadResult.Unavailable("configuration_invalid");
            }

            // (4) The Signals, AS THE CALLER. Dataverse trims by the Signal's own business unit and shares first.
            var rows = new List<SignalRow>();
            var seen = new HashSet<Guid>();
            foreach (var fetch in fetches)
            {
                var page = await FetchAllAsCallerAsync(fetch, ct).ConfigureAwait(false);
                if (page.Outcome is { } failure)
                {
                    return failure;
                }

                foreach (var raw in page.Rows)
                {
                    if (SignalRow.TryParse(raw, out var row) && seen.Add(row.SignalId))
                    {
                        rows.Add(row);
                    }
                }
            }

            if (rows.Count == 0)
            {
                return Ok(set, []);
            }

            // (5) The record-type catalog rows the page names (core and subject types), one app-only query.
            var catalog = await ReadCatalogAsync(rows, ct).ConfigureAwait(false);

            // (6) Core-record access: one caller read per core type per chunk. Unreadable, malformed or non-core: dropped.
            var cores = await ReadReadableCoresAsync(rows, catalog, ct).ConfigureAwait(false);
            if (cores.Outcome is { } coreFailure)
            {
                return coreFailure;
            }

            var candidates = new List<SignalRow>();
            foreach (var row in rows)
            {
                if (row.Lane is not { } lane)
                {
                    continue;
                }

                if (row.HasNoCore)
                {
                    // D-35: no core record → Do lane only (and then only if the subject is the caller's, step 7).
                    if (lane == DecisionLane.Do)
                    {
                        candidates.Add(row);
                    }

                    continue;
                }

                if (row.CoreTypeId is { } typeId && row.CoreId is { } coreId && cores.Readable.ContainsKey((typeId, coreId)))
                {
                    candidates.Add(row);
                }
            }

            // (7) Do lane: the caller's own work only (also proves the caller reads the subject). Also the subject flag and,
            // for the closed sets, the assignee whose day decides "today".
            var subjects = await ReadSubjectsAsync(candidates, catalog, callerId, contactId, ct).ConfigureAwait(false);
            if (subjects.Outcome is { } subjectFailure)
            {
                return subjectFailure;
            }

            var visible = new List<VisibleRow>();
            foreach (var row in candidates)
            {
                var key = (row.SubjectId ?? Guid.Empty);
                if (row.Lane == DecisionLane.Do)
                {
                    if (key == Guid.Empty || !subjects.MineDo.TryGetValue(key, out var mine))
                    {
                        continue;
                    }

                    visible.Add(new VisibleRow(row, mine.HighPriority, mine.AssigneeContact, mine.Table));
                }
                else
                {
                    // No subject reference on a Decide row: no caller read of its subject admitted it (task 154).
                    var decide = key != Guid.Empty && subjects.Decide.TryGetValue(key, out var found) ? found : null;
                    visible.Add(new VisibleRow(row, decide?.HighPriority == true, decide?.AssigneeContact, SubjectTable: null));
                }
            }

            // (8) Closed sets: each item in exactly one of today / earlier, by its own today (D-25 as amended by D-129).
            if (set is WorklistSet.ClosedToday or WorklistSet.ClosedEarlier)
            {
                visible = await PartitionClosedAsync(visible, set == WorklistSet.ClosedToday, ct).ConfigureAwait(false);
            }

            // (9) The witness rules at read time (D-123; task 145 review F1, option B; D-142, task 151): a stored line keeps its subject
            // and sender only when the caller's own read returns its witness AND the witness is in the Signal's own known secure family.
            // D-176 (task 157): a billing Duplicate's earlier-invoice references are taken from the STORED evidence first, because (9)
            // removes the text of every line whose ref is not a witness, which is every billing line.
            var duplicates = DuplicateQuotesOf(visible);
            visible = await ApplyWitnessRuleAsync(visible, ct).ConfigureAwait(false);

            // (9b) D-176 (task 157): a Duplicate keeps its earlier-invoice detail only when the caller's own read returns that invoice;
            // otherwise it is shaped as ebilling withholds it at write time. Runs after (9); both rules must keep.
            if (duplicates.Count > 0)
            {
                visible = await ApplyDuplicateRuleAsync(visible, duplicates, catalog, ct).ConfigureAwait(false);
            }

            // (10) Task 045 (#1959 item 2, D-47): the Decision Record of each closed item, read AS THE CALLER. A record the caller
            // cannot read is omitted; in the Decided set an item without a readable record is not listed at all.
            var decisions = new Dictionary<Guid, WorklistDecisionRecord>();
            if (set is WorklistSet.ClosedToday or WorklistSet.ClosedEarlier or WorklistSet.Decided)
            {
                var records = await ReadDecisionRecordsAsync(visible.Select(v => v.Row.DecisionRecordId), ct).ConfigureAwait(false);
                if (records.Outcome is { } recordFailure)
                {
                    return recordFailure;
                }

                foreach (var v in visible)
                {
                    if (v.Row.DecisionRecordId is { } recordId && records.ByRecord.TryGetValue(recordId, out var found))
                    {
                        decisions[v.Row.SignalId] = found;
                    }
                }

                if (set == WorklistSet.Decided)
                {
                    visible = [.. visible.Where(v => decisions.ContainsKey(v.Row.SignalId))];
                }
            }

            // (11) D-185 (task 163, #2285): the policy short name, work type and severity of the rows that are returned, read as the
            // BFF. A read fault is caught below as "fault" (503): severity ranks the list, so rows are never returned without it.
            visible = await ApplyPolicyColumnsAsync(visible, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Signal read: set {Set}, {Read} Signals read as the caller, {Visible} returned.", set, rows.Count, visible.Count);

            return Ok(set, Shape(set, visible, cores.Readable, catalog, decisions));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Type only, never the message (it can echo row content).
            _logger.LogError("Signal read: faulted ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalReadResult.Unavailable("fault");
        }
    }

    private static SignalReadResult Ok(WorklistSet set, IReadOnlyList<WorklistLaneGroups> lanes) =>
        new(SignalReadOutcome.Ok, new WorklistResponse(SetName(set), lanes.Count == 0 ? EmptyLanes() : lanes));

    internal static string SetName(WorklistSet set) => set switch
    {
        WorklistSet.Lanes => "lanes",
        WorklistSet.ClosedToday => "closedToday",
        WorklistSet.Suppressed => "suppressed",
        WorklistSet.ClosedEarlier => "closedEarlier",
        WorklistSet.Decided => "decided",
        _ => throw new ArgumentOutOfRangeException(nameof(set)),
    };

    private static IReadOnlyList<WorklistLaneGroups> EmptyLanes() =>
        [new WorklistLaneGroups(LaneName(DecisionLane.Decide), []), new WorklistLaneGroups(LaneName(DecisionLane.Do), [])];

    private static string LaneName(DecisionLane lane) => lane == DecisionLane.Decide ? "Decide" : "Do";

    // ---------------------------------------------------------------------------------------------------------------
    // (3) configuration
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The FetchXML(s) the set runs, or null when the row is missing, inactive or not a Signal worklist.</summary>
    private async Task<IReadOnlyList<string>?> ReadFetchesAsync(WorklistSet set, CancellationToken ct)
    {
        var query = new QueryExpression(ConfigurationEntity) { ColumnSet = new ColumnSet("sprk_configjson", "statecode"), TopCount = 1 };
        query.Criteria.AddCondition($"{ConfigurationEntity}id", ConditionOperator.Equal, WorklistConfigurationId);
        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var row = found.Entities.FirstOrDefault();
        if (row is null || row.GetAttributeValue<OptionSetValue>("statecode") is { Value: not 0 })
        {
            _logger.LogError("Signal read: the worklist configuration row {RowId} is missing or inactive.", WorklistConfigurationId);
            return null;
        }

        var fetches = ParseFetches(row.GetAttributeValue<string>("sprk_configjson"), set);
        if (fetches is null)
        {
            _logger.LogError("Signal read: the worklist configuration row {RowId} is not a valid sprk_signal worklist for set {Set}.",
                WorklistConfigurationId, set);
        }

        return fetches;
    }

    /// <summary>Parses task 050's definition. Every FetchXML must be a <c>sprk_signal</c> query; anything else is null.</summary>
    internal static IReadOnlyList<string>? ParseFetches(string? configJson, WorklistSet set)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (ReadString(root, "kind") != "worklist" || ReadString(root, "entity") != "sprk_signal")
            {
                return null;
            }

            (string Section, string Key)[] paths = set switch
            {
                WorklistSet.Lanes => [("lanes", "decide"), ("lanes", "do")],
                WorklistSet.ClosedToday => [("secondary", "closedToday")],
                WorklistSet.Suppressed => [("secondary", "suppressed")],
                WorklistSet.ClosedEarlier => [("secondary", "closedEarlier")],
                // Task 045: the Decision Record tab derives from closedEarlier (same columns, same order), see DecidedFetch.
                WorklistSet.Decided => [("secondary", "closedEarlier")],
                _ => [],
            };

            var fetches = new List<string?>();
            foreach (var (section, key) in paths)
            {
                if (!root.TryGetProperty(section, out var s) || !s.TryGetProperty(key, out var entry)
                    || ReadString(entry, "fetchXml") is not { Length: > 0 } fetch)
                {
                    return null;
                }

                var entity = XDocument.Parse(fetch).Root?.Element("entity");
                if (entity?.Attribute("name")?.Value != "sprk_signal")
                {
                    return null;
                }

                fetches.Add(set switch
                {
                    WorklistSet.ClosedToday or WorklistSet.ClosedEarlier => WithDecisionRecordColumn(fetch),
                    WorklistSet.Decided => DecidedFetch(fetch),
                    _ => fetch,
                });
            }

            return fetches.Count == 0 || fetches.Any(f => f is null) ? null : fetches.Select(f => f!).ToList();
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The query with <c>sprk_decisionrecord</c> selected (a no-op when the configuration already selects it), so a closed
    /// item can be joined to its record whatever version of the configuration row is live (task 045).</summary>
    private static string WithDecisionRecordColumn(string fetchXml)
    {
        var doc = XDocument.Parse(fetchXml);
        var entity = doc.Root!.Element("entity")!;
        if (!entity.Elements("attribute").Any(a => a.Attribute("name")?.Value == "sprk_decisionrecord"))
        {
            var last = entity.Elements("attribute").LastOrDefault();
            var column = new XElement("attribute", new XAttribute("name", "sprk_decisionrecord"));
            if (last is null) entity.AddFirst(column); else last.AddAfterSelf(column);
        }

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The <see cref="WorklistSet.Decided"/> query: the configuration's closedEarlier query with its resolved-on window replaced by
    /// <see cref="DecidedWindowDays"/> days and a "has a Decision Record" condition added. Null when the query has no resolved-on
    /// window to replace (a configuration this build does not understand fails closed, 503, rather than reading everything).
    /// </summary>
    private static string? DecidedFetch(string closedEarlierFetch)
    {
        var doc = XDocument.Parse(WithDecisionRecordColumn(closedEarlierFetch));
        var filter = doc.Root!.Element("entity")!.Elements("filter").FirstOrDefault();
        var window = filter?.Elements("condition").FirstOrDefault(c => c.Attribute("attribute")?.Value == "sprk_resolvedon");
        var status = filter?.Elements("condition").FirstOrDefault(c => c.Attribute("attribute")?.Value == "sprk_signalstatus");
        if (filter is null || window is null || status is null)
        {
            return null;
        }

        // A review that leaves its Signal open (inquiry sent, reminder, follow-on only) writes a Decision Record and leaves the Signal
        // Acknowledged: those records belong in the history too, so Acknowledged joins Resolved here (closedToday / closedEarlier unchanged).
        status.SetAttributeValue("operator", "in");
        status.SetAttributeValue("value", null);
        status.Add(new XElement("value", "100000001"), new XElement("value", "100000002"));

        window.SetAttributeValue("operator", "last-x-days");
        window.SetAttributeValue("value", DecidedWindowDays.ToString(CultureInfo.InvariantCulture));
        filter.Add(new XElement("condition", new XAttribute("attribute", "sprk_decisionrecord"), new XAttribute("operator", "not-null")));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (4) the Signals, as the caller, paged
    // ---------------------------------------------------------------------------------------------------------------

    private sealed record FetchPages(List<JsonElement> Rows, SignalReadResult? Outcome);

    private async Task<FetchPages> FetchAllAsCallerAsync(string fetchXml, CancellationToken ct)
    {
        var fetch = XDocument.Parse(fetchXml);
        var root = fetch.Root!;
        var rows = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
            {
                _logger.LogError("Signal read: more than {MaxPages} pages of Signals for one caller; refusing rather than ranking a partial list.", MaxPages);
                return new FetchPages(rows, SignalReadResult.Unavailable("too_many_signals"));
            }

            var response = await _user.GetAsync(
                $"{SignalEntitySet}?fetchXml={Uri.EscapeDataString(fetch.ToString(SaveOptions.DisableFormatting))}", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                if (IsCallerUnresolved(response.ErrorCode))
                {
                    return new FetchPages(rows, SignalReadResult.CallerUnresolved);
                }

                if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && page > 1)
                {
                    // Page 1 was readable, so this is not "no Read on sprk_signal": never rank the pages already read as the list.
                    _logger.LogWarning("Signal read: page {Page} of the Signal query was refused after page 1 was read; unavailable.", page);
                    return new FetchPages(rows, SignalReadResult.Unavailable("signal_query_failed"));
                }

                if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
                {
                    // No Read on sprk_signal at all: the caller reads nothing, which is an empty list, not an error (FR-54).
                    // Warning (task 149, uac-r2 K3 on #1891): an empty worklist from a refused read looks exactly like "no work", so the
                    // refusal must be visible at the level an operator filters on. No behaviour change.
                    _logger.LogWarning("Signal read: the caller has no read on sprk_signal; nothing to return.");
                    return new FetchPages([], null);
                }

                _logger.LogWarning("Signal read: the Signal query failed ({ErrorCode}); unavailable.", response.ErrorCode);
                return new FetchPages(rows, SignalReadResult.Unavailable("signal_query_failed"));
            }

            if (response.Body is not { } body || !body.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return new FetchPages(rows, SignalReadResult.Unavailable("signal_query_unparseable"));
            }

            rows.AddRange(value.EnumerateArray().Select(e => e.Clone()));

            if (!HasMoreRecords(body))
            {
                return new FetchPages(rows, null);
            }

            var cookie = PagingCookieOf(body);
            if (cookie is null)
            {
                return new FetchPages(rows, SignalReadResult.Unavailable("paging_cookie_missing"));
            }

            root.SetAttributeValue("page", (page + 1).ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("paging-cookie", cookie);
        }
    }

    /// <summary>
    /// Whether another page exists. The caller-identity client sends no <c>Prefer: odata.include-annotations</c>, and then
    /// Dataverse omits <c>@Microsoft.Dynamics.CRM.morerecords</c> and returns <c>@Microsoft.Dynamics.CRM.fetchxmlpagingcookie</c>
    /// only when more rows exist (verified live on spaarkedev1, 2026-10-14). An explicit <c>morerecords</c> wins when present.
    /// </summary>
    internal static bool HasMoreRecords(JsonElement body)
    {
        if (body.TryGetProperty("@Microsoft.Dynamics.CRM.morerecords", out var more))
        {
            return more.ValueKind == JsonValueKind.True;
        }

        return body.TryGetProperty("@Microsoft.Dynamics.CRM.fetchxmlpagingcookie", out var cookie) && cookie.ValueKind == JsonValueKind.String;
    }

    /// <summary>
    /// The paging cookie for the next page: the Web API returns <c>&lt;cookie pagingcookie="…" /&gt;</c> whose attribute is
    /// URL-encoded twice; FetchXML's <c>paging-cookie</c> takes the decoded XML.
    /// </summary>
    internal static string? PagingCookieOf(JsonElement body)
    {
        if (!body.TryGetProperty("@Microsoft.Dynamics.CRM.fetchxmlpagingcookie", out var c) || c.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            var encoded = XElement.Parse(c.GetString()!).Attribute("pagingcookie")?.Value;
            return string.IsNullOrEmpty(encoded) ? null : Uri.UnescapeDataString(Uri.UnescapeDataString(encoded));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (11) policy columns, as the BFF (D-185)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The three <c>sprk_policy</c> columns the worklist shows and ranks by.</summary>
    private sealed record PolicyColumns(string? ShortName, int? WorkType, int? Severity);

    /// <summary>
    /// D-185 (task 163, #2285): the rule short name, work type and severity come from ONE batched BFF read of <c>sprk_policy</c> by id
    /// (<see cref="IdChunk"/> ids per query), not from a caller-run join, so a user needs no Read on <c>sprk_policy</c>. This is
    /// configuration, not matter data, so the read is the BFF's own (as <c>DecisionPlanService</c>). Only the rows that are already
    /// visible to the caller are enriched, and the read reveals nothing beyond the three columns the worklist already shows. It runs
    /// whether or not the configuration still carries the old <c>policy</c> link, so the BFF and the configuration deploy in either order.
    /// A row with no policy id, or whose policy no longer exists, keeps what its Signal row carried (today's missing-alias behaviour:
    /// no short name or work type, severity unknown, ranked last). A faulted read throws to the caller's catch (unavailable).
    /// </summary>
    private async Task<List<VisibleRow>> ApplyPolicyColumnsAsync(List<VisibleRow> visible, CancellationToken ct)
    {
        var ids = visible.Select(v => v.Row.PolicyId).Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return visible;
        }

        var found = new Dictionary<Guid, PolicyColumns>();
        foreach (var chunk in ids.Chunk(IdChunk))
        {
            var query = new QueryExpression(PolicyEntity) { ColumnSet = new ColumnSet("sprk_shortname", "sprk_worktype", "sprk_severity") };
            query.Criteria.AddCondition("sprk_policyid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var policy in result.Entities.Where(e => chunk.Contains(e.Id)))
            {
                found[policy.Id] = new PolicyColumns(
                    policy.GetAttributeValue<string>("sprk_shortname"),
                    policy.GetAttributeValue<OptionSetValue>("sprk_worktype")?.Value,
                    policy.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value);
            }
        }

        return [.. visible.Select(v => v.Row.PolicyId is { } id && found.TryGetValue(id, out var p)
            ? v with { Row = v.Row with { RuleShortName = p.ShortName, WorkType = p.WorkType, Severity = p.Severity } }
            : v)];
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (5) catalog
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>One <c>sprk_recordtype_ref</c> row: the table it names and the field names its catalog entry declares.</summary>
    internal sealed record CatalogRow(string LogicalName, string? Label, string? DisplayField, string? NumberField);

    private async Task<Dictionary<Guid, CatalogRow>> ReadCatalogAsync(IReadOnlyList<SignalRow> rows, CancellationToken ct)
    {
        var ids = rows.SelectMany(r => new[] { r.CoreTypeId, r.SubjectTypeId })
            .Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        var catalog = new Dictionary<Guid, CatalogRow>();
        if (ids.Length == 0)
        {
            return catalog;
        }

        var query = new QueryExpression(RecordTypeRefEntity)
        {
            ColumnSet = new ColumnSet("sprk_recordlogicalname", "sprk_recorddisplayname", "sprk_recorddisplaynamefield", "sprk_regardingrecordnumberfield"),
        };
        query.Criteria.AddCondition("sprk_recordtype_refid", ConditionOperator.In, ids.Cast<object>().ToArray());
        var found = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        foreach (var entity in found.Entities)
        {
            var logical = entity.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(logical) || !IsLogicalName(logical))
            {
                continue;
            }

            catalog[entity.Id] = new CatalogRow(
                logical,
                entity.GetAttributeValue<string>("sprk_recorddisplayname"),
                Field(entity, "sprk_recorddisplaynamefield"),
                Field(entity, "sprk_regardingrecordnumberfield"));
        }

        return catalog;

        static string? Field(Entity row, string column) =>
            row.GetAttributeValue<string>(column)?.Trim() is { Length: > 0 } f && IsLogicalName(f) ? f : null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (6) core-record access, batched, as the caller
    // ---------------------------------------------------------------------------------------------------------------

    private sealed record CoreAccess(Dictionary<(Guid TypeId, Guid CoreId), (string? Name, string? Number)> Readable, SignalReadResult? Outcome);

    private async Task<CoreAccess> ReadReadableCoresAsync(
        IReadOnlyList<SignalRow> rows, IReadOnlyDictionary<Guid, CatalogRow> catalog, CancellationToken ct)
    {
        var readable = new Dictionary<(Guid, Guid), (string?, string?)>();
        foreach (var byType in rows.Where(r => r.CoreTypeId is not null && r.CoreId is not null).GroupBy(r => r.CoreTypeId!.Value))
        {
            if (!catalog.TryGetValue(byType.Key, out var type)
                || !CoreAncestorResolver.CoreRecordEntities.Contains(type.LogicalName, StringComparer.OrdinalIgnoreCase))
            {
                // K2 (as SignalCoreRecordAccess): a type that does not resolve, or is not one of uac-r2's core types, is a forged or
                // stale pointer. The rows are dropped; the route is never a probe of an arbitrary table.
                _logger.LogWarning("Signal read: {Count} Signals name a core record type that is not an access-control core type; dropped.",
                    byType.Count());
                continue;
            }

            var entitySet = await _entities.GetEntitySetNameAsync(type.LogicalName, ct).ConfigureAwait(false);
            var pk = $"{type.LogicalName}id";
            var select = new List<string> { pk };
            if (type.DisplayField is { } d && d != pk) select.Add(d);
            if (type.NumberField is { } n && n != pk && !select.Contains(n)) select.Add(n);

            foreach (var chunk in byType.Select(r => r.CoreId!.Value).Distinct().Chunk(IdChunk))
            {
                var response = await _user.GetAsync(
                    $"{entitySet}?$select={string.Join(',', select)}&$filter={InFilter(pk, chunk)}", ct).ConfigureAwait(false);
                var result = ReadAccessRows(response, "core record");
                if (result.Outcome is { } failure)
                {
                    return new CoreAccess(readable, failure);
                }

                foreach (var core in result.Rows)
                {
                    if (TryReadGuid(core, pk, out var id) && chunk.Contains(id))
                    {
                        readable[(byType.Key, id)] = (
                            type.DisplayField is { } df ? ReadDisplay(core, df) : null,
                            type.NumberField is { } nf ? ReadDisplay(core, nf) : null);
                    }
                }
            }
        }

        return new CoreAccess(readable, null);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (7) subjects: Do-lane "mine", the D-42 flag, the closed sets' assignee
    // ---------------------------------------------------------------------------------------------------------------

    /// <param name="Table">The table whose caller read returned the subject (task 154: the Do-lane row's subject reference).</param>
    private sealed record MineSubject(bool HighPriority, Guid? AssigneeContact, string Table);

    private sealed record SubjectReads(Dictionary<Guid, MineSubject> MineDo, Dictionary<Guid, MineSubject> Decide, SignalReadResult? Outcome);

    private async Task<SubjectReads> ReadSubjectsAsync(
        IReadOnlyList<SignalRow> rows, IReadOnlyDictionary<Guid, CatalogRow> catalog, Guid callerId, Guid? contactId, CancellationToken ct)
    {
        var mine = new Dictionary<Guid, MineSubject>();
        var decide = new Dictionary<Guid, MineSubject>();

        // Do lane: per rule table, the subject ids typed to it, plus every untyped id (a GUID matches in its own table only).
        var doRows = rows.Where(r => r.Lane == DecisionLane.Do && r.SubjectId is not null).ToList();
        foreach (var rule in DoLaneMineRules.Values)
        {
            var ids = doRows
                .Where(r => SubjectTable(r, catalog) is not { } t || string.Equals(t, rule.Table, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.SubjectId!.Value).Distinct().ToArray();
            if (ids.Length == 0)
            {
                continue;
            }

            var entitySet = await _entities.GetEntitySetNameAsync(rule.Table, ct).ConfigureAwait(false);
            var pk = $"{rule.Table}id";
            foreach (var chunk in ids.Chunk(IdChunk))
            {
                var response = await _user.GetAsync(
                    MineSubjectQuery(rule, entitySet, chunk, callerId, contactId), ct).ConfigureAwait(false);
                var result = ReadAccessRows(response, "Do-lane subject");
                if (result.Outcome is { } failure)
                {
                    return new SubjectReads(mine, decide, failure);
                }

                foreach (var subject in result.Rows)
                {
                    if (TryReadGuid(subject, pk, out var id) && chunk.Contains(id))
                    {
                        mine[id] = new MineSubject(
                            subject.TryGetProperty(HighPriorityColumn, out var hp) && hp.ValueKind == JsonValueKind.True,
                            TryReadGuid(subject, $"_{rule.PersonColumn}_value", out var person) ? person : null,
                            rule.Table);
                    }
                }
            }
        }

        // Decide lane: the flag and, for the closed sets' per-item day (D-25 as amended by D-129), the subject's individual (its
        // Assigned To contact, OwnedChildWrite.ForPersonColumns). A subject the caller cannot read is simply not flagged and has no
        // assignee (its day is UTC); the row stays (D-15 checks the core).
        foreach (var byTable in rows.Where(r => r.Lane == DecisionLane.Decide && r.SubjectId is not null)
                     .GroupBy(r => SubjectTable(r, catalog))
                     .Where(g => g.Key is { } t && HighPriorityTables.Contains(t)))
        {
            var table = byTable.Key!;
            var entitySet = await _entities.GetEntitySetNameAsync(table, ct).ConfigureAwait(false);
            var pk = $"{table}id";
            var person = OwnedChildWrite.ForPersonColumns.TryGetValue(table, out var column)
                && string.Equals(column.RelatedTable, "contact", StringComparison.Ordinal) ? column.Column : null;
            var select = $"{pk},{HighPriorityColumn}" + (person is null ? string.Empty : $",_{person}_value");
            foreach (var chunk in byTable.Select(r => r.SubjectId!.Value).Distinct().Chunk(IdChunk))
            {
                var response = await _user.GetAsync(
                    $"{entitySet}?$select={select}&$filter={InFilter(pk, chunk)}", ct).ConfigureAwait(false);
                var result = ReadAccessRows(response, "Decide-lane subject");
                if (result.Outcome is { } failure)
                {
                    return new SubjectReads(mine, decide, failure);
                }

                foreach (var subject in result.Rows)
                {
                    if (TryReadGuid(subject, pk, out var id) && chunk.Contains(id))
                    {
                        decide[id] = new MineSubject(
                            subject.TryGetProperty(HighPriorityColumn, out var hp) && hp.ValueKind == JsonValueKind.True,
                            person is not null && TryReadGuid(subject, $"_{person}_value", out var assignee) ? assignee : null,
                            table);
                    }
                }
            }
        }

        return new SubjectReads(mine, decide, null);
    }

    /// <summary>The subject's table from its <c>sprk_regardingrecordtype</c> catalog row; null when untyped or not cataloged.</summary>
    private static string? SubjectTable(SignalRow row, IReadOnlyDictionary<Guid, CatalogRow> catalog) =>
        row.SubjectTypeId is { } t && catalog.TryGetValue(t, out var type) ? type.LogicalName : null;

    /// <summary>The caller and their linked contact, as the Do-lane "mine" rule compares them; or why they could not be resolved.</summary>
    /// <param name="Failure">Null when resolved; otherwise the single 403 (<see cref="SignalReadOutcome.CallerUnresolved"/>) or a
    /// 503 (<see cref="SignalReadOutcome.Unavailable"/>).</param>
    /// <param name="CallerId">The caller's <c>systemuserid</c>; set only when resolved.</param>
    /// <param name="ContactId">The caller's linked contact, or null when they have none (never null because a read faulted).</param>
    internal sealed record MineCaller(SignalReadResult? Failure, Guid CallerId, Guid? ContactId);

    /// <summary>
    /// The inputs of the Do-lane "mine" rule, read AS THE CALLER: who they are (<c>WhoAmI</c>) and their linked contact (the person
    /// columns are contact lookups). Shared by the worklist and the per-Signal gate (<see cref="SignalCoreRecordAccess"/>, task 150,
    /// D-141) so both resolve "the caller" the same way. An unresolvable caller is the single 403; a fault reading their identity or
    /// contact is 503 (under D-129 records are team-owned, so most of the caller's own work matches only on an assignee column: a
    /// contact that cannot be READ, as opposed to a user with no linked contact, never reads as "no contact").
    /// </summary>
    internal static async Task<MineCaller> ResolveMineCallerAsync(
        IDataverseUserClient user, IIdentityNormalizationService identity, ILogger logger, CancellationToken ct)
    {
        var who = await user.GetAsync("WhoAmI()", ct).ConfigureAwait(false);
        if (!who.IsSuccess || !TryReadGuid(who.Body, "UserId", out var callerId))
        {
            if (who.IsSuccess || IsCallerUnresolved(who.ErrorCode) || who.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
            {
                logger.LogWarning("Do-lane mine: the caller could not be resolved ({ErrorCode}); single 403.", who.ErrorCode ?? "no-user-id");
                return new MineCaller(SignalReadResult.CallerUnresolved, Guid.Empty, null);
            }

            logger.LogWarning("Do-lane mine: the caller's identity could not be checked ({ErrorCode}); unavailable.", who.ErrorCode);
            return new MineCaller(SignalReadResult.Unavailable("caller_identity_fault"), Guid.Empty, null);
        }

        try
        {
            var person = await identity.ResolveAsync(callerId, ct).ConfigureAwait(false);
            if (person.ContactUnreadable)
            {
                logger.LogWarning("Do-lane mine: the caller's linked contact could not be read; unavailable.");
                return new MineCaller(SignalReadResult.Unavailable("caller_contact_fault"), Guid.Empty, null);
            }

            return new MineCaller(null, callerId, person.ContactId is { } c && c != Guid.Empty ? c : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Do-lane mine: the caller's linked contact could not be resolved ({ExceptionType}); unavailable.",
                ex.GetType().Name);
            return new MineCaller(SignalReadResult.Unavailable("caller_contact_fault"), Guid.Empty, null);
        }
    }

    /// <summary>
    /// THE Do-lane "mine" read (task 150, D-141): the caller's own read of <paramref name="ids"/> in <paramref name="rule"/>'s table,
    /// filtered to the rows <see cref="MineFilter"/> admits. A row comes back only when the caller can read it AND it is their own
    /// work. The worklist (a chunk of ids) and the per-Signal gate (<see cref="SignalCoreRecordAccess"/>, one id) both send this
    /// request; neither builds the predicate itself.
    /// </summary>
    /// <param name="includeTodoNotes">The per-Signal gate's single-id read only (task 154, D-111): a To Do's notes (the inquiry hint)
    /// and its matter (the fallback, pass 2 F1), so the plan route identifies an inquiry-outcome To Do exactly as the commit route does
    /// with no To Do request of its own. Columns, never a predicate. The worklist's chunked read leaves them out (adversarial pass 1
    /// K3: an unused ntext column on up to 100 rows).</param>
    internal static string MineSubjectQuery(
        DoLaneMineRule rule, string entitySet, IEnumerable<Guid> ids, Guid callerId, Guid? contactId, bool includeTodoNotes = false)
    {
        var pk = $"{rule.Table}id";
        var select = $"{pk},_{rule.PersonColumn}_value" + (HighPriorityTables.Contains(rule.Table) ? $",{HighPriorityColumn}" : string.Empty)
            + (includeTodoNotes && string.Equals(rule.Table, "sprk_todo", StringComparison.OrdinalIgnoreCase) ? $",{TodoNotesColumn},{TodoMatterColumn}" : string.Empty);
        return $"{entitySet}?$select={select}&$filter={InFilter(pk, ids)} and ({MineFilter(rule, callerId, contactId)})";
    }

    /// <summary>The OData "mine" clause: the rule's parts OR-ed, each compared with the caller (never widened, only narrowed).</summary>
    internal static string MineFilter(DoLaneMineRule rule, Guid callerId, Guid? contactId)
    {
        var parts = new List<string>();
        if (rule.OwnerIsCaller)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "_ownerid_value eq {0:D}", callerId));
        }

        if (contactId is { } contact && contact != Guid.Empty)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "_{0}_value eq {1:D}", rule.PersonColumn, contact));
        }

        if (rule.CreatorIsCaller)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "_{0}_value eq {1:D}", RecordCreatorPersonColumn.LogicalName, callerId));
        }

        // Every rule has an owner or creator part, so the clause is never empty (an empty clause would mean "everything").
        return parts.Count > 0
            ? string.Join(" or ", parts)
            : throw new InvalidOperationException($"The Do-lane rule for {rule.Table} has no part that names the caller.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (8) closed sets: today per item
    // ---------------------------------------------------------------------------------------------------------------

    private async Task<List<VisibleRow>> PartitionClosedAsync(List<VisibleRow> rows, bool wantToday, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var days = new DataverseRecipientDays(_entities, now);
        var zones = new Dictionary<Guid, TimeZoneInfo?>();
        var kept = new List<VisibleRow>();
        foreach (var row in rows)
        {
            if (row.Row.ResolvedOn is not { } resolvedOn)
            {
                continue;
            }

            // D-25 as amended by D-129: the assignee's linked user's zone, else UTC; never the owner (records are owned by teams).
            var recipient = await days.ForAsync(row.AssigneeContact, owner: null, ct).ConfigureAwait(false);
            if (recipient.Error is { } dayError)
            {
                _logger.LogWarning("Signal read: the assignee's day could not be read ({Reason}, {ExceptionType}); the UTC date is used for this item.",
                    recipient.FallbackReason, dayError.GetType().Name);
            }
            TimeZoneInfo? zone = null;
            if (recipient.Source == RecipientDaySource.Assignee && recipient.SystemUserId is { } user)
            {
                if (!zones.TryGetValue(user, out zone))
                {
                    zone = (await DataverseUserTimeZone.UserDayForUserAsync(_entities, user, now, ct).ConfigureAwait(false)).Zone;
                    zones[user] = zone;
                }
            }

            var today = zone is null ? DataverseUserTimeZone.LocalDate(now, null) : recipient.Today;
            var resolvedToday = DataverseUserTimeZone.LocalDate(resolvedOn, zone) == today;
            if (resolvedToday == wantToday)
            {
                kept.Add(row);
            }
        }

        return kept;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (10) the Decision Records of the closed items, as the caller (task 045)
    // ---------------------------------------------------------------------------------------------------------------

    private sealed record DecisionRecordReads(Dictionary<Guid, WorklistDecisionRecord> ByRecord, SignalReadResult? Outcome);

    /// <summary>
    /// One caller read per <see cref="IdChunk"/> record ids (a FetchXML <c>in</c>, with an outer link to the confirming user's name).
    /// Dataverse trims the answer by the caller's own security: a secure child reaches only its family's members through the mirrored
    /// shares (D-33/D-123), an owner-only record (D-35/D-39) only its owner. A service identity is never used. No Read on the table at
    /// all is an empty answer (the records are omitted); any other failure is a fault (503), never a shorter list.
    /// </summary>
    private async Task<DecisionRecordReads> ReadDecisionRecordsAsync(IEnumerable<Guid?> recordIds, CancellationToken ct)
    {
        var found = new Dictionary<Guid, WorklistDecisionRecord>();
        var ids = recordIds.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new DecisionRecordReads(found, null);
        }

        // Only a record the ontology writer itself created is a Decision Record here (the condition DecisionCommitService.FindRecordAsync
        // uses): Console User can Create a row and Write a Signal's lookup, and no field is secured, so any other row could be a forgery
        // ("Judgement, decided by <partner>"). Such a row is treated like an unreadable one. A WhoAmI fault throws to ReadAsync's catch (503).
        var writerUserId = await _records.WriterUserIdAsync(ct).ConfigureAwait(false);
        foreach (var chunk in ids.Chunk(IdChunk))
        {
            var fetch = new XElement("fetch",
                new XElement("entity", new XAttribute("name", "sprk_decisionrecord"),
                    WorklistDecisionRecordReader.Columns.Select(c => new XElement("attribute", new XAttribute("name", c))),
                    new XElement("link-entity", new XAttribute("name", "systemuser"), new XAttribute("from", "systemuserid"),
                        new XAttribute("to", "sprk_confirmedby"), new XAttribute("link-type", "outer"), new XAttribute("alias", "by"),
                        new XElement("attribute", new XAttribute("name", "fullname"))),
                    new XElement("filter", new XAttribute("type", "and"),
                        new XElement("condition", new XAttribute("attribute", "sprk_decisionrecordid"), new XAttribute("operator", "in"),
                            chunk.Select(id => new XElement("value", id.ToString("D")))),
                        new XElement("condition", new XAttribute("attribute", DecisionRecordWriter.CreatedByColumn), new XAttribute("operator", "eq"),
                            new XAttribute("value", writerUserId.ToString("D"))))));

            var response = await _user.GetAsync(
                $"{DecisionRecordEntitySet}?fetchXml={Uri.EscapeDataString(fetch.ToString(SaveOptions.DisableFormatting))}", ct).ConfigureAwait(false);
            if (!response.IsSuccess && response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
            {
                if (response.StatusCode == 401)
                {
                    // The same mapped code, but the caller's token failed mid-request: a fault, not a verdict (as the witness read).
                    _logger.LogWarning("Signal read: the Decision Record read was answered 401; unavailable.");
                    return new DecisionRecordReads(found, SignalReadResult.Unavailable("decision record read unauthorized"));
                }

                // A 403 is "no Read on the table (or on the confirming user the query links to)": the records are omitted. Warning, not Debug:
                // an empty tab from a refused read looks exactly like "no decisions".
                _logger.LogWarning("Signal read: the caller's Decision Record read was refused (403); the records are omitted.");
                return new DecisionRecordReads(found, null);
            }

            var result = ReadAccessRows(response, "decision record");
            if (result.Outcome is { } failure)
            {
                return new DecisionRecordReads(found, failure);
            }

            foreach (var raw in result.Rows)
            {
                if (WorklistDecisionRecordReader.TryParse(raw, out var record) && chunk.Contains(record.RecordId))
                {
                    found[record.RecordId] = record;
                }
            }
        }

        return new DecisionRecordReads(found, null);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (9) the witness rule at read time (D-123)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Each returned row's stored witness lines, judged now (<see cref="SignalSuppressionService.WitnessDetailShownOnReadAsync"/>): a
    /// line that carries a <c>text</c> or <c>source</c> keeps them only when the caller's own read returns its witness
    /// (<see cref="WitnessesTheCallerReadsAsync"/>, D-142) AND its witness is in the Signal's own known secure family (D-123);
    /// otherwise both are removed and the line carries <c>"withheld": true</c> (kind, ref, tier, as-of and clause stay). A fault in
    /// either judgement withholds every such line (fail closed; the list itself is unchanged). Stored evidence that is not a JSON array
    /// is returned as null: what it holds cannot be judged line by line.
    /// </summary>
    private async Task<List<VisibleRow>> ApplyWitnessRuleAsync(List<VisibleRow> rows, CancellationToken ct)
    {
        var parsed = new Dictionary<Guid, JsonArray?>();
        var asked = new List<(Guid SignalId, IReadOnlyList<string> References)>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Row.EvidenceRefs) || parsed.ContainsKey(row.Row.SignalId))
            {
                continue;
            }

            var lines = ParseEvidence(row.Row.EvidenceRefs);
            parsed[row.Row.SignalId] = lines;
            var references = lines?.OfType<JsonObject>().Where(HasDetail).Select(RefOf).OfType<string>().ToList();
            if (references is { Count: > 0 })
            {
                asked.Add((row.Row.SignalId, references));
            }
        }

        if (parsed.Count == 0)
        {
            return rows;
        }

        // D-142 (task 151): the caller's own read of the witness rows first; a fault there withholds every detail on the page.
        IReadOnlyDictionary<Guid, IReadOnlySet<string>>? shown = new Dictionary<Guid, IReadOnlySet<string>>();
        if (asked.Count > 0)
        {
            var readable = await WitnessesTheCallerReadsAsync(asked.SelectMany(a => a.References), ct).ConfigureAwait(false);
            shown = readable is null ? null : await _families.WitnessDetailShownOnReadAsync(asked, readable, ct).ConfigureAwait(false);
        }

        var withheld = 0;
        var result = new List<VisibleRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!parsed.TryGetValue(row.Row.SignalId, out var lines))
            {
                result.Add(row);
                continue;
            }

            if (lines is null)
            {
                result.Add(row with { Row = row.Row with { EvidenceRefs = null } });
                continue;
            }

            var keep = shown is not null && shown.TryGetValue(row.Row.SignalId, out var set) ? set : null;
            var copy = (JsonArray)lines.DeepClone();
            foreach (var line in copy.OfType<JsonObject>().Where(HasDetail))
            {
                if (keep is not null && RefOf(line) is { } reference && keep.Contains(reference))
                {
                    continue;
                }

                line.Remove("text");
                line.Remove("source");
                line["withheld"] = true;
                withheld++;
            }

            result.Add(row with { Row = row.Row with { EvidenceRefs = copy.ToJsonString() } });
        }

        if (withheld > 0)
        {
            _logger.LogInformation("Signal read: the detail of {Count} witness line(s) is withheld (a witness the caller cannot read, or another or an undetermined secure family; D-142, D-123).", withheld);
        }

        return result;

        static bool HasDetail(JsonObject line) => line["text"] is not null || line["source"] is not null;

        static string? RefOf(JsonObject line) => line["ref"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>
    /// D-142 (task 151): the witnesses (<see cref="SignalSuppressionService.WitnessOf"/>) the CALLER's own read returns. One batched
    /// read per witness table per request, in lists of at most <see cref="WitnessIdChunk"/> ids, never one per witness. The table's
    /// entity set and key come from uac-r2's lineage (<see cref="SecureChildLineage"/>), the only tables a witness can name. A table
    /// the caller has no Read on (403) returns none of its rows. Any other failure (a 5xx, throttling, an unparseable body, an
    /// exception) returns null: the caller then withholds every detail on the page, and the list itself is unchanged (fail closed,
    /// logged at Warning with the table and the error code only).
    /// </summary>
    /// <remarks>Component justification (CLAUDE.md section 11). Existing: the core-record and subject reads above (batched caller
    /// reads through <see cref="IDataverseUserClient"/>, <see cref="InFilter"/>). Extension: they read core and subject tables, never
    /// the rows a Signal's evidence quotes, so this applies the same caller read to the witness rows. Cost of doing nothing: a reader
    /// sees the subject and sender of a communication they cannot open (re-filed to another business unit's matter, or unfiled and
    /// privately owned), contrary to D-142.</remarks>
    private async Task<IReadOnlySet<(string Table, Guid Id)>?> WitnessesTheCallerReadsAsync(IEnumerable<string> references, CancellationToken ct)
    {
        var readable = new HashSet<(string Table, Guid Id)>();
        foreach (var byTable in references.Select(SignalSuppressionService.WitnessOf).OfType<(string Table, Guid Id)>().Distinct()
                     .GroupBy(w => w.Table, StringComparer.Ordinal))
        {
            var lineage = SecureChildLineage.Children[byTable.Key];
            var pk = lineage.IdColumn;
            foreach (var chunk in byTable.Select(w => w.Id).Chunk(WitnessIdChunk))
            {
                DataverseUserResponse response;
                try
                {
                    response = await _user.GetAsync($"{lineage.EntitySet}?$select={pk}&$filter={InFilter(pk, chunk)}", ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(SuppressionEvents.WitnessReadFailed,
                        "Signal read: the caller's read of {Table} witnesses faulted ({ExceptionType}); every witness line is shown without its detail (D-142, fail closed).",
                        byTable.Key, ex.GetType().Name);
                    return null;
                }

                if (response.IsSuccess
                    && response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in value.EnumerateArray())
                    {
                        if (TryReadGuid(row, pk, out var id) && chunk.Contains(id))
                        {
                            readable.Add((byTable.Key, id));
                        }
                    }

                    continue;
                }

                // A 403 is "no Read on the table": none of its witnesses is readable. A 401 is the same mapped code but means the
                // caller's token failed mid-request, which is a fault (every detail on the page is withheld), not a verdict.
                if (!response.IsSuccess && response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && response.StatusCode != 401)
                {
                    continue;
                }

                _logger.LogWarning(SuppressionEvents.WitnessReadFailed,
                    "Signal read: the caller's read of {Table} witnesses failed ({ErrorCode}); every witness line is shown without its detail (D-142, fail closed).",
                    byTable.Key, response.IsSuccess ? "unparseable" : response.ErrorCode);
                return null;
            }
        }

        return readable;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // (9b) a billing Duplicate's earlier invoice (D-176, task 157)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Evidence kind of a billing reference value (ebilling's <c>BillingFindingSignalMapper.ReferenceEvidenceKind</c>).</summary>
    internal const string BillingReferenceKind = "billing.reference";

    /// <summary>The ref prefix of every evidence line that quotes the earlier submission (ebilling's <c>DuplicateHandler</c>).</summary>
    internal const string DuplicateOfPrefix = "duplicateOf.";

    internal const string DuplicateOfInvoiceIdRef = "duplicateOf.invoiceId";
    internal const string DuplicateOfTrackingNumberRef = "duplicateOf.trackingNumber";

    /// <summary><c>sprk_billingevent</c>'s entity set (live on spaarkedev1, 2026-10-21): a line-level Duplicate's subject.</summary>
    internal const string BillingEventEntitySet = "sprk_billingevents";

    /// <summary>What a stored Duplicate quotes about its earlier invoice: the <c>duplicateOf.invoiceId</c> values that are GUIDs and the
    /// non-blank <c>duplicateOf.trackingNumber</c> values.</summary>
    private sealed record DuplicateQuote(IReadOnlyList<Guid> InvoiceIds, IReadOnlyList<string> TrackingNumbers);

    /// <summary>
    /// D-176: the rows whose STORED evidence has a <c>billing.reference</c> line (kind and ref matched case-insensitively) with ref
    /// <c>duplicateOf.invoiceId</c> or <c>duplicateOf.trackingNumber</c>, with what those lines quote. No other row is a Duplicate.
    /// Evidence that is not a JSON array cannot be judged and is returned as null by (9) anyway.
    /// </summary>
    private static Dictionary<Guid, DuplicateQuote> DuplicateQuotesOf(IEnumerable<VisibleRow> rows)
    {
        var quotes = new Dictionary<Guid, DuplicateQuote>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Row.EvidenceRefs) || ParseEvidence(row.Row.EvidenceRefs) is not { } lines)
            {
                continue;
            }

            var duplicate = false;
            var ids = new List<Guid>();
            var numbers = new List<string>();
            foreach (var line in lines.OfType<JsonObject>())
            {
                if (!string.Equals(StringOf(line["kind"]), BillingReferenceKind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var reference = StringOf(line["ref"]);
                var text = StringOf(line["text"]);
                if (string.Equals(reference, DuplicateOfInvoiceIdRef, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    if (Guid.TryParse(text, out var id) && id != Guid.Empty)
                    {
                        ids.Add(id);
                    }
                }
                else if (string.Equals(reference, DuplicateOfTrackingNumberRef, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        numbers.Add(text);
                    }
                }
            }

            if (duplicate)
            {
                quotes[row.Row.SignalId] = new DuplicateQuote([.. ids.Distinct()], [.. numbers.Distinct(StringComparer.Ordinal)]);
            }
        }

        return quotes;
    }

    /// <summary>
    /// D-176: each Duplicate row keeps its earlier-invoice detail only when the caller's own read returns every earlier invoice it
    /// quotes (<see cref="EarlierInvoiceReadability"/>). Otherwise the row takes ebilling's write-time withheld shape: no
    /// <c>duplicateOf.*</c> line, every other line without <c>text</c> or <c>source</c> and marked <c>"withheld": true</c>, and the
    /// sentence <see cref="EarlierInvoiceReadability.WithheldSentence"/>. The title is unchanged (ebilling's headlines are static
    /// text). A fault in any read withholds every Duplicate row on the page; no other row is touched and the list is unchanged.
    /// </summary>
    private async Task<List<VisibleRow>> ApplyDuplicateRuleAsync(
        List<VisibleRow> rows,
        IReadOnlyDictionary<Guid, DuplicateQuote> duplicates,
        IReadOnlyDictionary<Guid, CatalogRow> catalog,
        CancellationToken ct)
    {
        var shown = await DuplicatesShownAsync(rows, duplicates, catalog, ct).ConfigureAwait(false);
        var withheld = 0;
        var result = new List<VisibleRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!duplicates.ContainsKey(row.Row.SignalId) || shown.Contains(row.Row.SignalId))
            {
                result.Add(row);
                continue;
            }

            result.Add(row with
            {
                Row = row.Row with
                {
                    EvidenceRefs = WithheldDuplicateEvidence(row.Row.EvidenceRefs),
                    Sentence = EarlierInvoiceReadability.WithheldSentence,
                },
            });
            withheld++;
        }

        if (withheld > 0)
        {
            _logger.LogInformation(
                "Signal read: the earlier-invoice detail of {Count} Duplicate Signal(s) is withheld (an earlier invoice the caller cannot read, an unresolved number, or a fault; D-176).",
                withheld);
        }

        return result;
    }

    /// <summary>
    /// The Duplicate rows whose earlier invoice(s) the caller reads. An id quoted as <c>duplicateOf.invoiceId</c> is judged directly.
    /// A row that quotes only a tracking number is resolved within its CURRENT invoice's vendor org: the current invoice is the subject
    /// when the subject is <c>sprk_invoice</c>, or the subject billing event's invoice (<c>sprk_billingevent</c>, option B after
    /// escalation 1), each read AS THE CALLER; any other subject, an event or invoice the caller cannot read, or one with no invoice or
    /// vendor org: withheld. Reads per request: at most one billing-event read and one current-invoice read (In lists of at most
    /// <see cref="WitnessIdChunk"/> ids), then <see cref="EarlierInvoiceReadability.ShownAsync{TKey}"/>, which owns the combining
    /// rule (at least one reference, every one readable; none shown on a fault). A Duplicate with no usable reference is not shown.
    /// </summary>
    private async Task<HashSet<Guid>> DuplicatesShownAsync(
        List<VisibleRow> rows,
        IReadOnlyDictionary<Guid, DuplicateQuote> duplicates,
        IReadOnlyDictionary<Guid, CatalogRow> catalog,
        CancellationToken ct)
    {
        var shown = new HashSet<Guid>();
        var referencesOf = new Dictionary<Guid, IReadOnlyCollection<EarlierInvoiceReadability.Reference>>();
        var currentInvoiceOf = new Dictionary<Guid, Guid>();
        var eventOf = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
        {
            if (!duplicates.TryGetValue(row.Row.SignalId, out var quote))
            {
                continue;
            }

            // Every Duplicate gets an entry. One with no reference found below is not shown (EarlierInvoiceReadability.ShownAsync).
            referencesOf[row.Row.SignalId] = [];
            if (quote.InvoiceIds.Count > 0)
            {
                referencesOf[row.Row.SignalId] = [.. quote.InvoiceIds.Select(id => new EarlierInvoiceReadability.Reference(id, null, null))];
            }
            else if (quote.TrackingNumbers.Count > 0 && row.Row.SubjectId is { } subject)
            {
                switch (SubjectTable(row.Row, catalog))
                {
                    case "sprk_invoice":
                        currentInvoiceOf[row.Row.SignalId] = subject;
                        break;
                    case "sprk_billingevent":
                        eventOf[row.Row.SignalId] = subject;
                        break;
                }
            }

            // Otherwise nothing identifies the earlier invoice: no reference, so withheld.
        }

        if (eventOf.Count > 0)
        {
            var invoiceOfEvent = await CallerLookupAsync(
                BillingEventEntitySet, "sprk_billingeventid", EarlierInvoiceReadability.InvoiceColumn, eventOf.Values, "sprk_billingevent", ct)
                .ConfigureAwait(false);
            if (invoiceOfEvent is null)
            {
                return shown;
            }

            foreach (var (signalId, billingEvent) in eventOf)
            {
                if (invoiceOfEvent.TryGetValue(billingEvent, out var invoice))
                {
                    currentInvoiceOf[signalId] = invoice;
                }
            }
        }

        if (currentInvoiceOf.Count > 0)
        {
            var vendorOrgOf = await CallerLookupAsync(
                EarlierInvoiceReadability.InvoiceEntitySet, EarlierInvoiceReadability.InvoiceKey, EarlierInvoiceReadability.VendorOrgColumn,
                currentInvoiceOf.Values, "sprk_invoice", ct).ConfigureAwait(false);
            if (vendorOrgOf is null)
            {
                return shown;
            }

            foreach (var (signalId, invoice) in currentInvoiceOf)
            {
                if (vendorOrgOf.TryGetValue(invoice, out var vendorOrg))
                {
                    referencesOf[signalId] =
                        [.. duplicates[signalId].TrackingNumbers.Select(n => new EarlierInvoiceReadability.Reference(null, vendorOrg, n))];
                }
            }
        }

        return (await _earlierInvoices.ShownAsync(_user, referencesOf, ct).ConfigureAwait(false)).ToHashSet();
    }

    /// <summary>One lookup column of the rows the CALLER's batched read returns (In lists of at most <see cref="WitnessIdChunk"/> ids):
    /// id → the lookup's value; a row without it is absent. Null on a fault (<see cref="EarlierInvoiceReadability.ReadAsCallerAsync"/>).</summary>
    private async Task<Dictionary<Guid, Guid>?> CallerLookupAsync(
        string entitySet, string key, string lookup, IEnumerable<Guid> ids, string table, CancellationToken ct)
    {
        var found = new Dictionary<Guid, Guid>();
        foreach (var chunk in ids.Distinct().Chunk(WitnessIdChunk))
        {
            var rows = await _earlierInvoices.ReadAsCallerAsync(
                _user, $"{entitySet}?$select={key},{lookup}&$filter={InFilter(key, chunk)}", table, ct).ConfigureAwait(false);
            if (rows is null)
            {
                return null;
            }

            foreach (var row in rows)
            {
                if (EarlierInvoiceReadability.ReadGuid(row, key) is { } id && chunk.Contains(id)
                    && EarlierInvoiceReadability.ReadGuid(row, lookup) is { } value)
                {
                    found[id] = value;
                }
            }
        }

        return found;
    }

    /// <summary>ebilling's write-time withheld shape for a Duplicate's evidence, applied to the lines (9) returned: every
    /// <c>duplicateOf.*</c> line removed; every other line without <c>text</c> or <c>source</c> and marked <c>"withheld": true</c>.
    /// Null stays null; evidence that cannot be parsed is returned as null.</summary>
    private static string? WithheldDuplicateEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return evidence;
        }

        if (ParseEvidence(evidence) is not { } lines)
        {
            return null;
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = (JsonObject)lines[i]!;
            if (StringOf(line["ref"]) is { } reference && reference.StartsWith(DuplicateOfPrefix, StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                continue;
            }

            line.Remove("text");
            line.Remove("source");
            line["withheld"] = true;
        }

        return lines.ToJsonString();
    }

    private static string? StringOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The stored evidence as a JSON array of lines, or null when it is not one (never judged, never shown).</summary>
    /// <remarks>Property names are matched case-insensitively (a <c>Text</c> or <c>SOURCE</c> is detail too). A line whose names
    /// collide under that rule (<c>text</c> and <c>Text</c>) cannot be judged, so the Signal's whole evidence is withheld (null).</remarks>
    private static JsonArray? ParseEvidence(string json)
    {
        try
        {
            if (JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) is not JsonArray lines
                || !lines.All(l => l is JsonObject))
            {
                return null;
            }

            // JsonObject builds its property table lazily; build it now so a duplicate name fails here, for this Signal only.
            foreach (var line in lines.OfType<JsonObject>())
            {
                _ = line.Count;
            }

            return lines;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // shaping and D-42 order
    // ---------------------------------------------------------------------------------------------------------------

    /// <param name="SubjectTable">Do lane: the table whose caller-scoped "mine" read returned the subject; null on a Decide row.</param>
    private sealed record VisibleRow(SignalRow Row, bool HighPriority, Guid? AssigneeContact, string? SubjectTable);

    private static IReadOnlyList<WorklistLaneGroups> Shape(
        WorklistSet set,
        IReadOnlyList<VisibleRow> rows,
        IReadOnlyDictionary<(Guid TypeId, Guid CoreId), (string? Name, string? Number)> cores,
        IReadOnlyDictionary<Guid, CatalogRow> catalog,
        IReadOnlyDictionary<Guid, WorklistDecisionRecord> decisions)
    {
        var lanes = new List<WorklistLaneGroups>();
        foreach (var lane in new[] { DecisionLane.Decide, DecisionLane.Do })
        {
            var groups = rows.Where(r => r.Row.Lane == lane)
                .GroupBy(r => r.Row.HasNoCore ? (Guid.Empty, Guid.Empty) : (r.Row.CoreTypeId!.Value, r.Row.CoreId!.Value))
                .Select(g =>
                {
                    WorklistCoreRecord? core = null;
                    if (g.Key != (Guid.Empty, Guid.Empty))
                    {
                        var type = catalog[g.Key.Item1];
                        var (name, number) = cores[g.Key];
                        core = new WorklistCoreRecord(type.LogicalName, g.Key.Item2, type.Label, name, number);
                    }

                    return (Core: core, Rows: g.ToList());
                })
                .ToList();

            if (set == WorklistSet.Lanes)
            {
                // D-42: severity, then the subject's sprk_highpriority, then oldest, then record number; a core record's items stay
                // together at its best-ranked item. Deterministic: ties end on the record and Signal ids. No model input.
                foreach (var group in groups)
                {
                    group.Rows.Sort(CompareItems);
                }

                groups.Sort((a, b) =>
                {
                    var byBest = CompareRankKeys(a.Rows[0], b.Rows[0]);
                    if (byBest != 0) return byBest;
                    var byNumber = CompareNullableOrdinal(a.Core?.Number, b.Core?.Number);
                    if (byNumber != 0) return byNumber;
                    var byType = string.CompareOrdinal(a.Core?.RecordType ?? "~", b.Core?.RecordType ?? "~");
                    if (byType != 0) return byType;
                    var byCore = (a.Core?.RecordId ?? Guid.Empty).CompareTo(b.Core?.RecordId ?? Guid.Empty);
                    return byCore != 0 ? byCore : a.Rows[0].Row.SignalId.CompareTo(b.Rows[0].Row.SignalId);
                });
            }

            // Secondary sets keep the configuration's order (membership and order come from the row, FR-24), grouped by first
            // appearance; the closed and suppressed queries already order by the core pair first.

            if (lane == DecisionLane.Do)
            {
                // HANDOFF 1.2 (task 154, #1959 item 3): in every set, the Do lane's "Not filed" group (D-35) comes last; every other
                // group keeps its order above. Stable, so nothing else moves. The Decide lane has no Not filed group (no-core rows
                // are Do-only).
                groups = [.. groups.Where(g => g.Core is not null), .. groups.Where(g => g.Core is null)];
            }

            lanes.Add(new WorklistLaneGroups(LaneName(lane), groups
                .Select(g => new WorklistGroup(g.Core, g.Core is null, g.Rows.Select(r => ToItem(r, decisions)).ToList()))
                .ToList()));
        }

        return lanes;
    }

    private static int CompareItems(VisibleRow a, VisibleRow b)
    {
        var byRank = CompareRankKeys(a, b);
        return byRank != 0 ? byRank : a.Row.SignalId.CompareTo(b.Row.SignalId);
    }

    /// <summary>D-42's item keys: severity (Critical first, unknown last), flagged first, oldest first (undated last).</summary>
    private static int CompareRankKeys(VisibleRow a, VisibleRow b)
    {
        var bySeverity = SeverityRank(a.Row.Severity).CompareTo(SeverityRank(b.Row.Severity));
        if (bySeverity != 0) return bySeverity;
        var byFlag = (a.HighPriority ? 0 : 1).CompareTo(b.HighPriority ? 0 : 1);
        if (byFlag != 0) return byFlag;
        return (a.Row.FirstDetected ?? DateTimeOffset.MaxValue).CompareTo(b.Row.FirstDetected ?? DateTimeOffset.MaxValue);
    }

    internal static int SeverityRank(int? severity) => severity switch
    {
        SeverityCritical => 0,
        SeverityWarning => 1,
        SeverityInfo => 2,
        _ => 3,
    };

    private static int CompareNullableOrdinal(string? a, string? b) =>
        a is null ? (b is null ? 0 : 1) : b is null ? -1 : string.CompareOrdinal(a, b);

    private static WorklistItemRow ToItem(VisibleRow visible, IReadOnlyDictionary<Guid, WorklistDecisionRecord> decisions)
    {
        var row = visible.Row;
        var workType = row.WorkType is { } w && WorkTypeKeys.TryGetValue(w, out var key) ? key : null;

        // Task 154 (#1958): the subject reference only where the caller's own "mine" read returned the subject (Do lane).
        var subjectTable = row.Lane == DecisionLane.Do ? visible.SubjectTable : null;
        return new WorklistItemRow(
            row.SignalId,
            LaneName(row.Lane!.Value),
            row.Status is { } s && StatusLabels.TryGetValue(s, out var label) ? label : null,
            row.Title,
            row.SubjectName,
            row.RuleShortName,
            workType,
            workType == LateWorkType ? "late" : "overdue",
            row.FirstDetected,
            row.DueDate,
            row.EvidenceRefs,
            row.PolicyVersionId,
            row.ResolvedOn,
            row.ResolutionType,
            row.SuppressedUntil,
            row.Sentence,
            subjectTable,
            subjectTable is null ? null : row.SubjectId,
            decisions.GetValueOrDefault(row.SignalId));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------------------------

    private sealed record AccessRows(IReadOnlyList<JsonElement> Rows, SignalReadResult? Outcome);

    /// <summary>
    /// A batched caller read: success = the rows the caller may read; 403 (no Read on the table at all) = none; an unresolved
    /// caller = the single 403; anything else is a fault (503), never "none".
    /// </summary>
    private AccessRows ReadAccessRows(DataverseUserResponse response, string what)
    {
        if (response.IsSuccess)
        {
            return response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? new AccessRows(value.EnumerateArray().Select(e => e.Clone()).ToList(), null)
                : new AccessRows([], SignalReadResult.Unavailable($"{what} read unparseable"));
        }

        if (IsCallerUnresolved(response.ErrorCode))
        {
            return new AccessRows([], SignalReadResult.CallerUnresolved);
        }

        if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
        {
            _logger.LogDebug("Signal read: the caller has no read on the {What} table; those rows are dropped.", what);
            return new AccessRows([], null);
        }

        _logger.LogWarning("Signal read: the {What} read failed ({ErrorCode}); unavailable.", what, response.ErrorCode);
        return new AccessRows([], SignalReadResult.Unavailable($"{what} read failed"));
    }

    private static bool IsCallerUnresolved(string? code) =>
        code is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured;

    /// <summary><c>Microsoft.Dynamics.CRM.In(PropertyName='pk',PropertyValues=['…',…])</c> — one batched read for many ids.</summary>
    internal static string InFilter(string primaryKey, IEnumerable<Guid> ids)
    {
        var values = string.Join(',', ids.Select(id => $"'{id:D}'"));
        return $"Microsoft.Dynamics.CRM.In(PropertyName='{primaryKey}',PropertyValues=[{values}])";
    }

    private static string? ReadDisplay(JsonElement row, string column)
    {
        if (row.TryGetProperty(column, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
        }

        // A lookup display field comes back as its formatted value only when annotations are asked for; not here.
        return null;
    }

    private static bool IsLogicalName(string value) =>
        value.Length > 0 && char.IsAsciiLetterLower(value[0]) && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    private static string? ReadString(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryReadGuid(JsonElement? row, string name, out Guid id)
    {
        id = Guid.Empty;
        return row is { ValueKind: JsonValueKind.Object } r
            && r.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out id)
            && id != Guid.Empty;
    }

    /// <summary>One Signal row as the worklist FetchXML returns it through the Web API.</summary>
    internal sealed record SignalRow(
        Guid SignalId,
        DecisionLane? Lane,
        int? Status,
        string? Title,
        DateTimeOffset? FirstDetected,
        string? DueDate,
        Guid? SubjectTypeId,
        Guid? SubjectId,
        string? SubjectName,
        Guid? CoreTypeId,
        Guid? CoreId,
        bool HasNoCore,
        Guid? PolicyVersionId,
        Guid? PolicyId,
        string? EvidenceRefs,
        string? RuleShortName,
        int? WorkType,
        int? Severity,
        DateTimeOffset? ResolvedOn,
        int? ResolutionType,
        DateTimeOffset? SuppressedUntil,
        string? Sentence,
        Guid? DecisionRecordId)
    {
        internal static bool TryParse(JsonElement raw, out SignalRow row)
        {
            row = null!;
            if (!TryReadGuid(raw, "sprk_signalid", out var id))
            {
                return false;
            }

            var hasType = TryReadGuid(raw, "_sprk_corerecordtype_value", out var coreType);
            var coreText = ReadString(raw, "sprk_corerecordid");
            var hasCoreId = Guid.TryParse(coreText, out var coreId) && coreId != Guid.Empty;
            var noCore = !hasType && string.IsNullOrWhiteSpace(coreText);

            row = new SignalRow(
                id,
                Int(raw, "sprk_lane") is { } lane && Enum.IsDefined(typeof(DecisionLane), lane) ? (DecisionLane)lane : null,
                Int(raw, "sprk_signalstatus"),
                ReadString(raw, "sprk_name"),
                Instant(raw, "sprk_firstdetected"),
                ReadString(raw, "sprk_duedate"),
                TryReadGuid(raw, "_sprk_regardingrecordtype_value", out var subjectType) ? subjectType : null,
                Guid.TryParse(ReadString(raw, "sprk_regardingrecordid"), out var subjectId) && subjectId != Guid.Empty ? subjectId : null,
                ReadString(raw, "sprk_regardingrecordname"),
                // A half pair (type without id, or id without type) is malformed: neither "no core" nor a core to check, so the
                // row is dropped by both paths (never treated as owner-only, as in SignalCoreRecordAccess).
                hasType && hasCoreId ? coreType : null,
                hasType && hasCoreId ? coreId : null,
                noCore,
                TryReadGuid(raw, "_sprk_policyversion_value", out var version) ? version : null,
                TryReadGuid(raw, "_sprk_policy_value", out var policyId) ? policyId : null,
                ReadString(raw, "sprk_evidencerefs"),
                AsString(Aliased(raw, "sprk_shortname")),
                Int(Aliased(raw, "sprk_worktype")),
                Int(Aliased(raw, "sprk_severity")),
                Instant(raw, "sprk_resolvedon"),
                Int(raw, "sprk_resolutiontype"),
                Instant(raw, "sprk_suppresseduntil"),
                // Task 154: as stored; an empty or blank sentence is null (selected by the worklist configuration's queries).
                ReadString(raw, "sprk_sentence") is { } sentence && !string.IsNullOrWhiteSpace(sentence) ? sentence : null,
                // Task 045: the Decision Record that closed it (closed and decided sets select the column).
                TryReadGuid(raw, "_sprk_decisionrecord_value", out var decisionRecord) ? decisionRecord : null);
            return true;
        }

        /// <summary>The <c>policy</c> link-entity column, present only under the pre-D-185 configuration: the Web API names it
        /// <c>policy.{column}</c> (verified live on spaarkedev1, 2026-10-14); the OData-escaped <c>policy_x002e_{column}</c> is accepted
        /// too. Since D-185 the BFF reads these columns itself (<c>ApplyPolicyColumnsAsync</c>) and the configuration has no link.</summary>
        private static JsonElement? Aliased(JsonElement raw, string column) =>
            raw.TryGetProperty($"policy.{column}", out var v) || raw.TryGetProperty($"policy_x002e_{column}", out v) ? v : null;

        private static string? AsString(JsonElement? value) => value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

        private static int? Int(JsonElement raw, string name) => raw.TryGetProperty(name, out var v) ? Int(v) : null;

        private static int? Int(JsonElement? value) =>
            value is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

        private static DateTimeOffset? Instant(JsonElement raw, string name) =>
            ReadString(raw, name) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                ? at
                : null;
    }
}
