using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// Which of a profile's rules a push applies (field-mapping-server-write-path-r1 task 040, FR-23, FR-34).
/// </summary>
/// <remarks>
/// <para>A push applies <b>Copy</b> rules whose <c>sprk_syncmode</c> is <b>Manual Refresh</b>, and nothing else: a Default, a
/// Concat or a Template rule is a create-time rule, and a One Time rule is not meant to be re-applied. The skipped rules are
/// counted, never an error.</para>
/// <para><b>Assigned-To columns</b> (the columns the access registry reads: <c>MembershipOptions.AccessConferringRoles</c>) are
/// set apart because writing one confers or removes access (UAC-r2 4a). They are left out of an ordinary push whatever their
/// sync mode, and added to it — as Copy rules, whatever their sync mode — when the request says <c>includeAssignedTo</c>. The
/// class of a column is decided by <see cref="AssignedToColumnsOf"/> over the BOUND registry, for any table — the one classifier.
/// The sync-mode script's Assigned-To table and the provisioning seeder's seeded sync modes are pinned to the same answer by
/// tests (<c>SyncModeMigrationClassDataTests</c>, <c>FieldMappingSeederSyncModeClassTests</c>).</para>
/// <para><b>Display copies</b> (task 032b-1; <see cref="MembershipOptionsDefaults.DisplayCopyColumns"/>: the invoice's four
/// attorney and paralegal columns, which confer no access) are never pushed, whatever their sync mode and whatever
/// <c>includeAssignedTo</c> says; they are counted with the skipped rules. Only the create maps them (no re-map). This is enforced
/// here, so it does not depend on the rules' stored sync mode.</para>
/// </remarks>
internal static class PushRuleSelection
{
    /// <summary>The rules to apply, in the order given, and how many of the profile's rules were not applied.</summary>
    internal sealed record Selected(IReadOnlyList<FieldMappingRuleEntity> Rules, int SkippedCount);

    /// <summary>
    /// Selects the rules a push applies.
    /// </summary>
    /// <param name="rules">The profile's active rules.</param>
    /// <param name="targetEntity">The table the push writes to.</param>
    /// <param name="includeAssignedTo">True when the request asked for the Assigned-To rules too.</param>
    /// <param name="isAssignedToColumn">Whether (table, column) is an Assigned-To column: <see cref="AssignedToColumnsOf"/>.</param>
    internal static Selected Select(
        IEnumerable<FieldMappingRuleEntity> rules,
        string targetEntity,
        bool includeAssignedTo,
        Func<string, string, bool> isAssignedToColumn)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(isAssignedToColumn);
        var selected = new List<FieldMappingRuleEntity>();
        var skipped = 0;
        foreach (var rule in rules)
        {
            if (rule.MappingType != CreateTimeFieldMapping.MappingTypeCopy)
            {
                skipped++;
                continue;
            }

            if (IsDisplayCopy(targetEntity, rule.TargetField))
            {
                // Task 032b-1: a display copy is written by the create only; a push never re-maps it, whatever its sync mode
                // and whether or not the request asked for the Assigned-To rules.
                skipped++;
                continue;
            }

            var assignedTo = isAssignedToColumn(targetEntity, rule.TargetField ?? string.Empty);
            var applies = assignedTo ? includeAssignedTo : rule.SyncMode == SyncMode.ManualRefresh;
            if (applies)
                selected.Add(rule);
            else
                skipped++;
        }

        return new Selected(selected, skipped);
    }

    /// <summary>
    /// The one Assigned-To classifier: whether (table, column) is listed for that table in <paramref name="registry"/>
    /// (<c>MembershipOptions.AccessConferringRoles</c>, the registry the access system reads), for ANY table — a root, an event,
    /// an invoice. Names are compared trimmed and case-insensitively, and a bind key (<c>sprk_x@odata.bind</c>) reads as its
    /// column. Every entry with a column name counts, whatever identity type it declares: a column is never treated as plain
    /// data because its entry is malformed.
    /// </summary>
    /// <param name="registry">The bound registry. An empty one (a host that never bound it, so the defaults'
    /// <c>PostConfigure</c> did not run) reads as the canonical registry, the seed every host starts from — never as "none".</param>
    internal static Func<string, string, bool> AssignedToColumnsOf(AccessConferringRegistry? registry)
    {
        IEnumerable<(string Table, IEnumerable<string?> Columns)> entries = registry?.Entities is { Count: > 0 } bound
            ? bound.Select(kv => (kv.Key, kv.Value?.Select(c => c?.Field) ?? []))
            : MembershipOptionsDefaults.CanonicalAccessConferringRegistry.Select(kv => (kv.Key, kv.Value.Select(c => (string?)c.Field)));

        var columns = new HashSet<(string, string)>();
        foreach (var (table, fields) in entries)
        {
            foreach (var field in fields)
            {
                if (!string.IsNullOrWhiteSpace(table) && !string.IsNullOrWhiteSpace(field))
                    columns.Add((Normalize(table), Normalize(field)));
            }
        }

        return (table, column) => columns.Contains((Normalize(table), Normalize(column)));
    }

    /// <summary>
    /// Whether (table, column) is a display copy (<see cref="MembershipOptionsDefaults.DisplayCopyColumns"/>, task 032b-1): an
    /// <c>sprk_assigned*</c> value copied at create that confers no access and is never pushed. Names are compared as
    /// <see cref="AssignedToColumnsOf"/> compares them.
    /// </summary>
    internal static bool IsDisplayCopy(string? table, string? column) =>
        MembershipOptionsDefaults.DisplayCopyColumns.TryGetValue(Normalize(table), out var columns)
        && columns.Contains(Normalize(column));

    private static string Normalize(string? name) => (name ?? string.Empty).Split('@')[0].Trim().ToLowerInvariant();
}
