using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 101 part B2b-2, item 7 and item 5: the correction service (with its re-evaluation and Signals step), the line-decision service and the
/// approval service over ONE in-memory Dataverse (<see cref="ApprovalWorld"/>, with the real resolver, recorder and totals): an uncoded invoice becomes
/// approvable after its lines are re-coded, and a held invoice stays refused after a correction changes its held group.
/// </summary>
public sealed class InvoiceCorrectionEndToEndTests
{
    private static readonly Guid CodeSetId = Guid.Parse("10100000-0000-4000-8000-0000000000d7");
    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap);
    private static readonly BillingRuleCandidate SystemPolicy = Kit.Version(BillingRuleCatalogue.SystemPolicyCode, RuleType.SubmissionIntegrity, "{}");

    private static InvoiceCorrectionService Corrections(ApprovalWorld w, params BillingRuleCandidate[] rules) => Corrections(w, null, rules);

    private static InvoiceCorrectionService Corrections(
        ApprovalWorld w, Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>>? writeSignal, params BillingRuleCandidate[] rules) =>
        Corrections(w, rules, writeSignal);

    private static InvoiceCorrectionService Corrections(
        ApprovalWorld w,
        BillingRuleCandidate[] rules,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>>? writeSignal = null,
        Func<Guid, string, int, string, CancellationToken, Task<SignalCloseOutcome>>? closeSignal = null,
        Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>>? recalculate = null)
    {
        var resolver = new CodeSetResolver(w.Entities.Object, w.Settings.Object, new Mock<ICodeSetSuggestionProvider>().Object, NullLogger<CodeSetResolver>.Instance);
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            w.Entities.Object, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate,
            writeSignal ?? ((request, _) => Task.FromException<SignalWriteResult>(new InvalidOperationException("no Signal is raised on this path"))),
            closeSignal ?? ((id, rowVersion, resolution, _, _) => Task.FromResult(Close(w, id, rowVersion, resolution))),
            NullLogger<InvoiceCorrectionReevaluation>.Instance,
            (_, _) => Task.FromResult<(BillingRuleCandidate?, string)>((SystemPolicy, "in_force")),
            recalculate);
        return new InvoiceCorrectionService(
            w.Entities.Object, resolver, w.Ownership.Object, w.Identity.Object, (entity, id, ct) => w.Resolver().ResolveAsync(entity, id, ct),
            NullLogger<InvoiceCorrectionService>.Instance, w.Clock, reevaluation.ApplyAsync);
    }

    /// <summary>The writer's record-free system close over the world's Signal rows, conditional on the row version.</summary>
    private static SignalCloseOutcome Close(ApprovalWorld w, Guid id, string rowVersion, int resolution)
    {
        lock (w.Gate)
        {
            var row = w.Rows[("sprk_signal", id)];
            if (row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture) != rowVersion)
            {
                return SignalCloseOutcome.ChangedSinceRead;
            }

            row["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
            row["sprk_resolutiontype"] = new OptionSetValue(resolution);
            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            return SignalCloseOutcome.Closed;
        }
    }

    private static void Prepare(ApprovalWorld w)
    {
        var invoice = w.Invoice;
        invoice["sprk_vendororg"] = new EntityReference("sprk_organization", Kit.FirmId);
        invoice["sprk_invoicenumber"] = "INV-1001";
        invoice["sprk_currency"] = "USD";
        invoice["sprk_billingperiodstart"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        invoice["sprk_billingperiodend"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        invoice["versionnumber"] = 5L;
    }

    private static JsonObject Root(string kind)
    {
        var root = JsonNode.Parse(W.HeaderFindingsJson)!.AsObject();
        root["schema"] = 1;
        root["kind"] = kind;
        root["trackingNumber"] = "INV-1001.1";
        root["receivedOn"] = "2026-10-01";
        root["invoiceName"] = "INV-1001";
        root["findings"] = new JsonArray();
        root["pipelineFindings"] = new JsonArray();
        root["codingFindings"] = new JsonArray();
        root["lineFindings"] = new JsonArray();
        root["signalPlan"] = new JsonArray();
        return root;
    }

    private static Entity Submission(ApprovalWorld w, JsonObject root, InvoiceSubmissionStatus status, string reasonCode)
    {
        root["invoiceId"] = w.InvoiceId.ToString("D");
        var submission = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
            ["sprk_findingsjson"] = root.ToJsonString(),
            ["sprk_processingstatus"] = new OptionSetValue((int)status),
            ["sprk_outcomereasoncode"] = reasonCode,
            ["createdon"] = Now.UtcDateTime.AddDays(-1),
            ["versionnumber"] = 30L,
        };
        w.Put(submission);
        return submission;
    }

    private static JsonObject Stored(Entity submission) => JsonNode.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

    private static int StatusOf(Entity line) => line.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value;

    [Fact(DisplayName = "101 B2b-2 end to end: an uncoded invoice, a code set added, each line re-coded - the hold closes, the Flagged line is accepted, the invoice is approved")]
    public async Task UncodedInvoice_ReCodedLineByLine_BecomesApprovable()
    {
        var w = new ApprovalWorld();
        Prepare(w);
        var pending = w.AddLine(InvoiceLineStatus.Pending, extraction: W.ExtractionJson(coded: false));
        var blank = w.AddLine(InvoiceLineStatus.Flagged, extraction: W.ExtractionJson(coded: false));
        blank.Attributes.Remove("sprk_description"); // EB-D-176: a blank narrative on an uncoded invoice is Flagged
        var root = Root(nameof(SubmissionEvaluationKind.AcceptedUncoded));
        root["reasonCode"] = SubmissionReasonCodes.CodingNoCodeSetHold;
        root["pipelineFindings"] = new JsonArray(new JsonObject { ["reasonCode"] = SubmissionReasonCodes.CodingNoCodeSetHold, ["values"] = new JsonObject() });
        root["lineFindings"] = new JsonArray(new JsonObject { ["line"] = 1, ["code"] = InvoiceSubmissionPersister.LineFindingNoDescription });
        var submission = Submission(w, root, InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.CodingNoCodeSetHold);
        var hold = w.AddInvoiceSignal(BillingRuleCatalogue.SystemPolicyCode);
        w.Row("sprk_signal", hold)["sprk_evidencerefs"] =
            "[{\"kind\":\"" + BillingFindingSignalMapper.FingerprintEvidenceKind + "\",\"ref\":\"coding.no_code_set|Flag\"}]";
        w.Row("sprk_signal", hold)["versionnumber"] = 1L;

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.LinesOpen, "before the re-code the lines are Pending and Flagged");

        // A code set is added (the settings default, FR-22 step 5).
        w.Put(new Entity("sprk_billingcodeset", CodeSetId) { ["sprk_name"] = "UTBMS Litigation", ["statecode"] = new OptionSetValue(0) });
        w.Put(new Entity("sprk_billingcode", Guid.NewGuid())
        {
            ["sprk_code"] = "L120", ["sprk_codetype"] = new OptionSetValue((int)BillingCodeKind.Task), ["sprk_description"] = "L120",
            ["sprk_codeset"] = new EntityReference("sprk_billingcodeset", CodeSetId), ["statecode"] = new OptionSetValue(0),
        });
        w.SettingsValue = new EbillingSettings { DefaultCodeSetId = CodeSetId };
        var corrections = Corrections(w);

        (await corrections.CorrectAsync(new CorrectionCommand(w.InvoiceId, pending.Id, "TaskCode", "L120", false, AliceUser), CancellationToken.None))
            .IsApplied.Should().BeTrue();
        StatusOf(w.Row("sprk_billingevent", pending.Id)).Should().Be((int)InvoiceLineStatus.Clean);
        w.SignalStatus(hold).Should().Be(SignalWriter.SignalStatusOpen, "a line is still uncoded");

        (await corrections.CorrectAsync(new CorrectionCommand(w.InvoiceId, blank.Id, "TaskCode", "L120", false, AliceUser), CancellationToken.None))
            .IsApplied.Should().BeTrue();
        w.SignalStatus(hold).Should().Be(SignalWriter.SignalStatusResolved);
        w.Row("sprk_signal", hold).GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        ((JsonArray)Stored(w.Row("sprk_invoicesubmission", submission.Id))["pipelineFindings"]!).Should().BeEmpty();
        StatusOf(w.Row("sprk_billingevent", blank.Id)).Should().Be((int)InvoiceLineStatus.Flagged, "the blank narrative still needs a decision");

        var accepted = await w.LineService().DecideAsync(
            new LineDecisionCommand(w.InvoiceId, Guid.NewGuid(), LineDecisionAction.Accept, null, [new LineDecisionLine(blank.Id)], BobUser, Bulk: false, CallerToken),
            CancellationToken.None);
        accepted.IsApplied.Should().BeTrue();

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);

        // 101 B2c: a code the reviewer recorded is not an AI code, so the approval's bulk rows ("accepted in bulk") never claim it as one.
        w.Rows.Values.Where(r => r.LogicalName == "sprk_invoicecorrection"
                && r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value == 100000002
                && r.GetAttributeValue<string>("sprk_fieldname") == "TaskCode")
            .Should().BeEmpty();
        w.Rows.Values.Count(r => r.LogicalName == "sprk_invoicecorrection" && r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value == 100000002)
            .Should().BeGreaterThan(0, "the bulk rows of the AI-read fields of the Clean line are written, so the check above can fail");
    }

    [Fact(DisplayName = "101 B2b-2 item 5: a held (blocked) invoice is still refused approval after a correction removes its held group's reason")]
    public async Task HeldInvoice_AfterACorrectionThatChangesTheHeldGroup_IsStillRefusedApproval()
    {
        var w = new ApprovalWorld();
        Prepare(w);
        var line = w.AddLine(InvoiceLineStatus.Flagged, extraction: W.ExtractionJson());
        line["sprk_quantity"] = 25m; // over the daily cap
        line["sprk_timekeeper"] = "A. Lee";
        var root = Root(nameof(SubmissionEvaluationKind.Blocked));
        root["reasonCode"] = SubmissionReasonCodes.Blocked;
        root["decision"] = new JsonObject { ["outcome"] = "Rejected", ["deciding"] = new JsonArray(0) };
        root["findings"] = new JsonArray(new JsonObject
        {
            ["index"] = 0, ["ruleType"] = RuleType.DailyHoursCap.ToString(), ["policyId"] = Cap.PolicyId.ToString("D"), ["policyCode"] = Cap.PolicyCode,
            ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"), ["subject"] = "Line", ["line"] = 0,
            ["reasonCode"] = BillingReasonCodes.DailyHoursOverCap, ["consequence"] = "Reject", ["rejects"] = true,
        });
        root["signalPlan"] = new JsonArray(new JsonObject
        {
            ["policyId"] = Cap.PolicyId.ToString("D"), ["policyCode"] = Cap.PolicyCode, ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"),
            ["subjectEntity"] = BillingFindingSignalMapper.LineEntity, ["subjectId"] = line.Id.ToString("D"),
            ["fingerprint"] = BillingReasonCodes.DailyHoursOverCap + "|Flag", ["decision"] = nameof(BillingGroupDecision.Blocked),
        });
        var submission = Submission(w, root, InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked);

        (await Corrections(w, Cap).CorrectAsync(new CorrectionCommand(w.InvoiceId, line.Id, "Hours", "2.5", false, AliceUser), CancellationToken.None))
            .IsApplied.Should().BeTrue();

        var entry = ((JsonArray)Stored(w.Row("sprk_invoicesubmission", submission.Id))["signalPlan"]!).OfType<JsonObject>().Should().ContainSingle().Subject;
        entry["decision"]!.GetValue<string>().Should().Be(nameof(BillingGroupDecision.Blocked));
        entry[InvoiceCorrectionReevaluation.CorrectionReviewMember]!.GetValue<bool>().Should().BeTrue();
        w.Row("sprk_invoicesubmission", submission.Id).GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value
            .Should().Be((int)InvoiceSubmissionStatus.Rejected, "a correction never changes the submission that holds the invoice");

        var approve = await w.ApproveAs(AliceUser);

        approve.Outcome.Should().Be(ApprovalOutcome.Held);
        approve.Code.Should().Be(InvoiceApprovalCodes.Held);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    // =========================================================================================
    // Fix round, part 3 (F-2): an unfinished correction is never approved past
    // =========================================================================================

    /// <summary>A Clean, coded line on an Accepted submission with no findings; the correction's findings write (its first all-update transaction) fails once.</summary>
    private static (Entity Line, Entity Submission) CleanInvoice(ApprovalWorld w)
    {
        Prepare(w);
        var line = w.AddLine(InvoiceLineStatus.Clean, extraction: W.ExtractionJson());
        line["sprk_timekeeper"] = "A. Lee";
        var submission = Submission(w, Root(nameof(SubmissionEvaluationKind.Accepted)), InvoiceSubmissionStatus.Accepted, "accepted");
        return (line, submission);
    }

    private static void AssertApproveWroteNothing(ApprovalWorld w, int writesBefore, Entity line)
    {
        w.Writes.Should().Be(writesBefore, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse("nothing is claimed");
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Clean, "the Clean line is not accepted in bulk");
    }

    [Fact(DisplayName = "101 fix 3 F-2 SEEDED: while a saved correction's findings step has not finished, approve is 409 corrections_pending and canApprove is false; after the repeat the invoice is approved")]
    public async Task AnUnfinishedCorrection_BlocksApproval_UntilItIsRepeated()
    {
        var w = new ApprovalWorld();
        var (line, _) = CleanInvoice(w);
        var corrections = Corrections(w);
        var command = new CorrectionCommand(w.InvoiceId, line.Id, "Narrative", "Prepared the brief", false, AliceUser);
        w.FaultsOnce["tx:approvers"] = new TimeoutException("simulated"); // the findings patch is the first all-update transaction

        var first = await corrections.CorrectAsync(command, CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        (await w.GetAs(AliceUser))!.CanApprove.Should().BeFalse("a correction is waiting for its follow-through");
        var writes = w.Writes;

        var refused = await w.ApproveAs(AliceUser);

        refused.Outcome.Should().Be(ApprovalOutcome.Conflict);
        refused.Code.Should().Be(InvoiceApprovalCodes.CorrectionsPending);
        refused.Detail.Should().Be("These corrections were saved but did not finish: Narrative (line 1). Repeat each correction, then approve again.");
        refused.Detail.Should().NotContain(w.InvoiceId.ToString("D")).And.NotContain(line.Id.ToString("D"));
        AssertApproveWroteNothing(w, writes, line);

        (await corrections.CorrectAsync(command, CancellationToken.None)).IsApplied.Should().BeTrue();
        (await w.GetAs(AliceUser))!.CanApprove.Should().BeTrue();

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "101 fix 3 F-2: a Clean line corrected from 2.5 to 25 hours whose findings step failed is not accepted in bulk; the repeat flags it")]
    public async Task AnUnfinishedHoursCorrection_IsNotBulkAccepted_AndTheRepeatFlagsTheLine()
    {
        var w = new ApprovalWorld();
        var (line, _) = CleanInvoice(w);
        var corrections = Corrections(w, (_, _) => Task.FromResult(new SignalWriteResult(Guid.NewGuid(), true, Guid.Empty, Guid.Empty)), Cap);
        var command = new CorrectionCommand(w.InvoiceId, line.Id, "Hours", "25", false, AliceUser);
        w.FaultsOnce["tx:approvers"] = new TimeoutException("simulated");

        (await corrections.CorrectAsync(command, CancellationToken.None)).FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        var writes = w.Writes;

        var refused = await w.ApproveAs(AliceUser);

        refused.Code.Should().Be(InvoiceApprovalCodes.CorrectionsPending);
        AssertApproveWroteNothing(w, writes, line);

        (await corrections.CorrectAsync(command, CancellationToken.None)).IsApplied.Should().BeTrue();
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Flagged, "25 hours is over the daily cap");
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.LinesOpen);
    }

    [Fact(DisplayName = "101 fix 3 F-2: the guard's read fails closed - a fault refuses the approve (nothing written) and the approval GET does not answer canApprove")]
    public async Task TheGuardsReadFault_RefusesApproval()
    {
        var w = new ApprovalWorld();
        var (line, _) = CleanInvoice(w);
        w.Faults["read:sprk_invoicecorrection"] = new TimeoutException("simulated");
        var writes = w.Writes;

        var refused = await w.ApproveAs(AliceUser);

        refused.Outcome.Should().Be(ApprovalOutcome.Unavailable);
        refused.Code.Should().Be(InvoiceApprovalCodes.Unavailable);
        AssertApproveWroteNothing(w, writes, line);
        await FluentActions.Awaiting(() => w.GetAs(AliceUser)).Should().ThrowAsync<TimeoutException>("the GET throws on a read fault, as for the hold read");
    }

    // =========================================================================================
    // Fix round, part 3, round 2: "applied" only after the WHOLE follow-through; no line decided past an unfinished correction
    // =========================================================================================

    private static readonly Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> SignalWritten =
        (_, _) => Task.FromResult(new SignalWriteResult(Guid.NewGuid(), true, Guid.Empty, Guid.Empty));

    private static bool WritesLineStatus(IReadOnlyList<Microsoft.Xrm.Sdk.OrganizationRequest> requests) =>
        requests.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Any(u => u.Target.LogicalName == "sprk_billingevent" && u.Target.Contains("sprk_linestatus"));

    /// <summary>The first transaction <paramref name="matches"/> picks fails once.</summary>
    private static Func<IReadOnlyList<Microsoft.Xrm.Sdk.OrganizationRequest>, Exception?> FailOnce(Func<IReadOnlyList<Microsoft.Xrm.Sdk.OrganizationRequest>, bool> matches)
    {
        var failed = false;
        return requests =>
        {
            if (failed || !matches(requests))
            {
                return null;
            }

            failed = true;
            return new TimeoutException("simulated");
        };
    }

    private static async Task<ApprovalResult> AssertRefusedForPendingCorrections(ApprovalWorld w, Entity line, string field)
    {
        (await w.GetAs(AliceUser))!.CanApprove.Should().BeFalse("a correction is waiting for its follow-through");
        var writes = w.Writes;
        var refused = await w.ApproveAs(AliceUser);
        refused.Outcome.Should().Be(ApprovalOutcome.Conflict);
        refused.Code.Should().Be(InvoiceApprovalCodes.CorrectionsPending);
        refused.Detail.Should().Be($"These corrections were saved but did not finish: {field} (line 1). Repeat each correction, then approve again.");
        w.Writes.Should().Be(writes, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse("nothing is claimed");
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().NotBe((int)InvoiceLineStatus.Accepted, "the line is not accepted in bulk");
        return refused;
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2 SEEDED: a correction whose findings patch landed but whose STATUS step failed (the line still Clean, a finding on it) blocks approval; after the repeat the line is Flagged, decided, and the invoice approved")]
    public async Task AFailedStatusStep_BlocksApproval_UntilTheRepeat()
    {
        var w = new ApprovalWorld();
        var (line, submission) = CleanInvoice(w);
        var corrections = Corrections(w, [Cap], writeSignal: SignalWritten);
        var command = new CorrectionCommand(w.InvoiceId, line.Id, "Hours", "25", false, AliceUser);
        w.TransactionFault = FailOnce(WritesLineStatus);

        var first = await corrections.CorrectAsync(command, CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.StatusStep);
        ((JsonArray)Stored(w.Row("sprk_invoicesubmission", submission.Id))["findings"]!).Should().ContainSingle("the findings patch landed");
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Clean, "an approve would otherwise accept it in bulk");

        await AssertRefusedForPendingCorrections(w, line, "Hours");

        (await corrections.CorrectAsync(command, CancellationToken.None)).IsApplied.Should().BeTrue();
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Flagged);
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.LinesOpen);

        (await w.LineService().DecideAsync(
            new LineDecisionCommand(w.InvoiceId, Guid.NewGuid(), LineDecisionAction.Accept, null, [new LineDecisionLine(line.Id)], BobUser, Bulk: false, CallerToken),
            CancellationToken.None)).IsApplied.Should().BeTrue("every correction of the line is followed through");
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2: a re-code whose SIGNALS step failed (the line Clean, the coding hold still open) blocks approval; after the repeat the hold closes and the invoice is approved")]
    public async Task AFailedSignalsStep_BlocksApproval_UntilTheRepeat()
    {
        var w = new ApprovalWorld();
        Prepare(w);
        var line = w.AddLine(InvoiceLineStatus.Pending, extraction: W.ExtractionJson(coded: false));
        var root = Root(nameof(SubmissionEvaluationKind.AcceptedUncoded));
        root["reasonCode"] = SubmissionReasonCodes.CodingNoCodeSetHold;
        root["pipelineFindings"] = new JsonArray(new JsonObject { ["reasonCode"] = SubmissionReasonCodes.CodingNoCodeSetHold, ["values"] = new JsonObject() });
        Submission(w, root, InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.CodingNoCodeSetHold);
        var hold = w.AddInvoiceSignal(BillingRuleCatalogue.SystemPolicyCode);
        w.Row("sprk_signal", hold)["sprk_evidencerefs"] =
            "[{\"kind\":\"" + BillingFindingSignalMapper.FingerprintEvidenceKind + "\",\"ref\":\"coding.no_code_set|Flag\"}]";
        w.Row("sprk_signal", hold)["versionnumber"] = 1L;
        w.Put(new Entity("sprk_billingcodeset", CodeSetId) { ["sprk_name"] = "UTBMS Litigation", ["statecode"] = new OptionSetValue(0) });
        w.Put(new Entity("sprk_billingcode", Guid.NewGuid())
        {
            ["sprk_code"] = "L120", ["sprk_codetype"] = new OptionSetValue((int)BillingCodeKind.Task), ["sprk_description"] = "L120",
            ["sprk_codeset"] = new EntityReference("sprk_billingcodeset", CodeSetId), ["statecode"] = new OptionSetValue(0),
        });
        w.SettingsValue = new EbillingSettings { DefaultCodeSetId = CodeSetId };
        var closeFails = true;
        var corrections = Corrections(w, [], closeSignal: (id, rowVersion, resolution, _, _) =>
        {
            if (closeFails)
            {
                closeFails = false;
                return Task.FromException<SignalCloseOutcome>(new TimeoutException("simulated"));
            }

            return Task.FromResult(Close(w, id, rowVersion, resolution));
        });
        var command = new CorrectionCommand(w.InvoiceId, line.Id, "TaskCode", "L120", false, AliceUser);

        var first = await corrections.CorrectAsync(command, CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.SignalsStep);
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Clean, "the status step finished");
        w.SignalStatus(hold).Should().Be(SignalWriter.SignalStatusOpen, "the hold's close did not finish");

        await AssertRefusedForPendingCorrections(w, line, "TaskCode");

        (await corrections.CorrectAsync(command, CancellationToken.None)).IsApplied.Should().BeTrue();
        w.SignalStatus(hold).Should().Be(SignalWriter.SignalStatusResolved);
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2: an hours correction whose TOTALS step failed (the approved total stale) blocks approval; after the repeat the invoice is approved")]
    public async Task AFailedTotalsStep_BlocksApproval_UntilTheRepeat()
    {
        var w = new ApprovalWorld();
        var (line, _) = CleanInvoice(w);
        var corrections = Corrections(w, [], recalculate: (id, ct) => w.Totals().RecalculateAsync(id, ct));
        var command = new CorrectionCommand(w.InvoiceId, line.Id, "Hours", "3", false, AliceUser);
        w.FaultsOnce["totals"] = new TimeoutException("simulated");

        var first = await corrections.CorrectAsync(command, CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.TotalsStep);
        StatusOf(w.Row("sprk_billingevent", line.Id)).Should().Be((int)InvoiceLineStatus.Clean);

        await AssertRefusedForPendingCorrections(w, line, "Hours");

        (await corrections.CorrectAsync(command, CancellationToken.None)).IsApplied.Should().BeTrue();
        (await w.GetAs(AliceUser))!.CanApprove.Should().BeTrue();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
    }
}
