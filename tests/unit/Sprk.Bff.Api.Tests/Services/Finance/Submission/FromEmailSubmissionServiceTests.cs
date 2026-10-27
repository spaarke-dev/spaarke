using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="FromEmailSubmissionService"/> (ebilling-r1 task 095). It covers the order of the refusals (every one before the save writes
/// nothing), the replay of K and EB-D-160's re-attempt after Failed, the email-only save request, and the <c>.eml</c>'s resolution: a
/// fresh save, or the caller's earlier save through its communication. The HTTP shape of the same rows is the route's contract suite.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the intake's guarantees for the Outlook channel.</remarks>
public class FromEmailSubmissionServiceTests
{
    private static Task<FromEmailSubmissionOutcome> Run(W world, FromEmailSubmissionInput? input = null) =>
        world.Build().SubmitAsync(input ?? W.Input(), "office-user", W.Http(), CancellationToken.None);

    private static void RefusedBeforeAnyWrite(W world)
    {
        world.Save.Requests.Should().BeEmpty("a refusal before the save writes nothing");
        world.Operation.Submits.Should().BeEmpty();
        world.Operation.Requeues.Should().BeEmpty();
    }

    // ── a fresh submission ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fresh_SavesTheEmailAlone_ThenSubmitsThatEml_WithEachMarkedPartsOrdinalAndSha_AsGenerationOne()
    {
        var world = new W();

        var outcome = await Run(world, W.Input(number: "  1001 ") with { PoNumber = " PO-7 " });

        var k = W.K();
        outcome.Receipt.Should().Be(new FromEmailReceipt(
            FromEmailSubmissionKey.SubmissionIdFor(k, 1), FromEmailSubmissionKey.JobIdFor(k, 1), "1001.1", W.EmlId));

        var save = world.Save.Requests.Should().ContainSingle().Subject;
        save.ContentType.Should().Be(SaveContentType.Email);
        save.TargetEntity!.EntityType.Should().Be("Matter");
        save.TargetEntity.EntityId.Should().Be(W.MatterId);
        save.Email!.SelectedAttachmentFileNames.Should().NotBeNull().And.BeEmpty("model 9.1 step 1: no attachment Documents");
        save.Email.Attachments.Should().HaveCount(2, "the .eml carries every attachment");
        save.IdempotencyKey.Should().BeNull();

        var submit = world.Operation.Submits.Should().ContainSingle().Subject;
        submit.SourceEmlDocumentId.Should().Be(W.EmlId);
        submit.CommunicationId.Should().Be(W.CommunicationId);
        submit.Generation.Should().Be(1);
        submit.FirmInvoiceNumber.Should().Be("1001");
        submit.PoNumber.Should().Be("PO-7");
        submit.CallerSystemUserId.Should().Be(W.CallerId);
        submit.Mode.Should().Be("New");
        submit.MarkedParts.Select(p => (p.Ordinal, p.FileName, p.Mark, p.Sha256)).Should().Equal(
            (0, "Invoice 1001.pdf", "Invoice", EmlMimeParts.Sha256Hex(W.PdfBytes)),
            (1, "Timesheet.xlsx", "Supporting", EmlMimeParts.Sha256Hex(W.XlsxBytes)));

        // Task 092: the parts are staged in the file store; the Invoice under the id 092's upload stages its PDF under.
        var submissionId = FromEmailSubmissionKey.SubmissionIdFor(k, 1);
        submit.InvoiceStoredFileId.Should().Be(Sprk.Bff.Api.Services.Finance.Files.StoredFileIds.ForUpload(submissionId));
        submit.MarkedParts[1].StoredFileId.Should().Be(Sprk.Bff.Api.Services.Finance.Files.StoredFileIds.ForAttachment(submissionId, "eml-part-1"));
    }

    [Fact]
    public async Task Fresh_TheStoredFileIds_AreNewForEachGeneration()
    {
        var world = new W();
        var k = W.K();
        world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);

        await Run(world);

        world.Operation.Submits.Single().InvoiceStoredFileId.Should().Be(
            Sprk.Bff.Api.Services.Finance.Files.StoredFileIds.ForUpload(FromEmailSubmissionKey.SubmissionIdFor(k, 2)),
            "a re-attempt after Failed stages its own files, never over the failed attempt's");
    }

    [Fact]
    public async Task Replay_AnAttemptWithNoRecordedSource_Is503Failed()
    {
        var world = new W();
        var row = world.AddAttempt(W.K(), 1, InvoiceSubmissionStatus.Processing);
        world.Operation.Sources.Remove(row.Id);

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.failed");
        world.Save.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Replay_TheSourceReadFaults_Is503RecordUnavailable()
    {
        var world = new W();
        world.AddAttempt(W.K(), 1, InvoiceSubmissionStatus.Processing);
        world.Operation.SourceReadFailure = new TimeoutException();

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.record_unavailable");
        world.Save.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Fresh_ADontSaveAttachment_IsStillInTheEml_ButNotAMarkedPart()
    {
        var world = new W();

        await Run(world, W.Input(new[] { W.Mark(W.Invoice, "Invoice"), W.Mark(W.Timesheet, "DontSave") }));

        world.Operation.Submits.Single().MarkedParts.Select(p => p.Mark).Should().Equal("Invoice");
    }

    // ── refusals before any write, in the contract's order ──────────────────────────────────────────────────────

    [Fact]
    public async Task Marks_AreCheckedFirst_400MarksInvalid_WithReasonAndFileName()
    {
        var world = new W { Caller = Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("x") };

        var outcome = await Run(world, W.Input(new[] { W.Mark(W.Invoice, "Invoice") }, number: null, mode: "Correction"));

        outcome.Status.Should().Be(400);
        outcome.ErrorCode.Should().Be("ebilling.submission.marks_invalid");
        outcome.Extensions.Should().Contain("reason", "unmarked_attachment").And.Contain("fileName", "Timesheet.xlsx");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Bytes_AMarkedFileWithoutContent_Is422MarkedFileMissing_WithFileNameAndMark()
    {
        var world = new W();
        var noBytes = W.Timesheet with { ContentBase64 = null };

        var outcome = await Run(world, W.Input(email: W.Email(W.Invoice, noBytes)));

        outcome.Status.Should().Be(422);
        outcome.ErrorCode.Should().Be("ebilling.submission.marked_file_missing");
        outcome.Extensions.Should().Contain("fileName", "Timesheet.xlsx").And.Contain("mark", "Supporting").And.NotContainKey("reason");
        RefusedBeforeAnyWrite(world);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Number_Missing_Is400FirmInvoiceNumberRequired(string? number)
    {
        var world = new W();

        var outcome = await Run(world, W.Input(number: number));

        (outcome.Status, outcome.ErrorCode).Should().Be((400, "ebilling.submission.firm_invoice_number_required"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Correction_Is409CorrectionNotAllowed_NotAvailable_UntilTask097()
    {
        var world = new W();

        var outcome = await Run(world, W.Input(mode: "Correction") with { InvoiceId = Guid.NewGuid() });

        (outcome.Status, outcome.ErrorCode).Should().Be((409, "ebilling.submission.correction_not_allowed"));
        outcome.Extensions.Should().Contain("reason", "not_available");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Firm_Empty_Is400FirmInvalid()
    {
        var world = new W();

        var outcome = await Run(world, W.Input(firm: Guid.Empty));

        (outcome.Status, outcome.ErrorCode).Should().Be((400, "ebilling.submission.firm_invalid"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Firm_Inactive_Is400FirmInvalid()
    {
        var world = new W();
        world.Intake.Firm!["statecode"] = new OptionSetValue(1);

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.firm_invalid");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Firm_ReadFault_Is503RecordUnavailable_TheContractsCode()
    {
        var world = new W();
        world.Intake.FirmReadFailure = new TimeoutException();

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((503, "ebilling.submission.record_unavailable"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Firm_NotLinkedToTheRecord_Is403FirmNotLinked()
    {
        var world = new W();
        world.Intake.Links[("sprk_matter", W.MatterId)] = new[] { Guid.NewGuid() };

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((403, "ebilling.submission.firm_not_linked"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Firm_LinkUnreadable_Is503RecordUnavailable_TheContractsCode()
    {
        var world = new W();
        world.Intake.LinksUnreadable = true;

        (await Run(world)).Should().Match<FromEmailSubmissionOutcome>(o => o.Status == 503 && o.ErrorCode == "ebilling.submission.record_unavailable");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Operation_NotYetAvailable_Is503Failed_BeforeTheSave()
    {
        var world = new W();
        world.Operation.Available = false;

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((503, "ebilling.submission.failed"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Caller_WithoutASystemUser_Is503RecordUnavailable()
    {
        var world = new W { Caller = Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("lookup-failed") };

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.record_unavailable");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task TheRealNotYetAvailableSeam_IsUnavailable_AndThrowsIfCalled()
    {
        var seam = new SubmitInvoiceFromEmailNotYetAvailable();

        seam.IsAvailable.Should().BeFalse();
        await FluentActions.Awaiting(() => seam.RequeueAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None))
            .Should().ThrowAsync<SubmitInvoiceFromEmailNotYetAvailableException>();
    }

    // ── the replay of K (contract section 5) and EB-D-160 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(InvoiceSubmissionStatus.Received, true)]
    [InlineData(InvoiceSubmissionStatus.Processing, false)]
    [InlineData(InvoiceSubmissionStatus.Accepted, false)]
    public async Task Replay_TheCallersLatestAttempt_Is202WithTheSameBody_AndAReceivedOneIsRequeued(InvoiceSubmissionStatus status, bool requeued)
    {
        var world = new W();
        var k = W.K();
        var earlierEml = Guid.NewGuid();
        var row = world.AddAttempt(k, 1, status, eml: earlierEml, tracking: "1001.4");

        var outcome = await Run(world);

        outcome.Receipt.Should().Be(new FromEmailReceipt(row.Id, FromEmailSubmissionKey.JobIdFor(k, 1), "1001.4", earlierEml));
        world.Operation.Requeues.Should().Equal(requeued ? new[] { (row.Id, FromEmailSubmissionKey.JobIdFor(k, 1)) } : Array.Empty<(Guid, Guid)>());
        world.Save.Requests.Should().BeEmpty("a replay saves nothing again");
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Replay_AReceivedAttemptThatCannotBeRequeued_Is503Failed()
    {
        var world = new W();
        world.AddAttempt(W.K(), 1, InvoiceSubmissionStatus.Received);
        world.Operation.Requeued = false;

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.failed");
    }

    [Fact]
    public async Task Replay_Rejected_Is409SubmissionAlreadyEnded_D139()
    {
        var world = new W();
        world.AddAttempt(W.K(), 1, InvoiceSubmissionStatus.Rejected);

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((409, "ebilling.submission.submission_already_ended"));
        RefusedBeforeAnyWrite(world);
    }

    [Theory]
    [InlineData(InvoiceSubmissionStatus.Received)]
    [InlineData(InvoiceSubmissionStatus.Failed)]
    public async Task Replay_AnotherPersonsAttemptOfK_Is409DocumentAlreadySubmitted_OP5(InvoiceSubmissionStatus status)
    {
        var world = new W();
        world.AddAttempt(W.K(), 1, status, creator: W.OtherCallerId);

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((409, "ebilling.submission.document_already_submitted"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Replay_TheSameKWithADifferentInvoiceFile_Is409DocumentAlreadySubmitted()
    {
        var world = new W();
        var row = world.AddAttempt(W.K(), 1, InvoiceSubmissionStatus.Accepted);
        world.Operation.Sources[row.Id] = new FromEmailAttemptSource(W.EmlId, new string('0', 64));

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.document_already_submitted");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task ReAttempt_AfterFailed_IsANewSubmission_WithGenerationTwosIds_EBD160()
    {
        var world = new W();
        var k = W.K();
        world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);

        var outcome = await Run(world);

        outcome.Receipt!.SubmissionId.Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 2));
        outcome.Receipt.JobId.Should().Be(FromEmailSubmissionKey.JobIdFor(k, 2));
        outcome.Receipt.TrackingNumber.Should().Be("1001.2", "the allocator gives the new attempt a new suffix");
        world.Operation.Submits.Single().Generation.Should().Be(2);
    }

    [Fact]
    public async Task ReAttempt_WalksEveryFailedGeneration_ToTheFirstUnused()
    {
        var world = new W();
        var k = W.K();
        world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);
        world.AddAttempt(k, 2, InvoiceSubmissionStatus.Failed);

        (await Run(world)).Receipt!.SubmissionId.Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 3));
    }

    [Fact]
    public async Task ReAttempt_AfterTheLastAllowedGeneration_Is409SubmissionAlreadyEnded()
    {
        var world = new W();
        var k = W.K();
        for (var g = 1; g <= FromEmailSubmissionKey.MaxGeneration; g++)
        {
            world.AddAttempt(k, g, InvoiceSubmissionStatus.Failed);
        }

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.submission_already_ended");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task ReAttempt_TheLatestOfAFailedChainIsAnswered_SoAReceivedGenerationTwoIsReplayed()
    {
        var world = new W();
        var k = W.K();
        world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);
        world.AddAttempt(k, 2, InvoiceSubmissionStatus.Processing, tracking: "1001.2");

        var outcome = await Run(world);

        outcome.Receipt!.SubmissionId.Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 2));
        outcome.Receipt.TrackingNumber.Should().Be("1001.2");
        world.Save.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentRetry_TheOperationsAlreadyCreated_IsAnsweredAsAReplayOfThatAttempt()
    {
        var world = new W();
        var k = W.K();
        world.AddAttempt(k, 1, InvoiceSubmissionStatus.Failed);
        world.Operation.Answer = input =>
        {
            // The other retry created generation 2 first (the allocator's alternate key refused this create).
            world.AddAttempt(k, input.Generation, InvoiceSubmissionStatus.Received, tracking: "1001.2", eml: W.EmlId);
            return FromEmailSubmitResult.AlreadyCreated();
        };

        var outcome = await Run(world);

        outcome.Receipt.Should().Be(new FromEmailReceipt(
            FromEmailSubmissionKey.SubmissionIdFor(k, 2), FromEmailSubmissionKey.JobIdFor(k, 2), "1001.2", W.EmlId));
        world.Operation.Submits.Should().ContainSingle("exactly one new attempt is created");
    }

    [Fact]
    public async Task Replay_AnAttemptReadFault_Is503RecordUnavailable()
    {
        var world = new W { AttemptReadFailure = new TimeoutException() };

        (await Run(world)).Should().Match<FromEmailSubmissionOutcome>(o => o.Status == 503 && o.ErrorCode == "ebilling.submission.record_unavailable");
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Operation_Refused_IsPassedThrough()
    {
        var world = new W();
        world.Operation.Answer = _ => FromEmailSubmitResult.Refused(409, "ebilling.submission.document_already_invoiced", "already");

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((409, "ebilling.submission.document_already_invoiced"));
    }

    // ── the communication's filing (409 before the save) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Communication_FiledUnderAnotherRecord_Is409DocumentCoreRecordMismatch_BeforeTheSave()
    {
        var world = new W { Communication = new Entity("sprk_communication", W.CommunicationId) };
        world.Intake.Communications[W.CommunicationId]["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((409, "ebilling.submission.document_core_record_mismatch"));
        RefusedBeforeAnyWrite(world);
    }

    [Fact]
    public async Task Communication_LookupFault_Is503RecordUnavailable_BeforeTheSave()
    {
        var world = new W { CommunicationLookupFailure = new TimeoutException() };

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.record_unavailable");
        RefusedBeforeAnyWrite(world);
    }

    // ── the save's outcome ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_NoCommunication_Is422EmailUnidentifiable_NoCommunication()
    {
        var world = new W();
        world.Save.Answer = new SubmissionEmailSaveOutcome { Refusal = SubmissionEmailSaveRefusal.NoCommunication };

        var outcome = await Run(world);

        (outcome.Status, outcome.ErrorCode).Should().Be((422, "ebilling.submission.email_unidentifiable"));
        outcome.Extensions.Should().Contain("reason", "no_communication");
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_CommunicationUnavailable_Is503()
    {
        var world = new W();
        world.Save.Answer = new SubmissionEmailSaveOutcome { Refusal = SubmissionEmailSaveRefusal.CommunicationUnavailable };

        (await Run(world)).Status.Should().Be(503);
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_AnOfficeFailure_IsPassedThroughAsTheOfficeError()
    {
        var world = new W();
        var error = new SaveError { Code = "OFFICE_012", Message = "Upload failed", Retryable = true };
        world.Save.Answer = new SubmissionEmailSaveOutcome { Response = new SaveResponse { Success = false, Error = error } };

        (await Run(world)).OfficeError.Should().Be(error);
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ResolvedByContentToAnotherDocument_Is422EmlAmbiguous()
    {
        var world = new W();
        world.Save.Answer = new SubmissionEmailSaveOutcome
        {
            Response = new SaveResponse { Success = true }, EmlDocumentId = Guid.NewGuid(), CommunicationId = W.CommunicationId,
            ContentDuplicate = true, EmlParts = Array.Empty<EmlPart>(),
        };

        (await Run(world)).Extensions.Should().Contain("reason", "eml_ambiguous");
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_AFreshEmlWithoutAMarkedPart_Is503Failed_ADefect()
    {
        var world = new W();
        world.Save.Answer = new SubmissionEmailSaveOutcome
        {
            Response = new SaveResponse { Success = true }, EmlDocumentId = W.EmlId, CommunicationId = W.CommunicationId,
            EmlParts = new[] { new EmlPart(0, "Invoice 1001.pdf", EmlMimeParts.Sha256Hex(W.PdfBytes)) },
        };

        (await Run(world)).ErrorCode.Should().Be("ebilling.submission.failed");
        world.Operation.Submits.Should().BeEmpty();
    }

    // ── the caller's earlier save (the save's duplicate, model 9.1 step 1a) ─────────────────────────────────────

    private static W DuplicateWorld()
    {
        var world = new W { Communication = new Entity("sprk_communication", W.CommunicationId) };
        world.Save.Answer = W.FakeSave.Duplicate();
        return world;
    }

    [Fact]
    public async Task Duplicate_TheCallersOneEarlierEmlCarryingTheMarkedParts_IsTheSource()
    {
        var world = DuplicateWorld();
        var earlier = Guid.NewGuid();
        world.AddEarlierEml(earlier, null, W.Invoice, W.Timesheet);

        var outcome = await Run(world);

        outcome.Receipt!.EmlDocumentId.Should().Be(earlier);
        world.Operation.Submits.Single().MarkedParts.Select(p => p.Ordinal).Should().Equal(0, 1);

        var query = world.Queries.Should().ContainSingle().Subject;
        query.EntityName.Should().Be("sprk_document");
        query.Criteria.Conditions.Select(c => (c.AttributeName, c.Operator)).Should().BeEquivalentTo(new[]
        {
            ("sprk_isemailarchive", ConditionOperator.Equal), ("statecode", ConditionOperator.Equal),
            ("sprk_createdbyperson", ConditionOperator.Equal), ("sprk_matter", ConditionOperator.Equal),
            ("sprk_parentdocument", ConditionOperator.Null), ("sprk_relatedcommunication", ConditionOperator.Equal),
        });
        query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_createdbyperson").Values.Should().Equal(W.CallerId);
    }

    [Fact]
    public async Task Duplicate_AnEarlierEmlStillProvisionallyRestricted_Is422EarlierSaveUnlinked()
    {
        var world = DuplicateWorld();
        world.AddEarlierEml(Guid.NewGuid(), AccessInheritance.OwnNarrowingUntilEmailIdentified(100000002).Serialize(), W.Invoice, W.Timesheet);

        (await Run(world)).Extensions.Should().Contain("reason", "earlier_save_unlinked");
        world.Operation.Submits.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_NoLinkedCandidate_ButAnUnlinkedEarlierSave_Is422EarlierSaveUnlinked()
    {
        var world = DuplicateWorld();
        world.UnlinkedSaves.Add(new Entity("sprk_document", Guid.NewGuid()));

        (await Run(world)).Extensions.Should().Contain("reason", "earlier_save_unlinked");
    }

    [Fact]
    public async Task Duplicate_NoCandidateAtAll_Is422EmlAmbiguous()
    {
        (await Run(DuplicateWorld())).Extensions.Should().Contain("reason", "eml_ambiguous");
    }

    [Fact]
    public async Task Duplicate_TwoCandidatesCarryingTheParts_Is422EmlAmbiguous()
    {
        var world = DuplicateWorld();
        world.AddEarlierEml(Guid.NewGuid(), null, W.Invoice, W.Timesheet);
        world.AddEarlierEml(Guid.NewGuid(), null, W.Timesheet, W.Invoice);

        (await Run(world)).Extensions.Should().Contain("reason", "eml_ambiguous");
    }

    [Fact]
    public async Task Duplicate_ACandidateMissingAMarkedPart_IsNotTheSource()
    {
        var world = DuplicateWorld();
        world.AddEarlierEml(Guid.NewGuid(), null, W.Invoice);

        (await Run(world)).Extensions.Should().Contain("reason", "eml_ambiguous");
    }

    [Theory]
    [InlineData(true, "earlier_save_unlinked")]
    [InlineData(false, "no_communication")]
    public async Task Duplicate_WithNoCommunicationForTheMessage_IsEarlierSaveUnlinkedOrNoCommunication(bool unlinkedSave, string reason)
    {
        var world = new W();
        world.Save.Answer = W.FakeSave.Duplicate();
        if (unlinkedSave)
        {
            world.UnlinkedSaves.Add(new Entity("sprk_document", Guid.NewGuid()));
        }

        (await Run(world)).Extensions.Should().Contain("reason", reason);
    }
}
