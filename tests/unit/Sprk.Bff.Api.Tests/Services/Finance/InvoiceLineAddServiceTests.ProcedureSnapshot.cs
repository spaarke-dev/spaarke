using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>ebilling-r1 082d fix round F4: what Add line writes into the <c>procedureSnapshot</c> member of the stored findings (see <see cref="ProcedureSnapshotPatchKit"/>).</summary>
public sealed partial class InvoiceLineAddServiceTests
{
    private static void SeedSnapshot(AddLineWorld add) =>
        StoreRoot(add, root => root[ProcedureSnapshot.Member] = ProcedureSnapshotPatchKit.Seeded());

    /// <summary>The "before" side has the stored two lines; the "after" side has the added line too.</summary>
    private static Func<BillingEvaluationRequest, BillingEvaluationResult, BillingEvaluationResult> BasisPerSide(
        BillingProcedureBasis before, BillingProcedureBasis after) =>
        (request, result) => result with { ProcedureBasis = request.Lines.Count > 2 ? after : before };

    [Fact(DisplayName = "082d fix round F4 add line must-fire: after Add line the stored procedureSnapshot is the re-run's basis, not the one the submission was stored with")]
    public async Task AddLine_WritesTheReRunsBasis()
    {
        var add = new AddLineWorld
        {
            Shape = BasisPerSide(
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, false, 2),
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.AfterVersion, false, 2, 3)),
        };
        SeedSnapshot(add);
        ProcedureSnapshotPatchKit.AppliedOf(add.Findings()).Should().Equal(ProcedureSnapshotPatchKit.SeededVersion);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        ProcedureSnapshotPatchKit.AppliedOf(add.Findings()).Should().Equal(ProcedureSnapshotPatchKit.AfterVersion);
    }

    [Fact(DisplayName = "082d fix round F4 add line: a decided line whose applied rules the re-run changes makes the member JSON null (no snapshot, fail closed)")]
    public async Task AddLine_WithADecidedLineTheReRunChanges_WritesNull()
    {
        var add = new AddLineWorld
        {
            // The decided fee line (index 2) is judged by the version before the add and by no version after it: the stored per-line entries of a
            // decided line are not patched, so the whole-invoice basis of the re-run would not describe them.
            Shape = BasisPerSide(
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, false, 2),
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, true)),
        };
        add.World.Line(W.FeeLineId)["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        SeedSnapshot(add);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        var stored = add.Findings();
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: readers treat it as no snapshot");
        ProcedureSnapshot.Read(stored).Should().BeNull();
    }

    [Fact(DisplayName = "082d fix round F4 add line control: a decided line the re-run leaves alone does not stop the re-run's basis from being written")]
    public async Task AddLine_WithAnUnaffectedDecidedLine_StillWritesTheReRunsBasis()
    {
        var add = new AddLineWorld
        {
            Shape = BasisPerSide(
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, false, 2),
                ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, false, 2, 3)),
        };
        add.World.Line(W.FeeLineId)["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        SeedSnapshot(add);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        ProcedureSnapshotPatchKit.AppliedOf(add.Findings()).Should().Equal(ProcedureSnapshotPatchKit.BeforeVersion);
        add.Findings()[ProcedureSnapshot.Member]!["applications"]![0]!["applied"]!["lines"]!.AsArray().Select(l => l!.GetValue<int>()).Should().Equal([2, 3], "the re-run applies the version to the added line too: the member is the re-run's, not null");
    }

    [Fact(DisplayName = "082d fix round F4 add line must-not-fire: a line refused into a Procedure gap writes nothing, so the stored member is byte-identical")]
    public async Task ARefusedAddLine_LeavesTheMemberByteIdentical()
    {
        var add = new AddLineWorld();
        SeedSnapshot(add);
        var stored = add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson");
        var member = add.Findings()[ProcedureSnapshot.Member]!.ToJsonString();
        var kit = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 9, 15));
        kit.Rule("Ended", "ENDED-CAP", RuleType.DailyHoursCap);
        var loaded = Kit.Loaded() with { Procedures = kit.Build() };

        var result = await add.Service(loaded).AddAsync(Fee(date: new DateOnly(2026, 9, 20)), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.ProcedureGap);
        add.Transactions.Should().BeEmpty();
        add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored);
        add.Findings()[ProcedureSnapshot.Member]!.ToJsonString().Should().Be(member);
    }
}
