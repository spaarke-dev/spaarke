// ebilling-r1 task 013 (spec FR-05, ADR-024; for review by unified-access-control-r2).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — CoreAncestorResolver derives stamps from rows already written and refuses only two DIFFERENT records of
//       one root type; an invoice naming both a matter and a project derives two stamps. TodoRegardingBuilder refuses a
//       second regarding lookup on a to-do / event, but throws, knows only its own host tables and writes the pair with
//       an optional record type. InvoiceReviewService.BuildInvoiceCreateFields writes the resolver fields for a matter only.
//   (2) Extension — the tables and record-type rules are reused (CoreAncestorResolver.StampColumnFor for the
//       sprk_regarding{core} names, RegardingRecordType for the name / number / URL). CoreAncestorResolver itself cannot
//       be the guard: it derives from a stored row, while FR-05 must refuse BEFORE the write (ADR-002, one owner).
//       Its stored-row twin is CoreAncestorResolver.ResolveInvoiceFamilyCoreRecordAsync, which calls Check below.
//   (3) Cost of doing nothing — a write naming two core records is accepted (FR-05's negative acceptance fails), and
//       every invoice-family writer (tasks 044, 049, 100, 104) re-implements the resolver fields.
//
// Placement (bff-extensions.md): Services/Dataverse beside CoreAncestorResolver, the shared write-path owner. A static
// class: no package, no endpoint, no DI registration (ADR-010); its read helpers take the services their caller holds.
// Task 026 fix-round-2: D-96 (an invoice re-file must not split it from its lines; the count is app-only) and DEF-022
// (#2056: CheckGenericUpdateAsync for the three generic update paths that do not write through OwnedChildWrite).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The one server-side owner of the FR-05 invariant for the invoice family: a row of <c>sprk_invoice</c>,
/// <c>sprk_billingevent</c>, <c>sprk_invoicesubmission</c>, <c>sprk_invoiceapproval</c>, <c>sprk_externalidentifier</c>
/// or <c>sprk_invoicecorrection</c> names exactly ONE core record (matter, project or work assignment) through its
/// entity-specific lookup, and carries all five ADR-024 resolver fields describing it.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> A writer builds its payload, calls <see cref="Check(Entity)"/> (or the column-reader overload for a
/// Web API payload) with the row's lookups AS THEY WILL BE after the write (for an update, the stored values merged with
/// the change), then <see cref="DeriveResolverFieldsAsync"/>, and writes <see cref="InvoiceFamilyResolverFields.ToFields"/>
/// with the lookup. A refusal means nothing is written; <see cref="InvoiceFamilyCoreRecord.RefusalCode"/> is stable.</para>
/// <para><b>The write core invokes it (ISS-010, #1973; unified-access-control-r2).</b> <c>RecordCreationService</c>'s
/// pre-write step (the create; task 017 replaced the owned create that called it, deleted in 017) and
/// <c>OwnedChildWrite.RefileAsync</c> — the cores every child-record route and chat create / update tool writes through —
/// call <see cref="CheckWriteAsync"/> for every write of a family table, keyed by table: on an update, on the stored
/// lookups read as the caller and overlaid with the change (<see cref="GuardedLookups"/>). This class owns the rule; the
/// core only assembles the row and reads what the rule needs.</para>
/// <para><b>The generic update paths (DEF-022, #2056).</b> The playbook update, the output orchestrator and the
/// field-mapping push call <see cref="CheckGenericUpdateAsync"/> before their own write, with their app-only reader.</para>
/// <para><b>An invoice re-file keeps its lines (D-96).</b> An update that writes an invoice's core lookup is refused with
/// <see cref="LineInvoiceCoreRecordMismatchCode"/> when any of its lines names another core record, counted as the app.</para>
/// <para><b>Supersedes.</b> <c>sprk_invoice.sprk_supersedesinvoice</c> is a peer reference, not a lineage parent: the
/// superseding and the superseded invoice must regard the same core record (<see cref="SupersedesCoreRecordMismatchCode"/>).</para>
/// <para><b>Refuses, never picks.</b> Two or three lookups set is <see cref="MultipleCoreRecordsCode"/>; none is
/// <see cref="NoCoreRecordCode"/>. A value that is not a lookup to the column's core table is
/// <see cref="LookupNotACoreRecordCode"/> (a mis-built payload must not read as "unset").</para>
/// <para><b>The mixed naming scheme</b> (project CLAUDE.md, task 012): <c>sprk_invoice</c> and <c>sprk_billingevent</c>
/// keep their typed <c>sprk_matter</c> / <c>sprk_project</c> and add <c>sprk_regardingworkassignment</c>; the tables
/// created for eBilling carry the <c>sprk_regarding{core}</c> trio (<see cref="CoreAncestorResolver.CoreAncestorLookups"/>
/// names). FR-05 names three core types, so <c>sprk_regardingservicerequest</c> is never one of them.</para>
/// <para><b>D-75.</b> A line's core record is its OWN lookup, never derived from <c>sprk_billingevent.sprk_invoice</c>: a
/// line may in future not belong to exactly one invoice. On a WRITE, a line that does name an invoice must regard the same
/// core record as that invoice (<see cref="LineInvoiceCoreRecordMismatchCode"/>, task 026): the invoice is compared, never
/// the source of the answer, and a line with no invoice is decided on its own lookup alone.</para>
/// <para><b>Invoice-only external identifiers (D-68).</b> An <c>sprk_externalidentifier</c> row with no core lookup but
/// with <c>sprk_regardinginvoice</c> regards the invoice: its pair (<c>sprk_regardingrecordid</c> and the other four
/// fields) describes the invoice, so the D-68 alternate key (firm, type, normalized value, record) never has an empty
/// column. The invoice is never walked to its core record (that would name a record whose lookup is empty).</para>
/// </remarks>
public static class InvoiceFamilyCoreRecordGuard
{
    /// <summary>The entity is not one of the invoice family's tables.</summary>
    public const string UnknownEntityCode = "invoice_family_unknown_entity";

    /// <summary>None of the row's core-record lookups is set (and, for an external identifier, no invoice either).</summary>
    public const string NoCoreRecordCode = "invoice_family_no_core_record";

    /// <summary>Two or three of the row's core-record lookups are set. FR-05: refused, never resolved by picking one.</summary>
    public const string MultipleCoreRecordsCode = "invoice_family_multiple_core_records";

    /// <summary>A core-record column holds a value that is not a reference to that column's core table.</summary>
    public const string LookupNotACoreRecordCode = "invoice_family_lookup_not_a_core_record";

    /// <summary>A metadata or record read failed. Fail closed: never "no core record".</summary>
    public const string ReadFailedCode = "invoice_family_read_failed";

    /// <summary>The row (or the record it regards) does not exist.</summary>
    public const string RecordNotFoundCode = "invoice_family_record_not_found";

    /// <summary>One of the five resolver fields cannot be filled (no record-type row, no name or no number).</summary>
    public const string ResolverFieldsIncompleteCode = "invoice_family_resolver_fields_incomplete";

    /// <summary>
    /// An invoice supersedes an invoice filed under a different core record (or one that names none, or more than one).
    /// <c>sprk_supersedesinvoice</c> is a peer reference, not a lineage parent (unified-access-control-r2, ISS-010): the two
    /// invoices must regard the same matter, project or work assignment.
    /// </summary>
    public const string SupersedesCoreRecordMismatchCode = "invoice_family_supersedes_core_record_mismatch";

    /// <summary>
    /// A line (<c>sprk_billingevent</c>) names an invoice filed under a different core record than its own (or an invoice
    /// that names none, or more than one). The line's core record is its own lookup (D-75); the invoice it belongs to must
    /// regard the same matter, project or work assignment (ebilling-r1 task 026, D-94 condition 2), so what is filed under
    /// a line and under its invoice lands in one core record.
    /// </summary>
    public const string LineInvoiceCoreRecordMismatchCode = "invoice_family_line_invoice_core_record_mismatch";

    /// <summary>
    /// An invoice would be filed under a work assignment (<c>sprk_regardingworkassignment</c>) that is itself filed under that
    /// same invoice (<c>sprk_workassignment.sprk_regardinginvoice</c>, owner Option A). Neither record would then say which
    /// matter or project the pair belongs to (unified-access-control-r2 ruling on RecordFamilyEdges, 2026-10-28). The work
    /// assignment is read as the APP.
    /// </summary>
    public const string WorkAssignmentCycleCode = "invoice_family_work_assignment_cycle";

    /// <summary>
    /// Task 045e (owner, 2026-11-13, #2772 "Refuse it"): the write's regarding pair (<c>sprk_regardingrecordid</c> and its record
    /// type) names a record other than the row's core record — the core lookup as it will be after the write (the value written
    /// in the same request, else the stored one). The pair describes the core record (045d); it is never corrected from the
    /// write, so a write that disagrees writes nothing. <see cref="CheckRegardingPairAgreesAsync"/>.
    /// </summary>
    public const string RegardingPairCoreRecordMismatchCode = "invoice_family_regarding_pair_core_record_mismatch";

    /// <summary>The reason of <see cref="RegardingPairCoreRecordMismatchCode"/>: fixed text, naming no record.</summary>
    internal const string RegardingPairCoreRecordMismatchReason =
        "The regarding record (sprk_regardingrecordid and its record type) must name this row's own matter, project or work "
        + "assignment (for an external identifier filed under an invoice only, that invoice); this write names another record. "
        + "To move the row, change its core-record lookup and the server writes the regarding record. Nothing was written.";

    /// <summary><c>sprk_workassignment.sprk_regardinginvoice</c> (lookup to <c>sprk_invoice</c>): the invoice a work assignment
    /// is filed under (<c>SecureRootInheritance.IntermediateFilingColumns</c>).</summary>
    public const string WorkAssignmentInvoiceColumn = "sprk_regardinginvoice";

    private const string WorkAssignmentEntity = "sprk_workassignment";

    /// <summary><c>sprk_billingevent.sprk_invoice</c> (lookup to <c>sprk_invoice</c>): the invoice a line belongs to.</summary>
    public const string LineInvoiceColumn = "sprk_invoice";

    /// <summary><c>sprk_invoice.sprk_supersedesinvoice</c> (lookup to <c>sprk_invoice</c>, task 012): the invoice this one replaces.</summary>
    public const string SupersedesInvoiceColumn = "sprk_supersedesinvoice";

    /// <summary>The external identifier table, the one family table whose row may regard an invoice only (D-68).</summary>
    public const string ExternalIdentifierEntity = "sprk_externalidentifier";

    /// <summary><c>sprk_externalidentifier.sprk_regardinginvoice</c> (lookup to <c>sprk_invoice</c>).</summary>
    public const string ExternalIdentifierInvoiceColumn = "sprk_regardinginvoice";

    private const string InvoiceEntity = "sprk_invoice";

    private const string LineEntity = "sprk_billingevent";

    /// <summary>
    /// The shortest live <c>sprk_regardingrecordname</c> in the family (<c>sprk_invoice</c> / <c>sprk_billingevent</c>:
    /// 100; the eBilling tables: 200). The name is display text, so it is cut to fit every table.
    /// </summary>
    internal const int RegardingRecordNameMaxLength = 100;

    /// <summary>The three core types FR-05 allows (the service request is a core record, but not an invoice's).</summary>
    private static readonly string[] FilingCores = ["sprk_matter", "sprk_project", "sprk_workassignment"];

    /// <summary>
    /// The core-record types, read-only (ebilling-r1 task 089b: the assignment and override routes are fixed per core type from this list,
    /// never from a free string; a Procedure assignment's typed core lookups are named after them, task 080).
    /// </summary>
    internal static IReadOnlyList<string> FilingCoreTables => FilingCores;

    /// <summary>
    /// Each family table's entity-specific core-record lookups (column → core table), from live metadata (spaarkedev1,
    /// 2026-10-15, <c>EntityDefinitions(...)/ManyToOneRelationships</c>; tasks 010, 012, 020, 021, 022, 027).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string CoreEntity)>> CoreRecordLookups =
        new Dictionary<string, IReadOnlyList<(string Column, string CoreEntity)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_invoice"] = TypedMatterProjectPlusRegardingWorkAssignment(),
            ["sprk_billingevent"] = TypedMatterProjectPlusRegardingWorkAssignment(),
            ["sprk_invoicesubmission"] = RegardingTrio(),
            ["sprk_invoiceapproval"] = RegardingTrio(),
            ["sprk_externalidentifier"] = RegardingTrio(),
            // D-82 (task 027): the correction table carries the trio "like the other invoice-family child tables"; its
            // writers (tasks 100, 101, 103) use this guard too.
            ["sprk_invoicecorrection"] = RegardingTrio(),
        };

    private static IReadOnlyList<(string Column, string CoreEntity)> TypedMatterProjectPlusRegardingWorkAssignment() =>
    [
        ("sprk_matter", "sprk_matter"),
        ("sprk_project", "sprk_project"),
        (CoreAncestorResolver.StampColumnFor("sprk_workassignment"), "sprk_workassignment"),
    ];

    private static IReadOnlyList<(string Column, string CoreEntity)> RegardingTrio() =>
        FilingCores.Select(core => (CoreAncestorResolver.StampColumnFor(core), core)).ToArray();

    /// <summary>
    /// The lookups a stored row is read for: its core-record lookups and, for an external identifier, its invoice. Never a
    /// line's <c>sprk_invoice</c> (D-75). Null for a table outside the family.
    /// </summary>
    public static IReadOnlyList<string>? ColumnsToRead(string entityLogicalName)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName)
            || !CoreRecordLookups.TryGetValue(entityLogicalName.Trim(), out var lookups))
        {
            return null;
        }

        var columns = lookups.Select(l => l.Column).ToList();
        if (IsExternalIdentifier(entityLogicalName))
        {
            columns.Add(ExternalIdentifierInvoiceColumn);
        }

        return columns;
    }

    /// <summary>
    /// Every lookup a write of <paramref name="entityLogicalName"/> is decided on, with the one table it targets: the core
    /// lookups (<see cref="CoreRecordLookups"/>), an external identifier's invoice (D-68), an invoice's
    /// <see cref="SupersedesInvoiceColumn"/> and a line's <see cref="LineInvoiceColumn"/> (compared with the line's own core
    /// record, never its source — D-75). Null for a table outside the family.
    /// The write core reads these from the stored row and overlays the change before <see cref="CheckWriteAsync"/>.
    /// </summary>
    public static IReadOnlyList<(string Column, string Table)>? GuardedLookups(string entityLogicalName)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName)
            || !CoreRecordLookups.TryGetValue(entityLogicalName.Trim(), out var lookups))
        {
            return null;
        }

        var columns = lookups.Select(l => (l.Column, l.CoreEntity)).ToList();
        if (IsExternalIdentifier(entityLogicalName))
            columns.Add((ExternalIdentifierInvoiceColumn, InvoiceEntity));
        if (IsInvoice(entityLogicalName))
            columns.Add((SupersedesInvoiceColumn, InvoiceEntity));
        if (IsLine(entityLogicalName))
            columns.Add((LineInvoiceColumn, InvoiceEntity));

        return columns;
    }

    /// <summary>
    /// The rule for a write (create or update) of an invoice-family row: <see cref="Check(string, Func{string, object?})"/>
    /// on the row AS IT WILL BE after the write, then, for an invoice that supersedes another, the superseded invoice must
    /// regard the SAME core record (<see cref="SupersedesCoreRecordMismatchCode"/> otherwise — never accepted because one
    /// side could not be decided); for a line that names its invoice, that invoice must regard the line's own core record
    /// (<see cref="LineInvoiceCoreRecordMismatchCode"/>, task 026). A failure to read the other invoice is
    /// <see cref="ReadFailedCode"/> or <see cref="RecordNotFoundCode"/>, never a match.
    /// </summary>
    /// <param name="entityLogicalName">The table written.</param>
    /// <param name="read">Each guarded lookup's value after the write (<see cref="GuardedLookups"/>; null = not set).</param>
    /// <param name="resolveSuperseded">Decides the other invoice's core record (the superseded invoice, or a line's
    /// invoice) from its stored row — read by the writer, as its caller; its refusal (a read fault included) is taken as
    /// given.</param>
    /// <param name="anyLineOfAnotherCoreRecord">D-96: for an UPDATE of a stored invoice that writes one of its core lookups
    /// (<see cref="WritesCoreLookup"/>), whether any line of that invoice names a core record other than the one given (or
    /// none) — <see cref="LinesOfAnotherCoreRecord"/>, read as the APP. True is
    /// <see cref="LineInvoiceCoreRecordMismatchCode"/>; a fault is <see cref="ReadFailedCode"/>. Null for every other write
    /// (a create has no lines yet). Lines are never re-filed from their invoice (D-75).</param>
    /// <param name="workAssignmentFiledUnderThisInvoice">For an UPDATE of a stored invoice that writes one of its core lookups,
    /// whether the work assignment the invoice would regard is itself filed under this invoice —
    /// <see cref="WorkAssignmentFiledUnderInvoice"/>, read as the APP. True is <see cref="WorkAssignmentCycleCode"/>; a fault is
    /// <see cref="ReadFailedCode"/>. Asked only when the invoice would regard a work assignment. Null for every other write (a
    /// new invoice has no work assignment filed under it yet).</param>
    public static async Task<InvoiceFamilyCoreRecord> CheckWriteAsync(
        string entityLogicalName,
        Func<string, object?> read,
        Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> resolveSuperseded,
        CancellationToken ct = default,
        Func<EntityReference, CancellationToken, Task<bool>>? anyLineOfAnotherCoreRecord = null,
        Func<EntityReference, CancellationToken, Task<bool>>? workAssignmentFiledUnderThisInvoice = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(resolveSuperseded);
        var decided = await CheckWriteAgainstOtherInvoiceAsync(entityLogicalName, read, resolveSuperseded, ct).ConfigureAwait(false);
        if (decided.IsRefused || !IsInvoice(decided.EntityLogicalName))
            return decided;

        if (workAssignmentFiledUnderThisInvoice is not null
            && string.Equals(decided.Regarding!.LogicalName, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
        {
            bool cycle;
            try
            {
                cycle = await workAssignmentFiledUnderThisInvoice(decided.Regarding, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return decided.Refuse(ReadFailedCode,
                    $"The work assignment's own filing could not be read ({ex.GetType().Name}); nothing was written.");
            }

            if (cycle)
            {
                return decided.Refuse(WorkAssignmentCycleCode,
                    "The invoice would be filed under a work assignment that is itself filed under this invoice, so neither "
                    + "would say which matter or project it belongs to. File the invoice under another matter, project or work "
                    + "assignment, or file the work assignment elsewhere first. Nothing was written.");
            }
        }

        if (anyLineOfAnotherCoreRecord is null)
            return decided;

        // D-96 (owner, on uac-r2's 026 re-check F1): an invoice re-filed to another core record must not leave its lines
        // behind. The count is the APP's, so a line the caller cannot see blocks too; the refusal is uniform and names no line.
        bool split;
        try
        {
            split = await anyLineOfAnotherCoreRecord(decided.Regarding!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return decided.Refuse(ReadFailedCode,
                $"The invoice's lines could not be read ({ex.GetType().Name}); nothing was written.");
        }

        return split
            ? decided.Refuse(LineInvoiceCoreRecordMismatchCode,
                $"{decided.EntityLogicalName} would regard {decided.Regarding!.LogicalName}({decided.Regarding.Id:D}) but at least "
                + "one of its lines names another core record, or none; a line belongs only to an invoice of its own matter, "
                + "project or work assignment, and lines are never re-filed from their invoice (D-75). Nothing was written. "
                + DetachMoveReattachHint)
            : decided;
    }

    /// <summary>
    /// D-96: true when a write of <paramref name="entityLogicalName"/> sets or clears one of its core-record lookups
    /// (<see cref="CoreRecordLookups"/>; a <c>@odata.bind</c> key counts as its column).
    /// </summary>
    public static bool WritesCoreLookup(string entityLogicalName, IEnumerable<string> writtenColumns)
    {
        ArgumentNullException.ThrowIfNull(writtenColumns);
        if (string.IsNullOrWhiteSpace(entityLogicalName)
            || !CoreRecordLookups.TryGetValue(entityLogicalName.Trim(), out var lookups))
        {
            return false;
        }

        return writtenColumns.Select(ColumnOfKey)
            .Any(c => lookups.Any(l => string.Equals(l.Column, c, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// D-96: the line check for an update of invoice <paramref name="invoiceId"/>, read with the APP-only
    /// <paramref name="appOnly"/> (owner: count lines the caller cannot see). With no reader it fails closed: the check
    /// throws, which <see cref="CheckWriteAsync"/> refuses as <see cref="ReadFailedCode"/>.
    /// </summary>
    public static Func<EntityReference, CancellationToken, Task<bool>> LinesOfAnotherCoreRecord(
        IGenericEntityService? appOnly, Guid invoiceId) =>
        appOnly is null
            ? (_, _) => throw new InvalidOperationException("No app-only reader for the invoice's lines.")
            : async (core, ct) =>
                (await appOnly.RetrieveMultipleAsync(LinesOfAnotherCoreRecordQuery(invoiceId, core), ct).ConfigureAwait(false))
                    .Entities.Count > 0;

    /// <summary>
    /// The work-assignment cycle check for an update of invoice <paramref name="invoiceId"/>: reads the work assignment's
    /// <see cref="WorkAssignmentInvoiceColumn"/> with the APP-only <paramref name="appOnly"/> (a work assignment the caller cannot
    /// see counts too) and answers whether it names this invoice. With no reader, or a work assignment that cannot be found, the
    /// check throws, which <see cref="CheckWriteAsync"/> refuses as <see cref="ReadFailedCode"/> (fail closed).
    /// </summary>
    public static Func<EntityReference, CancellationToken, Task<bool>> WorkAssignmentFiledUnderInvoice(
        IGenericEntityService? appOnly, Guid invoiceId) =>
        appOnly is null
            ? (_, _) => throw new InvalidOperationException("No app-only reader for the work assignment.")
            : async (workAssignment, ct) =>
            {
                var row = await appOnly.RetrieveAsync(WorkAssignmentEntity, workAssignment.Id, [WorkAssignmentInvoiceColumn], ct)
                              .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("The work assignment was not found.");
                return row.GetAttributeValue<EntityReference>(WorkAssignmentInvoiceColumn) is { } filedUnder
                       && filedUnder.Id == invoiceId;
            };

    /// <summary>
    /// D-96: one bounded query — the first line of <paramref name="invoiceId"/> whose own core lookups are not exactly
    /// <paramref name="core"/>: its column for that core type is another record or empty, or one of its other core lookups
    /// is set. Never selects more than the line's id.
    /// </summary>
    internal static QueryExpression LinesOfAnotherCoreRecordQuery(Guid invoiceId, EntityReference core)
    {
        ArgumentNullException.ThrowIfNull(core);
        var lookups = CoreRecordLookups[LineEntity];
        var own = lookups.FirstOrDefault(l => string.Equals(l.CoreEntity, core.LogicalName, StringComparison.OrdinalIgnoreCase));
        if (own.Column is null)
            throw new ArgumentException($"'{core.LogicalName}' is not a core record a line can name.", nameof(core));

        var differs = new FilterExpression(LogicalOperator.Or);
        differs.AddCondition(own.Column, ConditionOperator.NotEqual, core.Id);
        differs.AddCondition(own.Column, ConditionOperator.Null);
        foreach (var other in lookups.Where(l => !string.Equals(l.Column, own.Column, StringComparison.OrdinalIgnoreCase)))
            differs.AddCondition(other.Column, ConditionOperator.NotNull);

        var query = new QueryExpression(LineEntity) { ColumnSet = new ColumnSet(LineEntity + "id"), TopCount = 1 };
        query.Criteria.AddCondition(LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        query.Criteria.AddFilter(differs);
        return query;
    }

    /// <summary>
    /// DEF-022 (#2056; unified-access-control-r2 direction): the guard for the generic update paths that do not write
    /// through <c>OwnedChildWrite</c> — the playbook update (<c>UpdateRecordActionCore</c>), the output orchestrator
    /// (<c>DataverseUpdateHandler</c>) and the field-mapping push. For an update of any invoice-family table (H1, 2026-11-13:
    /// all six, not <c>sprk_invoice</c> and <c>sprk_billingevent</c> only) the stored row is read first and a missing row is
    /// refused; for one whose patch sets or clears a guarded lookup (<see cref="GuardedLookups"/>), the stored
    /// lookups are read with <paramref name="appOnly"/>, overlaid with the patch, and decided by
    /// <see cref="CheckWriteAsync"/> — including D-96's line check when an invoice's core lookup is written. The other
    /// invoice (superseded, or a line's) is read the same way. Null when the write may proceed (or is not checked here).
    /// </summary>
    /// <param name="patch">The update payload as the path writes it: a column → <see cref="EntityReference"/> or null, or a
    /// <c>{navigation}@odata.bind</c> → <c>/{set}({id})</c> or null. Any other value of a guarded column is
    /// <see cref="LookupNotACoreRecordCode"/>.</param>
    /// <param name="appOnly">The path's app-only reader; null fails closed (<see cref="ReadFailedCode"/>) when a check is due.</param>
    /// <param name="writeIsImpersonated">The write will run AS a Dataverse user. The guard reads as the APP, before any check
    /// of that user's access, so "<see cref="RecordNotFoundCode"/>" here versus the later privilege error would tell the
    /// caller whether a GUID names a row they cannot read. With this set, a row (or other invoice) that is not found is
    /// <see cref="ReadFailedCode"/>, the same answer as a row that cannot be read (#2081 fix round 2).</param>
    public static async Task<InvoiceFamilyCoreRecord?> CheckGenericUpdateAsync(
        string entityLogicalName,
        Guid recordId,
        IEnumerable<KeyValuePair<string, object?>> patch,
        IGenericEntityService? appOnly,
        CancellationToken ct = default,
        bool writeIsImpersonated = false)
    {
        var decided = await CheckGenericUpdateCoreAsync(entityLogicalName, recordId, patch, appOnly, ct).ConfigureAwait(false);
        return writeIsImpersonated && decided is { RefusalCode: RecordNotFoundCode }
            ? decided.Refuse(ReadFailedCode, $"What this {decided.EntityLogicalName} is filed under could not be read; nothing was written.")
            : decided;
    }

    /// <summary>
    /// Task 045d (owner ruling Q8 (a); owner, 2026-11-12, option (b)): the app-only update half — the playbook update, the output
    /// orchestrator and the field-mapping push. For an update of an invoice-family row whose patch sets or clears one of the
    /// row's own core lookups (<see cref="ColumnsToRead"/>), the stored lookups are read with <paramref name="appOnly"/>,
    /// overlaid with the patch, decided by <see cref="Check(string, Func{string, object?})"/>, and the resolver fields of the
    /// record the row will name are derived (<see cref="DeriveGenericResolverFieldsAsync"/>). The path writes them in
    /// the same write (<see cref="InvoiceFamilyCoreRecord.ResolverFields"/>, <see cref="InvoiceFamilyResolverFields.ToGenericFields"/>),
    /// or answers a refusal through the refusal form it uses for <see cref="CheckGenericUpdateAsync"/>.
    /// Null when nothing is derived: a table outside the family, a patch that moves no core lookup, a stored row that does not
    /// exist (the path answers it as any missing row), or a patch that leaves the row naming the record it already names
    /// (the same table and id: the row keeps the fields it has, as the pair planner keeps a pair that does not move).
    /// A row <see cref="Check(string, Func{string, object?})"/> refuses here is refused with its code: the path's guard ran
    /// before this and admitted it, so the row changed in between, and the write must not move the core with stale fields.
    /// </summary>
    /// <param name="patch">As <see cref="CheckGenericUpdateAsync"/>'s: a column → <see cref="EntityReference"/> or null, or a
    /// <c>{navigation}@odata.bind</c> → <c>/{set}({id})</c> or null.</param>
    /// <param name="appOnly">The path's app-only reader of the stored row and the catalog row; null fails closed
    /// (<see cref="ReadFailedCode"/>) when a derive is due.</param>
    /// <param name="records">The reader of the regarded record's name and number, as the path reads display values: the push's
    /// caller, or the person a handler's run acts as (<c>OwnedChildWrite.CallerDisplayRecords</c>). Required, so no path falls
    /// back to an app-only read of them.</param>
    /// <param name="writeIsImpersonated">As <see cref="CheckGenericUpdateAsync"/>'s: a record not found is answered
    /// <see cref="ReadFailedCode"/>, the path's answer for a missing record when the write runs as a user.</param>
    public static async Task<InvoiceFamilyCoreRecord?> DeriveForGenericUpdateAsync(
        string entityLogicalName,
        Guid recordId,
        IEnumerable<KeyValuePair<string, object?>> patch,
        IGenericEntityService? appOnly,
        RecordLookup records,
        CancellationToken ct = default,
        bool writeIsImpersonated = false)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(patch);
        var entity = entityLogicalName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (ColumnsToRead(entity) is not { } coreColumns)
            return null;

        var targets = CoreRecordLookups[entity].Select(l => (l.Column, Table: l.CoreEntity)).ToList();
        if (IsExternalIdentifier(entity))
            targets.Add((ExternalIdentifierInvoiceColumn, InvoiceEntity));
        var change = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in patch)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = ColumnOfKey(key);
            var target = targets.FirstOrDefault(t => string.Equals(t.Column, column, StringComparison.OrdinalIgnoreCase));
            if (target.Column is null)
                continue;

            change[target.Column] = value is string bind && IsBindKey(key) && TryParseBind(bind, out var id)
                ? new EntityReference(target.Table, id)
                : value;
        }

        if (change.Count == 0)
            return null;

        InvoiceFamilyCoreRecord derived;
        if (appOnly is null)
        {
            derived = InvoiceFamilyCoreRecord.Refused(entity, ReadFailedCode,
                $"What this {entity} is filed under cannot be read here; nothing was written.");
        }
        else
        {
            // The stored row: derived from only when it exists. A row that does not exist is left to the path, which answers it
            // as it answers any missing row (the guard before this, or the update-only write after it); a read that faults
            // fails closed.
            Entity? stored;
            try
            {
                stored = await appOnly.RetrieveAsync(entity, recordId, coreColumns.ToArray(), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (IsRecordNotFound(ex))
                    return null;
                return InvoiceFamilyCoreRecord.Refused(entity, ReadFailedCode,
                    $"What this {entity} is filed under could not be read ({ex.GetType().Name}); nothing was written.");
            }

            if (stored is null)
                return null;

            var decided = Check(entity, column =>
                change.TryGetValue(column, out var changed) ? changed
                : stored.Attributes.TryGetValue(column, out var kept) ? kept
                : null);
            if (decided.IsRefused)
                return NotFoundAs(decided, writeIsImpersonated);
            if (NamesTheSameRecord(decided, Check(entity, column => stored.Attributes.TryGetValue(column, out var kept) ? kept : null)))
                return null;

            derived = await DeriveGenericResolverFieldsAsync(decided, records, AppOnlyRecordTypes(appOnly), ct).ConfigureAwait(false);
        }

        return NotFoundAs(derived, writeIsImpersonated);
    }

    /// <summary>
    /// True when <paramref name="after"/> and <paramref name="before"/> are both decided and name the same record (table and
    /// id): a write that re-names the record a row already names moves nothing, so nothing is derived for it. Pure.
    /// </summary>
    public static bool NamesTheSameRecord(InvoiceFamilyCoreRecord after, InvoiceFamilyCoreRecord before)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(before);
        return !after.IsRefused && !before.IsRefused
               && after.Regarding is { } next && before.Regarding is { } kept
               && string.Equals(next.LogicalName, kept.LogicalName, StringComparison.OrdinalIgnoreCase)
               && next.Id == kept.Id;
    }

    /// <summary>
    /// Task 045e (owner, 2026-11-13, #2772 "Refuse it"; FR-23 amended for the invoice family): the ONE agreement check between
    /// an invoice-family row's regarding pair and its core record, asked by every generic writer of a family row — the create
    /// (step 5½) and the caller update paths through <c>OwnedChildWrite.DecideInvoiceFamilyAsync</c>, and the playbook update, the
    /// output orchestrator and the field-mapping push through <see cref="CheckGenericUpdateAsync"/>. Null when the write may
    /// proceed; otherwise the refusal, and nothing is written.
    /// <list type="bullet">
    /// <item>Only the halves the write sends are checked; a write that sends neither is not checked and reads nothing.</item>
    /// <item>The id agrees when it is the id of <paramref name="after"/>'s record. The type agrees when it is the active
    /// record-type catalog row of that record's table (the row the server's derive writes), read through
    /// <paramref name="recordTypes"/>; the catalog is read only when the id (if sent) agrees.</item>
    /// <item>A half that names no record (a clear, a blank) disagrees on an update: the row would be left without the pair of its
    /// core record. On a create it is not checked: the server writes the pair from the core record (step 5½).</item>
    /// <item>An update that moves the core record (writes a core lookup, and the row then names another record than it did) is
    /// not checked when <paramref name="aCoreMoveIsChecked"/> is false: the caller (the app-only gate,
    /// <see cref="KeepRegardingPairAgreeingAsync"/>) removes the pair, and 045d's derive writes the one of the record the row
    /// will name. The caller update paths run the derive BEFORE this check, so they pass true: the pair as it will be written
    /// (the derived one, or the one sent when the derive found nothing to move) must name the record the row will name. An
    /// update that re-names the record the row already names is checked like any other.</item>
    /// <item>A row that names no single core record after the write is refused with the guard's own answer for it.</item>
    /// <item>Fail closed: no catalog reader, or a catalog read that faults, is <see cref="ReadFailedCode"/> (the fault's own
    /// text stays out of the reason); a core table with no active catalog row is <see cref="ResolverFieldsIncompleteCode"/>.
    /// The caller's own cancellation is rethrown.</item>
    /// </list>
    /// </summary>
    /// <param name="entityLogicalName">The table written.</param>
    /// <param name="writtenColumns">The columns (or <c>{nav}@odata.bind</c> keys) the write sets or clears.</param>
    /// <param name="pair">What the write sends of the pair (<see cref="RegardingPairWrite"/>).</param>
    /// <param name="after"><see cref="Check(string, Func{string, object?})"/> on the row as it will be after the write.</param>
    /// <param name="before"><see cref="Check(string, Func{string, object?})"/> on the stored row; null on a create.</param>
    /// <param name="recordTypes">The APP-ONLY catalog reader (the row of a table); null fails closed when the type is checked.</param>
    /// <param name="aCoreMoveIsChecked">True on a path whose derive has already run (the caller update paths).</param>
    public static async Task<InvoiceFamilyCoreRecord?> CheckRegardingPairAgreesAsync(
        string entityLogicalName,
        IEnumerable<string> writtenColumns,
        RegardingPairWrite pair,
        InvoiceFamilyCoreRecord after,
        InvoiceFamilyCoreRecord? before,
        RecordTypeLookup? recordTypes,
        CancellationToken ct = default,
        bool aCoreMoveIsChecked = false)
    {
        ArgumentNullException.ThrowIfNull(writtenColumns);
        ArgumentNullException.ThrowIfNull(after);
        if (!pair.DecidesAnything || ColumnsToRead(entityLogicalName) is not { } coreColumns)
            return null;

        // A row that will name no single core record is refused with the guard's own answer — never exempted as a move.
        if (after.IsRefused)
            return after;

        var create = before is null;
        if (!create && !aCoreMoveIsChecked
            && writtenColumns.Select(ColumnOfKey).Any(c => coreColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            && !NamesTheSameRecord(after, before!))
        {
            return null; // the core record moves: 045d's derive writes the pair of the record the row will name
        }
        if (after.Regarding is not { } regarding || pair.Unreadable)
            return Disagrees(after);

        if (pair.SetsId && !(create && string.IsNullOrWhiteSpace(pair.IdText))
            && !(Guid.TryParse(pair.IdText?.Trim(), out var id) && id == regarding.Id))
        {
            return Disagrees(after);
        }

        if (pair.SetsType && !(create && pair.TypeId is null))
        {
            if (pair.TypeId is not { } typeId || typeId == Guid.Empty)
                return Disagrees(after);
            if (recordTypes is null)
            {
                return after.Refuse(ReadFailedCode,
                    $"The record-type row for {regarding.LogicalName} cannot be read here; nothing was written.");
            }

            Entity? catalogRow;
            try
            {
                catalogRow = await recordTypes(regarding.LogicalName, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return after.Refuse(ReadFailedCode,
                    $"The record-type row for {regarding.LogicalName} could not be read ({ex.GetType().Name}); nothing was written.");
            }

            if (catalogRow is null || catalogRow.Id == Guid.Empty)
            {
                return after.Refuse(ResolverFieldsIncompleteCode,
                    $"{regarding.LogicalName} has no active sprk_recordtype_ref row, so the regarding record's type cannot be checked. "
                    + "Nothing was written.");
            }

            if (catalogRow.Id != typeId)
                return Disagrees(after);
        }

        // Fix round 3 (ebilling-r1, 2026-11-13): a record URL an update sends names the same record — its etn and id, decided
        // without a read, after the pair's own halves. A clear names no other record; on a create the derive writes the URL.
        if (pair.SetsUrl && !create && !string.IsNullOrWhiteSpace(pair.UrlText)
            && !RegardingPairWrite.UrlNamesRecord(pair.UrlText, regarding.LogicalName, regarding.Id))
        {
            return Disagrees(after);
        }

        return null;

        static InvoiceFamilyCoreRecord Disagrees(InvoiceFamilyCoreRecord decided) =>
            decided.Refuse(RegardingPairCoreRecordMismatchCode, RegardingPairCoreRecordMismatchReason);
    }

    /// <summary>A record not found, answered <see cref="ReadFailedCode"/> when the write runs as a user (#2081 fix round 2).</summary>
    private static InvoiceFamilyCoreRecord NotFoundAs(InvoiceFamilyCoreRecord derived, bool writeIsImpersonated) =>
        writeIsImpersonated && derived is { RefusalCode: RecordNotFoundCode }
            ? derived.Refuse(ReadFailedCode, $"What this {derived.EntityLogicalName} is filed under could not be read; nothing was written.")
            : derived;

    private static async Task<InvoiceFamilyCoreRecord?> CheckGenericUpdateCoreAsync(
        string entityLogicalName,
        Guid recordId,
        IEnumerable<KeyValuePair<string, object?>> patch,
        IGenericEntityService? appOnly,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var entity = entityLogicalName?.Trim().ToLowerInvariant() ?? string.Empty;
        // H1 (ebilling-r1 + unified-access-control-r2 agreed 2026-11-13): every invoice-family table, not the invoice and the
        // line only — the same rule (one core record; D-96 for an invoice) and the same stored-row read before deciding.
        if (!CoreRecordLookups.ContainsKey(entity))
            return null;

        var guarded = GuardedLookups(entity)!;
        var change = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in patch)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = ColumnOfKey(key);
            var target = guarded.FirstOrDefault(g => string.Equals(g.Column, column, StringComparison.OrdinalIgnoreCase));
            if (target.Column is null)
                continue;

            change[target.Column] = value is string bind && IsBindKey(key) && TryParseBind(bind, out var id)
                ? new EntityReference(target.Table, id)
                : value; // null clears; an EntityReference is checked against the column's table; anything else is refused
        }

        // #2081 (ebilling-r1 follow-up to uac-r2's 026 review): these paths PATCH with no If-Match, which is an UPSERT, so a
        // stale or deleted id would CREATE an orphan row (no core record, owned by the app instead of the root's team). The
        // stored row is therefore read for EVERY write of a family row (H1), whether or not the patch touches a guarded
        // lookup, and a missing row is refused. No reader, or a read that faults, fails closed.
        if (appOnly is null)
        {
            return InvoiceFamilyCoreRecord.Refused(entity, ReadFailedCode,
                $"What this {entity} is filed under cannot be read here; nothing was written.");
        }

        Entity stored;
        try
        {
            stored = await appOnly.RetrieveAsync(entity, recordId, guarded.Select(g => g.Column).ToArray(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return IsRecordNotFound(ex)
                ? InvoiceFamilyCoreRecord.Refused(entity, RecordNotFoundCode, $"{entity}({recordId:D}) does not exist; nothing was written.")
                : InvoiceFamilyCoreRecord.Refused(entity, ReadFailedCode,
                    $"What this {entity} is filed under could not be read ({ex.GetType().Name}); nothing was written.");
        }

        if (stored is null)
            return InvoiceFamilyCoreRecord.Refused(entity, RecordNotFoundCode, $"{entity}({recordId:D}) does not exist; nothing was written.");

        // Task 045e: the regarding pair is decided later, on the payload as it will be written (after the pair planner), by
        // KeepRegardingPairAgreeingAsync — the path's derive step calls it — not here on the payload as sent.
        if (change.Count == 0)
            return null; // the row exists and the patch moves no guarded lookup: nothing more to decide

        object? Effective(string column) =>
            change.TryGetValue(column, out var changed) ? changed
            : stored.Attributes.TryGetValue(column, out var kept) ? kept
            : null;

        var decided = await CheckWriteAsync(
            entity,
            Effective,
            async (invoice, token) =>
            {
                Entity other;
                try
                {
                    other = await appOnly.RetrieveAsync(invoice.LogicalName, invoice.Id,
                        CoreRecordLookups[invoice.LogicalName].Select(l => l.Column).ToArray(), token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsRecordNotFound(ex))
                {
                    return InvoiceFamilyCoreRecord.Refused(invoice.LogicalName, RecordNotFoundCode,
                        "The other invoice was not found; nothing was written.");
                }

                return other is null
                    ? InvoiceFamilyCoreRecord.Refused(invoice.LogicalName, RecordNotFoundCode, "The other invoice was not found; nothing was written.")
                    : Check(invoice.LogicalName, column => other.Attributes.TryGetValue(column, out var value) ? value : null);
            },
            ct,
            IsInvoice(entity) && WritesCoreLookup(entity, change.Keys) ? LinesOfAnotherCoreRecord(appOnly, recordId) : null,
            IsInvoice(entity) && WritesCoreLookup(entity, change.Keys) ? WorkAssignmentFiledUnderInvoice(appOnly, recordId) : null)
            .ConfigureAwait(false);

        return decided.IsRefused ? decided : null;
    }

    /// <summary>
    /// Task 045e (fix round 1, K1/K2/F2): the app-only update paths' regarding-pair gate, on the payload as it will be written —
    /// after the 045b planner (so a pair the planner writes is decided too) and before 045d's derive and the write. Every
    /// app-only path (playbook update, output orchestrator, field-mapping push) reaches it through its derive step
    /// (<c>OwnedChildWrite.KeepInvoiceFamilyResolverFieldsAppOnlyAsync</c>), whose refusal form it answers through.
    /// <list type="bullet">
    /// <item>A payload that sends no pair or resolver column, or a table outside the family: unchanged, nothing read.</item>
    /// <item>A payload that moves the row's core record (writes a core lookup, and the row then names another record than its
    /// stored one): every pair and resolver column is REMOVED, so only 045d's derive writes them — the pair of the record the
    /// row will name. A derive that then finds nothing to derive (the core was already moved) leaves the stored pair.</item>
    /// <item>Otherwise <see cref="CheckRegardingPairAgreesAsync"/> on the stored row (read app-only) overlaid with the payload;
    /// when it admits a pair id, the id is written as the core record's id in canonical form (lower-case "D"), whatever text
    /// was sent, so the D-68 alternate key never takes another spelling of the same id.</item>
    /// <item>Fail closed: no reader, a read that faults, or a missing row writes nothing (the guard's codes).</item>
    /// </list>
    /// </summary>
    public static async Task<(Dictionary<string, object?> Payload, InvoiceFamilyCoreRecord? Refusal)> KeepRegardingPairAgreeingAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> payload,
        IGenericEntityService? appOnly,
        CancellationToken ct,
        bool writeIsImpersonated = false)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var entity = entityLogicalName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CoreRecordLookups.ContainsKey(entity)
            || !payload.Keys.Any(k => !string.IsNullOrWhiteSpace(k)
                                      && Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping.RegardingPairAndResolverColumns
                                          .Contains(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(k))))
        {
            return (payload, null);
        }

        if (appOnly is null)
            return (payload, Refused(entity, ReadFailedCode, $"What this {entity} is filed under cannot be read here; nothing was written."));

        var guarded = GuardedLookups(entity)!;
        Entity? stored;
        try
        {
            stored = await appOnly.RetrieveAsync(entity, recordId, guarded.Select(g => g.Column).ToArray(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (payload, IsRecordNotFound(ex)
                ? NotFoundAs(Refused(entity, RecordNotFoundCode, $"{entity}({recordId:D}) does not exist; nothing was written."), writeIsImpersonated)
                : Refused(entity, ReadFailedCode, $"What this {entity} is filed under could not be read ({ex.GetType().Name}); nothing was written."));
        }

        if (stored is null)
            return (payload, NotFoundAs(Refused(entity, RecordNotFoundCode, $"{entity}({recordId:D}) does not exist; nothing was written."), writeIsImpersonated));

        var change = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in payload)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var target = guarded.FirstOrDefault(g => string.Equals(g.Column, ColumnOfKey(key), StringComparison.OrdinalIgnoreCase));
            if (target.Column is not null)
            {
                change[target.Column] = value is string bind && IsBindKey(key) && TryParseBind(bind, out var id)
                    ? new EntityReference(target.Table, id)
                    : value;
            }
        }

        var before = Check(entity, column => stored.Attributes.TryGetValue(column, out var kept) ? kept : null);
        var after = Check(entity, column =>
            change.TryGetValue(column, out var changed) ? changed
            : stored.Attributes.TryGetValue(column, out var kept) ? kept
            : null);

        if (ColumnsToRead(entity) is { } coreColumns
            && change.Keys.Any(c => coreColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            && !after.IsRefused && !NamesTheSameRecord(after, before))
        {
            // K1: the core record moves, so the pair and its display are the derive's alone.
            var dropped = new Dictionary<string, object?>(payload.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in payload)
            {
                if (!Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping.RegardingPairAndResolverColumns
                        .Contains(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key)))
                {
                    dropped[key] = value;
                }
            }

            return (dropped, null);
        }

        var pair = RegardingPairWrite.Of(entity, payload);
        if (!pair.DecidesAnything)
            return (payload, null); // a name or number only: decided by no read here (known limit)

        var disagrees = await CheckRegardingPairAgreesAsync(entity, change.Keys, pair, after, before, AppOnlyRecordTypes(appOnly), ct)
            .ConfigureAwait(false);
        if (disagrees is not null)
            return (payload, disagrees);

        return (WithCanonicalPairId(payload, after), null);

        static InvoiceFamilyCoreRecord Refused(string table, string code, string reason) =>
            InvoiceFamilyCoreRecord.Refused(table, code, reason);
    }

    /// <summary>
    /// Task 045e (fix round 1, F2): <paramref name="payload"/> with every text value under the pair id column replaced by
    /// <paramref name="admitted"/>'s record id in canonical form (lower-case "D"). Call only after
    /// <see cref="CheckRegardingPairAgreesAsync"/> admitted the pair, so the text named that same id. Fix round 4: a sent record
    /// URL (admitted, so it named that same record) is written in canonical form too (<see cref="CanonicalRecordUrl"/>).
    /// </summary>
    internal static Dictionary<string, object?> WithCanonicalPairId(Dictionary<string, object?> payload, InvoiceFamilyCoreRecord admitted)
    {
        if (admitted.Regarding is not { } regarding)
            return payload;
        var canonical = CanonicalPairId(regarding.Id);
        var canonicalUrl = CanonicalRecordUrl(regarding.LogicalName, regarding.Id);
        var written = new Dictionary<string, object?>(payload, payload.Comparer);
        foreach (var key in payload.Keys)
        {
            var column = Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key);
            // Every value the pair view reads as an id (OwnedChildWrite.PairChangeOf): text, a Guid, or a JSON string (fix round 3).
            if (string.Equals(column, Sprk.Bff.Api.Services.Access.SecureRootInheritance.PairIdColumn, StringComparison.Ordinal)
                && payload[key] switch
                {
                    string text => !string.IsNullOrWhiteSpace(text),
                    Guid => true,
                    System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => !string.IsNullOrWhiteSpace(element.GetString()),
                    _ => false,
                })
            {
                written[key] = canonical;
            }
            else if (string.Equals(column, RegardingPairWrite.RecordUrlColumn, StringComparison.OrdinalIgnoreCase)
                && payload[key] switch
                {
                    string text => !string.IsNullOrWhiteSpace(text),
                    System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => !string.IsNullOrWhiteSpace(element.GetString()),
                    _ => false,
                })
            {
                written[key] = canonicalUrl;
            }
        }

        return written;
    }

    /// <summary>
    /// Task 045e (fix round 4): a record URL as the server writes it — <c>RegardingRecordType.BuildRecordUrl</c> of the table
    /// (lower case) and the canonical id (<see cref="CanonicalPairId"/>), the form 045d's derive writes.
    /// </summary>
    public static string CanonicalRecordUrl(string table, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return RegardingRecordType.BuildRecordUrl(table.Trim().ToLowerInvariant(), CanonicalPairId(id));
    }

    /// <summary>Task 045e: a pair id as the server writes it — lower-case "D", the form 045d's derive writes.</summary>
    public static string CanonicalPairId(Guid id) => id.ToString("D").ToLowerInvariant();

    /// <summary>
    /// The refusal text the generic update paths surface (task 026 fix-round-2): the stable code and a fixed sentence, never
    /// <see cref="InvoiceFamilyCoreRecord.Reason"/> — those paths read the stored row and the other invoice as the APP, so
    /// the reason can name a record or core record the caller has not read.
    /// </summary>
    public static string CodeOnlyRefusalMessage(string refusalCode) =>
        $"The invoice-family core-record rule refused this write ({refusalCode}); nothing was written."
        + (refusalCode == LineInvoiceCoreRecordMismatchCode ? " " + DetachMoveReattachHint : string.Empty);

    /// <summary>
    /// The supported sequence for moving an invoice that has lines, or a line of one (#2081 follow-up; D-75, D-96), shown with
    /// <see cref="LineInvoiceCoreRecordMismatchCode"/>. A line is never re-filed from its invoice, and the invoice and its
    /// lines must always regard one core record, so neither can move first while they are attached. A line with no invoice is
    /// decided on its own lookup alone, which is what makes the sequence possible. Fixed text: it names no record and no user data.
    /// </summary>
    public const string DetachMoveReattachHint =
        "To move an invoice that has lines, or a line of one, detach each line (clear its invoice), move the invoice, move each line to "
        + "the same matter, project or work assignment, then re-attach the lines to the invoice.";

    /// <summary>
    /// True for the tables the generic update paths write UPDATE-ONLY (<c>If-Match: *</c>): the guard checked that the row
    /// exists, and the write itself must not re-create it if it was deleted since (#2081 fix round 2). The tables
    /// <see cref="CheckGenericUpdateAsync"/> covers: every invoice-family table (H1, 2026-11-13); every other table keeps its
    /// write unchanged.
    /// </summary>
    public static bool IsUpdateOnlyTarget(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && CoreRecordLookups.ContainsKey(entityLogicalName.Trim());

    /// <summary>
    /// The update-only write of an invoice or line. Null when written. A row that is gone now (the platform refused the
    /// precondition) writes nothing and answers <see cref="RecordNotFoundCode"/>, or <see cref="ReadFailedCode"/> when
    /// <paramref name="notFoundAsReadFailed"/> (the caller's id did not come from a query the user read, so "not found" would
    /// tell them whether it names a row they cannot read — the reasoning of <c>writeIsImpersonated</c> in
    /// <see cref="CheckGenericUpdateAsync"/>). Any other fault propagates.
    /// </summary>
    public static async Task<string?> UpdateExistingAsync(
        IFieldMappingDataverseService records,
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        Guid? impersonateSystemUserId,
        bool notFoundAsReadFailed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(records);
        try
        {
            await records.UpdateExistingRecordFieldsAsync(entityLogicalName, recordId, fields, impersonateSystemUserId, ct)
                .ConfigureAwait(false);
            return null;
        }
        catch (KeyNotFoundException)
        {
            return notFoundAsReadFailed ? ReadFailedCode : RecordNotFoundCode;
        }
    }

    /// <summary>The generic update paths' refusal on the existing "throw" contract (<see cref="CodeOnlyRefusalMessage"/>).</summary>
    public static RecordOwnerUnresolvedException RefusalException(string entityLogicalName, string refusalCode) =>
        new(entityLogicalName, refusalCode, CodeOnlyRefusalMessage(refusalCode));

    private const string BindSuffix = "@odata.bind";

    private static bool IsBindKey(string key) => key.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase);

    private static string ColumnOfKey(string key) =>
        (IsBindKey(key) ? key[..^BindSuffix.Length] : key).Trim().ToLowerInvariant();

    /// <summary>The id in a Web API bind (<c>/sprk_matters(…)</c> or <c>sprk_matters(…)</c>).</summary>
    private static bool TryParseBind(string bind, out Guid id)
    {
        id = Guid.Empty;
        var open = bind.LastIndexOf('(');
        var close = bind.LastIndexOf(')');
        return open >= 0 && close == bind.Length - 1 && close > open + 1
               && Guid.TryParse(bind.AsSpan(open + 1, close - open - 1), out id);
    }

    /// <summary>
    /// <see cref="Check(string, Func{string, object?})"/>, then the comparison with the one other invoice a write names:
    /// the invoice an invoice supersedes, or the invoice a line belongs to.
    /// </summary>
    private static async Task<InvoiceFamilyCoreRecord> CheckWriteAgainstOtherInvoiceAsync(
        string entityLogicalName,
        Func<string, object?> read,
        Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> resolveSuperseded,
        CancellationToken ct)
    {
        var decided = Check(entityLogicalName, read);
        if (decided.IsRefused)
            return decided;

        // The one other invoice a write is compared with: the invoice an invoice supersedes, or the invoice a line belongs
        // to (task 026). Every other family table has none.
        var (column, mismatchCode, relation, rule) = IsInvoice(decided.EntityLogicalName)
            ? (SupersedesInvoiceColumn, SupersedesCoreRecordMismatchCode, "the invoice it supersedes",
                "an invoice supersedes only an invoice of the same matter, project or work assignment")
            : IsLine(decided.EntityLogicalName)
                ? (LineInvoiceColumn, LineInvoiceCoreRecordMismatchCode, "the invoice it belongs to",
                    "a line belongs only to an invoice of its own matter, project or work assignment")
                : ((string?)null, (string?)null, (string?)null, (string?)null);
        if (column is null || mismatchCode is null || relation is null || rule is null)
            return decided;

        if (!TryReadReference(read(column), InvoiceEntity, out var invoice))
        {
            return decided.Refuse(LookupNotACoreRecordCode,
                $"{decided.EntityLogicalName}.{column} must be a lookup to {InvoiceEntity}; the value given is not.");
        }

        if (invoice is null)
            return decided;

        InvoiceFamilyCoreRecord other;
        try
        {
            other = await resolveSuperseded(invoice, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The reason reaches the caller (a 500's detail): the fault's own text stays out of it.
            return decided.Refuse(ReadFailedCode,
                $"The {relation["the ".Length..]} could not be read ({ex.GetType().Name}); nothing was written.");
        }

        if (other.RefusalCode is ReadFailedCode or RecordNotFoundCode)
            return decided.Refuse(other.RefusalCode, other.Reason ?? $"The {relation["the ".Length..]} could not be read; nothing was written.");

        var own = decided.Regarding!; // decided, so it names its record
        if (other.IsRefused || other.Regarding is not { } otherCore
            || !string.Equals(otherCore.LogicalName, own.LogicalName, StringComparison.OrdinalIgnoreCase)
            || otherCore.Id != own.Id)
        {
            return decided.Refuse(mismatchCode,
                $"{decided.EntityLogicalName} regards {own.LogicalName}({own.Id:D}) but {relation} "
                + (other.Regarding is { } r && !other.IsRefused
                    ? $"regards {r.LogicalName}({r.Id:D})"
                    : $"names no single core record ({other.RefusalCode})")
                + $"; {rule}. Nothing was written."
                + (mismatchCode == LineInvoiceCoreRecordMismatchCode ? " " + DetachMoveReattachHint : string.Empty));
        }

        return decided;
    }

    /// <summary>Decide the record an SDK payload (or a stored row) regards. Its <see cref="Entity.LogicalName"/> names the table.</summary>
    public static InvoiceFamilyCoreRecord Check(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Check(row.LogicalName, column => row.Attributes.TryGetValue(column, out var value) ? value : null);
    }

    /// <summary>
    /// Decide the record a row of <paramref name="entityLogicalName"/> regards, reading each lookup's value through
    /// <paramref name="read"/> (null = not set). The value must be an <see cref="EntityReference"/> or null.
    /// </summary>
    public static InvoiceFamilyCoreRecord Check(string entityLogicalName, Func<string, object?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var entity = entityLogicalName?.Trim() ?? string.Empty;
        if (!CoreRecordLookups.TryGetValue(entity, out var lookups))
        {
            return InvoiceFamilyCoreRecord.Refused(entity, UnknownEntityCode,
                $"'{entity}' is not an invoice-family table; FR-05 covers {string.Join(", ", CoreRecordLookups.Keys)}.");
        }

        entity = entity.ToLowerInvariant();
        var set = new List<(string Column, EntityReference Reference)>();
        foreach (var (column, core) in lookups)
        {
            if (!TryReadReference(read(column), core, out var reference))
            {
                return InvoiceFamilyCoreRecord.Refused(entity, LookupNotACoreRecordCode,
                    $"{entity}.{column} must be a lookup to {core}; the value given is not.");
            }

            if (reference is not null)
            {
                set.Add((column, reference));
            }
        }

        if (set.Count > 1)
        {
            return InvoiceFamilyCoreRecord.Refused(entity, MultipleCoreRecordsCode,
                $"{entity} names {set.Count} core records ({string.Join(", ", set.Select(s => s.Column))}); exactly one "
                + "matter, project or work assignment is allowed (FR-05). Nothing was written.");
        }

        if (set.Count == 1)
        {
            return new InvoiceFamilyCoreRecord
            {
                EntityLogicalName = entity,
                Regarding = set[0].Reference,
                RegardingColumn = set[0].Column,
            };
        }

        if (IsExternalIdentifier(entity))
        {
            if (!TryReadReference(read(ExternalIdentifierInvoiceColumn), InvoiceEntity, out var invoice))
            {
                return InvoiceFamilyCoreRecord.Refused(entity, LookupNotACoreRecordCode,
                    $"{entity}.{ExternalIdentifierInvoiceColumn} must be a lookup to {InvoiceEntity}; the value given is not.");
            }

            if (invoice is not null)
            {
                return new InvoiceFamilyCoreRecord
                {
                    EntityLogicalName = entity,
                    Regarding = invoice,
                    RegardingColumn = ExternalIdentifierInvoiceColumn,
                    IsInvoiceOnly = true,
                };
            }
        }

        return InvoiceFamilyCoreRecord.Refused(entity, NoCoreRecordCode,
            $"{entity} names no matter, project or work assignment ({string.Join(", ", lookups.Select(l => l.Column))}); "
            + "exactly one is required (FR-05). Nothing was written.");
    }

    /// <summary>Reads the <c>sprk_recordtype_ref</c> row of a table (<c>ICommunicationDataverseService.QueryRecordTypeRefAsync</c>).</summary>
    public delegate Task<Entity?> RecordTypeLookup(string entityLogicalName, CancellationToken ct);

    /// <summary>
    /// Fill the five resolver fields for a decided row: the record type (<c>sprk_recordtype_ref</c>), the id (lowercase
    /// "D"), the name, the relative record URL and the number (the column the record-type row names). All five or a
    /// refusal, never a partial set. A read failure is <see cref="ReadFailedCode"/>, never success.
    /// </summary>
    /// <param name="decided">The result of <see cref="Check(Entity)"/>; a refusal is returned unchanged.</param>
    /// <param name="records">Reads the regarded record's name and number.</param>
    /// <param name="recordTypes">Reads the record-type row (pass <c>ICommunicationDataverseService.QueryRecordTypeRefAsync</c>).</param>
    public static Task<InvoiceFamilyCoreRecord> DeriveResolverFieldsAsync(
        InvoiceFamilyCoreRecord decided,
        IGenericEntityService records,
        RecordTypeLookup recordTypes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        return DeriveAsync(decided, AppOnlyRecords(records), recordTypes, displayOptional: false, ct);
    }

    /// <summary>
    /// Reads the regarded record's <paramref name="columns"/> (task 045d: the generic writers pass their own reader — app-only on
    /// the create and the app-only updates, the caller's on a caller update). <c>null</c>, or a fault that is a record-not-found,
    /// is <see cref="RecordNotFoundCode"/>; any other fault is <see cref="ReadFailedCode"/>.
    /// </summary>
    public delegate Task<Entity?> RecordLookup(string entityLogicalName, Guid id, string[] columns, CancellationToken ct);

    /// <summary>
    /// Task 045d (owner ruling Q8 (a); owner, 2026-11-12, option (b)): the GENERIC writers' derive — the create's pre-write rule,
    /// the caller update planner and the app-only update paths. The same reads and the same refusals as
    /// <see cref="DeriveResolverFieldsAsync(InvoiceFamilyCoreRecord, IGenericEntityService, RecordTypeLookup, CancellationToken)"/>
    /// (no active catalog row, or one naming no number column, is <see cref="ResolverFieldsIncompleteCode"/>; a missing record is
    /// <see cref="RecordNotFoundCode"/>; a read fault is <see cref="ReadFailedCode"/>), except that the row takes what the
    /// record has: the type, the id and the URL always, the name and the number when the record has them
    /// (<see cref="InvoiceFamilyResolverFields.ToGenericFields"/>). A blank name or number is never a refusal here. The
    /// submission writers keep the strict overload (all five or a refusal).
    /// </summary>
    public static Task<InvoiceFamilyCoreRecord> DeriveGenericResolverFieldsAsync(
        InvoiceFamilyCoreRecord decided,
        RecordLookup records,
        RecordTypeLookup recordTypes,
        CancellationToken ct = default) =>
        DeriveAsync(decided, records, recordTypes, displayOptional: true, ct);

    /// <summary>An app-only <see cref="RecordLookup"/> over <paramref name="records"/>.</summary>
    public static RecordLookup AppOnlyRecords(IGenericEntityService records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return async (table, id, columns, token) => await records.RetrieveAsync(table, id, columns, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 045d: an app-only <see cref="RecordTypeLookup"/> over <paramref name="records"/> — the table's active
    /// <c>sprk_recordtype_ref</c> row, the same query as <c>ICommunicationDataverseService.QueryRecordTypeRefAsync</c>, for the
    /// app-only update paths whose only reader is the <see cref="IGenericEntityService"/>.
    /// </summary>
    public static RecordTypeLookup AppOnlyRecordTypes(IGenericEntityService records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return async (table, token) =>
        {
            var query = new QueryExpression(RecordTypeEntity)
            {
                ColumnSet = new ColumnSet(RecordTypeEntity + "id", "sprk_recorddisplayname", RegardingRecordType.RecordNumberFieldColumn),
                TopCount = 1,
            };
            query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, table);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var rows = await records.RetrieveMultipleAsync(query, token).ConfigureAwait(false);
            return rows.Entities.Count > 0 ? rows.Entities[0] : null;
        };
    }

    private const string RecordTypeEntity = "sprk_recordtype_ref";

    private static async Task<InvoiceFamilyCoreRecord> DeriveAsync(
        InvoiceFamilyCoreRecord decided,
        RecordLookup records,
        RecordTypeLookup recordTypes,
        bool displayOptional,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decided);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(recordTypes);
        if (decided.IsRefused || decided.Regarding is not { } regarding)
        {
            return decided;
        }

        Entity? recordType;
        try
        {
            recordType = await recordTypes(regarding.LogicalName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The generic writers' reason can reach a response (a 500's detail): the fault's own text stays out of it there.
            return decided.Refuse(ReadFailedCode,
                $"The record-type row for {regarding.LogicalName} could not be read ({(displayOptional ? ex.GetType().Name : ex.Message)}); nothing was written.");
        }

        var nameField = RegardingRecordType.GetPrimaryNameField(regarding.LogicalName);
        var numberField = RegardingRecordType.RecordNumberFieldOf(recordType);
        if (recordType is null || recordType.Id == Guid.Empty || nameField is null || numberField is null)
        {
            return decided.Refuse(ResolverFieldsIncompleteCode,
                $"The resolver fields for {regarding.LogicalName} cannot be completed: "
                + (recordType is null || recordType.Id == Guid.Empty
                    ? "no active sprk_recordtype_ref row"
                    : nameField is null ? "no known name column" : "its sprk_recordtype_ref row names no number column")
                + ". Nothing was written.");
        }

        Entity? row;
        try
        {
            row = await records(regarding.LogicalName, regarding.Id, [nameField, numberField], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return IsRecordNotFound(ex)
                ? decided.Refuse(RecordNotFoundCode, $"{regarding.LogicalName}({regarding.Id:D}) does not exist; nothing was written.")
                : decided.Refuse(ReadFailedCode,
                    $"{regarding.LogicalName}({regarding.Id:D}) could not be read ({(displayOptional ? ex.GetType().Name : ex.Message)}); nothing was written.");
        }

        if (row is null)
        {
            return decided.Refuse(RecordNotFoundCode, $"{regarding.LogicalName}({regarding.Id:D}) does not exist; nothing was written.");
        }

        var fields = displayOptional
            ? BuildGenericResolverFields(regarding, recordType.Id, row.GetAttributeValue<string>(nameField), row.GetAttributeValue<string>(numberField))
            : BuildResolverFields(regarding, recordType.Id, row.GetAttributeValue<string>(nameField),
                row.GetAttributeValue<string>(numberField));
        return fields is null
            ? decided.Refuse(ResolverFieldsIncompleteCode,
                $"{regarding.LogicalName}({regarding.Id:D}) has no {(string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(nameField)) ? nameField : numberField)}, "
                + "so its resolver fields cannot be completed. Nothing was written.")
            : decided with { ResolverFields = fields };
    }

    /// <summary>The five fields, or null when the name or number is blank (never a partial set). Pure.</summary>
    internal static InvoiceFamilyResolverFields? BuildResolverFields(
        EntityReference regarding, Guid recordTypeId, string? name, string? number)
    {
        ArgumentNullException.ThrowIfNull(regarding);
        if (recordTypeId == Guid.Empty || regarding.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var id = regarding.Id.ToString("D").ToLowerInvariant();
        var trimmedName = name.Trim();
        return new InvoiceFamilyResolverFields(
            recordTypeId,
            id,
            trimmedName.Length > RegardingRecordNameMaxLength ? trimmedName[..RegardingRecordNameMaxLength] : trimmedName,
            RegardingRecordType.BuildRecordUrl(regarding.LogicalName.ToLowerInvariant(), id),
            number.Trim());
    }

    /// <summary>
    /// Task 045d (owner, 2026-11-12, option (b)): the generic writers' fields — what the record has. The type, the id and the URL
    /// always; the name (cut to 100) and the number when the record has them, otherwise <see cref="string.Empty"/>, which
    /// <see cref="InvoiceFamilyResolverFields.ToGenericFields"/> writes as a clear. Null only for an empty type or record id. Pure.
    /// </summary>
    internal static InvoiceFamilyResolverFields? BuildGenericResolverFields(
        EntityReference regarding, Guid recordTypeId, string? name, string? number)
    {
        ArgumentNullException.ThrowIfNull(regarding);
        if (recordTypeId == Guid.Empty || regarding.Id == Guid.Empty)
            return null;

        var id = regarding.Id.ToString("D").ToLowerInvariant();
        var trimmedName = name?.Trim() ?? string.Empty;
        return new InvoiceFamilyResolverFields(
            recordTypeId,
            id,
            trimmedName.Length > RegardingRecordNameMaxLength ? trimmedName[..RegardingRecordNameMaxLength] : trimmedName,
            RegardingRecordType.BuildRecordUrl(regarding.LogicalName.ToLowerInvariant(), id),
            number?.Trim() ?? string.Empty);
    }

    private static bool IsExternalIdentifier(string entity) =>
        string.Equals(entity.Trim(), ExternalIdentifierEntity, StringComparison.OrdinalIgnoreCase);

    private static bool IsInvoice(string entity) =>
        string.Equals(entity.Trim(), InvoiceEntity, StringComparison.OrdinalIgnoreCase);

    private static bool IsLine(string entity) =>
        string.Equals(entity.Trim(), LineEntity, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A value is unset (null, or a reference with an empty id), a reference to <paramref name="core"/> (a reference
    /// with no logical name is taken as the column's own table), or invalid (anything else).
    /// </summary>
    private static bool TryReadReference(object? value, string core, out EntityReference? reference)
    {
        reference = null;
        switch (value)
        {
            case null:
                return true;
            case EntityReference r when r.Id == Guid.Empty:
                return true;
            case EntityReference r when string.IsNullOrWhiteSpace(r.LogicalName):
                reference = new EntityReference(core, r.Id);
                return true;
            case EntityReference r when string.Equals(r.LogicalName, core, StringComparison.OrdinalIgnoreCase):
                reference = new EntityReference(core, r.Id);
                return true;
            default:
                return false;
        }
    }

    private static bool IsRecordNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
                return true;
        }

        return false;
    }
}

/// <summary>
/// What <see cref="InvoiceFamilyCoreRecordGuard"/> decided for one invoice-family row: the record it regards, or a
/// refusal with a stable <see cref="RefusalCode"/>.
/// </summary>
public sealed record InvoiceFamilyCoreRecord
{
    /// <summary>The table, lower-case.</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>A stable <c>invoice_family_*</c> code (<see cref="InvoiceFamilyCoreRecordGuard"/>), or null when decided.</summary>
    public string? RefusalCode { get; init; }

    /// <summary>Why it was refused, for logs and problem details.</summary>
    public string? Reason { get; init; }

    /// <summary>True when nothing may be written.</summary>
    public bool IsRefused => RefusalCode is not null;

    /// <summary>The core record (matter / project / work assignment); for an invoice-only external identifier, the invoice.</summary>
    public EntityReference? Regarding { get; init; }

    /// <summary>The column on the row that names <see cref="Regarding"/>.</summary>
    public string? RegardingColumn { get; init; }

    /// <summary>True for an external identifier that regards an invoice and no core record (D-68).</summary>
    public bool IsInvoiceOnly { get; init; }

    /// <summary>The five resolver fields, once <see cref="InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync"/> filled them.</summary>
    public InvoiceFamilyResolverFields? ResolverFields { get; init; }

    internal static InvoiceFamilyCoreRecord Refused(string entity, string code, string reason) =>
        new() { EntityLogicalName = entity.ToLowerInvariant(), RefusalCode = code, Reason = reason };

    internal InvoiceFamilyCoreRecord Refuse(string code, string reason) =>
        this with { RefusalCode = code, Reason = reason, ResolverFields = null };
}

/// <summary>The five ADR-024 resolver fields of an invoice-family row, all filled.</summary>
/// <param name="RecordTypeId">The <c>sprk_recordtype_ref</c> row of the regarded table.</param>
/// <param name="RecordId">The regarded record's id, lowercase "D" (the D-68 key column on an external identifier).</param>
/// <param name="RecordName">Its display name (cut to 100 characters).</param>
/// <param name="RecordUrl">Its relative model-driven record URL.</param>
/// <param name="RecordNumber">Its reference number (the column its record-type row names).</param>
public sealed record InvoiceFamilyResolverFields(
    Guid RecordTypeId, string RecordId, string RecordName, string RecordUrl, string RecordNumber)
{
    /// <summary>The five columns as an SDK payload fragment (<see cref="Entity"/> attributes / an update dictionary).</summary>
    public Dictionary<string, object> ToFields() => new(StringComparer.OrdinalIgnoreCase)
    {
        [CoreAncestorResolver.RegardingRecordTypeColumn] = new EntityReference("sprk_recordtype_ref", RecordTypeId),
        [CoreAncestorResolver.RegardingRecordIdColumn] = RecordId,
        ["sprk_regardingrecordname"] = RecordName,
        ["sprk_regardingrecordurl"] = RecordUrl,
        ["sprk_regardingrecordnumber"] = RecordNumber,
    };

    /// <summary>
    /// Task 045d (owner, 2026-11-12, option (b)): the five columns as the GENERIC writers write them — a blank name or number is
    /// written as null (a clear), so a row moved to a record without one never keeps the previous record's. The submission
    /// writers use <see cref="ToFields"/>, whose fields are never blank.
    /// </summary>
    public Dictionary<string, object?> ToGenericFields() => new(StringComparer.OrdinalIgnoreCase)
    {
        [CoreAncestorResolver.RegardingRecordTypeColumn] = new EntityReference("sprk_recordtype_ref", RecordTypeId),
        [CoreAncestorResolver.RegardingRecordIdColumn] = RecordId,
        ["sprk_regardingrecordname"] = string.IsNullOrWhiteSpace(RecordName) ? null : RecordName,
        ["sprk_regardingrecordurl"] = RecordUrl,
        ["sprk_regardingrecordnumber"] = string.IsNullOrWhiteSpace(RecordNumber) ? null : RecordNumber,
    };
}

/// <summary>
/// Task 045e: what a write sends of the ADR-024 regarding pair — whether it sets each half, and to what — for
/// <see cref="InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync"/>. Read from the path's own payload shape:
/// <see cref="Of(string, IEnumerable{KeyValuePair{string, object?}})"/> for a Web API dictionary (the app-only paths), and
/// <c>OwnedChildWrite.PairWriteOf</c> for a mapped item (the create and the caller paths).
/// </summary>
/// <param name="SetsId">The write sets or clears <c>sprk_regardingrecordid</c>.</param>
/// <param name="IdText">The id it writes (null for a clear).</param>
/// <param name="SetsType">The write binds or clears the record type.</param>
/// <param name="TypeId">The catalog row it binds (null for a clear).</param>
/// <param name="Unreadable">A pair value that is neither a clear nor readable as an id or a bind: it names no record.</param>
public readonly record struct RegardingPairWrite(bool SetsId, string? IdText, bool SetsType, Guid? TypeId, bool Unreadable)
{
    /// <summary>The write sends either half of the pair.</summary>
    public bool SetsEither => SetsId || SetsType;

    /// <summary>Task 045e (fix round 3): the write sends <c>sprk_regardingrecordurl</c>.</summary>
    public bool SetsUrl { get; init; }

    /// <summary>The URL text it writes (null for a clear; a non-text value as its text, which names no record).</summary>
    public string? UrlText { get; init; }

    /// <summary>The write sends something the agreement check decides: either half of the pair, or the record URL.</summary>
    public bool DecidesAnything => SetsEither || SetsUrl;

    /// <summary>The pair a Web API dictionary sends (a type as a <c>{nav}@odata.bind</c> path or a reference; null clears).</summary>
    public static RegardingPairWrite Of(string entityLogicalName, IEnumerable<KeyValuePair<string, object?>> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var pairs = payload as IReadOnlyCollection<KeyValuePair<string, object?>> ?? payload.ToList();
        var change = OwnedChildWrite.PairChangeOf(entityLogicalName, pairs);
        var written = new RegardingPairWrite(change.SetsId, change.IdText, change.SetsType, change.TypeId, change.Unreadable);
        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(key)
                || !string.Equals(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key), RecordUrlColumn, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            written = written with
            {
                SetsUrl = true,
                UrlText = value switch
                {
                    null => null,
                    System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined } => null,
                    System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
                    _ => value.ToString(),
                },
            };
        }

        return written;
    }

    /// <summary>The record URL column (ADR-024 display column).</summary>
    public const string RecordUrlColumn = "sprk_regardingrecordurl";

    /// <summary>
    /// Task 045e (fix round 3): whether a record URL names <paramref name="table"/>'s record <paramref name="id"/> — read from
    /// its <c>etn</c> (any case) and <c>id</c> (as an id: any case, braces allowed) query parameters, so the server's relative
    /// form (<c>RegardingRecordType.BuildRecordUrl</c>) and a client's absolute form with an app id both read alike. A URL
    /// without exactly one of each, or whose id is not an id, names no record: false.
    /// </summary>
    public static bool UrlNamesRecord(string? url, string table, Guid id) =>
        TryReadRecordUrl(url, out var named, out var namedId)
        && string.Equals(named, table?.Trim(), StringComparison.OrdinalIgnoreCase)
        && namedId == id;

    /// <summary>
    /// Task 045e (fix round 4): the table and record a record URL names, read as <see cref="UrlNamesRecord"/> reads them (exactly
    /// one non-blank <c>etn</c>, exactly one <c>id</c> that parses as an id); false when it names no record.
    /// </summary>
    public static bool TryReadRecordUrl(string? url, out string table, out Guid id)
    {
        table = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var start = url.IndexOf('?');
        if (start < 0)
            return false;
        var query = url[(start + 1)..];
        var fragment = query.IndexOf('#');
        if (fragment >= 0)
            query = query[..fragment];

        string? etn = null, idText = null;
        int etns = 0, ids = 0;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var name = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]).Trim();
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' ')).Trim();
            if (string.Equals(name, "etn", StringComparison.OrdinalIgnoreCase))
            {
                etns++;
                etn = value;
            }
            else if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
            {
                ids++;
                idText = value;
            }
        }

        if (etns != 1 || ids != 1 || string.IsNullOrWhiteSpace(etn) || !Guid.TryParse(idText, out var named))
            return false;
        table = etn;
        id = named;
        return true;
    }

    /// <summary>
    /// Task 045f: the reason code a write path refuses with when a regarding record URL it would store does not name the record
    /// the row's regarding pair names after the write (every pair table outside the invoice family; the family keeps
    /// <see cref="InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode"/>).
    /// </summary>
    public const string RecordUrlMismatchCode = "regarding_record_url_mismatch";

    /// <summary>The words of <see cref="RecordUrlMismatchCode"/>: fixed text that names no record.</summary>
    public const string RecordUrlMismatchReason =
        "the regarding record URL does not name the record this row is regarding";

    /// <summary>
    /// Task 045f: whether <paramref name="url"/> names the record a row's regarding pair names — <paramref name="pairTable"/>'s
    /// record <paramref name="pairId"/> — read as <see cref="TryReadRecordUrl"/> reads it (its <c>etn</c> any case, its
    /// <c>id</c> any case or braced). When it does, <paramref name="canonical"/> is the URL as the server writes it
    /// (<see cref="InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl"/>: the table lower case, the id lower-case "D"); its scheme,
    /// host and other parameters are not kept. A pair with an id and no type names no table, so only the id is compared and
    /// the URL's own <c>etn</c> is kept, when it is a logical name. Pure: no read.
    /// </summary>
    public static bool TryAdmitRecordUrl(string? url, string? pairTable, Guid pairId, out string canonical)
    {
        canonical = string.Empty;
        if (pairId == Guid.Empty || !TryReadRecordUrl(url, out var named, out var namedId) || namedId != pairId)
            return false;
        if (pairTable is not null)
        {
            if (!string.Equals(named, pairTable.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        else if (!IsLogicalName(named))
        {
            return false;
        }

        canonical = InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl(pairTable ?? named, pairId);
        return true;

        static bool IsLogicalName(string text) =>
            text.Length is > 0 and <= 64 && (char.IsAsciiLetterLower(text[0]) || char.IsAsciiLetterUpper(text[0]))
            && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }
}
