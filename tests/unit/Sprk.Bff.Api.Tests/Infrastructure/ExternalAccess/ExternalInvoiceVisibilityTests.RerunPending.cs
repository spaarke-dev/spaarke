using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 095d-2b-1 (uac-r2 2026-11-05 conditions 1 and 2): a submission whose firm Set firm wrote, but whose outcome the re-run has
/// not yet decided with it (its stored reason is still the firm hold's <see cref="SubmissionReasonCodes.FirmMissing"/>), is withheld from every
/// external caller exactly as a firmless one is, on the routes and in the assistant tools. The re-run's commit replaces that reason, which lifts it.
/// </summary>
public sealed partial class ExternalInvoiceVisibilityTests
{
    [Theory(DisplayName = "095d-2b-1 (seed: the withhold condition dropped): a submission with a firm but still held for it is withheld; the same row decided with its firm is visible")]
    [MemberData(nameof(ExternalCallers))]
    public async Task RerunPending_WithAFirm_IsWithheld_UntilTheRerunCommits(string caller)
    {
        var submission = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        _world.SpaUpload(invoice, submission);
        var view = SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: invoice);

        var pending = await DecideSubmissions(caller, view with { OutcomeReasonCode = SubmissionReasonCodes.FirmMissing });
        var committed = await DecideSubmissions(caller, view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked });

        pending.IsVisible(submission).Should().BeFalse("partners only see a state decided with the firm");
        pending.HiddenSummary().Should().Be(NoFirmCode);
        committed.IsVisible(submission).Should().BeTrue("control: the re-run's commit replaced the reason", committed.HiddenSummary());
    }

    // ── uac-r2 2026-11-06 C1: the invoice's field-secured marker decides, not a user-writable submission column ──

    [Theory(DisplayName = "095d-2b-1 fix round (uac-r2 C1, seed: the invoice-marker condition dropped from the submission withhold): a submission whose reason was edited away from firm_missing stays withheld while its invoice's marker is pending; it shows after the commit clears it")]
    [MemberData(nameof(ExternalCallers))]
    public async Task ReasonEditedAway_StaysWithheld_WhileTheInvoiceMarkerIsPending(string caller)
    {
        var submission = Guid.NewGuid();
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        _world.SpaUpload(invoice, submission);
        var edited = SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: invoice) with { OutcomeReasonCode = SubmissionReasonCodes.Blocked };
        _world.FirmHold(invoice, InvoiceFirmHold.InvoiceRerunPendingValue);

        var pending = await DecideSubmissions(caller, edited);
        _world.FirmHold(invoice, null); // the re-run's commit
        var committed = await DecideSubmissions(caller, edited);

        pending.IsVisible(submission).Should().BeFalse("the marker is pending, whatever the submission's own reason says");
        pending.HiddenSummary().Should().Be(NoFirmCode);
        committed.IsVisible(submission).Should().BeTrue("must not fire: the commit cleared the marker", committed.HiddenSummary());
    }

    [Theory(DisplayName = "095d-2b-1 fix round (uac-r2 C1): unlinking or repointing the submission does not lift the withhold - the invoice its pipeline created is asked too")]
    [InlineData("unlinked")]
    [InlineData("repointed")]
    public async Task UnlinkedOrRepointed_StaysWithheld_ByItsOwnInvoicesMarker(string edit)
    {
        var submission = Guid.NewGuid();
        var own = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        _world.FirmHold(own, InvoiceFirmHold.InvoiceRerunPendingValue);
        var elsewhere = _world.NewInvoice();
        _world.SpaUpload(elsewhere);
        var view = SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: edit == "unlinked" ? null : elsewhere)
            with
        { OutcomeReasonCode = SubmissionReasonCodes.Blocked };

        var pending = await DecideSubmissions("contact", view);
        _world.FirmHold(own, null);
        var committed = await DecideSubmissions("contact", view);

        pending.IsVisible(submission).Should().BeFalse();
        if (edit == "unlinked")
        {
            committed.IsVisible(submission).Should().BeTrue("must not fire: once cleared, the row is decided as before", committed.HiddenSummary());
        }
        else
        {
            // Task 165c: visibility counts only a submission's own invoice, so the repointed row stays withheld after the commit.
            committed.IsVisible(submission).Should().BeFalse();
            committed.HiddenSummary().Should().Be($"{InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.InvoiceLinkNotOwn)}=1");
        }
    }

    // ── uac-r2 2026-11-06 C2: the invoice itself while the re-run is pending ──

    [Theory(DisplayName = "095d-2b-1 fix round (uac-r2 C2, seed: the invoice-marker condition dropped from the invoice withhold): an invoice after Set firm and before the re-run commits is hidden from the firm's external user; after the commit it shows")]
    [MemberData(nameof(ExternalCallers))]
    public async Task TheInvoice_IsHidden_WhileItsMarkerIsPending_AndShowsAfterTheCommit(string caller)
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        _world.FirmHold(invoice, InvoiceFirmHold.InvoiceRerunPendingValue);

        var pending = await Decide(caller, invoice);
        _world.FirmHold(invoice, null); // the re-run's commit
        var committed = await Decide(caller, invoice);

        pending.IsVisible(invoice).Should().BeFalse("Set firm wrote sprk_vendororg, but the lines still carry their pre-firm statuses");
        pending.HiddenSummary().Should().Be(NoFirmCode);
        committed.IsVisible(invoice).Should().BeTrue("must not fire: the commit cleared the marker", committed.HiddenSummary());
    }

    [Theory(DisplayName = "095d-2b-1 fix round (uac-r2 C2): any non-blank hold hides the invoice (firm_missing, or a value it does not recognise: fail closed); blank does not")]
    [InlineData(SubmissionReasonCodes.FirmMissing, false)]
    [InlineData("an.unrecognised.value", false)]
    [InlineData("   ", true)]
    public async Task AnyNonBlankHold_HidesTheInvoice(string value, bool visible)
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        _world.FirmHold(invoice, value);

        (await Decide("contact", invoice)).IsVisible(invoice).Should().Be(visible);
    }

    [Fact(DisplayName = "095d-2b-1 fix round (uac-r2 C2): the assistant tools' taint closure hides the held invoice and its lines, and shows them after the commit")]
    public async Task TheTaintClosure_HidesTheHeldInvoiceAndItsLines()
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        var line = _world.FamilyRow("sprk_billingevent", invoice: invoice);
        _world.FirmHold(invoice, InvoiceFirmHold.InvoiceRerunPendingValue);
        var service = _world.NewService(_identity.Object);

        var invoices = await service.HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoice", new[] { invoice }, _world.RootFlags, _world.SystemUserFirms);
        var lines = await service.HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_billingevent", new[] { line }, _world.RootFlags, _world.SystemUserFirms);
        _world.FirmHold(invoice, null);
        var after = await service.HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_billingevent", new[] { line }, _world.RootFlags, _world.SystemUserFirms);

        invoices.Should().Contain(invoice);
        lines.Should().Contain(line);
        after.Should().BeEmpty("must not fire: the commit cleared the marker");
    }

    [Fact(DisplayName = "095d-2b-1 fix round (uac-r2 C2): the hold is read app-only, and a reader that cannot read it hides everything asked (fail closed)")]
    public async Task TheHold_IsReadAppOnly_AndAFaultHidesEverything()
    {
        var invoice = _world.NewInvoice();
        _world.SpaUpload(invoice);
        _world.FilingReadThrows = true;

        var decision = await Decide("contact", invoice);

        decision.IsVisible(invoice).Should().BeFalse();
        decision.Faulted.Should().BeTrue();
    }

    [Fact]
    public async Task RerunPending_AnInternalSystemuser_StillSeesIt()
    {
        var submission = Guid.NewGuid();

        var decision = await DecideSubmissions("internal-systemuser",
            SubmissionView(submission, InvoiceWorld.SpaChannel) with { OutcomeReasonCode = SubmissionReasonCodes.FirmMissing });

        decision.IsVisible(submission).Should().BeTrue("the withhold is for external callers only");
    }

    [Fact]
    public async Task RerunPending_TheAssistantTools_HideTheRow_AndReadTheReason()
    {
        var pendingInvoice = _world.NewInvoice();
        var decidedInvoice = _world.NewInvoice();
        var pending = _world.SpaUpload(pendingInvoice);
        var decided = _world.SpaUpload(decidedInvoice);
        _world.SetSubmissions(pendingInvoice, "sprk_outcomereasoncode", SubmissionReasonCodes.FirmMissing);

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoicesubmission", new[] { pending, decided }, _world.RootFlags, _world.SystemUserFirms);

        hidden.Should().Contain(pending);
        hidden.Should().NotContain(decided, "control: a submission decided with its firm is decided by its invoice, as before");

        // uac-r2 2026-11-06 C1: the reason edited away while the invoice's marker is pending: still hidden.
        _world.SetSubmissions(pendingInvoice, "sprk_outcomereasoncode", SubmissionReasonCodes.Blocked);
        _world.FirmHold(pendingInvoice, InvoiceFirmHold.InvoiceRerunPendingValue);
        (await _world.NewService(_identity.Object).HideInvoiceFamilyRowsAsync(_world.NewFamilyCaller(), "sprk_invoicesubmission", new[] { pending }, _world.RootFlags, _world.SystemUserFirms))
            .Should().Contain(pending, "the invoice's marker decides, not the submission's reason");
        _world.Reads("sprk_invoicesubmissions").Should().Contain(u => u.Contains("sprk_outcomereasoncode", StringComparison.Ordinal),
            "the tools' read of the rows carries the stored reason");
    }
}
