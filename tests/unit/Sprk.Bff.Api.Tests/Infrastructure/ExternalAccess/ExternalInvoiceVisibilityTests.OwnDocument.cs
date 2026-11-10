using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 165f: both external visibility readers decide only from the submission's own document. A submission's
/// <c>sprk_document</c> counts only when <see cref="InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf"/> holds for the pair (the
/// persister's rule; no copy); otherwise it contributes nothing and the submission, or its invoice, is decided as if the link were empty.
/// Also (from 165e, K1): reader 1 decides a submission with no invoice link by its own invoice's verdict when that invoice exists.
/// </summary>
public sealed partial class ExternalInvoiceVisibilityTests
{
    private static string UnreadableCode => $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceLinkUnreadable)}=1";

    // ── Reader 1: the submission decision ─────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "165f reader 1 (seed: the own-document check dropped from DecideSubmissionsAsync): a document that is not the submission's own contributes nothing; the submission is withheld as if it had no document")]
    [MemberData(nameof(ExternalCallers))]
    public async Task Reader1_ADocumentThatIsNotItsOwn_ContributesNothing(string caller)
    {
        var readable = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var submission = Guid.NewGuid(); // not derived from the document

        var decision = await DecideSubmissionsLogged(caller, SubmissionView(submission, InvoiceWorld.InternalChannel, readable));

        decision.IsVisible(submission).Should().BeFalse("a readable document that is not its own does not show it");
        decision.HiddenSummary().Should().Be(UnreadableCode, "decided as an Internal submission with no document");
        decision.Faulted.Should().BeFalse();
        _world.Reads("sprk_documents").Should().BeEmpty("the document is not a source of this submission");
        _events.DocumentNotOwnAt(LogLevel.Warning).Should().ContainSingle().Which.Should().Be((submission, readable));
    }

    [Theory(DisplayName = "165f reader 1 must not fire: the submission's own document still decides it (readable: visible; Restricted: withheld), with no event")]
    [MemberData(nameof(ExternalCallersBothWays))]
    public async Task Reader1_ItsOwnDocument_StillDecidesIt(string caller, bool restricted)
    {
        var document = _world.Document(Guid.NewGuid(), permission: restricted ? Restricted : Standard, matter: CleanMatter());
        var submission = InvoiceWorld.SubmissionFor(document);

        var decision = await DecideSubmissionsLogged(caller, SubmissionView(submission, InvoiceWorld.InternalChannel, document));

        decision.IsVisible(submission).Should().Be(!restricted, decision.HiddenSummary());
        if (restricted)
            decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceHidden)}=1");
        _world.Reads("sprk_documents").Should().ContainSingle("its own document is read and decides it");
        _events.DocumentNotOwn.Should().BeEmpty();
    }

    [Fact(DisplayName = "165f reader 1: the assistant tools decide a row naming a document that is not its own as the routes do (hidden)")]
    public async Task Reader1_TheAssistantTools_HideARowNamingADocumentThatIsNotItsOwn()
    {
        var readable = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var notOwn = _world.PendingSubmission(readable, InvoiceWorld.InternalChannel, id: Guid.NewGuid());
        var own = _world.PendingSubmission(readable, InvoiceWorld.InternalChannel);

        var hidden = await _world.NewService(_identity.Object, _events)
            .HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoicesubmission", new[] { notOwn, own }, _world.RootFlags, _world.SystemUserFirms);

        hidden.Should().BeEquivalentTo(new[] { notOwn }, "control: the row derived from the document is decided by it (readable: visible)");
        _events.DocumentNotOwnAt(LogLevel.Warning).Should().ContainSingle().Which.Should().Be((notOwn, readable));
    }

    public static TheoryData<string, string> Reader1FaultingReads()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "sprk_documents");
            data.Add(caller, "sprk_invoices");
        }

        return data;
    }

    [Theory(DisplayName = "165f reader 1: a read fault while deciding a submission by its own document withholds every asked submission (fail closed)")]
    [MemberData(nameof(Reader1FaultingReads))]
    public async Task Reader1_AReadFault_WithholdsTheSubmission(string caller, string faultingSet)
    {
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var submission = InvoiceWorld.SubmissionFor(document);
        var spa = Guid.NewGuid();
        _world.Faults[faultingSet] = HttpStatusCode.ServiceUnavailable;

        var decision = await DecideSubmissions(caller,
            SubmissionView(submission, InvoiceWorld.InternalChannel, document), SubmissionView(spa, InvoiceWorld.SpaChannel));

        decision.Faulted.Should().BeTrue();
        decision.IsVisible(submission).Should().BeFalse();
        decision.IsVisible(spa).Should().BeFalse();
    }

    [Fact(DisplayName = "165f reader 1: the assistant tools' read of the rows faulting gives the tool no data (fail closed)")]
    public async Task Reader1_TheToolsRowReadFaults_Throws()
    {
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var submission = _world.PendingSubmission(document, InvoiceWorld.InternalChannel);
        _world.Faults["sprk_invoicesubmissions"] = HttpStatusCode.ServiceUnavailable;

        var act = () => _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoicesubmission", new[] { submission }, _world.RootFlags, _world.SystemUserFirms);

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>("the failed read of the submission rows is the cause, not a setup error");
    }

    // ── Reader 2: the invoice predicate's submission read ───────────────────────────────────────────────────────────

    public static TheoryData<string, bool> Reader2NotOwnDocumentShapes() => new()
    {
        { "contact", true },
        { "contact", false },
        { "flagged-systemuser", true },
        { "flagged-systemuser", false },
    };

    [Theory(DisplayName = "165f reader 2 (seed: the own-document check dropped from ReadSubmissionSourcesAsync): the own submission's document that is not its own document is no source; the invoice is decided as if the link were empty")]
    [MemberData(nameof(Reader2NotOwnDocumentShapes))]
    public async Task Reader2_ADocumentThatIsNotItsOwn_ContributesNothing(string caller, bool sourcelessInvoicePdf)
    {
        var invoice = _world.NewInvoice();
        var foreign = _world.Document(Guid.NewGuid(), permission: Restricted, matter: CleanMatter());
        var own = _world.Submission(invoice, foreign); // the invoice's own submission, Internal, naming a document it was not derived from
        if (sourcelessInvoicePdf)
            _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf); // as an internal upload: an empty link with this PDF is visible

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().Be(sourcelessInvoicePdf, decision.HiddenSummary());
        if (!sourcelessInvoicePdf)
            decision.HiddenSummary().Should().Be(UnreadableCode, "an Internal own submission with no source and no InvoicePdf");
        decision.Faulted.Should().BeFalse();
        _world.Reads("sprk_documents").Should().BeEmpty("the document is not a source of the invoice");
        _events.DocumentNotOwnAt(LogLevel.Information).Should().ContainSingle().Which.Should().Be((own, foreign));
        _events.DocumentNotOwnAt(LogLevel.Warning).Should().BeEmpty("the submission reader owns the Warning");
    }

    [Theory(DisplayName = "165f reader 2 must not fire: the own submission's own document still decides the invoice (readable: visible; Restricted: hidden), with no event")]
    [MemberData(nameof(ExternalCallersBothWays))]
    public async Task Reader2_ItsOwnDocument_StillDecidesTheInvoice(string caller, bool restricted)
    {
        var invoice = _world.NewInvoice();
        _world.SubmissionSource(invoice, permission: restricted ? Restricted : Standard, matter: CleanMatter());

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().Be(!restricted, decision.HiddenSummary());
        if (restricted)
            decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceHidden)}=1");
        _world.Reads("sprk_documents").Should().ContainSingle();
        _events.DocumentNotOwn.Should().BeEmpty();
    }

    public static TheoryData<string, string> Reader2FaultingReads()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "sprk_invoicesubmissions");
            data.Add(caller, "sprk_documents");
        }

        return data;
    }

    [Theory(DisplayName = "165f reader 2: a read fault while deciding an invoice by its own submission's document hides the invoice (fail closed)")]
    [MemberData(nameof(Reader2FaultingReads))]
    public async Task Reader2_AReadFault_HidesTheInvoice(string caller, string faultingSet)
    {
        var invoice = _world.NewInvoice();
        _world.SubmissionSource(invoice, permission: Standard, matter: CleanMatter());
        _world.Faults[faultingSet] = HttpStatusCode.ServiceUnavailable;

        var decision = await Decide(caller, invoice);

        decision.Faulted.Should().BeTrue();
        decision.IsVisible(invoice).Should().BeFalse();
    }

    // ── The event and its level split ───────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "165f: the document event carries only its code and the two ids")]
    public async Task TheDocumentEvent_CarriesOnlyItsCodeAndTheIds()
    {
        var document = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var submission = Guid.NewGuid();

        await DecideSubmissionsLogged("contact", SubmissionView(submission, InvoiceWorld.InternalChannel, document));

        var entry = _events.Entries.Should()
            .ContainSingle(e => Equals(e.Values.GetValueOrDefault("EventCode"), ExternalInvoiceVisibility.DocumentLinkNotOwnEvent)).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Values.Keys.Should().BeEquivalentTo("EventCode", "SubmissionId", "DocumentId", "{OriginalFormat}");
        entry.Values["SubmissionId"].Should().Be(submission);
        entry.Values["DocumentId"].Should().Be(document);
    }

    [Fact(DisplayName = "165f: one decision call logs a document pair once at Warning; the invoice read of the same call logs it again only at Debug")]
    public async Task TheDocumentEvent_IsOneWarningPerPairPerCall()
    {
        var invoice = _world.NewInvoice();
        var foreign = _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var own = _world.Submission(invoice, foreign);
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf);

        var decision = await DecideSubmissionsLogged("contact",
            SubmissionView(own, InvoiceWorld.InternalChannel, foreign, invoice, InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(own).Should().BeTrue(decision.HiddenSummary());
        _events.DocumentNotOwnAt(LogLevel.Warning).Should().ContainSingle().Which.Should().Be((own, foreign));
        _events.DocumentNotOwnAt(LogLevel.Debug).Should().ContainSingle().Which.Should().Be((own, foreign));
        _events.DocumentNotOwnAt(LogLevel.Information).Should().BeEmpty();
    }

    // ── 165e K1: a submission with no invoice link whose own invoice exists takes that invoice's verdict ─────────────

    public static TheoryData<string, string> UnlinkedWithAHiddenOwnInvoice()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "linked-since-it-was-read"); // the commit linked it after the view was read; the invoice's source is Restricted
            data.Add(caller, "link-cleared"); // the invoice exists, and no submission linked to it is its own
        }

        return data;
    }

    [Theory(DisplayName = "165f K1 (seed: the own-invoice lookup dropped from DecideSubmissionsAsync): a submission with no invoice link whose own invoice exists and is hidden is withheld, though its channel alone would show it")]
    [MemberData(nameof(UnlinkedWithAHiddenOwnInvoice))]
    public async Task K1_AnUnlinkedSubmission_WhoseOwnInvoiceIsHidden_IsWithheld(string caller, string shape)
    {
        var submission = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        _world.Invoice(invoice);
        _world.Source(invoice, permission: Restricted);
        if (shape == "linked-since-it-was-read")
            _world.Submission(invoice, document: null, InvoiceWorld.SpaChannel, id: submission);

        var decision = await DecideSubmissionsLogged(caller, SubmissionView(submission, InvoiceWorld.SpaChannel));

        decision.IsVisible(submission).Should().BeFalse("a Spa submission is visible by channel alone; its own invoice is hidden");
        decision.HiddenSummary().Should().Be(shape == "link-cleared"
            ? OwnMissingCode
            : $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceHidden)}=1");
        decision.Faulted.Should().BeFalse();
    }

    [Fact(DisplayName = "165f K1: the assistant tools decide an unlinked submission row whose own invoice exists by that invoice, as the routes do")]
    public async Task K1_TheAssistantTools_DecideAnUnlinkedRowByItsOwnInvoice()
    {
        var hiddenRow = _world.PendingSubmission(null, InvoiceWorld.SpaChannel);
        _world.Invoice(InvoiceSubmissionPersister.InvoiceIdFor(hiddenRow)); // exists, with no submission linked to it
        var pending = _world.PendingSubmission(null, InvoiceWorld.SpaChannel); // no invoice yet

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoicesubmission", new[] { hiddenRow, pending }, _world.RootFlags, _world.SystemUserFirms);

        hidden.Should().BeEquivalentTo(new[] { hiddenRow }, "control: one whose invoice does not exist yet is decided by its channel");
    }

    [Theory(DisplayName = "165f K1 must not fire: a submission with no invoice link whose own invoice exists and is visible is visible")]
    [MemberData(nameof(ExternalCallers))]
    public async Task K1_AnUnlinkedSubmission_WhoseOwnInvoiceIsVisible_IsVisible(string caller)
    {
        var submission = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        _world.Invoice(invoice);
        _world.SpaUpload(invoice, submission);

        var decision = await DecideSubmissionsLogged(caller, SubmissionView(submission, InvoiceWorld.SpaChannel));

        decision.IsVisible(submission).Should().BeTrue(decision.HiddenSummary());
        _world.Reads("sprk_storedfiles").Should().ContainSingle("decided by its own invoice");
    }

    public static TheoryData<string, InvoiceSubmissionStatus, string> PendingWithoutAnInvoice()
    {
        var data = new TheoryData<string, InvoiceSubmissionStatus, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            foreach (var status in new[] { InvoiceSubmissionStatus.Received, InvoiceSubmissionStatus.Processing })
            {
                data.Add(caller, status, "spa");
                data.Add(caller, status, "internal-own-readable-document");
            }
        }

        return data;
    }

    [Theory(DisplayName = "165f K1 must not fire: a Received or Processing submission with no invoice yet is decided by channel, not withheld")]
    [MemberData(nameof(PendingWithoutAnInvoice))]
    public async Task K1_ASubmissionWithNoInvoiceYet_IsDecidedByChannel(string caller, InvoiceSubmissionStatus status, string shape)
    {
        var document = shape == "spa" ? (Guid?)null : _world.Document(Guid.NewGuid(), permission: Standard, matter: CleanMatter());
        var submission = document is { } d ? InvoiceWorld.SubmissionFor(d) : Guid.NewGuid();
        var channel = shape == "spa" ? InvoiceWorld.SpaChannel : InvoiceWorld.InternalChannel;

        var decision = await DecideSubmissionsLogged(caller, SubmissionView(submission, channel, document, status: status));

        decision.IsVisible(submission).Should().BeTrue(decision.HiddenSummary());
        _world.Reads("sprk_invoices").Should().ContainSingle("its own invoice is looked up, and does not exist yet");
        _world.Reads("sprk_storedfiles").Should().BeEmpty("no invoice is decided for it");
    }

    // ── K3-1 (fix round 1): the existence read fails closed and reads every chunk ─────────────────────────────────────

    [Theory(DisplayName = "165f K3-1 (seed: a stray row ignored by ReadExistingInvoicesAsync): a row the filter cannot have produced fails closed; the submission is withheld")]
    [InlineData("contact", "other-id")]
    [InlineData("contact", "no-id")]
    [InlineData("flagged-systemuser", "other-id")]
    [InlineData("flagged-systemuser", "no-id")]
    public async Task K3_AStrayRowInTheExistenceRead_WithholdsTheSubmission(string caller, string shape)
    {
        var submission = Guid.NewGuid();

        // Control: with no stray row the invoice does not exist yet and the Spa submission is visible by its channel.
        var before = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.SpaChannel));
        before.IsVisible(submission).Should().BeTrue(before.HiddenSummary());

        _world.Stray["sprk_invoices"] = new()
        {
            new Dictionary<string, object?> { ["sprk_invoiceid"] = shape == "no-id" ? null : Guid.NewGuid().ToString() },
        };
        var decision = await DecideSubmissions(caller, SubmissionView(submission, InvoiceWorld.SpaChannel));

        decision.IsVisible(submission).Should().BeFalse("a row the filter cannot produce counts the chunk's invoices as existing: decided by the invoice");
        decision.Faulted.Should().BeFalse();
    }

    private static List<Guid> UnlinkedSpaSubmissions(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Theory(DisplayName = "165f K3-1: more than 50 unlinked submissions are looked up in two chunks, both are read and each chunk's invoices are decided")]
    [MemberData(nameof(ExternalCallers))]
    public async Task K3_MoreThan50UnlinkedSubmissions_ReadBothChunks(string caller)
    {
        var ids = UnlinkedSpaSubmissions(60);
        var first = ids[0];
        var second = ids[55];
        _world.Invoice(InvoiceSubmissionPersister.InvoiceIdFor(first)); // exists, no submission linked: hidden
        _world.Invoice(InvoiceSubmissionPersister.InvoiceIdFor(second));

        var decision = await DecideSubmissions(caller, ids.Select(id => SubmissionView(id, InvoiceWorld.SpaChannel)).ToArray());

        var reads = _world.Reads("sprk_invoices");
        reads.Should().HaveCount(2, "60 invoices are looked up in chunks of 50");
        reads.Should().ContainSingle(u => u.Contains(InvoiceSubmissionPersister.InvoiceIdFor(first).ToString()));
        reads.Should().ContainSingle(u => u.Contains(InvoiceSubmissionPersister.InvoiceIdFor(second).ToString()));
        decision.IsVisible(first).Should().BeFalse("its own invoice exists in the first chunk and is hidden");
        decision.IsVisible(second).Should().BeFalse("its own invoice exists in the second chunk and is hidden");
        ids.Where(id => id != first && id != second).Should().OnlyContain(id => decision.IsVisible(id), "no invoice yet: decided by channel");
        decision.Faulted.Should().BeFalse();
    }

    [Theory(DisplayName = "165f K3-1: a fault in the second chunk of the existence read withholds that chunk's submissions (and, the decision being one, the page)")]
    [MemberData(nameof(ExternalCallers))]
    public async Task K3_AFaultInTheSecondChunk_WithholdsItsSubmissions(string caller)
    {
        var ids = UnlinkedSpaSubmissions(60);
        var inSecondChunk = InvoiceSubmissionPersister.InvoiceIdFor(ids[55]).ToString();
        _world.FaultOn = url => url.Contains("/sprk_invoices?", StringComparison.Ordinal) && url.Contains(inSecondChunk, StringComparison.Ordinal)
            ? HttpStatusCode.ServiceUnavailable
            : null;

        var decision = await DecideSubmissions(caller, ids.Select(id => SubmissionView(id, InvoiceWorld.SpaChannel)).ToArray());

        decision.Faulted.Should().BeTrue();
        ids.Skip(50).Should().OnlyContain(id => !decision.IsVisible(id), "the submissions of the chunk that faulted");
        ids.Should().OnlyContain(id => !decision.IsVisible(id), "one decision per page: a read fault withholds every asked submission");
    }
}
