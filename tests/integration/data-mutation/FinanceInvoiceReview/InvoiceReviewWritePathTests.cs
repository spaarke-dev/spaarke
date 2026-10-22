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
/// No confirm path creates an invoice: the submission job creates it (ADR-002) and files the document under it
/// (<see cref="InvoiceDocumentRefile"/>, the link's one writer; its tests are the "#2095" section below, over the REAL ownership
/// resolver and reconciler). A reject refuses a document that is linked to an invoice or has a confirm submission.</para>
/// <para><b>The live schema</b> (spaarkedev1, read-only metadata, 2026-10-01): the link is <c>sprk_document.sprk_invoice</c>;
/// the notes column is <c>sprk_invoicereviewnotes</c> (<c>sprk_invoicerejectionnotes</c> does not exist).</para>
/// <para>Substitution is at the Dataverse service seams (<see cref="IFieldMappingDataverseService"/> as a stateful double; the
/// intake's own seams through <see cref="InvoiceSubmissionServiceTests.Harness"/>), the ownership resolver and the queue's method
/// boundary. The re-file runs the REAL <see cref="RecordOwnershipResolver"/> and <see cref="SecureChildReconciler"/> over an
/// in-memory Dataverse. No HTTP handler is mocked
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

    // #2095 / 050a review #7: the REAL reconciler and the REAL ownership resolver over ONE in-memory Dataverse. A document the
    // world does not hold reads as not isolated.
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();

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
        _intake.Uploads.Should().BeEmpty("D-134: no second copy of the PDF");
        _intake.DocumentCreates.Should().BeEmpty();

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

    [Fact(DisplayName = "050a review #4: a retry after the submission ended Failed is 409 submission_already_ended and the document is NOT re-marked")]
    public async Task Confirm_RetryAfterTheSubmissionFailed_Is409_AndDoesNotMark()
    {
        var request = NewRequest();
        _records.ExistingDocument(request.DocumentId, reviewStatus: ToReview);
        TheIntakeHolds(request.DocumentId);
        TheSubmissionWasStored(new InvoiceSubmissionReceipt(
            InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId), Guid.Empty, "INV-42.1", request.DocumentId), request);
        _intake.Submission!["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Failed);

        var outcome = await Sut().ConfirmInvoiceAsync(request, "corr-ended");

        outcome.StatusCode.Should().Be(409);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded);
        _records.Writes.Should().BeEmpty("the document is not re-marked");
        _intake.JobsSent.Should().BeEmpty();
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
    // #2095 / 050a (D-134 item 3; review #1 / #7) — the job files a confirmed document under its invoice: live-row check,
    // isolation read, ReparentAsync (whose conditional write sets the link), then SecureChildReconciler.AfterRefileAsync.
    // The REAL RecordOwnershipResolver and the REAL reconciler over ONE in-memory Dataverse (SecureChildShareWorld): the
    // link moves only because the re-file wrote it, and the owner only because the resolver reassigned it.
    // =========================================================================================

    private static readonly Guid SecureMatter = Guid.Parse("20950000-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryMatter = Guid.Parse("20950000-0000-4000-8000-000000000002");
    private static readonly Guid FlaggedMatter = Guid.Parse("20950000-0000-4000-8000-000000000003");
    private static readonly Guid SecureInvoice = Guid.Parse("20950000-0000-4000-8000-0000000000f1");
    private static readonly Guid OrdinaryInvoice = Guid.Parse("20950000-0000-4000-8000-0000000000f2");
    private static readonly Guid FlaggedInvoice = Guid.Parse("20950000-0000-4000-8000-0000000000f3");
    private static readonly Guid Sharee = Guid.Parse("20950000-0000-4000-8000-0000000000b1");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    /// <summary>Each document's <c>versionnumber</c> (1 until a write moves it), as Dataverse keeps it.</summary>
    private readonly Dictionary<Guid, long> _versions = new();

    /// <summary>The link writes the re-file sent and Dataverse applied, each with the version it was conditional on.</summary>
    private readonly List<(Guid Document, Guid Invoice, long ExpectedVersion)> _linkWrites = new();

    /// <summary>Every conditional link write the re-file sent, applied or refused (412).</summary>
    private int _linkWriteAttempts;

    /// <summary>Runs at the start of each conditional link write, before its version is checked — another writer's slot.</summary>
    private Action<Guid>? _beforeLinkWrite;

    /// <summary>Each document's <c>sprk_invoicereviewstatus</c> (absent: no decision), as the re-file's row read answers it.</summary>
    private readonly Dictionary<Guid, int> _reviewStatus = new();

    /// <summary>Runs when the real resolver's <c>ReparentAsync</c> has RETURNED (link written, owner moved), before the step after the re-file.</summary>
    private Action? _afterReparent;

    /// <summary>The real resolver, with <see cref="_afterReparent"/> run once its re-file has landed.</summary>
    private sealed class AfterReparentHook(IRecordOwnershipResolver inner, Func<Action?> hook) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) => inner.ResolveOwningTeamAsync(context, ct);

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct) => inner.ResolveOwnerAsync(context, ct);

        public async Task<RecordOwnerResolution> ReparentAsync(RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
        {
            var resolution = await inner.ReparentAsync(request, applyChange, ct);
            hook()?.Invoke();
            return resolution;
        }
    }

    /// <summary>
    /// Review round 2, item 4: the share seam as the Web API's HTTP calls behave - a cancelled token is never sent, read or
    /// write. The shared <see cref="FakeRecordShareTable"/> ignores the token, so without this a step after the re-file that
    /// ran on the job's (cancelled) token would pass the K1 test exactly as one on <see cref="CancellationToken.None"/>.
    /// </summary>
    private sealed class TokenHonouringShares(Sprk.Bff.Api.Services.Access.IDataverseRecordShareService inner)
        : Sprk.Bff.Api.Services.Access.IDataverseRecordShareService
    {
        public Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GrantAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);
        }

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.ModifyAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);
        }

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.RevokeAccessAsync(entitySetName, recordId, principal, ct);
        }

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, DataversePrincipalRef recordOwner, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.RevokeAccessAsync(entitySetName, recordId, principal, recordOwner, ct);
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);
        }

        public Task<AccessRights> GetPrincipalRightsAsync(Guid principalSystemUserId, string entitySetName, Guid recordId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetPrincipalRightsAsync(principalSystemUserId, entitySetName, recordId, ct);
        }

        public Task<AccessRights?> GetPrincipalRightsOrUnknownAsync(Guid principalSystemUserId, string entitySetName, Guid recordId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetPrincipalRightsOrUnknownAsync(principalSystemUserId, entitySetName, recordId, ct);
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
        }
    }

    /// <summary>The world: the two matters, an invoice under each, and a third invoice under a matter flagged but not isolated.</summary>
    private void Matters()
    {
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .FlaggedNotIsolatedRoot("sprk_matter", FlaggedMatter)
            .SecureChild("sprk_invoice", SecureInvoice, ("sprk_matter", "sprk_matter", SecureMatter))
            .OrdinaryChild("sprk_invoice", OrdinaryInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter))
            .OrdinaryChild("sprk_invoice", FlaggedInvoice, ("sprk_matter", "sprk_matter", FlaggedMatter));
    }

    private long VersionOf(Guid document) => _versions.TryGetValue(document, out var v) ? v : 1L;

    /// <summary>Another writer files the document under <paramref name="invoice"/> (a document PUT): the lookup moves and the version rises.</summary>
    private void SomeoneFiles(Guid document, Guid invoice)
    {
        _world.Set("sprk_document", document, "sprk_invoice", new EntityReference("sprk_invoice", invoice));
        _versions[document] = VersionOf(document) + 1;
    }

    /// <summary>The REAL ownership resolver over the world (its row reads included), as the host registers it.</summary>
    private RecordOwnershipResolver RealResolver()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) => Task.FromResult(_world.Retrieve(table, id, columns)));
        return new RecordOwnershipResolver(entities.Object, SecureChildShareWorld.Configuration(), NullLogger<RecordOwnershipResolver>.Instance);
    }

    /// <summary>
    /// The document reads and the conditional link write over the SAME world rows the resolver reads: the read answers the link and the
    /// version (an empty dictionary for a missing row, as the Web API service does); the write refuses a stale version (the live 412) and
    /// otherwise moves the lookup and the version. Strict: any other call fails the test.
    /// </summary>
    private Mock<IFieldMappingDataverseService> WorldDocuments()
    {
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => !_world.Has(table, id)
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>
                {
                    ["_sprk_invoice_value"] = _world.ValueOf<EntityReference>(table, id, "sprk_invoice")?.Id.ToString(),
                    // The Web API answers an option set column as its integer value (review round 2, item 3).
                    ["sprk_invoicereviewstatus"] = _reviewStatus.TryGetValue(id, out var status) ? status : null,
                    ["versionnumber"] = VersionOf(id),
                });
        records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                "sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object?> fields, long expected, CancellationToken token) =>
            {
                _linkWriteAttempts++;
                _beforeLinkWrite?.Invoke(id);

                // Review round 2, item 4: the token is honoured as the Web API's HTTP call honours it - a cancelled write is never sent.
                token.ThrowIfCancellationRequested();
                if (!_world.Has(table, id))
                {
                    throw new KeyNotFoundException($"{table} record was not found.");
                }

                if (VersionOf(id) != expected)
                {
                    throw new DBConcurrencyException("412 Precondition Failed");
                }

                fields.Keys.Should().Equal("sprk_Invoice@odata.bind");
                var path = (string)fields["sprk_Invoice@odata.bind"]!;
                path.Should().StartWith("/sprk_invoices(");
                var invoice = Guid.Parse(path["/sprk_invoices(".Length..^1]);
                _world.Set(table, id, "sprk_invoice", new EntityReference("sprk_invoice", invoice));
                _versions[id] = expected + 1;
                _linkWrites.Add((id, invoice, expected));
                return Task.CompletedTask;
            });
        return records;
    }

    /// <summary>The re-file of <paramref name="document"/>'s confirm submission under <paramref name="invoice"/>, all real.</summary>
    private Task<InvoiceDocumentRefileResult> Refile(Guid document, Guid invoice, CancellationToken ct = default) =>
        new InvoiceDocumentRefile(new AfterReparentHook(RealResolver(), () => _afterReparent),
                SecureChildShareWorld.ReconcilerOver(() => _world, new TokenHonouringShares(_shares), null!), WorldDocuments().Object,
                NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document), document, invoice, ct);

    private Guid? LinkOf(Guid document) => _world.ValueOf<EntityReference>("sprk_document", document, "sprk_invoice")?.Id;

    [Fact(DisplayName = "050a review #1: an unfiled ordinary document re-filed under a Secure matter's invoice: the re-file writes the link (conditional on the version read), the resolver isolates it, and the mirror follows")]
    public async Task Refile_UnfiledDocument_UnderASecureMattersInvoice_WritesTheLink_IsolatesIt_AndMirrors()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);

        var result = await Refile(document, SecureInvoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        _linkWrites.Should().Equal((document, SecureInvoice, 1L));
        LinkOf(document).Should().Be(SecureInvoice);
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "filed under a Secure matter's invoice, the document is isolated by the resolver's reassignment");
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().NotBeNull(
            "the step after the re-file mirrors the secure matter's sharee onto the document now, not at the next two-minute pass");
    }

    [Fact(DisplayName = "#2159 slice 2: the re-file hands the reparent's answer to AfterRefileAsync, so a release the answer permits reaches the reconciler")]
    public async Task Refile_PassesTheReparentsAnswer_ToTheStepAfterTheRefile()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        var left = new RecordOwnershipParent("sprk_matter", SecureMatter);
        var permit = new SecureExitPermit(
            [left],
            new Sprk.Bff.Api.Services.Access.SecureRemovalDecision(
                Sprk.Bff.Api.Services.Access.SecureRemovalOutcome.Permitted, Sprk.Bff.Api.Services.Access.SecureRemovalBasis.FullAccess, Sharee),
            [new RecordOwnershipParent("sprk_matter", OrdinaryMatter)]);
        var logger = new ReconcilerLog();

        // The job acts for nobody, so the real resolver never permits a secure exit here; the answer is stamped with one so the
        // test can see whether the answer, not a bare call, reached the step (no relocator: the reconciler logs the permit).
        var result = await new InvoiceDocumentRefile(new PermitStamping(RealResolver(), permit),
                SecureChildShareWorld.ReconcilerOver(() => _world, new TokenHonouringShares(_shares), null!, logger: logger),
                WorldDocuments().Object, NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document), document, OrdinaryInvoice, CancellationToken.None);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        logger.Warnings.Should().Contain(m => m.Contains(document.ToString()) && m.Contains("left 1 secure record(s) with F3"),
            "the reconciler acts on the permit only when the re-file passes it the reparent's RecordOwnerResolution");
    }

    /// <summary>The real resolver, its answer stamped with <paramref name="permit"/> (a re-file that left a secure root with F3).</summary>
    private sealed class PermitStamping(IRecordOwnershipResolver inner, SecureExitPermit permit) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct) => inner.ResolveOwningTeamAsync(context, ct);

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct) => inner.ResolveOwnerAsync(context, ct);

        public async Task<RecordOwnerResolution> ReparentAsync(RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            await inner.ReparentAsync(request, applyChange, ct) with { SecureExitPermitted = permit };
    }

    /// <summary>The reconciler's warnings, formatted.</summary>
    private sealed class ReconcilerLog : Microsoft.Extensions.Logging.ILogger<Sprk.Bff.Api.Services.Access.SecureChildReconciler>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    [Fact(DisplayName = "050a review #1: a redelivery that finds the link ALREADY written writes no link and still re-derives the owner through the real resolver")]
    public async Task Refile_LinkAlreadyWritten_WritesNoLink_AndStillReDerivesTheOwner()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document, ("sprk_invoice", "sprk_invoice", SecureInvoice));

        var result = await Refile(document, SecureInvoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        _linkWriteAttempts.Should().Be(0, "the link already holds");
        LinkOf(document).Should().Be(SecureInvoice);
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "an earlier run that wrote the link but died before the owner moved is completed");
    }

    [Fact(DisplayName = "050a review #1: a document filed under ANOTHER invoice when the job runs is Refused document_already_invoiced: the link and the owner do not move")]
    public async Task Refile_DocumentFiledUnderAnotherInvoice_IsRefused_NothingMoves()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document, ("sprk_invoice", "sprk_invoice", OrdinaryInvoice));

        var result = await Refile(document, SecureInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentAlreadyInvoiced));
        LinkOf(document).Should().Be(OrdinaryInvoice, "another invoice's link is never overwritten");
        _linkWriteAttempts.Should().Be(0);
        _world.OwnerWrites.Should().BeEmpty();
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
    }

    [Fact(DisplayName = "050a review #1 (the review's scenario): a document filed under invoice X of a SECURE matter is never released: the real resolver refuses the job's reparent without F3, and the re-file refuses before it")]
    public async Task Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_TheRealResolverRefusesWithoutF3()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.SecureChild("sprk_document", document, ("sprk_invoice", "sprk_invoice", SecureInvoice));

        // The reparent exactly as the re-file sends it, against the document's TRUE prior parents (the link is no longer written by the
        // persister's transaction first): leaving the Secure matter needs F3, and the job acts for nobody.
        var applied = false;
        var resolution = await RealResolver().ReparentAsync(
            InvoiceDocumentRefile.ReparentRequestFor(document, OrdinaryInvoice),
            _ => { applied = true; return Task.CompletedTask; },
            CancellationToken.None);

        resolution.IsRefused.Should().BeTrue("a move out of a Secure record without a caller who holds F3 is refused");
        applied.Should().BeFalse("a refused re-file writes nothing");

        // And the re-file itself refuses before it asks: the document is another invoice's.
        var result = await Refile(document, OrdinaryInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentAlreadyInvoiced));
        LinkOf(document).Should().Be(SecureInvoice);
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam), "still isolated");
        _world.OwnerWrites.Should().BeEmpty();
        _linkWriteAttempts.Should().Be(0);
    }

    [Fact(DisplayName = "050a review #1: a filing that lands between the re-file's read and its link write fails the conditional write; the document is read again and the re-file refuses")]
    public async Task Refile_AFilingThatLandsBeforeTheLinkWrite_IsReadAgain_AndRefused()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        var filed = false;
        _beforeLinkWrite = id =>
        {
            if (!filed)
            {
                filed = true;
                SomeoneFiles(id, OrdinaryInvoice); // a document PUT, after the resolver read the row
            }
        };

        var result = await Refile(document, SecureInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentAlreadyInvoiced));
        _linkWriteAttempts.Should().Be(1, "the one write was refused (412); the re-read decided again");
        _linkWrites.Should().BeEmpty();
        LinkOf(document).Should().Be(OrdinaryInvoice, "the other writer's filing stands");
        _world.OwnerWrites.Should().BeEmpty("the conditional write failed before any owner assignment");
    }

    [Fact(DisplayName = "050a review #1: an unrelated write between the read and the link write is retried on the new version; the link lands once")]
    public async Task Refile_AnUnrelatedWriteBeforeTheLinkWrite_IsRetried()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _beforeLinkWrite = id =>
        {
            if (_linkWriteAttempts == 1)
            {
                _versions[id] = VersionOf(id) + 1; // e.g. a profiling status write
            }
        };

        (await Refile(document, OrdinaryInvoice)).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        _linkWriteAttempts.Should().Be(2);
        _linkWrites.Should().Equal((document, OrdinaryInvoice, 2L));
        LinkOf(document).Should().Be(OrdinaryInvoice);
    }

    [Fact(DisplayName = "050a: a document the resolver refuses to re-file (filed under a matter flagged secure but not isolated) is Refused with its code: no link, no owner, no mirror step")]
    public async Task Refile_WhenTheRealResolverRefuses_NothingIsWritten()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document, ("sprk_matter", "sprk_matter", FlaggedMatter));

        var result = await Refile(document, FlaggedInvoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refused);
        result.ReasonCode.Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _linkWriteAttempts.Should().Be(0);
        LinkOf(document).Should().BeNull();
        _world.OwnerWrites.Should().BeEmpty();
        _shares.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2095 / 050a: an isolation read that faults twice is IsolationUnreadable: no link write, no reparent, nothing written (the job retries)")]
    public async Task Refile_WhenTheIsolationReadFaultsTwice_WritesNothing()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        var faults = 0;
        _world.QueryFault = () =>
        {
            faults++;
            return new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        };
        _world.FailingQueriesOf("sprk_document");

        var result = await Refile(document, SecureInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.IsolationUnreadable, InvoiceDocumentRefile.IsolationUnreadableCode));
        faults.Should().Be(2, "the read, then its one retry");
        _linkWriteAttempts.Should().Be(0);
        _world.OwnerWrites.Should().BeEmpty();
        _world.StampWrites.Should().BeEmpty();
        _world.AccessRecordWrites.Should().BeEmpty();
        _world.AccessPermissionWrites.Should().BeEmpty();
        _world.Deletes.Should().BeEmpty();
        _shares.Writes.Should().BeEmpty();
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2095 K1 / 050a: AfterRefileAsync runs with CancellationToken.None - a job cancelled once the re-file has LANDED (link written, owner moved) still moves the share mirror")]
    public async Task Refile_CancelledAfterTheReFileLanded_StillRunsAfterRefile()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        using var cancel = new CancellationTokenSource();
        _afterReparent = cancel.Cancel; // the link write honours the token; the cancel lands after the resolver returned

        (await Refile(document, SecureInvoice, cancel.Token)).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        cancel.IsCancellationRequested.Should().BeTrue();
        LinkOf(document).Should().Be(SecureInvoice, "the re-file landed before the cancel");
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().NotBeNull(
            "the step after the re-file ran to completion although the job's token was cancelled (CancellationToken.None)");
    }

    [Fact(DisplayName = "050a review round 2, item 4 (the fake honours the token): a job cancelled BEFORE the link write sends no write: no link, no owner move, no mirror")]
    public async Task Refile_CancelledBeforeTheLinkWrite_WritesNothing()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        using var cancel = new CancellationTokenSource();
        _beforeLinkWrite = _ => cancel.Cancel();

        var act = () => Refile(document, SecureInvoice, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _linkWrites.Should().BeEmpty();
        LinkOf(document).Should().BeNull();
        _world.OwnerWrites.Should().BeEmpty();
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().BeNull("the step after the re-file never ran");
    }

    [Fact(DisplayName = "050a review round 2, item 4: an ALREADY-ISOLATED document (held under a Secure matter) re-filed under that same Secure matter's invoice: the link is written, it stays isolated, and its mirror is kept")]
    public async Task Refile_AlreadyIsolatedDocument_UnderItsOwnSecureMattersInvoice_StaysIsolated()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask); // its mirror

        var result = await Refile(document, SecureInvoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        _linkWrites.Should().Equal((document, SecureInvoice, 1L));
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _world.OwnerWrites.Should().BeEmpty("the owner did not change");
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().NotBeNull("still isolated: the mirror stays");
    }

    [Fact(DisplayName = "050a review round 2, item 4: an isolated document that no Secure record holds any more is never taken out of isolation by the job: the real resolver refuses (no F3 caller), nothing moves, the mirror stays - the only path where the isolation answer would decide anything is unreachable")]
    public async Task Refile_IsolatedDocumentWithNoSecureParent_IsNeverReleasedByTheJob()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.SecureChild("sprk_document", document);
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask); // the mirror of its old Secure record

        var result = await Refile(document, OrdinaryInvoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refused, "leaving isolation needs F3, and the job acts for nobody");
        _linkWriteAttempts.Should().Be(0);
        LinkOf(document).Should().BeNull();
        _world.OwnerWrites.Should().BeEmpty();
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().NotBeNull();
    }

    [Fact(DisplayName = "050a review round 2, item 3: a document rejected as not an invoice (a stalled reject) is Refused document_rejected: no link, no owner move")]
    public async Task Refile_RejectedDocument_IsRefused_NothingMoves()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _reviewStatus[document] = InvoiceReviewService.ReviewStatusRejectedNotInvoice;

        var result = await Refile(document, SecureInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, "ebilling.submission.document_rejected"));
        _linkWriteAttempts.Should().Be(0);
        LinkOf(document).Should().BeNull();
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review round 2, item 3 (must not fire): a document marked ConfirmedInvoice is re-filed")]
    public async Task Refile_ConfirmedDocument_IsRefiled()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        _reviewStatus[document] = InvoiceReviewService.ReviewStatusConfirmedInvoice;

        (await Refile(document, OrdinaryInvoice)).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        LinkOf(document).Should().Be(OrdinaryInvoice);
    }

    [Fact(DisplayName = "050a review #1: a document deleted before the job re-files it is Refused document_not_found and nothing is written")]
    public async Task Refile_DocumentGone_IsRefused()
    {
        Matters();

        var result = await Refile(Guid.NewGuid(), SecureInvoice);

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentNotFound));
        _linkWriteAttempts.Should().Be(0);
    }

    [Fact(DisplayName = "050a: a Dataverse fault of the reparent (its owner assignment) propagates; the after-steps turn it into a Retry")]
    public async Task Refile_WhenTheReparentFaults_ItPropagates()
    {
        Matters();
        var document = Guid.NewGuid();
        _world.OrdinaryChild("sprk_document", document);
        var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
        ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("owner assignment failed"));

        var act = () => new InvoiceDocumentRefile(ownership.Object, SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!),
                WorldDocuments().Object, NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document), document, SecureInvoice, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        _linkWriteAttempts.Should().Be(0);
    }

    [Fact(DisplayName = "050a: an intake-created document (its submission id is not derived from it) is NotApplicable: nothing is read or written")]
    public async Task Refile_ForAnIntakeDocument_IsNotApplicable()
    {
        var ownership = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);

        var result = await new InvoiceDocumentRefile(ownership.Object, SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!),
                records.Object, NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.NotApplicable);
        ownership.VerifyNoOtherCalls();
        records.VerifyNoOtherCalls();
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
        _intake.Uploads.Should().BeEmpty();
        _intake.DocumentCreates.Should().BeEmpty();
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
