using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// ebilling-r1 task 095d (uac-r2): <c>sprk_invoicesubmission.sprk_previoussequence</c> is a mapping-protected column
/// (<see cref="RecordTableRule.MappingProtectedColumns"/>): only <see cref="TrackingNumberAllocator"/>'s renumber writes it (it is an access
/// column in the parent lineage). Same two seams as the findings column: a post-mapping default is checked against the protected set alone
/// (must fire if the entry is removed), and the set is per table (must not fire).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    [Fact(DisplayName = "095d: a post-mapping default aimed at the submission's previous sequence is an argument error (mapping never writes it)")]
    public async Task PostMappingDefault_TheSubmissionsPreviousSequence_IsAnArgumentError()
    {
        var column = TrackingNumberAllocator.PreviousSequenceColumn;

        var act = () => Service().CreateAsync(
            Request("sprk_invoicesubmission", new Dictionary<string, object?>
            {
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            },
                mapping: true) with
            { PostMappingDefaults = new Dictionary<string, object?> { [column] = 1 } },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(column).And.Contain("field mapping never writes it");
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d: only the submission protects the previous-sequence column from mapping, and its other columns stay mappable")]
    public void ProtectedFromMapping_ThePreviousSequence_IsOnTheSubmissionOnly()
    {
        var column = TrackingNumberAllocator.PreviousSequenceColumn;

        RecordCreationService.ProtectedFromMapping("sprk_invoicesubmission", RecordTableRules.Get("sprk_invoicesubmission"), null)
            .Should().Contain(column);
        RecordCreationService.ProtectedFromMapping("sprk_invoicesubmission", RecordTableRules.Get("sprk_invoicesubmission"), null)
            .Should().NotContain("sprk_processingstatus", "an ordinary column stays mappable");
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().NotContain(column, "only the submission carries the previous sequence");
    }
}
