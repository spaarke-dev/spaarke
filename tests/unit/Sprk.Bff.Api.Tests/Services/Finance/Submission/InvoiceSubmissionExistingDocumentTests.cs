using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
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
        h.Stages.Should().BeEmpty();
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
        h.Stages.Should().BeEmpty();

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

    // ── 050a review round 4 (uac-r2 F1): the communication's THREAD files it too: the union of both, as SecureChildReconciler.ContextFor ──

    /// <summary>
    /// An email attachment whose communication sits on a thread. The communication's own regarding matter is
    /// <paramref name="communicationMatter"/> (null: none, a thread-only reply); the thread's is <paramref name="threadMatter"/>. Not isolated
    /// (the isolation world holds no row of it), so only the D-140 chain decides.
    /// </summary>
    private static (Harness Harness, Guid Thread) ThreadReply(Guid? communicationMatter, Guid threadMatter, int? threadType = null)
    {
        var (h, communication) = EmailAttachment(communicationMatter);
        var thread = Guid.NewGuid();
        h.Communications[communication]["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread);
        h.Threads[thread] = new Entity("sprk_communicationthread", thread)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", threadMatter),
        };
        if (threadType is { } type)
        {
            h.Threads[thread]["sprk_threadtype"] = new OptionSetValue(type);
        }

        return (h, thread);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F1, must fire): a thread-only reply on ordinary matter S's thread (the communication names no record) confirmed onto M is 409 document_core_record_mismatch")]
    public async Task ThreadOnlyReply_UnderAnotherMatter_Is409_AndWritesNothing()
    {
        var (h, _) = ThreadReply(communicationMatter: null, threadMatter: Guid.NewGuid());

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F1, must not fire): a thread-only reply on the confirm's own matter's thread is received")]
    public async Task ThreadOnlyReply_UnderTheConfirmsMatter_IsReceived()
    {
        var (h, _) = ThreadReply(communicationMatter: null, threadMatter: MatterId);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F1, the union): a communication regarding M on S's thread is filed under BOTH, so it is 409 confirmed onto M and 409 confirmed onto S")]
    public async Task CommunicationRegardingM_OnSsThread_IsRefusedForMAndForS()
    {
        var threadMatterS = Guid.NewGuid();

        var (ontoM, _) = ThreadReply(communicationMatter: MatterId, threadMatter: threadMatterS);
        var refusedForM = await ontoM.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);
        refusedForM.StatusCode.Should().Be(StatusCodes.Status409Conflict, "the thread files it under S as well");
        refusedForM.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(ontoM);

        var (ontoS, _) = ThreadReply(communicationMatter: MatterId, threadMatter: threadMatterS);
        var refusedForS = await ontoS.Build().SubmitExistingDocumentAsync(
            Confirm() with { CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, threadMatterS) }, CancellationToken.None);
        refusedForS.StatusCode.Should().Be(StatusCodes.Status409Conflict, "the communication's own regarding files it under M as well");
        refusedForS.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(ontoS);
    }

    // -- D-140 thread hop = RecordThreadFiling.ParentsOf (#2143 round 7d): the thread's TYPE decides whether it files its messages --

    private const int RecordThreadType = 100000000;
    private const int DirectThreadType = 100000001;

    [Fact(DisplayName = "D-140 / ParentsOf (must not fire): a communication regarding M in a DIRECT thread whose row carries regarding S is filed under M only, so the confirm onto M is received (the over-refusal)")]
    public async Task DirectThread_CarryingAnotherRecord_FilesNothing_ConfirmOntoM_IsReceived()
    {
        var (h, _) = ThreadReply(communicationMatter: MatterId, threadMatter: Guid.NewGuid(), threadType: DirectThreadType);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received, "a Direct thread (the per-user master included) files its messages under nothing");
    }

    [Fact(DisplayName = "D-140 / ParentsOf (must not fire): a thread-only reply in a DIRECT thread carrying a record files nothing, so it is unfiled and received for any record")]
    public async Task DirectThread_OnlyReply_IsUnfiled_IsReceived()
    {
        var (h, _) = ThreadReply(communicationMatter: null, threadMatter: Guid.NewGuid(), threadType: DirectThreadType);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Theory(DisplayName = "D-140 / ParentsOf (must fire): a communication regarding M in a RECORD thread, or one whose type is unset or unknown (fail closed), whose row carries regarding S is 409 document_core_record_mismatch")]
    [InlineData(RecordThreadType)]
    [InlineData(null)]
    [InlineData(7)]
    [InlineData(0)]
    public async Task RecordUnsetOrUnknownThreadType_CarryingAnotherRecord_ConfirmOntoM_Is409(int? threadType)
    {
        var (h, _) = ThreadReply(communicationMatter: MatterId, threadMatter: Guid.NewGuid(), threadType: threadType);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F1): a communication thread that cannot be read is 503 (fail closed, as a communication read fault), nothing written")]
    public async Task ThreadUnreadable_Is503_AndWritesNothing()
    {
        var (h, _) = ThreadReply(communicationMatter: null, threadMatter: MatterId);
        h.ThreadReadFailure = new HttpRequestException("dataverse down");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F1): a thread that no longer exists files nothing (as a gone communication), so a reply naming no record is received")]
    public async Task ThreadGone_FilesNothing_IsReceived()
    {
        var (h, thread) = ThreadReply(communicationMatter: null, threadMatter: Guid.NewGuid());
        h.Threads.Remove(thread);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    // ── 050a review #4: a retry after the submission ended ──

    [Fact(DisplayName = "050a review #4 (D-139, must not fire for EB-D-167): a retry whose submission ended Rejected is 409 submission_already_ended; nothing is written or queued")]
    public async Task Retry_AfterRejected_Is409_SubmissionAlreadyEnded()
    {
        var h = NewHarness();
        h.Submission = Earlier(status: "Rejected");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded).And.Be("ebilling.submission.submission_already_ended");
        outcome.Detail.Should().Contain("rejected");
        outcome.Receipt.Should().BeNull("the review service marks the document only on Received");
        ShouldHaveWrittenNothing(h);
    }

    // ── EB-D-167 (owner 2026-10-28; closes ISS-012 #2374): after a Failed attempt the confirm starts a NEW attempt ──

    private static readonly Guid SecondAttempt = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 2);
    private static readonly Guid SecondJob = InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId, 2);

    /// <summary>An earlier attempt row at <paramref name="id"/> (created by <paramref name="creator"/>), as the undo leaves a committed one.</summary>
    private static Entity Attempt(Guid id, string status, Guid? creator = null, bool undone = false)
    {
        var row = Earlier(status: status);
        row.Id = id;
        if (undone)
        {
            row.Attributes.Remove(TrackingNumberAllocator.FirmInvoiceNumberColumn);
        }

        if (creator is { } person)
        {
            row[RecordCreatorPerson.Column] = new EntityReference("systemuser", person);
        }

        return row;
    }

    [Fact(DisplayName = "EB-D-167 (must fire): a retry after the document's submission ended Failed is a NEW attempt - generation 2's id and job, every check run again, created by this caller, the allocator numbering it")]
    public async Task Retry_AfterFailed_IsANewAttempt()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed", undone: true);
        var caller = Guid.NewGuid();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { CallerObjectId = caller }, CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.SubmissionId.Should().Be(SecondAttempt);
        outcome.Receipt.JobId.Should().Be(SecondJob);
        var allocation = h.Allocations.Should().ContainSingle().Subject;
        allocation.SubmissionId.Should().Be(SecondAttempt, "the next generation, numbered by the allocator ({number}.{n+1})");
        allocation.Fields!["sprk_document"].Should().BeEquivalentTo(new EntityReference("sprk_document", DocumentId));
        h.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(SecondJob);
        h.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.ObjectId.Should().Be(caller, "the new attempt is created by THIS caller");
        h.SubmissionReads.Should().Equal(SubmissionId, SecondAttempt);
    }

    [Fact(DisplayName = "EB-D-167 (must fire): another user's Failed attempt on the confirm route gives a new attempt by THIS caller (the creator of the failed one is not assumed or required)")]
    public async Task AnotherUsersFailedAttempt_GivesANewAttemptByThisCaller()
    {
        var h = NewHarness();
        h.OwnerAnswer = RecordOwnerResolution.Owned(Guid.NewGuid()) with { CreatedByPerson = Guid.Parse("0000c0de-0000-4000-8000-000000000167") };
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed", creator: Guid.NewGuid());
        var caller = Guid.NewGuid();

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { CallerObjectId = caller }, CancellationToken.None);

        outcome.Receipt!.SubmissionId.Should().Be(SecondAttempt);
        h.OwnerContexts.Should().ContainSingle().Which.RequestedBy!.ObjectId.Should().Be(caller);
        h.Allocations.Single().Fields![RecordCreatorPerson.Column].Should().BeEquivalentTo(
            new EntityReference("systemuser", Guid.Parse("0000c0de-0000-4000-8000-000000000167")), "sprk_createdbyperson is this caller's person");
    }

    // ── Review round 5 (pass-2 F1-1): no new attempt while the failed one's undo is unfinished ──

    /// <summary>A Failed attempt the commit gate failed with <paramref name="reasonCode"/>; <paramref name="linked"/>: its undo is unfinished
    /// (the invoice link stays until the undo completes).</summary>
    private static Entity FailedByTheGate(string reasonCode, bool linked)
    {
        var row = Attempt(SubmissionId, "Failed");
        row[InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode;
        if (linked)
        {
            row[InvoiceSubmissionService.InvoiceColumn] = new EntityReference("sprk_invoice", InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId));
        }
        else
        {
            row.Attributes.Remove(TrackingNumberAllocator.FirmInvoiceNumberColumn);
        }

        return row;
    }

    [Theory(DisplayName = "Round 5 (pass-2 F1-1, must fire): a retry while the failed attempt's undo is unfinished (undo reason, invoice still linked) is 503 earlier_attempt_being_removed - no new attempt, nothing written or queued")]
    [InlineData(SubmissionReasonCodes.SourceCheckFailed)]
    [InlineData(SubmissionReasonCodes.FileUnservable)]
    public async Task Retry_WhileTheFailedAttemptsUndoIsUnfinished_Is503_NothingWritten(string reasonCode)
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = FailedByTheGate(reasonCode, linked: true);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.EarlierAttemptBeingRemoved).And.Be("ebilling.submission.earlier_attempt_being_removed");
        outcome.Receipt.Should().BeNull();
        ShouldHaveWrittenNothing(h);
        h.Writes.Should().Be(0);
        h.SubmissionReads.Should().Equal([SubmissionId, SecondAttempt], "refused at the walk, before the document or anything else is read");
    }

    [Theory(DisplayName = "Round 5 (pass-2 F1-1, must not fire): once the failed attempt's undo completed (the invoice link cleared) the retry is a NEW attempt")]
    [InlineData(SubmissionReasonCodes.SourceCheckFailed)]
    [InlineData(SubmissionReasonCodes.FileUnservable)]
    public async Task Retry_OnceTheFailedAttemptsUndoCompleted_IsANewAttempt(string reasonCode)
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = FailedByTheGate(reasonCode, linked: false);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.SubmissionId.Should().Be(SecondAttempt);
        h.JobsSent.Should().ContainSingle().Which.JobId.Should().Be(SecondJob);
    }

    [Fact(DisplayName = "EB-D-167: the walk passes every Failed attempt - after attempts 1 and 2 failed, the retry is attempt 3")]
    public async Task Retry_AfterTwoFailedAttempts_IsAttemptThree()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed");
        h.Submissions[SecondAttempt] = Attempt(SecondAttempt, "Failed", undone: true);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Receipt!.SubmissionId.Should().Be(InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 3));
    }

    [Fact(DisplayName = "EB-D-167 (must not fire): a Rejected attempt after a Failed one stands (D-139) - 409, no new attempt")]
    public async Task Rejected_AfterAFailedAttempt_Is409_NoNewAttempt()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed");
        h.Submissions[SecondAttempt] = Attempt(SecondAttempt, "Rejected");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "EB-D-167: the new attempt runs EVERY check again - a document filed under another record since is 409, nothing written")]
    public async Task Retry_AfterFailed_RunsEveryCheck()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed");
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "EB-D-167 race: two concurrent retries after a Failed attempt make exactly ONE new attempt - the loser meets the winner's row (AlreadyCreated) and is answered with it")]
    public async Task TwoConcurrentRetries_AfterFailed_MakeExactlyOneNewAttempt()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed");
        // The winner committed generation 2 between the loser's walk and its create: the create of the same id is AlreadyCreated.
        h.Allocation = TrackingNumberResult.AlreadyCreated("INV-7.2", 2);
        h.StoredOnCreate = Attempt(SecondAttempt, "Received");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.SubmissionId.Should().Be(SecondAttempt, "the winner's attempt, never a third");
        h.Allocations.Should().ContainSingle().Which.SubmissionId.Should().Be(SecondAttempt, "both racers ask for the same generation's id");
        h.Submissions.Keys.Should().BeEquivalentTo(new[] { SubmissionId, SecondAttempt });
    }

    [Fact(DisplayName = "EB-D-167 race (must not fire): the loser whose details differ from the winner's new attempt is 409 document_already_submitted, nothing queued")]
    public async Task TwoConcurrentRetries_LoserWithOtherDetails_Is409()
    {
        var h = NewHarness();
        h.Submissions[SubmissionId] = Attempt(SubmissionId, "Failed");
        h.Allocation = TrackingNumberResult.AlreadyCreated("INV-8.1", 1);
        var winner = Attempt(SecondAttempt, "Received");
        winner[TrackingNumberAllocator.FirmInvoiceNumberColumn] = "INV-8";
        h.StoredOnCreate = winner;

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.JobsSent.Should().BeEmpty();
    }

    [Fact(DisplayName = "EB-D-167: a document whose every allowed attempt failed is 409 submission_already_ended, nothing written")]
    public async Task AfterTheLastAllowedAttempt_Is409()
    {
        var h = NewHarness();
        for (var g = 1; g <= InvoiceSubmissionService.MaxExistingDocumentGeneration; g++)
        {
            var id = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, g);
            h.Submissions[id] = Attempt(id, "Failed");
        }

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.SubmissionAlreadyEnded);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "EB-D-167: generation 1 keeps the original derived ids; later generations are distinct and each is recognised as the document's own")]
    public void GenerationIds_KeepGenerationOne_AndAreTheDocumentsOwn()
    {
        InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 1).Should().Be(SubmissionId);
        InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId, 1).Should().Be(JobId);
        var ids = Enumerable.Range(1, InvoiceSubmissionService.MaxExistingDocumentGeneration)
            .Select(g => InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, g)).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(DocumentId, id));
        InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(Guid.NewGuid(), SecondAttempt).Should().BeFalse();
        InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(DocumentId, Guid.NewGuid()).Should().BeFalse();
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

    // ── 050a review round 3 (uac-r2 F1-a): "unfiled" is decided by ISOLATION, through every ownership parent, not by a column list ──

    /// <summary>A Secure matter other than the confirm's (S in the review's scenario).</summary>
    private static readonly Guid SecureMatterS = Guid.NewGuid();

    /// <summary>
    /// The review's scenario: a firm replies on Secure matter S's record thread with the invoice PDF. The thread join files the communication
    /// under S through <c>sprk_communicationthread</c> only (ThreadResolver: ParentChanges = {}, InheritedParents = [the thread's record]), so
    /// the communication has NO regarding column and D-140 reads the attachment as unfiled; the attachment is Secure-owned. The harness holds
    /// the D-140 facts, the world the isolation facts, as Dataverse would answer both.
    /// </summary>
    private static Harness ThreadJoinedAttachment(Guid secureMatter)
    {
        var (h, communication) = EmailAttachment(regardingMatter: null);
        var thread = Guid.NewGuid();
        h.Isolation
            .SecureRoot("sprk_matter", secureMatter)
            .SecureChild("sprk_communicationthread", thread, ("sprk_regardingmatter", "sprk_matter", secureMatter))
            .SecureChild("sprk_communication", communication, ("sprk_communicationthread", "sprk_communicationthread", thread))
            .SecureChild("sprk_document", DocumentId, ("sprk_relatedcommunication", "sprk_communication", communication));
        return h;
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-a, must fire): an attachment of a communication that JOINED a Secure matter's thread (no regarding column, Secure-owned) confirmed onto an ordinary matter is 409, nothing written")]
    public async Task ThreadJoinedCommunication_OnASecureMatter_ConfirmedOntoAnOrdinaryMatter_Is409()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.Isolation.OrdinaryRoot("sprk_matter", MatterId);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        outcome.Detail.Should().NotContain(SecureMatterS.ToString(), "the other record is never named");
        ShouldHaveWrittenNothing(h);
        h.LinkReads.Should().BeEmpty("refused before the intake's checks");
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-a, must fire): a Secure-owned document linked ONLY through sprk_relatedevent to a Secure matter's event is 409")]
    public async Task RelatedEventOnly_UnderASecureMatter_Is409()
    {
        var h = NewHarness();
        var secureEvent = Guid.NewGuid();
        h.ExistingDocument!["sprk_relatedevent"] = new EntityReference("sprk_event", secureEvent);
        h.Isolation
            .SecureRoot("sprk_matter", SecureMatterS)
            .SecureChild("sprk_event", secureEvent, ("sprk_regardingmatter", "sprk_matter", SecureMatterS))
            .SecureChild("sprk_document", DocumentId, ("sprk_relatedevent", "sprk_event", secureEvent));

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-a, must fire): a communication regarding a Secure matter's INVOICE (no core-ancestor stamp, no regarding matter) isolates its attachment: 409")]
    public async Task CommunicationRegardingASecureMattersInvoice_Is409()
    {
        var (h, communication) = EmailAttachment(regardingMatter: null);
        var invoice = Guid.NewGuid();
        h.Communications[communication]["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice);
        h.Isolation
            .SecureRoot("sprk_matter", SecureMatterS)
            .SecureChild("sprk_invoice", invoice, ("sprk_matter", "sprk_matter", SecureMatterS))
            .SecureChild("sprk_communication", communication, ("sprk_regardinginvoice", "sprk_invoice", invoice))
            .UserOwnedChild("sprk_document", DocumentId, ("sprk_relatedcommunication", "sprk_communication", communication));

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-a variant, must fire): a document filed under the confirm's own ordinary matter but isolated through a communication on Secure matter S is 409")]
    public async Task FiledUnderTheConfirmsMatter_ButIsolatedThroughASecureThread_Is409()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", MatterId); // D-140 resolves to the confirm's matter
        h.Isolation
            .OrdinaryRoot("sprk_matter", MatterId)
            .Set("sprk_document", DocumentId, "sprk_matter", new EntityReference("sprk_matter", MatterId));

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 3 (uac-r2 F1-a variant, must fire): a Secure-owned document whose isolation resolves to Secure matter S, confirmed onto ANOTHER Secure matter S2, is 409 (a different root)")]
    public async Task IsolatedUnderS_ConfirmedOntoAnotherSecureMatterS2_Is409()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.Isolation.SecureRoot("sprk_matter", MatterId); // the confirm's matter is S2, Secure too

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 3 (must fire, stricter than 'among the roots'): a document under BOTH S and the confirm's Secure matter S2 is 409 (its content would reach S2's sharees alone)")]
    public async Task IsolatedUnderSAndS2_ConfirmedOntoS2_Is409()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        h.Isolation
            .SecureRoot("sprk_matter", MatterId)
            .Set("sprk_document", DocumentId, "sprk_matter", new EntityReference("sprk_matter", MatterId));

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "050a round 3 (must fire, fail closed): a Secure-owned document under NO secure root the walk can find is 409, never 'unfiled'")]
    public async Task SecureOwnedDocument_UnderNoSecureRoot_Is409()
    {
        var h = NewHarness();
        h.Isolation.SecureChild("sprk_document", DocumentId);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 3 (must fire, fail closed): a document under a root flagged secure but not isolated (undetermined) is 409")]
    public async Task DocumentUnderAFlaggedNotIsolatedRoot_Is409()
    {
        var h = NewHarness();
        var flagged = Guid.NewGuid();
        h.Isolation
            .FlaggedNotIsolatedRoot("sprk_matter", flagged)
            .UserOwnedChild("sprk_document", DocumentId, ("sprk_relatedmatter", "sprk_matter", flagged));

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).ErrorCode
            .Should().Be(InvoiceSubmissionErrors.DocumentCoreRecordMismatch);
    }

    [Fact(DisplayName = "050a round 3 (must fire): an isolation read that faults is 503 and nothing is written (never read as 'not isolated')")]
    public async Task IsolationReadFaults_Is503_AndWritesNothing()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.Isolation.FailingQueriesOf("sprk_communication");

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        ShouldHaveWrittenNothing(h);
    }

    /// <summary>
    /// 050a round 4 (uac-r2 F3): a SECOND team carrying the Secure Record owner team's name, type and business unit. The walk cannot tell
    /// which team isolates a row, so it answers Failed (an ambiguous owner team), never Completed or NotApplicable.
    /// </summary>
    internal static void AddAmbiguousSecureOwnerTeam(SecureChildShareWorld world) =>
        world.Team(Guid.NewGuid(), world.OwnerTeamBusinessUnit, SecureChildShareWorld.SecureOwnerTeamName, isDefault: false, teamType: 0);

    [Fact(DisplayName = "050a round 4 (uac-r2 F3, must fire): an ambiguous Secure Record owner team (the walk's Failed) at confirm is 503 record_unavailable and nothing is written, never 'allowed'")]
    public async Task AmbiguousSecureOwnerTeam_AtConfirm_Is503_AndWritesNothing()
    {
        var h = ThreadJoinedAttachment(SecureMatterS);
        h.Isolation.OrdinaryRoot("sprk_matter", MatterId);
        AddAmbiguousSecureOwnerTeam(h.Isolation);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.CoreRecordUnavailable);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "050a round 4 (uac-r2 F3, must not fire): an environment with no Secure Record business unit isolates nothing, so the document is received")]
    public async Task NoSecureRecordBusinessUnit_IsReceived()
    {
        var h = NewHarness();
        h.Isolation = SecureChildShareWorld.WithoutSecureBusinessUnit();
        h.Isolation.UserOwnedChild("sprk_document", DocumentId);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Allocations.Should().ContainSingle();
    }

    [Fact(DisplayName = "050a round 3 (must not fire): a truly unfiled document that is not isolated (user-owned, no parent) is received")]
    public async Task TrulyUnfiled_NotIsolated_IsReceived()
    {
        var h = NewHarness();
        h.Isolation.UserOwnedChild("sprk_document", DocumentId);

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a round 3 (must not fire): a Secure-owned document whose isolation resolves EXACTLY to the confirm's Secure matter is received")]
    public async Task SecureOwned_IsolatedExactlyUnderTheConfirmsMatter_IsReceived()
    {
        var h = ThreadJoinedAttachment(MatterId); // the thread is the confirm's own Secure matter's

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a round 3 (must not fire): a document filed under the ORDINARY matter it is confirmed onto, not isolated, is received")]
    public async Task FiledUnderTheConfirmsOrdinaryMatter_NotIsolated_IsReceived()
    {
        var h = NewHarness();
        h.ExistingDocument!["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        h.Isolation
            .OrdinaryRoot("sprk_matter", MatterId)
            .OrdinaryChild("sprk_document", DocumentId, ("sprk_matter", "sprk_matter", MatterId));

        (await h.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None)).Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
    }

    [Fact(DisplayName = "050a: the dedupe ids are deterministic per document and distinct across documents")]
    public void DedupeIds_AreDeterministicPerDocument()
    {
        var other = Guid.NewGuid();
        InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId).Should().Be(SubmissionId);
        InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(other).Should().NotBe(SubmissionId);
        InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId).Should().NotBe(SubmissionId);
    }

    // ── ebilling-r1 task 094 fix round (F1-b): step A (the route's filing of an unfiled source communication) runs only after every read-only
    //    refusal, once, before the create; a refusal after it says the filing is kept ──

    /// <summary>A recording step A: when it ran (the creates made and the checks run by then); answers <paramref name="filed"/>.</summary>
    private sealed class RecordedStepA(Harness h, bool filed = true)
    {
        public List<(int Creates, int LinkReads, int OwnerChecks)> Runs { get; } = [];

        public Func<CancellationToken, Task<bool>> Hook => _ =>
        {
            Runs.Add((h.Allocations.Count, h.LinkReads.Count, h.OwnerContexts.Count));
            return Task.FromResult(filed);
        };
    }

    [Fact(DisplayName = "094 F1-b (must fire): step A runs once, after the read-only checks (firm link, owner) and before the create")]
    public async Task StepA_RunsOnce_AfterTheChecks_BeforeTheCreate()
    {
        var h = NewHarness();
        var stepA = new RecordedStepA(h);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { FileSourceBeforeWrite = stepA.Hook }, CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        var run = stepA.Runs.Should().ContainSingle().Subject;
        run.Creates.Should().Be(0, "before the create");
        run.LinkReads.Should().BeGreaterThan(0, "after the firm-link check");
        run.OwnerChecks.Should().BeGreaterThan(0, "after the owner check");
        h.Allocations.Should().ContainSingle();
    }

    public static TheoryData<string> RefusalsBeforeStepA => new()
    {
        "earlier-ended", "earlier-other-details", "d140-chain-case-1", "isolation-case-1", "firm-not-linked", "owner-refused",
        "document-missing", "document-invoiced", "isolation-unreadable",
    };

    [Theory(DisplayName = "094 F1-b (must fire): a read-only refusal is answered before step A runs, so a refused submission leaves no filing behind")]
    [MemberData(nameof(RefusalsBeforeStepA))]
    public async Task StepA_DoesNotRun_BeforeARefusal(string refusal)
    {
        var h = refusal switch
        {
            "d140-chain-case-1" => ThreadReply(communicationMatter: null, threadMatter: Guid.NewGuid()).Harness,
            "isolation-case-1" => ThreadJoinedAttachment(SecureMatterS),
            _ => NewHarness(),
        };
        switch (refusal)
        {
            case "earlier-ended":
                h.Submission = Earlier(status: "Failed");
                break;
            case "earlier-other-details":
                h.Submission = Earlier(number: "INV-8");
                break;
            case "isolation-case-1":
                h.Isolation.OrdinaryRoot("sprk_matter", MatterId);
                break;
            case "firm-not-linked":
                h.Links.Clear();
                break;
            case "owner-refused":
                h.OwnerAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "no team");
                break;
            case "document-missing":
                h.ExistingDocument = null;
                break;
            case "document-invoiced":
                h.ExistingDocument!["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
                break;
            case "isolation-unreadable":
                h.Isolation.FailingQueriesOf("sprk_document");
                break;
        }

        var stepA = new RecordedStepA(h);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { FileSourceBeforeWrite = stepA.Hook }, CancellationToken.None);

        outcome.Kind.Should().NotBe(InvoiceSubmissionOutcomeKind.Received);
        stepA.Runs.Should().BeEmpty("step A writes; it never runs before a read-only refusal");
        outcome.Detail?.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "094 F1-b (must fire): a retry that finds the earlier submission (same details, still Received) runs step A once and answers it")]
    public async Task StepA_RunsOnTheEarlierSubmissionsRetry()
    {
        var h = NewHarness();
        h.Submission = Earlier(status: "Received");
        var stepA = new RecordedStepA(h, filed: false);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { FileSourceBeforeWrite = stepA.Hook }, CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        stepA.Runs.Should().ContainSingle();
        h.Allocations.Should().BeEmpty();
    }

    public static TheoryData<string> RefusalsAfterStepA => new() { "queue", "allocator" };

    [Theory(DisplayName = "094 F1-b (must fire): a refusal after step A filed the communication says so truthfully: no submission was saved, the filing is kept")]
    [MemberData(nameof(RefusalsAfterStepA))]
    public async Task StepA_Filed_ThenARefusal_SaysTheFilingIsKept(string fault)
    {
        var h = NewHarness();
        if (fault == "queue")
        {
            h.JobFailure = new InvalidOperationException("service bus down");
        }
        else
        {
            h.AllocatorFailure = new InvalidOperationException("exists for another firm or number");
        }

        var stepA = new RecordedStepA(h, filed: true);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { FileSourceBeforeWrite = stepA.Hook }, CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Refused);
        stepA.Runs.Should().ContainSingle();
        outcome.Detail.Should().Contain(InvoiceSubmissionService.FilingKeptSentence);
        outcome.Detail.Should().NotContain("Nothing was saved", "the communication's filing was saved");
    }

    [Fact(DisplayName = "094 F1-b (must not fire): a refusal after a step A that filed nothing (already filed) says nothing about a filing")]
    public async Task StepA_FiledNothing_ThenARefusal_SaysNothingOfAFiling()
    {
        var h = NewHarness();
        h.AllocatorFailure = new InvalidOperationException("exists for another firm or number");
        var stepA = new RecordedStepA(h, filed: false);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Confirm() with { FileSourceBeforeWrite = stepA.Hook }, CancellationToken.None);

        stepA.Runs.Should().ContainSingle();
        outcome.Detail.Should().NotContain(InvoiceSubmissionService.FilingKeptSentence);
    }
}
