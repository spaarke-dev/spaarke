using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Jobs.Handlers.InvoiceSubmissionProcessingJobHandlerTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ebilling;

/// <summary>
/// Regression for GitHub issue #2553 (DEF-052, ebilling-r1): the per-file size limit
/// (<c>Ebilling:MaxSubmissionFileSizeMb</c>, <see cref="InvoiceSubmissionService.MaxFileBytes"/>) was enforced on uploads (413) but not on
/// two copy paths: the processing job's copy of a Submit Invoice source document (it buffered the whole file, about twice its size) and the
/// from-email route's marked <c>.eml</c> parts. Fixed: the job passes the limit (over it: permanent Failed
/// <see cref="SubmissionReasonCodes.FileTooLarge"/>, no retry, nothing staged; refused before the download when the row records the size), and
/// the from-email route refuses a marked file over it in step 1 (<see cref="FromEmailMarkRules.Check"/>), 413
/// <see cref="InvoiceSubmissionErrors.TooLarge"/>, before the <c>.eml</c> (which carries every marked part) is saved, so nothing is stored.
/// The part reader decodes each part under the limit as a backstop.
/// </summary>
/// <remarks>
/// The limit under test is 1 MB (the job rig's <c>MaxFileSizeMb</c> and the intake harness's option), so a file just over it is a real
/// 1 MB buffer, not a stand-in. Exactly at the limit is read (must-not-fire).
/// </remarks>
public sealed class Issue2553_SubmissionFileSizeLimitTests
{
    private const int OneMb = 1024 * 1024;
    private const int Processing = (int)InvoiceSubmissionStatus.Processing;
    private const int Failed = (int)InvoiceSubmissionStatus.Failed;

    // ═══════════════════════════════════════════════════════════════════════════
    // The processing job's copy of a Submit Invoice source document
    // ═══════════════════════════════════════════════════════════════════════════

    private static void LoadsTheSource(Rig rig)
    {
        rig.Processor.Script = async (row, load) =>
        {
            await load(DocumentSnapshot(row), CancellationToken.None);
            return Outcome(SubmissionEvaluationKind.Accepted) with { Submission = DocumentSnapshot(row) };
        };
    }

    private static byte[]? StagedCopy(Rig rig)
    {
        var name = StoredFileBlobName.Build(
            rig.Files.TenantId, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId), StoredFileIds.ForSourceVersion(SubmissionId, rig.DocumentVersion));
        return rig.Files.Gateway.Get(StoredFileSchema.ContainerOf(StoredFilePurpose.InvoicePdf), name)?.Content;
    }

    [Fact]
    public async Task Job_ADocumentOverTheLimit_EndsPermanentlyFailedTooLarge_NothingCopied()
    {
        var rig = new Rig { MaxFileSizeMb = 1, Bytes = new byte[OneMb + 1] }.HappyPath();
        LoadsTheSource(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned, "a retry reads the same file: the delivery is not redelivered");
        rig.StatusWrites.Should().Equal([(Processing, (string?)null), (Failed, SubmissionReasonCodes.FileTooLarge)]);
        SubmissionReasonCodes.FileTooLarge.Should().Be(InvoiceSubmissionErrors.TooLarge, "the one code the upload route answers and the UI already words");
        StagedCopy(rig).Should().BeNull("nothing is copied");
        rig.Persister.Calls.Should().BeEmpty("no invoice is committed");
    }

    [Fact]
    public async Task Job_ADocumentWhoseRowRecordsAnOverLimitSize_IsRefusedBeforeTheDownload()
    {
        // The stream is small: only the row's recorded size (sprk_filesize) says it is over the limit.
        var rig = new Rig { MaxFileSizeMb = 1, RecordedFileSize = OneMb + 1 }.HappyPath();
        LoadsTheSource(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Should().Contain((Failed, SubmissionReasonCodes.FileTooLarge));
        rig.Downloads.Should().BeEmpty("the recorded size is judged before anything is downloaded");
        StagedCopy(rig).Should().BeNull();
        rig.Persister.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_ADocumentOverTheLimitOnTheFinalDelivery_IsTheSameFailedTooLarge()
    {
        var rig = new Rig { MaxFileSizeMb = 1, Bytes = new byte[OneMb + 1] }.HappyPath();
        LoadsTheSource(rig);

        var outcome = await rig.Build().ProcessAsync(FinalJob(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.StatusWrites.Count(s => s.Status == Failed).Should().Be(1);
        rig.StatusWrites.Should().Contain((Failed, SubmissionReasonCodes.FileTooLarge));
    }

    [Fact]
    public async Task Job_ADocumentExactlyAtTheLimit_IsCopiedAndPasses()
    {
        var rig = new Rig { MaxFileSizeMb = 1, Bytes = new byte[OneMb], RecordedFileSize = OneMb }.HappyPath();
        LoadsTheSource(rig);

        var outcome = await rig.Build().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Downloads.Should().Equal("drive-1/item-1");
        StagedCopy(rig).Should().HaveCount(OneMb);
        rig.StatusWrites.Should().NotContain(s => s.Status == Failed);
        rig.Persister.Calls.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The from-email part reader
    // ═══════════════════════════════════════════════════════════════════════════

    private static byte[] Filler(int length, string header)
    {
        var bytes = new byte[length];
        System.Text.Encoding.ASCII.GetBytes(header).CopyTo(bytes, 0);
        return bytes;
    }

    private static FromEmailMarkedPart Part(int ordinal, string name, byte[] bytes, string mark) =>
        new(ordinal, name, EmlMimeParts.Sha256Hex(bytes), mark);

    private readonly W _world = new();
    private readonly Mock<IGenericEntityService> _emlRows = new();

    private InvoiceSubmissionServiceTests.Harness H => _world.Intake;

    private void StoreEml(params Sprk.Bff.Api.Models.Office.AttachmentReference[] attachments)
    {
        var eml = _world.AddEarlierEml(W.EmlId, null, attachments);
        eml["versionnumber"] = 4711L;
        _emlRows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(eml);
    }

    private EmlMarkedPartReader Reader() => new(_emlRows.Object, _world.Spe.Object, NullLogger<EmlMarkedPartReader>.Instance);

    [Fact]
    public async Task EmlPartReader_AMarkedPartOverTheLimit_IsTooLarge_AndNoPartIsReturned()
    {
        var big = Filler(OneMb + 1, "PK big timesheet");
        StoreEml(W.Invoice, W.Attachment("Timesheet.xlsx", big, "application/vnd.ms-excel"));
        var marks = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice"), Part(1, "Timesheet.xlsx", big, "Supporting") };

        var read = await Reader().ReadAsync(Guid.NewGuid(), W.EmlId, marks, CancellationToken.None, OneMb);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.TooLarge);
        read.Parts.Should().BeEmpty();
    }

    [Fact]
    public async Task EmlPartReader_AMarkedPartExactlyAtTheLimit_IsRead()
    {
        var atLimit = Filler(OneMb, "PK exactly 1 MB");
        StoreEml(W.Invoice, W.Attachment("Timesheet.xlsx", atLimit, "application/vnd.ms-excel"));
        var marks = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice"), Part(1, "Timesheet.xlsx", atLimit, "Supporting") };

        var read = await Reader().ReadAsync(Guid.NewGuid(), W.EmlId, marks, CancellationToken.None, OneMb);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.Read);
        read.Parts[1].Content.Should().Equal(atLimit);
    }

    [Fact]
    public async Task EmlPartReader_AnUnmarkedPartOverTheLimit_DoesNotRefuseTheMarkedOnes()
    {
        var big = Filler(OneMb + 1, "PK unmarked");
        StoreEml(W.Invoice, W.Attachment("Unmarked.zip", big, "application/zip"));
        var marks = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice") };

        var read = await Reader().ReadAsync(Guid.NewGuid(), W.EmlId, marks, CancellationToken.None, OneMb);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.Read);
        read.Parts.Should().ContainSingle().Which.Content.Should().Equal(W.PdfBytes);
    }

    [Fact]
    public async Task EmlPartReader_AnOversizePartUnderAnotherName_IsDocumentUnavailable_NotTooLarge()
    {
        var big = Filler(OneMb + 1, "PK renamed");
        StoreEml(W.Invoice, W.Attachment("Other name.xlsx", big, "application/vnd.ms-excel"));
        var marks = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice"), Part(1, "Timesheet.xlsx", big, "Supporting") };

        var read = await Reader().ReadAsync(Guid.NewGuid(), W.EmlId, marks, CancellationToken.None, OneMb);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.DocumentUnavailable);
    }

    [Fact]
    public async Task EmlPartReader_WithNoLimit_ReadsAnyPart()
    {
        var big = Filler(OneMb + 1, "PK big timesheet");
        StoreEml(W.Invoice, W.Attachment("Timesheet.xlsx", big, "application/vnd.ms-excel"));
        var marks = new[] { Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice"), Part(1, "Timesheet.xlsx", big, "Supporting") };

        (await Reader().ReadAsync(Guid.NewGuid(), W.EmlId, marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.Read);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The from-email route's answer
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class Operation(
        InvoiceSubmissionService submissions, ITrackingNumberAllocator allocator, StoredFileService files, JobSubmissionService jobs, EmlMarkedPartReader reader)
        : SubmitInvoiceFromEmailOperation(submissions, allocator, files, jobs, reader, NullLogger<SubmitInvoiceFromEmailOperation>.Instance)
    {
        public int StepACalls;

        internal override Task<(IResult? Refusal, Func<CancellationToken, Task<bool>>? FileSource)> PrepareSourceAsync(
            FromEmailSubmitInput input, CancellationToken ct) =>
            Task.FromResult<(IResult?, Func<CancellationToken, Task<bool>>?)>((null, _ => { StepACalls++; return Task.FromResult(true); }
        ));
    }

    private static FromEmailSubmitInput Input(IEnumerable<FromEmailMarkedPart> marked)
    {
        const string key = "k-2553";
        var submissionId = FromEmailSubmissionKey.SubmissionIdFor(key, 1);
        return new FromEmailSubmitInput
        {
            SourceEmlDocumentId = W.EmlId,
            CommunicationId = W.CommunicationId,
            CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, W.MatterId),
            FirmId = W.FirmId,
            FirmInvoiceNumber = "1001",
            MarkedParts = marked.Select(p => p with { StoredFileId = FromEmailSubmissionKey.StoredFileIdFor(submissionId, p) }).ToList(),
            SubmissionId = submissionId,
            JobId = FromEmailSubmissionKey.JobIdFor(key, 1),
            Generation = 1,
            Mode = FromEmailModes.New,
            CallerSystemUserId = W.CallerId,
            CorrelationId = "trace-2553",
        };
    }

    private Operation OperationOver()
    {
        H.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecordOwnershipContext c, CancellationToken _) => H.OwnerAnswer with { CreatedByPerson = c.RequestedBy?.SystemUserId });
        return new Operation(H.Build(), H.Allocator.Object, H.Files.Service, H.Jobs.Object, Reader());
    }

    [Fact]
    public async Task FromEmailOperation_Backstop_AMarkedPartOverTheLimit_Is413TooLarge_BeforeStepA_NothingWritten()
    {
        var big = Filler(OneMb + 1, "PK big timesheet");
        StoreEml(W.Invoice, W.Attachment("Timesheet.xlsx", big, "application/vnd.ms-excel"));
        var operation = OperationOver();
        var input = Input([Part(0, "Invoice 1001.pdf", W.PdfBytes, FromEmailMarks.Invoice), Part(1, "Timesheet.xlsx", big, FromEmailMarks.Supporting)]);

        var result = await operation.SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Refused);
        (result.Status, result.ErrorCode, result.Detail).Should().Be(
            (StatusCodes.Status413PayloadTooLarge, InvoiceSubmissionErrors.TooLarge, FromEmailMarkRules.TooLargeDetail(OneMb)),
            "the backstop answers as the route's step 1 does");
        operation.StepACalls.Should().Be(0, "a refused submission never leaves a filing behind");
        H.Writes.Should().Be(0);
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FromEmailOperation_AMarkedPartExactlyAtTheLimit_IsReceived()
    {
        var atLimit = Filler(OneMb, "PK exactly 1 MB");
        StoreEml(W.Invoice, W.Attachment("Timesheet.xlsx", atLimit, "application/vnd.ms-excel"));
        var input = Input([Part(0, "Invoice 1001.pdf", W.PdfBytes, FromEmailMarks.Invoice), Part(1, "Timesheet.xlsx", atLimit, FromEmailMarks.Supporting)]);

        var result = await OperationOver().SubmitAsync(input, CancellationToken.None);

        result.Outcome.Should().Be(FromEmailSubmitOutcome.Received);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The from-email SERVICE (the route's step 1): refused before the .eml is saved
    // ═══════════════════════════════════════════════════════════════════════════

    private static Task<FromEmailSubmissionOutcome> Submit(W world, FromEmailSubmissionInput input) =>
        world.Build().SubmitAsync(input, "office-user", W.Http(), CancellationToken.None);

    private static FromEmailSubmissionInput InputWith(Sprk.Bff.Api.Models.Office.AttachmentReference timesheet, string timesheetMark = "Supporting") =>
        W.Input(
            [W.Mark(W.Invoice, "Invoice"), W.Mark(timesheet, timesheetMark)],
            email: W.Email(W.Invoice, timesheet));

    [Theory]
    [InlineData("Supporting")]
    [InlineData("Invoice")]
    public async Task FromEmailService_AMarkedFileOverTheLimit_Is413_AndNothingIsSaved(string mark)
    {
        var world = new W();
        var big = W.Attachment(mark == "Invoice" ? "Invoice big.pdf" : "Timesheet.xlsx", Filler(OneMb + 1, "%PDF-1.7 big"), "application/pdf");
        var input = mark == "Invoice"
            ? W.Input([W.Mark(big, "Invoice"), W.Mark(W.Timesheet, "Supporting")], email: W.Email(big, W.Timesheet))
            : InputWith(big);

        var outcome = await Submit(world, input);

        outcome.Receipt.Should().BeNull();
        (outcome.Status, outcome.ErrorCode, outcome.Detail).Should().Be(
            (StatusCodes.Status413PayloadTooLarge, InvoiceSubmissionErrors.TooLarge, FromEmailMarkRules.TooLargeDetail(OneMb)));
        outcome.Extensions.Should().Contain("fileName", big.FileName).And.Contain("mark", mark);
        world.Save.Requests.Should().BeEmpty("the .eml (every marked part, its sprk_document row, its Communication row) is never saved");
        world.Operation.Submits.Should().BeEmpty();
        world.Operation.Requeues.Should().BeEmpty();
        world.Files.Should().BeEmpty("no file was written to SPE");
        H.Writes.Should().Be(0);
        H.Stages.Should().BeEmpty();
        H.Allocations.Should().BeEmpty();
        world.Entities.Invocations.Select(i => i.Method.Name).Should().NotContain(n => n.Contains("Create") || n.Contains("Update") || n.Contains("Delete"));
    }

    [Fact]
    public async Task FromEmailService_AMarkedFileExactlyAtTheLimit_Proceeds_AndIsSaved()
    {
        var world = new W();
        var atLimit = W.Attachment("Timesheet.xlsx", Filler(OneMb, "PK exactly 1 MB"), "application/vnd.ms-excel");

        var outcome = await Submit(world, InputWith(atLimit));

        outcome.Receipt.Should().NotBeNull();
        world.Save.Requests.Should().ContainSingle("the .eml is saved once");
        world.Operation.Submits.Should().ContainSingle();
    }

    [Fact]
    public async Task FromEmailService_AnOverLimitFileMarkedDontSave_IsNotRefused()
    {
        var world = new W();
        var big = W.Attachment("Signature.zip", Filler(OneMb + 1, "PK dont save"), "application/zip");

        var outcome = await Submit(world, InputWith(big, "DontSave"));

        outcome.Status.Should().NotBe(StatusCodes.Status413PayloadTooLarge, "only a file marked to be saved is copied");
    }
}
