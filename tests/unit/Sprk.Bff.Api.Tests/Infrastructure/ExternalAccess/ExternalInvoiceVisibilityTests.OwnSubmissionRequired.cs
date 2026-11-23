using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 165e: an external caller sees an invoice only when the invoice's own submission
/// (<see cref="InvoiceBlockedHold.IsOwnSubmission"/>) is found among the submissions linked to it, is readable and passes the existing
/// checks. Otherwise the invoice is hidden (<see cref="InvoiceHiddenReason.OwnSubmissionMissing"/>, or the reason the existing checks
/// give), and so is every submission that takes its verdict. Internal callers are unchanged.
/// </summary>
public sealed partial class ExternalInvoiceVisibilityTests
{
    private static string OwnMissingCode => $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.OwnSubmissionMissing)}=1";

    public static TheoryData<string, string> NoOwnSubmissionShapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "no-submission");
            data.Add(caller, "only-another-submission");
        }

        return data;
    }

    [Theory(DisplayName = "165e must fire (seed: the own-submission check dropped from ReadSubmissionSourcesAsync): an invoice with no own submission is hidden, though its upload's own PDF would show it")]
    [MemberData(nameof(NoOwnSubmissionShapes))]
    public async Task NoOwnSubmission_HidesTheInvoice(string caller, string shape)
    {
        var invoice = _world.NewInvoice();
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf, firmVisible: true); // a sourceless InvoicePdf: visible on its own
        if (shape == "only-another-submission")
            _world.Submission(invoice, document: null, InvoiceWorld.SpaChannel, id: Guid.NewGuid());

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Be(OwnMissingCode);
        decision.Faulted.Should().BeFalse();
        _events.OwnMissingAt(LogLevel.Information).Should().ContainSingle().Which.Should().Be((null, invoice));
    }

    public static TheoryData<string, string> UnreadableOwnSubmissionShapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var caller in new[] { "contact", "flagged-systemuser" })
        {
            data.Add(caller, "id-unreadable");
            data.Add(caller, "channel-unreadable");
        }

        return data;
    }

    [Theory(DisplayName = "165e must fire: an invoice whose own submission cannot be read is hidden")]
    [MemberData(nameof(UnreadableOwnSubmissionShapes))]
    public async Task AnUnreadableOwnSubmission_HidesTheInvoice(string caller, string shape)
    {
        var invoice = _world.NewInvoice();
        var own = _world.SpaUpload(invoice);
        if (shape == "id-unreadable")
            _world.SetRow("sprk_invoicesubmissions", "sprk_invoicesubmissionid", own, "sprk_invoicesubmissionid", "not-a-guid");
        else
            _world.SetSubmissions(invoice, "sprk_channel", null);

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenSummary().Should().Be(shape == "id-unreadable"
            ? $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceLinkUnreadable)}=1"
            : $"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceKindUnknown)}=1");
    }

    [Theory(DisplayName = "165e must not fire: an invoice with its own submission is shown as today, with no event; an internal user sees one without")]
    [MemberData(nameof(ExternalCallers))]
    public async Task AnInvoiceWithItsOwnSubmission_IsShown(string caller)
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        var without = _world.NewInvoice();
        _world.WriterStoredFile(without, InvoiceWorld.InvoicePdf, firmVisible: true);

        var decision = await DecideLogged(caller, invoice);
        var internalCaller = await DecideLogged("internal-systemuser", without);

        decision.IsVisible(invoice).Should().BeTrue(decision.HiddenSummary());
        _events.OwnMissing.Should().BeEmpty();
        internalCaller.IsVisible(without).Should().BeTrue("an internal systemuser is unchanged");
    }

    [Fact(DisplayName = "165e (b): an invoice whose own submission exists but is linked to another invoice is hidden")]
    public async Task AnOwnSubmissionLinkedToAnotherInvoice_HidesTheInvoice()
    {
        var ownId = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(ownId);
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf, firmVisible: true);
        var elsewhere = _world.NewInvoice();
        _world.SpaUpload(elsewhere);
        _world.Submission(elsewhere, document: null, InvoiceWorld.SpaChannel, id: ownId);

        var decision = await DecideLogged("contact", invoice, elsewhere);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.HiddenReason(invoice).Should().Be(InvoiceHiddenReason.OwnSubmissionMissing);
        decision.IsVisible(elsewhere).Should().BeTrue("control: the other invoice keeps its own submission");
        _events.OwnMissing.Should().ContainSingle().Which.Should().Be((null, invoice));
    }

    // ── (a) a read fault hides the invoice in both readers ─────────────────────────────────────────────────────────

    [Theory(DisplayName = "165e (a) reader 2: when the own-submission read faults, the invoice is hidden (fail closed)")]
    [MemberData(nameof(ExternalCallers))]
    public async Task Reader2_TheOwnSubmissionReadFaults_HidesTheInvoice(string caller)
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        _world.Faults["sprk_invoicesubmissions"] = HttpStatusCode.ServiceUnavailable;

        var decision = await DecideLogged(caller, invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Theory(DisplayName = "165e (a) reader 1: when the own-submission read faults, the submission taking its invoice's verdict is withheld (fail closed)")]
    [MemberData(nameof(ExternalCallers))]
    public async Task Reader1_TheOwnSubmissionReadFaults_WithholdsTheSubmission(string caller)
    {
        var invoice = _world.NewInvoice();
        var own = _world.SpaUpload(invoice);
        _world.Faults["sprk_invoicesubmissions"] = HttpStatusCode.ServiceUnavailable;

        var decision = await DecideSubmissionsLogged(caller,
            SubmissionView(own, InvoiceWorld.SpaChannel, invoice: invoice, status: InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(own).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    // ── Reader 1 and the event ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "165e reader 1: a submission whose invoice's own submission is not found is withheld; one Warning with both ids, the invoice read logs it at Debug")]
    public async Task Reader1_TheInvoicesOwnSubmissionNotFound_IsWithheld_OneWarningPerPair()
    {
        var ownId = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(ownId);
        _world.WriterStoredFile(invoice, InvoiceWorld.InvoicePdf, firmVisible: true);
        // The route's view names the invoice, but the row linked to it is not there when the invoice is read.

        var decision = await DecideSubmissionsLogged("contact",
            SubmissionView(ownId, InvoiceWorld.SpaChannel, invoice: invoice, status: InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(ownId).Should().BeFalse();
        decision.HiddenSummary().Should().Be(OwnMissingCode);
        _events.OwnMissingAt(LogLevel.Warning).Should().ContainSingle().Which.Should().Be((ownId, invoice));
        _events.OwnMissingAt(LogLevel.Debug).Should().ContainSingle().Which.Should().Be((null, invoice));
        _events.OwnMissingAt(LogLevel.Information).Should().BeEmpty();
    }

    [Fact(DisplayName = "165e reader 1: when the invoice is already hidden for another reason, the invoice read logs the missing own submission at Information (not Debug)")]
    public async Task Reader1_AnInvoiceHiddenForAnotherReason_StillLogsTheMissingOwnSubmission()
    {
        var ownId = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(ownId);
        _world.StoredFileRaw(invoice, rawSource: "not-a-guid"); // hides the invoice first (source_link_unreadable)

        var decision = await DecideSubmissionsLogged("contact",
            SubmissionView(ownId, InvoiceWorld.SpaChannel, invoice: invoice, status: InvoiceSubmissionStatus.Accepted));

        decision.IsVisible(ownId).Should().BeFalse();
        decision.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.SourceLinkUnreadable)}=1");
        _events.OwnMissingAt(LogLevel.Information).Should().ContainSingle().Which.Should().Be((null, invoice));
        _events.OwnMissingAt(LogLevel.Warning).Should().BeEmpty();
        _events.OwnMissingAt(LogLevel.Debug).Should().BeEmpty();
    }

    [Fact(DisplayName = "165e: the event carries only its code and the invoice id")]
    public async Task TheOwnMissingEvent_CarriesOnlyItsCodeAndTheId()
    {
        var invoice = _world.NewInvoice();

        await DecideLogged("contact", invoice);

        var entry = _events.Entries.Should().ContainSingle(e => e.Values.ContainsKey("EventCode")).Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Values.Keys.Should().BeEquivalentTo("EventCode", "InvoiceId", "{OriginalFormat}");
        entry.Values["EventCode"].Should().Be(ExternalInvoiceVisibility.OwnSubmissionMissingEvent);
        entry.Values["InvoiceId"].Should().Be(invoice);
    }
}
