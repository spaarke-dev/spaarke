using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using InvoiceSubmissionServiceTests = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests;
using IntakeHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceInvoiceReview;

/// <summary>
/// What the invoice-review confirm and reject paths WRITE, in what order, and what they leave behind when a write
/// fails or races (unified-access-control-r2 task 130; ebilling-r1 task 050a, owner decision D-134).
/// </summary>
/// <remarks>
/// <para><b>Confirm since 050a (D-134)</b>: confirm creates NO invoice and queues no extraction. It submits the document through
/// the one invoice intake (<see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>: one Internal submission per
/// document pointing at it, the processing job queued; no upload, no document create), then marks the document ConfirmedInvoice
/// (conditional on the version read BEFORE the intake: a reject that lands in between is 409 ReviewDecisionChanged, 050a review #2).
/// No confirm path creates an invoice: the submission job creates it (ADR-002) and, since ebilling-r1 task 094, COPIES the
/// document's bytes into the invoice's Stored File; the document is never re-filed, re-owned or linked to the invoice (the 050a
/// re-file and its "#2095" tests are retired: <see cref="SubmissionCommitGate"/>). A reject refuses a document that is linked to an
/// invoice or has a confirm submission.</para>
/// <para><b>The live schema</b> (spaarkedev1, read-only metadata, 2026-10-01): the link is <c>sprk_document.sprk_invoice</c>;
/// the notes column is <c>sprk_invoicereviewnotes</c> (<c>sprk_invoicerejectionnotes</c> does not exist).</para>
/// <para>Substitution is at the Dataverse service seams (<see cref="IFieldMappingDataverseService"/> as a stateful double; the
/// intake's own seams through <see cref="InvoiceSubmissionServiceTests.Harness"/>), the ownership resolver and the queue's method
/// boundary. No HTTP handler is mocked
/// (ADR-038). Interleavings are driven by explicit hooks, never by sleeps.</para>
/// </remarks>
public class InvoiceReviewWritePathTests
{
    private const int ToReview = 100000000;
    private const int Confirmed = 100000001;
    private const int Rejected = 100000002;

    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;

    private readonly RecordingRecords _records = new();
    private readonly IntakeHarness _intake = new();

    // =========================================================================================
    // Confirm (050a, D-134) — the successful path
    // =========================================================================================

    [Fact(DisplayName = "050a: confirm submits the document (one Internal submission pointing at it, the processing job), creates no invoice, then marks it ConfirmedInvoice")]
    public async Task Confirm_SubmitsTheDocument_CreatesNoInvoice_ThenMarksItConfirmed()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-1");

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        var submissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId);
        outcome.Receipt!.SubmissionId.Should().Be(submissionId);
        outcome.Receipt.JobId.Should().Be(InvoiceSubmissionService.ExistingDocumentJobIdFor(request.DocumentId));

        // The submission, through the intake: Internal, pointing at THIS document, filed under the matter, for the vendor (the firm).
        var allocation = _intake.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(submissionId);
        allocation.FirmId.Should().Be(request.VendorOrgId);
        allocation.Number.Should().Be("INV-42");
        allocation.Fields!["sprk_document"].Should().Be(new EntityReference("sprk_document", request.DocumentId));
        allocation.Fields["sprk_regardingmatter"].Should().Be(new EntityReference("sprk_matter", request.MatterId));
        ((OptionSetValue)allocation.Fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);

        // ADR-002 / D-19: the job creates the invoice; confirm queues the submission job and NEVER InvoiceExtraction.
        _intake.JobsSent.Should().ContainSingle().Which.JobType.Should().Be(InvoiceSubmissionService.JobType);
        _intake.JobsSent.Should().NotContain(j => j.JobType == "InvoiceExtraction");
        _intake.Stages.Should().BeEmpty("D-134: no second copy of the PDF");

        // Confirm's own writes: ONLY the document's review stamp — no invoice create, no link.
        var write = _records.Writes.Should().ContainSingle().Subject;
        write.Kind.Should().Be(WriteKind.UpdateIfUnchanged);
        write.Entity.Should().Be("sprk_document");
        write.Id.Should().Be(request.DocumentId);
        write.Fields["sprk_invoicereviewstatus"].Should().Be(Confirmed);
        write.Fields["sprk_invoicereviewnotes"].Should().Be("looks right");
        write.Fields.Keys.Should().NotContain("sprk_Invoice@odata.bind");
        _records.Writes.Should().NotContain(w => w.Entity == "sprk_invoice");
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
        _records.LinkOf(request.DocumentId).Should().BeNull("the job links the document when it creates the invoice");
    }

    [Fact(DisplayName = "050a: the reviewer the endpoint named is passed to the intake as the requester; it is never bound from the body")]
    public async Task Confirm_PassesTheReviewerToTheIntake_AndNeverBindsItFromTheBody()
    {
        var reviewerOid = Guid.NewGuid();
        var request = NewRequest() with { RequestedBy = RecordRequester.Of(systemUserId: null, objectId: reviewerOid) };
        _records.ExistingDocument(request.DocumentId);
        TheIntakeHolds(request.DocumentId);

        await Sut().ConfirmInvoiceAsync(request, "corr-2");

        _intake.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.ObjectId.Should().Be(reviewerOid);
        typeof(InvoiceReviewConfirmRequest).GetProperty(nameof(InvoiceReviewConfirmRequest.RequestedBy))!
            .GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false)
            .Should().ContainSingle("a client never chooses who created a record");
    }

    // =========================================================================================
    // Confirm — refusals write nothing
    // =========================================================================================

    [Theory(DisplayName = "050a / D-91: a missing or blank invoice number is 400 firm_invoice_number_required and nothing is written (never defaulted from the hint)")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Confirm_MissingInvoiceNumber_Is400_AndWritesNothing(string? number)
    {
        var request = NewRequest() with { InvoiceNumber = number };
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-3");

        outcome.StatusCode.Should().Be(400);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmInvoiceNumberRequired);
        AssertConfirmWroteNothing(request.DocumentId);
    }

    [Fact(DisplayName = "050a / D-106 / D-110: a vendor that is not one of the matter's assigned law firms is 403 firm_not_linked and nothing is written")]
    public async Task Confirm_FirmNotLinkedToTheMatter_Is403_AndWritesNothing()
    {
        var request = NewRequest() with { VendorOrgId = Guid.NewGuid() };
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        _intake.Firm = new Entity("sprk_organization", request.VendorOrgId) { ["statecode"] = new OptionSetValue(0) };

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-4");

        outcome.StatusCode.Should().Be(403);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        AssertConfirmWroteNothing(request.DocumentId);
    }

    [Fact(DisplayName = "050a: a document already linked to an invoice (confirmed before 050a) is 409 and nothing is written")]
    public async Task Confirm_DocumentAlreadyInvoiced_Is409_AndWritesNothing()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: Confirmed);
        TheIntakeHolds(request.DocumentId);
        _intake.ExistingDocument!["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-5");

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadyInvoiced);
        _intake.Allocations.Should().BeEmpty();
        _records.Writes.Should().BeEmpty();
    }

    // =========================================================================================
    // Confirm — idempotent by document; the mark after the submission
    // =========================================================================================

    [Fact(DisplayName = "050a: a retry of the same confirm returns the earlier submission, creates no second one, and marks the document again")]
    public async Task Confirm_Retry_ReturnsTheEarlierSubmission_AndCreatesNoSecond()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        var first = await Sut().ConfirmInvoiceAsync(request, "corr-6");
        TheSubmissionWasStored(first.Receipt!, request);

        var second = await Sut().ConfirmInvoiceAsync(request, "corr-6-retry");

        second.Receipt.Should().Be(first.Receipt);
        _intake.Allocations.Should().ContainSingle("one submission per document");
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    [Fact(DisplayName = "050a: the mark failing names the submission (StatusNotUpdated); a retry returns the same submission and completes the mark")]
    public async Task Confirm_MarkFails_NamesTheSubmission_AndARetryCompletesIt()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        _records.FailDocumentStatus = new HttpRequestException("dataverse down");

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-7");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var submissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId);
        failure.Failure.Should().Be(InvoiceReviewFailure.StatusNotUpdated);
        failure.SubmissionId.Should().Be(submissionId);
        failure.Message.Should().Contain(submissionId.ToString()).And.Contain("Retry the confirmation");
        _records.StatusOf(request.DocumentId).Should().Be(ToReview);

        _records.FailDocumentStatus = null;
        TheSubmissionWasStored(new InvoiceSubmissionReceipt(submissionId, Guid.Empty, "INV-42.1", request.DocumentId), request);
        var retried = await Sut().ConfirmInvoiceAsync(request, "corr-7-retry");

        retried.Receipt!.SubmissionId.Should().Be(submissionId);
        _intake.Allocations.Should().ContainSingle();
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    [Fact(DisplayName = "050a: an unrelated write between the mark's read and write is retried; the mark lands once")]
    public async Task Confirm_AnUnrelatedWriteBeforeTheMark_IsRetried()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        var touched = false;
        _records.BeforeConditionalWrite = _ =>
        {
            if (!touched)
            {
                touched = true;
                _records.TouchDocument(request.DocumentId);
            }

            return Task.CompletedTask;
        };

        await Sut().ConfirmInvoiceAsync(request, "corr-8");

        _records.Writes.Should().ContainSingle().Which.ExpectedVersion.Should().Be(2);
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    [Fact(DisplayName = "050a: a document that changes on every mark attempt is StatusNotUpdated naming the submission, after the bound")]
    public async Task Confirm_DocumentChangedOnEveryMarkAttempt_IsStatusNotUpdated()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        var attempts = 0;
        _records.BeforeConditionalWrite = _ =>
        {
            attempts++;
            _records.TouchDocument(request.DocumentId);
            return Task.CompletedTask;
        };

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-9");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.StatusNotUpdated);
        failure.SubmissionId.Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId));
        attempts.Should().Be(InvoiceReviewService.MaxConditionalWriteAttempts);
        _records.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: the mark runs under CancellationToken.None - a caller gone after the submission still gets the document marked")]
    public async Task Confirm_CallerCancelledAfterTheSubmission_TheMarkStillLands()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        using var cancel = new CancellationTokenSource();
        _intake.Allocator.Setup(a => a.CreateSubmissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid firm, string number, IReadOnlyDictionary<string, object>? fields, CancellationToken _) =>
            {
                _intake.Allocations.Add((id, firm, number, fields));
                cancel.Cancel(); // the caller goes away once the submission is created
                return TrackingNumberResult.Created("INV-42.1", 1);
            });

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-10", cancel.Token);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    // =========================================================================================
    // Confirm vs reject
    // =========================================================================================

    [Fact(DisplayName = "050a review #2: a reject that lands between the confirm's submission create and its mark is NOT overwritten: 409 ReviewDecisionChanged naming the submission")]
    public async Task Confirm_RejectLandsBetweenTheCreateAndTheMark_Is409_AndTheRejectStands()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        _intake.Allocator.Setup(a => a.CreateSubmissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, Guid firm, string number, IReadOnlyDictionary<string, object>? fields, CancellationToken _) =>
            {
                _intake.Allocations.Add((id, firm, number, fields));
                // The reject read the document before the submission existed (its submission read finds none), and writes now.
                await Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = request.DocumentId, Notes = "statement" }, "corr-r");
                return TrackingNumberResult.Created("INV-42.1", 1);
            });

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-race");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ReviewDecisionChanged);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.review_decision_changed");
        failure.SubmissionId.Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId));
        _records.StatusOf(request.DocumentId).Should().Be(Rejected, "the reject stands; the job refuses the submission (document_rejected)");
        _records.Writes.Select(w => (int)w.Fields["sprk_invoicereviewstatus"]!).Should().Equal(Rejected);
    }

    [Fact(DisplayName = "050a review #2: a document already rejected is not confirmed: 409 ReviewDecisionChanged, no submission, nothing written")]
    public async Task Confirm_DocumentAlreadyRejected_Is409_AndNothingIsSubmitted()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: Rejected);
        TheIntakeHolds(request.DocumentId);

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-rejected");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ReviewDecisionChanged);
        failure.SubmissionId.Should().BeNull("nothing was submitted");
        _intake.Allocations.Should().BeEmpty();
        _intake.JobsSent.Should().BeEmpty();
        _records.Writes.Should().BeEmpty();
        _records.StatusOf(request.DocumentId).Should().Be(Rejected);
    }

    [Fact(DisplayName = "050a review round 2, item 5: a fault of confirm's pre-intake review-state read is ReviewStateUnreadable (503): nothing submitted, nothing written")]
    public async Task Confirm_ReviewStateReadFaults_IsReviewStateUnreadable_AndNothingIsSubmitted()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId);
        TheIntakeHolds(request.DocumentId);
        _records.FailRead = new HttpRequestException("Dataverse 503");

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-unreadable");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ReviewStateUnreadable);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.review_state_unreadable");
        failure.Message.Should().Contain("Nothing was saved");
        failure.SubmissionId.Should().BeNull();
        _intake.Allocations.Should().BeEmpty();
        _intake.JobsSent.Should().BeEmpty();
        _records.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review #6: a document deleted after its submission is DocumentNotFound naming the submission, not an endless 500 'retry'")]
    public async Task Confirm_DocumentDeletedAfterTheSubmission_IsDocumentNotFound_NamingTheSubmission()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        _intake.Allocator.Setup(a => a.CreateSubmissionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid firm, string number, IReadOnlyDictionary<string, object>? fields, CancellationToken _) =>
            {
                _intake.Allocations.Add((id, firm, number, fields));
                _records.Remove("sprk_document", request.DocumentId);
                return TrackingNumberResult.Created("INV-42.1", 1);
            });

        var act = () => Sut().ConfirmInvoiceAsync(request, "corr-gone");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        var submissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId);
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentNotFound);
        failure.ReasonCode.Should().Be(InvoiceReviewException.DocumentNotFoundReasonCode);
        failure.SubmissionId.Should().Be(submissionId);
        failure.Message.Should().Contain(submissionId.ToString()).And.Contain("no longer exists");
    }

    [Fact(DisplayName = "050a review #4 (D-139): a retry after the submission ended Rejected is 409 submission_already_ended and the document is NOT re-marked")]
    public async Task Confirm_RetryAfterTheSubmissionWasRejected_Is409_AndDoesNotMark()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        TheSubmissionWasStored(new InvoiceSubmissionReceipt(
            InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId), Guid.Empty, "INV-42.1", request.DocumentId), request);
        _intake.Submission!["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected);

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-ended");

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded);
        _records.Writes.Should().BeEmpty("the document is not re-marked");
        _intake.JobsSent.Should().BeEmpty();
    }

    [Fact(DisplayName = "EB-D-167: a confirm retried after the submission ended Failed is a NEW attempt (generation 2, its job queued) and the document is marked")]
    public async Task Confirm_RetryAfterTheSubmissionFailed_IsANewAttempt_AndMarks()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        var first = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId);
        TheSubmissionWasStored(new InvoiceSubmissionReceipt(first, Guid.Empty, "INV-42.1", request.DocumentId), request);
        _intake.Submission!["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed);
        _intake.Submissions[first] = _intake.Submission;

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-again");

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.SubmissionId.Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId, 2));
        _intake.Allocations.Should().ContainSingle().Which.SubmissionId.Should().Be(outcome.Receipt.SubmissionId);
        _intake.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(InvoiceSubmissionService.ExistingDocumentJobIdFor(request.DocumentId, 2));
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed);
    }

    [Fact(DisplayName = "050a review #3 / #7: a retry naming ANOTHER matter is 409 and the document is not re-marked")]
    public async Task Confirm_RetryWithAnotherMatter_Is409()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        TheSubmissionWasStored(new InvoiceSubmissionReceipt(
            InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId), Guid.Empty, "INV-42.1", request.DocumentId), request);
        _intake.Submission!["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-other-matter");

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        _records.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: a reject of a document with a confirm submission (not yet linked by the job) is 409 and writes nothing")]
    public async Task Reject_AfterConfirm_BeforeTheJobLinks_Is409_AndWritesNothing()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: Confirmed);
        _intake.Submission = new Entity("sprk_invoicesubmission", InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId));

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = request.DocumentId }, "corr-11");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToInvoice);
        failure.SubmissionId.Should().BeNull("a refusal names nothing");
        _records.Writes.Should().BeEmpty();
        _intake.SubmissionReads.Should().Equal(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId));
    }

    [Fact(DisplayName = "050a: a confirm that lands between a reject's read and write makes the reject 409; ConfirmedInvoice stands")]
    public async Task ConfirmLandsFirst_DuringAReject_TheRejectIs409_AndConfirmedStands()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        InvoiceSubmissionOutcome? confirmed = null;
        _records.BeforeConditionalWrite = async fields =>
        {
            if (confirmed is null && fields.TryGetValue("sprk_invoicereviewstatus", out var s) && (int)s! == Rejected)
            {
                confirmed = await Sut().ConfirmInvoiceAsync(request, "corr-confirm");
                TheSubmissionWasStored(confirmed.Receipt!, request);
            }
        };

        var act = () => Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = request.DocumentId, Notes = "not an invoice" }, "corr-reject");

        (await act.Should().ThrowAsync<InvoiceReviewException>()).Which.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToInvoice);
        confirmed!.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        _records.StatusOf(request.DocumentId).Should().Be(Confirmed, "the reject did not overwrite the confirmation");
        _records.Writes.Where(w => w.Fields.ContainsKey("sprk_invoicereviewstatus"))
            .Select(w => (int)w.Fields["sprk_invoicereviewstatus"]!)
            .Should().NotContain(Rejected);
    }

    [Fact(DisplayName = "050a: a reject whose confirm state cannot be read is ConfirmStateUnreadable and writes nothing")]
    public async Task Reject_ConfirmStateUnreadable_WritesNothing()
    {
        var documentId = Guid.NewGuid();
        _records.ExistingDocument(documentId, reviewStatus: ToReview);
        _intake.SubmissionReadFailure = new HttpRequestException("dataverse down");

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = documentId }, "corr-12");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.ConfirmStateUnreadable);
        failure.ReasonCode.Should().Be("sdap.finance.invoice_review.confirm_state_unreadable");
        _records.Writes.Should().BeEmpty();
    }

    // =========================================================================================
    // Reject
    // =========================================================================================

    [Fact]
    public async Task Reject_WithNotes_WritesTheLiveNotesColumn_ConditionallyAndUpdateOnly()
    {
        var documentId = Guid.NewGuid();
        _records.ExistingDocument(documentId);

        await Sut().RejectInvoiceAsync(
            new InvoiceReviewRejectRequest { DocumentId = documentId, Notes = "statement, not an invoice" }, "corr-14");

        var write = _records.Writes.Should().ContainSingle().Subject;
        write.Kind.Should().Be(WriteKind.UpdateIfUnchanged, "conditional on the version read — never creates, never overwrites unseen");
        write.ExpectedVersion.Should().Be(1);
        write.Entity.Should().Be("sprk_document");
        write.Id.Should().Be(documentId);
        write.Fields["sprk_invoicereviewstatus"].Should().Be(100000002);
        write.Fields["sprk_invoicereviewnotes"].Should().Be("statement, not an invoice");
        write.Fields.Keys.Should().NotContain("sprk_invoicerejectionnotes", "that column does not exist on live sprk_document");
    }

    [Fact]
    public async Task Reject_DocumentLinkedToAnInvoice_Is409_WithNoWrite_AndNeverNamesTheInvoice()
    {
        var documentId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        _records.ExistingDocument(documentId, linkedInvoiceId: invoiceId, reviewStatus: Confirmed);

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = documentId }, "corr-15");

        var failure = (await act.Should().ThrowAsync<InvoiceReviewException>()).Which;
        failure.Failure.Should().Be(InvoiceReviewFailure.DocumentLinkedToInvoice);
        failure.SubmissionId.Should().BeNull();
        failure.Message.Should().NotContain(invoiceId.ToString()).And.NotContainEquivalentOf(invoiceId.ToString("N"));
        _records.Writes.Should().BeEmpty();
        _records.StatusOf(documentId).Should().Be(Confirmed);
    }

    [Fact]
    public async Task Reject_DocumentGone_ThrowsNotFound_WithNoWrite()
    {
        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = Guid.NewGuid() }, "corr-16");

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _records.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_DocumentChangedOnEveryAttempt_Is409AfterTheBound_WithNoWrite()
    {
        var documentId = Guid.NewGuid();
        _records.ExistingDocument(documentId);
        var attempts = 0;
        _records.BeforeConditionalWrite = _ =>
        {
            attempts++;
            _records.TouchDocument(documentId);
            return Task.CompletedTask;
        };

        var act = () => Sut().RejectInvoiceAsync(new InvoiceReviewRejectRequest { DocumentId = documentId }, "corr-17");

        (await act.Should().ThrowAsync<InvoiceReviewException>())
            .Which.Failure.Should().Be(InvoiceReviewFailure.DocumentChangedConcurrently);
        attempts.Should().Be(3);
        _records.Writes.Should().BeEmpty();
    }

    // =========================================================================================
    // Name (used by the submission job's persister)
    // =========================================================================================

    [Theory]
    [InlineData("Acme LLP", "INV-1", null, "Acme LLP - INV-1")]
    [InlineData("Acme LLP", null, "2026-09-01", "Acme LLP - 2026-09-01")]
    [InlineData("Acme LLP", "  ", null, "Acme LLP - Invoice")]
    [InlineData(null, "INV-1", null, "Invoice INV-1")]
    [InlineData(" ", null, null, "Invoice")]
    public void InvoiceName_IsVendorPlusNumberOrDate(string? vendor, string? number, string? date, string expected)
    {
        var parsed = date is null ? (DateTime?)null : DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        InvoiceReviewService.BuildInvoiceName(vendor, number, parsed).Should().Be(expected);
    }

    [Fact]
    public void InvoiceName_IsCappedAtTheLiveColumnLength()
    {
        InvoiceReviewService.BuildInvoiceName(new string('v', 900), "INV-1", null).Length.Should().Be(850);
    }
    // =========================================================================================
    // Helpers
    // =========================================================================================

    /// <summary>The service over the stateful records double and the REAL intake (its seams are the harness's doubles).</summary>
    private InvoiceReviewService Sut() =>
        new(_records, _intake.Build(), new FinanceTelemetry(), NullLogger<InvoiceReviewService>.Instance);

    /// <summary>A confirm of a fresh document for the harness's matter and its assigned law firm, with a number and notes.</summary>
    private static InvoiceReviewConfirmRequest NewRequest() => new()
    {
        DocumentId = Guid.NewGuid(),
        MatterId = MatterId,
        VendorOrgId = FirmId,
        InvoiceNumber = "INV-42",
        Notes = "looks right",
    };

    /// <summary>The document as the intake reads it: present, holding a stored file, not linked to an invoice.</summary>
    private void TheIntakeHolds(Guid documentId) =>
        _intake.ExistingDocument = new Entity("sprk_document", documentId)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-9",
        };

    /// <summary>The confirm submission as Dataverse now holds it (the allocator double does not store rows).</summary>
    private void TheSubmissionWasStored(InvoiceSubmissionReceipt receipt, InvoiceReviewConfirmRequest request) =>
        _intake.Submission = new Entity("sprk_invoicesubmission", receipt.SubmissionId)
        {
            [TrackingNumberAllocator.TrackingNumberColumn] = receipt.TrackingNumber,
            [TrackingNumberAllocator.FirmInvoiceNumberColumn] = request.InvoiceNumber,
            [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", request.VendorOrgId),
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received),
            ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
            ["sprk_document"] = new EntityReference("sprk_document", request.DocumentId),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", request.MatterId),
        };

    /// <summary>A refused confirm: no submission, no job, no upload, no document create, no review stamp.</summary>
    private void AssertConfirmWroteNothing(Guid documentId)
    {
        _intake.Allocations.Should().BeEmpty();
        _intake.JobsSent.Should().BeEmpty();
        _intake.Stages.Should().BeEmpty();
        _records.Writes.Should().BeEmpty();
        _records.StatusOf(documentId).Should().Be(ToReview);
    }

    internal enum WriteKind { Upsert, UpdateExisting, UpdateIfUnchanged }

    internal sealed record Write(
        WriteKind Kind, string Entity, Guid Id, Dictionary<string, object?> Fields, long? ExpectedVersion = null);

    /// <summary>Holds the first N creates after they are applied, until released — the interleaving control.</summary>
    internal sealed class CreateGate
    {
        public CreateGate(int count)
        {
            Arrived = Enumerable.Range(0, count)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            Release = Enumerable.Range(0, count)
                .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            CreatedIds = new Guid[count];
        }

        public TaskCompletionSource[] Arrived { get; }
        public TaskCompletionSource[] Release { get; }
        public Guid[] CreatedIds { get; }
        public int Next { get; set; }
    }

    /// <summary>
    /// Hand-written <see cref="IFieldMappingDataverseService"/> that keeps row state like Dataverse: reads answer
    /// from registered rows (an empty dictionary for an unknown row, as the live Web API implementation does);
    /// every successful write is recorded and applied; a document's <c>versionnumber</c> rises on every write and
    /// the conditional write refuses a stale one with <see cref="DBConcurrencyException"/> (the live 412) and an
    /// absent row with <see cref="KeyNotFoundException"/> (the live 404). A write configured to fail throws and is not
    /// applied — unless it is an "after applying" failure, which models a lost response. A cancelled token throws
    /// before anything happens, as the real transport does. Hooks run other writers at exact points.
    /// </summary>
    internal sealed class RecordingRecords : IFieldMappingDataverseService
    {
        private readonly Dictionary<(string Entity, Guid Id), Dictionary<string, object?>> _rows = new();
        private CreateGate? _gate;

        public List<Write> Writes { get; } = new();
        public Exception? FailUpsert { get; set; }
        public Exception? FailUpsertAfterApplying { get; set; }
        public Exception? FailDocumentLink { get; set; }
        public Exception? FailDocumentLinkAfterApplying { get; set; }
        public Exception? FailDocumentStatus { get; set; }

        /// <summary>Non-null: every read throws it (a transient Dataverse fault, review round 2 item 5).</summary>
        public Exception? FailRead { get; set; }
        public Action? OnUpsertApplied { get; set; }
        public Func<Task>? OnUpsertAppliedAsync { get; set; }

        /// <summary>Runs at the start of every conditional write, BEFORE its version is checked — another writer's slot.</summary>
        public Func<Dictionary<string, object?>, Task>? BeforeConditionalWrite { get; set; }

        /// <summary>Conditional document writes that carried the link, applied or refused.</summary>
        public int LinkAttempts { get; private set; }

        public void ExistingDocument(
            Guid id, Guid? linkedInvoiceId = null, int? reviewStatus = null, DateTime? reviewedOnUtc = null,
            string? reviewNotes = null) =>
            _rows[("sprk_document", id)] = new()
            {
                ["_sprk_invoice_value"] = linkedInvoiceId?.ToString(),
                ["sprk_invoicereviewstatus"] = reviewStatus is null ? null : (long)reviewStatus.Value,
                ["sprk_invoicereviewedon"] = reviewedOnUtc is null ? null : AsWebApiDateTime(reviewedOnUtc.Value),
                ["sprk_invoicereviewnotes"] = reviewNotes,
                ["versionnumber"] = 1L,
            };

        /// <summary>The Web API answers a date-time column as an ISO string at whole-second precision.</summary>
        private static string AsWebApiDateTime(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        public void Invoice(Guid id, Guid matterId, Guid vendorId) =>
            _rows[("sprk_invoice", id)] = new()
            {
                ["_sprk_matter_value"] = matterId.ToString(),
                ["_sprk_vendororg_value"] = vendorId.ToString(),
            };

        public void Vendor(Guid id, string name) =>
            _rows[("sprk_organization", id)] = new() { ["sprk_organizationname"] = name };

        public void Matter(Guid id, string name, string? number) =>
            _rows[("sprk_matter", id)] = new() { ["sprk_mattername"] = name, ["sprk_matternumber"] = number };

        public void TouchDocument(Guid id) => Bump(_rows[("sprk_document", id)]);

        public void SetLink(Guid documentId, Guid invoiceId)
        {
            var row = _rows[("sprk_document", documentId)];
            row["_sprk_invoice_value"] = invoiceId.ToString();
            Bump(row);
        }

        public bool Exists(string entity, Guid id) => _rows.ContainsKey((entity, id));

        public void Remove(string entity, Guid id) => _rows.Remove((entity, id));

        public Guid? LinkOf(Guid documentId) =>
            _rows.TryGetValue(("sprk_document", documentId), out var row)
            && row["_sprk_invoice_value"] is string raw ? Guid.Parse(raw) : null;

        public int? StatusOf(Guid documentId) =>
            _rows.TryGetValue(("sprk_document", documentId), out var row)
            && row["sprk_invoicereviewstatus"] is long status ? (int)status : null;

        public CreateGate HoldCreates(int count) => _gate = new CreateGate(count);

        public Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
            string entityLogicalName, Guid recordId, string[] fields, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailRead is { } readFault)
            {
                return Task.FromException<Dictionary<string, object?>>(readFault);
            }

            if (!_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                return Task.FromResult(new Dictionary<string, object?>());
            }

            return Task.FromResult(fields.ToDictionary(f => f, f => row.TryGetValue(f, out var v) ? v : null));
        }

        public async Task UpdateRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields,
            CancellationToken ct = default, Guid? impersonateSystemUserId = null)
        {
            ct.ThrowIfCancellationRequested();
            if (FailUpsert is not null)
            {
                throw FailUpsert;
            }

            Writes.Add(new Write(WriteKind.Upsert, entityLogicalName, recordId, new(fields)));
            if (entityLogicalName == "sprk_invoice")
            {
                _rows[("sprk_invoice", recordId)] = new()
                {
                    ["_sprk_matter_value"] = BoundId(fields, "sprk_Matter@odata.bind")?.ToString(),
                    ["_sprk_vendororg_value"] = BoundId(fields, "sprk_vendororg@odata.bind")?.ToString(),
                };
            }

            OnUpsertApplied?.Invoke();
            if (OnUpsertAppliedAsync is not null)
            {
                await OnUpsertAppliedAsync();
            }

            if (FailUpsertAfterApplying is not null)
            {
                throw FailUpsertAfterApplying;
            }

            if (_gate is { } gate && gate.Next < gate.Arrived.Length)
            {
                var index = gate.Next++;
                gate.CreatedIds[index] = recordId;
                gate.Arrived[index].SetResult();
                await gate.Release[index].Task;
            }
        }

        public Task UpdateExistingRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Writes.Add(new Write(WriteKind.UpdateExisting, entityLogicalName, recordId, new(fields)));
            if (_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                Apply(row, fields); // an UNconditional write overwrites whatever is there
            }

            return Task.CompletedTask;
        }

        public Task UpdateExistingRecordFieldsAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, Guid? impersonateSystemUserId,
            CancellationToken ct = default) =>
            UpdateExistingRecordFieldsAsync(entityLogicalName, recordId, fields, ct); // this fake does not model the caller

        public async Task UpdateRecordFieldsIfUnchangedAsync(
            string entityLogicalName, Guid recordId, Dictionary<string, object?> fields, long expectedVersion,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var isLink = fields.ContainsKey("sprk_Invoice@odata.bind");
            if (isLink)
            {
                LinkAttempts++;
            }

            if (BeforeConditionalWrite is not null)
            {
                await BeforeConditionalWrite(fields);
            }

            if (entityLogicalName == "sprk_document" && isLink && FailDocumentLink is not null)
            {
                throw FailDocumentLink;
            }

            if (entityLogicalName == "sprk_document" && fields.ContainsKey("sprk_invoicereviewstatus") && FailDocumentStatus is not null)
            {
                throw FailDocumentStatus;
            }

            if (!_rows.TryGetValue((entityLogicalName, recordId), out var row))
            {
                throw new KeyNotFoundException($"{entityLogicalName} record was not found.");
            }

            if ((long)row["versionnumber"]! != expectedVersion)
            {
                throw new DBConcurrencyException("412 Precondition Failed");
            }

            Writes.Add(new Write(WriteKind.UpdateIfUnchanged, entityLogicalName, recordId, new(fields), expectedVersion));
            Apply(row, fields);

            if (isLink && FailDocumentLinkAfterApplying is not null)
            {
                throw FailDocumentLinkAfterApplying;
            }
        }

        private static void Apply(Dictionary<string, object?> row, Dictionary<string, object?> fields)
        {
            if (BoundId(fields, "sprk_Invoice@odata.bind") is { } invoiceId)
            {
                row["_sprk_invoice_value"] = invoiceId.ToString();
            }

            if (fields.TryGetValue("sprk_invoicereviewstatus", out var status) && status is int value)
            {
                row["sprk_invoicereviewstatus"] = (long)value; // the Web API reads an option set back as a number (long)
            }

            if (fields.TryGetValue("sprk_invoicereviewedon", out var reviewedOn) && reviewedOn is DateTime stamp)
            {
                row["sprk_invoicereviewedon"] = AsWebApiDateTime(stamp);
            }

            if (fields.TryGetValue("sprk_invoicereviewnotes", out var notes))
            {
                row["sprk_invoicereviewnotes"] = notes;
            }

            Bump(row);
        }

        private static void Bump(Dictionary<string, object?> row) =>
            row["versionnumber"] = row.TryGetValue("versionnumber", out var v) && v is long version ? version + 1 : 1L;

        private static Guid? BoundId(Dictionary<string, object?> fields, string bind)
        {
            if (!fields.TryGetValue(bind, out var raw) || raw is not string path)
            {
                return null;
            }

            var open = path.IndexOf('(');
            return Guid.Parse(path[(open + 1)..path.IndexOf(')')]);
        }

        public Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingProfileEntity?> GetFieldMappingProfileAsync(
            string sourceEntity, string targetEntity, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
            Guid profileId, bool activeOnly = true, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid[]> QueryChildRecordIdsAsync(
            string childEntityLogicalName, string parentLookupField, Guid parentRecordId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
            string sourceEntity, string targetEntity, bool activeRulesOnly = true, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
