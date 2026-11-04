using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095c-3: the persister with a submission that has no firm (an internal from-email submission, owner 2026-11-03). The
/// outcome comes from the real processor (<see cref="InvoiceSubmissionProcessorTests.NoFirm"/>: steps 4 and 5 skipped); the persister writes
/// no firm lookup, flags every line <c>rules.not_checked</c> (EB-D-175: no line is Clean), stores the submission Accepted with
/// <c>firm_missing</c>, links no predecessor, re-checks a correction with the intake's firm rule, and re-evaluates when the firm changed.
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    private static Store NoFirmStore(Store? store = null)
    {
        store ??= new Store();
        store.Submission.Attributes.Remove("sprk_vendororg");
        store.Submission["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal);
        return store;
    }

    private static ProcessorHarness NoFirmTwoLines()
    {
        var harness = InvoiceSubmissionProcessorTests.NoFirm();
        harness.Lines =
        [
            InvoiceSubmissionProcessorTests.FeeLine(),
            InvoiceSubmissionProcessorTests.FeeLine() with { Narrative = InvoiceSubmissionProcessorTests.F<string?>("Reviewed the exhibits.") },
        ];
        return harness;
    }

    [Fact(DisplayName = "095c-3 SEEDED: a no-firm invoice is committed In Review with no firm lookup on the invoice or its lines; EVERY line is Flagged rules.not_checked (none is Clean); the submission is Accepted firm_missing")]
    public async Task NoFirm_IsCommittedWithoutAFirm_EveryLineFlaggedRulesNotChecked_AcceptedFirmMissing()
    {
        var outcome = await NoFirmTwoLines().RunAsync();
        outcome.Evaluation.Should().BeNull("the setup: no rule ran, so a null evaluation alone would read as 'nothing refused'");
        var store = NoFirmStore();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Accepted);
        result.TerminalReasonCode.Should().Be(SubmissionReasonCodes.FirmMissing);
        var requests = store.Transactions.Should().ContainSingle().Subject;
        var invoice = Target(requests[0]);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        invoice.Attributes.Should().NotContainKey("sprk_vendororg", "no firm lookup, never the empty id");
        invoice.Attributes.Should().NotContainKey("sprk_supersedesinvoice");
        requests.Select(Describe).Should().Contain("Create sprk_invoiceapproval", "held In Review, decided by a reviewer once the firm is set");

        var lines = requests.Select(Target).Where(e => e.LogicalName == "sprk_billingevent").ToList();
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => !l.Contains("sprk_vendororg"));
        lines.Select(StatusOf).Should().OnlyContain(s => s == InvoiceSubmissionPersister.LineStatusFlagged, "EB-D-175: a check that did not run is not Clean");

        var submission = Target(requests[^1]);
        submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(SubmissionReasonCodes.FirmMissing);
        using var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"));
        var notChecked = NotChecked(findings).ToList();
        notChecked.Select(e => e.GetProperty("line").GetInt32()).Should().Equal(0, 1);
        notChecked.Should().OnlyContain(e => e.GetProperty("message").GetString() == InvoiceSubmissionPersister.RulesNotCheckedNoFirmMessage);
        findings.RootElement.GetProperty("kind").GetString().Should().Be(nameof(SubmissionEvaluationKind.Accepted));
        findings.RootElement.GetProperty("reasonCode").GetString().Should().Be(SubmissionReasonCodes.FirmMissing);
        findings.RootElement.GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null, "nothing was decided");
        findings.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);
        findings.RootElement.GetProperty("signalPlan").GetArrayLength().Should().Be(0);
        findings.RootElement.GetProperty("pipelineFindings").EnumerateArray()
            .Select(f => f.GetProperty("reasonCode").GetString()).Should().Equal(SubmissionReasonCodes.FirmMissing);
        store.EarlierInvoiceQueries.Should().BeEmpty("no firm, so no predecessor is looked up");

        // Must-not-fire control: the same two lines WITH the firm and no rule refusal are Clean, with no rules.not_checked finding.
        var (withFirmLines, withFirmFindings) = await PersistLinesAsync(await TwoLineHarness().RunAsync());
        using (withFirmFindings)
        {
            withFirmLines.Select(StatusOf).Should().OnlyContain(s => s == InvoiceSubmissionPersister.LineStatusClean);
            NotChecked(withFirmFindings).Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "095c-3: an Accepted outcome keeps no reason code unless it is firm_missing (the terminal mapping)")]
    public void TerminalFor_AcceptedPassesOnlyTheFirmHold()
    {
        InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind.Accepted, SubmissionReasonCodes.FirmMissing)
            .Should().Be((InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.FirmMissing));
        InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind.Accepted, null).Should().Be((InvoiceSubmissionStatus.Accepted, (string?)null));
        InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind.Accepted, "ebilling.submission.something_else")
            .Should().Be((InvoiceSubmissionStatus.Accepted, (string?)null), "must-not-fire: any other stored code on Accepted is dropped, as before");
        InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind.AcceptedUncoded, SubmissionReasonCodes.FirmMissing)
            .Should().Be((InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.FirmMissing));
    }

    [Theory(DisplayName = "095c-3 (fail closed): a no-firm outcome carrying a rule result, a rejection, or no firm hold is a builder defect and is never persisted")]
    [InlineData("evaluation")]
    [InlineData("rejected")]
    [InlineData("no-hold")]
    [InlineData("no-reason")]
    public async Task NoFirmOutcome_ThatIsNotAHeldAcceptance_IsRefusedBeforeAnyWrite(string defect)
    {
        var noFirm = await InvoiceSubmissionProcessorTests.NoFirm().RunAsync();
        var judged = await new ProcessorHarness().RunAsync();
        var outcome = defect switch
        {
            "evaluation" => noFirm with { EvaluationRequest = judged.EvaluationRequest, Evaluation = judged.Evaluation, Decision = judged.Decision },
            "rejected" => noFirm with { Kind = SubmissionEvaluationKind.Blocked, ReasonCode = SubmissionReasonCodes.Blocked },
            "no-hold" => noFirm with { PipelineFindings = [] },
            _ => noFirm with { ReasonCode = null },
        };
        var store = NoFirmStore();

        var act = () => store.Build().PersistAsync(outcome, null);

        await act.Should().ThrowAsync<ArgumentException>();
        store.Transactions.Should().BeEmpty();
    }

    [Theory(DisplayName = "095c-3: the firm set or cleared since the evaluation is re-evaluated, never persisted (as a changed core record)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirmChangedSinceTheEvaluation_ReEvaluates_PersistsNothing(bool evaluatedWithoutFirm)
    {
        SubmissionEvaluationOutcome outcome;
        var store = new Store();
        if (evaluatedWithoutFirm)
        {
            outcome = await InvoiceSubmissionProcessorTests.NoFirm().RunAsync();
            store.Submission["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal); // the firm was set since
        }
        else
        {
            outcome = await new ProcessorHarness().RunAsync();
            store.Submission.Attributes.Remove("sprk_vendororg"); // the firm was cleared since
        }

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.ReEvaluate);
        store.Transactions.Should().BeEmpty();
    }

    // ── A Correction of a no-firm invoice: the commit-time re-check uses the intake's firm rule (CorrectionKeepsFirm) ──

    [Fact(DisplayName = "095c-3: a no-firm Correction of a no-firm invoice is committed and supersedes it")]
    public async Task NoFirmCorrection_OfANoFirmInvoice_IsCommitted_AndSupersedesIt()
    {
        var store = NoFirmStore(CorrectionStore());
        store.OldInvoice!.Attributes.Remove("sprk_vendororg");

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(InvoiceSubmissionProcessorTests.NoFirm()), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var transaction = store.Transactions.Should().ContainSingle().Subject;
        Target(transaction[0]).GetAttributeValue<EntityReference>("sprk_supersedesinvoice").Id.Should().Be(OldInvoiceId);
        Target(transaction[0]).Attributes.Should().NotContainKey("sprk_vendororg");
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded);
    }

    [Theory(DisplayName = "095c-3: a firm on one side only at commit (a no-firm Correction of a firm invoice, or a firm Correction of a no-firm invoice) is refused different_firm, nothing sent")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFirmOnOneSideOnlyAtCommit_IsRefusedDifferentFirm(bool submissionHasNoFirm)
    {
        Store store;
        SubmissionEvaluationOutcome outcome;
        if (submissionHasNoFirm)
        {
            store = NoFirmStore(CorrectionStore()); // the old invoice keeps its firm
            outcome = await CorrectionOutcomeAsync(InvoiceSubmissionProcessorTests.NoFirm());
        }
        else
        {
            store = CorrectionStore();
            store.OldInvoice!.Attributes.Remove("sprk_vendororg");
            outcome = await CorrectionOutcomeAsync();
        }

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionDifferentFirm);
        store.Transactions.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    // ── 104 D-43: no predecessor for a submission with no firm ──

    [Fact(DisplayName = "095c-3: a no-firm submission is never linked to a Returned to Firm invoice, not even a no-firm one of the same number and record; nothing is queried")]
    public async Task NoFirm_IsNeverLinkedToAPredecessor_AndQueriesNone()
    {
        var store = NoFirmStore();
        var earlier = EarlierInvoice(Earlier1, InvoiceStatusCodes.ReturnedToFirm, EarlierOld);
        earlier.Attributes.Remove("sprk_vendororg");
        store.EarlierInvoices.Add(earlier);

        var result = await store.Build().PersistAsync(await InvoiceSubmissionProcessorTests.NoFirm().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        Target(store.Transactions.Single()[0]).Attributes.Should().NotContainKey("sprk_supersedesinvoice");
        store.EarlierInvoiceQueries.Should().BeEmpty("a null firm matches nothing");
    }
}
