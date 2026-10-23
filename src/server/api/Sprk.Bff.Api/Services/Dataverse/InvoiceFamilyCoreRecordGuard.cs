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
/// <para><b>The write core invokes it (ISS-010, #1973; unified-access-control-r2).</b> <c>OwnedChildWrite.CreateAsync</c> and
/// <c>OwnedChildWrite.RefileAsync</c> — the core every child-record route and chat create / update tool writes through —
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
    public static async Task<InvoiceFamilyCoreRecord> CheckWriteAsync(
        string entityLogicalName,
        Func<string, object?> read,
        Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> resolveSuperseded,
        CancellationToken ct = default,
        Func<EntityReference, CancellationToken, Task<bool>>? anyLineOfAnotherCoreRecord = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(resolveSuperseded);
        var decided = await CheckWriteAgainstOtherInvoiceAsync(entityLogicalName, read, resolveSuperseded, ct).ConfigureAwait(false);
        if (decided.IsRefused || anyLineOfAnotherCoreRecord is null || !IsInvoice(decided.EntityLogicalName))
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
    /// (<c>DataverseUpdateHandler</c>) and the field-mapping push. For an update of <c>sprk_invoice</c> or
    /// <c>sprk_billingevent</c> whose patch sets or clears a guarded lookup (<see cref="GuardedLookups"/>), the stored
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

    private static async Task<InvoiceFamilyCoreRecord?> CheckGenericUpdateCoreAsync(
        string entityLogicalName,
        Guid recordId,
        IEnumerable<KeyValuePair<string, object?>> patch,
        IGenericEntityService? appOnly,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var entity = entityLogicalName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsInvoice(entity) && !IsLine(entity))
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
        // stored row is therefore read for EVERY write of an invoice or line, whether or not the patch touches a guarded
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
            IsInvoice(entity) && WritesCoreLookup(entity, change.Keys) ? LinesOfAnotherCoreRecord(appOnly, recordId) : null)
            .ConfigureAwait(false);

        return decided.IsRefused ? decided : null;
    }

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
    /// exists, and the write itself must not re-create it if it was deleted since (#2081 fix round 2). Only the two tables
    /// <see cref="CheckGenericUpdateAsync"/> covers; every other table keeps its write unchanged.
    /// </summary>
    public static bool IsUpdateOnlyTarget(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && (IsInvoice(entityLogicalName) || IsLine(entityLogicalName));

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
    public static async Task<InvoiceFamilyCoreRecord> DeriveResolverFieldsAsync(
        InvoiceFamilyCoreRecord decided,
        IGenericEntityService records,
        RecordTypeLookup recordTypes,
        CancellationToken ct = default)
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
            return decided.Refuse(ReadFailedCode,
                $"The record-type row for {regarding.LogicalName} could not be read ({ex.Message}); nothing was written.");
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
            row = await records.RetrieveAsync(regarding.LogicalName, regarding.Id, [nameField, numberField], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return IsRecordNotFound(ex)
                ? decided.Refuse(RecordNotFoundCode, $"{regarding.LogicalName}({regarding.Id:D}) does not exist; nothing was written.")
                : decided.Refuse(ReadFailedCode,
                    $"{regarding.LogicalName}({regarding.Id:D}) could not be read ({ex.Message}); nothing was written.");
        }

        if (row is null)
        {
            return decided.Refuse(RecordNotFoundCode, $"{regarding.LogicalName}({regarding.Id:D}) does not exist; nothing was written.");
        }

        var fields = BuildResolverFields(regarding, recordType.Id, row.GetAttributeValue<string>(nameField),
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
}
