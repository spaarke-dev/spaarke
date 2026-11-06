using Spaarke.Dataverse;
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
/// class of a column is decided by the registry the caller supplies, never by a list kept here.</para>
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
    /// <param name="isAssignedToColumn">Whether (table, column) is an Assigned-To column of the access registry. Null reads as
    /// "none are", which is the answer for a host with no registry.</param>
    internal static Selected Select(
        IEnumerable<FieldMappingRuleEntity> rules,
        string targetEntity,
        bool includeAssignedTo,
        Func<string, string, bool>? isAssignedToColumn)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var selected = new List<FieldMappingRuleEntity>();
        var skipped = 0;
        foreach (var rule in rules)
        {
            if (rule.MappingType != CreateTimeFieldMapping.MappingTypeCopy)
            {
                skipped++;
                continue;
            }

            var assignedTo = isAssignedToColumn?.Invoke(targetEntity, rule.TargetField ?? string.Empty) ?? false;
            var applies = assignedTo ? includeAssignedTo : rule.SyncMode == SyncMode.ManualRefresh;
            if (applies)
                selected.Add(rule);
            else
                skipped++;
        }

        return new Selected(selected, skipped);
    }
}
