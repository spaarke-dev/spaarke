using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// fm task 056b (design note condition C5) — the three private-context references of a communication
/// (<see cref="PrivateContextColumns.All"/>) are BFF-owned columns (<see cref="InheritedAccessPermission.BffOwnedCommunicationColumns"/>).
/// Create-time field mapping asks <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> as an app-only writer, which refuses a
/// BFF-owned column for every identity, so a Copy or Default rule aimed at one is dropped with
/// <see cref="RecordCreationWarning.MappingRuleSkippedCode"/> naming the refusal code, and writes nothing. Must fire: each column,
/// each rule kind, in any case. Must not fire: an ordinary column of the same profile is still filled. The push half of C5 is in
/// <c>CommunicationBffOwnedColumnRefusalTests</c> (data-mutation).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    public static TheoryData<string, int> PrivateContextColumnsByRuleKind()
    {
        var data = new TheoryData<string, int>();
        foreach (var column in PrivateContextColumns.All)
        {
            data.Add(column, Copy);
            data.Add(column, Default);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PrivateContextColumnsByRuleKind))]
    public async Task CreateAsync_ACommunicationRuleTargetingAPrivateContextReference_IsDroppedWithTheBffColumnCode_AndAnOrdinaryRuleIsFilled(
        string column, int ruleKind)
    {
        _user.Columns["sprk_communication"] = new()
        {
            [column] = ("Lookup", true),
            ["sprk_subject"] = ("String", true),
            ["sprk_body"] = ("Memo", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
        };
        var privateRule = ruleKind == Copy
            ? Rule("sprk_matterdescription", $" {column.ToUpperInvariant()} ", Lookup)
            : Rule(null, $" {column.ToUpperInvariant()} ", Lookup, Default, Guid.NewGuid().ToString("D"));
        Profile("sprk_matter", "sprk_communication", privateRule, Rule("sprk_matterdescription", "sprk_body", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_communication", new Dictionary<string, object?>
            {
                ["sprk_subject"] = "Status",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            },
                mapping: true, ("sprk_matter", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => Normalized(k).StartsWith(Normalized(column), StringComparison.Ordinal),
            $"{column} is the BFF's own: a mapping rule never writes it (must fire)");
        StringOf(fields, "sprk_body").Should().Be("From the matter", "an ordinary column of the same profile is still mapped (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode
                && w.Detail.Contains(column, StringComparison.OrdinalIgnoreCase)
                && w.Detail.Contains(InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode));
    }
}
