using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 049 part C: step 6, all-or-nothing persistence (<see cref="InvoiceSubmissionPersister"/>). The outcomes are produced by the
/// real processor (part B harness: real 072 evaluation and 073 decision); Dataverse is a strict double, so any write outside the one
/// transaction fails the test. Task 092: an upload's PDF is staged in the real Finance file store over an in-memory blob gateway
/// (<see cref="StoredFileRig"/>); the committed transaction's Stored File create lands in that store's Dataverse, so the flip after the
/// commit runs for real.
/// </summary>
public class InvoiceSubmissionPersisterTests
{
    private static readonly Guid Submission = Guid.Parse("04900000-0000-4000-8000-0000000000b1");
    private static readonly Guid Matter = Guid.Parse("04900000-0000-4000-8000-0000000000a1");
    private static readonly Guid Project = Guid.Parse("04900000-0000-4000-8000-0000000000a2");
    private static readonly Guid Document = Guid.Parse("04900000-0000-4000-8000-0000000000d1");
    private static readonly Guid Firm = Guid.Parse("04900000-0000-4000-8000-0000000000f1");
    private static readonly Guid TeamId = Guid.Parse("04900000-0000-4000-8000-0000000000e7");
    private static readonly Guid RecordTypeId = Guid.Parse("04900000-0000-4000-8000-0000000000e8");

    private static readonly Guid Submitter = Guid.Parse("04900000-0000-4000-8000-0000000000c9");
    private static readonly Guid SubmitterContact = Guid.Parse("04900000-0000-4000-8000-0000000000ca");

    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(Submission);

    /// <summary>Task 092: the upload's Stored File id (the intake staged it, the transaction creates its row).</summary>
    private static readonly Guid FileId = StoredFileIds.ForUpload(Submission);

    // ── the four persisted kinds ──

    [Fact(DisplayName = "049 C / 092: Accepted is ONE transaction: invoice (In Review), the invoice PDF's Stored File, lines, approval, submission update")]
    public async Task Accepted_IsOneTransaction_WithEveryRow()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, "corr-1");

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        result.InvoiceId.Should().Be(InvoiceId);
        result.LineIds.Should().Equal(new Dictionary<int, Guid> { [0] = InvoiceSubmissionPersister.LineIdFor(Submission, 0) });
        result.ApprovalId.Should().Be(InvoiceSubmissionPersister.ApprovalIdFor(Submission));
        store.Transactions.Should().ContainSingle("FR-20: everything in one transaction");

        var requests = store.Transactions[0];
        requests.Select(Describe).Should().Equal(
            "Create sprk_invoice", "Create sprk_storedfile", "Create sprk_billingevent", "Create sprk_invoiceapproval", "Update sprk_invoicesubmission");

        var invoice = Target(requests[0]);
        invoice.Id.Should().Be(InvoiceId);
        invoice.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(0);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview, "status only through 014");
        invoice.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-100.2", "D-124: the tracking number is copied from the submission");
        invoice.GetAttributeValue<string>("sprk_invoicenumber").Should().Be("INV-100", "the firm number as typed (D-91)");
        invoice.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(Firm);
        invoice.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(Matter, "filed under the core record that was judged");
        invoice.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(Matter.ToString("D"));
        invoice.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id.Should().Be(RecordTypeId);
        invoice.GetAttributeValue<string>("sprk_correlationid").Should().Be("corr-1");
        invoice.Attributes.Should().NotContainKey("sprk_document", "an upload has no document (task 092)");
        invoice.GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(750m);

        // Task 092 (model 4.1, 9.1 step 4): the invoice PDF's row, Not yet servable, under the invoice, Firm-visible for the SPA channel.
        var file = Target(requests[1]);
        file.Id.Should().Be(FileId);
        file.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose).Value.Should().Be((int)StoredFilePurpose.InvoicePdf);
        file.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value.Should().Be((int)StoredFileStatus.NotYetServable);
        file.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId);
        file.GetAttributeValue<bool>(StoredFileSchema.FirmVisible).Should().BeTrue("the firm uploaded it through the SPA");
        file.GetAttributeValue<string>(StoredFileSchema.Sha256).Should().Be(store.StagedBlob!.Sha256);
        file.GetAttributeValue<string>(StoredFileSchema.BlobPath).Should().Be(store.StagedBlob.BlobPath);
        file.GetAttributeValue<string>(StoredFileSchema.Name).Should().Be("INV-100.2.pdf");
        file.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("team", TeamId), "owned like the invoice");
        file.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Submitter);
        file.Attributes.Keys.Should().NotContain(new[] { StoredFileSchema.SourceDocument, StoredFileSchema.SourceCommunication }, "an upload has no source");

        var line = Target(requests[2]);
        line.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId);
        line.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(Matter, "D-75: the line's own core lookup");
        line.GetAttributeValue<int>("sprk_linesequence").Should().Be(1);
        line.GetAttributeValue<OptionSetValue>("sprk_costtype").Value.Should().Be(InvoiceSubmissionPersister.CostTypeFee);
        line.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be(InvoiceSubmissionPersister.LineStatusPending);
        line.GetAttributeValue<Money>("sprk_amount").Value.Should().Be(750m);
        line.GetAttributeValue<decimal>("sprk_quantity").Should().Be(1.5m, "a fee line's hours");
        line.GetAttributeValue<string>("sprk_timekeeper").Should().Be("Jane Smith");
        line.GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper").Should().NotBeNull();
        line.GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(500m);
        line.GetAttributeValue<decimal>("sprk_codingconfidence").Should().Be(0.95m);
        line.GetAttributeValue<string>("sprk_taskcodeasbilled").Should().Be("L110");
        using (var confidence = JsonDocument.Parse(line.GetAttributeValue<string>("sprk_extractionconfidence")))
        {
            var coding = confidence.RootElement.GetProperty("coding");
            coding.GetProperty("codeSetSource").GetString().Should().Be(nameof(CodeSetSource.FirmCodes), "047/048 review: stored with the line");
            coding.GetProperty("needsReview").GetBoolean().Should().BeFalse();
            confidence.RootElement.GetProperty("fields").GetProperty("Hours").GetProperty("page").GetInt32().Should().Be(1);
        }

        var approval = Target(requests[3]);
        approval.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceSubmissionPersister.ApprovalStatusPending);
        approval.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId);
        approval.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(Matter);
        approval.Attributes.Keys.Should().NotContain(k => k.Contains("approver"), "approvers are 103's");

        var submission = Target(requests[4]);
        submission.Id.Should().Be(Submission);
        submission.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId, "the link is the commit marker");
        submission.Attributes.Should().NotContainKey("sprk_processingstatus", "D-130 a: the submission stays Processing inside the transaction");
        submission["sprk_outcomereasoncode"].Should().BeNull();
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Accepted);
        result.TerminalReasonCode.Should().BeNull();
        using (var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson")))
        {
            findings.RootElement.GetProperty("invoiceId").GetString().Should().Be(InvoiceId.ToString("D"));
            findings.RootElement.GetProperty("decision").GetProperty("outcome").GetString().Should().Be("Accepted");
        }

        store.TotalsCalls.Should().Equal(InvoiceId);
        store.FileStatusAtTotals.Should().Equal([StoredFileStatus.Servable], "the flip runs right after the commit, before the totals");
        store.FileStatus.Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "049 C (D-101): AutoRejected creates the invoice Rejected through 014, no approval; the submission is Rejected with the deciding code and the findings")]
    public async Task AutoRejected_InvoiceRejected_FindingsOnTheSubmission_NoApproval()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        result.ApprovalId.Should().BeNull();
        var requests = store.Transactions.Should().ContainSingle().Subject;
        requests.Select(Describe).Should().NotContain("Create sprk_invoiceapproval");
        var invoice = Target(requests[0]);
        invoice.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(1);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Rejected);

        var submission = Target(requests[^1]);
        submission.Attributes.Should().NotContainKey("sprk_processingstatus", "D-130 a: Processing until part D's last step");
        submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(outcome.ReasonCode).And.NotBeNullOrEmpty();
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        result.TerminalReasonCode.Should().Be(outcome.ReasonCode);
        submission.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId, "the findings and the invoice are linked to each other");
        using var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"));
        var deciding = findings.RootElement.GetProperty("findings").EnumerateArray().Should().ContainSingle(f => f.GetProperty("rejects").GetBoolean()).Subject;
        deciding.GetProperty("reasonCode").GetString().Should().Be(outcome.ReasonCode);
        deciding.GetProperty("policyVersionId").GetString().Should().NotBeNullOrEmpty();
        deciding.GetProperty("valuesRead").GetArrayLength().Should().BeGreaterThan(0);
        findings.RootElement.GetProperty("invoiceId").GetString().Should().Be(InvoiceId.ToString("D"));
    }

    [Fact(DisplayName = "049 C (D-101): Blocked creates the invoice In Review (held) with its approval; the submission is Rejected + ebilling.submission.blocked")]
    public async Task Blocked_InvoiceInReview_SubmissionRejectedBlocked()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var requests = store.Transactions.Should().ContainSingle().Subject;
        Target(requests[0]).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        requests.Select(Describe).Should().Contain("Create sprk_invoiceapproval");
        var submission = Target(requests[^1]);
        submission.Attributes.Should().NotContainKey("sprk_processingstatus", "D-130 a");
        submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(SubmissionReasonCodes.Blocked);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected, "the held representation: Rejected + blocked at terminal time");
        result.TerminalStatus.Should().Be((InvoiceSubmissionStatus)100000003);
        result.TerminalReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
    }

    [Fact(DisplayName = "049 C (D-126): read but not coded -> invoice In Review, lines uncoded, approval, submission Accepted, hold finding stored")]
    public async Task AcceptedUncoded_InvoiceHeld_LinesUncoded()
    {
        var harness = new ProcessorHarness();
        harness.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");
        var outcome = await harness.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AcceptedUncoded);
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var requests = store.Transactions.Should().ContainSingle().Subject;
        Target(requests[0]).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        var line = Target(requests[2]);
        line.Attributes.Should().NotContainKey("sprk_codingconfidence", "the lines are uncoded");
        using (var confidence = JsonDocument.Parse(line.GetAttributeValue<string>("sprk_extractionconfidence")))
        {
            confidence.RootElement.GetProperty("coding").GetProperty("status").GetString().Should().Be("NotCoded");
            confidence.RootElement.GetProperty("coding").GetProperty("hold").GetString().Should().Be(SubmissionReasonCodes.CodingNoCodeSetHold);
        }

        requests.Select(Describe).Should().Contain("Create sprk_invoiceapproval");
        var submission = Target(requests[^1]);
        submission.Attributes.Should().NotContainKey("sprk_processingstatus", "D-130 a");
        submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(SubmissionReasonCodes.CodingNoCodeSetHold, "D-130 b");
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Accepted);
        result.TerminalReasonCode.Should().Be(SubmissionReasonCodes.CodingNoCodeSetHold);
        using var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"));
        findings.RootElement.GetProperty("pipelineFindings").EnumerateArray()
            .Select(f => f.GetProperty("reasonCode").GetString()).Should().Contain(SubmissionReasonCodes.CodingNoCodeSetHold);
    }

    [Fact(DisplayName = "049 C2 (D-130 1): uncoded + a firm-data reject is persisted Rejected (no approval), keeping the rule finding AND the coding hold finding")]
    public async Task UncodedAutoRejected_InvoiceRejected_BothFindingsStored()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        harness.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");
        var outcome = await harness.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        result.ApprovalId.Should().BeNull();
        var requests = store.Transactions.Should().ContainSingle().Subject;
        Target(requests[0]).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Rejected);
        var line = Target(requests[2]);
        line.Attributes.Should().NotContainKey("sprk_codingconfidence");
        var submission = Target(requests[^1]);
        submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().Be(outcome.ReasonCode);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        result.TerminalReasonCode.Should().Be(outcome.ReasonCode);
        using var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"));
        findings.RootElement.GetProperty("findings").EnumerateArray().Should().ContainSingle(f => f.GetProperty("rejects").GetBoolean());
        findings.RootElement.GetProperty("pipelineFindings").EnumerateArray()
            .Select(f => f.GetProperty("reasonCode").GetString()).Should().Contain(SubmissionReasonCodes.CodingNoCodeSetHold);
        findings.RootElement.GetProperty("codeSet").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── D-130 (3): the submitter ──

    [Fact(DisplayName = "049 C2 (D-130 3): the submission's creator person is stamped as sprk_createdbyperson on the invoice, every line and the approval")]
    public async Task Submitter_IsStamped_OnInvoice_Lines_AndApproval()
    {
        var harness = new ProcessorHarness();
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(), InvoiceSubmissionProcessorTests.FeeLine(hours: 2m, amount: 1000m)];
        var outcome = await harness.RunAsync();
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var creates = store.Transactions.Single().OfType<CreateRequest>().Select(c => c.Target).ToList();
        creates.Select(c => c.LogicalName).Should().Equal("sprk_invoice", "sprk_storedfile", "sprk_billingevent", "sprk_billingevent", "sprk_invoiceapproval");
        creates.Should().OnlyContain(c => c.GetAttributeValue<EntityReference>("sprk_createdbyperson")!.Id == Submitter
                && c.GetAttributeValue<EntityReference>("sprk_createdbyperson")!.LogicalName == "systemuser",
            "the submitter is the person on every kind of row, the invoice PDF's Stored File included");
        Target(store.Transactions.Single()[^1]).Attributes.Should().NotContainKey("sprk_createdbyperson", "the submission carries its own stamp from intake");
    }

    [Fact(DisplayName = "049 C3 (D-131): a SPA submission with a person and a contact stamps both on the invoice; lines and approval carry the person only")]
    public async Task PersonAndContact_InvoiceCarriesBoth_LinesAndApprovalCarryThePersonOnly()
    {
        var harness = new ProcessorHarness();
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(), InvoiceSubmissionProcessorTests.FeeLine(hours: 2m, amount: 1000m)];
        var outcome = await harness.RunAsync();
        var store = new Store();
        store.Submission["sprk_submitter"] = new EntityReference("contact", SubmitterContact);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var creates = store.Transactions.Single().OfType<CreateRequest>().Select(c => c.Target).ToList();
        creates.Select(c => c.LogicalName).Should().Equal("sprk_invoice", "sprk_storedfile", "sprk_billingevent", "sprk_billingevent", "sprk_invoiceapproval");
        var invoice = creates[0];
        invoice.GetAttributeValue<EntityReference>("sprk_createdbyperson")!.Id.Should().Be(Submitter);
        var submittedBy = invoice.GetAttributeValue<EntityReference>("sprk_submittedby")!;
        submittedBy.LogicalName.Should().Be("contact");
        submittedBy.Id.Should().Be(SubmitterContact);
        creates.Should().OnlyContain(c => c.GetAttributeValue<EntityReference>("sprk_createdbyperson")!.Id == Submitter);
        creates.Skip(1).Should().OnlyContain(c => !c.Attributes.ContainsKey("sprk_submittedby"), "lines and the approval have no such column");
        store.SubmissionReadColumns.Should().OnlyContain(columns => columns.Contains("sprk_submitter"), "the re-read must ask for the contact");
    }

    [Fact(DisplayName = "049 C3 (D-131): a SPA submission with a contact and no linked user commits without sprk_createdbyperson and warns once")]
    public async Task ContactOnly_CommitsWithoutCreatorPerson_AndWarnsOnce()
    {
        var harness = new ProcessorHarness();
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(), InvoiceSubmissionProcessorTests.FeeLine(hours: 2m, amount: 1000m)];
        var outcome = await harness.RunAsync();
        var logger = new CapturingLogger();
        var store = new Store { Logger = logger };
        store.Submission.Attributes.Remove("sprk_createdbyperson");
        store.Submission["sprk_submitter"] = new EntityReference("contact", SubmitterContact);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var creates = store.Transactions.Single().OfType<CreateRequest>().Select(c => c.Target).ToList();
        creates.Select(c => c.LogicalName).Should().Equal("sprk_invoice", "sprk_storedfile", "sprk_billingevent", "sprk_billingevent", "sprk_invoiceapproval");
        creates.Should().OnlyContain(c => !c.Attributes.ContainsKey("sprk_createdbyperson"), "an unknown person refuses nothing; the row records nobody (K6 for the Stored File)");
        creates[0].GetAttributeValue<EntityReference>("sprk_submittedby")!.Id.Should().Be(SubmitterContact);
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("sprk_createdbyperson").And.Contain(Submission.ToString())
            .And.NotContain(SubmitterContact.ToString(), "no personal data in a log (ADR-015)");
    }

    [Fact(DisplayName = "049 C3 (D-131): an internal submission (person, no contact) stamps the person and no sprk_submittedby")]
    public async Task PersonOnly_NoSubmittedBy()
    {
        var outcome = await new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal }.RunAsync();
        var logger = new CapturingLogger();
        var store = new Store { Logger = logger };
        store.Submission["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var creates = store.Transactions.Single().OfType<CreateRequest>().Select(c => c.Target).ToList();
        creates.Should().OnlyContain(c => c.GetAttributeValue<EntityReference>("sprk_createdbyperson")!.Id == Submitter);
        creates.Should().OnlyContain(c => !c.Attributes.ContainsKey("sprk_submittedby"));
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact(DisplayName = "049 C2 (D-130 3): a submission that records no submitter is refused before any write (fails closed)")]
    public async Task NoSubmitter_Refuses_BeforeAnyWrite()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        store.Submission.Attributes.Remove("sprk_createdbyperson");

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.SubmitterMissingCode).And.Be("ebilling.submission.submitter_missing");
        result.InvoiceId.Should().BeNull();
        result.TerminalStatus.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.TotalsCalls.Should().BeEmpty();
    }

    // ── D-130 (a): Processing until part D ──

    [Fact(DisplayName = "049 C2 (D-130 a): a resumed submission exposes the same terminal status and reason code, and writes nothing")]
    public async Task Resumed_ExposesTheTerminalStatus()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        var store = new Store();
        store.Submission["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Resumed);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Rejected);
        result.TerminalReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 C2 (D-130 a): the committed transaction leaves the submission Processing")]
    public async Task Transaction_LeavesTheSubmissionProcessing()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();

        await store.Build().PersistAsync(outcome, null);

        store.Submission.GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value.Should().Be((int)InvoiceSubmissionStatus.Processing);
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId);
    }

    // ── approval record number ──

    [Fact(DisplayName = "049 C2: the approval's regarding record number is cut to its Text(100) column; a short one is untouched")]
    public async Task ApprovalRecordNumber_IsTrimmedTo100()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var longNumber = new string('N', 150);
        var store = new Store { MatterNumber = longNumber };

        await store.Build().PersistAsync(outcome, null);

        var approval = Target(store.Transactions.Single().Single(r => Describe(r) == "Create sprk_invoiceapproval"));
        approval.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be(new string('N', 100));

        var shortStore = new Store();
        await shortStore.Build().PersistAsync(outcome, null);
        Target(shortStore.Transactions.Single().Single(r => Describe(r) == "Create sprk_invoiceapproval"))
            .GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("M-1");
    }

    // ── line rows ──

    [Fact(DisplayName = "049 C (D-121): a credit line keeps its negative amount and hours")]
    public async Task CreditLine_IsStoredNegative()
    {
        var harness = new ProcessorHarness();
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(), InvoiceSubmissionProcessorTests.FeeLine(hours: -1m, amount: -500m)];
        var outcome = await harness.RunAsync();
        var store = new Store();

        await store.Build().PersistAsync(outcome, null);

        var credit = Target(store.Transactions.Single()[3]);
        credit.GetAttributeValue<int>("sprk_linesequence").Should().Be(2);
        credit.GetAttributeValue<Money>("sprk_amount").Value.Should().Be(-500m);
        credit.GetAttributeValue<decimal>("sprk_quantity").Should().Be(-1m);
        Target(store.Transactions.Single()[0]).GetAttributeValue<Money>("sprk_totalamount").Value.Should().Be(250m, "the billed total is signed");
    }

    [Fact(DisplayName = "049 C: a line with a finding is Flagged")]
    public async Task LineWithAFinding_IsFlagged()
    {
        var harness = new ProcessorHarness { Rules = [Version("BILL-AI", RuleType.AiObservation)] };
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];
        var outcome = await harness.RunAsync();
        outcome.Evaluation!.Findings.Should().Contain(f => f.Subject.Kind == BillingSubjectKind.Line && f.Subject.LineIndex == 0);
        var store = new Store();

        await store.Build().PersistAsync(outcome, null);

        Target(store.Transactions.Single()[2]).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value
            .Should().Be(InvoiceSubmissionPersister.LineStatusFlagged);
    }

    // ── owner ──

    [Fact(DisplayName = "049 C (uac-r2): the owner is resolved once from the core record and applied to every created row")]
    public async Task Owner_IsResolvedFromTheCoreRecord_OnEveryRow()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();

        await store.Build().PersistAsync(outcome, null);

        store.OwnerContexts.Should().ContainSingle().Which.Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", Matter));
        foreach (var create in store.Transactions.Single().OfType<CreateRequest>())
        {
            create.Target.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("team", TeamId), create.Target.LogicalName);
        }
    }

    [Fact(DisplayName = "049 C: an owner that cannot be resolved refuses before any write")]
    public async Task OwnerRefused_RefusesBeforeAnyWrite()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { Owner = RecordOwnerResolution.Refused("record_owner_parent_undetermined", "no") };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be("record_owner_parent_undetermined");
        store.Transactions.Should().BeEmpty();
    }

    // ── faults ──

    [Fact(DisplayName = "049 C (FR-20): a fault at the line request persists nothing and leaves the submission for retry, never Accepted")]
    public async Task FaultAtTheLineRequest_PersistsNothing_Retry()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { TransactionFault = new DataverseTransactionException("line refused", failedRequestIndex: 2, outcomeUnknown: false, null) };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionFailedCode);
        result.InvoiceId.Should().BeNull("nothing was committed");
        Describe(store.Transactions.Single()[2]).Should().Be("Create sprk_billingevent", "the fault named the line request");
        store.Files.Dataverse.Rows.Should().BeEmpty("a rolled-back transaction leaves no Stored File row");
        store.Files.Gateway.Names.Should().ContainSingle("the staged blob is kept for the retry");
        store.TotalsCalls.Should().BeEmpty();
        store.Submission.Attributes.Should().NotContainKey("sprk_invoice");
        store.Submission.GetAttributeValue<OptionSetValue>("sprk_processingstatus").Value.Should().Be((int)InvoiceSubmissionStatus.Processing,
            "the strict double proves no write outside the failed transaction");
    }

    [Fact(DisplayName = "049 C: an unknown outcome whose re-read shows the link is committed (resume), with the totals")]
    public async Task OutcomeUnknown_Linked_IsCommitted()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store
        {
            TransactionFault = new DataverseTransactionException("timeout", null, outcomeUnknown: true, new TimeoutException()),
            CommitDespiteFault = true,
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        result.InvoiceId.Should().Be(InvoiceId);
        result.LineIds.Should().ContainKey(0);
        store.TotalsCalls.Should().Equal(InvoiceId);
        store.Transactions.Should().ContainSingle();
    }

    [Fact(DisplayName = "049 C: an unknown outcome whose re-read shows no link is a retry, nothing assumed")]
    public async Task OutcomeUnknown_NotLinked_IsRetry()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { TransactionFault = new DataverseTransactionException("timeout", null, outcomeUnknown: true, new TimeoutException()) };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionOutcomeUnknownCode);
        result.InvoiceId.Should().BeNull();
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 C (016): a totals failure after the commit is a retry that resumes, never a second invoice")]
    public async Task TotalsFailure_AfterCommit_RetryResumes_NoSecondInvoice()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { TotalsFault = new InvalidOperationException("totals down") };

        var first = await store.Build().PersistAsync(outcome, null);
        store.TotalsFault = null;
        var second = await store.Build().PersistAsync(outcome, null);

        first.Status.Should().Be(SubmissionPersistStatus.Retry);
        first.ReasonCode.Should().Be(InvoiceSubmissionPersister.TotalsFailedCode);
        first.InvoiceId.Should().Be(InvoiceId, "the invoice was committed");
        second.Status.Should().Be(SubmissionPersistStatus.Resumed);
        second.InvoiceId.Should().Be(InvoiceId);
        store.Transactions.Should().ContainSingle("the retry resumes from the link and creates nothing");
        store.TotalsCalls.Should().Equal([InvoiceId], "R2: only the first call (the commit) ran the totals; the resume path does none, the after-steps' ResumeAsync recalculates them");
    }

    [Fact(DisplayName = "049 R2: a submission found already linked is Resumed even when the totals would fault; it never answers Retry-with-invoice (which the handler reads as 'committed by this delivery')")]
    public async Task AlreadyLinked_WithATotalsFault_IsStillResumed_AndNeverRecalculates()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { TotalsFault = new InvalidOperationException("totals down") };
        store.Submission["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Resumed);
        result.InvoiceId.Should().Be(InvoiceId);
        result.ReasonCode.Should().BeNull();
        store.TotalsCalls.Should().BeEmpty();
        store.Transactions.Should().BeEmpty();
    }

    // ── re-check and resume ──

    [Fact(DisplayName = "049 C (072 part L): a core record changed since the evaluation is re-evaluated, never persisted")]
    public async Task CoreRecordChanged_ReEvaluates_PersistsNothing()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        store.Submission.Attributes.Remove("sprk_regardingmatter");
        store.Submission["sprk_regardingproject"] = new EntityReference("sprk_project", Project);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.ReEvaluate);
        result.CurrentSubmission!.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(Project);
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 C (NFR-08): a submission already linked creates no rows and returns the stored invoice and line ids")]
    public async Task AlreadyLinked_CreatesNothing_ReturnsStoredIds()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var storedInvoice = Guid.NewGuid();
        var storedLine = Guid.NewGuid();
        var store = new Store();
        store.Submission["sprk_invoice"] = new EntityReference("sprk_invoice", storedInvoice);
        store.StoredLines.Add(new Entity("sprk_billingevent", storedLine) { ["sprk_linesequence"] = 1 });

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Resumed);
        result.InvoiceId.Should().Be(storedInvoice);
        result.LineIds.Should().Equal(new Dictionary<int, Guid> { [0] = storedLine });
        store.Transactions.Should().BeEmpty();
        store.TotalsCalls.Should().BeEmpty("R2: nothing is recalculated on the resume path; the after-steps' ResumeAsync does it");
    }

    [Fact(DisplayName = "049 C: a deleted submission is SubmissionMissing with nothing written")]
    public async Task MissingSubmission_WritesNothing()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { SubmissionDeleted = true };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.SubmissionMissing);
        store.Transactions.Should().BeEmpty();
    }

    // ── contract ──

    [Fact(DisplayName = "049 C (D-101): a Failed or RejectedUnreadable outcome is never persisted")]
    public async Task NotReadOutcome_IsACallerError()
    {
        var store = new Store();
        var failed = new SubmissionEvaluationOutcome { Kind = SubmissionEvaluationKind.Failed, SubmissionId = Submission, ReasonCode = "x" };
        var unreadable = new SubmissionEvaluationOutcome { Kind = SubmissionEvaluationKind.RejectedUnreadable, SubmissionId = Submission, ReasonCode = "y" };

        await FluentActions.Invoking(() => store.Build().PersistAsync(failed, null)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => store.Build().PersistAsync(unreadable, null)).Should().ThrowAsync<ArgumentException>();
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 C: ids are deterministic per submission, so a retry reuses them")]
    public void Ids_AreDeterministic()
    {
        InvoiceSubmissionPersister.InvoiceIdFor(Submission).Should().Be(InvoiceSubmissionPersister.InvoiceIdFor(Submission));
        InvoiceSubmissionPersister.InvoiceIdFor(Submission).Should().NotBe(InvoiceSubmissionPersister.InvoiceIdFor(Guid.NewGuid()));
        InvoiceSubmissionPersister.LineIdFor(Submission, 0).Should().NotBe(InvoiceSubmissionPersister.LineIdFor(Submission, 1));
        new[] { InvoiceSubmissionPersister.InvoiceIdFor(Submission), InvoiceSubmissionPersister.LineIdFor(Submission, 0), InvoiceSubmissionPersister.ApprovalIdFor(Submission) }
            .Should().OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "049 C (D-112): a withheld finding's group stores no values read")]
    public async Task WithheldFinding_StoresNoValuesRead()
    {
        var harness = new ProcessorHarness { Rules = [Version("BILL-AI", RuleType.AiObservation)] };
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];
        var outcome = await harness.RunAsync();
        var withheld = outcome with
        {
            Evaluation = outcome.Evaluation! with { Findings = [.. outcome.Evaluation.Findings.Select(f => f with { DetailWithheld = true })] },
        };

        using var visible = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));
        using var hidden = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(withheld, InvoiceId));

        visible.RootElement.GetProperty("findings")[0].TryGetProperty("valuesRead", out _).Should().BeTrue("must-not-fire: a visible finding keeps its values");
        hidden.RootElement.GetProperty("findings")[0].TryGetProperty("valuesRead", out _).Should().BeFalse();
        hidden.RootElement.GetProperty("findings")[0].GetProperty("detailWithheld").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "100: a finding's suggested adjustment is stored for the review read model; a withheld group stores none")]
    public async Task FindingsJson_StoresTheSuggestedAdjustment_ExceptForAWithheldGroup()
    {
        var harness = new ProcessorHarness { Rules = [Version("BILL-AI", RuleType.AiObservation)] };
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];
        var outcome = await harness.RunAsync();
        var suggested = outcome with
        {
            Evaluation = outcome.Evaluation! with
            {
                Findings = [.. outcome.Evaluation.Findings.Select(f => f with { SuggestedAdjustment = new BillingSuggestedAdjustment(BillingAdjustmentKind.Reduce, 120.50m) })],
            },
        };
        var withheld = suggested with
        {
            Evaluation = suggested.Evaluation! with { Findings = [.. suggested.Evaluation.Findings.Select(f => f with { DetailWithheld = true })] },
        };

        using var visible = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(suggested, InvoiceId));
        using var hidden = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(withheld, InvoiceId));
        using var none = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));

        var entry = visible.RootElement.GetProperty("findings")[0].GetProperty("suggestedAdjustment");
        entry.GetProperty("kind").GetString().Should().Be("Reduce");
        entry.GetProperty("amount").GetDecimal().Should().Be(120.50m);
        hidden.RootElement.GetProperty("findings")[0].TryGetProperty("suggestedAdjustment", out _).Should().BeFalse("D-112: a withheld group stores no detail");
        none.RootElement.GetProperty("findings")[0].TryGetProperty("suggestedAdjustment", out _).Should().BeFalse("must-not-fire: a rule that suggests nothing stores nothing");
    }

    // ── part D: the stored Signal plan ──

    [Fact(DisplayName = "049 D: the findings JSON stores the Signal plan (merged request, sentence, fingerprint, 073's decision) on the persisted row ids")]
    public async Task FindingsJson_StoresTheSignalPlan_WithTheDecidingReasonAndTheRowIds()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));

        var plan = json.RootElement.GetProperty("signalPlan");
        plan.GetArrayLength().Should().Be(1);
        var entry = plan[0];
        entry.GetProperty("policyCode").GetString().Should().Be("BILL-AGE");
        entry.GetProperty("subjectEntity").GetString().Should().Be("sprk_billingevent");
        entry.GetProperty("subjectId").GetString().Should().Be(InvoiceSubmissionPersister.LineIdFor(Submission, 0).ToString("D"), "the row the persister creates");
        entry.GetProperty("subjectName").GetString().Should().Be("INV-100.2 #1");
        entry.GetProperty("decision").GetString().Should().Be(nameof(BillingGroupDecision.Rejects));
        entry.GetProperty("fingerprint").GetString().Should().EndWith("|Reject");
        entry.GetProperty("sentence").GetString().Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
        entry.GetProperty("evidence").EnumerateArray().Should().Contain(e => e.GetProperty("kind").GetString() == BillingFindingSignalMapper.FingerprintEvidenceKind);
        json.RootElement.GetProperty("invoiceName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "049 Held: a Blocked outcome's stored plan entry is the held presentation (headline, sentence with the deciding reason, Held line); the rejected outcome's is not")]
    public async Task SignalPlan_Blocked_StoresTheHeldPresentation()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var blocked = await harness.RunAsync();
        blocked.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        var rejected = blocked with { Kind = SubmissionEvaluationKind.AutoRejected };

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(blocked, InvoiceId));
        using var control = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(rejected, InvoiceId));

        var entry = json.RootElement.GetProperty("signalPlan").EnumerateArray().Single();
        var plain = control.RootElement.GetProperty("signalPlan").EnumerateArray().Single();
        entry.GetProperty("headline").GetString().Should().Be("Invoice held for review");
        entry.GetProperty("sentence").GetString().Should().Be("The rejection rule would reject this invoice: " + plain.GetProperty("sentence").GetString());
        entry.GetProperty("decision").GetString().Should().Be(nameof(BillingGroupDecision.Blocked));
        entry.GetProperty("fingerprint").GetString().Should().Be(plain.GetProperty("fingerprint").GetString());
        entry.GetProperty("evidence").EnumerateArray().Should().ContainSingle(e => e.GetProperty("kind").GetString() == BillingFindingSignalMapper.DecisionEvidenceKind)
            .Which.GetProperty("text").GetString().Should().Be("ebilling.submission.blocked");
        json.RootElement.TryGetProperty(InvoiceSubmissionPersister.SignalPlanRefusalsMember, out _).Should().BeFalse();

        plain.GetProperty("headline").GetString().Should().NotBe("Invoice held for review");
        plain.GetProperty("decision").GetString().Should().Be(nameof(BillingGroupDecision.Rejects));
        plain.GetProperty("evidence").EnumerateArray().Should().NotContain(e => e.GetProperty("kind").GetString() == BillingFindingSignalMapper.DecisionEvidenceKind);
    }

    [Fact(DisplayName = "049 Held (F1-3 preflight): a Blocked outcome whose reason fits alone but not behind the held wording keeps its Signal with the no-value wording, not a persist refusal")]
    public async Task SignalPlan_Blocked_OverflowingReason_FallsBackToTheNoValueWording()
    {
        var room = 2000 - "The rejection rule would reject this invoice: ".Length;
        var rule = InvoiceSubmissionProcessorTests.InvoiceAgeReject() with { MessageTemplate = new string('a', room + 1) };
        var harness = new ProcessorHarness { Rules = [rule], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var blocked = await harness.RunAsync();
        blocked.Kind.Should().Be(SubmissionEvaluationKind.Blocked);

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(blocked, InvoiceId));
        using var control = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(blocked with { Kind = SubmissionEvaluationKind.AutoRejected }, InvoiceId));

        control.RootElement.GetProperty("signalPlan").GetArrayLength().Should().Be(1, "the reason alone is within the sentence limit");
        json.RootElement.TryGetProperty(InvoiceSubmissionPersister.SignalPlanRefusalsMember, out _).Should().BeFalse();
        var entry = json.RootElement.GetProperty("signalPlan").EnumerateArray().Single();
        entry.GetProperty("sentence").GetString().Should().Be("The rejection rule would reject this invoice.");
        entry.GetProperty("headline").GetString().Should().Be("Invoice held for review");
    }

    [Fact(DisplayName = "049 D (D-112): a withheld group's stored plan carries no value text")]
    public async Task SignalPlan_WithheldGroup_HasNoValueText()
    {
        var harness = new ProcessorHarness { Rules = [Version("BILL-AI", RuleType.AiObservation)] };
        harness.Coding.Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.99m, QuotedText = "x" }];
        var outcome = await harness.RunAsync();
        var withheld = outcome with
        {
            Evaluation = outcome.Evaluation! with { Findings = [.. outcome.Evaluation.Findings.Select(f => f with { DetailWithheld = true })] },
        };

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(withheld, InvoiceId));

        var entry = json.RootElement.GetProperty("signalPlan")[0];
        entry.GetProperty("template").GetString().Should().Be(BillingRuleCatalogue.WithheldDuplicateTemplate);
        entry.GetProperty("facts").EnumerateObject().Should().BeEmpty();
        entry.GetProperty("evidence").EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() is BillingFindingSignalMapper.FieldEvidenceKind or BillingFindingSignalMapper.ReferenceEvidenceKind)
            .Should().OnlyContain(e => e.GetProperty("text").ValueKind == JsonValueKind.Null);
    }

    // ── 049 F1-3: the writer's preflight at persist ──

    /// <summary>An AutoRejected outcome whose plan has two entries: BILL-AGE (writable) and BILL-LONG, whose authored template renders to
    /// 2,100 characters (over <c>sprk_sentence</c>), so the writer would refuse it on every attempt.</summary>
    internal static async Task<SubmissionEvaluationOutcome> OutcomeWithAnOverLongPlanEntryAsync()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        var evaluation = outcome.Evaluation!;
        var age = evaluation.Findings.Single();
        var longRule = InvoiceSubmissionProcessorTests.InvoiceAgeReject() with
        {
            PolicyId = Id("policy:BILL-LONG"),
            PolicyCode = "BILL-LONG",
            PolicyVersionId = Id("version:BILL-LONG:1"),
            MessageTemplate = new string('x', 2100),
        };
        var rules = evaluation.RulesByVersion.ToDictionary(kv => kv.Key, kv => kv.Value);
        rules[longRule.PolicyVersionId] = longRule;
        var longFinding = age with { PolicyId = longRule.PolicyId, PolicyCode = longRule.PolicyCode, PolicyVersionId = longRule.PolicyVersionId };
        var decision = outcome.Decision!;
        var ageDecision = decision.Findings.Single();
        return outcome with
        {
            Evaluation = evaluation with { Findings = [age, longFinding], RulesByVersion = rules },
            Decision = decision with { Findings = [ageDecision, ageDecision with { Index = 1, Finding = longFinding, Rejects = false }] },
        };
    }

    [Fact(DisplayName = "049 F1-3: an entry the writer would refuse is left out of the plan and recorded by code; the rest is kept")]
    public async Task SignalPlan_DropsOnlyTheRefusedEntry_AndRecordsIt()
    {
        var outcome = await OutcomeWithAnOverLongPlanEntryAsync();

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));

        var plan = json.RootElement.GetProperty("signalPlan");
        plan.EnumerateArray().Select(e => e.GetProperty("policyCode").GetString()).Should().Equal("BILL-AGE");
        var refusal = json.RootElement.GetProperty(InvoiceSubmissionPersister.SignalPlanRefusalsMember).EnumerateArray().Should().ContainSingle().Subject;
        refusal.GetProperty("policyCode").GetString().Should().Be("BILL-LONG");
        refusal.GetProperty("code").GetString().Should().Be(SignalWriteRefusal.SentenceTooLong);
        refusal.GetProperty("subject").GetProperty("entity").GetString().Should().Be("sprk_billingevent");
        refusal.GetProperty("subject").GetProperty("id").GetString().Should().Be(InvoiceSubmissionPersister.LineIdFor(Submission, 0).ToString("D"));
        refusal.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["policyCode", "subject", "code"], "codes only, no values");
        json.RootElement.GetProperty("findings").GetArrayLength().Should().Be(2, "the finding itself stays in the findings");
    }

    [Fact(DisplayName = "049 F1-3: a plan the writer accepts records no refusals member (must-not-fire)")]
    public async Task SignalPlan_NothingRefused_NoRefusalsMember()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();

        using var json = JsonDocument.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));

        json.RootElement.TryGetProperty(InvoiceSubmissionPersister.SignalPlanRefusalsMember, out _).Should().BeFalse();
    }

    [Fact(DisplayName = "049 F1-3: persisting a plan with a refused entry commits, and warns once with codes only")]
    public async Task Persist_WithARefusedPlanEntry_Commits_AndWarnsOnce()
    {
        var outcome = await OutcomeWithAnOverLongPlanEntryAsync();
        var logger = new CapturingLogger();
        var store = new Store { Logger = logger };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var warning = logger.Entries.Where(e => e.Level == LogLevel.Warning).Should().ContainSingle().Subject;
        warning.Message.Should().Contain("BILL-LONG:" + SignalWriteRefusal.SentenceTooLong).And.NotContain("xxxx");
        using var stored = JsonDocument.Parse(Target(store.Transactions.Single()[^1]).GetAttributeValue<string>("sprk_findingsjson"));
        stored.RootElement.GetProperty(InvoiceSubmissionPersister.SignalPlanRefusalsMember).GetArrayLength().Should().Be(1);
    }

    [Fact(DisplayName = "049 D-133: a refused plan entry sets sprk_adminattention in the SAME submission update as the findings, codes only")]
    public async Task Persist_WithARefusedPlanEntry_SetsAdminAttention_InTheSameUpdate()
    {
        var outcome = await OutcomeWithAnOverLongPlanEntryAsync();
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var update = Target(store.Transactions.Single()[^1]);
        update.LogicalName.Should().Be("sprk_invoicesubmission");
        update.GetAttributeValue<string>("sprk_findingsjson").Should().Contain(InvoiceSubmissionPersister.SignalPlanRefusalsMember);
        update.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().Be(
            $"1 Signal could not be created: {SignalWriteRefusal.SentenceTooLong} (BILL-LONG on line {InvoiceSubmissionPersister.LineIdFor(Submission, 0).ToString("D")[..8]})");
        update.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().NotContain("xxxx", "codes only, no extracted or authored text");
    }

    [Fact(DisplayName = "049 D-133: a plan nothing was refused from leaves sprk_adminattention out of the submission update (must-not-fire)")]
    public async Task Persist_NothingRefused_DoesNotWriteAdminAttention()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await harness.RunAsync();
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        Target(store.Transactions.Single()[^1]).Contains(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().BeFalse("never written null or empty");
    }

    // ── harness ──

    private static Entity Target(OrganizationRequest request) => request switch
    {
        CreateRequest c => c.Target,
        UpdateRequest u => u.Target,
        _ => throw new InvalidOperationException(request.GetType().Name),
    };

    private static string Describe(OrganizationRequest request) =>
        $"{request.GetType().Name.Replace("Request", string.Empty)} {Target(request).LogicalName}";

    private sealed class CapturingLogger : ILogger<InvoiceSubmissionPersister>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    // ── 050a review #1 / #2: a confirm submission's EXISTING document is checked on its live row, and never linked by the transaction ──

    /// <summary>The outcome as the job produces it for the confirm submission of <see cref="Document"/> (its id derived from the document).</summary>
    private static SubmissionEvaluationOutcome AsConfirmSubmission(SubmissionEvaluationOutcome outcome)
    {
        var id = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(Document);
        return outcome with { SubmissionId = id, Submission = outcome.Submission! with { SubmissionId = id, DocumentId = Document } };
    }

    [Fact(DisplayName = "050a review #1: a confirm submission whose document is unlinked commits WITHOUT the document link (its re-file is the link's one writer)")]
    public async Task ConfirmSubmission_UnlinkedDocument_CommitsWithoutTheDocumentLink()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, "corr-050a");

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.DocumentReads.Should().ContainSingle("the live row is read before anything is built");
        store.Transactions.Single().Select(Describe).Should().Equal(
            "Create sprk_invoice", "Create sprk_billingevent", "Create sprk_invoiceapproval", "Update sprk_document", "Update sprk_invoicesubmission");

        // Review round 2, 2(a): the document's one request is the guard - conditional on the version the check read, and NO link.
        var guard = store.Transactions.Single().OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_document");
        guard.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        guard.Target.RowVersion.Should().Be("7", "the version the check read");
        guard.Target.Id.Should().Be(Document);
        guard.Target.Attributes.Keys.Should().Equal(new[] { "sprk_invoicereviewstatus" }, "the guard writes nothing to sprk_invoice");
        guard.Target["sprk_invoicereviewstatus"].Should().BeNull("written back as read (the document has no decision yet)");
    }

    [Fact(DisplayName = "050a review round 2, 2(a): the guard writes the review status back exactly as the check read it (a value-preserving write)")]
    public async Task ConfirmSubmission_Guard_WritesTheReviewStatusBackAsRead()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            DocumentRow = new Entity("sprk_document", Document)
            {
                ["sprk_invoicereviewstatus"] = new OptionSetValue(InvoiceReviewService.ReviewStatusConfirmedInvoice),
            },
            DocumentVersion = 42,
        };

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        var guard = store.Transactions.Single().OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_document");
        guard.Target.RowVersion.Should().Be("42");
        guard.Target.GetAttributeValue<OptionSetValue>("sprk_invoicereviewstatus").Value.Should().Be(InvoiceReviewService.ReviewStatusConfirmedInvoice);
        guard.Target.Attributes.Should().NotContainKey("sprk_invoice");
    }

    /// <summary>What another writer does to the document between the persister's check and its transaction.</summary>
    public static TheoryData<string, string> ChangesBeforeTheCommit => new()
    {
        { "filed under another invoice", InvoiceSubmissionErrors.DocumentAlreadyInvoiced },
        { "rejected as not an invoice", InvoiceSubmissionPersister.DocumentRejectedCode },
        { "deleted", InvoiceSubmissionErrors.DocumentNotFound },
    };

    [Theory(DisplayName = "050a review round 2, 2(a): a filing, a reject or a delete between the document check and the commit rolls the transaction back (the guard) and is refused in the same delivery: no invoice committed")]
    [MemberData(nameof(ChangesBeforeTheCommit))]
    public async Task ConfirmSubmission_ChangeBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused(string change, string expectedCode)
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            BeforeTransaction = s =>
            {
                switch (change)
                {
                    case "filed under another invoice":
                        s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()) };
                        break;
                    case "rejected as not an invoice":
                        s.DocumentRow = new Entity("sprk_document", Document)
                        {
                            ["sprk_invoicereviewstatus"] = new OptionSetValue(InvoiceReviewService.ReviewStatusRejectedNotInvoice),
                        };
                        break;
                    default:
                        s.DocumentDeleted = true;
                        break;
                }

                s.DocumentVersion++;
            },
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks the submission Failed: no invoice exists");
        result.ReasonCode.Should().Be(expectedCode);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().ContainSingle("the one transaction was sent and rolled back by the guard");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull("nothing committed: the submission links no invoice");
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review round 2, 2(a): an unrelated write between the check and the commit rolls the transaction back too; nothing refuses the document, so it is the transaction's Retry")]
    public async Task ConfirmSubmission_UnrelatedWriteBetweenTheCheckAndTheCommit_IsRetry()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store { BeforeTransaction = s => s.DocumentVersion++ };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionFailedCode);
        result.InvoiceId.Should().BeNull();
        store.DocumentReads.Should().HaveCount(2, "the check, then the re-check after the rolled-back transaction");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
    }

    [Fact(DisplayName = "050a review #1: a confirm submission whose document is linked to ANOTHER invoice is Refused document_already_invoiced and nothing is written")]
    public async Task ConfirmSubmission_DocumentLinkedToAnotherInvoice_IsRefused_NothingWritten()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            DocumentRow = new Entity("sprk_document", Document) { ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()) },
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks the submission Failed");
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadyInvoiced).And.Be("ebilling.submission.document_already_invoiced");
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review #1: a confirm submission whose document already links THIS submission's invoice commits (negative control)")]
    public async Task ConfirmSubmission_DocumentLinkedToItsOwnInvoice_Commits()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            DocumentRow = new Entity("sprk_document", Document)
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceSubmissionPersister.InvoiceIdFor(outcome.SubmissionId)),
            },
        };

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
    }

    [Fact(DisplayName = "050a review #2: a confirm submission whose document was rejected as not an invoice is Refused document_rejected and nothing is written")]
    public async Task ConfirmSubmission_DocumentRejected_IsRefused_NothingWritten()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            DocumentRow = new Entity("sprk_document", Document)
            {
                ["sprk_invoicereviewstatus"] = new OptionSetValue(InvoiceReviewService.ReviewStatusRejectedNotInvoice),
            },
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.DocumentRejectedCode).And.Be("ebilling.submission.document_rejected");
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review #1: a confirm submission whose document is gone is Refused document_not_found and nothing is written")]
    public async Task ConfirmSubmission_DocumentGone_IsRefused_NothingWritten()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store { DocumentDeleted = true };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentNotFound);
        store.Transactions.Should().BeEmpty();
    }

    // ── 050a review round 3 (uac-r2 F1-b): the isolation rule is decided again at commit, and again after a rolled-back transaction ──

    private static readonly Guid SecureMatterS = Guid.Parse("050a0003-0000-4000-8000-0000000000a5");

    /// <summary>The world after the document joined Secure matter S's thread (through its communication, Secure-owned, no regarding column).</summary>
    private static void IsolateUnderS(Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld world)
    {
        var thread = Guid.NewGuid();
        var communication = Guid.NewGuid();
        world
            .SecureRoot("sprk_matter", SecureMatterS)
            .SecureChild("sprk_communicationthread", thread, ("sprk_regardingmatter", "sprk_matter", SecureMatterS))
            .SecureChild("sprk_communication", communication, ("sprk_communicationthread", "sprk_communicationthread", thread))
            .SecureChild("sprk_document", Document, ("sprk_relatedcommunication", "sprk_communication", communication));
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-b, must fire): a confirm submission whose document became isolated under another Secure record after the confirm is Refused document_core_record_mismatch at commit: no transaction, no invoice")]
    public async Task ConfirmSubmission_DocumentIsolatedUnderAnotherSecureRecord_IsRefused_NoInvoice()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store();
        IsolateUnderS(store.Isolation);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks the submission Failed: no invoice exists");
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-b, must fire, the re-check after a rollback): the document is filed under Secure matter S between the check and the commit; the guard rolls the transaction back and the re-check refuses it in the same delivery")]
    public async Task ConfirmSubmission_FiledUnderASecureRecordBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store
        {
            BeforeTransaction = s =>
            {
                IsolateUnderS(s.Isolation); // the filing write moves the document's row version, so the guard faults
                s.DocumentVersion++;
            },
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().ContainSingle("the one transaction was sent and rolled back by the guard");
        store.DocumentReads.Should().HaveCount(2, "the check, then the re-check after the rolled-back transaction");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull("nothing committed");
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-b): a document isolation that cannot be read at commit is a Retry (document_isolation_unreadable), nothing committed")]
    public async Task ConfirmSubmission_IsolationUnreadable_IsRetry_NothingCommitted()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store();
        IsolateUnderS(store.Isolation);
        store.Isolation.FailingQueriesOf("sprk_communication");

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceDocumentRefile.IsolationUnreadableCode);
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F3, must fire): an ambiguous Secure Record owner team (the walk's Failed) at commit is a Retry (document_isolation_unreadable): no transaction, no invoice")]
    public async Task ConfirmSubmission_AmbiguousSecureOwnerTeam_IsRetry_NothingCommitted()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store();
        IsolateUnderS(store.Isolation);
        InvoiceSubmissionExistingDocumentTests.AddAmbiguousSecureOwnerTeam(store.Isolation);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceDocumentRefile.IsolationUnreadableCode);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
    }

    [Fact(DisplayName = "050a round 3 (must not fire): a confirm submission whose document is isolated EXACTLY under the submission's own Secure matter commits")]
    public async Task ConfirmSubmission_IsolatedUnderItsOwnSecureMatter_Commits()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store();
        store.Isolation
            .SecureRoot("sprk_matter", Matter)
            .SecureChild("sprk_document", Document, ("sprk_matter", "sprk_matter", Matter));

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
    }

    // ── ebilling-r1 task 092: the upload's Stored File row in the transaction, and the flip right after the commit ──

    [Fact(DisplayName = "092: an upload reads no document and links none; the transaction holds exactly one Stored File create")]
    public async Task Upload_ReadsNoDocument_LinksNone_OneStoredFileCreate()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        outcome.Submission!.IsUpload.Should().BeTrue();
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.DocumentReads.Should().BeEmpty();
        var described = store.Transactions.Single().Select(Describe).ToList();
        described.Should().NotContain(d => d.EndsWith(" sprk_document", StringComparison.Ordinal));
        described.Count(d => d == "Create sprk_storedfile").Should().Be(1);
        store.Files.Dataverse.Rows.Keys.Should().Equal(FileId);
        store.Files.Dataverse.Writes.Should().Be(1, "the flip's one update; the row's create was the transaction's");
    }

    [Theory(DisplayName = "092: the Stored File is Firm-visible only for the SPA channel")]
    [InlineData(InvoiceSubmissionChannel.Spa, true)]
    [InlineData(InvoiceSubmissionChannel.Internal, false)]
    public async Task StoredFile_FirmVisible_ByChannel(InvoiceSubmissionChannel channel, bool firmVisible)
    {
        var outcome = await new ProcessorHarness { Channel = channel }.RunAsync();
        var store = new Store();
        store.Submission["sprk_channel"] = new OptionSetValue((int)channel);

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        var file = store.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == StoredFileSchema.Entity).Target;
        file.GetAttributeValue<bool>(StoredFileSchema.FirmVisible).Should().Be(firmVisible);
    }

    [Fact(DisplayName = "092 must-fire: no staged blob is Refused file_unservable before anything is written (no invoice)")]
    public async Task NoStagedBlob_IsRefused_BeforeAnyWrite()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { Staged = false };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.FileUnservable).And.Be("ebilling.submission.file_unservable");
        result.InvoiceId.Should().BeNull("nothing was committed");
        store.Transactions.Should().BeEmpty();
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "092 must-fire: a definitive flip failure after the commit (bytes changed) is Refused file_unservable WITH the invoice; no totals; the row stays Not yet servable")]
    public async Task DefinitiveFlipFailure_IsRefusedWithTheInvoice_NoTotals()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store
        {
            AfterCommit = s => s.Files.Gateway.Overwrite(
                s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5)),
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.FileUnservable);
        result.InvoiceId.Should().Be(InvoiceId, "the invoice is committed; the handler marks the submission Failed (the FileUnservable exception)");
        store.TotalsCalls.Should().BeEmpty("nothing runs after a definitive flip failure");
        store.FileStatus.Should().Be(StoredFileStatus.NotYetServable, "left for 090's detector");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId);
    }

    [Fact(DisplayName = "092: a flip fault after the commit throws (the job retries) before the totals; the retry is a resume with nothing created again")]
    public async Task FlipFault_Throws_BeforeTheTotals_RetryResumes()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { AfterCommit = s => s.Files.Gateway.FailEverything = new Azure.RequestFailedException(503, "storage unavailable") };

        await FluentActions.Invoking(() => store.Build().PersistAsync(outcome, null)).Should().ThrowAsync<Azure.RequestFailedException>();

        store.TotalsCalls.Should().BeEmpty();
        store.FileStatus.Should().Be(StoredFileStatus.NotYetServable);
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(InvoiceId, "the commit stands");

        store.Files.Gateway.FailEverything = null;
        var retry = await store.Build().PersistAsync(outcome, null);

        retry.Status.Should().Be(SubmissionPersistStatus.Resumed, "the after-steps' resume flips before anything else (InvoiceSubmissionAfterStepsTests)");
        store.Transactions.Should().ContainSingle("nothing is created twice");
    }

    [Fact(DisplayName = "092: a row changed under the flip is flipped once more and the commit completes")]
    public async Task FlipConcurrency_IsRetriedOnce()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store { AfterCommit = s => s.Files.Dataverse.BumpVersionBeforeNextUpdate = true };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.FileStatus.Should().Be(StoredFileStatus.Servable);
        store.FileStatusAtTotals.Should().Equal([StoredFileStatus.Servable]);
    }

    [Fact(DisplayName = "092 must-fire: an outcome naming a document that is not its 050a confirm is Refused document_unavailable, nothing read or written")]
    public async Task DocumentThatIsNotAConfirm_IsRefused_NothingWritten()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        outcome = outcome with { Submission = outcome.Submission! with { DocumentId = Document } };
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.DocumentUnavailable);
        store.DocumentReads.Should().BeEmpty();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
    }

    [Fact(DisplayName = "092 must-not-fire: a confirm submission (050a) builds no Stored File row and reads no staged blob")]
    public async Task ConfirmSubmission_HasNoStoredFile()
    {
        var outcome = AsConfirmSubmission(await new ProcessorHarness().RunAsync());
        var store = new Store { Staged = false };

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        store.Transactions.Single().Select(Describe).Should().NotContain("Create sprk_storedfile");
        store.Files.Gateway.StorageCalls.Should().Be(0);
    }

    private sealed class Store
    {
        public Entity Submission { get; } = new("sprk_invoicesubmission", InvoiceSubmissionPersisterTests.Submission)
        {
            ["sprk_trackingnumber"] = "INV-100.2",
            ["sprk_submissionsequence"] = 2,
            ["sprk_firminvoicenumber"] = "INV-100",
            ["sprk_vendororg"] = new EntityReference("sprk_organization", Firm),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
            ["createdon"] = new DateTime(2026, 6, 5, 23, 30, 0, DateTimeKind.Utc),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Spa),
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Processing),
            ["sprk_createdbyperson"] = new EntityReference("systemuser", Submitter),
        };

        public string MatterNumber { get; init; } = "M-1";

        public List<Entity> StoredLines { get; } = [];
        public List<IReadOnlyList<OrganizationRequest>> Transactions { get; } = [];
        public List<Guid> TotalsCalls { get; } = [];
        public List<RecordOwnershipContext> OwnerContexts { get; } = [];
        public Exception? TransactionFault { get; init; }
        public bool CommitDespiteFault { get; init; }
        public Exception? TotalsFault { get; set; }
        public bool SubmissionDeleted { get; init; }
        public RecordOwnerResolution Owner { get; init; } = RecordOwnerResolution.Owned(TeamId);

        /// <summary>050a review #1 / #2: the document's live row as a confirm submission's check reads it (null: unlinked, not rejected).</summary>
        public Entity? DocumentRow { get; set; }

        /// <summary>050a review #1: the document no longer exists.</summary>
        public bool DocumentDeleted { get; set; }

        /// <summary>050a review round 2, 2(a): the document's <c>versionnumber</c> as Dataverse keeps it (every read answers it).</summary>
        public long DocumentVersion { get; set; } = 7;

        /// <summary>
        /// 050a review round 3 (uac-r2 F1-b): the isolation facts the persister's existing-document check reads (the REAL synchronizer walk).
        /// By default a world with a Secure Record owner team and no rows: the document reads as not isolated.
        /// </summary>
        public Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld Isolation { get; set; } =
            Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard();

        /// <summary>050a review round 2, 2(a): another writer's slot between the persister's document check and its transaction.</summary>
        public Action<Store>? BeforeTransaction { get; init; }

        /// <summary>Every read of the document row (columns).</summary>
        public List<string[]> DocumentReads { get; } = [];
        public ILogger<InvoiceSubmissionPersister> Logger { get; init; } = NullLogger<InvoiceSubmissionPersister>.Instance;

        /// <summary>The column set of every submission re-read.</summary>
        public List<string[]> SubmissionReadColumns { get; } = [];

        /// <summary>Task 092: the Finance file store (real service, in-memory blob gateway and Dataverse).</summary>
        public StoredFileRig Files { get; } = new();

        /// <summary>Task 092: false, the intake's staged blob is not there (nothing was staged, or it was removed).</summary>
        public bool Staged { get; init; } = true;

        /// <summary>The blob staged for the upload of <see cref="InvoiceSubmissionPersisterTests.Submission"/> (null until <see cref="Build"/>).</summary>
        public StagedBlob? StagedBlob { get; private set; }

        /// <summary>The Stored File's status each time the totals ran (the flip must already have run).</summary>
        public List<StoredFileStatus?> FileStatusAtTotals { get; } = [];

        /// <summary>The Stored File's status now (null: no row).</summary>
        public StoredFileStatus? FileStatus => Files.Dataverse.Rows.TryGetValue(FileId, out var row)
            ? (StoredFileStatus)row.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value
            : null;

        /// <summary>Runs after the transaction committed (before the flip): another writer's slot.</summary>
        public Action<Store>? AfterCommit { get; init; }

        private void StageOnce()
        {
            if (!Staged || StagedBlob is not null)
            {
                return;
            }

            var spec = InvoiceSubmissionService.UploadFileSpec(InvoiceSubmissionPersisterTests.Submission, "invoice.pdf", InvoiceSubmissionChannel.Spa);
            StagedBlob = Files.Service.StageAsync(spec, new MemoryStream("%PDF-1.7 persister test"u8.ToArray()), CancellationToken.None)
                .GetAwaiter().GetResult().Blob!;
        }

        public InvoiceSubmissionPersister Build()
        {
            StageOnce();
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] columns, CancellationToken _) => entity switch
                {
                    "sprk_invoicesubmission" when SubmissionDeleted => throw new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"),
                    "sprk_invoicesubmission" => Capture(columns, Copy(Submission)),
                    "sprk_document" when DocumentDeleted => throw new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"),
                    "sprk_document" => ReadDocument(id, columns),
                    _ => new Entity(entity, id) { [RegardingRecordType.GetPrimaryNameField(entity)!] = "Acme v. Roe", ["sprk_matternumber"] = MatterNumber },
                });
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new EntityCollection(StoredLines));
            entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    Transactions.Add(requests);
                    BeforeTransaction?.Invoke(this);

                    // Dataverse's optimistic concurrency inside the transaction: a conditional update of the document at a version the row
                    // no longer has (or of a row that is gone) faults that request, and the whole transaction rolls back (MapTransactionFailure).
                    for (var index = 0; index < requests.Count; index++)
                    {
                        if (requests[index] is UpdateRequest { ConcurrencyBehavior: ConcurrencyBehavior.IfRowVersionMatches } guard
                            && guard.Target.LogicalName == "sprk_document"
                            && (DocumentDeleted || guard.Target.RowVersion != DocumentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        {
                            return Task.FromException(new DataverseTransactionException(
                                "ConcurrencyVersionMismatch", failedRequestIndex: index, outcomeUnknown: false, null));
                        }
                    }

                    if (TransactionFault is null || CommitDespiteFault)
                    {
                        Apply(requests);
                        AfterCommit?.Invoke(this);
                    }

                    return TransactionFault is null ? Task.CompletedTask : Task.FromException(TransactionFault);
                });

            var recordTypes = new Mock<ICommunicationDataverseService>();
            recordTypes.Setup(r => r.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", RecordTypeId) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" });

            var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
            ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .Callback((RecordOwnershipContext context, CancellationToken _) => OwnerContexts.Add(context))
                .ReturnsAsync(() => Owner);

            var totals = new Mock<IInvoiceTotalsService>(MockBehavior.Strict);
            totals.Setup(t => t.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid invoiceId, CancellationToken _) =>
                {
                    TotalsCalls.Add(invoiceId);
                    FileStatusAtTotals.Add(FileStatus);
                    return TotalsFault is { } fault
                        ? Task.FromException<InvoiceTotalsCalculation>(fault)
                        : Task.FromResult(new InvoiceTotalsCalculation(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), null));
                });

            return new InvoiceSubmissionPersister(
                entities.Object,
                recordTypes.Object,
                ownership.Object,
                new InvoiceLifecycleService(new Mock<IFieldMappingDataverseService>().Object, NullLogger<InvoiceLifecycleService>.Instance),
                totals.Object,
                InvoiceSubmissionServiceTests.IsolationOver(() => Isolation),
                Files.Service,
                Logger);
        }

        /// <summary>
        /// The committed transaction's effect on the submission row (the only row a later read looks at) and, task 092, on the file store's
        /// Dataverse: the Stored File create lands there (one Dataverse in production), so the flip finds its row.
        /// </summary>
        private void Apply(IReadOnlyList<OrganizationRequest> requests)
        {
            foreach (var create in requests.OfType<CreateRequest>().Where(c => c.Target.LogicalName == StoredFileSchema.Entity))
            {
                Files.Dataverse.AddRow(Copy(create.Target));
            }

            foreach (var update in requests.OfType<UpdateRequest>().Where(u => u.Target.LogicalName == "sprk_invoicesubmission"))
            {
                foreach (var (column, value) in update.Target.Attributes)
                {
                    Submission[column] = value;
                }
            }
        }

        private Entity ReadDocument(Guid id, string[] columns)
        {
            DocumentReads.Add(columns);
            var row = DocumentRow is null ? new Entity("sprk_document", id) : Copy(DocumentRow);
            row["versionnumber"] = DocumentVersion;
            return row;
        }

        private Entity Capture(string[] columns, Entity row)
        {
            SubmissionReadColumns.Add(columns);
            return row;
        }

        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var (column, value) in row.Attributes)
            {
                copy[column] = value;
            }

            return copy;
        }
    }
}
