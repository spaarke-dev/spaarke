using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095c-4 (095c-3 review K4): the submission update in the commit transaction is conditional on the row version the persister
/// re-read (<c>IfRowVersionMatches</c>), so a firm or core-record write that lands between the re-read and the commit rolls the transaction back.
/// A firm or core-record change re-evaluates (with its own reason); any other change is the ordinary Retry. Also: the no-firm line finding
/// carries the firm cause that <see cref="InvoiceFirmHold"/> reads.
/// </summary>
/// <remarks>
/// <b>Seed proof (notes 095c section 14).</b> <c>FirmChanged_BetweenTheReReadAndTheCommit_ReEvaluates_NothingPersisted</c> fails when the
/// <c>ConcurrencyBehavior</c> is dropped (the transaction then commits the stale no-firm evaluation).
/// </remarks>
public partial class InvoiceSubmissionPersisterTests
{
    private static UpdateRequest SubmissionUpdateOf(IReadOnlyList<OrganizationRequest> requests) =>
        requests.OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoicesubmission");

    [Fact(DisplayName = "095c-4 K4: the submission update is IfRowVersionMatches on the re-read version; an untouched submission commits")]
    public async Task SubmissionUpdate_IsConditional_OnTheReReadVersion()
    {
        var store = new Store();
        store.Submission["versionnumber"] = 41L;

        var result = await store.Build().PersistAsync(await new ProcessorHarness().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var update = SubmissionUpdateOf(store.Transactions.Single());
        update.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        update.Target.RowVersion.Should().Be("41", "the version the persister re-read");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().NotBeNull("committed: the submission links its invoice");
    }

    [Theory(DisplayName = "095c-4 K4 SEEDED: the firm set (or the core record changed) between the re-read and the commit rolls back and re-evaluates with its own reason; nothing persisted")]
    [InlineData("firm")]
    [InlineData("core")]
    public async Task FirmChanged_BetweenTheReReadAndTheCommit_ReEvaluates_NothingPersisted(string change)
    {
        var store = NoFirmStore(new Store
        {
            BeforeTransaction = s =>
            {
                if (change == "firm")
                {
                    s.Submission["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.NewGuid()); // a direct write of the firm
                }
                else
                {
                    s.Submission.Attributes.Remove("sprk_regardingmatter");
                    s.Submission["sprk_regardingproject"] = new EntityReference("sprk_project", Project);
                }

                s.BumpSubmission();
            },
        });

        var result = await store.Build().PersistAsync(await InvoiceSubmissionProcessorTests.NoFirm().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.ReEvaluate);
        result.ReasonCode.Should().Be(change == "firm" ? SubmissionReasonCodes.FirmChanged : SubmissionReasonCodes.CoreRecordChanged);
        result.InvoiceId.Should().BeNull();
        store.Transactions.Should().ContainSingle("the one transaction was sent and rolled back by the condition");
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull("nothing committed: the submission links no invoice");
        store.Submission.GetAttributeValue<string>("sprk_outcomereasoncode").Should().BeNull("the stale firm_missing outcome was not written");
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "095c-4 K4: any other write of the submission between the re-read and the commit rolls back and is Retry (ebilling.submission.changed); nothing persisted")]
    public async Task OtherChange_BetweenTheReReadAndTheCommit_Retries_NothingPersisted()
    {
        var store = new Store { BeforeTransaction = s => s.BumpSubmission() };

        var result = await store.Build().PersistAsync(await new ProcessorHarness().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.SubmissionChangedCode);
        store.Transactions.Should().ContainSingle();
        store.Submission.GetAttributeValue<EntityReference>("sprk_invoice").Should().BeNull();
        store.TotalsCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "095c-4 K4 (fail closed): a submission read with no row version is Retry before any transaction")]
    public async Task NoRowVersion_IsRetry_NoTransaction()
    {
        var store = new Store();
        store.Submission.Attributes.Remove("versionnumber");

        var result = await store.Build().PersistAsync(await new ProcessorHarness().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.SubmissionVersionMissingCode);
        store.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "095c-4: every no-firm rules.not_checked line finding carries the firm cause; a firm invoice's lines carry none")]
    public async Task NoFirmLineFindings_CarryTheFirmCause()
    {
        var store = NoFirmStore();

        await store.Build().PersistAsync(await NoFirmTwoLines().RunAsync(), null);

        var submission = SubmissionUpdateOf(store.Transactions.Single()).Target;
        using var findings = JsonDocument.Parse(submission.GetAttributeValue<string>("sprk_findingsjson"));
        var notChecked = NotChecked(findings).ToList();
        notChecked.Should().HaveCount(2);
        notChecked.Should().OnlyContain(e => e.GetProperty(InvoiceFirmHold.LineFindingCauseMember).GetString() == SubmissionReasonCodes.FirmMissing);
        InvoiceFirmHold.IsHeld(submission).Should().BeTrue("the stored submission reads as held");

        // Must not fire: a firm invoice's committed submission is not held.
        var firmStore = new Store();
        await firmStore.Build().PersistAsync(await TwoLineHarness().RunAsync(), null);
        InvoiceFirmHold.IsHeld(SubmissionUpdateOf(firmStore.Transactions.Single()).Target).Should().BeFalse();
    }

    [Fact(DisplayName = "095c-5: the held outcome sets the invoice's own firm hold in the same transaction; a firm invoice's create never names the column")]
    public async Task HeldOutcome_SetsTheInvoiceHold_InTheSameTransaction()
    {
        var store = NoFirmStore();

        var result = await store.Build().PersistAsync(await NoFirmTwoLines().RunAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var transaction = store.Transactions.Single();
        var invoice = transaction.OfType<CreateRequest>().Single(c => c.Target.LogicalName == "sprk_invoice").Target;
        invoice.GetAttributeValue<string>(InvoiceFirmHold.InvoiceHoldColumn).Should().Be(InvoiceFirmHold.InvoiceHoldValue,
            "the invoice carries its own hold, written with the held outcome (uac-r2 2026-11-04)");
        InvoiceFirmHold.IsInvoiceHeld(invoice).Should().BeTrue();
        transaction.Should().Contain(r => r is UpdateRequest && ((UpdateRequest)r).Target.LogicalName == "sprk_invoicesubmission",
            "the hold and the held outcome are one transaction");

        // Must not fire: a firm invoice's create does not name the column (an environment without it still persists firm submissions).
        var firmStore = new Store();
        await firmStore.Build().PersistAsync(await TwoLineHarness().RunAsync(), null);
        firmStore.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == "sprk_invoice").Target
            .Contains(InvoiceFirmHold.InvoiceHoldColumn).Should().BeFalse();
    }
}
