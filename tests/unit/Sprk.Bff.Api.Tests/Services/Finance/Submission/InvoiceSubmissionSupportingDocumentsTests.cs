using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 118b-1: the from-document intake's supporting documents (<see cref="InvoiceSubmissionRequest.SupportingDocumentIds"/>)
/// in <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>: the set joins the "same details?" comparison with an earlier
/// attempt (never the submission id, D-134), a Failed attempt still gives a new attempt, and, until 118b-2 stages the copies, a new attempt
/// that names supporting documents is refused read-only.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): one submission per document and attempt, and "a different request is never answered with another's receipt",
/// are invariants of the only invoice creator's entry. The earlier attempt's set is seeded through the stage-read seam
/// (<see cref="InvoiceSubmissionService.ReadAttemptSupportingDocumentsAsync"/>), which 118b-2 implements over the part stages.
/// </remarks>
public sealed class InvoiceSubmissionSupportingDocumentsTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid SubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId);
    private static readonly Guid JobId = InvoiceSubmissionService.ExistingDocumentJobIdFor(DocumentId);
    private static readonly Guid A = Guid.Parse("118b0000-0000-4000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("118b0000-0000-4000-8000-00000000000b");

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

    [Fact(DisplayName = "118b-1 (must fire): until 118b-2 stores parts, an earlier attempt's set is the EMPTY set, so a retry that names supporting documents is 409; one that names none replays as before")]
    public async Task UntilTheParts_AreStaged_AnEarlierAttemptHasTheEmptySet()
    {
        var withSet = NewHarness();
        withSet.Submission = Earlier(SubmissionId, "Accepted");
        var refused = await withSet.Build().SubmitExistingDocumentAsync(Confirm(A), CancellationToken.None);

        var without = NewHarness();
        without.Submission = Earlier(SubmissionId, "Accepted");
        var replayed = await without.Build().SubmitExistingDocumentAsync(Confirm(), CancellationToken.None);

        refused.ErrorCode.Should().Be(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        ShouldHaveWrittenNothing(withSet);
        replayed.Receipt.Should().Be(new InvoiceSubmissionReceipt(SubmissionId, JobId, "INV-7.3", DocumentId));
        ShouldHaveWrittenNothing(without);
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

        var outcome = await service.SubmitExistingDocumentAsync(Confirm(A), CancellationToken.None);

        outcome.ErrorCode.Should().NotBe(InvoiceSubmissionErrors.DocumentAlreadySubmitted);
        h.SubmissionReads.Should().Equal(SubmissionId, InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId, 2));
        service.Reads.Should().BeEmpty("a Failed attempt is not answered from; the request is the next generation");
        // 118b-1: the new attempt reaches the interim refusal (118b-2 stages the copies there instead).
        outcome.Detail.Should().Be(InvoiceSubmissionService.SupportingNotYetCopiedDetail);
        ShouldHaveWrittenNothing(h);
    }

    [Fact(DisplayName = "118b-1 (interim, must fire): a new attempt naming supporting documents is refused after every read-only check and BEFORE step A, so the field is never dropped silently; nothing filed or written")]
    public async Task NewAttempt_WithSupportingDocuments_IsRefusedBeforeStepA_UntilTheCopiesAreBuilt()
    {
        var h = NewHarness();
        var stepA = 0;

        var outcome = await h.Build().SubmitExistingDocumentAsync(
            Confirm(A, B) with { FileSourceBeforeWrite = _ => { stepA++; return Task.FromResult(true); } }, CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.ErrorCode.Should().Be(InvoiceSubmissionErrors.Failed);
        outcome.Detail.Should().Be(InvoiceSubmissionService.SupportingNotYetCopiedDetail);
        h.OwnerContexts.Should().NotBeEmpty("the intake's own checks ran first");
        stepA.Should().Be(0);
        ShouldHaveWrittenNothing(h);
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
