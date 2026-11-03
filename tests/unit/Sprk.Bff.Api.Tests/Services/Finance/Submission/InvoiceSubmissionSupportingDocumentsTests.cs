using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 118b-1: the from-document intake's supporting documents (<see cref="InvoiceSubmissionRequest.SupportingDocumentIds"/>)
/// in <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>: the set joins the "same details?" comparison with an earlier
/// attempt (never the submission id, D-134), a Failed attempt still gives a new attempt, and (118b-2) a new attempt reads each supporting
/// document before step A and stages its copy, with that document's own source, under its part id after step A.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): one submission per document and attempt, "a different request is never answered with another's receipt", and
/// "an attempt commits only the files its request named", are invariants of the only invoice creator's entry. The earlier attempt's set is
/// seeded either through the stage-read seam (<see cref="InvoiceSubmissionService.ReadAttemptSupportingDocumentsAsync"/>) or, from 118b-2,
/// by a real earlier request whose parts land in the real file store (<see cref="StoredFileRig"/>).
/// </remarks>
public sealed class InvoiceSubmissionSupportingDocumentsTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid SubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId);
    private static readonly Guid JobId = InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId);
    private static readonly Guid A = Guid.Parse("118b0000-0000-4000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("118b0000-0000-4000-8000-00000000000b");
    private static readonly Guid C = Guid.Parse("118b0000-0000-4000-8000-00000000000c");

    private static Harness NewHarness() => new()
    {
        ExistingDocument = new Entity("sprk_document", DocumentId)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-118b",
        },
    };

    private static InvoiceSubmissionRequest Confirm(params Guid[] supporting) => new()
    {
        FirmId = InvoiceSubmissionServiceTests.FirmId,
        CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, InvoiceSubmissionServiceTests.MatterId),
        Channel = InvoiceSubmissionChannel.Internal,
        FirmInvoiceNumber = "INV-7",
        ExistingDocumentId = DocumentId,
        CallerObjectId = Guid.NewGuid(),
        CorrelationId = "corr-118b",
        SupportingDocumentIds = supporting,
    };

    private static Entity Earlier(Guid id, string status) => new("sprk_invoicesubmission", id)
    {
        [TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.3",
        [TrackingNumberAllocator.FirmInvoiceNumberColumn] = "INV-7",
        [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", InvoiceSubmissionServiceTests.FirmId),
        ["sprk_processingstatus"] = new OptionSetValue((int)Enum.Parse<InvoiceSubmissionStatus>(status)),
        ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
        ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", InvoiceSubmissionServiceTests.MatterId),
    };

    /// <summary>The intake, with the earlier attempts' stored sets seeded (what 118b-2's stage read will answer).</summary>
    private sealed class WithStoredSets(Harness h, IReadOnlyList<Guid> earlier, Exception? fault = null)
        : InvoiceSubmissionService(h.Allocator.Object, h.Entities.Object, h.RecordTypes.Object, h.Participations, h.Files.Service, h.Ownership.Object,
            InvoiceSubmissionServiceTests.IsolationOver(() => h.Isolation), h.Jobs.Object, h.Monitor,
            Microsoft.Extensions.Options.Options.Create(h.Reader), NullLogger<InvoiceSubmissionService>.Instance)
    {
        public List<Guid> Reads { get; } = new();

        internal override Task<IReadOnlyList<Guid>> ReadAttemptSupportingDocumentsAsync(Guid submissionId, CancellationToken ct)
        {
            Reads.Add(submissionId);
            return fault is null ? Task.FromResult(earlier) : Task.FromException<IReadOnlyList<Guid>>(fault);
        }
    }

    /// <summary>
    /// Task 118b-2: the route's binding of the shared document read, as a double: each supporting document reads as a file with its own bytes
    /// (unless <see cref="Files"/> says otherwise), and every read is recorded in <see cref="Events"/> beside step A.
    /// </summary>
    private sealed class Documents
    {
        public Dictionary<Guid, SourceDocumentFile> Files { get; } = new();
        public Exception? Fault { get; init; }
        public List<string> Events { get; } = new();

        /// <summary>The per-file limit each read was asked to enforce (task 118b review F1-b).</summary>
        public List<long> MaxBytes { get; } = new();

        public static byte[] BytesOf(Guid id) => System.Text.Encoding.ASCII.GetBytes("supporting " + id.ToString("N"));

        public static SourceDocumentFile Readable(Guid id, long version = 3) =>
            new(SourceDocumentFileOutcome.Read, BytesOf(id), version, "application/pdf", CommunicationId: null, FileName: "Timesheet " + id.ToString("N")[..4] + ".PDF");

        public Task<SourceDocumentFile> ReadAsync(Guid id, long maxBytes, CancellationToken ct)
        {
            Events.Add("read " + id);
            MaxBytes.Add(maxBytes);
            return Fault is { } fault ? Task.FromException<SourceDocumentFile>(fault) : Task.FromResult(Files.TryGetValue(id, out var file) ? file : Readable(id));
        }
    }

    /// <summary>A new attempt or retry naming <paramref name="supporting"/>, reading them through <paramref name="documents"/>, step A recorded.</summary>
    private static InvoiceSubmissionRequest Submit(Documents documents, params Guid[] supporting) => Confirm(supporting) with
    {
        ReadSupportingDocument = documents.ReadAsync,
        FileSourceBeforeWrite = _ => { documents.Events.Add("step A"); return Task.FromResult(true); },
    };

    /// <summary>The staged blob of supporting part <paramref name="number"/> of <paramref name="submissionId"/> (null: none).</summary>
    private static InMemoryBlobGateway.Blob? Part(Harness h, Guid submissionId, int number) =>
        h.Files.Gateway.Get(
            StoredFileSchema.ContainerOf(StoredFilePurpose.InvoiceSupportingFile),
            StoredFileBlobName.Build(h.Files.TenantId, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId)),
                InvoiceSubmissionPersister.DocumentPartFileId(submissionId, number)));

    private static void ShouldHaveWrittenNothing(Harness h)
    {
        h.Allocations.Should().BeEmpty("no submission may be created");
        h.JobsSent.Should().BeEmpty("no job may be queued");
        h.Stages.Should().BeEmpty();
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "118b (must not fire): a retry with the same supporting set, in another order and with a repeat, replays the earlier submission - the id is the document's, unchanged")]
    public async Task Retry_WithTheSameSet_ReplaysTheEarlierSubmission_WithTheDocumentsId()
    {
        var h = NewHarness();
        h.Submission = Earlier(SubmissionId, "Accepted");
        var service = new WithStoredSets(h, [A, B]);

        var outcome = await service.SubmitExistingDocumentAsync(Confirm(B, A, B), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.3", DocumentId));
        SubmissionId.Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 1), "the set never joins the submission id (D-134)");
        service.Reads.Should().Equal(SubmissionId);
        ShouldHaveWrittenNothing(h);
    }

    public static TheoryData<Guid[], Guid[]> OtherSets => new()
    {
        { new[] { A, B }, new[] { A } },
        { new[] { A }, new[] { A, B } },
        { new[] { A }, new[] { B } },
        { Array.Empty<Guid>(), new[] { A } },
        { new[] { A }, Array.Empty<Guid>() },
    };

    [Theory(DisplayName = "118b (must fire): a retry whose supporting set differs from the earlier attempt's is 409 document_already_submitted (other details); nothing written")]
    [MemberData(nameof(OtherSets))]
    public async Task Retry_WithAnotherSet_Is409_DocumentAlreadySubmitted(Guid[] earlier, Guid[] requested)
    {
        var h = NewHarness();
        h.Submission = Earlier(SubmissionId, "Received");
        var stepA = 0;

        var outcome = await new WithStoredSets(h, earlier).SubmitExistingDocumentAsync(
            Confirm(requested) with { FileSourceBeforeWrite = _ => { stepA++; return Task.FromResult(true); } }, CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        stepA.Should().Be(0, "nothing is filed for another request's details");
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "118b-2 (must fire / must not fire): the earlier set is READ from the attempt's part stages: the same set (another order) replays the earlier submission; fewer documents or none is 409; an attempt with no parts staged has the empty set")]
    public async Task Retry_ComparesWithTheSetTheEarlierAttemptStaged()
    {
        var h = NewHarness();
        var documents = new Documents();
        (await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Submission = Earlier(SubmissionId, "Accepted");

        var same = await h.Build().SubmitExistingDocumentAsync(Submit(documents, B, A), CancellationToken.None);
        var fewer = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A), CancellationToken.None);
        var none = await h.Build().SubmitExistingDocumentAsync(Submit(documents), CancellationToken.None);

        same.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.3", DocumentId), "the same request replays the same submission");
        fewer.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        none.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.Allocations.Should().ContainSingle("only the first request created a submission");
        h.Stages.Should().HaveCount(2, "a retry stages nothing");

        var withoutParts = NewHarness();
        withoutParts.Submission = Earlier(SubmissionId, "Accepted");
        (await withoutParts.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Receipt
            .Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.3", DocumentId), "no parts staged is the empty set (an attempt made before 118b)");
    }

    [Fact(DisplayName = "118b-2 (must fire): an earlier attempt whose parts do not read back as its first part lists them (a part gone) is 503 record_unavailable, never \"no supporting documents\"; nothing written")]
    public async Task Retry_TheEarlierPartsDoNotReadBack_Is503()
    {
        var h = NewHarness();
        (await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A, B), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Submission = Earlier(SubmissionId, "Accepted");
        var second = Part(h, SubmissionId, 2)!;
        second.Metadata.Clear(); // no recorded hash: the stage reads back as no usable blob

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A, B), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        h.Allocations.Should().ContainSingle();
    }

    [Fact(DisplayName = "118b (must fire): an earlier attempt's set that cannot be read is 503 record_unavailable, never \"no supporting documents\"; nothing filed or written")]
    public async Task Retry_TheEarlierSetCannotBeRead_Is503_NothingWritten()
    {
        var h = NewHarness();
        h.Submission = Earlier(SubmissionId, "Accepted");
        var stepA = 0;

        var outcome = await new WithStoredSets(h, [], new InvalidOperationException("stage store down")).SubmitExistingDocumentAsync(
            Confirm() with { FileSourceBeforeWrite = _ => { stepA++; return Task.FromResult(true); } }, CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        stepA.Should().Be(0);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "118b (must not fire): after a Failed attempt another supporting set is a NEW attempt (generation 2), not a 409; the Failed attempt's set is not compared")]
    public async Task Retry_AfterFailed_WithAnotherSet_IsANewAttempt_Not409()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Earlier(SubmissionId, "Failed");
        var service = new WithStoredSets(h, [B]);
        var second = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 2);

        var outcome = await service.SubmitExistingDocumentAsync(Submit(new Documents(), A), CancellationToken.None);

        h.SubmissionReads.Should().Equal(SubmissionId, second);
        service.Reads.Should().BeEmpty("a Failed attempt is not answered from; the request is the next generation");
        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received, "a new attempt with the new set");
        outcome.Receipt!.SubmissionId.Should().Be(second);
        h.Allocations.Should().ContainSingle().Which.SubmissionId.Should().Be(second);
        Part(h, second, 1)!.Metadata[StoredFileBlobs.MetadataSourceDocument].Should().Be(A.ToString("D"), "the new set is staged under the NEW attempt's ids");
        Part(h, SubmissionId, 1).Should().BeNull();
    }

    [Fact(DisplayName = "118b-2 (acceptance: two readable supporting documents): each is read BEFORE step A, then each copy is staged after step A under its part id in sorted order, with that document's own source; the first part lists the parts; then the submission and its job")]
    public async Task NewAttempt_WithTwoSupportingDocuments_ReadsBeforeStepA_StagesEachCopyWithItsSource()
    {
        var h = NewHarness();
        var documents = new Documents();
        documents.Files[A] = Documents.Readable(A, version: 11);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, B, A, B), CancellationToken.None);

        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-1.1", DocumentId), "the id is still the document's (D-134)");
        documents.Events.Should().Equal("read " + A, "read " + B, "step A");
        h.Stages.Should().HaveCount(2);
        h.Allocations.Should().ContainSingle();
        h.JobsSent.Should().ContainSingle();
        var first = Part(h, SubmissionId, 1)!;
        var second = Part(h, SubmissionId, 2)!;
        first.Metadata[StoredFileBlobs.MetadataSourceDocument].Should().Be(A.ToString("D"), "part numbers follow the sorted ids");
        first.Metadata[StoredFileBlobs.MetadataSourceVersion].Should().Be("11", "each part records its own document's version");
        first.Metadata[StoredFileBlobs.MetadataEmlParts].Should().Be("1,2", "the first part lists the parts");
        first.Metadata[StoredFileBlobs.MetadataExtension].Should().Be(".pdf");
        first.Metadata.Should().NotContainKey(StoredFileBlobs.MetadataSourceAttachment, "a document is copied whole");
        first.Content.Should().Equal(Documents.BytesOf(A));
        second.Metadata[StoredFileBlobs.MetadataSourceDocument].Should().Be(B.ToString("D"));
        second.Metadata.Should().NotContainKey(StoredFileBlobs.MetadataEmlParts);
        second.Metadata[StoredFileBlobs.MetadataSha256].Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Documents.BytesOf(B))));
        h.WriteTokens.Should().OnlyContain(t => t == CancellationToken.None, "no write observes the caller's token");
    }

    public static TheoryData<string, int, string> SupportingReadRefusals => new()
    {
        { "gone", StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable },
        { "no_file", StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable },
        { "storage_unverified", StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable },
        { "another_communication", StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable },
        { "not_downloaded", StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable },
        { "fault", StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable },
        { "store_not_configured", StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.StorageNotConfigured },
        { "too_large", StatusCodes.Status413PayloadTooLarge, InvoiceSubmissionErrors.TooLarge },
    };

    [Theory(DisplayName = "118b-2 (must fire): a supporting document that cannot be copied (gone, no file, unverified storage, another communication) is 422, one that cannot be read now 503, one over the file limit 413 (review F1-b), an unconfigured store 422; all BEFORE step A: nothing filed, staged or created")]
    [MemberData(nameof(SupportingReadRefusals))]
    public async Task NewAttempt_ASupportingDocumentCannotBeRead_IsRefusedBeforeStepA(string state, int status, string code)
    {
        var h = NewHarness();
        var documents = new Documents { Fault = state == "fault" ? new TimeoutException("dataverse") : null };
        documents.Files[B] = state switch
        {
            "gone" => new SourceDocumentFile(SourceDocumentFileOutcome.Gone),
            "no_file" => new SourceDocumentFile(SourceDocumentFileOutcome.NoFile),
            "storage_unverified" => new SourceDocumentFile(SourceDocumentFileOutcome.StorageUnverified),
            "not_downloaded" => new SourceDocumentFile(SourceDocumentFileOutcome.NotDownloaded),
            "too_large" => new SourceDocumentFile(SourceDocumentFileOutcome.TooLarge),
            "another_communication" => Documents.Readable(B) with { CommunicationId = Guid.NewGuid() },
            _ => Documents.Readable(B),
        };
        if (state == "store_not_configured")
        {
            h.Files = new StoredFileRig(configured: false);
        }

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        outcome.StatusCode.Should().Be(status);
        outcome.ErrorCode.Should().Be(code);
        documents.Events.Should().NotContain("step A");
        if (state == "store_not_configured")
        {
            documents.Events.Should().BeEmpty("the store is checked before any document is read");
        }

        outcome.Detail.Should().NotContain(B.ToString()).And.NotContain(A.ToString(), "the answer names no document");
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "118b review F1-b (must fire): every supporting document is read with the configured per-file limit (the upload's MaxFileBytes), so the shared read can refuse an oversize file")]
    public async Task NewAttempt_ReadsEverySupportingDocument_WithTheConfiguredFileLimit()
    {
        var h = NewHarness();
        h.Options.MaxSubmissionFileSizeMb = 3;
        var documents = new Documents();

        (await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);

        documents.MaxBytes.Should().Equal(3L * 1024 * 1024, 3L * 1024 * 1024);
    }

    /// <summary>The refusal shape every supporting-document check shares: before any file is read or step A, nothing written, no document named.</summary>
    private static void ShouldBeRefusedBeforeAnyRead(Harness h, Documents documents, InvoiceSubmissionOutcome outcome, int status, string code)
    {
        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(status);
        outcome.ErrorCode.Should().Be(code);
        documents.Events.Should().BeEmpty("the filing of every supporting document is judged before any file is read, and before step A");
        outcome.Detail.Should().NotContain(A.ToString()).And.NotContain(B.ToString()).And.NotContain(SecureMatterS.ToString(), "the answer names no document or record");
        ShouldHaveWrittenNothing(h);
    }

    /// <summary>A Secure matter other than the intake's (the review's S).</summary>
    private static readonly Guid SecureMatterS = Guid.NewGuid();

    [Fact(DisplayName = "118b review F1-a (must fire, Mismatch by the filing chain): a supporting document filed under ANOTHER core record is 409 document_core_record_mismatch before any read or step A; nothing staged")]
    public async Task NewAttempt_ASupportingDocumentFiledUnderAnotherRecord_Is409_NothingStaged()
    {
        var h = NewHarness();
        h.OtherDocuments[B] = new Entity("sprk_document", B) { ["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid()) };
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        ShouldBeRefusedBeforeAnyRead(h, documents, outcome, StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "118b review F1-a (must fire, Mismatch by isolation): a supporting document unfiled by the columns but isolated under another Secure matter's thread is 409, as the invoice document is")]
    public async Task NewAttempt_ASupportingDocumentIsolatedUnderAnotherSecureMatter_Is409_NothingStaged()
    {
        var h = NewHarness();
        var (communication, thread) = (Guid.NewGuid(), Guid.NewGuid());
        h.Isolation
            .SecureRoot("sprk_matter", SecureMatterS)
            .OrdinaryRoot("sprk_matter", InvoiceSubmissionServiceTests.MatterId)
            .SecureChild("sprk_communicationthread", thread, ("sprk_regardingmatter", "sprk_matter", SecureMatterS))
            .SecureChild("sprk_communication", communication, ("sprk_communicationthread", "sprk_communicationthread", thread))
            .SecureChild("sprk_document", B, ("sprk_relatedcommunication", "sprk_communication", communication));
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        ShouldBeRefusedBeforeAnyRead(h, documents, outcome, StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "118b review F1-a (must fire, Unavailable): a supporting document whose isolation cannot be read is 503 record_unavailable - never 'not isolated' - before any read or step A")]
    public async Task NewAttempt_ASupportingDocumentWhoseIsolationCannotBeRead_Is503_NothingStaged()
    {
        var h = NewHarness();
        var (communication, thread) = (Guid.NewGuid(), Guid.NewGuid());
        h.Isolation
            .SecureRoot("sprk_matter", SecureMatterS)
            .SecureChild("sprk_communicationthread", thread, ("sprk_regardingmatter", "sprk_matter", SecureMatterS))
            .SecureChild("sprk_communication", communication, ("sprk_communicationthread", "sprk_communicationthread", thread))
            .SecureChild("sprk_document", B, ("sprk_relatedcommunication", "sprk_communication", communication))
            .FailingQueriesOf("sprk_communication");
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        ShouldBeRefusedBeforeAnyRead(h, documents, outcome, StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable);
    }

    [Fact(DisplayName = "118b review F1-a (must fire, fault): a supporting document whose row cannot be read is 503 record_unavailable before any read or step A")]
    public async Task NewAttempt_ASupportingDocumentWhoseRowCannotBeRead_Is503_NothingStaged()
    {
        var h = NewHarness();
        h.Entities.Setup(e => e.RetrieveAsync("sprk_document", B, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse"));
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        ShouldBeRefusedBeforeAnyRead(h, documents, outcome, StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable);
    }

    [Fact(DisplayName = "118b review F1-a (must fire, Gone): a supporting document that no longer exists is 422 document_unavailable (as the shared read answers it) before any read or step A")]
    public async Task NewAttempt_ASupportingDocumentThatNoLongerExists_Is422_NothingStaged()
    {
        var h = NewHarness();
        h.OtherDocuments[B] = null!; // the row read answers "not found"
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None);

        ShouldBeRefusedBeforeAnyRead(h, documents, outcome, StatusCodes.Status422UnprocessableEntity, SubmissionReasonCodes.DocumentUnavailable);
    }

    [Fact(DisplayName = "118b review F1-a (must not fire): supporting documents UNFILED, or filed under the intake's own record, are allowed and staged (an unfiled document stays Allowed)")]
    public async Task NewAttempt_SupportingDocumentsUnfiledOrFiledUnderTheSameRecord_AreAllowed()
    {
        var h = NewHarness();
        h.OtherDocuments[B] = new Entity("sprk_document", B) { ["sprk_matter"] = new EntityReference("sprk_matter", InvoiceSubmissionServiceTests.MatterId) };
        var documents = new Documents();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(documents, A, B), CancellationToken.None); // A is unfiled

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Stages.Should().HaveCount(2);
    }

    [Fact(DisplayName = "118b-2 (seed-proof, StoredFileBlobs.SameSource's parts list; must fire): a stage left under this attempt's ids by a request with ANOTHER set (no row created) is 409 document_already_submitted - with fewer documents, more, or none - never committed as extra files; the same set reuses it")]
    public async Task NewAttempt_ALeftoverStageOfAnotherSet_Is409_NeverExtraFiles()
    {
        var h = NewHarness();
        h.Allocation = TrackingNumberResult.Refused("test: no tracking number");
        (await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A, B), CancellationToken.None)).StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        Part(h, SubmissionId, 1).Should().NotBeNull("the refused request left its parts (no compensation)");
        h.Allocation = TrackingNumberResult.Created("INV-1.1", 1);
        var allocationsBefore = h.Allocations.Count;

        var fewer = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A), CancellationToken.None);
        var more = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A, B, C), CancellationToken.None);
        var none = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents()), CancellationToken.None);

        foreach (var refused in new[] { fewer, more, none })
        {
            refused.StatusCode.Should().Be(StatusCodes.Status409Conflict);
            refused.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        }

        h.Allocations.Count.Should().Be(allocationsBefore, "no submission is created over another set's parts");
        Part(h, SubmissionId, 1)!.Metadata[StoredFileBlobs.MetadataEmlParts].Should().Be("1,2", "the leftover is left unchanged");
        Part(h, SubmissionId, 3).Should().BeNull();

        var same = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), B, A), CancellationToken.None);
        same.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received, "the same set reuses its own stage (a retry)");
    }

    [Fact(DisplayName = "118b-2 (must fire): a leftover whose parts do not read back (a request that stopped between two stages) is still 409 for a request without supporting documents, never a 503 retried for ever")]
    public async Task NewAttempt_WithoutSupportingDocuments_AnIncompleteLeftover_Is409()
    {
        var h = NewHarness();
        h.Allocation = TrackingNumberResult.Refused("test: no tracking number");
        await h.Build().SubmitExistingDocumentAsync(Submit(new Documents(), A, B), CancellationToken.None);
        Part(h, SubmissionId, 2)!.Metadata.Clear();
        h.Allocation = TrackingNumberResult.Created("INV-1.1", 1);
        var allocationsBefore = h.Allocations.Count;

        var outcome = await h.Build().SubmitExistingDocumentAsync(Submit(new Documents()), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.Allocations.Count.Should().Be(allocationsBefore);
    }

    [Fact(DisplayName = "118b-2 (must not fire): a store that is not configured holds no parts, so a request without supporting documents is not refused for them (unchanged)")]
    public async Task NewAttempt_WithoutSupportingDocuments_AStoreNotConfigured_IsUnchanged()
    {
        var h = NewHarness();
        h.Files = new StoredFileRig(configured: false);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-1.1", DocumentId));
        h.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "118b (must not fire): a new attempt without supporting documents is created exactly as before (the empty default)")]
    public async Task NewAttempt_WithoutSupportingDocuments_IsUnchanged()
    {
        var h = NewHarness();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-1.1", DocumentId));
        h.Allocations.Should().ContainSingle().Which.SubmissionId.Should().Be(SubmissionId);
    }

    [Theory(DisplayName = "118b: the set comparison ignores order and repeats; two empty sets are the same")]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a", "a" }, true)]
    [InlineData(new string[0], new string[0], true)]
    [InlineData(new[] { "a" }, new string[0], false)]
    [InlineData(new[] { "a" }, new[] { "b" }, false)]
    public void SameSupportingDocuments_IsSetEquality(string[] earlier, string[] requested, bool same)
    {
        static Guid Of(string key) => key == "a" ? A : B;

        InvoiceSubmissionService.SameSupportingDocuments(earlier.Select(Of).ToArray(), requested.Select(Of).ToArray()).Should().Be(same);
    }
}
