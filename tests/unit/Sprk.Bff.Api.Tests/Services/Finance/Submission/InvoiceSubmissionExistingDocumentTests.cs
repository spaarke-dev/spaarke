using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 050a (D-134): <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>, the existing-document entry the
/// review-confirm route calls. It runs the intake's checks (number, firm, core record, firm link, owner), does no upload and no document
/// create, and is idempotent by document.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the confirm route's intake guarantees (one submission per document, no second copy of the PDF, the firm-link
/// rule and D-91) are business invariants of the only invoice creator's entry. Dataverse, the allocator and the queue are doubles (the
/// <see cref="InvoiceSubmissionServiceTests.Harness"/>).
/// </remarks>
public sealed class InvoiceSubmissionExistingDocumentTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;
    private static readonly Guid SubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId);
    private static readonly Guid JobId = InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId);

    private static Harness NewHarness() => new()
    {
        ExistingDocument = new Entity("sprk_document", DocumentId)
        {
            ["sprk_graphdriveid"] = "b!drive",
            ["sprk_graphitemid"] = "item-9",
        },
    };

    private static InvoiceSubmissionRequest Confirm(string? number = "INV-7", Guid? firm = null) => new()
    {
        FirmId = firm ?? FirmId,
        CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, MatterId),
        Channel = InvoiceSubmissionChannel.Internal,
        FirmInvoiceNumber = number,
        ExistingDocumentId = DocumentId,
        CallerObjectId = Guid.NewGuid(),
        CorrelationId = "corr-confirm",
    };

    private static Entity Earlier(string status = "Received", string number = "INV-7", Guid? firm = null, Guid? matter = null) => new("sprk_invoicesubmission", SubmissionId)
    {
        [TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.3",
        [TrackingNumberAllocator.FirmInvoiceNumberColumn] = number,
        [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", firm ?? FirmId),
        ["sprk_processingstatus"] = new OptionSetValue((int)Enum.Parse<InvoiceSubmissionStatus>(status)),
        ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
        ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter ?? MatterId),
    };

    private static void ShouldHaveWrittenNothing(Harness h)
    {
        h.Allocations.Should().BeEmpty("no submission may be created");
        h.JobsSent.Should().BeEmpty("no job may be queued");
        h.Uploads.Should().BeEmpty();
        h.DocumentCreates.Should().BeEmpty();
        h.ExternalCreates.Should().BeEmpty();
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: the existing-document entry runs the checks, creates one Internal submission pointing at the document, queues its job, and uploads nothing")]
    public async Task ExistingDocument_RunsTheChecks_CreatesTheSubmission_WithNoUploadAndNoDocumentCreate()
    {
        var h = NewHarness();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-1.1", DocumentId));

        // The checks: the firm link (D-110) was read for the matter and the owner was resolved.
        h.LinkReads.Should().ContainSingle().Which.Entity.Should().Be("sprk_matter");
        h.OwnerContexts.Should().ContainSingle();

        // D-134: no second copy of the PDF.
        h.Uploads.Should().BeEmpty();
        h.DocumentCreates.Should().BeEmpty();
        h.ExternalCreates.Should().BeEmpty();

        var allocation = h.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(SubmissionId, "the dedupe key: the submission id is derived from the document");
        allocation.FirmId.Should().Be(FirmId);
        allocation.Number.Should().Be("INV-7");
        allocation.Fields!["sprk_document"].Should().Be(new EntityReference("sprk_document", DocumentId));
        ((OptionSetValue)allocation.Fields["sprk_channel"]).Value.Should().Be((int)InvoiceSubmissionChannel.Internal);
        ((OptionSetValue)allocation.Fields["sprk_processingstatus"]).Value.Should().Be((int)InvoiceSubmissionStatus.Received);
        allocation.Fields["sprk_regardingmatter"].Should().Be(new EntityReference("sprk_matter", MatterId));

        var job = h.JobsSent.Should().ContainSingle().Subject;
        job.JobId.Should().Be(JobId);
        job.JobType.Should().Be(InvoiceSubmissionService.JobType);
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: the writes ignore the caller's token once the checks have passed")]
    public async Task ExistingDocument_WritesRunUnderCancellationTokenNone()
    {
        var h = NewHarness();
        using var caller = new CancellationTokenSource();

        await h.Build().SubmitExistingDocumentAsync(Confirm(), caller.Token);

        h.WriteTokens.Should().NotBeEmpty().And.OnlyContain(t => t == CancellationToken.None);
    }

    [Fact(DisplayName = "050a: a retry for the same document, firm, matter and number returns the earlier submission and creates nothing")]
    public async Task Retry_ReturnsTheEarlierSubmission_AndCreatesNothing()
    {
        var h = NewHarness();
        h.Submission = Earlier(status: "Accepted");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(number: " inv-7 "), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.3", DocumentId));
        h.SubmissionReads.Should().Equal(SubmissionId);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: a retry that finds the earlier submission still Received re-queues its job (a lost enqueue is completed) and creates nothing")]
    public async Task Retry_OfAReceivedSubmission_RequeuesItsJob_AndCreatesNothing()
    {
        var h = NewHarness();
        h.Submission = Earlier(status: "Received");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Receipt!.SubmissionId.Should().Be(SubmissionId);
        h.Allocations.Should().BeEmpty();
        h.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(JobId);
    }

    public static TheoryData<string, string?> OtherDetails => new()
    {
        { "number", "INV-8" },
        { "firm", null },
    };

    [Theory(DisplayName = "050a: a confirm of an already-submitted document with another number or firm is 409 and writes nothing")]
    [MemberData(nameof(OtherDetails))]
    public async Task Retry_WithOtherDetails_Is409_AndWritesNothing(string what, string? number)
    {
        var h = NewHarness();
        h.Submission = what == "firm" ? Earlier(firm: Guid.NewGuid()) : Earlier();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(number: number ?? "INV-7"), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        ShouldHaveWrittenNothing(h);
    }

    [Theory(DisplayName = "050a / D-91: a missing or blank firm invoice number is 400 before anything is read or written")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingInvoiceNumber_Is400_BeforeAnyReadOrWrite(string? number)
    {
        var h = NewHarness();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(number: number), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmInvoiceNumberRequired);
        h.SubmissionReads.Should().BeEmpty();
        h.LinkReads.Should().BeEmpty();
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a / D-110: a firm that is not one of the matter's assigned law firms is 403 firm_not_linked and nothing is written")]
    public async Task FirmNotLinked_Is403_AndWritesNothing()
    {
        var h = NewHarness();
        h.Links.Clear();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: a linked firm (negative control of firm_not_linked) is received")]
    public async Task FirmLinked_IsReceived()
    {
        var h = NewHarness();
        var other = Guid.NewGuid();
        h.Links[("sprk_matter", MatterId)] = new[] { other, FirmId };

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a: unreadable firm links are 503 firm_unverifiable and nothing is written")]
    public async Task FirmLinksUnreadable_Is503_AndWritesNothing()
    {
        var h = NewHarness();
        h.LinksUnreadable = true;

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmUnverifiable);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: an inactive firm is refused and nothing is written")]
    public async Task InactiveFirm_IsRefused_AndWritesNothing()
    {
        var h = NewHarness();
        h.Firm = new Entity("sprk_organization", FirmId) { ["statecode"] = new OptionSetValue(1) };

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.FirmInvalid);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: no owner team is OwnerRefused and nothing is written")]
    public async Task OwnerRefused_WritesNothing()
    {
        var h = NewHarness();
        h.OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "no team");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.OwnerRefused);
        ShouldHaveWrittenNothing(h);
    }

    public static TheoryData<string, int, string> DocumentRefusals => new()
    {
        { "missing", StatusCodes.Status404NotFound, InvoiceSubmissionErrors.DocumentNotFound },
        { "invoiced", StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadyInvoiced },
        { "no-file", StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.DocumentHasNoFile },
        { "unreadable", StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable },
    };

    [Theory(DisplayName = "050a: a document that is missing, already an invoice's, has no file or cannot be read is refused before any write")]
    [MemberData(nameof(DocumentRefusals))]
    public async Task DocumentRefusal_WritesNothing(string state, int status, string errorCode)
    {
        var h = NewHarness();
        switch (state)
        {
            case "missing":
                h.ExistingDocument = null;
                break;
            case "invoiced":
                h.ExistingDocument!["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
                break;
            case "no-file":
                h.ExistingDocument!["sprk_graphitemid"] = null;
                break;
            default:
                h.ExistingDocumentReadFailure = new HttpRequestException("dataverse down");
                break;
        }

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(status);
        outcome.ErrorCode.Should().Be(errorCode);
        outcome.Detail.Should().NotContain(DocumentId.ToString());
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: a concurrent confirm that created the same id first (AlreadyCreated) answers Received and (re)queues the job")]
    public async Task ConcurrentCreate_AlreadyCreated_IsReceived_AndQueuesTheJob()
    {
        var h = NewHarness();
        h.Allocation = TrackingNumberResult.AlreadyCreated("INV-7.2", 2);
        h.StoredOnCreate = Earlier(status: "Received");
        h.StoredOnCreate[TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.2";

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.2", DocumentId));
        h.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(JobId);
        h.Deleted.Should().BeEmpty();
    }

    // ── 050a review #3: the document's own core record ──

    [Theory(DisplayName = "050a review #3: a document filed under another core record is 409 document_core_record_mismatch and nothing is written")]
    [InlineData("sprk_matter", "sprk_matter")]
    [InlineData("sprk_project", "sprk_project")]
    [InlineData("sprk_workassignment", "sprk_workassignment")]
    public async Task DocumentFiledUnderAnotherCoreRecord_Is409_AndWritesNothing(string column, string table)
    {
        var h = NewHarness();
        h.ExistingDocument![column] = new EntityReference(table, Guid.NewGuid());

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch).And.Be("ebilling.submission.document_core_record_mismatch");
        ShouldHaveWrittenNothing(h);
        h.LinkReads.Should().BeEmpty("refused before the intake's checks");
    }

    [Fact(DisplayName = "050a review #3: a document filed under the confirm's own matter is received (negative control)")]
    public async Task DocumentFiledUnderTheSameMatter_IsReceived()
    {
        var h = NewHarness();
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", MatterId);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a review #3 (owner 2026-10-20, D-139): an UNFILED document (no core lookup) may be confirmed")]
    public async Task UnfiledDocument_IsReceived()
    {
        var h = NewHarness();
        h.ExistingDocument!.Attributes.Keys.Should().NotContain(["sprk_matter", "sprk_project", "sprk_workassignment"]);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Allocations.Should().ContainSingle();
    }

    // ── D-140 (owner 2026-10-20, uac-r2 item 1): the EFFECTIVE core record, through the filing chain ──

    /// <summary>An email attachment as the communication writers create it: ONLY sprk_relatedcommunication, no core lookup of its own.</summary>
    private static (Harness Harness, Guid Communication) EmailAttachment(Guid? regardingMatter)
    {
        var h = NewHarness();
        var communication = Guid.NewGuid();
        h.ExistingDocument!["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communication);
        var row = new Entity("sprk_communication", communication);
        if (regardingMatter is { } matter)
        {
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
        }

        h.Communications[communication] = row;
        return (h, communication);
    }

    [Fact(DisplayName = "D-140: an email attachment whose communication is filed under matter A, confirmed onto matter B, is 409 document_core_record_mismatch")]
    public async Task EmailAttachment_CommunicationUnderAnotherMatter_Is409()
    {
        var (h, _) = EmailAttachment(regardingMatter: Guid.NewGuid());

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "D-140: an email attachment whose communication is filed under the confirm's own matter is received")]
    public async Task EmailAttachment_CommunicationUnderTheSameMatter_IsReceived()
    {
        var (h, _) = EmailAttachment(regardingMatter: MatterId);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Theory(DisplayName = "D-140: an email attachment whose communication is under ANOTHER project or work assignment is 409 too")]
    [InlineData("sprk_regardingproject", "sprk_project")]
    [InlineData("sprk_regardingworkassignment", "sprk_workassignment")]
    public async Task EmailAttachment_CommunicationUnderAnotherCoreRecordKind_Is409(string column, string table)
    {
        var (h, communication) = EmailAttachment(regardingMatter: null);
        h.Communications[communication][column] = new EntityReference(table, Guid.NewGuid());

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "D-140 / D-139: an email attachment whose communication has no regarding record is unfiled and may be confirmed")]
    public async Task EmailAttachment_CommunicationWithNoRegarding_IsReceived()
    {
        var (h, _) = EmailAttachment(regardingMatter: null);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "D-140: the document's OWN core lookup decides before its communication (the communication is not read)")]
    public async Task OwnCoreLookup_DecidesBeforeTheCommunication()
    {
        var (h, _) = EmailAttachment(regardingMatter: Guid.NewGuid());
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        h.CommunicationReadFailure = new InvalidOperationException("the communication must not be read");

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "D-140: a document filed through the sprk_related* twin (sprk_relatedmatter) under another matter is 409")]
    public async Task RelatedMatterTwin_UnderAnotherMatter_Is409()
    {
        var h = NewHarness();
        h.ExistingDocument!["sprk_relatedmatter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "D-140: a parent-document chain is resolved: an attachment with no link of its own follows its parent's communication (409 for another matter)")]
    public async Task ParentDocumentChain_IsResolved_ThroughTheParentsCommunication()
    {
        var h = NewHarness();
        var parent = Guid.NewGuid();
        var communication = Guid.NewGuid();
        h.ExistingDocument!["sprk_parentdocument"] = new EntityReference("sprk_document", parent);
        h.OtherDocuments[parent] = new Entity("sprk_document", parent)
        {
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", communication),
        };
        h.Communications[communication] = new Entity("sprk_communication", communication)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()),
        };

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "D-140: a parent-document chain that resolves to the confirm's own matter is received (negative control)")]
    public async Task ParentDocumentChain_ToTheSameMatter_IsReceived()
    {
        var h = NewHarness();
        var parent = Guid.NewGuid();
        h.ExistingDocument!["sprk_parentdocument"] = new EntityReference("sprk_document", parent);
        h.OtherDocuments[parent] = new Entity("sprk_document", parent) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) };

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "D-140: a chain longer than the bound (4 documents) or one that loops is refused fail-closed (409), nothing written")]
    public async Task ParentDocumentChain_PastTheBoundOrLooping_IsRefused()
    {
        var deep = NewHarness();
        var row = deep.ExistingDocument!;
        for (var i = 0; i < InvoiceSubmissionService.MaxFilingChainDocuments; i++)
        {
            var parent = Guid.NewGuid();
            row["sprk_parentdocument"] = new EntityReference("sprk_document", parent);
            row = deep.OtherDocuments[parent] = new Entity("sprk_document", parent);
        }

        row["sprk_matter"] = new EntityReference("sprk_matter", MatterId); // filed, but one document past the bound

        (await deep.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(deep);

        var loop = NewHarness();
        var other = Guid.NewGuid();
        loop.ExistingDocument!["sprk_parentdocument"] = new EntityReference("sprk_document", other);
        loop.OtherDocuments[other] = new Entity("sprk_document", other) { ["sprk_parentdocument"] = new EntityReference("sprk_document", DocumentId) };

        (await loop.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "D-140: a communication that cannot be read is 503 (fail closed), nothing written")]
    public async Task EmailAttachment_CommunicationUnreadable_Is503()
    {
        var (h, _) = EmailAttachment(regardingMatter: MatterId);
        h.CommunicationReadFailure = new HttpRequestException("dataverse down");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        ShouldHaveWrittenNothing(h);
    }

    // ── 050a review #4: a retry after the submission ended ──

    [Theory(DisplayName = "050a review #4 (owner 2026-10-20, D-139): a retry whose submission ended Failed or Rejected is 409 submission_already_ended naming the status; nothing is written or queued")]
    [InlineData("Failed")]
    [InlineData("Rejected")]
    public async Task Retry_AfterTheSubmissionEnded_Is409_NamingTheStatus(string status)
    {
        var h = NewHarness();
        h.Submission = Earlier(status: status);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded).And.Be("ebilling.submission.submission_already_ended");
        outcome.Detail.Should().Contain(status);
        outcome.Receipt.Should().BeNull("the review service marks the document only on Received");
        ShouldHaveWrittenNothing(h);
    }

    // ── 050a review #5: the concurrent-create path compares the core record too ──

    [Fact(DisplayName = "050a review #5: AlreadyCreated by a concurrent confirm for ANOTHER matter is 409 document_already_submitted and queues nothing")]
    public async Task ConcurrentCreate_AlreadyCreated_ForAnotherMatter_Is409()
    {
        var h = NewHarness();
        h.Allocation = TrackingNumberResult.AlreadyCreated("INV-7.2", 2);
        h.StoredOnCreate = Earlier(status: "Received", matter: Guid.NewGuid());

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.JobsSent.Should().BeEmpty("the other confirm's submission is not ours to queue");
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a review #5: AlreadyCreated whose row already ended Failed is 409 submission_already_ended")]
    public async Task ConcurrentCreate_AlreadyCreated_AlreadyEnded_Is409()
    {
        var h = NewHarness();
        h.Allocation = TrackingNumberResult.AlreadyCreated("INV-7.2", 2);
        h.StoredOnCreate = Earlier(status: "Failed");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded);
        h.JobsSent.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: a concurrent confirm with other details (the allocator's reconcile) is 409 and nothing is deleted")]
    public async Task ConcurrentCreate_WithOtherDetails_Is409_AndDeletesNothing()
    {
        var h = NewHarness();
        h.AllocatorFailure = new InvalidOperationException("exists for another firm or number");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.JobsSent.Should().BeEmpty();
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: a queue failure after the create is 503 failed and deletes nothing (a retry re-queues the same submission)")]
    public async Task QueueFailure_Is503_AndDeletesNothing()
    {
        var h = NewHarness();
        h.JobFailure = new InvalidOperationException("service bus down");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        h.Allocations.Should().ContainSingle();
        h.Deleted.Should().BeEmpty();
    }

    [Fact(DisplayName = "050a: the entry refuses a request that carries a file or is not Internal (programming errors, thrown)")]
    public async Task Guards_FileOrChannel_Throw()
    {
        var h = NewHarness();
        var service = h.Build();

        var withFile = () => service.SubmitExistingDocumentAsync(
            Confirm() with { File = new InvoiceSubmissionFile("a.pdf", "application/pdf", new byte[] { 1 }) }, CancellationToken.None);
        var spa = () => service.SubmitExistingDocumentAsync(Confirm() with { Channel = InvoiceSubmissionChannel.Spa }, CancellationToken.None);
        var noDocument = () => service.SubmitExistingDocumentAsync(Confirm() with { ExistingDocumentId = null }, CancellationToken.None);

        await withFile.Should().ThrowAsync<ArgumentException>();
        await spa.Should().ThrowAsync<ArgumentException>();
        await noDocument.Should().ThrowAsync<ArgumentException>();
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a: the dedupe ids are deterministic per document and distinct across documents")]
    public void DedupeIds_AreDeterministicPerDocument()
    {
        var other = Guid.NewGuid();
        InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId).Should().Be(SubmissionId);
        InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(other).Should().NotBe(SubmissionId);
        InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId).Should().NotBe(SubmissionId);
    }
}
