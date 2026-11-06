using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 164a fix round: the findings of the two reviews of 164a-4b (notes/164a-4b-steps-7-9.md, "Fix round").
public sealed partial class InvoiceLineAddServiceTests
{
    // =========================================================================================
    // F1: Add line (steps 1 and 9) and the correction re-evaluation read only the invoice's OWN submission
    // =========================================================================================

    /// <summary>A submission row that links the invoice but is not the one its id derives from, created after the own one.</summary>
    private static Entity OtherLinkedSubmission(CorrectionWorld world)
    {
        var other = new Entity("sprk_invoicesubmission", Guid.Parse("16400000-0000-4000-8000-0000000000f9"))
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", W.InvoiceId),
            ["sprk_findingsjson"] = W.HeaderFindingsJson,
            ["createdon"] = W.T0,
            ["versionnumber"] = 7L,
        };
        InvoiceSubmissionPersister.InvoiceIdFor(other.Id).Should().NotBe(W.InvoiceId, "the setup: not the invoice's own submission");
        world.OtherSubmissions.Add(other);
        return other;
    }

    [Fact(DisplayName = "164a fix round F1: the shared submission read returns the invoice's own submission, never a newer linked row; none of its own is null")]
    public async Task TheSubmissionRead_TakesOnlyTheOwnSubmission()
    {
        var add = new AddLineWorld();
        OtherLinkedSubmission(add.World);

        (await InvoiceCorrectionReevaluation.ReadSubmissionAsync(add.World.Entities.Object, W.InvoiceId, CancellationToken.None))!
            .Id.Should().Be(W.SubmissionId, "must-not-fire: the own submission is read even with a newer linked row first");

        add.World.Submission = null;
        (await InvoiceCorrectionReevaluation.ReadSubmissionAsync(add.World.Entities.Object, W.InvoiceId, CancellationToken.None))
            .Should().BeNull("must-fire: a linked row that is not the own submission is never returned");
    }

    [Fact(DisplayName = "164a fix round F1 must-fire: Add line reads and writes the own submission; a newer linked row is never written")]
    public async Task AddLine_WritesOnlyTheOwnSubmission()
    {
        var add = new AddLineWorld();
        var other = OtherLinkedSubmission(add.World);
        var otherBefore = W.Copy(other);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.Transactions.SelectMany(t => t).OfType<UpdateRequest>().Should().NotContain(u => u.Target.Id == other.Id, "steps 6 and 9 write the own submission only");
        other.GetAttributeValue<string>("sprk_findingsjson").Should().Be(otherBefore.GetAttributeValue<string>("sprk_findingsjson"));
        other.GetAttributeValue<long>("versionnumber").Should().Be(7L);
        add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateDone, "control: the own submission");
    }

    [Fact(DisplayName = "164a fix round F1 must-fire: with only another linked row and no own submission, Add line is Unavailable and writes nothing")]
    public async Task AddLine_WithNoOwnSubmission_IsUnavailable_AndWritesNothing()
    {
        var add = new AddLineWorld();
        OtherLinkedSubmission(add.World);
        add.World.Submission = null;
        var lines = add.World.Lines.Count;

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Unavailable);
        add.Transactions.Should().BeEmpty();
        add.World.Lines.Should().HaveCount(lines);
        add.DecisionRecords().Should().BeEmpty();
    }
}
