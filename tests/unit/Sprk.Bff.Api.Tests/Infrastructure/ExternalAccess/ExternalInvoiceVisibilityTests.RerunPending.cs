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
        var invoice = Guid.NewGuid();
        var submission = Guid.NewGuid();
        _world.SpaUpload(invoice, submission);
        var view = SubmissionView(submission, InvoiceWorld.SpaChannel, invoice: invoice);

        var pending = await DecideSubmissions(caller, view with { OutcomeReasonCode = SubmissionReasonCodes.FirmMissing });
        var committed = await DecideSubmissions(caller, view with { OutcomeReasonCode = SubmissionReasonCodes.Blocked });

        pending.IsVisible(submission).Should().BeFalse("partners only see a state decided with the firm");
        pending.HiddenSummary().Should().Be(NoFirmCode);
        committed.IsVisible(submission).Should().BeTrue("control: the re-run's commit replaced the reason", committed.HiddenSummary());
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
        var pendingInvoice = Guid.NewGuid();
        var decidedInvoice = Guid.NewGuid();
        var pending = _world.SpaUpload(pendingInvoice);
        var decided = _world.SpaUpload(decidedInvoice);
        _world.SetSubmissions(pendingInvoice, "sprk_outcomereasoncode", SubmissionReasonCodes.FirmMissing);

        var hidden = await _world.NewService(_identity.Object)
            .HideInvoiceFamilyRowsAsync("sprk_invoicesubmission", new[] { pending, decided }, _world.RootFlags);

        hidden.Should().Contain(pending);
        hidden.Should().NotContain(decided, "control: a submission decided with its firm is decided by its invoice, as before");
        _world.Reads("sprk_invoicesubmissions").Should().Contain(u => u.Contains("sprk_outcomereasoncode", StringComparison.Ordinal),
            "the tools' read of the rows carries the stored reason");
    }
}
