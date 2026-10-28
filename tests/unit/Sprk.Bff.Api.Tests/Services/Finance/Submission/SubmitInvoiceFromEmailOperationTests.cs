using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 094: <see cref="SubmitInvoiceFromEmailOperation"/>, the Submit Invoice operation behind 095's from-email seam (model
/// 9.1; uac-r2 K5 hand-offs). The intake's checks are the REAL <see cref="InvoiceSubmissionService"/> over
/// <see cref="InvoiceSubmissionServiceTests.Harness"/>, the parts are read by the REAL <see cref="EmlMarkedPartReader"/> out of a real
/// <c>.eml</c>, and they are staged in the REAL file store over an in-memory blob gateway. Step 1 and step A (the caller's Read and the
/// filing, which need an HTTP pipeline) are the one seam replaced.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): these pin the operation's contract with 095 (the row's shape, the creator person on every generation, the
/// order of step A against the read-only checks and the first write, and the no-compensation rule for deterministic ids).
/// </remarks>
public sealed class SubmitInvoiceFromEmailOperationTests
{
    private const long EmlVersion = 4711L;
    private const string K = "k-094-operation";

    private readonly W _world = new();
    private readonly Mock<IGenericEntityService> _emlRows = new();
    private Entity? _eml;

    private InvoiceSubmissionServiceTests.Harness H => _world.Intake;

    public SubmitInvoiceFromEmailOperationTests()
    {
        // As the real resolver does: the creator person is the RequestedBy systemuser the intake hands it.
        H.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecordOwnershipContext c, CancellationToken _) =>
            {
                H.OwnerContexts.Add(c);
                return H.OwnerAnswer with { CreatedByPerson = c.RequestedBy?.SystemUserId };
            });
        StoreEml(W.Invoice, W.Timesheet);
    }

    private void StoreEml(params Sprk.Bff.Api.Models.Office.AttachmentReference[] attachments)
    {
        _eml = _world.AddEarlierEml(W.EmlId, null, attachments);
        _eml["versionnumber"] = EmlVersion;
        _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(_eml);
    }

    /// <summary>The operation with step 1 and step A replaced (the only seam).</summary>
    private sealed class Operation(
        InvoiceSubmissionService submissions, ITrackingNumberAllocator allocator, StoredFileService files, Sprk.Bff.Api.Services.Jobs.JobSubmissionService jobs,
        EmlMarkedPartReader reader)
        : SubmitInvoiceFromEmailOperation(submissions, allocator, files, jobs, reader, NullLogger<SubmitInvoiceFromEmailOperation>.Instance)
    {
        public IResult? StepOne;
        public Func<CancellationToken, Task<bool>>? StepA;
        public int PrepareCalls;

        internal override Task<(IResult? Refusal, Func<CancellationToken, Task<bool>>? FileSource)> PrepareSourceAsync(
            FromEmailSubmitInput input, CancellationToken ct)
        {
            PrepareCalls++;
            return Task.FromResult((StepOne, StepA));
        }
    }

    private Operation Build() => new(H.Build(), H.Allocator.Object, H.Files.Service, H.Jobs.Object,
        new EmlMarkedPartReader(_emlRows.Object, _world.Spe.Object, NullLogger<EmlMarkedPartReader>.Instance));

    private static FromEmailMarkedPart Part(int ordinal, string name, byte[] bytes, string mark) =>
        new(ordinal, name, EmlMimeParts.Sha256Hex(bytes), mark);

    private static FromEmailSubmitInput Input(int generation = 1, Guid? caller = null)
    {
        var submissionId = FromEmailSubmissionKey.SubmissionIdFor(K, generation);
        var parts = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, FromEmailMarks.Invoice), Part(1, "Timesheet.xlsx", W.XlsxBytes, FromEmailMarks.Supporting) }
            .Select(p => p with { StoredFileId = FromEmailSubmissionKey.StoredFileIdFor(submissionId, p) })
            .ToList();
        return new FromEmailSubmitInput
        {
            SourceEmlDocumentId = W.EmlId,
            CommunicationId = W.CommunicationId,
            CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, W.MatterId),
            FirmId = W.FirmId,
            FirmInvoiceNumber = "1001",
            PoNumber = " PO-7 ",
            MarkedParts = parts,
            SubmissionId = submissionId,
            JobId = FromEmailSubmissionKey.JobIdFor(K, generation),
            Generation = generation,
            Mode = FromEmailModes.New,
            CallerSystemUserId = caller ?? W.CallerId,
            CorrelationId = "trace-094",
        };
    }

    private InMemoryBlobGateway.Blob StagedOf(Guid submissionId, StoredFilePurpose purpose, Guid fileId)
    {
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId));
        return H.Files.Gateway.Get(StoredFileSchema.ContainerOf(purpose), StoredFileBlobName.Build(H.Files.TenantId, parent, fileId))!;
    }

    /// <summary>A refusal as the submission routes shape it (<see cref="InvoiceSubmissionHttp.Refused"/>), or another route's ProblemDetails.</summary>
    private static IResult Problem(int status, string? errorCode) => errorCode is null
        ? TypedResults.Problem(detail: "refused", statusCode: status)
        : InvoiceSubmissionHttp.Refused(status, errorCode, "refused");

    // ═══════════════════════════════════════════════════════════════════════════
    // Received
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Submit_StagesEveryPartWithItsSource_AndCreatesThisIdWithNoDocument_ChannelInternal_CreatedByTheCaller()
    {
        var input = Input();

        var result = await Build().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        result.TrackingNumber.Should().Be("INV-1.1");

        // The Invoice part: under ForUpload(SubmissionId), Invoice PDF, the future invoice as parent; its source and the Supporting list.
        var invoice = StagedOf(input.SubmissionId, StoredFilePurpose.InvoicePdf, StoredFileIds.ForUpload(input.SubmissionId));
        invoice.Content.Should().Equal(W.PdfBytes);
        invoice.Metadata[StoredFileBlobs.MetadataSubmission].Should().Be(input.SubmissionId.ToString("D"));
        invoice.Metadata[StoredFileBlobs.MetadataSourceDocument].Should().Be(W.EmlId.ToString("D"));
        invoice.Metadata[StoredFileBlobs.MetadataSourceCommunication].Should().Be(W.CommunicationId.ToString("D"));
        invoice.Metadata[StoredFileBlobs.MetadataSourceVersion].Should().Be(StoredFileSourceVersion.ToCanonical(EmlVersion));
        invoice.Metadata[StoredFileBlobs.MetadataSourceAttachment].Should().Be("eml-part-0");
        invoice.Metadata[StoredFileBlobs.MetadataEmlParts].Should().Be("1");

        // The Supporting part: under its eml-part id, Invoice supporting file, with the same source and its extension.
        var supportingId = input.MarkedParts[1].StoredFileId;
        supportingId.Should().Be(StoredFileIds.ForAttachment(input.SubmissionId, "eml-part-1"));
        var supporting = StagedOf(input.SubmissionId, StoredFilePurpose.InvoiceSupportingFile, supportingId);
        supporting.Content.Should().Equal(W.XlsxBytes);
        supporting.Metadata[StoredFileBlobs.MetadataSourceDocument].Should().Be(W.EmlId.ToString("D"));
        supporting.Metadata[StoredFileBlobs.MetadataSourceAttachment].Should().Be("eml-part-1");
        supporting.Metadata[StoredFileBlobs.MetadataExtension].Should().Be(".xlsx");
        H.Files.Dataverse.Writes.Should().Be(0, "every Stored File row is written only in the persister's commit transaction");

        // The submission: exactly this id, Channel Internal, no document, the caller as creator person (OP-5).
        var allocation = H.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(input.SubmissionId);
        allocation.FirmId.Should().Be(W.FirmId);
        allocation.Number.Should().Be("1001");
        var fields = allocation.Fields!;
        fields.Keys.Should().NotContain("sprk_document", "a from-email submission's sprk_document stays empty (092)");
        ((OptionSetValue)fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        ((OptionSetValue)fields["sprk_processingstatus"]).Value.Should().Be((int)InvoiceSubmissionStatus.Received);
        ((EntityReference)fields[RecordCreatorPerson.Column]).Should().Match<EntityReference>(r => r.LogicalName == "systemuser" && r.Id == W.CallerId);
        fields["sprk_ponumber"].Should().Be("PO-7");
        H.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.SystemUserId.Should().Be(W.CallerId);

        // The job, after the row.
        var job = H.JobsSent.Should().ContainSingle().Subject;
        job.JobId.Should().Be(input.JobId);
        job.SubjectId.Should().Be(input.SubmissionId.ToString());
        H.WriteTokens.Should().OnlyContain(t => !t.CanBeCanceled, "a write that started finishes (the intake's rule)");
        H.Deleted.Should().BeEmpty();
        H.BlobDeletes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_GenerationTwo_IsCreatedByThisCaller_WhoeverMadeTheEarlierAttempt(bool otherCallerMadeTheFirst)
    {
        _world.AddAttempt(K, 1, InvoiceSubmissionStatus.Failed, creator: otherCallerMadeTheFirst ? W.OtherCallerId : W.CallerId);
        var input = Input(generation: 2, caller: otherCallerMadeTheFirst ? W.CallerId : W.OtherCallerId);

        var result = await Build().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        var allocation = H.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(FromEmailSubmissionKey.SubmissionIdFor(K, 2));
        ((EntityReference)allocation.Fields![RecordCreatorPerson.Column]).Id.Should().Be(input.CallerSystemUserId,
            "the seam never assumes a generation's creator: the caller creates the attempt it starts (OP-5, EB-D-165)");
        H.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.SystemUserId.Should().Be(input.CallerSystemUserId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 1 and step A
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Submit_StepOneRefuses_IsThatRefusal_BeforeTheChecks_AndWritesNothing()
    {
        var stepA = 0;
        var operation = Build();
        operation.StepOne = Problem(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.SourceForbidden);
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status403Forbidden);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.SourceForbidden);
        stepA.Should().Be(0);
        H.OwnerContexts.Should().BeEmpty();
        H.Writes.Should().Be(0);
        _emlRows.Verify(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_StepOneAnswersWithoutAnErrorCode_IsTheRoutesAnswer()
    {
        var operation = Build();
        var answer = Problem(StatusCodes.Status403Forbidden, errorCode: null);
        operation.StepOne = answer;

        var act = () => operation.SubmitAsync(Input(), CancellationToken.None);

        (await act.Should().ThrowAsync<FromEmailRouteAnswerException>()).Which.Answer.Should().BeSameAs(answer);
        H.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Submit_TheFirmIsNotLinked_IsRefusedByTheIntake_AndStepAIsNotRun()
    {
        H.Links.Clear();
        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status403Forbidden);
        stepA.Should().Be(0, "step A runs only after the read-only refusals");
        H.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Submit_StepARuns_AfterTheReadOnlyChecks_AndBeforeAnyWrite()
    {
        (int Owners, int Writes, int EmlReads)? atStepA = null;
        var operation = Build();
        operation.StepA = _ =>
        {
            atStepA = (H.OwnerContexts.Count, H.Writes, _emlRows.Invocations.Count);
            return Task.FromResult(false);
        };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        atStepA.Should().Be((1, 0, 0), "the owner check ran, nothing was staged or created, the .eml's parts were not read yet");
    }

    [Fact]
    public async Task Submit_StepARefusesTheFiling_IsTheRefileRoutesAnswer_AndWritesNothing()
    {
        var operation = Build();
        var refile = TypedResults.Problem(detail: "Not allowed", statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = "COMMUNICATION_REFILE_FORBIDDEN" });
        operation.StepA = _ => throw new InvoiceSubmissionEndpoints.SourceFilingRefusedException(refile);

        var act = () => operation.SubmitAsync(Input(), CancellationToken.None);

        (await act.Should().ThrowAsync<FromEmailRouteAnswerException>()).Which.Answer.Should().BeSameAs(refile);
        H.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Submit_StepARefusesWithASubmissionCode_IsThatRefusal()
    {
        var operation = Build();
        operation.StepA = _ => throw new InvoiceSubmissionEndpoints.SourceFilingRefusedException(
            Problem(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch));

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status409Conflict);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        H.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Submit_TheEmlCannotBeReadAfterFiling_Is503_SayingTheFilingIsKept_AndWritesNothing()
    {
        _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(W.NotFound());
        var operation = Build();
        operation.StepA = _ => Task.FromResult(true);

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        result.Detail.Should().EndWith(InvoiceSubmissionService.FilingKeptSentence);
        H.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Submit_APartDiffersFromItsMark_Is503_AndWritesNothing()
    {
        StoreEml(W.Invoice); // the .eml no longer carries the Supporting part at ordinal 1

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        H.Writes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // After the first write: no compensation (deterministic ids)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Submit_TheRowAlreadyExists_IsAlreadyCreated_NoJob_NothingDeleted()
    {
        H.Allocation = TrackingNumberResult.AlreadyCreated("1001.1", 1);

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.AlreadyCreated);
        H.JobsSent.Should().BeEmpty();
        H.Deleted.Should().BeEmpty();
        H.BlobDeletes.Should().BeEmpty("the winner's blobs share these ids");
    }

    [Fact]
    public async Task Submit_TheJobCannotBeQueued_Is503_AndKeepsTheRowAndTheBlobs()
    {
        H.JobFailure = new InvalidOperationException("queue down");

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        H.Allocations.Should().ContainSingle();
        H.Deleted.Should().BeEmpty("the retry's replay re-queues the job of the row that is there");
        H.BlobDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_NoTrackingNumber_Is503TrackingUnavailable()
    {
        H.Allocation = TrackingNumberResult.Refused("busy");

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        result.ErrorCode.Should().Be(TrackingNumberReasons.Unavailable);
        H.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_OtherBytesAreStagedUnderThisAttempt_Is409DocumentAlreadySubmitted_NoRow()
    {
        var input = Input();
        var (spec, manifest) = SubmitInvoiceFromEmailOperation.SpecOf(input, input.MarkedParts[0], EmlVersion, [1]);
        (await H.Files.Service.StageAsync(spec, new MemoryStream("%PDF-1.7 another invoice"u8.ToArray()), manifest, CancellationToken.None))
            .IsStaged.Should().BeTrue();

        var result = await Build().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status409Conflict);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        H.Allocations.Should().BeEmpty();
        H.JobsSent.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_TheStoreIsNotConfigured_Is422StorageNotConfigured()
    {
        H.Files = new StoredFileRig(configured: false);

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.StorageNotConfigured);
        H.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_AnInconsistentInput_IsAWiringDefect()
    {
        var input = Input();
        var wrongId = input with { MarkedParts = input.MarkedParts.Select(p => p with { StoredFileId = Guid.NewGuid() }).ToList() };
        var correction = input with { Mode = FromEmailModes.Correction };

        await Build().Invoking(o => o.SubmitAsync(wrongId, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await Build().Invoking(o => o.SubmitAsync(correction, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        H.Writes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The replay's reads (contract section 5)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAttemptSource_IsTheEmlAndTheInvoicePartsSha_FromWhatTheStageRecorded()
    {
        var input = Input();
        var operation = Build();
        await operation.SubmitAsync(input, CancellationToken.None);

        var source = await operation.ReadAttemptSourceAsync(input.SubmissionId, CancellationToken.None);

        source.Should().Be(new FromEmailAttemptSource(W.EmlId, EmlMimeParts.Sha256Hex(W.PdfBytes)));
    }

    [Fact]
    public async Task ReadAttemptSource_AnUploadOrNothingStaged_IsNull()
    {
        var upload = await H.Build().SubmitAsync(InvoiceSubmissionServiceTests.Spa(), CancellationToken.None);
        upload.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        var operation = Build();

        (await operation.ReadAttemptSourceAsync(upload.Receipt!.SubmissionId, CancellationToken.None)).Should().BeNull("an upload records no source");
        (await operation.ReadAttemptSourceAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Requeue_QueuesTheJob_AndIsFalseWhenTheQueueRefuses()
    {
        var submissionId = FromEmailSubmissionKey.SubmissionIdFor(K, 1);
        var jobId = FromEmailSubmissionKey.JobIdFor(K, 1);

        (await Build().RequeueAsync(submissionId, jobId, "trace", CancellationToken.None)).Should().BeTrue();
        H.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(jobId);

        H.JobFailure = new InvalidOperationException("queue down");
        (await Build().RequeueAsync(submissionId, jobId, "trace", CancellationToken.None)).Should().BeFalse();
    }
}
