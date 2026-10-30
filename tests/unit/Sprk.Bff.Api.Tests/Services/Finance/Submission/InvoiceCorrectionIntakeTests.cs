using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// The old invoice and its submission a Correction names (ebilling-r1 task 097b), as the job and the allocator leave them: the submission's
/// id derives the invoice's (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>), both regard the test matter and the test firm.
/// Shared by the intake, from-email and route suites.
/// </summary>
internal static class CorrectionSeed
{
    public static readonly Guid OldSubmissionId = Guid.Parse("097b0000-0000-4000-8000-0000000000a1");
    public static Guid OldInvoiceId => InvoiceSubmissionPersister.InvoiceIdFor(OldSubmissionId);
    public const string Number = "INV-7";

    public static Entity Invoice(Guid? firm = null, string coreColumn = "sprk_matter", string coreEntity = "sprk_matter", Guid? coreId = null,
        int state = InvoiceStatusCodes.StateActive, int status = InvoiceStatusCodes.InReview) => new("sprk_invoice", OldInvoiceId)
    {
        [InvoiceSubmissionPersister.InvoiceVendorOrg] = new EntityReference("sprk_organization", firm ?? InvoiceSubmissionServiceTests.FirmId),
        [coreColumn] = new EntityReference(coreEntity, coreId ?? InvoiceSubmissionServiceTests.MatterId),
        ["statecode"] = new OptionSetValue(state),
        ["statuscode"] = new OptionSetValue(status),
    };

    public static Entity Submission(string number = Number, Guid? firm = null, Guid? matter = null, Guid? id = null) =>
        new("sprk_invoicesubmission", id ?? OldSubmissionId)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", firm ?? InvoiceSubmissionServiceTests.FirmId),
            [TrackingNumberAllocator.FirmInvoiceNumberColumn] = number,
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter ?? InvoiceSubmissionServiceTests.MatterId),
        };

    /// <summary>A correctable old invoice (In Review) with its submission, in <paramref name="h"/>.</summary>
    public static Entity Seed(Harness h, Entity? invoice = null, Entity? submission = null)
    {
        var row = invoice ?? Invoice();
        h.Invoices[OldInvoiceId] = row;
        h.PriorSubmissions.Add(submission ?? Submission());
        return row;
    }
}

/// <summary>
/// ebilling-r1 task 097b (model 9.1 "Existing invoice", EB-D-179): the intake's Correction guard (<see cref="InvoiceSubmissionService.CheckCorrectionAsync"/>),
/// the from-document entry that runs it (<see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/> with
/// <see cref="InvoiceSubmissionRequest.CorrectedInvoice"/>), and the job's snapshot of the stored target (<see cref="SubmissionSnapshot"/>).
/// The caller's own Write is the routes' check (their suites).
/// </summary>
/// <remarks>Maintain-class (ADR-038): a correction may only replace an In Review invoice of the same firm, record and number; the guard is
/// the cross-firm boundary of the write path.</remarks>
public sealed class InvoiceCorrectionIntakeTests
{
    private static readonly Guid FirmId = InvoiceSubmissionServiceTests.FirmId;
    private static readonly Guid MatterId = InvoiceSubmissionServiceTests.MatterId;
    private static readonly InvoiceCoreRecord Matter = new(InvoiceCoreRecordKind.Matter, MatterId);

    private static Task<(InvoiceSubmissionOutcome? Refusal, Guid? ResubmissionOf)> Check(Harness h, Entity invoice, string number = CorrectionSeed.Number,
        InvoiceCoreRecord? core = null, Guid? firm = null) =>
        h.Build().CheckCorrectionAsync(core ?? Matter, firm ?? FirmId, number, invoice, CancellationToken.None);

    private static void RefusedWith(InvoiceSubmissionOutcome? refusal, string reason)
    {
        refusal.Should().NotBeNull();
        (refusal!.StatusCode, refusal.ErrorCode).Should().Be((409, "ebilling.submission.correction_not_allowed"));
        refusal.Extensions.Should().Contain("reason", reason);
        refusal.Detail.Should().NotContain(FirmId.ToString()).And.NotContain(MatterId.ToString(), "the answer names no record");
    }

    // ── the guard ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "097b: an In Review invoice of the same firm, record and number may be corrected; the target is its own submission")]
    public async Task Allowed_InReview_SameFirmRecordAndNumber_TargetsItsSubmission()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h);

        var (refusal, target) = await Check(h, invoice, number: "  inv-7 ");

        refusal.Should().BeNull("the number is compared trimmed and ignoring case, as the tracking sequence keys it (D-37)");
        target.Should().Be(CorrectionSeed.OldSubmissionId);
        h.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "097b (must fire): an invoice of another firm is 409 different_firm")]
    public async Task Refused_TheInvoiceIsAnotherFirms()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, CorrectionSeed.Invoice(firm: Guid.NewGuid()));

        var (refusal, target) = await Check(h, invoice);

        RefusedWith(refusal, "different_firm");
        target.Should().BeNull();
    }

    [Fact(DisplayName = "097b (must fire): an invoice whose submission is another firm's is 409 different_firm")]
    public async Task Refused_TheSubmissionIsAnotherFirms()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, submission: CorrectionSeed.Submission(firm: Guid.NewGuid()));

        RefusedWith((await Check(h, invoice)).Refusal, "different_firm");
    }

    public static TheoryData<string, string, bool> OtherCores => new()
    {
        // (the invoice's core column, its table, same id as the request's matter)
        { "sprk_matter", "sprk_matter", false },
        { "sprk_project", "sprk_project", true },
        { "sprk_regardingworkassignment", "sprk_workassignment", true },
    };

    [Theory(DisplayName = "097b (must fire): an invoice filed under another core record is 409 different_core_record")]
    [MemberData(nameof(OtherCores))]
    public async Task Refused_TheInvoiceIsUnderAnotherCoreRecord(string column, string table, bool sameId)
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, CorrectionSeed.Invoice(coreColumn: column, coreEntity: table, coreId: sameId ? MatterId : Guid.NewGuid()));

        RefusedWith((await Check(h, invoice)).Refusal, "different_core_record");
    }

    [Fact(DisplayName = "097b: an invoice that names two core records is refused as another core record (fail closed)")]
    public async Task Refused_TheInvoiceNamesTwoCoreRecords()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Invoice();
        invoice["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());
        CorrectionSeed.Seed(h, invoice);

        RefusedWith((await Check(h, invoice)).Refusal, "different_core_record");
    }

    [Fact(DisplayName = "097b (must fire): an invoice whose submission is under another core record is 409 different_core_record (the peer edge reaches no other root)")]
    public async Task Refused_TheSubmissionIsUnderAnotherCoreRecord()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, submission: CorrectionSeed.Submission(matter: Guid.NewGuid()));

        RefusedWith((await Check(h, invoice)).Refusal, "different_core_record");
    }

    public static TheoryData<int, int, string?> NotInReview => new()
    {
        { InvoiceStatusCodes.StateActive, InvoiceStatusCodes.Approved, "Approved" },
        { InvoiceStatusCodes.StateActive, InvoiceStatusCodes.SentToAp, "SentToAp" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Paid, "Paid" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Rejected, "Rejected" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm" },
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.Cancelled, "Cancelled" },
        // A status this lifecycle does not know (Superseded arrives with task 097a): refused, and no name is guessed.
        { InvoiceStatusCodes.StateInactive, 659490005, null },
        // A pair Dataverse would not hold (In Review on an inactive row): refused.
        { InvoiceStatusCodes.StateInactive, InvoiceStatusCodes.InReview, "InReview" },
    };

    [Theory(DisplayName = "097b (must fire): an invoice not In Review (Approved, Sent to AP, Paid or inactive) is 409 invoice_status with its status")]
    [MemberData(nameof(NotInReview))]
    public async Task Refused_TheInvoiceIsNotInReview(int state, int status, string? name)
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, CorrectionSeed.Invoice(state: state, status: status));

        var (refusal, _) = await Check(h, invoice);

        RefusedWith(refusal, "invoice_status");
        if (name is null)
        {
            refusal!.Extensions.Should().NotContainKey("invoiceStatus");
        }
        else
        {
            refusal!.Extensions.Should().Contain("invoiceStatus", name);
        }
    }

    [Fact(DisplayName = "097b (EB-D-179 item 1, must fire): an invoice with no submission record is 409 no_submission")]
    public async Task Refused_TheInvoiceHasNoSubmission()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h);
        h.PriorSubmissions.Clear();

        RefusedWith((await Check(h, invoice)).Refusal, "no_submission");
    }

    [Fact(DisplayName = "097b: a submission that points at the invoice but did not create it (its id does not derive it) is not its submission: no_submission")]
    public async Task Refused_ASubmissionThatDidNotCreateTheInvoice()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h, submission: CorrectionSeed.Submission(id: Guid.NewGuid()));

        RefusedWith((await Check(h, invoice)).Refusal, "no_submission");
    }

    [Fact(DisplayName = "097b: the submission read faults: 503 record_unavailable, never a guess")]
    public async Task ReadFault_Is503()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h);
        h.PriorSubmissionReadFailure = new TimeoutException();

        var (refusal, target) = await Check(h, invoice);

        (refusal!.StatusCode, refusal.ErrorCode).Should().Be((503, "ebilling.submission.record_unavailable"));
        target.Should().BeNull();
    }

    [Fact(DisplayName = "097b (EB-D-179 item 3, D-37, must fire): another firm invoice number is 409 different_invoice_number")]
    public async Task Refused_AnotherFirmInvoiceNumber()
    {
        var h = new Harness();
        var invoice = CorrectionSeed.Seed(h);

        RefusedWith((await Check(h, invoice, number: "INV-8")).Refusal, "different_invoice_number");
    }

    // ── the from-document entry (SubmitExistingDocumentAsync) ──────────────────────────────────────────────────────

    private static readonly Guid DocumentId = Guid.Parse("097b0000-0000-4000-8000-0000000000d1");
    private static readonly Guid SubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(DocumentId);

    private static Harness DocumentHarness() => new()
    {
        ExistingDocument = new Entity("sprk_document", DocumentId) { ["sprk_graphdriveid"] = "b!drive", ["sprk_graphitemid"] = "item-97" },
    };

    private static InvoiceSubmissionRequest Request(Entity? corrected, string number = CorrectionSeed.Number, Func<CancellationToken, Task<bool>>? stepA = null) => new()
    {
        FirmId = FirmId,
        CoreRecord = Matter,
        Channel = InvoiceSubmissionChannel.Internal,
        FirmInvoiceNumber = number,
        ExistingDocumentId = DocumentId,
        CallerObjectId = Guid.NewGuid(),
        CorrelationId = "corr-097b",
        CorrectedInvoice = corrected,
        FileSourceBeforeWrite = stepA,
    };

    [Fact(DisplayName = "097b: an allowed Correction creates the submission with sprk_resubmissionof = the replaced invoice's submission")]
    public async Task FromDocument_AllowedCorrection_RecordsTheTarget()
    {
        var h = DocumentHarness();
        var invoice = CorrectionSeed.Seed(h);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Request(invoice), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Allocations.Should().ContainSingle().Which.Fields!["sprk_resubmissionof"]
            .Should().Be(new EntityReference("sprk_invoicesubmission", CorrectionSeed.OldSubmissionId));
    }

    [Fact(DisplayName = "097b: a request without a correction is unchanged: no sprk_resubmissionof, no invoice or submission read for it")]
    public async Task FromDocument_New_IsUnchanged()
    {
        var h = DocumentHarness();
        CorrectionSeed.Seed(h);

        var outcome = await h.Build().SubmitExistingDocumentAsync(Request(null), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        h.Allocations.Should().ContainSingle().Which.Fields!.Should().NotContainKey("sprk_resubmissionof");
        h.Entities.Verify(e => e.RetrieveMultipleAsync(
            It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "097b: a refused Correction writes nothing and never runs step A (the filing)")]
    public async Task FromDocument_RefusedCorrection_WritesNothing_AndRunsNoStepA()
    {
        var h = DocumentHarness();
        var invoice = CorrectionSeed.Seed(h, CorrectionSeed.Invoice(state: InvoiceStatusCodes.StateActive, status: InvoiceStatusCodes.Approved));
        var stepARuns = 0;

        var outcome = await h.Build().SubmitExistingDocumentAsync(
            Request(invoice, stepA: _ => { stepARuns++; return Task.FromResult(true); }), CancellationToken.None);

        RefusedWith(outcome, "invoice_status");
        outcome.FilingKept.Should().BeFalse();
        stepARuns.Should().Be(0);
        h.Writes.Should().Be(0);
    }

    private static Entity Earlier(Guid? resubmissionOf) => new("sprk_invoicesubmission", SubmissionId)
    {
        [TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.2",
        [TrackingNumberAllocator.FirmInvoiceNumberColumn] = CorrectionSeed.Number,
        [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", FirmId),
        ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
        ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
        ["sprk_document"] = new EntityReference("sprk_document", DocumentId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        ["sprk_resubmissionof"] = resubmissionOf is { } id ? new EntityReference("sprk_invoicesubmission", id) : null,
    };

    [Fact(DisplayName = "097b: a retry of a committed Correction is answered with its submission, even though the old invoice has moved on")]
    public async Task FromDocument_Retry_OfTheSameCorrection_IsTheEarlierSubmission()
    {
        var h = DocumentHarness();
        h.Submission = Earlier(CorrectionSeed.OldSubmissionId);
        var moved = CorrectionSeed.Seed(h, CorrectionSeed.Invoice(state: InvoiceStatusCodes.StateInactive, status: 659490005));

        var outcome = await h.Build().SubmitExistingDocumentAsync(Request(moved), CancellationToken.None);

        outcome.Kind.Should().Be(InvoiceSubmissionOutcomeKind.Received);
        outcome.Receipt!.SubmissionId.Should().Be(SubmissionId);
        h.Allocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "097b: a retry naming another target than the earlier submission (or none) is 409 document_already_submitted")]
    public async Task FromDocument_Retry_WithAnotherTarget_Is409()
    {
        foreach (var (earlierTarget, corrected) in new (Guid?, bool)[] { (null, true), (CorrectionSeed.OldSubmissionId, false), (Guid.NewGuid(), true) })
        {
            var h = DocumentHarness();
            h.Submission = Earlier(earlierTarget);
            var invoice = CorrectionSeed.Seed(h);

            var outcome = await h.Build().SubmitExistingDocumentAsync(Request(corrected ? invoice : null), CancellationToken.None);

            outcome.ErrorCode.Should().Be("ebilling.submission.document_already_submitted", $"earlier target {earlierTarget}, corrected {corrected}");
            h.Allocations.Should().BeEmpty();
        }
    }

    // ── the job's snapshot ─────────────────────────────────────────────────────────────────────────────────────────

    private static Entity NewSubmissionRow(Guid? resubmissionOf) => new("sprk_invoicesubmission", Guid.NewGuid())
    {
        [TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.2",
        [TrackingNumberAllocator.SequenceColumn] = 2,
        [TrackingNumberAllocator.FirmInvoiceNumberColumn] = CorrectionSeed.Number,
        [TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", FirmId),
        ["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        ["createdon"] = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc),
        ["sprk_resubmissionof"] = resubmissionOf is { } id ? new EntityReference("sprk_invoicesubmission", id) : null,
    };

    [Fact(DisplayName = "097b: the job's snapshot reads sprk_resubmissionof (a column of SubmissionSnapshot.Columns)")]
    public void Snapshot_ReadsTheTarget()
    {
        SubmissionSnapshot.Columns.Should().Contain("sprk_resubmissionof");
        SubmissionSnapshot.Read(NewSubmissionRow(CorrectionSeed.OldSubmissionId)).Snapshot!.ResubmissionOfSubmissionId
            .Should().Be(CorrectionSeed.OldSubmissionId);
        SubmissionSnapshot.Read(NewSubmissionRow(null)).Snapshot!.ResubmissionOfSubmissionId.Should().BeNull();
    }

    private static async Task<CorrectedInvoiceRead> Resolve(Guid? resubmissionOf, Func<Entity>? earlier)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync("sprk_invoicesubmission", CorrectionSeed.OldSubmissionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => earlier is null
                ? Task.FromException<Entity>(new System.ServiceModel.FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = -2147220969 }, "Does Not Exist"))
                : Task.FromResult(earlier()));
        var snapshot = SubmissionSnapshot.Read(NewSubmissionRow(resubmissionOf)).Snapshot!;
        return await snapshot.ReadCorrectedInvoiceAsync(entities.Object, CancellationToken.None);
    }

    [Fact(DisplayName = "097b: the snapshot resolves the old invoice from the earlier submission's sprk_invoice, checked against InvoiceIdFor")]
    public async Task Snapshot_ResolvesTheOldInvoice()
    {
        (await Resolve(null, null)).Should().Be(CorrectedInvoiceRead.None, "a New submission reads nothing");
        (await Resolve(CorrectionSeed.OldSubmissionId, () => CorrectionSeed.Submission()))
            .Should().Be(CorrectedInvoiceRead.Resolved(CorrectionSeed.OldSubmissionId, CorrectionSeed.OldInvoiceId));
    }

    [Fact(DisplayName = "097b: a target that is gone, names no invoice or names another invoice is Unresolved (fail closed)")]
    public async Task Snapshot_UnresolvedTargets_FailClosed()
    {
        var unresolved = CorrectedInvoiceRead.Unresolved(CorrectionSeed.OldSubmissionId);
        (await Resolve(CorrectionSeed.OldSubmissionId, null)).Should().Be(unresolved);

        var noInvoice = CorrectionSeed.Submission();
        noInvoice["sprk_invoice"] = null;
        (await Resolve(CorrectionSeed.OldSubmissionId, () => noInvoice)).Should().Be(unresolved);

        var other = CorrectionSeed.Submission();
        other["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        (await Resolve(CorrectionSeed.OldSubmissionId, () => other)).Should().Be(unresolved);
    }
}
