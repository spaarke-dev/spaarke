using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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

    /// <summary>
    /// The error code when a mixed write was only partly saved: the other columns were written as the person, then the
    /// application's write of the locked columns failed (decision 3 of the #2799 chunk-3 brief, K-1 in the task notes). Never
    /// success; logged at Warning with the record and the trace id.
    /// </summary>
    public const string LockedWritePartlySavedCode = "invoice_family_locked_write_partly_saved";

    /// <summary>The fixed answer for <see cref="LockedWritePartlySavedCode"/>: it names no record and echoes no fault.</summary>
    public const string PartlySavedMessage =
        "The change was only partly saved: the record's other changes were saved, but what it is filed under was not. "
        + "Reload it and try again (" + LockedWritePartlySavedCode + ").";

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
            // #2799 review 1 (F3): a key is a locked column here only in a form the guard reads as that column (the logical name,
            // or {navigation}@odata.bind; InvoiceFamilyCoreRecordGuard.ColumnOfKey). Split sets aside every key that NORMALIZES to
            // one (_x_value, any @… suffix); one the guard did not read as that column was never decided by it, so it is refused.
            if (!columns.TryGetValue(InvoiceFamilyCoreRecordGuard.ColumnOfKey(key), out var targetTable)
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
    /// A row gone at either write answers as the guard's write does (<paramref name="notFoundAsReadFailed"/>), except that once
    /// the person's write of the other columns has landed, ANY failure of the application's write (a fault, a row gone, a
    /// cancellation) is <see cref="LockedWritePartlySavedCode"/>, logged at Warning with the record and
    /// <paramref name="traceId"/> (the current trace when null) — never success, and no fault text in the answer (K-1).
    /// </summary>
    internal static async Task<string?> UpdateExistingSplitAsync(
        IFieldMappingDataverseService records,
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        Guid? impersonateSystemUserId,
        bool notFoundAsReadFailed,
        RightsQuestion rightsOf,
        ILogger logger,
        CancellationToken ct,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rightsOf);
        ArgumentNullException.ThrowIfNull(logger);

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

        if (rest.Count == 0)
        {
            return await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                    records, entityLogicalName, recordId, locked, impersonateSystemUserId: null, notFoundAsReadFailed, ct)
                .ConfigureAwait(false);
        }

        if (await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                    records, entityLogicalName, recordId, rest, impersonateSystemUserId, notFoundAsReadFailed, ct)
                .ConfigureAwait(false) is { } gone)
        {
            return gone;
        }

        // The person's write landed: from here a failure is "partly saved", never success (K-1). #2799 review 1 (F1): the
        // application's write is not the request's to cancel (a client disconnect or a playbook timeout would otherwise leave the
        // other columns written and the locked ones not) — CancellationToken.None, as the after-write steps of every update path.
        try
        {
            if (await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(
                        records, entityLogicalName, recordId, locked, impersonateSystemUserId: null, notFoundAsReadFailed,
                        CancellationToken.None)
                    .ConfigureAwait(false) is null)
            {
                return null;
            }

            LogPartlySaved(logger, entityLogicalName, recordId, traceId, fault: null);
        }
        catch (Exception ex)
        {
            LogPartlySaved(logger, entityLogicalName, recordId, traceId, ex);
        }

        return LockedWritePartlySavedCode;
    }

    /// <summary>
    /// The generic update paths' refusal on their existing "throw" contract: <see cref="PartlySavedMessage"/> for
    /// <see cref="LockedWritePartlySavedCode"/>, otherwise the guard's code-only refusal
    /// (<see cref="InvoiceFamilyCoreRecordGuard.RefusalException"/>).
    /// </summary>
    internal static RecordOwnerUnresolvedException RefusalException(string entityLogicalName, string refusalCode) =>
        refusalCode == LockedWritePartlySavedCode
            ? new RecordOwnerUnresolvedException(entityLogicalName, refusalCode, PartlySavedMessage)
            : InvoiceFamilyCoreRecordGuard.RefusalException(entityLogicalName, refusalCode);

    private static void LogPartlySaved(ILogger logger, string table, Guid recordId, string? traceId, Exception? fault) =>
        logger.LogWarning(
            "[INVOICE-FAMILY-LOCK] {Table} {RecordId}: the other columns were written as the person, but the application's write of "
            + "the locked columns failed ({Fault}); answered {Code}. TraceId={TraceId}",
            table, recordId, fault?.GetType().Name ?? "record not found", LockedWritePartlySavedCode,
            traceId ?? System.Diagnostics.Activity.Current?.TraceId.ToString());

    // ── The OBO paths (C4-C6): the person's own token ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The rights question asked under the person's OWN token (the OBO paths): <c>RetrievePrincipalAccess</c> naming
    /// <paramref name="me"/> (from <c>WhoAmI</c> under that token, so it cannot name anyone else). A 403 or 404 is Dataverse's
    /// "no" (<see cref="AccessRights.None"/>: a record that does not exist answers as one they may not use); a success that
    /// carries no rights, and any other status (throttled, 5xx, no token, a transport fault), is no answer (<see langword="null"/>).
    /// </summary>
    internal static RightsQuestion RightsOfCaller(IDataverseUserClient user, Guid me)
    {
        ArgumentNullException.ThrowIfNull(user);
        return async (entitySet, recordId, ct) =>
        {
            var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{entitySet}({recordId:D})\"}}");
            var access = await user.GetAsync(
                    $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1={target}", ct)
                .ConfigureAwait(false);
            if (access.IsSuccess)
            {
                return access.Body is { } body
                       && body.TryGetProperty("AccessRights", out var value)
                       && value.ValueKind == JsonValueKind.String
                    ? DataverseAccessRightsMapper.FromAccessRightsString(value.GetString())
                    : null;
            }

            return access.StatusCode is 403 or 404 ? AccessRights.None : null;
        };
    }

    /// <summary>What <see cref="PatchAsCallerAsync"/> did.</summary>
    internal enum CallerPatchKind
    {
        /// <summary>Written (as before when the body holds no locked column).</summary>
        Written,

        /// <summary>The person's own PATCH (or the <c>WhoAmI</c> it needed) failed at Dataverse: their own error. Nothing locked written.</summary>
        ClientFailure,

        /// <summary><see cref="LockedWriteCheck.Denied"/>. Nothing written.</summary>
        Denied,

        /// <summary><see cref="LockedWriteCheck.Unavailable"/>, or no application writer here. Nothing written.</summary>
        Unavailable,

        /// <summary>The other columns were written as the person; the application's write of the locked ones failed (K-1).</summary>
        PartlySaved,
    }

    /// <summary>The answer of <see cref="PatchAsCallerAsync"/>; <see cref="ClientFailure"/> with <see cref="CallerPatchKind.ClientFailure"/>.</summary>
    internal sealed record CallerPatch(CallerPatchKind Kind, DataverseUserResponse? ClientFailure = null)
    {
        public static readonly CallerPatch Written = new(CallerPatchKind.Written);
    }

    /// <summary>
    /// The OBO paths' PATCH of <paramref name="table"/>(<paramref name="recordId"/>) (C4-C6). A body with no locked column:
    /// exactly the person's own PATCH as before (<c>{entitySet}({id})</c>, the body unchanged). Otherwise: <c>WhoAmI</c> and
    /// <see cref="CheckPersonMayWriteAsync"/> under the person's token (<see cref="RightsOfCaller"/>); then the other columns
    /// as the person (their PATCH), then the locked ones as the application (update-only). Nothing is written on a refusal.
    /// A failure of the application's write after the person's landed is <see cref="CallerPatchKind.PartlySaved"/>, logged at
    /// Warning with the record and <paramref name="traceId"/>; with nothing else written, a row gone there is
    /// <see cref="CallerPatchKind.Denied"/> (the one answer) and a fault <see cref="CallerPatchKind.Unavailable"/>.
    /// </summary>
    internal static async Task<CallerPatch> PatchAsCallerAsync(
        IDataverseUserClient user,
        IFieldMappingDataverseService? appOnly,
        string table,
        string entitySet,
        Guid recordId,
        string jsonBody,
        ILogger logger,
        string? traceId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(logger);
        var path = $"{entitySet}({recordId:D})";
        var (locked, restBody) = LockedColumnsOf(table).Count == 0 ? (new Dictionary<string, object?>(), jsonBody) : SplitBody(table, jsonBody);
        if (locked.Count == 0)
        {
            var patch = await user.PatchAsync(path, jsonBody, ct).ConfigureAwait(false);
            return patch.IsSuccess ? CallerPatch.Written : new CallerPatch(CallerPatchKind.ClientFailure, patch);
        }

        if (appOnly is null)
        {
            logger.LogWarning("[INVOICE-FAMILY-LOCK] {Table} {RecordId}: no application writer for the locked columns here; nothing written.",
                table, recordId);
            return new CallerPatch(CallerPatchKind.Unavailable);
        }

        var me = await CallerWriteAuthorization.WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is { } whoAmIFailure)
            return new CallerPatch(CallerPatchKind.ClientFailure, whoAmIFailure);

        switch (await CheckPersonMayWriteAsync(table, recordId, locked, RightsOfCaller(user, me.SystemUserId), ct).ConfigureAwait(false))
        {
            case LockedWriteCheck.Unavailable:
                return new CallerPatch(CallerPatchKind.Unavailable);
            case LockedWriteCheck.Denied:
                return new CallerPatch(CallerPatchKind.Denied);
        }

        if (restBody is not null)
        {
            var patch = await user.PatchAsync(path, restBody, ct).ConfigureAwait(false);
            if (!patch.IsSuccess)
                return new CallerPatch(CallerPatchKind.ClientFailure, patch);
        }

        try
        {
            // #2799 review 1 (F1): once the person's PATCH landed, the application's write is not the request's to cancel.
            await appOnly.UpdateExistingRecordFieldsAsync(
                    table, recordId, locked, impersonateSystemUserId: null, restBody is null ? ct : CancellationToken.None)
                .ConfigureAwait(false);
            return CallerPatch.Written;
        }
        catch (Exception ex) when (restBody is not null)
        {
            LogPartlySaved(logger, table, recordId, traceId, ex);
            return new CallerPatch(CallerPatchKind.PartlySaved);
        }
        catch (KeyNotFoundException)
        {
            return new CallerPatch(CallerPatchKind.Denied);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[INVOICE-FAMILY-LOCK] {Table} {RecordId}: the application's write of the locked columns failed; "
                                  + "nothing was written. TraceId={TraceId}",
                table, recordId, traceId ?? System.Diagnostics.Activity.Current?.TraceId.ToString());
            return new CallerPatch(CallerPatchKind.Unavailable);
        }
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
