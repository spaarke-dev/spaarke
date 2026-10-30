using System.Data;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 101 part B1 (FR-31, FR-22, D-64, EB-D-170, EB-D-171, NFR-09): <see cref="InvoiceCorrectionService"/> over the in-memory
/// Dataverse boundary of <see cref="CorrectionWorld"/>, so each test proves the stored rows and values, not the calls the service makes.
/// Out of scope here (part B2): re-evaluation of the billing rules, Signals, findings and line status after a correction.
/// </summary>
public sealed class InvoiceCorrectionServiceTests
{
    private static CorrectionCommand Line(string field, string? value, bool confirm = false, Guid? line = null) =>
        new(W.InvoiceId, line ?? W.FeeLineId, field, value, confirm, W.Reviewer);

    private static CorrectionCommand Header(string field, string? value, bool confirm = false) =>
        new(W.InvoiceId, null, field, value, confirm, W.Reviewer);

    private static void NothingWritten(CorrectionWorld world)
    {
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();
        world.RowTransactions.Should().Be(0);
    }

    // =========================================================================================
    // Corrected / Confirmed rows and the value
    // =========================================================================================

    [Fact]
    public async Task Corrected_WritesOneLabelRow_WithTheAiValueConfidenceSourceAndReviewer_AndUpdatesTheValue()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        result.AlreadyApplied.Should().BeFalse();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.GetAttributeValue<string>("sprk_fieldname").Should().Be("Hours");
        row.GetAttributeValue<string>("sprk_aivalue").Should().Be("25");
        row.GetAttributeValue<string>("sprk_finalvalue").Should().Be("2.5");
        row.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(100000000); // Corrected
        row.GetAttributeValue<bool>("sprk_islabel").Should().BeTrue();
        row.GetAttributeValue<decimal>("sprk_aiconfidence").Should().Be(0.55m); // 046's combined confidence
        row.GetAttributeValue<string>("sprk_sourcecode").Should().Be("sprk_invoice_v1");
        row.GetAttributeValue<string>("sprk_sourceversion").Should().Be("2025-11-01");
        row.GetAttributeValue<string>("sprk_reviewerrole").Should().Be("invoice-reviewer");
        row.GetAttributeValue<DateTime>("sprk_reviewedon").Should().Be(W.T0);
        row.GetAttributeValue<EntityReference>("sprk_reviewedby").Id.Should().Be(W.Reviewer);
        row.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(W.InvoiceId);
        row.GetAttributeValue<EntityReference>("sprk_invoiceline").Id.Should().Be(W.FeeLineId);
        row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(W.Core); // 013: exactly the invoice's single core record
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(2.5m);
        result.Correction!.Outcome.Should().Be("Corrected");
    }

    [Fact]
    public async Task Confirmed_WritesOneLabelRow_AndChangesNoValue()
    {
        var world = new CorrectionWorld();
        var before = W.Copy(world.Line());

        var result = await world.Service().CorrectAsync(Line("Amount", null, confirm: true), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(100000001); // Confirmed individually
        row.GetAttributeValue<bool>("sprk_islabel").Should().BeTrue();
        row.GetAttributeValue<string>("sprk_aivalue").Should().Be("2500.00");
        row.GetAttributeValue<string>("sprk_finalvalue").Should().Be("2500.00");
        world.ValueWrites.Should().BeEmpty();
        world.Line().GetAttributeValue<long>("versionnumber").Should().Be(before.GetAttributeValue<long>("versionnumber"));
    }

    [Fact]
    public async Task TypingTheValueAsRead_IsConfirmed_NotCorrected()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Rate", "100.00"), CancellationToken.None);

        result.Correction!.Outcome.Should().Be("Confirmed");
        world.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ACorrection_NeverTouchesStatusApprovedAmountOrDecisionFields()
    {
        var world = new CorrectionWorld();

        await world.Service().CorrectAsync(Line("Amount", "2000.00"), CancellationToken.None);

        var written = world.ValueWrites.Should().ContainSingle().Subject;
        written.Fields.Keys.Should().BeEquivalentTo(["sprk_amount"]);
        var line = world.Line();
        line.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged);
        line.Contains("sprk_approvedamount").Should().BeFalse();
        line.Contains("sprk_reviewedby").Should().BeFalse();
        line.Contains("sprk_adjustmentreason").Should().BeFalse();
    }

    [Theory]
    [InlineData("Hours", "2.5", "sprk_quantity")]
    [InlineData("Rate", "95.5", "sprk_rate")]
    [InlineData("Amount", "-12.345", "sprk_amount")]
    [InlineData("Narrative", "  Corrected narrative  ", "sprk_description")]
    public async Task EachExtractedLineField_IsCorrectedThroughItsColumn(string field, string value, string column)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line(field, value), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.ValueWrites.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo([column]);
        world.Corrections.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_fieldname").Should().Be(field);
    }

    [Fact]
    public async Task AMoneyValue_IsRoundedToCents_AndTheRowKeepsWhatWasStored()
    {
        var world = new CorrectionWorld();

        await world.Service().CorrectAsync(Line("Amount", "1999.995"), CancellationToken.None);

        world.Line().GetAttributeValue<Money>("sprk_amount").Value.Should().Be(2000.00m);
        world.Corrections.Single().GetAttributeValue<string>("sprk_finalvalue").Should().Be("2000.00");
    }

    // =========================================================================================
    // Retry, row order, the AI value
    // =========================================================================================

    [Fact]
    public async Task RepeatingTheSameCorrection_WritesNoSecondRow_AndNoSecondValueWrite()
    {
        var world = new CorrectionWorld();
        var service = world.Service();

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        var again = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.AlreadyApplied.Should().BeFalse();
        again.IsApplied.Should().BeTrue();
        again.AlreadyApplied.Should().BeTrue();
        again.Correction!.Id.Should().Be(first.Correction!.Id);
        world.Corrections.Should().HaveCount(1);
        world.RowTransactions.Should().Be(1);
        world.ValueWrites.Should().HaveCount(1);
    }

    [Fact]
    public async Task RepeatingAConfirm_WritesNoSecondRow()
    {
        var world = new CorrectionWorld();
        var service = world.Service();

        await service.CorrectAsync(Line("Narrative", null, confirm: true), CancellationToken.None);
        var again = await service.CorrectAsync(Line("Narrative", null, confirm: true), CancellationToken.None);

        again.AlreadyApplied.Should().BeTrue();
        world.Corrections.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheAiValue_IsTheEarliestRowsValue_SoALaterCorrectionAndAReturnToTheReadValueAreLabelledAgainstWhatTheAiRead()
    {
        var world = new CorrectionWorld();
        var service = world.Service();

        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        world.Time.Now = W.T0.AddMinutes(5);
        var second = await service.CorrectAsync(Line("Hours", "3"), CancellationToken.None);
        world.Time.Now = W.T0.AddMinutes(10);
        var back = await service.CorrectAsync(Line("Hours", "25"), CancellationToken.None);

        second.Correction!.AiValue.Should().Be("25"); // not 2.5: the AI read 25
        second.Correction.Outcome.Should().Be("Corrected");
        back.Correction!.Outcome.Should().Be("Confirmed"); // final equals the AI value
        world.Corrections.Should().HaveCount(3, "append-only: each change is a new row");
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(25m);
    }

    [Fact]
    public async Task AWriteThatFailed_ChangesNothing_AndTheRepeatDoesItAll_WithOneRow()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        world.ValueFault = new TimeoutException("simulated");

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Unavailable);
        world.Corrections.Should().BeEmpty("the row and the value are one transaction: a failed write leaves neither");
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(25m);

        world.ValueFault = null;
        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        repeat.AlreadyApplied.Should().BeFalse();
        world.Corrections.Should().HaveCount(1);
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(2.5m);
    }

    [Fact]
    public async Task AStaleRowVersion_Is409LineChanged_AndNeitherTheRowNorTheValueIsWritten()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        world.BeforeValueWrite = () => world.Line()["versionnumber"] = 11L; // another writer got in between the read and the write

        var result = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.Changed);
        result.Code.Should().Be("ebilling.line.changed");
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(25m);
        world.Corrections.Should().BeEmpty("the stale version rolls the row back: no label is left for a value that was not applied (D-64 rows are append-only)");
        world.ValueWrites.Should().BeEmpty();

        // Reload and send the same correction: one row, the value is written at the new version.
        world.BeforeValueWrite = null;
        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        repeat.IsApplied.Should().BeTrue();
        world.Corrections.Should().HaveCount(1);
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(2.5m);
    }

    [Fact]
    public async Task AStaleInvoiceVersion_OnAHeaderCorrection_Is409InvoiceChanged()
    {
        var world = new CorrectionWorld();
        world.BeforeValueWrite = () => world.Invoice["versionnumber"] = 6L;

        var result = await world.Service().CorrectAsync(Header("TotalDue", "2400"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.Changed);
        result.Code.Should().Be("ebilling.invoice.changed");
        world.Corrections.Should().BeEmpty("a stale invoice version rolls the correction row back too");
        world.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ANarrativeLongerThanTheColumn_Is400_BeforeAnyWrite()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Narrative", new string('n', 2001)), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.correction.value_invalid");
        world.Corrections.Should().BeEmpty();
        (await world.Service().CorrectAsync(Line("Narrative", new string('n', 2000)), CancellationToken.None)).IsApplied.Should().BeTrue();
    }

    [Fact]
    public async Task AFailedRowWrite_ChangesNothing_AndIsUnavailable()
    {
        var world = new CorrectionWorld();
        world.RowFault = new TimeoutException("simulated");

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.Unavailable);
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnresolvedOwner_RefusesBeforeAnyWrite()
    {
        var world = new CorrectionWorld();
        world.Owner = RecordOwnerResolution.Refused("no_owner", "test");

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.OwnerUnresolved);
        NothingWritten(world);
    }

    [Fact]
    public async Task AnInvoiceThatNamesTwoCoreRecords_IsRefused_NeverTheFirstOneFound()
    {
        var world = new CorrectionWorld();
        world.Invoice["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()); // #2455: project AND matter

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.OwnerUnresolved);
        NothingWritten(world);
    }

    // =========================================================================================
    // Codes (FR-22)
    // =========================================================================================

    [Fact]
    public async Task ACodeInTheSet_IsStoredInTheCodingJson_NotInTheCodeLookups_AndTheMismatchFollowsIt()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("TaskCode", " l100 "), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var written = world.ValueWrites.Should().ContainSingle().Subject;
        written.Fields.Keys.Should().BeEquivalentTo(["sprk_extractionconfidence"]); // the firm's billed code and the code lookups are not written
        var task = JsonNode.Parse(world.Line().GetAttributeValue<string>("sprk_extractionconfidence"))!["coding"]!["task"]!;
        task["code"]!.GetValue<string>().Should().Be("L100", "stored as the set spells it");
        task["mismatch"]!.GetValue<bool>().Should().BeFalse("the firm billed L100");
        JsonNode.Parse(world.Line().GetAttributeValue<string>("sprk_extractionconfidence"))!["coding"]!["firmCodeMismatch"]!.GetValue<bool>().Should().BeFalse();
        var row = world.Corrections.Single();
        row.GetAttributeValue<string>("sprk_fieldname").Should().Be("TaskCode");
        row.GetAttributeValue<string>("sprk_aivalue").Should().Be("L110");
        row.GetAttributeValue<string>("sprk_finalvalue").Should().Be("L100");
        row.GetAttributeValue<string>("sprk_sourcecode").Should().Be("invoice-line-code");
        row.GetAttributeValue<string>("sprk_sourceversion").Should().Be("1");
        row.GetAttributeValue<decimal>("sprk_aiconfidence").Should().Be(0.77m);
    }

    [Fact]
    public async Task ACodeOutsideTheSet_IsRefused_AndNothingIsWritten()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("TaskCode", "L999"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.CodeNotInSet);
        result.Code.Should().Be("ebilling.code.not_in_set");
        NothingWritten(world);
    }

    [Fact]
    public async Task AnActivityCode_IsCheckedAgainstTheActivityList_NotTheTaskList()
    {
        var world = new CorrectionWorld();

        var wrongKind = await world.Service().CorrectAsync(Line("ActivityCode", "L100"), CancellationToken.None);
        wrongKind.Code.Should().Be("ebilling.code.not_in_set");
        NothingWritten(world);

        var right = await world.Service().CorrectAsync(Line("ActivityCode", "A102"), CancellationToken.None);
        right.IsApplied.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmingTheAiCode_WritesAConfirmedRow_AndNoValueWrite()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("TaskCode", null, confirm: true), CancellationToken.None);

        result.Correction!.Outcome.Should().Be("Confirmed");
        result.Correction.FinalValue.Should().Be("L110");
        world.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ALineThatWasNotCoded_HasNoCodeSet_Conflict()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_extractionconfidence"] = W.ExtractionJson(coded: false);
        world.CodeSets.Clear(); // B2b-1 (D-126): an uncoded line is re-coded from the resolved set; here no set resolves, so the 409 stands

        var result = await world.Service().CorrectAsync(Line("TaskCode", "L100"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.CodeSetUnavailable);
        result.Code.Should().Be("ebilling.correction.code_set_unavailable");
        NothingWritten(world);
    }

    [Fact]
    public async Task ACodeSetThatIsNoLongerInUse_IsConflict()
    {
        var world = new CorrectionWorld();
        world.CodeSets.Clear();

        var result = await world.Service().CorrectAsync(Line("TaskCode", "L100"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.CodeSetUnavailable);
        NothingWritten(world);
    }

    [Fact]
    public async Task AnExpenseCode_IsForExpenseLines_AndFeeCodesAreNotForThem()
    {
        var world = new CorrectionWorld();
        var service = world.Service();

        var feeLineWithExpenseCode = await service.CorrectAsync(Line("ExpenseCode", "E101"), CancellationToken.None);
        var expenseLineWithTaskCode = await service.CorrectAsync(Line("TaskCode", "L100", line: W.ExpenseLineId), CancellationToken.None);
        var hoursOnExpense = await service.CorrectAsync(Line("Hours", "1", line: W.ExpenseLineId), CancellationToken.None);

        foreach (var refused in new[] { feeLineWithExpenseCode, expenseLineWithTaskCode, hoursOnExpense })
        {
            refused.Status.Should().Be(CorrectionStatus.InvalidRequest);
            refused.Code.Should().Be("ebilling.correction.field_not_applicable");
        }

        NothingWritten(world);
        (await service.CorrectAsync(Line("ExpenseCode", "E101", confirm: false, line: W.ExpenseLineId), CancellationToken.None)).IsApplied.Should().BeTrue();
    }

    // =========================================================================================
    // Which lines and invoices
    // =========================================================================================

    [Theory]
    [InlineData((int)InvoiceLineStatus.Accepted)]
    [InlineData((int)InvoiceLineStatus.Reduced)]
    [InlineData((int)InvoiceLineStatus.Disallowed)]
    public async Task ADecidedLine_Is409_AndNothingIsWritten(int status)
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_linestatus"] = new OptionSetValue(status);

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.LineNotCorrectable);
        result.Code.Should().Be("ebilling.correction.line_not_correctable");
        NothingWritten(world);
    }

    [Fact]
    public async Task APendingLine_Is409_AndNothingIsWritten()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Pending);

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.LineNotCorrectable);
        result.Code.Should().Be("ebilling.line.pending");
        NothingWritten(world);
    }

    [Fact]
    public async Task AnAdjustmentLine_IsNotCorrectable()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Adjustment);

        var result = await world.Service().CorrectAsync(Line("Amount", "1"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.LineNotCorrectable);
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.Cancelled)]
    public async Task AnInvoiceNotInReview_Is409_ForLineAndHeader(int status)
    {
        var world = new CorrectionWorld(status);

        var line = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        var header = await world.Service().CorrectAsync(Header("TotalDue", "10"), CancellationToken.None);

        line.Status.Should().Be(CorrectionStatus.NotInReview);
        header.Status.Should().Be(CorrectionStatus.NotInReview);
        line.Code.Should().Be("ebilling.invoice.not_in_review");
        NothingWritten(world);
    }

    [Fact]
    public async Task ALineOfAnotherInvoice_IsNotFound_AndNothingIsWritten()
    {
        var world = new CorrectionWorld();
        var foreign = W.FeeLine();
        world.Lines.Clear();
        foreign["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        world.Lines.Add(foreign);

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.LineNotFound);
        NothingWritten(world);
    }

    [Fact]
    public async Task AnUnknownInvoice_IsNotFound()
    {
        var world = new CorrectionWorld();
        var command = Line("Hours", "2.5") with { InvoiceId = Guid.NewGuid() };
        world.Invoice = new Entity("sprk_invoice", Guid.NewGuid());

        var result = await world.Service().CorrectAsync(command, CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvoiceNotFound);
    }

    [Fact]
    public async Task AReadFault_ChangesNothing_AndIsUnavailable()
    {
        var world = new CorrectionWorld();
        world.ReadFault = new TimeoutException("simulated");

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.Unavailable);
        NothingWritten(world);
    }

    // =========================================================================================
    // EB-D-170: a value that cannot be attributed to a source version is refused, not written with a blank one
    // =========================================================================================

    [Fact]
    public async Task ALineStoredBeforeProvenance_RefusesAnExtractedFieldCorrection_With409()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_extractionconfidence"] = W.ExtractionJson(provenance: false);

        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.NoProvenance);
        result.Code.Should().Be("ebilling.correction.no_provenance");
        NothingWritten(world);
    }

    [Fact]
    public async Task ALineStoredBeforeProvenance_RefusesACorrectionOfTheCodeTheAiAssigned()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_extractionconfidence"] = W.ExtractionJson(provenance: false);

        var result = await world.Service().CorrectAsync(Line("TaskCode", "L100"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.NoProvenance);
        NothingWritten(world);
    }

    [Fact]
    public async Task AFieldTheAiNeverRead_HasNoStoredValue_ToConfirm()
    {
        var world = new CorrectionWorld();
        world.Line().Attributes.Remove("sprk_description");

        var result = await world.Service().CorrectAsync(Line("Narrative", null, confirm: true), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.correction.nothing_to_confirm");
        NothingWritten(world);
    }

    [Fact]
    public async Task AValueForAFieldTheAiLeftEmpty_IsCorrected_WithNoAiValueOnTheRow()
    {
        var world = new CorrectionWorld();
        world.Line().Attributes.Remove("sprk_description");

        var result = await world.Service().CorrectAsync(Line("Narrative", "Filled in by the reviewer"), CancellationToken.None);

        result.Correction!.Outcome.Should().Be("Corrected");
        world.Corrections.Single().Contains("sprk_aivalue").Should().BeFalse();
    }

    // =========================================================================================
    // Closed field list and value rules
    // =========================================================================================

    [Theory]
    [InlineData("Quantity", true)]
    [InlineData("sprk_amount", true)]
    [InlineData("", true)]
    [InlineData("TotalDue", true)] // a header field on the line route
    [InlineData("Hours", false)] // a line field on the header route
    public async Task AFieldOutsideTheClosedList_ForTheRoute_IsUnknown(string field, bool onLine)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(onLine ? Line(field, "1") : Header(field, "1"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.correction.field_unknown");
        NothingWritten(world);
    }

    [Theory]
    [InlineData("Hours", "abc", "ebilling.correction.value_invalid")]
    [InlineData("Hours", "1e3", "ebilling.correction.value_invalid")]
    [InlineData("Hours", "99999999999", "ebilling.correction.value_invalid")]
    [InlineData("Rate", "-1", "ebilling.correction.value_invalid")]
    [InlineData("Amount", "12,5", "ebilling.correction.value_invalid")]
    [InlineData("Narrative", "   ", "ebilling.correction.value_required")]
    [InlineData("Narrative", null, "ebilling.correction.value_required")]
    [InlineData("TaskCode", "", "ebilling.correction.value_required")]
    public async Task AnInvalidOrMissingValue_Is400_WithItsCode_BeforeAnyRead(string field, string? value, string code)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line(field, value), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be(code);
        NothingWritten(world);
        world.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AValueAndConfirmTogether_AreRefused()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Hours", "2", confirm: true), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.InvalidRequest);
        result.Code.Should().Be("ebilling.request.invalid");
    }

    [Fact]
    public async Task ANarrativeTooLong_IsRefused_NotTruncated()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Line("Narrative", new string('x', 4001)), CancellationToken.None);

        result.Code.Should().Be("ebilling.correction.value_invalid");
        NothingWritten(world);
    }

    // =========================================================================================
    // The header
    // =========================================================================================

    [Fact]
    public async Task AHeaderCorrection_UsesTheHeaderProvenanceAsTheAiValue_HasNoLine_AndUpdatesTheInvoice()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Header("TotalDue", "2400"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        row.Contains("sprk_invoiceline").Should().BeFalse();
        row.GetAttributeValue<string>("sprk_aivalue").Should().Be("2500.00");
        row.GetAttributeValue<string>("sprk_finalvalue").Should().Be("2400.00");
        row.GetAttributeValue<decimal>("sprk_aiconfidence").Should().Be(0.65m);
        row.GetAttributeValue<string>("sprk_sourcecode").Should().Be("sprk_invoice_v1");
        row.GetAttributeValue<string>("sprk_sourceversion").Should().Be("2025-11-01");
        world.ValueWrites.Should().ContainSingle().Which.Entity.Should().Be("sprk_invoice");
        world.Invoice.GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(2400m);
    }

    [Theory]
    [InlineData("FirmInvoiceNumber", "INV-2002", "sprk_invoicenumber")]
    [InlineData("BillingPeriodStart", "2026-08-15", "sprk_billingperiodstart")]
    [InlineData("BillingPeriodEnd", "2026-10-01", "sprk_billingperiodend")]
    [InlineData("Currency", "cad", "sprk_currency")]
    [InlineData("TotalDue", "-300", "sprk_totalamount")] // a credit memo is negative (D-121)
    public async Task EachHeaderField_IsCorrectedThroughItsColumn(string field, string value, string column)
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Header(field, value), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.ValueWrites.Where(w => w.Entity == "sprk_invoice").Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo([column]); // a Currency correction also moves the undecided lines
    }

    [Fact]
    public async Task ConfirmingAHeaderValue_AsRead_WritesAConfirmedRow()
    {
        var world = new CorrectionWorld();

        var result = await world.Service().CorrectAsync(Header("Currency", null, confirm: true), CancellationToken.None);

        result.Correction!.Outcome.Should().Be("Confirmed");
        world.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ATotalTheAiNeverRead_ConfirmedAsTheFallbackSum_IsStillACorrectionOfWhatWasRead()
    {
        var world = new CorrectionWorld();
        world.Submission!["sprk_findingsjson"] = W.HeaderFindingsJson.Replace("\"value\":\"2500.00\"", "\"value\":null");

        var result = await world.Service().CorrectAsync(Header("TotalDue", null, confirm: true), CancellationToken.None);

        result.Correction!.Outcome.Should().Be("Corrected");
        result.Correction.AiValue.Should().BeNull();
    }

    [Fact]
    public async Task AnInvoiceStoredBeforeProvenance_RefusesAHeaderCorrection_With409()
    {
        var world = new CorrectionWorld();
        world.Submission = null;

        var result = await world.Service().CorrectAsync(Header("TotalDue", "2400"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.NoProvenance);
        NothingWritten(world);
    }

    [Fact]
    public async Task HeaderAndLineRows_AreSeparateScopes()
    {
        var world = new CorrectionWorld();
        var service = world.Service();

        await service.CorrectAsync(Header("Currency", "CAD"), CancellationToken.None);
        await service.CorrectAsync(Line("Amount", "2000"), CancellationToken.None);
        var headerAgain = await service.CorrectAsync(Header("Currency", "CAD"), CancellationToken.None);

        headerAgain.AlreadyApplied.Should().BeTrue();
        world.Corrections.Should().HaveCount(2);
    }

    // =========================================================================================
    // Reviewer role (EB-D-171)
    // =========================================================================================

    [Fact]
    public async Task TheRole_IsEligibleApprover_WhenTheCallersOwnContactIsInTheEligibleSetOfTheSingleCore()
    {
        var world = new CorrectionWorld();
        world.EligibleContacts.Add(Guid.NewGuid());
        world.EligibleContacts.Add(W.ReviewerContact);

        await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        world.Corrections.Single().GetAttributeValue<string>("sprk_reviewerrole").Should().Be("eligible-approver");
    }

    [Fact]
    public async Task TheRole_IsInvoiceReviewer_WhenTheCallerIsNotEligible_HasNoContact_OrTheLookupFails()
    {
        var notEligible = new CorrectionWorld();
        notEligible.EligibleContacts.Add(Guid.NewGuid());
        var noContact = new CorrectionWorld { CallerContact = null };
        noContact.EligibleContacts.Add(W.ReviewerContact);
        var fault = new CorrectionWorld { ApproverFault = new TimeoutException("simulated") };

        foreach (var world in new[] { notEligible, noContact, fault })
        {
            var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

            result.IsApplied.Should().BeTrue("the role is directional: a failed lookup never blocks the correction");
            world.Corrections.Single().GetAttributeValue<string>("sprk_reviewerrole").Should().Be("invoice-reviewer");
        }
    }

    [Fact]
    public async Task TheRole_IsAskedOnlyWhenARowIsWritten()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        world.ApproverCalls = 0;

        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        world.ApproverCalls.Should().Be(0);
    }

    // =========================================================================================
    // The corrections read
    // =========================================================================================

    [Fact]
    public async Task List_ReturnsThisInvoicesRows_NewestFirst_AndNeverAnotherInvoicesRows()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        world.Time.Now = W.T0.AddMinutes(3);
        await service.CorrectAsync(Header("Currency", "CAD"), CancellationToken.None);
        world.AddRow(new Entity("sprk_invoicecorrection", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()), // another invoice's row
            ["sprk_fieldname"] = "Hours",
            ["sprk_outcometype"] = new OptionSetValue(100000000),
            ["sprk_sourcecode"] = "sprk_invoice_v1",
            ["sprk_sourceversion"] = "2025-11-01",
            ["sprk_reviewedon"] = W.T0.AddMinutes(9),
        });

        var rows = await service.ListAsync(W.InvoiceId, null, null, CancellationToken.None);

        rows!.Select(r => r.Field).Should().Equal("Currency", "Hours");
    }

    [Fact]
    public async Task List_FiltersBySourceCodeAndVersion()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        await service.CorrectAsync(Line("TaskCode", "L100"), CancellationToken.None);

        var byAnalyzer = await service.ListAsync(W.InvoiceId, "sprk_invoice_v1", null, CancellationToken.None);
        var byAction = await service.ListAsync(W.InvoiceId, "invoice-line-code", "1", CancellationToken.None);
        var wrongVersion = await service.ListAsync(W.InvoiceId, "invoice-line-code", "2", CancellationToken.None);

        byAnalyzer!.Select(r => r.Field).Should().Equal("Hours");
        byAction!.Select(r => r.Field).Should().Equal("TaskCode");
        wrongVersion.Should().BeEmpty();
    }

    [Fact]
    public async Task List_AReadFault_IsNull_NotAnEmptyList()
    {
        var world = new CorrectionWorld();
        world.ReadFault = new TimeoutException("simulated");

        (await world.Service().ListAsync(W.InvoiceId, null, null, CancellationToken.None)).Should().BeNull();
    }

    // =========================================================================================
    // ADR-015 and the part B2 seam
    // =========================================================================================

    [Fact]
    public async Task NoValueOrNarrative_IsEverLogged()
    {
        var world = new CorrectionWorld();
        var service = world.Service();
        world.ValueFault = new TimeoutException("simulated");
        await service.CorrectAsync(Line("Narrative", "A very private settlement narrative"), CancellationToken.None);
        world.ValueFault = null;
        await service.CorrectAsync(Line("Narrative", "A very private settlement narrative"), CancellationToken.None);
        await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        world.Log.Entries.Should().NotBeEmpty();
        world.Log.Entries.Select(e => e.Message).Should().NotContain(m => m.Contains("private settlement") || m.Contains(W.Narrative) || m.Contains("2.5"));
    }

    [Fact]
    public void TheB2Seam_IsAnInternalMethod_ThatDoesNothingInB1()
    {
        var world = new CorrectionWorld();

        var task = world.Service().OnValueWrittenAsync(new CorrectionApplied(W.InvoiceId, W.FeeLineId, "Hours", "Corrected", true), CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        world.ValueWrites.Should().BeEmpty();
    }

    // =========================================================================================
    // 101 B2a: the seam runs on a repeat, and a seam failure is 503 Incomplete, never success
    // =========================================================================================

    [Fact(DisplayName = "101 B2a: an idempotent repeat that writes no second row still runs the seam")]
    public async Task TheSeam_RunsOnAnIdempotentRepeat_ThatWritesNoSecondRow()
    {
        var world = new CorrectionWorld();
        var applied = new List<CorrectionApplied>();
        world.Seam = (a, _) =>
        {
            applied.Add(a);
            return Task.CompletedTask;
        };
        var service = world.Service();

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);
        var again = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.AlreadyApplied.Should().BeFalse();
        again.AlreadyApplied.Should().BeTrue("the repeat wrote no second row and no second value");
        world.Corrections.Should().HaveCount(1);
        applied.Should().HaveCount(2, "the follow-through runs on the repeat too");
        applied[0].ValueChanged.Should().BeTrue();
        applied[1].ValueChanged.Should().BeFalse();
        applied[1].Field.Should().Be("Hours");
    }

    [Fact(DisplayName = "101 B2a: a seam failure answers Incomplete (not Applied) with the failed step; the repeat writes no second row and completes it")]
    public async Task ASeamFailure_IsIncomplete_AndTheRepeatCompletesIt_WithOneRow()
    {
        var world = new CorrectionWorld();
        var calls = 0;
        world.Seam = (_, _) => ++calls == 1 ? throw new TimeoutException("simulated") : Task.CompletedTask;
        var service = world.Service();

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.IsApplied.Should().BeFalse();
        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.Code.Should().Be("ebilling.correction.incomplete");
        first.FailedStep.Should().Be("reevaluation");
        first.Detail.Should().Contain("Repeat the same request");
        world.Corrections.Should().HaveCount(1, "the correction itself is saved");
        world.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(2.5m);

        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        repeat.AlreadyApplied.Should().BeTrue();
        world.Corrections.Should().HaveCount(1, "the repeat writes no second row");
        calls.Should().Be(2);
    }

    [Fact(DisplayName = "101 B2a: Incomplete is HTTP 503 with errorCode and failedStep; Applied is 200")]
    public async Task Incomplete_MapsTo503_WithTheStableCode_AndTheFailedStep()
    {
        var world = new CorrectionWorld { Seam = (_, _) => throw new InvalidOperationException("simulated") };
        var result = await world.Service().CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        var http = Sprk.Bff.Api.Api.Finance.InvoiceCorrectionEndpoints.ToResult(result, new Microsoft.AspNetCore.Http.DefaultHttpContext());

        var problem = http.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("ebilling.correction.incomplete");
        problem.ProblemDetails.Extensions["failedStep"].Should().Be("reevaluation");
    }
}
