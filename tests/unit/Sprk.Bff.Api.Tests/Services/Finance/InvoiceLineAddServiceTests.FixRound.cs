using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
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

    // =========================================================================================
    // F2: a repeat finds its own row before the status, firm and correction refusals; step 8 always runs
    // =========================================================================================

    public static TheoryData<string> MovedOn() => new() { "returned", "rejected", "correction pending", "firm held" };

    [Theory(DisplayName = "164a fix round F2 must-fire: an add whose record step failed is finished by its repeat after the invoice moved on (one record)")]
    [MemberData(nameof(MovedOn))]
    public async Task ARepeat_AfterTheInvoiceMovedOn_StillWritesTheRecord(string change)
    {
        var add = new AddLineWorld();
        var service = add.Service(Cap);
        var command = Fee(hours: 13m, date: new DateOnly(2026, 9, 14));
        add.H.LookupFault = new TimeoutException();
        var failed = await service.AddAsync(command, CancellationToken.None);
        failed.Status.Should().Be(AddLineStatus.Incomplete);
        failed.FailedStep.Should().Be(InvoiceLineAddService.RecordStep);
        add.H.LookupFault = null;
        add.DecisionRecords().Should().BeEmpty("the setup: the record was not written");
        var totalsWrites = add.TotalsWrites.Count;
        var signalWrites = add.World.SignalWrites.Count;

        var inReview = true;
        switch (change)
        {
            case "returned":
                add.World.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.ReturnedToFirm);
                inReview = false;
                break;
            case "rejected":
                add.World.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Rejected);
                inReview = false;
                break;
            case "correction pending":
                add.World.AddRow(new Entity("sprk_invoicecorrection", Guid.NewGuid())
                {
                    ["sprk_invoice"] = new EntityReference("sprk_invoice", W.InvoiceId),
                    ["sprk_invoiceline"] = new EntityReference("sprk_billingevent", W.FeeLineId),
                    ["sprk_fieldname"] = "Hours",
                    ["sprk_aivalue"] = "25",
                    ["sprk_finalvalue"] = "2.5",
                    ["sprk_outcometype"] = new OptionSetValue(InvoiceCorrectionService.OutcomeCorrected),
                    ["createdon"] = W.T0,
                });
                break;
            default:
                add.World.Invoice[InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceRerunPendingValue;
                break;
        }

        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied, repeat.Detail);
        repeat.AlreadyApplied.Should().BeTrue();
        var record = add.DecisionRecords().Should().ContainSingle("the repeat writes the add-line record, once").Subject;
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(InvoiceLineAddService.ActionCode);
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(W.Reviewer);
        add.World.Lines.Should().ContainSingle(l => l.Id == ExpectedLineId());
        if (inReview)
        {
            add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateDone, "In Review: steps 7 and 9 run");
        }
        else
        {
            add.TotalsWrites.Should().HaveCount(totalsWrites, "not In Review: step 7 does not run");
            add.World.SignalWrites.Should().HaveCount(signalWrites, "not In Review: step 9 does not run");
            add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateCommitted);
        }
    }

    [Theory(DisplayName = "164a fix round F2 must-not-fire: a FIRST add on an invoice that is not In Review is still refused, nothing written")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm)]
    [InlineData(InvoiceStatusCodes.Rejected)]
    public async Task AFirstAdd_OnAnInvoiceNotInReview_IsStillRefused(int status)
    {
        var add = new AddLineWorld();
        add.World.Invoice["statuscode"] = new OptionSetValue(status);
        var lines = add.World.Lines.Count;

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.NotInReview);
        add.Transactions.Should().BeEmpty();
        add.World.Lines.Should().HaveCount(lines);
        add.DecisionRecords().Should().BeEmpty();
        add.TotalsWrites.Should().BeEmpty();
        add.RecorderIdentity.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "164a fix round F2: step 8 runs even when step 7 fails; the answer names the totals step, step 9 waits for the repeat")]
    public async Task TheRecordStep_RunsEvenWhenTheTotalsFail()
    {
        var add = new AddLineWorld();
        var service = add.Service(Cap);
        var command = Fee(hours: 13m, date: new DateOnly(2026, 9, 14));
        add.TotalsFault = new TimeoutException();

        var failed = await service.AddAsync(command, CancellationToken.None);

        failed.Status.Should().Be(AddLineStatus.Incomplete);
        failed.FailedStep.Should().Be(InvoiceLineAddService.TotalsStep);
        add.DecisionRecords().Should().ContainSingle("the record does not depend on the totals");
        add.World.SignalWrites.Should().BeEmpty("step 9 is left to the repeat");
        add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateCommitted);

        add.TotalsFault = null;
        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied, repeat.Detail);
        add.DecisionRecords().Should().ContainSingle();
        add.World.SignalWrites.Should().Contain((Cap.PolicyCode, ExpectedLineId(), 1));
    }

    // =========================================================================================
    // Another decider (review F3 access; K2 sprk_createdbyperson) and the over-broad id_reused (K2)
    // =========================================================================================

    [Fact(DisplayName = "164a fix round: the same command repeated by another decider is id_reused; exactly one record, naming the first caller")]
    public async Task ARepeat_ByAnotherDecider_IsIdReused_AndTheRecordNamesTheFirstCaller()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        var other = Guid.Parse("16400000-0000-4000-8000-0000000000b9");

        var repeat = await service.AddAsync(Expense() with { DeciderSystemUserId = other }, CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.IdReused);
        add.DecisionRecords().Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(W.Reviewer);
    }

    [Fact(DisplayName = "164a fix round K2 must-fire: when the first attempt wrote no record, another decider's repeat is id_reused and writes none naming it")]
    public async Task ARepeat_ByAnotherDecider_BeforeTheRecord_WritesNoRecord()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        add.H.LookupFault = new TimeoutException();
        (await service.AddAsync(Expense(), CancellationToken.None)).FailedStep.Should().Be(InvoiceLineAddService.RecordStep);
        add.H.LookupFault = null;
        var other = Guid.Parse("16400000-0000-4000-8000-0000000000b9");

        var foreign = await service.AddAsync(Expense() with { DeciderSystemUserId = other }, CancellationToken.None);

        foreign.Status.Should().Be(AddLineStatus.IdReused);
        add.DecisionRecords().Should().BeEmpty("no record names a caller who did not add the line");

        var own = await service.AddAsync(Expense(), CancellationToken.None);

        own.Status.Should().Be(AddLineStatus.Applied, own.Detail);
        add.DecisionRecords().Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(W.Reviewer);
    }

    [Fact(DisplayName = "164a fix round K2: a repeat whose recorded content cannot be read is not id_reused (it does not ask for a new request id)")]
    public async Task ARepeat_WhoseRecordedContentIsUnreadable_IsNotIdReused()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        var record = add.DecisionRecords().Should().ContainSingle().Subject;
        var steps = JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!.AsObject();
        steps.Remove("reviewHash").Should().BeTrue("the setup: the record stores its content hash");
        record["sprk_steps"] = steps.ToJsonString();

        var repeat = await service.AddAsync(Expense(), CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Incomplete);
        repeat.Code.Should().NotBe(AddLineCodes.IdReused);
        repeat.FailedStep.Should().Be(InvoiceLineAddService.RecordStep);
        add.DecisionRecords().Should().ContainSingle("nothing is written over it");
    }

    // =========================================================================================
    // uac-r2 condition: the add-line record always carries its regarding invoice-line link
    // =========================================================================================

    [Theory(DisplayName = "uac-r2: every add-line Decision Record carries the regarding invoice-line link (the added line), Fee and Expense")]
    [InlineData("fee")]
    [InlineData("expense")]
    public async Task TheAddLineRecord_AlwaysCarriesTheRegardingLineLink(string kind)
    {
        var add = new AddLineWorld();

        (await add.Service().AddAsync(kind == "fee" ? Fee() : Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);

        var record = add.DecisionRecords().Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Should().NotBeNull();
        record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(ExpectedLineId());
        record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").LogicalName.Should().Be("sprk_billingevent");
    }

    // =========================================================================================
    // F3 (T7): the added line through InvoiceApprovalService (I-2) and Accept all Clean, and the EB-D-199 total at approval
    // =========================================================================================

    [Theory(DisplayName = "T7 (I-2) seam: an added line Flagged is 409 lines_open (one Flagged); Accept all Clean leaves it; once accepted or reduced, approve stores billed + its contribution (EB-D-199)")]
    [InlineData(LineDecisionAction.Accept, null, 1200)]
    [InlineData(LineDecisionAction.Reduce, 150, 1150)]
    public async Task T7_ApprovalWithAnAddedLine_IsRefusedUntilDecided_ThenStoresTheAddedLineRule(LineDecisionAction action, int? approvedAmount, int expectedApproved)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        var clean = w.AddLine(InvoiceLineStatus.Clean, 600m);
        var addDecision = Guid.Parse("16400000-0000-4000-8000-0000000000d7");
        // The row as Add line commits it: the derived id on this invoice, the marker, Flagged (R10). The firm billed 1,000 (the world's invoice).
        var added = w.AddLine(
            InvoiceLineStatus.Flagged, 200m, extraction: ExtractionProvenance.ForReviewerAddedLine(addDecision),
            id: ExtractionProvenance.AddedLineIdFor(w.InvoiceId, addDecision));
        ExtractionProvenance.IsReviewerAdded(added).Should().BeTrue("the setup: an added line");
        await w.RecordAddedLineAsync(added.Id, addDecision); // task 165b: Add line's own record (the line's known origin)

        var refused = await w.ApproveAs(ApprovalWorld.AliceUser);

        refused.Outcome.Should().Be(ApprovalOutcome.LinesOpen);
        refused.OpenLines.Should().Be(new ApprovalOpenLinesDto(0, 1), "the added line is the one Flagged line");

        var acceptClean = await w.LineService().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), ApprovalWorld.BobUser, CancellationToken.None, ApprovalWorld.CallerToken);

        acceptClean.IsApplied.Should().BeTrue(acceptClean.Detail);
        w.Row("sprk_billingevent", clean.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        w.Row("sprk_billingevent", added.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value
            .Should().Be((int)InvoiceLineStatus.Flagged, "Accept all Clean selects Clean lines only");
        (await w.ApproveAs(ApprovalWorld.AliceUser)).OpenLines.Should().Be(new ApprovalOpenLinesDto(0, 1), "still open until decided");

        var decided = await w.LineService().DecideAsync(
            new LineDecisionCommand(
                w.InvoiceId, Guid.NewGuid(), action, action == LineDecisionAction.Accept ? null : "Only part of the work is chargeable",
                [new LineDecisionLine(added.Id, approvedAmount is { } a ? a : null)], ApprovalWorld.BobUser, Bulk: false, ApprovalWorld.CallerToken),
            CancellationToken.None);
        decided.IsApplied.Should().BeTrue(decided.Detail);

        var approved = await w.ApproveAs(ApprovalWorld.AliceUser);

        approved.Outcome.Should().Be(ApprovalOutcome.Approved, approved.Code);
        w.Invoice.GetAttributeValue<Money>("sprk_approvedtotal").Value.Should().Be(expectedApproved, "EB-D-199: billed 1,000 + the added line's contribution");
        w.Portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(expectedApproved);
    }
}
