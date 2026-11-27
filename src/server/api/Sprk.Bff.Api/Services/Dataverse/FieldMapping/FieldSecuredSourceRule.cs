using System.Text.Json;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// The field-secured SOURCE rule of field mapping (owner 2026-10-27 boundary 3, task 064 on create, DI-051 / #3258 on push;
/// narrowed by the owner 2026-11-26, task 065): a value read from a field-secured source column is not copied by field mapping
/// unless the row it is copied into is filed under the source. A rule that reads such a column is not applied and the column it
/// would have filled is named in a warning. Anything other than an explicit <c>IsSecured == false</c> counts as secured (fail
/// closed).
/// </summary>
/// <remarks>
/// <para>Lifted only for rows filed under the source: on create, when the mapping source is the new row's filing parent (table
/// and id); on push, for rows filed under the source by a listed Filing edge (family mode, or an ordinary push whose parent
/// lookup is that edge's column). Other related rows keep the rule. A lifted value is still read as the caller, so it copies only
/// when the caller can read it, and a copied value is then readable by whoever can read the target column.</para>
/// <para>A system-principal mapping (the Repair pass) never copies a field-secured source column, even from the filing parent:
/// it has no caller to mask the read against.</para>
/// <para>The ONE definition, asked by both writers that apply mapping rules: the unified create (<c>RecordCreationService</c>,
/// step 3) and the field-mapping push (<c>FieldMappingEndpoints.PushFieldMappingsAsync</c>, step 2a).
/// <c>FieldSecuredSourceRuleGuardTests</c> keeps both on it and keeps a second copy of the predicate out of the field-mapping
/// sources.</para>
/// <para>Pure: it reads nothing. Each writer reads its source table's attribute metadata (<c>IsSecured</c>) as the caller and
/// hands the answer here.</para>
/// </remarks>
internal static class FieldSecuredSourceRule
{
    /// <summary>The metadata property this rule reads.</summary>
    internal const string IsSecuredProperty = "IsSecured";

    /// <summary>Why a value copied from a field-secured source column is not written: words for the caller, never the value.</summary>
    internal const string NotCopiedReason = "the column it would be copied from is field-secured";

    /// <summary>
    /// Whether a column whose metadata answered <paramref name="isSecured"/> counts as field-secured: anything but an explicit
    /// JSON <c>false</c> (a missing, null, non-boolean or <c>true</c> answer) does. The one predicate.
    /// </summary>
    internal static bool CountsAsSecured(JsonElement? isSecured) =>
        isSecured is not { ValueKind: JsonValueKind.False };

    /// <summary>
    /// <paramref name="rules"/> split into the rules field mapping may apply and the rules that read a column of
    /// <paramref name="securedSourceColumns"/> (never applied). The source columns of a rule are the engine's own
    /// (<see cref="CreateTimeFieldMapping.CollectSourceColumns"/>), so a Concat or Template rule is caught by any placeholder.
    /// An empty or absent set applies every rule; a caller that could not read the set maps nothing and does not ask. Asked
    /// only when the target row is not filed under the source. Pure.
    /// </summary>
    internal static (List<FieldMappingRuleEntity> Copyable, List<FieldMappingRuleEntity> SecuredSourced) Split(
        IReadOnlyList<FieldMappingRuleEntity> rules, IReadOnlySet<string>? securedSourceColumns)
    {
        if (securedSourceColumns is not { Count: > 0 })
            return ([.. rules], []);

        var copyable = new List<FieldMappingRuleEntity>(rules.Count);
        var securedSourced = new List<FieldMappingRuleEntity>();
        foreach (var candidate in rules)
        {
            if (CreateTimeFieldMapping.CollectSourceColumns([candidate]).Any(securedSourceColumns.Contains))
                securedSourced.Add(candidate);
            else
                copyable.Add(candidate);
        }

        return (copyable, securedSourced);
    }
}
