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
/// (conditional on its version, retried on an unrelated change). The submission job is the only creator of <c>sprk_invoice</c>
/// (ADR-002) and re-files the document under that invoice (<see cref="InvoiceDocumentRefile"/>, whose isolation tests are the
/// "#2095" section below — they moved here with the calls from the old confirm). A reject refuses a document that is linked to
/// an invoice or has a confirm submission.</para>
/// <para><b>The live schema</b> (spaarkedev1, read-only metadata, 2026-10-01): the link is <c>sprk_document.sprk_invoice</c>;
/// the notes column is <c>sprk_invoicereviewnotes</c> (<c>sprk_invoicerejectionnotes</c> does not exist).</para>
/// <para>Substitution is at the Dataverse service seams (<see cref="IFieldMappingDataverseService"/> as a stateful double; the
/// intake's own seams through <see cref="InvoiceSubmissionServiceTests.Harness"/>), the ownership resolver and the queue's method
/// boundary. The re-file runs the REAL <see cref="SecureChildReconciler"/> over an in-memory Dataverse. No HTTP handler is mocked
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

    // #2095: the REAL reconciler (its real synchronizer and ownership resolver) over an in-memory Dataverse. A document the
    // world does not hold reads as not isolated.
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly Mock<IRecordOwnershipResolver> _ownership = new(MockBehavior.Strict);

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
    // #2095 / 050a (D-134 item 3) — the job re-files a confirmed document under its invoice: isolation read, then
    // ReparentAsync, then SecureChildReconciler.AfterRefileAsync (moved here from the old confirm with its calls)
    // =========================================================================================

    private static readonly Guid SecureMatter = Guid.Parse("20950000-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryMatter = Guid.Parse("20950000-0000-4000-8000-000000000002");
    private static readonly Guid Sharee = Guid.Parse("20950000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceTeam = Guid.Parse("7e4a0000-0000-4000-8000-000000000130");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    /// <summary>The re-file of <paramref name="document"/>'s confirm submission under <paramref name="invoice"/>.</summary>
    private Task<InvoiceDocumentRefileResult> Refile(Guid document, Guid invoice, CancellationToken ct = default) =>
        new InvoiceDocumentRefile(_ownership.Object, SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!),
                NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document), document, invoice, ct);

    /// <summary>
    /// The reparent as Dataverse leaves it: the document's owner moved to the secure team (<paramref name="intoSecure"/>) or the
    /// ordinary business unit's team, its matter lookup on <paramref name="matter"/> (the invoice's matter). Records each request.
    /// </summary>
    private List<RecordReparent> TheReparentMovesTheDocumentTo(Guid document, Guid matter, bool intoSecure, Action? after = null)
    {
        var requests = new List<RecordReparent>();
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RecordReparent request, Func<CancellationToken, Task> apply, CancellationToken token) =>
            {
                requests.Add(request);
                await apply(token);
                _world.Set("sprk_document", document, "sprk_matter", new EntityReference("sprk_matter", matter));
                _world.MoveOwner("sprk_document", document, DataversePrincipalRef.Team(
                    intoSecure ? SecureChildShareWorld.SecureTeam : SecureChildShareWorld.GeneralTeam));
                after?.Invoke();
                return RecordOwnerResolution.Owned(InvoiceTeam);
            });
        return requests;
    }

    [Fact(DisplayName = "050a: the re-file is ONE reparent of the document with the invoice as its inherited parent and no column change (the persister linked it)")]
    public async Task Refile_IsAReparentUnderTheInvoice_WithNoColumnChange()
    {
        var document = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var requests = TheReparentMovesTheDocumentTo(document, OrdinaryMatter, intoSecure: false);

        var result = await Refile(document, invoice);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);
        var request = requests.Should().ContainSingle().Subject;
        request.EntityLogicalName.Should().Be("sprk_document");
        request.RecordId.Should().Be(document);
        request.ParentChanges.Should().BeEmpty("the link is already written by the persister's transaction");
        request.InheritedParents.Should().Equal(new RecordOwnershipParent("sprk_invoice", invoice));
    }

    [Fact(DisplayName = "#2095 / 050a: re-filing a Secure-owned document under an ordinary invoice runs AfterRefileAsync with wasIsolated=true (read BEFORE the reparent)")]
    public async Task Refile_AnIsolatedDocumentMovedOutOfSecure_RunsAfterRefile_WithWasIsolatedTrue()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask);
        TheReparentMovesTheDocumentTo(document, OrdinaryMatter, intoSecure: false);

        (await Refile(document, Guid.NewGuid())).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        // The mirror comes off only for a row that WAS isolated, and the step reads that BEFORE the write: read after the
        // reparent the document is ordinary (wasIsolated=false) and its share would stay. So the share going proves the order
        // (isolation read, then reparent, then AfterRefileAsync) and that the step ran for THIS document with wasIsolated=true.
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the secure matter's sharee is taken off a document that left every Secure record (owner round 22)");
    }

    [Fact(DisplayName = "#2095 / 050a: re-filing an ordinary document under a Secure matter's invoice runs AfterRefileAsync with wasIsolated=false (mirror created)")]
    public async Task Refile_AnOrdinaryDocumentMovedIntoSecure_RunsAfterRefile_WithWasIsolatedFalse()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .OrdinaryChild("sprk_document", document, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        TheReparentMovesTheDocumentTo(document, SecureMatter, intoSecure: true);

        (await Refile(document, Guid.NewGuid())).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().NotBeNull(
            "the document moved under a Secure matter is shared with that matter's sharees now, not at the next two-minute pass");
    }

    [Fact(DisplayName = "#2095 / 050a: an isolation read that faults twice is IsolationUnreadable: NO reparent and nothing written (the job retries)")]
    public async Task Refile_WhenTheIsolationReadFaultsTwice_WritesNothing()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter)
            .SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        var faults = 0;
        _world.QueryFault = () =>
        {
            faults++;
            return new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        };
        _world.FailingQueriesOf("sprk_document");

        var result = await Refile(document, Guid.NewGuid());

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.IsolationUnreadable, InvoiceDocumentRefile.IsolationUnreadableCode));
        faults.Should().Be(2, "the read, then its one retry");
        _ownership.Verify(o => o.ReparentAsync(
            It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        _world.OwnerWrites.Should().BeEmpty();
        _world.StampWrites.Should().BeEmpty();
        _world.AccessRecordWrites.Should().BeEmpty();
        _world.AccessPermissionWrites.Should().BeEmpty();
        _world.Deletes.Should().BeEmpty();
        _shares.Writes.Should().BeEmpty();
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2095 K1 / 050a: AfterRefileAsync runs with CancellationToken.None - a job cancelled after the reparent still moves the share mirror")]
    public async Task Refile_CancelledAfterTheReparent_StillRunsAfterRefile()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask);
        using var cancel = new CancellationTokenSource();
        TheReparentMovesTheDocumentTo(document, OrdinaryMatter, intoSecure: false, after: cancel.Cancel);

        (await Refile(document, Guid.NewGuid(), cancel.Token)).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        cancel.IsCancellationRequested.Should().BeTrue();
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the step after the re-file ran to completion although the job's token was cancelled (CancellationToken.None)");
    }

    [Fact(DisplayName = "#2095 / 050a: a refused reparent is Refused with its code, and AfterRefileAsync does not run")]
    public async Task Refile_WhenTheReparentIsRefused_DoesNotRunAfterRefile()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused("record_owner_test", "refused for the test"));

        var result = await Refile(document, Guid.NewGuid());

        result.Should().Be(new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, "record_owner_test"));
        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask,
            "nothing was re-filed, so the step did not run");
    }

    [Fact(DisplayName = "050a: a reparent that reports ParentsUnchanged does not run AfterRefileAsync")]
    public async Task Refile_WhenParentsAreUnchanged_DoesNotRunAfterRefile()
    {
        var document = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", SecureMatter).OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .SecureChild("sprk_document", document, ("sprk_matter", "sprk_matter", SecureMatter));
        _shares.Seed("sprk_document", document, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (RecordReparent _, Func<CancellationToken, Task> apply, CancellationToken token) =>
            {
                await apply(token);
                // As if the owner had moved: only ParentsUnchanged keeps the step from running, so the share staying proves it.
                _world.MoveOwner("sprk_document", document, DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
                return RecordOwnerResolution.Owned(InvoiceTeam) with { ParentsUnchanged = true };
            });

        (await Refile(document, Guid.NewGuid())).Status.Should().Be(InvoiceDocumentRefileStatus.Refiled);

        _shares.MaskOf("sprk_document", document, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact(DisplayName = "050a: a Dataverse fault of the reparent (its owner assignment) propagates; the after-steps turn it into a Retry")]
    public async Task Refile_WhenTheReparentFaults_ItPropagates()
    {
        var document = Guid.NewGuid();
        _ownership.Setup(o => o.ReparentAsync(
                It.IsAny<RecordReparent>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("owner assignment failed"));

        var act = () => Refile(document, Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact(DisplayName = "050a: an intake-created document (its submission id is not derived from it) is NotApplicable: nothing is read or written")]
    public async Task Refile_ForAnIntakeDocument_IsNotApplicable()
    {
        var result = await new InvoiceDocumentRefile(_ownership.Object, SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!),
                NullLogger<InvoiceDocumentRefile>.Instance)
            .RefileAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Status.Should().Be(InvoiceDocumentRefileStatus.NotApplicable);
        _ownership.VerifyNoOtherCalls();
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
