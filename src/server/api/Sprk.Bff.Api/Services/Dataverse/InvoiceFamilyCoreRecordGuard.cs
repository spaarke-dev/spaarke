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
// class: no package, no endpoint, no DI registration (ADR-010); its one read helper takes the services its caller holds.

using Microsoft.Xrm.Sdk;
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
/// <para><b>Refuses, never picks.</b> Two or three lookups set is <see cref="MultipleCoreRecordsCode"/>; none is
/// <see cref="NoCoreRecordCode"/>. A value that is not a lookup to the column's core table is
/// <see cref="LookupNotACoreRecordCode"/> (a mis-built payload must not read as "unset").</para>
/// <para><b>The mixed naming scheme</b> (project CLAUDE.md, task 012): <c>sprk_invoice</c> and <c>sprk_billingevent</c>
/// keep their typed <c>sprk_matter</c> / <c>sprk_project</c> and add <c>sprk_regardingworkassignment</c>; the tables
/// created for eBilling carry the <c>sprk_regarding{core}</c> trio (<see cref="CoreAncestorResolver.CoreAncestorLookups"/>
/// names). FR-05 names three core types, so <c>sprk_regardingservicerequest</c> is never one of them.</para>
/// <para><b>D-75.</b> A line's core record is its OWN lookup. <c>sprk_billingevent.sprk_invoice</c> is never read here: a
/// line may in future not belong to exactly one invoice.</para>
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

    /// <summary>The external identifier table, the one family table whose row may regard an invoice only (D-68).</summary>
    public const string ExternalIdentifierEntity = "sprk_externalidentifier";

    /// <summary><c>sprk_externalidentifier.sprk_regardinginvoice</c> (lookup to <c>sprk_invoice</c>).</summary>
    public const string ExternalIdentifierInvoiceColumn = "sprk_regardinginvoice";

    private const string InvoiceEntity = "sprk_invoice";

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
