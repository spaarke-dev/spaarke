using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// ebilling-r1 task 095d-1c (uac-r2 final review F4-1): <c>sprk_invoicesubmission.sprk_findingsjson</c> is a mapping-protected column
/// (<see cref="RecordTableRule.MappingProtectedColumns"/>): its stored <c>assignFirm</c> member decides the immutable Decision Record, and
/// only typed writers write it. A post-mapping default is checked against that protected set alone (it is not the app-only refusal), so it is the
/// seam through the real create service that fails if the entry is removed (must fire). The set is per table (must not fire).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    [Fact(DisplayName = "095d-1c F4-1: a post-mapping default aimed at the submission's stored findings is an argument error (mapping never writes it)")]
    public async Task PostMappingDefault_TheSubmissionsFindingsJson_IsAnArgumentError()
    {
        var column = InvoiceSubmissionPersister.SubmissionFindingsJson;

        var act = () => Service().CreateAsync(
            Request("sprk_invoicesubmission", new Dictionary<string, object?>
            {
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            },
                mapping: true) with
            { PostMappingDefaults = new Dictionary<string, object?> { [column] = "{}" } },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(column).And.Contain("field mapping never writes it");
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1c F4-1: only the submission protects the findings column from mapping, and its other columns stay mappable")]
    public void ProtectedFromMapping_TheFindingsJson_IsOnTheSubmissionOnly()
    {
        var column = InvoiceSubmissionPersister.SubmissionFindingsJson;

        RecordCreationService.ProtectedFromMapping("sprk_invoicesubmission", RecordTableRules.Get("sprk_invoicesubmission"), null)
            .Should().Contain(column);
        RecordCreationService.ProtectedFromMapping("sprk_invoicesubmission", RecordTableRules.Get("sprk_invoicesubmission"), null)
            .Should().NotContain("sprk_processingstatus", "an ordinary column stays mappable");
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().NotContain(column, "only the submission carries the findings");
    }
}
