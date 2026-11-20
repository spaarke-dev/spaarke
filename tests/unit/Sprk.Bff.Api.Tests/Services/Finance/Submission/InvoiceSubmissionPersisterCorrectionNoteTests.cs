using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095f (EB-D-225): a from-email Correction's note is written on the NEW invoice, in the same create as the rest of the
/// invoice, and nowhere else. Over the persister harness (<see cref="InvoiceSubmissionPersisterTests.Store"/>).
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    private const string NoteText = "The firm re-issued the invoice with the corrected rate.";

    private static Entity InvoiceCreate(Store store) =>
        store.Transactions.Single().OfType<CreateRequest>().Select(c => c.Target).Single(t => t.LogicalName == "sprk_invoice");

    [Fact(DisplayName = "095f (seed-proof): a Correction's note is in the NEW invoice's create, and the old invoice's close names no note")]
    public async Task CorrectionNote_IsInTheNewInvoicesCreate_NotOnTheOldInvoice()
    {
        var store = CorrectionStore();
        var outcome = (await CorrectionOutcomeAsync()) with { CorrectionNote = NoteText };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        InvoiceCreate(store).GetAttributeValue<string>("sprk_correctionnote").Should().Be(NoteText);
        var close = store.Transactions.Single().OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoice");
        close.Target.Id.Should().Be(OldInvoiceId);
        close.Target.Attributes.Should().NotContainKey("sprk_correctionnote", "nothing extra goes on the replaced invoice");
        store.OldInvoice!.Attributes.Should().NotContainKey("sprk_correctionnote");
    }

    [Fact(DisplayName = "095f (must-not-fire): a Correction with no note never names the column (an environment without it still persists)")]
    public async Task CorrectionWithoutANote_NeverNamesTheColumn()
    {
        var store = CorrectionStore();

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        InvoiceCreate(store).Attributes.Should().NotContainKey("sprk_correctionnote");
    }

    [Fact(DisplayName = "095f (must-not-fire): a New submission never writes a note, even when the outcome carries one")]
    public async Task NewSubmission_NeverWritesANote()
    {
        var store = new Store();
        var outcome = (await new ProcessorHarness().RunAsync()) with { CorrectionNote = NoteText };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        InvoiceCreate(store).Attributes.Should().NotContainKey("sprk_correctionnote");
    }

    // Task 095f-2e1 (EB-D-237): the from-document route now carries a note on the job, so a from-document Correction reaches the persister with
    // one, and the persister's "no note" warning (legacy payloads only) must stay quiet for it. The control proves the warning still fires.
    private const string NoNoteWarning = "carries no correction note";

    [Fact(DisplayName = "095f-2e1 (must-not-fire): a Correction that carries a note (a from-document one now does) never logs the no-note warning")]
    public async Task CorrectionWithANote_DoesNotLogTheNoNoteWarning()
    {
        var logger = new CapturingLogger();
        var store = CorrectionStore(logger: logger);

        var result = await store.Build().PersistAsync((await CorrectionOutcomeAsync()) with { CorrectionNote = NoteText }, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        InvoiceCreate(store).GetAttributeValue<string>("sprk_correctionnote").Should().Be(NoteText);
        logger.Entries.Should().NotContain(e => e.Message.Contains(NoNoteWarning));
    }

    [Fact(DisplayName = "095f-2e1 (must-fire control): a Correction with no note (a legacy payload) still logs the no-note warning once and persists")]
    public async Task CorrectionWithoutANote_StillLogsTheNoNoteWarning()
    {
        var logger = new CapturingLogger();
        var store = CorrectionStore(logger: logger);

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        logger.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains(NoNoteWarning)).Should().ContainSingle();
    }

    // Task 095f-2e2a (EB-D-245): the internal upload route now takes a Correction. Its submission is an Internal upload (no source document, the
    // PDF staged under the submission id) naming the replaced invoice's submission; the commit is the same as every other channel's.
    [Fact(DisplayName = "095f-2e2a: an internal upload Correction commits like every channel's: the new invoice supersedes the old one, the old one closes Superseded in the same transaction, and the note is on the new invoice")]
    public async Task InternalUploadCorrection_SupersedesTheOldInvoice_AndWritesTheNote()
    {
        var store = CorrectionStore();
        store.Submission["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal);
        store.Submission.Attributes.Should().NotContainKey("sprk_document", "an upload names no source document");

        var result = await store.Build().PersistAsync((await CorrectionOutcomeAsync()) with { CorrectionNote = NoteText }, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var transaction = store.Transactions.Should().ContainSingle().Subject;
        var created = InvoiceCreate(store);
        created.GetAttributeValue<EntityReference>("sprk_supersedesinvoice").Id.Should().Be(OldInvoiceId);
        created.GetAttributeValue<string>("sprk_correctionnote").Should().Be(NoteText);
        transaction.OfType<UpdateRequest>().Should().ContainSingle(u => u.Target.LogicalName == "sprk_invoice" && u.Target.Id == OldInvoiceId);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.Superseded);
    }

    [Theory(DisplayName = "095f: a note over the column's length or with a control character is never written, never truncated")]
    [InlineData("tooLong")]
    [InlineData("control")]
    public async Task ARefusedNote_IsNeverWritten(string kind)
    {
        var store = CorrectionStore();
        var note = kind == "tooLong" ? new string('x', InvoiceCorrectionNote.MaxLength + 1) : "bad\u0000note";
        var outcome = (await CorrectionOutcomeAsync()) with { CorrectionNote = note };

        await store.Build().PersistAsync(outcome, null);

        InvoiceCreate(store).Attributes.Should().NotContainKey("sprk_correctionnote");
    }

    [Fact(DisplayName = "095f: a note of exactly the maximum length, with line breaks and tabs, is written whole")]
    public async Task ANoteOfTheMaximumLength_IsWrittenWhole()
    {
        var store = CorrectionStore();
        var note = "line one\r\n\tline two " + new string('y', InvoiceCorrectionNote.MaxLength - "line one\r\n\tline two ".Length);
        note.Length.Should().Be(InvoiceCorrectionNote.MaxLength);

        await store.Build().PersistAsync((await CorrectionOutcomeAsync()) with { CorrectionNote = note }, null);

        InvoiceCreate(store).GetAttributeValue<string>("sprk_correctionnote").Should().Be(note);
    }
}
