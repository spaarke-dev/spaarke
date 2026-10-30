using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 049 part B: Processing Pipeline steps 1-5 (<see cref="InvoiceSubmissionProcessor"/>). The read, resolution and
/// coding are faked at their interfaces; the evaluation (072, in memory on loaded facts) and the decision (073) are the real ones, so
/// each outcome is what the shipped rules produce from the request this processor built.
/// </summary>
public class InvoiceSubmissionProcessorTests
{
    private const int Reject = (int)BillingConsequence.Reject;
    private const int Flag = (int)BillingConsequence.Flag;

    private static readonly Guid Submission = Guid.Parse("04900000-0000-4000-8000-0000000000b1");
    private static readonly Guid Firm = Guid.Parse("04900000-0000-4000-8000-0000000000f1");
    private static readonly Guid Matter = Guid.Parse("04900000-0000-4000-8000-0000000000a1");
    private static readonly Guid Document = Guid.Parse("04900000-0000-4000-8000-0000000000d1");
    private static readonly DateTime CreatedOn = new(2026, 6, 5, 23, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly InvoiceOn = new(2026, 6, 1);

    // ── outcome classes ──

    [Fact(DisplayName = "049 B: a clean invoice is Accepted, carrying the read, resolution, coding, evaluation and decision")]
    public async Task CleanInvoice_IsAccepted_WithEveryStep()
    {
        var h = new Harness();

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        outcome.ReasonCode.Should().BeNull();
        outcome.Read.Should().NotBeNull();
        outcome.Resolution.Should().NotBeNull();
        outcome.Coding!.Lines.Should().ContainSingle().Which.CodeSetSource.Should().Be(CodeSetSource.FirmCodes);
        outcome.Evaluation.Should().NotBeNull();
        outcome.Decision!.Outcome.Should().Be(SubmissionOutcome.Accepted);
        outcome.PipelineFindings.Should().BeEmpty();
        outcome.CoreRecord.Should().Be(new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, Matter));
        h.Coding.Request!.CoreRecord.Should().Be(new CodingCoreRecord("sprk_matter", Matter));
        h.Resolution.Input!.CoreRecord.Should().Be(new CoreRecordReference(CoreRecordKind.Matter, Matter));
        h.Evaluated!.CoreRecord.Should().Be(new CoreRecordReference(CoreRecordKind.Matter, Matter));
    }

    [Fact(DisplayName = "097b: a Correction is judged with its replaced invoice derived on the server from sprk_resubmissionof; a New submission names none and reads nothing")]
    public async Task ACorrection_CarriesItsReplacedInvoice_DerivedFromItsResubmissionOf()
    {
        var earlier = Guid.NewGuid();
        var replaced = InvoiceSubmissionPersister.InvoiceIdFor(earlier);
        var h = new Harness { ReadInvoiceNumber = "INV-100-R" };
        h.Row["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", earlier);
        h.Entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", earlier, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoicesubmission", earlier) { ["sprk_invoice"] = new EntityReference("sprk_invoice", replaced) });
        var asNew = new Harness();

        await h.RunAsync();
        await asNew.RunAsync();

        h.Evaluated!.ReplacedInvoiceId.Should().Be(replaced);
        h.Evaluated.SubmittedFirmInvoiceNumber.Should().Be("INV-100", "the typed number on the submission row, not the read one");
        asNew.Evaluated!.ReplacedInvoiceId.Should().BeNull();
        asNew.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "097b: a replaced submission that names another invoice than the one it created gives no replaced invoice (nothing excluded, fail closed)")]
    public async Task ACorrection_WhoseTargetDoesNotResolve_ExcludesNothing()
    {
        var earlier = Guid.NewGuid();
        var h = new Harness();
        h.Row["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", earlier);
        h.Entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", earlier, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoicesubmission", earlier) { ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()) });

        await h.RunAsync();

        h.Evaluated!.ReplacedInvoiceId.Should().BeNull();
    }

    [Fact(DisplayName = "049 B: ReceivedOn is createdon's UTC day, and the tracking number is sprk_trackingnumber, never re-allocated")]
    public async Task ReceivedOnAndTrackingNumber_ComeFromTheSubmissionRead()
    {
        var h = new Harness();

        var outcome = await h.RunAsync();

        outcome.ReceivedOn.Should().Be(new DateOnly(2026, 6, 5));
        h.Evaluated!.ReceivedOn.Should().Be(new DateOnly(2026, 6, 5));
        h.Evaluated.TrackingNumber.Should().Be("INV-100.2");
        outcome.Submission!.Sequence.Should().Be(2);
    }

    [Fact(DisplayName = "049 B: a rejecting finding on an SPA submission is AutoRejected, reason = the first deciding finding's code")]
    public async Task RejectingFinding_OnSpa_IsAutoRejected()
    {
        var h = new Harness { Rules = [InvoiceAgeReject()] };
        h.Lines = [FeeLine(date: new DateOnly(2026, 1, 20))];

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        outcome.Decision!.DecidingFindings.Should().NotBeEmpty();
        outcome.ReasonCode.Should().Be(outcome.Decision.DecidingFindings[0].Finding.ReasonCode);
        outcome.Read.Should().NotBeNull("D-101: a read submission gets its invoice, created Rejected");
    }

    [Fact(DisplayName = "049 B: the same rejecting finding on an Internal submission is Blocked (held), reason ebilling.submission.blocked")]
    public async Task RejectingFinding_OnInternal_IsBlocked()
    {
        var h = new Harness { Rules = [InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        h.Lines = [FeeLine(date: new DateOnly(2026, 1, 20))];

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.Blocked).And.Be("ebilling.submission.blocked");
        outcome.Decision!.Outcome.Should().Be(SubmissionOutcome.Rejected);
    }

    [Theory(DisplayName = "049 B (FR-21): a document-level read failure is RejectedUnreadable with that code, before any resolution")]
    [InlineData("invoice.read.unreadable")]
    [InlineData("invoice.read.page_limit")]
    [InlineData("invoice.read.no_invoice")]
    public async Task DocumentLevelReadFailure_IsRejectedUnreadable(string code)
    {
        var h = new Harness { ReadFailure = new InvoiceAiException(code, InvoiceAiException.ReadCapability, "read failed") };

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.RejectedUnreadable);
        outcome.ReasonCode.Should().Be(code);
        outcome.Read.Should().BeNull();
        h.Resolution.Input.Should().BeNull();
        h.Coding.Request.Should().BeNull();
        SubmissionReasonCodes.IsDocumentLevelReadFailure(code).Should().BeTrue();
    }

    [Fact(DisplayName = "049 B (FR-21): invoice.read.service_failed is not classified; it propagates so the job retries")]
    public async Task ServiceFailed_Propagates_ForRetry()
    {
        var h = new Harness
        {
            ReadFailure = new InvoiceAiException(InvoiceAiException.ReadServiceFailed, InvoiceAiException.ReadCapability, "down"),
        };

        var act = () => h.RunAsync();

        (await act.Should().ThrowAsync<InvoiceAiException>()).Which.ReasonCode.Should().Be(SubmissionReasonCodes.ReadServiceFailed);
        SubmissionReasonCodes.IsDocumentLevelReadFailure(InvoiceAiException.ReadServiceFailed).Should().BeFalse();
    }

    [Fact(DisplayName = "049 B: a line-coding AI failure propagates so the job retries")]
    public async Task CodingAiFailure_Propagates_ForRetry()
    {
        var h = new Harness();
        h.Coding.Failure = new InvoiceAiException(InvoiceAiException.CompletionFailed, "invoice-line-code", "model down");

        var act = () => h.RunAsync();

        (await act.Should().ThrowAsync<InvoiceAiException>()).Which.ReasonCode.Should().Be(InvoiceAiException.CompletionFailed);
    }

    [Fact(DisplayName = "049 B: the Null AI facade is Failed ai.invoice.disabled, with no read")]
    public async Task NullAiFacade_IsFailed_AiInvoiceDisabled()
    {
        var h = new Harness { Ai = new NullInvoiceAi(NullLogger<NullInvoiceAi>.Instance) };

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.AiInvoiceDisabled).And.Be("ai.invoice.disabled");
        outcome.Read.Should().BeNull();
    }

    [Fact(DisplayName = "049 B: a reader with no endpoint is Failed ai.invoice.read.not_configured")]
    public async Task ReaderNotConfigured_IsFailed()
    {
        var h = new Harness { ReadFailure = new FeatureDisabledException(InvoiceAi.ReadNotConfiguredCode, "no endpoint") };

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.ReadNotConfigured);
    }

    [Fact(DisplayName = "049 B (ADR-018): Ebilling:Enabled false is Failed ebilling.disabled; nothing is downloaded or read")]
    public async Task FlagOff_IsFailed_EbillingDisabled()
    {
        var h = new Harness { Enabled = false };

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.EbillingDisabled).And.Be("ebilling.disabled");
        h.DocumentLoads.Should().Be(0);
    }

    [Fact(DisplayName = "049 C (D-126): no code set after a successful read is AcceptedUncoded, held by a coding_no_code_set finding, never Failed")]
    public async Task NoCodeSet_AfterTheRead_IsAcceptedUncoded_Held()
    {
        var h = new Harness { ReadInvoiceNumber = "INV-999" };
        h.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded, "D-126: only a submission that fails before it is read has no invoice");
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.CodingNoCodeSetHold, "D-130 b: the reason code carries the coding code");
        outcome.Read.Should().NotBeNull();
        outcome.Resolution.Should().NotBeNull("the lines keep their resolved timekeeper and rate");
        outcome.Coding.Should().BeNull();
        outcome.Evaluation.Should().NotBeNull("D-130 1: the rules still run on the firm's billed data");
        outcome.Decision!.Outcome.Should().Be(SubmissionOutcome.Accepted);
        h.Evaluated.Should().NotBeNull();
        outcome.PipelineFindings.Select(f => f.ReasonCode).Should().Equal(
            SubmissionReasonCodes.InvoiceNumberMismatch, SubmissionReasonCodes.CodingNoCodeSetHold);
        outcome.PipelineFindings[1].Values["cause"].Should().Be(SubmissionReasonCodes.NoCodeSet);
    }

    [Fact(DisplayName = "049 C (D-126): line coding switched off after the read is AcceptedUncoded, held by a coding_unavailable finding")]
    public async Task CodingSwitchedOff_AfterTheRead_IsAcceptedUncoded_Held()
    {
        var h = new Harness();
        h.Coding.Failure = new FeatureDisabledException(SubmissionReasonCodes.AiLinearConsumersDisabled, "off");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.CodingUnavailableHold, "D-130 b");
        outcome.PipelineFindings.Should().ContainSingle().Which.Should().Match<SubmissionPipelineFinding>(f =>
            f.ReasonCode == SubmissionReasonCodes.CodingUnavailableHold && f.Values["cause"] == SubmissionReasonCodes.AiLinearConsumersDisabled);
        h.Evaluated.Should().NotBeNull("D-130 1");
    }

    // ── D-130 (1): the rules run on an uncoded submission ──

    [Fact(DisplayName = "049 C2 (D-130 1): uncoded + a firm-data reject rule on SPA is AutoRejected, with the rule finding and the coding hold finding both kept")]
    public async Task Uncoded_WithAFirmDataReject_IsAutoRejected_BothFindings()
    {
        var h = new Harness { Rules = [InvoiceAgeReject()] };
        h.Lines = [FeeLine(date: new DateOnly(2026, 1, 20))];
        h.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        outcome.Coding.Should().BeNull("the lines are still uncoded");
        outcome.ReasonCode.Should().Be(outcome.Decision!.DecidingFindings[0].Finding.ReasonCode);
        outcome.Decision.Outcome.Should().Be(SubmissionOutcome.Rejected);
        outcome.PipelineFindings.Select(f => f.ReasonCode).Should().Contain(SubmissionReasonCodes.CodingNoCodeSetHold,
            "the coding hold finding is still recorded beside the rejection");
        h.Evaluated!.Lines[0].TaskCode.Should().BeNull();
        h.Evaluated.Lines[0].Observations.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 C2 (D-130 1): uncoded + a firm-data reject rule on Internal is Blocked, with the coding hold finding kept")]
    public async Task Uncoded_WithAFirmDataReject_OnInternal_IsBlocked()
    {
        var h = new Harness { Rules = [InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        h.Lines = [FeeLine(date: new DateOnly(2026, 1, 20))];
        h.Coding.Failure = new FeatureDisabledException(SubmissionReasonCodes.AiLinearConsumersDisabled, "off");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        outcome.PipelineFindings.Select(f => f.ReasonCode).Should().Contain(SubmissionReasonCodes.CodingUnavailableHold);
    }

    [Fact(DisplayName = "049 C2 (D-130 1): uncoded + no rejecting rule is held In Review (AcceptedUncoded) with the rule findings beside the coding finding")]
    public async Task Uncoded_WithoutAReject_IsHeld_WithRuleFindings()
    {
        var h = new Harness { Rules = [Version("BILL-CAP", RuleType.DailyHoursCap) with { Consequence = Flag }] };
        h.Lines = [FeeLine(hours: 13m, amount: 6500m)];
        h.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.CodingNoCodeSetHold);
        outcome.Evaluation!.Findings.Select(f => f.RuleType).Should().Equal(RuleType.DailyHoursCap);
        outcome.Decision!.Findings.Should().HaveCount(1);
        outcome.PipelineFindings.Select(f => f.ReasonCode).Should().Contain(SubmissionReasonCodes.CodingNoCodeSetHold);
    }

    [Fact(DisplayName = "049 C2 (D-130 1, D-117): an uncoded line never rejects through an AI-assigned field; the same rule on a billed code does (control)")]
    public async Task Uncoded_NeverRejectsThroughAnAiAssignedField()
    {
        // An over-cap expense with NO billed code reads the AI-assigned ExpenseCode field; uncoded lines have none, so 073 holds it.
        var uncoded = new Harness { Rules = [Version("BILL-EXP", RuleType.ExpenseRule) with { Consequence = Reject }] };
        uncoded.Lines = [ExpenseLine(billedCode: null)];
        uncoded.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var held = await uncoded.RunAsync();

        var finding = held.Decision!.Findings.Should().ContainSingle().Subject;
        finding.Finding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseAmountOverCap);
        finding.Finding.ReadFields.Should().Contain(f => f.Field == BillingFieldNames.ExpenseCode);
        finding.Rejects.Should().BeFalse();
        finding.HeldBecause.Should().Be(SubmissionHoldReasons.AiAssignedInput);
        held.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);

        // Control: a billed code is the firm's own data, so the same over-cap expense rejects, uncoded or not.
        var billed = new Harness { Rules = [Version("BILL-EXP", RuleType.ExpenseRule) with { Consequence = Reject }] };
        billed.Lines = [ExpenseLine(billedCode: "E100")];
        billed.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var rejected = await billed.RunAsync();

        rejected.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        rejected.Decision!.DecidingFindings.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 C2 (D-130 1): every rule type evaluates uncoded lines without a fault (null AI codes, no observations)")]
    public async Task Uncoded_EveryRuleType_EvaluatesWithoutAFault()
    {
        var h = new Harness { Rules = [.. Enum.GetValues<RuleType>().Where(t => RuleDomains.IsBilling(t) && t != RuleType.SubmissionIntegrity).Select(t => Version("BILL-" + t, t) with { Consequence = Flag })] };
        h.Lines = [FeeLine(), ExpenseLine(billedCode: null)];
        h.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);
        outcome.Decision!.Outcome.Should().Be(SubmissionOutcome.Accepted);
        h.Evaluated!.Lines.Should().OnlyContain(l => l.TaskCode == null && l.ActivityCode == null && l.ExpenseCode == null && l.Observations.Count == 0);
        h.Evaluated.Lines.Should().OnlyContain(l => !l.FieldConfidence.ContainsKey(BillingFieldNames.TaskCode));
    }

    // ── fail closed on the submission read ──

    [Theory(DisplayName = "049 B (D-87, 072 W): a submission missing createdon or its tracking number fails closed before anything is read")]
    [InlineData("createdon", SubmissionReasonCodes.ReceivedOnMissing)]
    [InlineData("sprk_submissionsequence", SubmissionReasonCodes.TrackingMissing)]
    [InlineData("sprk_trackingnumber", SubmissionReasonCodes.TrackingMissing)]
    [InlineData("sprk_vendororg", SubmissionReasonCodes.FirmMissing)]
    [InlineData("sprk_regardingmatter", SubmissionReasonCodes.CoreRecordMissing)]
    [InlineData("sprk_firminvoicenumber", SubmissionReasonCodes.FirmInvoiceNumberMissing)]
    [InlineData("sprk_channel", SubmissionReasonCodes.ChannelUnsupported)]
    public async Task MissingColumn_FailsClosed_BeforeTheRead(string column, string code)
    {
        var h = new Harness();
        h.Row.Attributes.Remove(column);

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(code);
        outcome.Submission.Should().BeNull();
        h.DocumentLoads.Should().Be(0);
        h.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "049 B (072 W): a default createdon (0001-01-01) is no received date and fails closed")]
    public async Task DefaultCreatedOn_FailsClosed()
    {
        var h = new Harness();
        h.Row["createdon"] = default(DateTime);

        var outcome = await h.RunAsync();

        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.ReceivedOnMissing);
        h.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "092: a submission with no document is an upload; its staged PDF is loaded and read")]
    public async Task NoDocument_IsAnUpload_AndIsRead()
    {
        var h = new Harness();

        var outcome = await h.RunAsync();

        outcome.Kind.Should().NotBe(SubmissionEvaluationKind.Failed);
        outcome.Submission!.IsUpload.Should().BeTrue();
        outcome.Submission.DocumentId.Should().BeNull();
        h.DocumentLoads.Should().Be(1);
        h.Reads.Should().Be(1);
    }

    // ── 094 (092 review item 1): the file source is decided by channel and document, once, never by "no document" alone ──

    [Theory(DisplayName = "094 (092 review item 1): the file source per channel - Internal with a document is a SourceDocument (copied by the job); any channel without one is an Upload")]
    [InlineData(InvoiceSubmissionChannel.Internal, true, SubmissionFileSource.SourceDocument)]
    [InlineData(InvoiceSubmissionChannel.Internal, false, SubmissionFileSource.Upload)]
    [InlineData(InvoiceSubmissionChannel.Spa, false, SubmissionFileSource.Upload)]
    public async Task FileSource_PerChannel(InvoiceSubmissionChannel channel, bool withDocument, SubmissionFileSource expected)
    {
        var h = new Harness { Channel = channel };
        if (withDocument)
        {
            h.Row.Id = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(Document);
            h.Row["sprk_document"] = new EntityReference("sprk_document", Document);
        }

        var outcome = await h.RunAsync();

        outcome.Kind.Should().NotBe(SubmissionEvaluationKind.Failed);
        outcome.Submission!.FileSource.Should().Be(expected);
        outcome.Submission.DocumentId.Should().Be(withDocument ? Document : null);
        outcome.Submission.IsUpload.Should().Be(expected == SubmissionFileSource.Upload);
        h.DocumentLoads.Should().Be(1);
    }

    [Fact(DisplayName = "094 must-fire (092 review item 1): a firm-channel (SPA) submission naming a document fails closed DocumentUnavailable before any load or read - a document is a source only on the internal channel")]
    public async Task SpaWithADocument_FailsClosed_DocumentUnavailable()
    {
        var h = new Harness { Channel = InvoiceSubmissionChannel.Spa };
        h.Row["sprk_document"] = new EntityReference("sprk_document", Document);

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.DocumentUnavailable);
        h.DocumentLoads.Should().Be(0);
        h.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "094 fix round F1-1 (must fire; restores 092's rule): an internal submission naming a document it was not created for (a sprk_document written onto an upload submission) fails closed DocumentUnavailable before any load or read")]
    public async Task InternalUploadSubmission_PatchedToNameADocument_FailsClosed_DocumentUnavailable()
    {
        var h = new Harness { Channel = InvoiceSubmissionChannel.Internal };
        h.Row["sprk_document"] = new EntityReference("sprk_document", Document);

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.DocumentUnavailable);
        h.DocumentLoads.Should().Be(0);
        h.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "094 fix round F1-1 (must fire): a submission derived from one document but re-pointed at another fails closed DocumentUnavailable before any load or read")]
    public async Task DerivedSubmission_RepointedAtAnotherDocument_FailsClosed_DocumentUnavailable()
    {
        var h = new Harness { Channel = InvoiceSubmissionChannel.Internal };
        h.Row.Id = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(Guid.Parse("09400000-0000-4000-8000-0000000000f1"));
        h.Row["sprk_document"] = new EntityReference("sprk_document", Document);

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Failed);
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.DocumentUnavailable);
        h.DocumentLoads.Should().Be(0);
        h.Reads.Should().Be(0);
    }

    [Fact(DisplayName = "049 B: a createdon late in the UTC day stays on that UTC day")]
    public async Task CreatedOn_UnspecifiedKind_IsTreatedAsUtc()
    {
        var h = new Harness();
        h.Row["createdon"] = DateTime.SpecifyKind(new DateTime(2026, 6, 5, 23, 59, 0), DateTimeKind.Unspecified);

        var outcome = await h.RunAsync();

        outcome.ReceivedOn.Should().Be(new DateOnly(2026, 6, 5));
    }

    [Fact(DisplayName = "049 B (FR-05): a submission regarding two core records fails closed")]
    public async Task TwoCoreRecords_FailClosed()
    {
        var h = new Harness();
        h.Row["sprk_regardingproject"] = new EntityReference("sprk_project", Guid.NewGuid());

        var outcome = await h.RunAsync();

        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.CoreRecordMissing);
    }

    // ── I-5, D-89, D-91 ──

    [Fact(DisplayName = "049 B (I-5): a failing AiObservation finding with all-reliable reads is Accepted, never Rejected")]
    public async Task AiObservationAlone_NeverRejects()
    {
        var h = new Harness
        {
            Rules = [Version("BILL-AI", RuleType.AiObservation) with { Consequence = Reject }],
            StoredRejectBypassesTheGate = true,
        };
        h.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];

        var outcome = await h.RunAsync();

        var finding = outcome.Decision!.Findings.Should().ContainSingle().Which;
        finding.Finding.RuleType.Should().Be(RuleType.AiObservation);
        finding.Consequence.Should().Be(BillingConsequence.Reject, "the stored consequence reaches the decider unchanged");
        finding.Rejects.Should().BeFalse();
        finding.HeldBecause.Should().Be(SubmissionHoldReasons.TypeNotRejectEligible);
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        h.Evaluated!.Lines[0].FieldConfidence[BillingFieldNames.ObservationBlockBilling].Should().Be(0.99m);
    }

    [Fact(DisplayName = "049 B: Accepted-with-findings keeps every finding")]
    public async Task AcceptedWithFindings_KeepsEveryFinding()
    {
        var h = new Harness
        {
            Rules =
            [
                Version("BILL-AI", RuleType.AiObservation) with { Consequence = Flag },
                Version("BILL-CAP", RuleType.DailyHoursCap) with { Consequence = Flag },
            ],
        };
        h.Lines = [FeeLine(hours: 13m, amount: 6500m)];
        h.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        outcome.Evaluation!.Findings.Select(f => f.RuleType).Should().BeEquivalentTo([RuleType.AiObservation, RuleType.DailyHoursCap]);
        outcome.Decision!.Findings.Should().HaveCount(outcome.Evaluation.Findings.Count);
    }

    [Theory(DisplayName = "049 B (D-89): an unmatched timekeeper never rejects, even with ReferenceMissingCap raised above the threshold")]
    [InlineData("0.95")]
    [InlineData("0.80")]
    public async Task ApprovedTimekeeper_NeverRejects_EvenWithTheCapRaised(string cap)
    {
        var raised = decimal.Parse(cap, System.Globalization.CultureInfo.InvariantCulture);
        var h = new Harness
        {
            Rules = [Version("BILL-TK", RuleType.ApprovedTimekeeper) with { Consequence = Reject }],
            Settings = new EbillingSettings { ReliabilityThreshold = 0.90m, ReferenceMissingCap = raised },
            StoredRejectBypassesTheGate = true,
        };

        // The cap no longer keeps the unmatched name below the threshold: 046 reports it at 0.95.
        h.Resolution.Timekeeper = new TimekeeperResolution { Reason = TimekeeperUnmatchedReason.NotOnRoster };

        var outcome = await h.RunAsync();

        var finding = outcome.Decision!.Findings.Should().ContainSingle().Which;
        finding.Finding.RuleType.Should().Be(RuleType.ApprovedTimekeeper);
        finding.Consequence.Should().Be(BillingConsequence.Reject);
        h.Evaluated!.Lines[0].FieldConfidence[BillingFieldNames.TimekeeperAsBilled].Should().BeGreaterThanOrEqualTo(0.90m);
        finding.Rejects.Should().BeFalse();
        finding.HeldBecause.Should().Be(SubmissionHoldReasons.TypeHoldsOnly, "D-89 holds by type, not by the cap");
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
    }

    [Fact(DisplayName = "049 B (D-91): a read invoice number that differs from the typed one is a hold finding citing both; never a rejection")]
    public async Task InvoiceNumberMismatch_Holds_NeverRejects()
    {
        var h = new Harness { ReadInvoiceNumber = "INV-999" };

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        outcome.InvoiceNumber!.Result.Should().Be(InvoiceNumberMatch.Mismatch);
        var finding = outcome.PipelineFindings.Should().ContainSingle().Which;
        finding.ReasonCode.Should().Be(SubmissionReasonCodes.InvoiceNumberMismatch);
        finding.Values.Should().Contain("typed", "INV-100").And.Contain("read", "INV-999");
        h.Evaluated!.TrackingNumber.Should().Be("INV-100.2", "D-91 never changes the tracking number");
    }

    [Fact(DisplayName = "049 B (D-91): a mismatch beside a rejecting rule leaves the rule's decision unchanged")]
    public async Task InvoiceNumberMismatch_DoesNotChangeTheDecision()
    {
        var h = new Harness { Rules = [InvoiceAgeReject()], ReadInvoiceNumber = "INV-999" };
        h.Lines = [FeeLine(date: new DateOnly(2026, 1, 20))];

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        outcome.ReasonCode.Should().NotBe(SubmissionReasonCodes.InvoiceNumberMismatch);
        outcome.PipelineFindings.Should().ContainSingle();
    }

    [Theory(DisplayName = "049 B (D-91): case, whitespace and punctuation are ignored")]
    [InlineData("inv 100")]
    [InlineData(" INV.100 ")]
    [InlineData("Inv-100")]
    [InlineData("INV/100")]
    public async Task InvoiceNumber_NormalisedMatch_RaisesNothing(string read)
    {
        var h = new Harness { ReadInvoiceNumber = read };

        var outcome = await h.RunAsync();

        outcome.InvoiceNumber!.Result.Should().Be(InvoiceNumberMatch.Match);
        outcome.PipelineFindings.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 B (D-91): a PDF with no invoice number is NotRead, not a mismatch")]
    public async Task InvoiceNumber_NotRead_RaisesNothing()
    {
        var h = new Harness { ReadInvoiceNumber = null };

        var outcome = await h.RunAsync();

        outcome.InvoiceNumber!.Result.Should().Be(InvoiceNumberMatch.NotRead);
        outcome.PipelineFindings.Should().BeEmpty();
    }

    // ── currency, credit lines ──

    [Fact(DisplayName = "049 B (072 caller): the header currency confidence is passed under exactly \"Currency\"; a bare \"$\" is USD (D-119)")]
    public async Task CurrencyConfidence_UnderExactlyTheCurrencyKey()
    {
        var h = new Harness { Rules = [Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Flag }], Currency = "$" };

        var outcome = await h.RunAsync();

        h.Resolution.Input!.Header.OtherFields.Should().ContainKey("Currency");
        h.Evaluated!.HeaderConfidence.Should().ContainKey("Currency").WhoseValue.Should().Be(0.99m);
        h.Evaluated.Currency.Should().Be("$", "the evaluator normalises through BillingCurrency, never the processor");
        outcome.Evaluation!.Skips.Should().BeEmpty("a reliably read \"$\" is USD, so rates are checked");
    }

    [Fact(DisplayName = "049 B (072 caller) control: a currency read below the threshold skips the money rules as unclear")]
    public async Task CurrencyConfidence_BelowThreshold_IsUnclear()
    {
        var h = new Harness { Rules = [Version("BILL-RATE", RuleType.ApprovedRate) with { Consequence = Flag }], Currency = "$", CurrencyConfidence = 0.5m };

        var outcome = await h.RunAsync();

        outcome.Evaluation!.Skips.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingEvaluationReasons.CurrencyUnreliable);
    }

    [Fact(DisplayName = "049 B (D-105, D-121): a credit line passes through with its negative amount and hours")]
    public async Task CreditLine_PassesThroughNegative()
    {
        var h = new Harness();
        h.Lines = [FeeLine(), FeeLine(hours: -1m, amount: -500m)];

        var outcome = await h.RunAsync();

        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        h.Resolution.Input!.Lines[1].Amount.Value.Should().Be(-500m);
        h.Coding.Request!.Lines[1].Amount.Should().Be(-500m);
        h.Evaluated!.Lines[1].Amount.Should().Be(-500m);
        h.Evaluated.Lines[1].Hours.Should().Be(-1m);
    }

    // ── one definition of the codes ──

    [Fact(DisplayName = "049 B: the reason codes are the owners' values (043, 047, kill switches, the spend predicate)")]
    public void ReasonCodes_AreTheOwnersValues()
    {
        FinanceBaseValues.BlockedReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        SubmissionReasonCodes.AiLinearConsumersDisabled.Should().Be(NullActionResolver.ErrorCode);
        SubmissionReasonCodes.ReadNotConfigured.Should().Be("ai.invoice.read.not_configured");
        SubmissionReasonCodes.ReadUnreadable.Should().Be("invoice.read.unreadable");
        SubmissionReasonCodes.NoCodeSet.Should().Be("ebilling.coding.no_code_set");
        SubmissionReasonCodes.CodingNoCodeSetHold.Should().Be("ebilling.submission.coding_no_code_set");
        SubmissionReasonCodes.CodingUnavailableHold.Should().Be("ebilling.submission.coding_unavailable");
    }

    // ── builders ──

    internal static BillingRuleCandidate InvoiceAgeReject() => Version("BILL-AGE", RuleType.InvoiceAge) with { Consequence = Reject };

    internal static ExtractedInvoiceField<T> F<T>(T value, decimal? confidence = 0.99m) => new() { Value = value, Confidence = confidence, Page = 1 };

    internal static ExtractedInvoiceLine FeeLine(DateOnly? date = null, decimal hours = 1.5m, decimal amount = 750m) => new()
    {
        LineType = InvoiceLineType.Fee,
        RowIndex = 0,
        Date = F<DateOnly?>(date ?? new DateOnly(2026, 5, 20)),
        TimekeeperAsBilled = F<string?>("Jane Smith"),
        TitleAsBilled = F<string?>("Partner"),
        Hours = F<decimal?>(hours),
        Rate = F<decimal?>(500m),
        Quantity = F<decimal?>(null, null),
        UnitCost = F<decimal?>(null, null),
        Amount = F<decimal?>(amount),
        Narrative = F<string?>("Drafted the motion."),
        TaskCodeAsBilled = F<string?>("L110"),
        ActivityCodeAsBilled = F<string?>("A103"),
        ExpenseCodeAsBilled = F<string?>(null, null),
    };

    /// <summary>An expense line over the default ExpenseRule cap (500); <paramref name="billedCode"/> is the firm's own expense code, or none.</summary>
    internal static ExtractedInvoiceLine ExpenseLine(string? billedCode, decimal amount = 900m) => new()
    {
        LineType = InvoiceLineType.Expense,
        RowIndex = 1,
        Date = F<DateOnly?>(new DateOnly(2026, 5, 21)),
        TimekeeperAsBilled = F<string?>(null, null),
        TitleAsBilled = F<string?>(null, null),
        Hours = F<decimal?>(null, null),
        Rate = F<decimal?>(null, null),
        Quantity = F<decimal?>(1m),
        UnitCost = F<decimal?>(amount),
        Amount = F<decimal?>(amount),
        Narrative = F<string?>("Travel."),
        TaskCodeAsBilled = F<string?>(null, null),
        ActivityCodeAsBilled = F<string?>(null, null),
        ExpenseCodeAsBilled = billedCode is null ? F<string?>(null, null) : F<string?>(billedCode),
    };

    internal sealed class Harness
    {
        public IReadOnlyList<BillingRuleCandidate> Rules { get; init; } = [];
        public InvoiceSubmissionChannel Channel
        {
            get => (InvoiceSubmissionChannel)Row.GetAttributeValue<OptionSetValue>("sprk_channel").Value;
            init => Row["sprk_channel"] = new OptionSetValue((int)value);
        }
        public bool Enabled { get; init; } = true;
        public Exception? ReadFailure { get; init; }
        public IInvoiceAi? Ai { get; init; }
        public string? ReadInvoiceNumber { get; init; } = "INV-100";
        public string? Currency { get; init; } = "USD";
        public decimal CurrencyConfidence { get; init; } = 0.99m;
        public EbillingSettings Settings { get; init; } = new();
        public bool StoredRejectBypassesTheGate { get; init; }
        public IReadOnlyList<ExtractedInvoiceLine> Lines { get; set; } = [FeeLine()];

        public FakeResolution Resolution { get; } = new();
        public FakeCoding Coding { get; } = new();
        public BillingEvaluationRequest? Evaluated { get; private set; }

        /// <summary>Task 097b: the job's Dataverse for a Correction's replaced-invoice read (strict: any other call fails the test).</summary>
        public Mock<Spaarke.Dataverse.IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public int DocumentLoads { get; private set; }
        public int Reads { get; private set; }

        public Entity Row { get; }

        public Harness()
        {
            Row = new Entity("sprk_invoicesubmission", Submission)
            {
                ["sprk_trackingnumber"] = "INV-100.2",
                ["sprk_submissionsequence"] = 2,
                ["sprk_firminvoicenumber"] = "INV-100",
                ["sprk_vendororg"] = new EntityReference("sprk_organization", Firm),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
                ["createdon"] = CreatedOn,
                ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa),
            };
        }

        public Task<SubmissionEvaluationOutcome> RunAsync() => Build().EvaluateAsync(Row, LoadDocument);

        /// <summary>The document loader the harness hands the processor (counts the loads).</summary>
        public Task<InvoiceReadRequest> LoadDocument(SubmissionSnapshot snapshot, CancellationToken _)
        {
            DocumentLoads++;
            return Task.FromResult(new InvoiceReadRequest
            {
                Content = new byte[] { 1 },
                DocumentId = snapshot.DocumentId ?? Sprk.Bff.Api.Services.Finance.Files.StoredFileIds.ForUpload(snapshot.SubmissionId),
            });
        }

        /// <summary>The real processor over this harness's fakes (049 part E drives it through the job handler).</summary>
        public InvoiceSubmissionProcessor Build()
        {
            var ai = Ai ?? ReadingAi();
            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings);
            var monitor = Mock.Of<IOptionsMonitor<EbillingOptions>>(m => m.CurrentValue == new EbillingOptions { Enabled = Enabled });

            var sut = new InvoiceSubmissionProcessor(
                ai,
                Resolution,
                Coding,
                (request, _) =>
                {
                    Evaluated = request;
                    var result = Evaluator().Evaluate(request, Loaded([.. Rules]));

                    // A row written outside the publish path: the evaluation gate would downgrade the consequence, so the decider's
                    // own guard is proved by restoring Reject after the gate (defence in depth, as 073's DecideAsync warns about).
                    return Task.FromResult(StoredRejectBypassesTheGate
                        ? result with { RulesByVersion = result.RulesByVersion.ToDictionary(r => r.Key, r => r.Value with { Consequence = Reject }) }
                        : result);
                },
                new SubmissionRejectionRule(settings.Object, NullLogger<SubmissionRejectionRule>.Instance),
                monitor,
                NullLogger<InvoiceSubmissionProcessor>.Instance,
                Entities.Object);

            return sut;
        }

        private IInvoiceAi ReadingAi()
        {
            var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
            var setup = ai.Setup(a => a.ReadInvoiceAsync(It.IsAny<InvoiceReadRequest>(), It.IsAny<CancellationToken>()));
            if (ReadFailure is { } failure)
            {
                setup.Callback(() => Reads++).ThrowsAsync(failure);
            }
            else
            {
                setup.Callback(() => Reads++).ReturnsAsync(() => Invoice());
            }

            return ai.Object;
        }

        private ExtractedInvoice Invoice() => new()
        {
            Header = new ExtractedInvoiceHeader
            {
                FirmName = F<string?>("Firm LLP"),
                FirmInvoiceNumber = F(ReadInvoiceNumber),
                InvoiceDate = F<DateOnly?>(InvoiceOn),
                BillingPeriodStart = F<DateOnly?>(new DateOnly(2026, 5, 1)),
                BillingPeriodEnd = F<DateOnly?>(new DateOnly(2026, 5, 31)),
                ClientName = F<string?>("Client"),
                MatterName = F<string?>("Matter"),
                FirmMatterReference = F<string?>(null, null),
                PurchaseOrderAsQuoted = F<string?>(null, null),
                FeesTotal = F<decimal?>(Lines.Sum(l => l.Amount.Value ?? 0m)),
                ExpensesTotal = F<decimal?>(0m),
                AdjustmentsTotal = F<decimal?>(0m),
                TotalDue = F<decimal?>(Lines.Sum(l => l.Amount.Value ?? 0m)),
                Currency = F(Currency, CurrencyConfidence),
            },
            Lines = Lines,
            TimekeeperSummary = [],
            PageCount = 1,
            AnalyzerId = "test",
            ApiVersion = "test",
        };
    }

    /// <summary>046 at its interface: combined confidence = the analyzer confidence (0 when none), every timekeeper matched unless told otherwise.</summary>
    internal sealed class FakeResolution : IInvoiceResolutionService
    {
        private static readonly Guid MatchedTimekeeper = Guid.Parse("04900000-0000-4000-8000-0000000000c1");

        public InvoiceResolutionInput? Input { get; private set; }

        public TimekeeperResolution? Timekeeper { get; set; }

        public Task<InvoiceResolutionResult> ResolveAsync(InvoiceResolutionInput input, CancellationToken ct = default)
        {
            Input = input;
            var header = new Dictionary<string, FieldConfidence>(StringComparer.Ordinal)
            {
                [ResolutionFieldNames.FeesTotal] = C(input.Header.FeesTotal.AnalyzerConfidence),
                [ResolutionFieldNames.ExpensesTotal] = C(input.Header.ExpensesTotal.AnalyzerConfidence),
                [ResolutionFieldNames.AdjustmentsTotal] = C(input.Header.AdjustmentsTotal.AnalyzerConfidence),
                [ResolutionFieldNames.TotalDue] = C(input.Header.TotalDue.AnalyzerConfidence),
            };
            foreach (var (name, reading) in input.Header.OtherFields)
            {
                header[name] = C(reading.AnalyzerConfidence);
            }

            var lines = input.Lines.Select((line, index) =>
            {
                var fields = new Dictionary<string, FieldConfidence>(StringComparer.Ordinal)
                {
                    [ResolutionFieldNames.Date] = C(line.Date.AnalyzerConfidence),
                    [ResolutionFieldNames.TimekeeperAsBilled] = C(line.TimekeeperAsBilled.AnalyzerConfidence),
                    [ResolutionFieldNames.Hours] = C(line.Hours.AnalyzerConfidence),
                    [ResolutionFieldNames.Rate] = C(line.Rate.AnalyzerConfidence),
                    [ResolutionFieldNames.Amount] = C(line.Amount.AnalyzerConfidence),
                };
                foreach (var (name, reading) in line.OtherFields)
                {
                    fields[name] = C(reading.AnalyzerConfidence);
                }

                return new ResolvedInvoiceLine
                {
                    LineIndex = index,
                    LineType = line.LineType,
                    Timekeeper = Timekeeper ?? new TimekeeperResolution { TimekeeperId = MatchedTimekeeper, Stage = TimekeeperMatchStage.Exact, MatchConfidence = 1m },
                    Rate = new RateResolution { Status = RateResolutionStatus.Found, ApprovedRate = 500m },
                    Fields = fields,
                };
            }).ToList();

            return Task.FromResult(new InvoiceResolutionResult
            {
                HeaderFields = header,
                Lines = lines,
                ArithmeticChecks = [],
                ApprovedTimekeeperFindingInputs = [],
            });
        }

        private static FieldConfidence C(decimal? analyzer) => new()
        {
            Combined = analyzer ?? 0m,
            AnalyzerSignal = analyzer ?? 0m,
            Arithmetic = ArithmeticOutcome.NotApplicable,
            Reference = ReferenceOutcome.NotApplicable,
        };
    }

    /// <summary>047 at its interface: one Coded result per line under a firm code set, with the configured observations.</summary>
    internal sealed class FakeCoding : ILineCodingService
    {
        public LineCodingRequest? Request { get; private set; }

        public Exception? Failure { get; set; }

        public IReadOnlyList<InvoiceLineObservation> Observations { get; set; } = [];

        /// <summary>Line keys the model returned nothing usable for (<see cref="LineCodingStatus.NotReturned"/>). A blank narrative is
        /// <see cref="LineCodingStatus.NoNarrative"/>, as in the real service.</summary>
        public IReadOnlySet<string> NotReturnedKeys { get; set; } = new HashSet<string>();

        public Task<InvoiceCodingResult> CodeLinesAsync(LineCodingRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Failure is { } failure)
            {
                return Task.FromException<InvoiceCodingResult>(failure);
            }

            var codeSet = new ResolvedCodeSet(Guid.Parse("04900000-0000-4000-8000-0000000000e1"), "UTBMS", CodeSetSource.FirmCodes, [], [], []);
            return Task.FromResult(new InvoiceCodingResult
            {
                CodeSet = codeSet,
                ChunkCount = 1,
                Lines = request.Lines.Select(line => new LineCodingResult
                {
                    LineKey = line.LineKey,
                    Status = string.IsNullOrWhiteSpace(line.Narrative) ? LineCodingStatus.NoNarrative
                        : NotReturnedKeys.Contains(line.LineKey) ? LineCodingStatus.NotReturned
                        : LineCodingStatus.Coded,
                    Task = new LineCodeValue(Coded(line) ? "L110" : null, line.BilledTaskCode, false),
                    Activity = new LineCodeValue(Coded(line) ? "A103" : null, line.BilledActivityCode, false),
                    Expense = new LineCodeValue(null, null, false),
                    Confidence = Coded(line) ? 0.95m : 0m,
                    Observations = Coded(line) ? Observations : [],
                    RefusedCodes = [],
                    CodeSetId = codeSet.CodeSetId,
                    CodeSetName = codeSet.CodeSetName,
                    CodeSetSource = codeSet.Source,
                }).ToList(),
            });
        }

        private bool Coded(InvoiceLineToCode line) => !string.IsNullOrWhiteSpace(line.Narrative) && !NotReturnedKeys.Contains(line.LineKey);
    }
}
