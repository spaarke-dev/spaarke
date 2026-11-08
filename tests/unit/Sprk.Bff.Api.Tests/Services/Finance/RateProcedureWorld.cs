using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 085: the Procedure rows the Procedure resolver (<c>ProcedureResolver.ReadAsync</c>) reads for an invoice's rate history, for
/// the test fakes that answer Dataverse reads from fixture rows. A rate now belongs to a Procedure Version that the resolver chose for the line's
/// work date, so a fake that serves rate rows must also serve one Procedure with one version in force and point its rate rows at that version.
/// Test support only: it adds no behaviour under test.
/// </summary>
internal static class RateProcedureWorld
{
    /// <summary>An all-records Procedure in force from <see cref="InForceFrom"/>, and its one Active version.</summary>
    public static readonly Guid ProcedureId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid VersionId = Guid.Parse("00000000-0000-0000-0000-0000000000e2");

    public static readonly DateTime InForceFrom = new(2020, 1, 1);

    public static Entity Procedure(Guid id, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null)
    {
        var e = new Entity("sprk_procedure", id)
        {
            ["sprk_name"] = "Rates",
            ["sprk_area"] = new OptionSetValue(RuleDomains.PolicyDomainBilling),
            ["sprk_status"] = new OptionSetValue(ProcedureEntity.StatusActive),
            ["sprk_appliesto"] = new OptionSetValue((int)appliesTo),
        };
        if (firm is { } f) e["sprk_firm"] = new EntityReference("sprk_organization", f);
        return e;
    }

    public static Entity Version(Guid id, Guid procedure) => new("sprk_procedureversion", id)
    {
        ["sprk_procedure"] = new EntityReference("sprk_procedure", procedure),
        ["sprk_versionnumber"] = 1,
        ["sprk_status"] = new OptionSetValue(ProcedureEntity.VersionStatusActive),
        ["sprk_effectivefrom"] = InForceFrom,
    };

    /// <summary>The standard world: one all-records Procedure with one version in force.</summary>
    public static (List<Entity> Procedures, List<Entity> Versions) Standard() => ([Procedure(ProcedureId)], [Version(VersionId, ProcedureId)]);

    /// <summary>The card's Procedure Version as the rate query returns it (the card link is aliased <c>card</c>).</summary>
    public static AliasedValue CardVersion(Guid version) =>
        new("sprk_ratecard", "sprk_procedureversion", new EntityReference("sprk_procedureversion", version));

    /// <summary>
    /// The rows a resolver read gets, or null when <paramref name="query"/> is not one of the resolver's reads. The core-record chain read
    /// answers the record itself (filed under nothing); the assignment, rule set and policy reads answer nothing.
    /// </summary>
    public static IReadOnlyList<Entity>? AnswerResolverRead(QueryExpression query, IEnumerable<Entity> procedures, IEnumerable<Entity> versions) =>
        query.EntityName switch
        {
            "sprk_procedure" => procedures.ToList(),
            "sprk_procedureversion" => versions.ToList(),
            "sprk_matter" or "sprk_project" or "sprk_workassignment" => ChainRecord(query),
            "sprk_procedureassignment" or "sprk_ruleset" or "sprk_policy" or "sprk_policyversion" => [],
            _ => null,
        };

    private static IReadOnlyList<Entity> ChainRecord(QueryExpression query)
    {
        var id = Flatten(query.Criteria).FirstOrDefault(c => c.AttributeName == query.EntityName + "id")?.Values.OfType<Guid>().FirstOrDefault();
        return id is { } g ? [new Entity(query.EntityName, g)] : [];
    }

    private static IEnumerable<ConditionExpression> Flatten(FilterExpression? filter) =>
        filter is null ? [] : filter.Conditions.Concat(filter.Filters.SelectMany(Flatten));
}
