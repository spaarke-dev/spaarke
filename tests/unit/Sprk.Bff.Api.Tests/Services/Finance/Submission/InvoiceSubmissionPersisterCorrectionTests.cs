using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 097c (model 9.1 "Existing invoice", steps 4 and 5; EB-D-179): a Correction's commit supersedes the invoice it replaces in the
/// same transaction, guarded on the version read at commit, and the commit gate's undo reopens it. Over the persister harness
/// (<see cref="InvoiceSubmissionPersisterTests.Store"/>: the real persister, lifecycle builders and commit gate over an in-memory Dataverse that
/// enforces <c>IfRowVersionMatches</c> and rolls a faulted transaction back).
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    private static readonly Guid EarlierSubmission = Guid.Parse("097c0000-0000-4000-8000-000000000001");
    private static readonly Guid OldInvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(EarlierSubmission);
    private const long OldVersion = 41;

    /// <summary>A store whose submission is a Correction of <see cref="OldInvoiceId"/> (In Review, this firm, this matter, version 41).</summary>
    private static Store CorrectionStore(Action<Store>? beforeTransaction = null, Action<Store>? afterCommit = null)
    {
        var store = new Store { BeforeTransaction = beforeTransaction, AfterCommit = afterCommit, ProjectReads = true };
        store.Submission[InvoiceSubmissionService.ResubmissionOfColumn] = new EntityReference("sprk_invoicesubmission", EarlierSubmission);
        store.OtherSubmissions[EarlierSubmission] = new Entity("sprk_invoicesubmission", EarlierSubmission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            // Second review F1, F2: the old invoice's own submission has finished and carries this submission's typed number.
            ["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
            ["sprk_firminvoicenumber"] = "INV-100",
        };
        store.OldInvoice = new Entity("sprk_invoice", OldInvoiceId)
        {
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateActive),
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview),
            ["versionnumber"] = OldVersion,
            ["sprk_vendororg"] = new EntityReference("sprk_organization", Firm),
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
        };
        return store;
    }

    private static async Task<SubmissionEvaluationOutcome> CorrectionOutcomeAsync(ProcessorHarness? harness = null)
    {
        var outcome = await (harness ?? new ProcessorHarness()).RunAsync();
        return outcome with { Submission = outcome.Submission! with { ResubmissionOfSubmissionId = EarlierSubmission } };
    }

    private static string IndexKey(Guid invoiceId, long version) => $"invoice-index-{invoiceId:N}-v{version}";

    private static void NothingWritten(Store store)
    {
        store.InvoiceExists.Should().BeFalse("the transaction rolled back: no new invoice");
        store.Files.Dataverse.Rows.Should().BeEmpty("no Stored File row");
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionInvoice);
        store.TotalsCalls.Should().BeEmpty();
        store.Index.Sent.Should().BeEmpty("nothing changed, so nothing is re-indexed");
    }

    // ── The commit ──

    [Fact(DisplayName = "097c: a Correction commits the new invoice linked to the old one and the old invoice Superseded, in ONE transaction guarded on the version read at commit; the old invoice is re-indexed")]
    public async Task Correction_SupersedesTheOldInvoice_InTheSameTransaction()
    {
        var store = CorrectionStore();
        store.EarlierInvoices.Add(EarlierInvoice(Earlier1, InvoiceStatusCodes.ReturnedToFirm, EarlierOld));

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var transaction = store.Transactions.Should().ContainSingle("one transaction").Subject;
        Target(transaction[0]).GetAttributeValue<EntityReference>("sprk_supersedesinvoice").Id.Should().Be(OldInvoiceId,
            "the explicit target, not 104's Returned to Firm predecessor");
        var close = transaction.OfType<UpdateRequest>().Should().ContainSingle(u => u.Target.LogicalName == "sprk_invoice").Subject;
        close.Target.Id.Should().Be(OldInvoiceId);
        close.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        close.Target.RowVersion.Should().Be("41");
        close.Target.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Superseded);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded);
        store.OldInvoice!.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(InvoiceStatusCodes.StateInactive);
        store.EarlierInvoiceQueries.Should().BeEmpty("a Correction's explicit target replaces the automatic lookup");
        store.OldInvoiceReads.Should().ContainSingle("status, firm, core record and version in ONE read").Which
            .Should().Contain(["statecode", "statuscode", "versionnumber", "sprk_vendororg", "sprk_matter"]);
        var job = store.Index.Sent.Should().ContainSingle().Subject;
        job.JobType.Should().Be(InvoiceIndexingJobHandler.JobTypeName);
        job.IdempotencyKey.Should().Be(IndexKey(OldInvoiceId, OldVersion), "keyed to the version the close replaced");
    }

    [Fact(DisplayName = "097c: a New submission is unchanged - 104's automatic link to a Returned to Firm predecessor, no read of any old invoice, no close, no re-index")]
    public async Task NewSubmission_Keeps104sAutomaticLink()
    {
        var store = CorrectionStore();
        store.Submission.Attributes.Remove(InvoiceSubmissionService.ResubmissionOfColumn);
        store.EarlierInvoices.Add(EarlierInvoice(Earlier1, InvoiceStatusCodes.ReturnedToFirm, EarlierOld));

        var result = await store.Build().PersistAsync(await new ProcessorHarness().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var transaction = store.Transactions.Single();
        Target(transaction[0]).GetAttributeValue<EntityReference>("sprk_supersedesinvoice").Id.Should().Be(Earlier1);
        transaction.OfType<UpdateRequest>().Should().NotContain(u => u.Target.LogicalName == "sprk_invoice");
        store.EarlierInvoiceQueries.Should().ContainSingle();
        store.OldInvoiceReads.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.Index.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "097c (as 104): an automatically rejected Correction supersedes nothing and links nothing - the old invoice stays In Review for the next correction")]
    public async Task AutoRejectedCorrection_SupersedesNothing()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var outcome = await CorrectionOutcomeAsync(harness);
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected);
        var store = CorrectionStore();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        Target(store.Transactions.Single()[0]).Attributes.Should().NotContainKey("sprk_supersedesinvoice");
        store.Transactions.Single().OfType<UpdateRequest>().Should().NotContain(u => u.Target.LogicalName == "sprk_invoice");
        store.OldInvoiceReads.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    // ── The commit-time re-check (the intake's check is not a lock) ──

    [Theory(DisplayName = "097c SEEDED: the old invoice decided before the commit's read (Approved, Sent to AP, Paid) - Failed with invoice_status, no transaction sent")]
    [InlineData(InvoiceStatusCodes.Approved, InvoiceStatusCodes.StateActive)]
    [InlineData(InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.StateActive)]
    [InlineData(InvoiceStatusCodes.Paid, InvoiceStatusCodes.StateInactive)]
    public async Task DecidedBeforeTheCommit_IsRefused_NothingSent(int status, int state)
    {
        var store = CorrectionStore();
        store.ChangeOldInvoice(status, state);

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionInvoiceStatus);
        result.ReasonCode.Should().Be("ebilling.submission.correction_not_allowed.invoice_status", "the intake's 409 code and reason, as one stored code");
        store.Transactions.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(status, "the decided invoice is never overwritten");
        NothingWritten(store);
    }

    [Theory(DisplayName = "097c: at commit the old invoice must still be this firm's and filed under this core record alone; otherwise Failed with the intake's reason, nothing sent")]
    [InlineData("firm", "ebilling.submission.correction_not_allowed.different_firm")]
    [InlineData("core", "ebilling.submission.correction_not_allowed.different_core_record")]
    [InlineData("two-cores", "ebilling.submission.correction_not_allowed.different_core_record")]
    public async Task AnotherFirmOrCoreAtCommit_IsRefused(string change, string code)
    {
        var store = CorrectionStore();
        switch (change)
        {
            case "firm":
                store.OldInvoice!["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.NewGuid());
                break;
            case "core":
                store.OldInvoice!.Attributes.Remove("sprk_matter");
                store.OldInvoice!["sprk_project"] = new EntityReference("sprk_project", Project);
                break;
            default:
                store.OldInvoice!["sprk_project"] = new EntityReference("sprk_project", Project);
                break;
        }

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(code);
        store.Transactions.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "097c (fail closed): a target that no longer resolves - the earlier submission naming no invoice or another one, or the old invoice gone - is Failed, never committed as a New invoice")]
    [InlineData("no-invoice")]
    [InlineData("other-invoice")]
    [InlineData("invoice-gone")]
    public async Task UnresolvedTarget_IsRefused(string change)
    {
        var store = CorrectionStore();
        switch (change)
        {
            case "no-invoice":
                store.OtherSubmissions[EarlierSubmission].Attributes.Remove("sprk_invoice"); // e.g. its own commit was undone
                break;
            case "other-invoice":
                store.OtherSubmissions[EarlierSubmission]["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
                break;
            default:
                store.OldInvoiceDeleted = true;
                break;
        }

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionTargetUnavailable);
        store.Transactions.Should().BeEmpty();
    }

    // ── Second review: the old invoice's own submission at commit (F1 finished, F2 same number) ──

    private static void EarlierIs(Store store, InvoiceSubmissionStatus? status, string? reasonCode = null)
    {
        var earlier = store.OtherSubmissions[EarlierSubmission];
        earlier.Attributes.Remove("sprk_processingstatus");
        if (status is { } s)
        {
            earlier["sprk_processingstatus"] = new OptionSetValue((int)s);
        }

        if (reasonCode is not null)
        {
            earlier["sprk_outcomereasoncode"] = reasonCode;
        }
    }

    [Theory(DisplayName = "097 second review F1 SEEDED: the old invoice's own submission has not finished at commit (Processing, Received, Failed, unknown) - Failed with submission_in_progress, no transaction sent")]
    [InlineData(InvoiceSubmissionStatus.Processing, null)]
    [InlineData(InvoiceSubmissionStatus.Received, null)]
    [InlineData(InvoiceSubmissionStatus.Processing, SubmissionReasonCodes.Blocked)]
    [InlineData(InvoiceSubmissionStatus.Failed, SubmissionReasonCodes.SourceCheckFailed)]
    [InlineData(null, null)]
    public async Task EarlierSubmissionNotFinishedAtCommit_IsRefused_NothingSent(InvoiceSubmissionStatus? status, string? reasonCode)
    {
        var store = CorrectionStore();
        EarlierIs(store, status, reasonCode);

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "not a Retry: the earlier submission may run for a long time");
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionSubmissionInProgress);
        result.ReasonCode.Should().Be("ebilling.submission.correction_not_allowed.submission_in_progress", "the intake's 409 code and reason, as one stored code");
        store.Transactions.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview, "the old invoice is not superseded under a running submission");
        NothingWritten(store);
    }

    [Theory(DisplayName = "097 second review F1 (must not fire): the old invoice's own submission Accepted, or Rejected and held Blocked, commits the supersede")]
    [InlineData(InvoiceSubmissionStatus.Accepted, null)]
    [InlineData(InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.Overridden)]
    [InlineData(InvoiceSubmissionStatus.Rejected, SubmissionReasonCodes.Blocked)]
    public async Task EarlierSubmissionFinishedAtCommit_Commits(InvoiceSubmissionStatus status, string? reasonCode)
    {
        var store = CorrectionStore();
        EarlierIs(store, status, reasonCode);

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed, result.ReasonCode);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded);
    }

    [Fact(DisplayName = "097 second review F2 SEEDED: the submission sprk_resubmissionof names carries another firm invoice number at commit - Failed with different_invoice_number, no transaction sent")]
    public async Task EarlierSubmissionAnotherNumberAtCommit_IsRefused_NothingSent()
    {
        var store = CorrectionStore();
        store.OtherSubmissions[EarlierSubmission]["sprk_firminvoicenumber"] = "INV-200";

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionDifferentInvoiceNumber);
        result.ReasonCode.Should().Be("ebilling.submission.correction_not_allowed.different_invoice_number");
        store.Transactions.Should().BeEmpty();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        NothingWritten(store);
    }

    [Theory(DisplayName = "097 second review F2: the number is compared trimmed and ignoring case (D-37), and a missing one is not the same")]
    [InlineData("  inv-100 ", true)]
    [InlineData(null, false)]
    public async Task EarlierSubmissionNumberAtCommit_ComparedAsTheIntakeDoes(string? earlierNumber, bool commits)
    {
        var store = CorrectionStore();
        store.OtherSubmissions[EarlierSubmission]["sprk_firminvoicenumber"] = earlierNumber;

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(commits ? SubmissionPersistStatus.Committed : SubmissionPersistStatus.Refused, result.ReasonCode);
        if (!commits)
        {
            result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionDifferentInvoiceNumber);
        }
    }

    [Fact(DisplayName = "097c: a fault reading the old invoice at commit is a Retry with nothing sent")]
    public async Task OldInvoiceReadFault_IsARetry()
    {
        var store = CorrectionStore();
        store.OldInvoiceReadFaults = 1;

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.SupersedesReadFailedCode);
        store.Transactions.Should().BeEmpty();
    }

    // ── The guard inside the transaction ──

    [Theory(DisplayName = "097c SEEDED: the approval race - the old invoice is decided between the commit's read and the transaction; the guard faults, nothing is written, the submission ends Failed with invoice_status")]
    [InlineData(InvoiceStatusCodes.Approved, InvoiceStatusCodes.StateActive)]
    [InlineData(InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.StateActive)]
    [InlineData(InvoiceStatusCodes.Paid, InvoiceStatusCodes.StateInactive)]
    public async Task ApprovalRace_IsFailed_NothingWritten(int status, int state)
    {
        var store = CorrectionStore(beforeTransaction: s => s.ChangeOldInvoice(status, state));

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Refused, "the job marks a Refused result Failed: no retry can ever commit it");
        result.ReasonCode.Should().Be(SubmissionReasonCodes.CorrectionInvoiceStatus);
        store.Transactions.Should().ContainSingle("the transaction was sent and rolled back");
        store.OldInvoiceStatus.Should().Be(status, "the approval stands");
        store.OldInvoiceReads.Should().HaveCount(2, "read at commit, then again after the guard faulted");
        NothingWritten(store);
    }

    [Fact(DisplayName = "097c: a row-version race (the old invoice edited, still In Review) is the transaction's ordinary Retry, nothing written; the next delivery commits")]
    public async Task RowVersionRace_IsARetry_ThenCommits()
    {
        var raced = false;
        var store = CorrectionStore(beforeTransaction: s =>
        {
            if (!raced)
            {
                raced = true;
                s.ChangeOldInvoice();
            }
        });
        var outcome = await CorrectionOutcomeAsync();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.TransactionFailedCode);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        NothingWritten(store);

        var again = await store.Build().PersistAsync(outcome, null);

        again.Status.Should().Be(SubmissionPersistStatus.Committed);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded);
        store.Index.Sent.Single().IdempotencyKey.Should().Be(IndexKey(OldInvoiceId, OldVersion + 1));
    }

    // ── The undo (commit gate, step 5) ──

    [Fact(DisplayName = "097c: the undo deletes the new invoice's rows, then reopens the old invoice to In Review (097a's undo request at the version read then) and re-indexes it")]
    public async Task Undo_ReopensTheOldInvoice()
    {
        var store = CorrectionStore(afterCommit: s => s.Files.Gateway.Overwrite(
            s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5)));

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeFalse();
        ShouldBeUndone(store, SubmissionReasonCodes.FileUnservable, FileId);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.OldInvoice!.GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(InvoiceStatusCodes.StateActive);
        var reopen = store.OldInvoiceWrites.Should().ContainSingle().Subject;
        reopen.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        reopen.Target.RowVersion.Should().Be((OldVersion + 1).ToString(), "the version the supersede left");
        store.Index.Sent.Select(j => j.IdempotencyKey).Should().Equal(
            [IndexKey(OldInvoiceId, OldVersion), IndexKey(OldInvoiceId, OldVersion + 1)], "the commit's close and the undo's reopen each re-index, under their own version");
    }

    [Fact(DisplayName = "097c: an old invoice already In Review counts as done - the compensation run again writes nothing and completes")]
    public async Task Undo_AlreadyInReview_IsDone()
    {
        var store = CorrectionStore(afterCommit: s => s.Files.Gateway.Overwrite(
            s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5)));
        await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        var writes = store.OldInvoiceWrites.Count;

        (await store.Gate!.CompensateAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None)).Should().BeTrue();

        store.OldInvoiceWrites.Should().HaveCount(writes, "Current == Requested: nothing to undo, and not a failure");
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Fact(DisplayName = "097c (review K3): an old invoice in a status the lifecycle does not know, with no version, cannot carry this commit's close - the undo is done, not held")]
    public async Task Undo_UnknownStatusWithoutVersion_IsDone()
    {
        var store = CorrectionStore(afterCommit: s =>
        {
            s.Files.Gateway.Overwrite(s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5));
            s.ChangeOldInvoice(statusCode: 123456789);
            s.OldInvoice!.Attributes.Remove("versionnumber");
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeFalse("only a Superseded invoice needs the reopen");
        store.OldInvoiceWrites.Should().BeEmpty();
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionInvoice);
    }

    [Fact(DisplayName = "097c SEEDED: a stale row version at the reopen is reported - the undo is held (Failed, link kept, note pending), the change is not overwritten; a later compensation reopens")]
    public async Task Undo_StaleVersion_IsHeld_ThenCompletes()
    {
        var store = CorrectionStore(afterCommit: s =>
        {
            s.Files.Gateway.Overwrite(s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5));
            s.BeforeOldInvoiceWrite = t =>
            {
                t.BeforeOldInvoiceWrite = null;
                t.ChangeOldInvoice(); // another writer touched the Superseded invoice after the undo read it
            };
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeTrue("the stale version is reported as a held undo, not swallowed as done");
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded, "the conditional reopen never overwrites the change");
        store.InvoiceExists.Should().BeFalse("the new invoice's rows went first");
        store.Submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice).Should().NotBeNull("held: the link stays until the reopen");
        store.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionAdminAttention)
            .Should().Contain(AdminAttentionText.UndoPendingNote(SubmissionReasonCodes.FileUnservable));
        store.Index.Sent.Select(j => j.IdempotencyKey).Should().Equal([IndexKey(OldInvoiceId, OldVersion)], "only the commit's: the reopen did not happen");

        (await store.Gate!.CompensateAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None)).Should().BeTrue();

        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.OldInvoiceWrites.Should().HaveCount(2);
        store.Submission.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.SubmissionInvoice);
        store.Index.Sent.Select(j => j.IdempotencyKey).Should().Equal(IndexKey(OldInvoiceId, OldVersion), IndexKey(OldInvoiceId, OldVersion + 2));
    }

    [Fact(DisplayName = "097c: the undo of an automatically rejected Correction never reopens an invoice another correction superseded")]
    public async Task Undo_SupersededByAnotherCorrection_IsNotReopened()
    {
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var other = Guid.Parse("097c0000-0000-4000-8000-0000000000f2");
        var store = CorrectionStore(afterCommit: s =>
        {
            s.Files.Gateway.Overwrite(s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5));
            s.ChangeOldInvoice(InvoiceStatusCodes.Superseded, InvoiceStatusCodes.StateInactive); // another correction committed meanwhile
            s.OtherSupersedingInvoices.Add(other);
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(harness), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeFalse();
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded, "the close is the other correction's");
        store.OldInvoiceWrites.Should().BeEmpty();
    }
}
