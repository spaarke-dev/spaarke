using System.Text.Json;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// The field-secured SOURCE rule of field mapping (owner 2026-10-27 boundary 3; task 064 on create, DI-051 / #3258 on push): a
/// value read from a field-secured source column is never copied by field mapping, because a secured value written into an
/// unsecured column would be readable by everyone who can read that column. A rule that reads such a column is not applied and
/// the column it would have filled is named in a warning. Anything other than an explicit <c>IsSecured == false</c> counts as
/// secured (fail closed).
/// </summary>
/// <remarks>
/// <para>The ONE definition, asked by both writers that apply mapping rules: the unified create
/// (<c>RecordCreationService</c>, step 3) and the field-mapping push (<c>FieldMappingEndpoints.PushFieldMappingsAsync</c>,
/// step 2). <c>FieldSecuredSourceRuleGuardTests</c> keeps both on it and keeps a second copy of the predicate out of the
/// tree.</para>
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
    /// An empty or absent set applies every rule; a caller that could not read the set maps nothing and does not ask. Pure.
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
