using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// #2799 (unified-access-control-r2): the invoice family's LOCKED columns — every lookup
/// <see cref="InvoiceFamilyCoreRecordGuard"/> decides a write on (<see cref="InvoiceFamilyCoreRecordGuard.GuardedLookups"/> over
/// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>) — and the one way a write that acts for a person writes them:
/// checked AS THE PERSON, written AS THE APPLICATION (project rule G5).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The guard holds on every BFF write path, but a native form or a direct Web API call by a user with
/// Write skips it (ADR-002: no plugins). Field-level security on these columns lets only the BFF application users write
/// them (<c>scripts/Set-InvoiceFamilyFieldSecurity.ps1</c>). A BFF path that writes them as the person
/// (<c>MSCRMCallerID</c> or the person's own token) would then be refused by that same lock, so each such path writes them
/// through here. When the application writes, Dataverse no longer checks the person, so this asks, as the person,
/// everything Dataverse would have asked: Write on the row, Append on it when a lookup is set to a record, and AppendTo on
/// every record a locked lookup now names. The guard's rule decisions are unchanged; only who writes the locked columns
/// changes.</para>
/// <para><b>Answers.</b> A question Dataverse does not answer is <see cref="LockedWriteCheck.Unavailable"/>; a refusal, a row or
/// record that does not exist, and a value that names no record are all <see cref="LockedWriteCheck.Denied"/> — one answer,
/// so an unknown id and a record the person may not use look alike (NFR-01). Neither writes anything.</para>
/// <para><b>Known limit</b> (as <see cref="ProjectMatterFiling"/>): a write of locked AND other columns is two requests — the
/// other columns as the person (their own field security and privileges apply), then the locked ones as the application.
/// If the second fails after the first succeeded, the other columns stay written and the answer says so.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — <see cref="CallerWriteAuthorization"/> asks AppendTo
/// as the caller through an OBO client only, and folds an unanswered question into a denial; <see cref="ProjectMatterFiling"/>
/// is the same pattern for one table's pair. Extension — the guard owns the rules, not the identity of a write; this sits
/// beside it as one static helper (ADR-010, no DI) that both the impersonated paths (no OBO token) and the OBO paths can
/// call, through a rights question that tells "no" from "no answer". Cost of doing nothing — once the columns are locked,
/// every BFF update that writes them as the person fails, and without the lock a native form edit skips the guard.</para>
/// </remarks>
internal static class InvoiceFamilyLockedWrite
{
    /// <summary>The refusal code when the person may not write the locked columns (no Write or Append on the row, or no
    /// AppendTo on a record a locked lookup names, or that record does not exist). Nothing is written.</summary>
    public const string LockedWriteDeniedCode = "invoice_family_locked_write_denied";

    /// <summary>One locked column: the table, the lookup column, and the one table it names.</summary>
    internal readonly record struct LockedColumn(string Table, string Column, string TargetTable);

    /// <summary>
    /// Every locked (table, column), derived from <see cref="InvoiceFamilyCoreRecordGuard.GuardedLookups"/> for each table in
    /// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/> — the single source the field-security script and its
    /// ArchTest are compared with. Ordered by table, then column.
    /// </summary>
    internal static readonly IReadOnlyList<LockedColumn> LockedColumns =
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys
            .SelectMany(table => InvoiceFamilyCoreRecordGuard.GuardedLookups(table)!
                .Select(g => new LockedColumn(table.ToLowerInvariant(), g.Column.ToLowerInvariant(), g.Table.ToLowerInvariant())))
            .Distinct()
            .OrderBy(c => c.Table, StringComparer.Ordinal)
            .ThenBy(c => c.Column, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The locked columns of <paramref name="table"/> (column → the table it names); empty outside the family.</summary>
    internal static IReadOnlyDictionary<string, string> LockedColumnsOf(string? table)
    {
        var key = table?.Trim().ToLowerInvariant() ?? string.Empty;
        return LockedColumns.Where(c => c.Table == key)
            .ToDictionary(c => c.Column, c => c.TargetTable, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Web API entity sets of the family tables and the tables their locked lookups name (live metadata, spaarkedev1,
    /// 2026-11-13). Pinned to <see cref="LockedColumns"/> by <c>InvoiceFamilyLockedWriteTests</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> EntitySets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_invoice"] = "sprk_invoices",
            ["sprk_billingevent"] = "sprk_billingevents",
            ["sprk_invoicesubmission"] = "sprk_invoicesubmissions",
            ["sprk_invoiceapproval"] = "sprk_invoiceapprovals",
            ["sprk_externalidentifier"] = "sprk_externalidentifiers",
            ["sprk_invoicecorrection"] = "sprk_invoicecorrections",
            ["sprk_matter"] = "sprk_matters",
            ["sprk_project"] = "sprk_projects",
            ["sprk_workassignment"] = "sprk_workassignments",
        };

    /// <summary>What <see cref="CheckPersonMayWriteAsync"/> decided.</summary>
    internal enum LockedWriteCheck
    {
        /// <summary>The person may write the locked columns; the application writes them.</summary>
        Allowed,

        /// <summary>The person may not (or a row or record named does not exist). Nothing is written.</summary>
        Denied,

        /// <summary>Dataverse did not answer a question. Nothing is written.</summary>
        Unavailable,
    }

    /// <summary>
    /// The rights question asked AS THE PERSON: their rights on one record (entity set, id), or <see langword="null"/> when
    /// Dataverse gave no answer. <see cref="AccessRights.None"/> for a record that does not exist.
    /// </summary>
    internal delegate Task<AccessRights?> RightsQuestion(string entitySet, Guid recordId, CancellationToken ct);

    /// <summary>
    /// The rights question for a known person: <see cref="CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync"/>
    /// naming <paramref name="systemUserId"/> (Dataverse answers from that user's own roles, teams and shares). The id must be
    /// the one the write impersonates, never client-supplied. No probe: every question is unanswered (fail closed).
    /// </summary>
    internal static RightsQuestion RightsOfPrincipal(CallerRecordAccessProbe? probe, Guid systemUserId) =>
        probe is null
            ? (_, _, _) => Task.FromResult<AccessRights?>(null)
            : (entitySet, recordId, ct) => probe.GetPrincipalRightsAppOnlyOrUnknownAsync(systemUserId, entitySet, recordId, ct);

    /// <summary>
    /// Splits a write into its locked columns and the rest. A <c>{navigation}@odata.bind</c> key counts as its column; keys
    /// and values are kept as given. Outside the family everything is "rest".
    /// </summary>
    internal static (Dictionary<string, object?> Locked, Dictionary<string, object?> Others) Split(
        string table, IEnumerable<KeyValuePair<string, object?>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var locked = LockedColumnsOf(table);
        var lockedPart = new Dictionary<string, object?>(StringComparer.Ordinal);
        var rest = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            if (locked.ContainsKey(SecureRootInheritance.NormalizeColumn(key)))
                lockedPart[key] = value;
            else
                rest[key] = value;
        }

        return (lockedPart, rest);
    }

    /// <summary>
    /// <see cref="Split"/> for a Web API JSON body (the OBO paths): the locked fields (values as <see cref="JsonElement"/>, for
    /// the application's write) and the rest of the body as JSON (the person's own PATCH), or <c>null</c> when nothing else is
    /// left.
    /// </summary>
    internal static (Dictionary<string, object?> Locked, string? RestBody) SplitBody(string table, string jsonBody)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);
        var locked = LockedColumnsOf(table);
        var lockedPart = new Dictionary<string, object?>(StringComparer.Ordinal);
        using var body = JsonDocument.Parse(jsonBody);
        using var stream = new MemoryStream();
        var rest = 0;
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in body.RootElement.EnumerateObject())
            {
                if (locked.ContainsKey(SecureRootInheritance.NormalizeColumn(property.Name)))
                {
                    lockedPart[property.Name] = property.Value.Clone();
                    continue;
                }

                property.WriteTo(writer);
                rest++;
            }

            writer.WriteEndObject();
        }

        return (lockedPart, rest > 0 ? System.Text.Encoding.UTF8.GetString(stream.ToArray()) : null);
    }

    /// <summary>
    /// AS THE PERSON (<paramref name="rightsOf"/>): whether they could write <paramref name="locked"/> on
    /// <paramref name="table"/>(<paramref name="recordId"/>) themselves — Write on the row (and Append when a lookup is set to a
    /// record), then AppendTo on every record a locked lookup now names, in the columns' order. The first answer that is not
    /// "yes" decides. Nothing is written. No locked column: <see cref="LockedWriteCheck.Allowed"/>, nothing asked.
    /// </summary>
    internal static async Task<LockedWriteCheck> CheckPersonMayWriteAsync(
        string table,
        Guid recordId,
        IReadOnlyDictionary<string, object?> locked,
        RightsQuestion rightsOf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(rightsOf);
        if (locked.Count == 0)
            return LockedWriteCheck.Allowed;

        var columns = LockedColumnsOf(table);
        if (columns.Count == 0 || !EntitySets.TryGetValue(table.Trim(), out var rowSet) || recordId == Guid.Empty)
            return LockedWriteCheck.Denied;

        var targets = new List<(string EntitySet, Guid Id)>();
        foreach (var (key, value) in locked)
        {
            if (!columns.TryGetValue(SecureRootInheritance.NormalizeColumn(key), out var targetTable)
                || !TryReadTarget(key, value, out var targetId)
                || !EntitySets.TryGetValue(targetTable, out var targetSet))
            {
                return LockedWriteCheck.Denied; // a value that names no record is never written for the person
            }

            if (targetId is { } id)
                targets.Add((targetSet, id));
        }

        var needed = targets.Count > 0 ? AccessRights.Write | AccessRights.Append : AccessRights.Write;
        var onRow = await rightsOf(rowSet, recordId, ct).ConfigureAwait(false);
        if (onRow is not { } rowRights)
            return LockedWriteCheck.Unavailable;
        if ((rowRights & needed) != needed)
            return LockedWriteCheck.Denied;

        foreach (var (entitySet, id) in targets.Distinct())
        {
            var onTarget = await rightsOf(entitySet, id, ct).ConfigureAwait(false);
            if (onTarget is not { } targetRights)
                return LockedWriteCheck.Unavailable;
            if (!targetRights.HasFlag(AccessRights.AppendTo))
                return LockedWriteCheck.Denied;
        }

        return LockedWriteCheck.Allowed;
    }

    /// <summary>
    /// The drop-in for <see cref="InvoiceFamilyCoreRecordGuard.UpdateExistingAsync"/> on a path that may write as a person
    /// (<paramref name="impersonateSystemUserId"/>). Null when written; otherwise the refusal code, nothing written:
    /// <list type="bullet">
    /// <item>No locked column in <paramref name="fields"/>, or an application run (no person): exactly the old single write,
    /// nothing asked.</item>
    /// <item>Otherwise <see cref="CheckPersonMayWriteAsync"/>: unanswered → <see cref="InvoiceFamilyCoreRecordGuard.ReadFailedCode"/>;
    /// denied → <see cref="LockedWriteDeniedCode"/>. Allowed → the other columns as the person (a refusal or fault there is
    /// theirs, as before, and nothing locked is written), then the locked columns as the application.</item>
    /// </list>
    /// A row gone at either write answers as the guard's write does (<paramref name="notFoundAsReadFailed"/>).
    /// </summary>
    internal static async Task<string?> UpdateExistingSplitAsync(
        IFieldMappingDataverseService records,
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        Guid? impersonateSystemUserId,
        bool notFoundAsReadFailed,
        RightsQuestion rightsOf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rightsOf);

        var (locked, rest) = Split(entityLogicalName, fields);
        if (locked.Count == 0 || impersonateSystemUserId is null)
        {
            return await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                    records, entityLogicalName, recordId, fields, impersonateSystemUserId, notFoundAsReadFailed, ct)
                .ConfigureAwait(false);
        }

        switch (await CheckPersonMayWriteAsync(entityLogicalName, recordId, locked, rightsOf, ct).ConfigureAwait(false))
        {
            case LockedWriteCheck.Unavailable:
                return InvoiceFamilyCoreRecordGuard.ReadFailedCode;
            case LockedWriteCheck.Denied:
                return LockedWriteDeniedCode;
        }

        if (rest.Count > 0
            && await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                    records, entityLogicalName, recordId, rest, impersonateSystemUserId, notFoundAsReadFailed, ct)
                .ConfigureAwait(false) is { } gone)
        {
            return gone;
        }

        return await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                records, entityLogicalName, recordId, locked, impersonateSystemUserId: null, notFoundAsReadFailed, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The record a locked value names: <c>null</c> for a clear (null, <see cref="DBNull"/>, an empty reference, a JSON null);
    /// the id of an <see cref="EntityReference"/>, a bind path under a <c>@odata.bind</c> key, or a JSON string bind. False
    /// for anything else.
    /// </summary>
    private static bool TryReadTarget(string key, object? value, out Guid? id)
    {
        id = null;
        var isBind = key.Contains("@odata.bind", StringComparison.OrdinalIgnoreCase);
        switch (value)
        {
            case null:
            case DBNull:
            case JsonElement { ValueKind: JsonValueKind.Null }:
                return true;
            case EntityReference reference:
                if (reference.Id != Guid.Empty)
                    id = reference.Id;
                return true;
            case string bind when isBind && InvoiceFamilyCoreRecordGuard.TryParseBind(bind, out var fromString):
                id = fromString;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element when isBind
                                                                         && InvoiceFamilyCoreRecordGuard.TryParseBind(element.GetString() ?? string.Empty, out var fromJson):
                id = fromJson;
                return true;
            default:
                return false;
        }
    }
}
