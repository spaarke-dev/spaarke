using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// ebilling-r1 task 165d (EB-D-207): the AP export claim (<c>sprk_invoice.sprk_billingexport</c>, <see cref="ApExportService.BatchLookup"/>) is a
/// mapping-protected column (<see cref="RecordTableRule.MappingProtectedColumns"/>) and a writer-only one
/// (<see cref="RecordTableRule.WriterOnlyColumns"/>): only AP export sets and clears it. Same seams as the invoice amounts: a post-mapping default
/// is checked against the protected set alone (must fire if the entry is removed), the set is per table (must not fire), and a create-time rule
/// aimed at the column is skipped with the existing warning while an unprotected target still maps.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    [Fact(DisplayName = "165d: a post-mapping default aimed at the invoice's AP export claim is an argument error (mapping never writes it)")]
    public async Task PostMappingDefault_TheApExportClaim_IsAnArgumentError()
    {
        var act = () => Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
                {
                    ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                },
                mapping: true) with { PostMappingDefaults = new Dictionary<string, object?> { [ApExportService.BatchLookup] = 1 } },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message
            .Should().Contain(ApExportService.BatchLookup).And.Contain("field mapping never writes it");
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "165d: only the invoice protects the AP export claim from mapping, and its other columns stay mappable")]
    public void ProtectedFromMapping_TheApExportClaim_IsOnTheInvoiceOnly()
    {
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().Contain(ApExportService.BatchLookup);
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().NotContain("sprk_description", "an ordinary column stays mappable");
        RecordCreationService.ProtectedFromMapping("sprk_billingevent", RecordTableRules.Get("sprk_billingevent"), null)
            .Should().NotContain(ApExportService.BatchLookup, "only the invoice carries the claim");
    }

    [Fact(DisplayName = "165d: a create-time rule aimed at the AP export claim is skipped with the warning, and an unprotected target of the same profile still maps")]
    public async Task CreateAsync_ARuleTargetingTheApExportClaim_IsSkippedWithTheWarning_AndAnOrdinaryRuleIsStillFilled()
    {
        _user.Columns["sprk_invoice"][ApExportService.BatchLookup] = ("String", true);
        Profile("sprk_matter", "sprk_invoice",
            Rule(null, $"  {ApExportService.BatchLookup.ToUpperInvariant()} ", Text, Default, "x"),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-165D",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => Normalized(k).StartsWith(Normalized(ApExportService.BatchLookup), StringComparison.Ordinal),
            "the claim is AP export's alone (must fire)");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "an unprotected target of the same profile still maps (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains(ApExportService.BatchLookup));
    }
}
