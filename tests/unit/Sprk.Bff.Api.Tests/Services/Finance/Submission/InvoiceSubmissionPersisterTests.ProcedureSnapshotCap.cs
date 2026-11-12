using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>ebilling-r1 082d fix round 2 (K): the persister drops the Procedure snapshot (JSON null) when it alone pushes the findings over the column.</summary>
public partial class InvoiceSubmissionPersisterTests
{
    [Fact(DisplayName = "082d fix round 2 K persister: a snapshot that pushes the findings JSON over the column is left out (JSON null) and the submission is persisted, not refused")]
    public async Task AnOverSizeProcedureSnapshot_IsDropped_AndTheSubmissionIsPersisted()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var basis = outcome.Evaluation!.ProcedureBasis ?? new BillingProcedureBasis([], [], []);
        var padded = outcome with
        {
            Evaluation = outcome.Evaluation with { ProcedureBasis = ProcedureSnapshotPatchKit.Padded(basis, ProcedureSnapshotPatchKit.OverTheColumn) },
        };
        InvoiceSubmissionPersister.BuildFindingsJson(padded, InvoiceId).Length.Should().BeGreaterThan(InvoiceSubmissionPersister.FindingsJsonMaxLength, "control: with the snapshot the findings are over the column");
        InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId).Length.Should().BeLessThan(InvoiceSubmissionPersister.FindingsJsonMaxLength, "control: without it they fit");
        var store = new Store();

        var result = await store.Build().PersistAsync(padded, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed, result.ReasonCode);
        var stored = JsonNode.Parse(Target(store.Transactions.Single()[^1]).GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: readers treat it as no snapshot");
        Target(store.Transactions.Single()[^1]).GetAttributeValue<string>("sprk_findingsjson").Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength);
        stored["findings"].Should().NotBeNull("control: the rest of the findings are written");
    }
}
