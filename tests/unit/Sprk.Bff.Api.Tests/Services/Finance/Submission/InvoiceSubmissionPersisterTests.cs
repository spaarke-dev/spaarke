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

    // ── Task 094 (model 9.1 steps 3-5): a Submit Invoice SOURCE document is checked on its live row, COPIED (never linked, re-owned or
    // re-shared), guarded at the version its copy was staged from, and checked again after the commit by the commit gate ──

    private static readonly Guid SourceSubmission = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(Document);
    private static readonly Guid SourceInvoice = InvoiceSubmissionPersister.InvoiceIdFor(SourceSubmission);
    private static readonly Guid OtherMatter = Guid.Parse("09400000-0000-4000-8000-0000000000a9");

    /// <summary>
    /// The outcome as the job produces it for a Submit Invoice of <see cref="Document"/>: the source-document kind (its id derived from the
    /// document) and the copy the job staged at the document's current version (<see cref="Store.StageSourceCopy"/>).
    /// </summary>
    private static SubmissionEvaluationOutcome AsSource(SubmissionEvaluationOutcome outcome, Store store) => outcome with
    {
        SubmissionId = SourceSubmission,
        Submission = outcome.Submission! with { SubmissionId = SourceSubmission, DocumentId = Document, FileSource = SubmissionFileSource.SourceDocument },
        StagedFiles = [store.StageSourceCopy()],
    };

    [Fact(DisplayName = "094 fix round F1-1 (must fire; restores 092's rule): an outcome naming a source document its submission was not created for is Refused document_unavailable - nothing read, owned or written")]
    public async Task Source_DocumentItsSubmissionWasNotCreatedFor_IsRefused_NothingWritten()
    {
        var store = new Store();
        var outcome = await new ProcessorHarness().RunAsync();
        outcome = outcome with
        {
            Submission = outcome.Submission! with { DocumentId = Document, FileSource = SubmissionFileSource.SourceDocument },
            StagedFiles = [store.StageSourceCopy()],
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.DocumentUnavailable);
        store.DocumentReads.Should().BeEmpty();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 happy path: a source submission commits invoice, the copy's Stored File row, lines, approval, the guard and the submission in ONE transaction; the copy ends Servable; nothing links the source")]
    public async Task Source_CommitsTheCopy_ServableAfterTheGate_NothingLinksTheSource()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, "corr-094");

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.Transactions.Single().Select(Describe).Should().Equal(
            "Create sprk_invoice", "Create sprk_storedfile", "Create sprk_billingevent", "Create sprk_invoiceapproval", "Update sprk_document",
            "Update sprk_invoicesubmission");
        // Step 3 reads the live row (version, link, review status) and runs the source check (its filing chain) before anything is built;
        // step 5 (the gate) runs the source check again after the commit.
        store.DocumentReads.Count(c => c.Contains("versionnumber")).Should().Be(1, "the version is read once, at step 3");
        store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter")).Should().Be(2, "the source check runs at step 3 and again at step 5");

        var file = store.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == StoredFileSchema.Entity).Target;
        file.Id.Should().Be(StoredFileIds.ForSourceVersion(SourceSubmission, 7));
        file.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose).Value.Should().Be((int)StoredFilePurpose.InvoicePdf);
        file.GetAttributeValue<bool>(StoredFileSchema.FirmVisible).Should().BeFalse("owner K: a copy is never firm-visible");
        file.GetAttributeValue<EntityReference>(StoredFileSchema.SourceDocument).Id.Should().Be(Document);
        file.GetAttributeValue<string>(StoredFileSchema.SourceVersion).Should().Be("7");
        file.GetAttributeValue<string>(StoredFileSchema.SourceSha256).Should().Be(store.SourceCopy!.Spec.SourceSha256);
        store.CopyStatus.Should().Be(StoredFileStatus.Servable, "the gate flipped it after the re-run passed");
        store.FileStatusAtTotals.Should().ContainSingle();

        // The guard is the source's ONE request: conditional on the staged version, and NO link, owner or share write.
        var guard = store.Transactions.Single().OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_document");
        guard.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        guard.Target.RowVersion.Should().Be("7", "the version the copy was staged from");
        guard.Target.Id.Should().Be(Document);
        guard.Target.Attributes.Keys.Should().Equal(new[] { "sprk_invoicereviewstatus" }, "the guard writes nothing to sprk_invoice, the owner or the shares");
        guard.Target["sprk_invoicereviewstatus"].Should().BeNull("written back as read (the document has no decision yet)");
        store.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == "sprk_invoice").Target.Attributes
            .Should().NotContainKey(InvoiceSubmissionPersister.InvoiceDocument, "the invoice no longer links a document");
        store.Isolation.OwnerWrites.Should().BeEmpty("the source is never re-owned");
    }

    [Fact(DisplayName = "094: the guard writes the review status back exactly as the check read it (a value-preserving write) at the staged version")]
    public async Task Source_Guard_WritesTheReviewStatusBackAsRead()
    {
        var store = new Store
        {
            DocumentRow = new Entity("sprk_document", Document)
            {
                ["sprk_invoicereviewstatus"] = new OptionSetValue(InvoiceReviewService.ReviewStatusConfirmedInvoice),
            },
            DocumentVersion = 42,
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

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

    [Theory(DisplayName = "094 step 4: a filing, a reject or a delete between the check and the commit rolls the transaction back (the guard) and is refused in the same delivery: no invoice, the staged copy deleted")]
    [MemberData(nameof(ChangesBeforeTheCommit))]
    public async Task Source_ChangeBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused(string change, string expectedCode)
    {
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
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks the submission Failed: no invoice exists");
        result.ReasonCode.Should().Be(expectedCode);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().ContainSingle("the one transaction was sent and rolled back by the guard");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull("nothing committed: the submission links no invoice");
        store.TotalsCalls.Should().BeEmpty();
        store.CopyBlobExists.Should().BeFalse("a staged copy no commit wrote a row for is deleted");
    }

    [Fact(DisplayName = "094 step 4 (must fire): a source write between staging and the commit faults the guard: SourceChanged (the job re-stages under the new version), nothing committed, the staged copy deleted")]
    public async Task Source_UnrelatedWriteBetweenTheCheckAndTheCommit_IsSourceChanged()
    {
        var store = new Store { BeforeTransaction = s => s.DocumentVersion++ };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.SourceChanged);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.SourceChanged);
        result.InvoiceId.Should().BeNull();
        store.DocumentReads.Count(c => c.Contains("versionnumber")).Should().Be(2, "the check, then the re-check after the rolled-back transaction");
        store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter")).Should().Be(2, "each runs the source check");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
        store.CopyBlobExists.Should().BeFalse();
    }

    [Fact(DisplayName = "094 step 3 (must fire): a source whose live version is not the staged one is SourceChanged before anything is built; no transaction")]
    public async Task Source_LiveVersionIsNotTheStagedOne_IsSourceChanged_NoTransaction()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        store.DocumentVersion = 8; // a write after the job staged its copy at 7

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.SourceChanged);
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.CopyBlobExists.Should().BeFalse();
    }

    [Fact(DisplayName = "094 step 4 (must fire): a transaction fault that is not the guard is the transaction's Retry, and the staged copy is deleted")]
    public async Task Source_OtherTransactionFault_IsRetry_StagedCopyDeleted()
    {
        var store = new Store { TransactionFault = new DataverseTransactionException("boom", failedRequestIndex: 0, outcomeUnknown: false, null) };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionFailedCode);
        store.CopyBlobExists.Should().BeFalse("a transaction failure deletes the staged blob");
    }

    [Fact(DisplayName = "094 step 4 (must not fire): an outcome-unknown transaction that did not link keeps the staged copy (the commit may yet be proved)")]
    public async Task Source_OutcomeUnknown_NotLinked_KeepsTheStagedCopy()
    {
        var store = new Store { TransactionFault = new DataverseTransactionException("timeout", failedRequestIndex: -1, outcomeUnknown: true, null) };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionOutcomeUnknownCode);
        store.CopyBlobExists.Should().BeTrue();
    }

    [Fact(DisplayName = "094 step 5 (must fire): the source re-filed to another core record between the commit and the re-run is undone - Stored File blob and row, approval, lines, invoice deleted in that order; the four columns cleared; Failed with the note; no totals")]
    public async Task Source_RefiledAfterTheCommit_IsUndone()
    {
        var store = new Store
        {
            AfterCommit = s => s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) },
        };
        store.Submission[InvoiceSubmissionPersister.SubmissionCorrelationId] = "corr-intake";
        store.Submission["sprk_ponumber"] = "PO-9";
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.SourceCheckFailed);
        result.UndoHeld.Should().BeFalse();
        result.InvoiceId.Should().Be(SourceInvoice);
        store.TotalsCalls.Should().BeEmpty("nothing runs after a failed step 5");
        ShouldBeUndone(store, SubmissionReasonCodes.SourceCheckFailed, store.SourceCopy!.Spec.FileId);
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionCorrelationId).Should().Be("corr-intake", "the correlation is kept");
    }

    [Fact(DisplayName = "094 step 5 (must not fire): the source still filed under the core record only passes the re-run, so nothing is undone")]
    public async Task Source_StillFiledHere_IsNotUndone()
    {
        var store = new Store
        {
            AfterCommit = s => s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) },
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        store.Deletes.Should().BeEmpty();
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
    }

    /// <summary>
    /// Task 094: the undo's end state - every row of the commit gone in order, the four columns cleared, Failed with the code and note. Fix round
    /// (undo order): the approval first, then the lines, then the file blobs, then the Stored File rows AND the invoice in one transaction - so no
    /// state of the undo leaves the invoice without its file row (the external visibility rule reads an invoice with no source file as visible).
    /// </summary>
    private static void ShouldBeUndone(Store store, string reasonCode, Guid fileId)
    {
        store.Files.Dataverse.Rows.Should().NotContainKey(fileId, "the Stored File row is deleted");
        store.InvoiceExists.Should().BeFalse();
        store.CopyBlobExists.Should().BeFalse("the file's blob is deleted before its row");
        store.Deletes.Select(d => d.Table).Should().StartWith(["sprk_invoiceapproval"]).And.EndWith("sprk_invoice",
            "the approval first (its invoice lookup is required), then the lines, then the file rows and the invoice");
        store.Deletes.Where(d => d.Table == "sprk_billingevent").Should().NotBeEmpty();
        store.Deletes.Where(d => d.Table is "sprk_invoiceapproval" or "sprk_billingevent").Should().OnlyContain(d => d.FilesLeft > 0,
            "the file row stays until the transaction that deletes the invoice");
        var undo = store.UndoTransactions.Should().ContainSingle("the file rows and the invoice go in one transaction").Subject;
        undo.Select(d => d.Target.LogicalName).Should().Equal(StoredFileSchema.Entity, "sprk_invoice");
        undo[0].Target.Id.Should().Be(fileId);
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)InvoiceSubmissionStatus.Failed);
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode).Should().Be(reasonCode);
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionInvoice);
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionFindingsJson);
        store.Submission.Attributes.Should().NotContainKey("sprk_firminvoicenumber");
        store.Submission.Attributes.Should().NotContainKey("sprk_ponumber");
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().Contain(AdminAttentionText.UndoneNote(reasonCode));
        // Step A's filing is kept: the undo deletes only the commit's rows (the Dataverse here is a strict mock, so any write to the
        // communication or the source document would throw), and the note says the filing stays.
        store.Deletes.Select(d => d.Table).Should().NotContain(["sprk_communication", "sprk_document"]);
        AdminAttentionText.UndoneNote(reasonCode).Should().Contain("a filing of its source communication made at submission is kept");
    }

    [Fact(DisplayName = "094 copy rule end to end: the copy a Submit Invoice committed is served on 090's download route to a caller with Read on the invoice and the source; once that caller loses Read on the source document it is the uniform 404")]
    public async Task CopyRule_EndToEnd_LosingReadOnTheSource_RefusesTheCopy()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, "corr-094")).Status.Should().Be(SubmissionPersistStatus.Committed);
        var copy = store.SourceCopy!;
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
        // The malware scan's verdict on the staged blob (Defender tags it after the write).
        store.Files.Gateway.Get(copy.Blob.Container, copy.Blob.BlobPath)!.Tags = StoredFileRig.CleanTags(store.Files.Gateway.Now.AddMinutes(1));
        await using var host = await Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host.StartAsync(rig: store.Files);
        store.Files.Probe.Grant("sprk_invoices", SourceInvoice, AccessRights.Read);
        store.Files.Probe.Grant("sprk_documents", Document, AccessRights.Read);
        var url = $"/api/finance/files/{copy.Spec.FileId}/content";

        var served = await host.SendAsync(url);

        served.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await served.Content.ReadAsStringAsync());
        (await served.Content.ReadAsStringAsync()).Should().Be("%PDF-1.7 source copy");

        // The caller loses Read on the source document (re-filed, unshared): the copy follows the source, never the invoice alone.
        store.Files.Probe.Grant("sprk_documents", Document, AccessRights.None);
        var storageCalls = store.Files.Gateway.StorageCalls;

        var refused = await host.SendAsync(url);

        refused.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        store.Files.Gateway.StorageCalls.Should().Be(storageCalls, "refused before any storage read");
        store.Files.Probe.Calls.Should().Contain(c => c.Set == "sprk_documents" && c.Id == Document && c.Token == "caller-token");
    }

    // ── 094: the commit gate itself (model 9.1 steps 5-6; uac-r2 conditions 1 and 3), over the same Dataverse, file store and isolation ──

    [Fact(DisplayName = "094 step 5 (uac-r2 condition 1, must fire): a re-run that cannot be made (the copy's roots unreadable) is a failure - undone source_check_failed, never a pass or a retry")]
    public async Task Gate_ReRunCannotBeMade_IsUndone()
    {
        var store = new Store { AfterCommit = s => s.Isolation.FailingQueriesOf("sprk_invoice") };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.SourceCheckFailed);
        store.TotalsCalls.Should().BeEmpty();
        ShouldBeUndone(store, SubmissionReasonCodes.SourceCheckFailed, store.SourceCopy!.Spec.FileId);
    }

    [Fact(DisplayName = "094 step 5 (must fire): an invoice whose file row is gone after the commit is undone file_unservable (none is never a pass)")]
    public async Task Gate_NoFile_IsUndone_FileUnservable()
    {
        var store = new Store { AfterCommit = s => s.Files.Dataverse.Rows.TryRemove(s.SourceCopy!.Spec.FileId, out _) };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.FileUnservable);
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)InvoiceSubmissionStatus.Failed);
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionInvoice);
        // Known limit (recorded in the 094 notes): the undo finds files by their rows, so a blob whose row another writer deleted is left
        // for the orphan sweep (StoredFileReconcileJob, disabled). It is not servable: nothing can reach a blob without its row.
        store.CopyBlobExists.Should().BeTrue();
    }

    [Fact(DisplayName = "094 step 6 (must not fire): once a file is Servable the re-run is skipped - a resume after the source moved neither re-checks nor undoes")]
    public async Task Gate_AFileAlreadyServable_SkipsTheReRun()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var checks = store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter"));
        store.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) };

        var again = await store.Gate!.RunAsync(SourceSubmission, CancellationToken.None);

        again.Should().Be(SubmissionCommitGateResult.Passed);
        store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter")).Should().Be(checks, "step 5 ran and flipped in the earlier run; never twice");
        store.Deletes.Should().BeEmpty();
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "094 (uac-r2 condition 3): the compensation is idempotent - a second run after a complete undo deletes nothing, writes nothing and answers complete")]
    public async Task Gate_Compensation_IsIdempotent()
    {
        var store = new Store
        {
            AfterCommit = s => s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) },
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Undone);
        var deletes = store.Deletes.Count;
        var attention = store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);

        (await store.Gate!.CompensateAsync(SourceSubmission, CancellationToken.None)).Should().BeTrue();

        store.Deletes.Should().HaveCount(deletes);
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention).Should().Be(attention);
        ShouldBeUndone(store, SubmissionReasonCodes.SourceCheckFailed, store.SourceCopy!.Spec.FileId);
    }

    [Fact(DisplayName = "094 (must not fire): the compensation of a submission that never committed (no link, no findings, no row) clears nothing - its firm invoice number is kept")]
    public async Task Gate_Compensation_WithoutACommit_ClearsNothing()
    {
        var store = new Store();
        store.Build();
        store.Submission["sprk_firminvoicenumber"] = "INV-100";
        store.Submission["sprk_ponumber"] = "PO-1";

        (await store.Gate!.CompensateAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None)).Should().BeTrue();

        store.Deletes.Should().BeEmpty();
        store.Submission.GetAttributeValue<string>("sprk_firminvoicenumber").Should().Be("INV-100");
        store.Submission.GetAttributeValue<string>("sprk_ponumber").Should().Be("PO-1");
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

    [Fact(DisplayName = "094 step 3 (uac-r2 F1-b, must fire): a source isolated under another Secure record is Refused document_core_record_mismatch at commit: no transaction, no invoice")]
    public async Task Source_DocumentIsolatedUnderAnotherSecureRecord_IsRefused_NoInvoice()
    {
        var store = new Store();
        IsolateUnderS(store.Isolation);
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks the submission Failed: no invoice exists");
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.OwnerContexts.Should().BeEmpty();
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
    }

    [Fact(DisplayName = "094 step 4 (uac-r2 F1-b, must fire, the re-check after a rollback): the source is filed under Secure matter S between the check and the commit; the guard rolls back and the re-check refuses it in the same delivery")]
    public async Task Source_FiledUnderASecureRecordBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused()
    {
        var store = new Store
        {
            BeforeTransaction = s =>
            {
                IsolateUnderS(s.Isolation); // the filing write moves the document's row version, so the guard faults
                s.DocumentVersion++;
            },
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().ContainSingle("the one transaction was sent and rolled back by the guard");
        store.DocumentReads.Count(c => c.Contains("versionnumber")).Should().Be(2, "the check, then the re-check after the rolled-back transaction");
        store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter")).Should().Be(2, "each runs the source check");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull("nothing committed");
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 3 (uac-r2 F1-b): a source isolation that cannot be read at commit is a Retry (document_isolation_unreadable), nothing committed")]
    public async Task Source_IsolationUnreadable_IsRetry_NothingCommitted()
    {
        var store = new Store();
        IsolateUnderS(store.Isolation);
        store.Isolation.FailingQueriesOf("sprk_communication");
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.IsolationUnreadableCode);
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "094 step 3 (uac-r2 F3, must fire): an ambiguous Secure Record owner team (the walk's Failed) at commit is a Retry (document_isolation_unreadable): no transaction, no invoice")]
    public async Task Source_AmbiguousSecureOwnerTeam_IsRetry_NothingCommitted()
    {
        var store = new Store();
        IsolateUnderS(store.Isolation);
        InvoiceSubmissionExistingDocumentTests.AddAmbiguousSecureOwnerTeam(store.Isolation);
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.IsolationUnreadableCode);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().BeEmpty();
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
    }

    [Fact(DisplayName = "094 (must not fire): a source isolated EXACTLY under the submission's own Secure matter commits, and its copy (under the invoice there) passes the re-run")]
    public async Task Source_IsolatedUnderItsOwnSecureMatter_Commits()
    {
        var store = new Store();
        store.Isolation
            .SecureRoot("sprk_matter", Matter)
            .SecureChild("sprk_document", Document, ("sprk_matter", "sprk_matter", Matter));
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "094 step 5 (must fire): the source's secure roots not covered by the copy's (the invoice moved out from under Secure matter M after the commit) is undone source_check_failed")]
    public async Task Gate_SourceRootsNotCoveredByTheCopy_IsUndone()
    {
        var store = new Store
        {
            // The source stays filed under M (the filing check passes); the copy's roots (its invoice's, until 086) no longer include M.
            AfterCommit = s => s.Isolation
                .OrdinaryRoot("sprk_matter", OtherMatter)
                .OrdinaryChild("sprk_invoice", SourceInvoice, ("sprk_matter", "sprk_matter", OtherMatter)),
        };
        store.Isolation
            .SecureRoot("sprk_matter", Matter)
            .SecureChild("sprk_document", Document, ("sprk_matter", "sprk_matter", Matter));
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.SourceCheckFailed);
        store.TotalsCalls.Should().BeEmpty();
        ShouldBeUndone(store, SubmissionReasonCodes.SourceCheckFailed, store.SourceCopy!.Spec.FileId);
    }

    [Fact(DisplayName = "094 (caller errors): a source outcome with no staged copy, or a copy of another document, or a file source that disagrees with the document, is thrown before anything is read")]
    public async Task Source_InconsistentOutcome_Throws()
    {
        var store = new Store();
        var accepted = await new ProcessorHarness().RunAsync();
        var noCopy = AsSource(accepted, store) with { StagedFiles = [] };
        var otherCopy = AsSource(accepted, store);
        otherCopy = otherCopy with { StagedFiles = [otherCopy.StagedFiles[0] with { Spec = otherCopy.StagedFiles[0].Spec with { SourceDocumentId = Guid.NewGuid() } }] };
        var uploadNamingADocument = accepted with { Submission = accepted.Submission! with { DocumentId = Document } };
        var sut = store.Build();

        foreach (var outcome in new[] { noCopy, otherCopy, uploadNamingADocument })
        {
            await FluentActions.Invoking(() => sut.PersistAsync(outcome, null)).Should().ThrowAsync<ArgumentException>();
        }

        store.DocumentReads.Should().BeEmpty();
        store.Transactions.Should().BeEmpty();
    }

    // ── ebilling-r1 tasks 092 / 094: the upload's Stored File row in the transaction, and the commit gate right after the commit ──

    [Fact(DisplayName = "092: an upload reads no document and links none; the transaction holds exactly one Stored File create")]
    public async Task Upload_ReadsNoDocument_LinksNone_OneStoredFileCreate()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        outcome.Submission!.IsUpload.Should().BeTrue();
        var store = new Store();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.DocumentReads.Should().BeEmpty("an upload has no source: the gate runs no re-run");
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

    [Fact(DisplayName = "094 (092 review item 2, must fire): a definitive flip failure after an upload's commit (bytes changed) is UNDONE - blob and row, approval, lines, invoice deleted; the four columns cleared; Failed; no totals")]
    public async Task Upload_DefinitiveFlipFailure_IsUndone_NoTotals()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store
        {
            AfterCommit = s => s.Files.Gateway.Overwrite(
                s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5)),
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.FileUnservable);
        result.InvoiceId.Should().Be(InvoiceId);
        store.TotalsCalls.Should().BeEmpty("nothing runs after a definitive flip failure");
        store.Files.Gateway.Get(store.StagedBlob!.Container, store.StagedBlob.BlobPath).Should().BeNull("the file's blob is deleted");
        ShouldBeUndone(store, SubmissionReasonCodes.FileUnservable, FileId);
    }

    [Fact(DisplayName = "094 undo held (must fire): a delete fault in the undo leaves the submission Failed and the Stored File Not yet servable; a later compensation completes it")]
    public async Task Upload_UndoHeld_ThenCompensationCompletes()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store
        {
            AfterCommit = s =>
            {
                s.Files.Gateway.Overwrite(s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5));
                s.Files.Gateway.FailOp["delete"] = new Azure.RequestFailedException(503, "storage unavailable");
            },
        };
        var sut = store.Build();

        var result = await sut.PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeTrue();
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)InvoiceSubmissionStatus.Failed);
        store.FileStatus.Should().Be(StoredFileStatus.Removed, "held after the withdraw (uac-r2 (b)): the file is never served");
        store.Submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice).Should().NotBeNull("held: the columns stay until the rows are gone");

        store.Files.Gateway.FailOp.Clear();
        (await store.Gate!.CompensateAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None)).Should().BeTrue();

        ShouldBeUndone(store, SubmissionReasonCodes.FileUnservable, FileId);
    }

    // ── Fix round (undo order, F2-a): no partial undo leaves an invoice without its file row; "removed" is written only when the undo completed ──

    [Fact(DisplayName = "094 undo (must fire): a fault in the transaction that deletes the file rows and the invoice leaves both, the note says the removal is pending (never removed); a later compensation completes it")]
    public async Task Undo_FaultInTheRowsAndInvoiceTransaction_LeavesTheInvoiceWithItsFileRow_ThenCompletes()
    {
        var store = new Store
        {
            AfterCommit = s =>
            {
                s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) };
                s.UndoTransactionFault = new DataverseTransactionException("ServiceUnavailable", failedRequestIndex: 1, outcomeUnknown: false, null);
            },
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeTrue();
        store.UndoTransactions.Should().ContainSingle("the transaction was sent and rolled back");
        store.InvoiceExists.Should().BeTrue("the transaction rolled back: the invoice stays");
        store.CopyStatus.Should().Be(StoredFileStatus.Removed, "and so does its file row, withdrawn before its blob went (uac-r2 (b)): never served");
        store.Deletes.Select(d => d.Table).Should().NotContain(["sprk_invoice", StoredFileSchema.Entity]);
        var note = store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
        note.Should().Contain(AdminAttentionText.UndoPendingNote(SubmissionReasonCodes.SourceCheckFailed));
        note.Should().NotContain("were removed", "nothing is reported removed before the undo completed");
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)InvoiceSubmissionStatus.Failed);

        (await store.Gate!.CompensateAsync(SourceSubmission, CancellationToken.None)).Should().BeTrue();

        store.UndoTransactions.Should().HaveCount(2);
        store.Files.Dataverse.Rows.Should().NotContainKey(store.SourceCopy!.Spec.FileId);
        store.InvoiceExists.Should().BeFalse();
        var completed = store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention);
        completed.Should().Contain(AdminAttentionText.UndoneNote(SubmissionReasonCodes.SourceCheckFailed));
        completed.Should().NotContain(AdminAttentionText.UndoPendingNote(SubmissionReasonCodes.SourceCheckFailed), "the one undone slot now says removed");
    }

    [Theory(DisplayName = "094 undo (must fire): a fault before the file step (approval or lines) leaves the invoice AND its file row; the note says pending")]
    [InlineData("sprk_invoiceapproval")]
    [InlineData("sprk_billingevent")]
    public async Task Undo_FaultBeforeTheFileStep_LeavesTheInvoiceWithItsFileRow(string faultingTable)
    {
        var store = new Store
        {
            AfterCommit = s =>
            {
                s.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) };
                s.DeleteFault = faultingTable;
            },
        };
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeTrue();
        store.UndoTransactions.Should().BeEmpty("the undo stopped before the file step");
        store.InvoiceExists.Should().BeTrue();
        store.CopyStatus.Should().Be(StoredFileStatus.NotYetServable);
        store.CopyBlobExists.Should().BeTrue("the file step never ran");
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention)
            .Should().Contain(AdminAttentionText.UndoPendingNote(SubmissionReasonCodes.SourceCheckFailed));
    }

    // ── uac-r2 re-check of d7ec16cb97: (b) rows withdrawn before their blobs go; K2 more rows than one transaction holds with nothing deleted ──

    [Fact(DisplayName = "094 undo (uac-r2 (b), must fire): an undo of a COMMITTED, Servable copy held after its blob was deleted leaves the row Removed - the download route answers the uniform 404, never a broken download; a later compensation completes it")]
    public async Task Undo_HeldAfterTheBlobDelete_LeavesTheServableRowWithdrawn_NeverServed()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, "corr-094")).Status.Should().Be(SubmissionPersistStatus.Committed);
        var copy = store.SourceCopy!;
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
        store.UndoTransactionFault = new DataverseTransactionException("ServiceUnavailable", failedRequestIndex: 1, outcomeUnknown: false, null);

        var result = await store.Gate!.UndoAsync(SourceSubmission, SubmissionReasonCodes.SourceCheckFailed, CancellationToken.None);

        result.Status.Should().Be(SubmissionCommitGateStatus.UndoHeld);
        store.CopyBlobExists.Should().BeFalse("the blob went before the transaction faulted");
        store.InvoiceExists.Should().BeTrue("the transaction rolled back");
        store.CopyStatus.Should().Be(StoredFileStatus.Removed, "withdrawn before its blob was deleted");
        await using (var host = await Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host.StartAsync(rig: store.Files))
        {
            store.Files.Probe.Grant("sprk_invoices", SourceInvoice, AccessRights.Read);
            store.Files.Probe.Grant("sprk_documents", Document, AccessRights.Read);
            var storageCalls = store.Files.Gateway.StorageCalls;

            var served = await host.SendAsync($"/api/finance/files/{copy.Spec.FileId}/content");

            served.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound, "a withdrawn row is never served");
            store.Files.Gateway.StorageCalls.Should().Be(storageCalls, "refused on the row, before any storage read");
        }

        (await store.Gate!.CompensateAsync(SourceSubmission, CancellationToken.None)).Should().BeTrue();

        store.Files.Dataverse.Rows.Should().NotContainKey(copy.Spec.FileId);
        store.InvoiceExists.Should().BeFalse();
    }

    [Fact(DisplayName = "094 undo (uac-r2 (b), must not fire): an undo held before the file step withdraws nothing - the Servable copy and its blob stay")]
    public async Task Undo_HeldBeforeTheFileStep_WithdrawsNothing()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        store.DeleteFault = "sprk_invoiceapproval";

        var result = await store.Gate!.UndoAsync(SourceSubmission, SubmissionReasonCodes.SourceCheckFailed, CancellationToken.None);

        result.Status.Should().Be(SubmissionCommitGateStatus.UndoHeld);
        store.CopyStatus.Should().Be(StoredFileStatus.Servable);
        store.CopyBlobExists.Should().BeTrue();
    }

    [Fact(DisplayName = "094 undo (uac-r2 K2, must fire): more file rows under the invoice than one transaction takes holds the undo with NOTHING deleted or withdrawn - no approval, line, row or blob")]
    public async Task Undo_MoreRowsThanOneTransaction_HoldsWithNothingDeleted()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, SourceInvoice);
        while (store.Files.Dataverse.Rows.Count <= SubmissionCommitGate.MaxFiles)
        {
            // Rows with no source: the shape the old surplus loop could keep as the only survivor.
            store.Files.SeedFile(Guid.NewGuid(), parent, "%PDF-1.7 extra"u8.ToArray(), StoredFileStatus.NotYetServable,
                StoredFilePurpose.InvoiceSupportingFile);
        }

        var rows = store.Files.Dataverse.Rows.Count;
        var statuses = store.Files.Dataverse.Rows.Values.Select(r => r.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value).ToList();

        var result = await store.Gate!.UndoAsync(SourceSubmission, SubmissionReasonCodes.SourceCheckFailed, CancellationToken.None);

        result.Status.Should().Be(SubmissionCommitGateStatus.UndoHeld);
        store.Deletes.Should().BeEmpty("held before the approval");
        store.UndoTransactions.Should().BeEmpty();
        store.Files.Dataverse.Rows.Should().HaveCount(rows);
        store.Files.Dataverse.Rows.Values.Select(r => r.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value).Should().Equal(statuses);
        store.CopyStatus.Should().Be(StoredFileStatus.Servable, "the sourced row is kept, never traded for a sourceless one");
        store.CopyBlobExists.Should().BeTrue();
        store.InvoiceExists.Should().BeTrue();
    }

    [Fact(DisplayName = "094 undo (uac-r2 K2, must not fire): exactly MaxFiles rows under the invoice fit one transaction - the undo completes, every row and the invoice deleted together")]
    public async Task Undo_ExactlyMaxFilesRows_Completes()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, SourceInvoice);
        while (store.Files.Dataverse.Rows.Count < SubmissionCommitGate.MaxFiles)
        {
            store.Files.SeedFile(Guid.NewGuid(), parent, "%PDF-1.7 extra"u8.ToArray(), StoredFileStatus.NotYetServable,
                StoredFilePurpose.InvoiceSupportingFile);
        }

        var result = await store.Gate!.UndoAsync(SourceSubmission, SubmissionReasonCodes.SourceCheckFailed, CancellationToken.None);

        result.Status.Should().Be(SubmissionCommitGateStatus.Undone);
        store.Files.Dataverse.Rows.Should().BeEmpty();
        store.InvoiceExists.Should().BeFalse();
        store.UndoTransactions.Should().ContainSingle().Which.Should().HaveCount(SubmissionCommitGate.MaxFiles + 1);
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

        retry.Status.Should().Be(SubmissionPersistStatus.Resumed, "the after-steps' resume runs the gate before anything else (InvoiceSubmissionAfterStepsTests)");
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

        /// <summary>Task 094: every delete the commit gate's undo sent through Dataverse, with how many Stored File rows were left then.</summary>
        public List<(string Table, Guid Id, int FilesLeft)> Deletes { get; } = [];

        /// <summary>Task 094: the invoice and approval rows the committed transaction created (the undo deletes them).</summary>
        private HashSet<(string Table, Guid Id)> CreatedRows { get; } = [];

        /// <summary>Task 094: the REAL commit gate the last <see cref="Build"/> composed (over this store's Dataverse, file store and isolation world).</summary>
        public SubmissionCommitGate? Gate { get; private set; }

        /// <summary>Task 094: the copy <see cref="StageSourceCopy"/> staged.</summary>
        public StagedSourceCopy? SourceCopy { get; private set; }

        /// <summary>Task 094: the copy's Stored File status now (null: no row).</summary>
        public StoredFileStatus? CopyStatus => SourceCopy is { } copy && Files.Dataverse.Rows.TryGetValue(copy.Spec.FileId, out var row)
            ? (StoredFileStatus)row.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value
            : null;

        /// <summary>Task 094: whether the copy's staged blob is still there.</summary>
        public bool CopyBlobExists => SourceCopy is { } copy && Files.Gateway.Get(copy.Blob.Container, copy.Blob.BlobPath) is not null;

        /// <summary>
        /// Task 094 (model 9.1 step 2): stages the job's copy of <see cref="Document"/> at <see cref="DocumentVersion"/> under
        /// <see cref="StoredFileIds.ForSourceVersion"/>, with the spec the job builds (Invoice PDF, Firm-visible No, the source ids, version and SHA).
        /// </summary>
        public StagedSourceCopy StageSourceCopy()
        {
            var bytes = "%PDF-1.7 source copy"u8.ToArray();
            var spec = new StoredFileRowSpec
            {
                FileId = StoredFileIds.ForSourceVersion(SourceSubmission, DocumentVersion),
                Purpose = StoredFilePurpose.InvoicePdf,
                Parent = new StoredFileParent(StoredFileParentKind.Invoice, SourceInvoice),
                Name = "invoice.pdf",
                ContentType = "application/pdf",
                FirmVisible = false,
                SourceDocumentId = Document,
                SourceVersion = DocumentVersion,
                SourceSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
                SubmissionId = SourceSubmission,
            };
            var blob = Files.Service.StageAsync(spec, new MemoryStream(bytes), CancellationToken.None).GetAwaiter().GetResult().Blob!;
            return SourceCopy = new StagedSourceCopy(spec, blob, DocumentVersion);
        }

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
            // The lines (the resume's read, the undo's delete loop); task 094: the gate's lineage read of the invoice goes to the isolation world.
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName == InvoiceSubmissionPersister.LineEntity
                    ? new EntityCollection(StoredLines.ToList())
                    : Isolation.Answer(q));

            // Task 094: the commit gate's writes - the undo's Failed write and its column clear (DBNull.Value clears), and its deletes.
            entities.Setup(e => e.UpdateAsync(FinanceBaseValues.SubmissionEntity, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    foreach (var (column, value) in fields)
                    {
                        if (value is DBNull)
                        {
                            Submission.Attributes.Remove(column);
                        }
                        else
                        {
                            Submission[column] = value;
                        }
                    }

                    return Task.CompletedTask;
                });
            entities.Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, CancellationToken _) =>
                {
                    if (DeleteFault == table)
                    {
                        DeleteFault = null;
                        return Task.FromException(new HttpRequestException("Dataverse unavailable"));
                    }

                    var existed = table == InvoiceSubmissionPersister.LineEntity ? StoredLines.RemoveAll(l => l.Id == id) > 0 : CreatedRows.Remove((table, id));
                    if (!existed)
                    {
                        return Task.FromException(new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"));
                    }

                    Deletes.Add((table, id, Files.Dataverse.Rows.Count));
                    return Task.CompletedTask;
                });
            entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    if (requests.Count > 0 && requests.All(r => r is DeleteRequest))
                    {
                        return UndoTransaction(requests.Cast<DeleteRequest>().ToList());
                    }

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

            // Task 094: the core record exists in the isolation world (an ordinary matter unless a test made it Secure), so the gate can walk
            // the committed invoice's roots.
            if (!Isolation.Has("sprk_matter", Matter))
            {
                Isolation.OrdinaryRoot("sprk_matter", Matter);
            }

            Gate = new SubmissionCommitGate(
                entities.Object,
                Files.Service,
                InvoiceSubmissionServiceTests.IsolationOver(() => Isolation),
                Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.SynchronizerOver(
                    () => Isolation, new Sprk.Bff.Api.Tests.AccessControl.FakeRecordShareTable()),
                NullLogger<SubmissionCommitGate>.Instance);

            return new InvoiceSubmissionPersister(
                entities.Object,
                recordTypes.Object,
                ownership.Object,
                new InvoiceLifecycleService(new Mock<IFieldMappingDataverseService>().Object, NullLogger<InvoiceLifecycleService>.Instance),
                totals.Object,
                InvoiceSubmissionServiceTests.IsolationOver(() => Isolation),
                Files.Service,
                Gate,
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

            // Task 094: the rows the undo deletes, and the invoice in the isolation world (filed under its core record, as the transaction files it).
            foreach (var create in requests.OfType<CreateRequest>())
            {
                switch (create.Target.LogicalName)
                {
                    case InvoiceSubmissionPersister.LineEntity:
                        StoredLines.Add(Copy(create.Target));
                        break;
                    case "sprk_invoice":
                        CreatedRows.Add(("sprk_invoice", create.Target.Id));
                        if (create.Target.GetAttributeValue<EntityReference>("sprk_matter") is { } matter)
                        {
                            if (Isolation.ValueOf<bool>("sprk_matter", matter.Id, "sprk_issecure"))
                            {
                                Isolation.SecureChild("sprk_invoice", create.Target.Id, ("sprk_matter", "sprk_matter", matter.Id));
                            }
                            else
                            {
                                Isolation.OrdinaryChild("sprk_invoice", create.Target.Id, ("sprk_matter", "sprk_matter", matter.Id));
                            }
                        }

                        break;
                    case InvoiceSubmissionPersister.ApprovalEntity:
                        CreatedRows.Add((InvoiceSubmissionPersister.ApprovalEntity, create.Target.Id));
                        break;
                }
            }

            foreach (var update in requests.OfType<UpdateRequest>().Where(u => u.Target.LogicalName == "sprk_invoicesubmission"))
            {
                foreach (var (column, value) in update.Target.Attributes)
                {
                    Submission[column] = value;
                }
            }
        }

        /// <summary>
        /// Fix round (undo order): the undo's one transaction - every Stored File row of the invoice and the invoice - applied all or nothing, as
        /// Dataverse's ExecuteTransaction does. <see cref="UndoTransactionFault"/> (one shot) rolls it back. Each delete is recorded in
        /// <see cref="Deletes"/> with the Stored File rows left after it.
        /// </summary>
        private Task UndoTransaction(IReadOnlyList<DeleteRequest> deletes)
        {
            UndoTransactions.Add(deletes);
            if (UndoTransactionFault is { } fault)
            {
                UndoTransactionFault = null;
                return Task.FromException(fault);
            }

            if (deletes.Any(d => d.Target.LogicalName == StoredFileSchema.Entity ? !Files.Dataverse.Rows.ContainsKey(d.Target.Id) : !CreatedRows.Contains((d.Target.LogicalName, d.Target.Id))))
            {
                return Task.FromException(new DataverseTransactionException("ObjectDoesNotExist", failedRequestIndex: 0, outcomeUnknown: false, null));
            }

            foreach (var delete in deletes)
            {
                if (delete.Target.LogicalName == StoredFileSchema.Entity)
                {
                    Files.Dataverse.Rows.TryRemove(delete.Target.Id, out _);
                }
                else
                {
                    CreatedRows.Remove((delete.Target.LogicalName, delete.Target.Id));
                }

                Deletes.Add((delete.Target.LogicalName, delete.Target.Id, Files.Dataverse.Rows.Count));
            }

            return Task.CompletedTask;
        }

        /// <summary>Fix round: every undo transaction sent (rows and invoice), rolled back or not.</summary>
        public List<IReadOnlyList<DeleteRequest>> UndoTransactions { get; } = [];

        /// <summary>Fix round: the next single-row delete of this table faults (one shot).</summary>
        public string? DeleteFault { get; set; }

        /// <summary>Fix round: the next undo transaction faults and rolls back (one shot).</summary>
        public Exception? UndoTransactionFault { get; set; }

        /// <summary>Fix round: whether the committed invoice row is still there.</summary>
        public bool InvoiceExists => CreatedRows.Any(r => r.Table == "sprk_invoice");

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
