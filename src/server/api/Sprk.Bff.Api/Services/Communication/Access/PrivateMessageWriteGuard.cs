using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// fm task 056 (owner 2026-11-22, Q5: a private message "always stays 1:1 - no filing"): the pre-write check every generic BFF
/// UPDATE of an existing <c>sprk_communication</c> asks before it writes. A private message (<see cref="PrivateContextColumns.IsPrivate"/>)
/// is never filed and never moved to another thread, by any writer. An update that sets only a typed regarding that is no ownership
/// parent (a contact, an account, an organization) or only the ADR-024 regarding pair never reaches
/// <see cref="IRecordOwnershipResolver.ReparentAsync"/>, so the rule is asked here, on the update itself.
/// <list type="bullet">
/// <item>An update naming none of <see cref="TriggerColumns"/>, or of another table, reads nothing and passes.</item>
/// <item>Otherwise the row's reference columns that exist in metadata are read app-only; a set reference refuses with
/// <see cref="RecordOwnerRefusal.PrivateMessageNotFileable"/> (HTTP 409), and a fault of that read refuses with
/// <see cref="CheckUnavailable"/> (HTTP 503), both before anything is written.</item>
/// </list>
/// A refusal is a <see cref="RecordOwnerResolution"/>, so an HTTP writer renders it with
/// <c>ProblemDetailsHelper.RecordOwnerRefused</c>, as it renders a ReparentAsync refusal.
/// </summary>
/// <remarks>
/// Component Justification (CLAUDE.md §11): (1) Existing — the ReparentAsync refusal (only on writes that re-file through it),
/// <see cref="DirectMessageOwnerStep.ClearPrivateFilingAsync"/> (after the write, out of band) and
/// <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> (a column rule, with no row read). (2) Extension — none of them sees a plain
/// update of a typed regarding or the regarding pair before it is written; this check reuses their column maps and the one
/// predicate. (3) Cost of doing nothing — a generic BFF update files a private message under a contact, an account or a record, and
/// the filing stands until the out-of-band clear runs.
/// </remarks>
internal static class PrivateMessageWriteGuard
{
    /// <summary>
    /// The reference columns could not be read, so whether the row is a private message is unknown; nothing was written and the
    /// caller tries again. HTTP 503.
    /// </summary>
    internal const string CheckUnavailable = "sdap.communication.private.check_unavailable";

    /// <summary>The table the guard applies to.</summary>
    private const string CommunicationTable = "sprk_communication";

    /// <summary>
    /// The ONE list of columns whose change makes an update of a <c>sprk_communication</c> ask the guard: every filing link a private
    /// message never holds or changes. Derived, never hand-listed: the communication's lineage columns
    /// (<see cref="SecureChildLineage.Children"/>: every ownership-parent lookup, and the thread lookup), every typed regarding
    /// lookup (<see cref="RegardingFieldMap.AllRegardingFields"/>), and the ADR-024 regarding pair
    /// (<see cref="CoreAncestorResolver.RegardingRecordIdColumn"/>, <see cref="CoreAncestorResolver.RegardingRecordTypeColumn"/>).
    /// <see cref="DirectMessageOwnerStep.PrivateFilingColumns"/> is this list without the thread lookup. Body, subject, status and
    /// every other column are not in it.
    /// </summary>
    internal static readonly IReadOnlyList<string> TriggerColumns =
        SecureChildLineage.Children[CommunicationTable].LineageColumns
            .Concat(RegardingFieldMap.AllRegardingFields)
            .Concat(new[] { CoreAncestorResolver.RegardingRecordIdColumn, CoreAncestorResolver.RegardingRecordTypeColumn })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly IReadOnlySet<string> TriggerSet = new HashSet<string>(TriggerColumns, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an update of <paramref name="table"/> writing <paramref name="columns"/> must ask the guard: the table is
    /// <c>sprk_communication</c> and a column (in any key form: <c>column@odata.bind</c>, <c>_column_value</c>) is in
    /// <see cref="TriggerColumns"/>.
    /// </summary>
    internal static bool Triggers(string table, IEnumerable<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return string.Equals(table?.Trim(), CommunicationTable, StringComparison.OrdinalIgnoreCase)
            && columns.Any(c => !string.IsNullOrWhiteSpace(c) && TriggerSet.Contains(SecureRootInheritance.NormalizeColumn(c)));
    }

    /// <summary>
    /// The pre-write check: <c>null</c> when the update may be written; otherwise the refusal (<see cref="RecordOwnerResolution.IsRefused"/>),
    /// and nothing may be written. An update that does not <see cref="Triggers"/> reads nothing. Otherwise the reference columns that
    /// exist in metadata (<see cref="PrivateContextColumns.PresentColumnsAsync"/>, cached) are read through
    /// <paramref name="appOnlyEntities"/>, the app-only service, so a caller's own read rights decide nothing: none exists → passes;
    /// a set reference → <see cref="RecordOwnerRefusal.PrivateMessageNotFileable"/>; a fault of either read →
    /// <see cref="CheckUnavailable"/>. Logs name the row and the column names, never a value. The caller's cancellation propagates.
    /// </summary>
    internal static async Task<RecordOwnerResolution?> RefuseUpdateAsync(
        IGenericEntityService appOnlyEntities, string table, Guid recordId, IEnumerable<string> columns, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(appOnlyEntities);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(logger);

        var written = columns.ToArray();
        if (!Triggers(table, written))
            return null;

        Entity row;
        try
        {
            var present = await PrivateContextColumns.PresentColumnsAsync(appOnlyEntities, ct).ConfigureAwait(false);
            if (present.Count == 0)
                return null;

            row = await appOnlyEntities.RetrieveAsync(CommunicationTable, recordId, present.ToArray(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "Refusing the update of {Entity} {RecordId}: whether it is a private message could not be read ({Code}). The change " +
                "was NOT written.", CommunicationTable, recordId, CheckUnavailable);
            return RecordOwnerResolution.Refused(
                CheckUnavailable,
                "whether this message is a private message could not be checked; try again");
        }

        if (row is null || !PrivateContextColumns.IsPrivate(row))
            return null;

        logger.LogWarning(
            "Refusing the update of {Entity} {RecordId}: it is a private message, which is never filed or moved to another thread " +
            "({Code}; columns {Columns}). The change was NOT written.",
            CommunicationTable, recordId, RecordOwnerRefusal.PrivateMessageNotFileable,
            string.Join(",", written.Where(c => TriggerSet.Contains(SecureRootInheritance.NormalizeColumn(c)))));
        return RecordOwnerResolution.Refused(
            RecordOwnerRefusal.PrivateMessageNotFileable,
            "a private message stays between its two participants: it is never filed to a record and never moved to another thread");
    }
}
