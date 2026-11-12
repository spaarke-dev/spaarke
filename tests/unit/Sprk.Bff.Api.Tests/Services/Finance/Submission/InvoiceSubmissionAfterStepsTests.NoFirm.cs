using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095c-3 (ontology-r1 D-201): the after-commit steps of an invoice submitted without a firm. The stored firm_missing
/// pipeline finding is written as the SubmissionIntegrity "Firm needed" Signal on the invoice; nothing is recorded (no rule decided it);
/// the submission ends Accepted with <c>ebilling.submission.firm_missing</c>.
/// </summary>
public partial class InvoiceSubmissionAfterStepsTests
{
    [Fact(DisplayName = "095c-3: a no-firm invoice gets the 'Firm needed' system Signal on the invoice, no Decision Record, notify Accepted, terminal Accepted firm_missing")]
    public async Task NoFirm_WritesTheFirmNeededSignal_AndEndsAcceptedFirmMissing()
    {
        var outcome = await InvoiceSubmissionProcessorTests.NoFirm().RunAsync();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        result.TerminalStatus.Should().Be(InvoiceSubmissionStatus.Accepted);
        var system = world.Writes.Should().ContainSingle().Subject;
        system.PolicyCode.Should().Be(BillingRuleCatalogue.SystemPolicyCode);
        system.SubjectEntityLogicalName.Should().Be("sprk_invoice");
        system.SubjectId.Should().Be(InvoiceId);
        system.ShortHeadline.Should().Be(BillingRuleCatalogue.FirmMissingHeadline).And.Be("Firm needed");
        system.MessageTemplate.Should().Be(BillingRuleCatalogue.FirmMissingTemplate);
        world.RecordCreates.Should().Be(0, "no rule decided it; a reviewer adds the firm (095d)");
        world.Notified.Should().ContainSingle().Which.Outcome.Should().Be(SubmissionDecisionOutcome.Accepted);
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionReasonCode]
            .Should().Be(SubmissionReasonCodes.FirmMissing);
    }

    [Fact(DisplayName = "095c-3: with no firm AND no code set, the invoice's one system Signal is titled by the firm hold (it decides approval); the coding note stays in the group")]
    public async Task NoFirm_AndUncoded_TheFirmHoldTitlesTheInvoiceSignal()
    {
        var harness = InvoiceSubmissionProcessorTests.NoFirm();
        harness.Coding.Failure = new LineCodingException(LineCodingException.NoCodeSet, "none");
        var outcome = await harness.RunAsync();
        var world = new World(outcome);

        var result = await world.Build().RunAsync(Persisted(outcome), null);

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var system = world.Writes.Should().ContainSingle("both notes are on the invoice: one (policy, subject) group").Subject;
        system.ShortHeadline.Should().Be(BillingRuleCatalogue.FirmMissingHeadline,
            "coding.no_code_set sorts before submission.firm_missing by code; the firm hold must still title the Signal");
        world.TerminalWrites.Should().ContainSingle().Which[InvoiceSubmissionPersister.SubmissionReasonCode]
            .Should().Be(SubmissionReasonCodes.FirmMissing);
    }
}
