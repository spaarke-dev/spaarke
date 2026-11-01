using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
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

    /// <summary>
    /// Review round 5: a step-1 answer without an <c>errorCode</c> (no path produces one today) is mapped onto contract section 6 as step A's
    /// are: a 403 is <c>source_forbidden</c>, a transient answer 503 <c>record_unavailable</c>, anything else 503 <c>failed</c>. No answer
    /// outside section 6 reaches the add-in; nothing is checked, filed or written.
    /// </summary>
    [Theory]
    [InlineData(StatusCodes.Status403Forbidden, 403, "ebilling.submission.source_forbidden")]
    [InlineData(StatusCodes.Status429TooManyRequests, 503, "ebilling.submission.record_unavailable")]
    [InlineData(StatusCodes.Status503ServiceUnavailable, 503, "ebilling.submission.record_unavailable")]
    [InlineData(StatusCodes.Status404NotFound, 503, "ebilling.submission.failed")]
    [InlineData(StatusCodes.Status500InternalServerError, 503, "ebilling.submission.failed")]
    public async Task Submit_StepOneAnswersWithoutAnErrorCode_IsMappedOntoSectionSix_NothingWritten(int stepOneStatus, int status, string code)
    {
        var stepA = 0;
        var operation = Build();
        operation.StepOne = Problem(stepOneStatus, errorCode: null);
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((status, code));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
        stepA.Should().Be(0);
        H.OwnerContexts.Should().BeEmpty("refused before the intake's checks");
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
    public async Task Submit_StepARuns_AfterTheReadOnlyChecksAndThePartsRead_AndBeforeAnyWrite()
    {
        (int Owners, int Writes, int EmlReads, int Stages)? atStepA = null;
        var operation = Build();
        operation.StepA = _ =>
        {
            atStepA = (H.OwnerContexts.Count, H.Writes, _emlRows.Invocations.Count, H.Stages.Count);
            return Task.FromResult(false);
        };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        atStepA.Should().Be((1, 0, 1, 0),
            "the owner check ran and the .eml's parts were read (review round 5: read-only, so before A); nothing was staged or created");
    }

    [Fact]
    public async Task Submit_StepARefusesTheFiling_403_IsTheRefileRoutesOwn403_AndWritesNothing()
    {
        var operation = Build();
        var refile = TypedResults.Problem(detail: "Not allowed", statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["reasonCode"] = "child_record.denied" });
        operation.StepA = _ => throw new InvoiceSubmissionEndpoints.SourceFilingRefusedException(refile);

        // Changed 2026-10-29 (095 contract fix): a refusal result (it carries errorCode at the top level of the body), not an
        // SdapProblemException the problem middleware would render with the code nested under extensions.code.
        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status403Forbidden, CommunicationRecordAuthorizationFilter.RefileDenyCode),
            "contract section 6 lists only the re-file route's own 403 (review round 5)");
        result.Detail.Should().Be(InvoiceSubmissionEndpoints.RefileForbiddenDetail, "the same words as a caller without Write gets (no existence oracle)");
        result.Filed.Should().BeFalse("nothing was filed");
        H.Writes.Should().Be(0);
        H.Allocations.Should().BeEmpty();
    }

    /// <summary>
    /// Review round 5: every other answer of the communication re-file core carries a code contract section 6 does not list, so it is mapped
    /// onto section 6: a transient one is 503 <c>record_unavailable</c>, anything else 503 <c>failed</c>. Nothing was filed or written.
    /// </summary>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "child_record.invalid_payload", 503, "ebilling.submission.failed")]
    [InlineData(StatusCodes.Status404NotFound, "child_record.not_found", 503, "ebilling.submission.failed")]
    [InlineData(StatusCodes.Status409Conflict, "record_owner.no_owner_source", 503, "ebilling.submission.failed")]
    [InlineData(StatusCodes.Status500InternalServerError, "child_record.no_decision", 503, "ebilling.submission.failed")]
    [InlineData(StatusCodes.Status429TooManyRequests, "child_record.dataverse", 503, "ebilling.submission.record_unavailable")]
    [InlineData(StatusCodes.Status502BadGateway, "child_record.dataverse", 503, "ebilling.submission.record_unavailable")]
    [InlineData(StatusCodes.Status503ServiceUnavailable, "child_record.dataverse", 503, "ebilling.submission.record_unavailable")]
    [InlineData(StatusCodes.Status409Conflict, "record_owner_unsettled", 503, "ebilling.submission.record_unavailable")]
    public async Task Submit_StepARefileAnswersACodeOutsideSectionSix_IsMappedOntoSectionSix_NothingWritten(
        int refileStatus, string refileCode, int status, string code)
    {
        var operation = Build();
        var refile = refileCode == "record_owner_unsettled"
            ? ProblemDetailsHelper.RecordOwnerUnsettled("communication", "trace")
            : TypedResults.Problem(detail: "refused", statusCode: refileStatus, extensions: new Dictionary<string, object?> { ["reasonCode"] = refileCode });
        operation.StepA = _ => throw new InvoiceSubmissionEndpoints.SourceFilingRefusedException(refile);

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((status, code));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence, "the re-file did not answer 204: nothing was filed");
        H.Writes.Should().Be(0);
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
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

    /// <summary>
    /// Review round 5 (pass-2 F1-2, uac-r2 K2-b): the parts are read before step A, so a refusal of that read leaves no filing behind; and
    /// the permanent outcomes are a 422, never a 503 the client would retry against the same <c>.eml</c>.
    /// </summary>
    [Theory]
    [InlineData("gone")]
    [InlineData("no file")]
    public async Task Submit_TheEmlIsGoneOrHoldsNoFile_Is422EmailSaveFailed_BeforeStepA_NothingFiledOrWritten(string fault)
    {
        if (fault == "gone")
        {
            _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(W.NotFound());
        }
        else
        {
            _eml!.Attributes.Remove("sprk_graphitemid");
        }

        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status422UnprocessableEntity, FromEmailSubmissionErrors.EmailSaveFailed));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence).And.Contain("invoice upload");
        stepA.Should().Be(0, "a refused submission never leaves a filing behind");
        H.Writes.Should().Be(0);
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a part is missing")]
    [InlineData("the parts are in another order")]
    public async Task Submit_APartDiffersFromItsMark_Is422DocumentUnavailable_BeforeStepA_NothingFiledOrWritten(string fault)
    {
        if (fault == "a part is missing")
        {
            StoreEml(W.Invoice); // the .eml no longer carries the Supporting part at ordinal 1
        }
        else
        {
            StoreEml(W.Timesheet, W.Invoice); // ordinal 0 is not the Invoice part the mark names
        }

        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence).And.Contain("invoice upload");
        stepA.Should().Be(0);
        H.Writes.Should().Be(0);
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_TheEmlCannotBeReadRightNow_Is503RecordUnavailable_BeforeStepA_NothingFiledOrWritten()
    {
        _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
        stepA.Should().Be(0);
        H.Writes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 1, the real wiring (review round 5, pass-2 F3-1): no PrepareSourceAsync override
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The REAL operation (step 1 is <see cref="InvoiceSubmissionEndpoints.PrepareSourceAsync"/> through the request's services) with a
    /// caller-rights probe and an app-only reader (strict: the <c>.eml</c> and its communication filed under the matter, nothing else).
    /// </summary>
    private (SubmitInvoiceFromEmailOperation Operation, FromEmailSubmitInput Input, RecordingRightsProbe Probe, Mock<IGenericEntityService> AppOnly) RealStepOne()
    {
        var probe = new RecordingRightsProbe();
        var appOnly = new Mock<IGenericEntityService>(MockBehavior.Strict);
        appOnly.Setup(e => e.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", W.EmlId) { ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", W.CommunicationId) });
        appOnly.Setup(e => e.RetrieveAsync("sprk_communication", W.CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", W.CommunicationId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", W.MatterId) });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(probe);
        services.AddSingleton(appOnly.Object);
        var http = new DefaultHttpContext { TraceIdentifier = "trace-094-step-1", RequestServices = services.BuildServiceProvider() };
        http.Request.Headers.Authorization = "Bearer caller-token";
        var operation = new SubmitInvoiceFromEmailOperation(H.Build(), H.Allocator.Object, H.Files.Service, H.Jobs.Object,
            new EmlMarkedPartReader(_emlRows.Object, _world.Spe.Object, NullLogger<EmlMarkedPartReader>.Instance),
            NullLogger<SubmitInvoiceFromEmailOperation>.Instance);
        return (operation, Input() with { Http = http }, probe, appOnly);
    }

    [Theory]
    [InlineData("the .eml")]
    [InlineData("its communication")]
    public async Task Submit_RealStepOne_TheCallerCannotRead_Is403SourceForbidden_NoStage_NoAllocation_NoStepA(string denied)
    {
        var (operation, input, probe, appOnly) = RealStepOne();
        if (denied == "its communication")
        {
            probe.Grant("sprk_documents", W.EmlId, AccessRights.Read);
        }

        var result = await operation.SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.SourceForbidden));
        probe.Calls.Should().Contain(c => c.Set == (denied == "the .eml" ? "sprk_documents" : "sprk_communications") && c.Token == "caller-token",
            "the caller's own Read is asked, with the caller's token");
        H.OwnerContexts.Should().BeEmpty("refused before the intake's checks");
        _emlRows.Invocations.Should().BeEmpty("the parts are not read");
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
        H.Writes.Should().Be(0);
        appOnly.Verify(e => e.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "step A (which reads the communication's filing) never runs");
    }

    [Fact]
    public async Task Submit_RealStepOne_TheCallerReadsBoth_PassesStepOne_AndAFiledCommunicationIsNotFiledAgain()
    {
        var (operation, input, probe, appOnly) = RealStepOne();
        probe.Grant("sprk_documents", W.EmlId, AccessRights.Read);
        probe.Grant("sprk_communications", W.CommunicationId, AccessRights.Read);

        var result = await operation.SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received, "the control: the same wiring passes a caller who can read both");
        H.Allocations.Should().ContainSingle();
        appOnly.Verify(e => e.RetrieveAsync("sprk_communication", W.CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once,
            "step A read the communication's filing and found it filed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The intake's refusals are contract section 6's codes (review round 5, pass-2 F4-2)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Every (status, code) the intake's checks can reach the add-in with, as contract section 6 lists them.</summary>
    private static readonly (int Status, string Code)[] SectionSixIntakeAnswers =
    [
        (StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid),
        (StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmNotLinked),
        (StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable),
    ];

    [Theory]
    [InlineData("the firm cannot be read", 503, "ebilling.submission.record_unavailable")]
    [InlineData("the firm's link cannot be read", 503, "ebilling.submission.record_unavailable")]
    [InlineData("the record cannot be read", 503, "ebilling.submission.record_unavailable")]
    [InlineData("the record is gone", 503, "ebilling.submission.record_unavailable")]
    [InlineData("the firm is inactive", 400, "ebilling.submission.firm_invalid")]
    [InlineData("the firm is not linked", 403, "ebilling.submission.firm_not_linked")]
    public async Task Submit_TheIntakeRefuses_WithAContractSectionSixCode_AndItsStatus(string fault, int status, string code)
    {
        switch (fault)
        {
            case "the firm cannot be read": H.FirmReadFailure = new TimeoutException(); break;
            case "the firm's link cannot be read": H.LinksUnreadable = true; break;
            case "the record cannot be read": H.MatterReadFailure = new TimeoutException(); break;
            case "the record is gone": H.Matter = null; break;
            case "the firm is inactive": H.Firm!["statecode"] = new OptionSetValue(1); break;
            case "the firm is not linked": H.Links.Clear(); break;
        }

        var result = await Build().SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((status, code));
        SectionSixIntakeAnswers.Should().Contain((result.Status, result.ErrorCode!), "no code outside contract section 6 reaches the add-in");
        H.Writes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // After the first write: no compensation (deterministic ids)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_TheRowAlreadyExists_IsAlreadyCreated_CarryingWhetherStepAFiled_NoJob_NothingDeleted(bool stepAFiled)
    {
        H.Allocation = TrackingNumberResult.AlreadyCreated("1001.1", 1);
        var operation = Build();
        operation.StepA = _ => Task.FromResult(stepAFiled);

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.AlreadyCreated);
        result.Filed.Should().Be(stepAFiled, "the route's answer to the race says the filing is kept only when this request filed (review round 5)");
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
    public async Task Submit_TheSameBytesAreStagedFromAnotherEml_Is409DocumentAlreadySubmitted_NoRow()
    {
        // An earlier request with the same key staged the same Invoice bytes from ANOTHER .eml: not this request's retry.
        var input = Input();
        var earlier = input with { SourceEmlDocumentId = Guid.NewGuid() };
        var (spec, manifest) = SubmitInvoiceFromEmailOperation.SpecOf(earlier, earlier.MarkedParts[0], EmlVersion, [1]);
        var staged = (await H.Files.Service.StageAsync(spec, new MemoryStream(W.PdfBytes), manifest, CancellationToken.None)).Blob!;

        var result = await Build().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        result.Status.Should().Be(StatusCodes.Status409Conflict);
        result.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        H.Allocations.Should().BeEmpty();
        H.JobsSent.Should().BeEmpty();
        H.Files.Gateway.Get(staged.Container, staged.BlobPath)!.Metadata[StoredFileBlobs.MetadataSourceDocument]
            .Should().Be(earlier.SourceEmlDocumentId.ToString("D"), "the earlier stage is left unchanged");
    }

    /// <summary>
    /// Review round 5: "storage not configured" is a read-only check, so it refuses before step A and leaves no filing behind (before, the
    /// first stage found it after step A had filed the communication). Both of the stage's NotConfigured checks: no store, and a store whose
    /// segment is not this environment's.
    /// </summary>
    [Theory]
    [InlineData("no store")]
    [InlineData("another environment")]
    public async Task Submit_TheStoreIsNotConfigured_Is422StorageNotConfigured_BeforeStepA_NothingFiledOrWritten(string fault)
    {
        H.Files = fault == "no store"
            ? new StoredFileRig(configured: false)
            : new StoredFileRig(organizationId: _ => Task.FromResult(Guid.NewGuid()));
        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.StorageNotConfigured));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
        stepA.Should().Be(0, "a refused submission never leaves a filing behind");
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
        H.Writes.Should().Be(0);
    }

    /// <summary>
    /// Task 191: a reader that is not set up is a 422 beside the store's check, before step A and before anything is staged or created;
    /// where the store is not configured either, the store's code wins; set up, the same request takes the normal path.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_TheReaderIsNotConfigured_Is422ReaderNotConfigured_BeforeStepA_NothingFiledOrWritten(bool storeUnconfiguredToo)
    {
        H.Reader.Endpoint = "";
        if (storeUnconfiguredToo)
        {
            H.Files = new StoredFileRig(configured: false);
        }

        var stepA = 0;
        var operation = Build();
        operation.StepA = _ => { stepA++; return Task.FromResult(true); };

        var result = await operation.SubmitAsync(Input(), CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode).Should().Be((StatusCodes.Status422UnprocessableEntity,
            storeUnconfiguredToo ? InvoiceSubmissionErrors.StorageNotConfigured : InvoiceSubmissionErrors.ReaderNotConfigured));
        result.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
        stepA.Should().Be(0, "a refused submission never leaves a filing behind");
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
        H.JobsSent.Should().BeEmpty();
        H.Writes.Should().Be(0);

        if (!storeUnconfiguredToo)
        {
            H.Reader.Endpoint = "https://invoice-reader.test/";
            (await operation.SubmitAsync(Input(), CancellationToken.None)).Outcome.Should().Be(FromEmailSubmitOutcome.Received);
            stepA.Should().Be(1);
        }
    }

    [Fact]
    public async Task Submit_AnInconsistentInput_IsAWiringDefect()
    {
        var input = Input();
        var wrongId = input with { MarkedParts = input.MarkedParts.Select(p => p with { StoredFileId = Guid.NewGuid() }).ToList() };
        var correction = input with { Mode = FromEmailModes.Correction };
        // Task 097b: a Correction names its invoice AND that invoice's own submission (the intake's check found it); New names neither.
        var target = Guid.NewGuid();
        var correctionNoTarget = input with { Mode = FromEmailModes.Correction, InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(target) };
        var correctionWrongTarget = correctionNoTarget with { ResubmissionOf = Guid.NewGuid() };
        var newWithTarget = input with { ResubmissionOf = target };
        var newWithInvoice = input with { InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(target) };
        // Review round 5 (pass-2 F4-2): the fields 095 and the route refuse first, so the intake's own field codes never reach the add-in.
        var noFirm = input with { FirmId = Guid.Empty };
        var noRecord = input with { CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, Guid.Empty) };
        var longNumber = input with { FirmInvoiceNumber = new string('9', TrackingNumberAllocator.MaxFirmInvoiceNumberLength + 1) };
        var longPo = input with { PoNumber = new string('P', InvoiceSubmissionService.MaxPoNumberLength + 1) };

        foreach (var defect in new[]
        {
            wrongId, correction, correctionNoTarget, correctionWrongTarget, newWithTarget, newWithInvoice, noFirm, noRecord, longNumber, longPo,
        })
        {
            await Build().Invoking(o => o.SubmitAsync(defect, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        }

        H.Writes.Should().Be(0);
        H.OwnerContexts.Should().BeEmpty();
    }

    [Fact(DisplayName = "097b: a Correction creates its submission with sprk_resubmissionof = the replaced invoice's submission; New writes none")]
    public async Task Submit_ACorrection_RecordsItsTarget_OnTheSubmission()
    {
        var target = Guid.NewGuid();
        var input = Input() with
        {
            Mode = FromEmailModes.Correction, InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(target), ResubmissionOf = target,
        };

        var result = await Build().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        H.Allocations.Should().ContainSingle().Which.Fields!["sprk_resubmissionof"]
            .Should().Be(new EntityReference(InvoiceSubmissionService.SubmissionEntity, target));
    }

    [Fact(DisplayName = "097b: a New submission from an email writes no sprk_resubmissionof")]
    public async Task Submit_New_RecordsNoTarget()
    {
        await Build().SubmitAsync(Input(), CancellationToken.None);

        H.Allocations.Should().ContainSingle().Which.Fields!.Should().NotContainKey("sprk_resubmissionof");
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

    /// <summary>
    /// Review round 5: a stage that records a source it cannot read is neither "no source" nor this attempt's source: the read fails closed
    /// (it throws, as a read fault does; the replay answers 503 record_unavailable), never a source the replay would accept.
    /// </summary>
    [Theory]
    [InlineData(StoredFileBlobs.MetadataSourceCommunication, "not-a-guid")]
    [InlineData(StoredFileBlobs.MetadataSourceVersion, "seven")]
    [InlineData(StoredFileBlobs.MetadataEmlParts, "1,x")]
    [InlineData(StoredFileBlobs.MetadataSourceDocument, "not-a-guid")]
    [InlineData(StoredFileBlobs.MetadataSourceDocument, " ")]
    [InlineData(StoredFileBlobs.MetadataSourceCommunication, "")]
    public async Task ReadAttemptSource_AStageWithAnUnreadableSourceKey_FailsClosed(string key, string value)
    {
        var input = Input();
        var operation = Build();
        (await operation.SubmitAsync(input, CancellationToken.None)).Outcome.Should().Be(FromEmailSubmitOutcome.Received);
        StagedOf(input.SubmissionId, StoredFilePurpose.InvoicePdf, StoredFileIds.ForUpload(input.SubmissionId)).Metadata[key] = value;

        var act = () => operation.ReadAttemptSourceAsync(input.SubmissionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
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
