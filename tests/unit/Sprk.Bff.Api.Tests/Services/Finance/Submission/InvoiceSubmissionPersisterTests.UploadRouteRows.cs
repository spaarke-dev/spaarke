using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 123 fix round 1 (F1-a): the commit gate and the rows the internal upload route (<c>POST /api/finance/invoices/{invoiceId}/files</c>)
/// adds to a committed invoice. The gate flips the Invoice PDF LAST, so a Servable Invoice PDF means the gate passed in an earlier run: a resume
/// then answers Passed with no count, no step 5 and no flip, whatever other rows the invoice has gained. The REAL gate over the same Dataverse,
/// file store and isolation world as the rest of this class.
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    /// <summary>A row id that sorts after every other row in the in-memory store's (id-ordered) query, so an id-ordered flip would reach it last.</summary>
    private static readonly Guid SortsLast = Guid.Parse("ffffffff-ffff-4fff-bfff-ffffffffff23");

    private static readonly byte[] SupportingFileBytes = "date,hours\n2026-10-01,7.5\n"u8.ToArray();

    private static StoredFileStatus StatusOf(Store store, Guid fileId) =>
        (StoredFileStatus)store.Files.Dataverse.Rows[fileId].GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value;

    [Fact(DisplayName = "123 F1-a (gate order): the Invoice PDF is flipped LAST - a commit with a supporting file flips the supporting file first, even when the store returns the PDF first")]
    public async Task Gate_FlipsTheInvoicePdfLast()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store
        {
            // A second file of the same commit, returned after the PDF by the id-ordered read.
            AfterCommit = s => s.Files.SeedFile(SortsLast, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId), SupportingFileBytes,
                StoredFileStatus.NotYetServable, StoredFilePurpose.InvoiceSupportingFile, contentType: "text/csv"),
        };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var flips = store.Files.Dataverse.Calls.Where(c => c.StartsWith($"update {StoredFileSchema.Entity} ", StringComparison.Ordinal)).ToList();
        flips.Should().Equal(
            [$"update {StoredFileSchema.Entity} {SortsLast}", $"update {StoredFileSchema.Entity} {FileId}"],
            "every other file first, the Invoice PDF last: its Servable status records that the whole commit was flipped");
        StatusOf(store, FileId).Should().Be(StoredFileStatus.Servable);
        StatusOf(store, SortsLast).Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "123 F1-a (must not fire): a resume after the gate passed, with a route-added row Not yet servable whose flip would be refused, answers Passed - nothing flipped, nothing undone")]
    public async Task Gate_ServablePdf_WithARouteOrphan_PassesAndTouchesNothing()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        store.FileStatus.Should().Be(StoredFileStatus.Servable);

        // The upload route's orphan: a row whose flip was refused (its bytes no longer match), left Not yet servable.
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId);
        store.Files.SeedFile(SortsLast, parent, SupportingFileBytes, StoredFileStatus.NotYetServable, StoredFilePurpose.InvoiceSupportingFile,
            contentType: "text/csv");
        store.Files.Gateway.Overwrite(StoredFileSchema.ContainerOf(StoredFilePurpose.InvoiceSupportingFile),
            StoredFileBlobName.Build(store.Files.TenantId, parent, SortsLast), "tampered"u8.ToArray(), store.Files.Gateway.Now.AddMinutes(5));
        var calls = store.Files.Dataverse.Calls.Count;

        var again = await store.Gate!.RunAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None);

        again.Should().Be(SubmissionCommitGateResult.Passed);
        store.Files.Dataverse.Calls.Skip(calls).Should().OnlyContain(c => c.StartsWith("query ", StringComparison.Ordinal), "the one row read; no flip, no withdraw");
        StatusOf(store, SortsLast).Should().Be(StoredFileStatus.NotYetServable, "the route's orphan is not the commit's to flip");
        store.FileStatus.Should().Be(StoredFileStatus.Servable);
        store.Deletes.Should().BeEmpty("a route orphan never undoes the firm's submission");
        store.InvoiceExists.Should().BeTrue();
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().NotBe((int)InvoiceSubmissionStatus.Failed);
    }

    [Fact(DisplayName = "123 F1-a (must not fire): a resume after the gate passed, with more than MaxFiles rows under the invoice, answers Passed - the count is the commit's, not the route's")]
    public async Task Gate_ServablePdf_WithMoreThanMaxFilesRows_Passes()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId);
        while (store.Files.Dataverse.Rows.Count <= SubmissionCommitGate.MaxFiles)
        {
            store.Files.SeedFile(Guid.NewGuid(), parent, SupportingFileBytes, StoredFileStatus.Servable, StoredFilePurpose.InvoiceSupportingFile,
                contentType: "text/csv");
        }

        var again = await store.Gate!.RunAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None);

        again.Should().Be(SubmissionCommitGateResult.Passed);
        store.Deletes.Should().BeEmpty();
        store.UndoTransactions.Should().BeEmpty();
        store.InvoiceExists.Should().BeTrue();
        store.Files.Dataverse.Rows.Values.Should().OnlyContain(r =>
            r.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value == (int)StoredFileStatus.Servable, "nothing withdrawn");
    }

    [Fact(DisplayName = "123 fix round 1 (gate read cap, must not fire): more than 51 rows under the invoice with the Servable PDF PAST the capped read - the resume answers Passed from the PDF's own read and touches nothing")]
    public async Task Gate_ServablePdf_PastTheCappedRead_PassesAndTouchesNothing()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        store.FileStatus.Should().Be(StoredFileStatus.Servable);

        // Route-added rows whose ids sort BEFORE the PDF's, more than the gate's capped read (MaxFiles + 1) takes; half are Not yet servable.
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId);
        for (var i = 1; i <= SubmissionCommitGate.MaxFiles + 5; i++)
        {
            store.Files.SeedFile(new Guid($"00000000-0000-4000-8000-{i:D12}"), parent, SupportingFileBytes,
                i % 2 == 0 ? StoredFileStatus.Servable : StoredFileStatus.NotYetServable, StoredFilePurpose.InvoiceSupportingFile, contentType: "text/csv");
        }

        // Dataverse's own answer to a capped, filtered read: the id-ordered first rows only. Precondition: the PDF is past the cap.
        store.Files.Dataverse.ApplyQueryFilters = true;
        store.Files.Dataverse.Rows.Keys.OrderBy(k => k).Take(SubmissionCommitGate.MaxFiles + 1).Should().NotContain(FileId);
        var statuses = store.Files.Dataverse.Rows.ToDictionary(r => r.Key, r => r.Value.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value);
        var calls = store.Files.Dataverse.Calls.Count;

        var again = await store.Gate!.RunAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None);

        again.Should().Be(SubmissionCommitGateResult.Passed);
        store.Files.Dataverse.Calls.Skip(calls).Should().OnlyContain(c => c.StartsWith("query ", StringComparison.Ordinal), "reads only; no flip, no withdraw");
        store.Files.Dataverse.Rows.ToDictionary(r => r.Key, r => r.Value.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value)
            .Should().Equal(statuses, "no status changed");
        store.Deletes.Should().BeEmpty("a valid submission is never undone over rows added after its gate passed");
        store.UndoTransactions.Should().BeEmpty();
        store.InvoiceExists.Should().BeTrue();
        store.Submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().NotBe((int)InvoiceSubmissionStatus.Failed);
    }

    [Fact(DisplayName = "123 fix round 1 (gate read cap): a fault of the Invoice PDF read throws (the job retries); nothing is undone")]
    public async Task Gate_PdfReadFault_Throws_NothingUndone()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        store.Files.Dataverse.QueryFault = new TimeoutException("Dataverse slow");

        await FluentActions.Invoking(() => store.Gate!.RunAsync(InvoiceSubmissionPersisterTests.Submission, CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();

        store.Deletes.Should().BeEmpty();
        store.InvoiceExists.Should().BeTrue();
        store.FileStatus.Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "123 F1-a (the kept rule, must not fire): the PDF not yet Servable but another file Servable (an earlier run stopped part-way) - step 5 is skipped and the PDF is flipped")]
    public async Task Gate_PartialFlip_SkipsTheReRun_AndFlipsThePdf()
    {
        var store = new Store();
        var outcome = AsSource(await new ProcessorHarness().RunAsync(), store);
        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);
        var copyId = store.SourceCopy!.Spec.FileId;
        var checks = store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter"));

        // An earlier run flipped the other file and stopped before the PDF; meanwhile the source moved (a re-run would now fail).
        store.Files.Dataverse.Rows[copyId][StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.NotYetServable);
        store.Files.Dataverse.AddRow(store.Files.Dataverse.Rows[copyId]);
        store.Files.SeedFile(SortsLast, new StoredFileParent(StoredFileParentKind.Invoice, SourceInvoice), SupportingFileBytes,
            StoredFileStatus.Servable, StoredFilePurpose.InvoiceSupportingFile, contentType: "text/csv");
        store.DocumentRow = new Entity("sprk_document", Document) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) };

        var again = await store.Gate!.RunAsync(SourceSubmission, CancellationToken.None);

        again.Should().Be(SubmissionCommitGateResult.Passed);
        store.DocumentReads.Count(c => c.Contains("sprk_relatedmatter")).Should().Be(checks, "step 5 passed in the earlier run; never twice");
        store.CopyStatus.Should().Be(StoredFileStatus.Servable, "the PDF is flipped now");
        store.Deletes.Should().BeEmpty();
    }
}
