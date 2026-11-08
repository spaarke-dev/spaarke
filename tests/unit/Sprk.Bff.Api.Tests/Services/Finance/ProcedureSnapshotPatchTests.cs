using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// What the two patch paths (a correction, an added line) write into the <c>procedureSnapshot</c> member of the stored findings (ebilling-r1 082d
/// fix round F4). The findings are re-judged by a re-run on both paths; the member must describe the re-run's basis, or be JSON null (no snapshot,
/// readers fail closed) when a decided line keeps findings the re-run would change. The evaluator's result is shaped so each side carries a basis
/// with a recognisable policy version.
/// </summary>
internal static class ProcedureSnapshotPatchKit
{
    internal static readonly Guid SeededVersion = Guid.Parse("082d0000-0000-4000-8000-00000000f401");
    internal static readonly Guid BeforeVersion = Guid.Parse("082d0000-0000-4000-8000-00000000f402");
    internal static readonly Guid AfterVersion = Guid.Parse("082d0000-0000-4000-8000-00000000f403");

    /// <summary>A basis with one Procedure rule version applied to the named lines (and, when <paramref name="invoice"/>, to the invoice).</summary>
    internal static BillingProcedureBasis BasisOf(Guid version, bool invoice = false, params int[] lines)
    {
        var subjects = lines.Select(BillingSubject.ForLine).ToList();
        if (invoice)
        {
            subjects.Insert(0, BillingSubject.ForInvoice());
        }

        return new BillingProcedureBasis([], [], [new BillingRuleApplication(version, BillingRuleSource.Procedure, subjects, [], [])]);
    }

    /// <summary>The snapshot an earlier run left in the findings (a version that neither side of the re-run carries).</summary>
    internal static JsonObject Seeded() => new()
    {
        ["version"] = ProcedureSnapshot.Version,
        ["chain"] = new JsonArray(),
        ["overrides"] = new JsonArray(),
        ["resolutions"] = new JsonArray(),
        ["applications"] = new JsonArray(new JsonObject
        {
            ["policyVersionId"] = SeededVersion.ToString("D"),
            ["source"] = nameof(BillingRuleSource.Procedure),
            ["applied"] = new JsonObject { ["invoice"] = true, ["lines"] = new JsonArray() },
        }),
    };

    internal static IReadOnlyList<Guid>? AppliedOf(JsonObject root) => ProcedureSnapshot.Read(root)?.AppliedPolicyVersionIds;
}

public sealed class ProcedureSnapshotPatchTests
{
    private const int FeeIndex = 2;
    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap);
    private static readonly BillingRuleCandidate WholeHours = Kit.Version("BILL-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");

    private static CorrectionCommand Hours(string value) => new(W.InvoiceId, W.FeeLineId, "Hours", value, false, W.Reviewer);

    /// <summary>The correction service over the real in-memory evaluator; the first evaluation of a re-run is the "before" side, the second the "after" side.</summary>
    private static InvoiceCorrectionService Service(CorrectionWorld world, Func<BillingEvaluationResult, bool, BillingEvaluationResult> shape, params BillingRuleCandidate[] rules)
    {
        var evaluator = Kit.Evaluator();
        var loaded = Kit.Loaded(rules);
        var calls = 0;
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), (request, facts) => shape(evaluator.Evaluate(request, facts), calls++ % 2 == 0),
            world.WriteSignalAsync, world.CloseSignalAsync, NullLogger<InvoiceCorrectionReevaluation>.Instance);
        world.Seam = reevaluation.ApplyAsync;
        return world.Service();
    }

    private static BillingEvaluationResult Basis(BillingEvaluationResult result, bool isBefore) =>
        result with
        {
            ProcedureBasis = isBefore
                ? ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.BeforeVersion, false, FeeIndex)
                : ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.AfterVersion, false, FeeIndex),
        };

    private static CorrectionWorld WorldWithSnapshot()
    {
        var world = new CorrectionWorld();
        InvoiceCorrectionReevaluationTests.Store(world);
        var root = Stored(world);
        root[ProcedureSnapshot.Member] = ProcedureSnapshotPatchKit.Seeded();
        world.Submission!["sprk_findingsjson"] = root.ToJsonString();
        return world;
    }

    private static JsonObject Stored(CorrectionWorld world) => JsonNode.Parse(world.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

    /// <summary>A line the reviewer has Accepted, as the first line of the day (index 0): five hours, no finding of its own unless the cap rule is in force.</summary>
    private static Guid AddDecidedLine(CorrectionWorld world)
    {
        var id = Guid.Parse("082d0000-0000-4000-8000-00000000f4b0");
        var decided = W.Copy(world.Line());
        decided.Id = id;
        decided["sprk_linesequence"] = 1;
        decided["sprk_quantity"] = 5m;
        decided["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        world.Lines.Add(decided);
        return id;
    }

    [Fact(DisplayName = "082d fix round F4 correction must-fire: after a correction the stored procedureSnapshot is the re-run's basis, not the one the submission was stored with")]
    public async Task ACorrection_WritesTheReRunsBasis()
    {
        var world = WorldWithSnapshot();
        ProcedureSnapshotPatchKit.AppliedOf(Stored(world)).Should().Equal(ProcedureSnapshotPatchKit.SeededVersion);

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        ProcedureSnapshotPatchKit.AppliedOf(Stored(world)).Should().Equal(ProcedureSnapshotPatchKit.AfterVersion);
    }

    [Fact(DisplayName = "082d fix round F4 correction: a decided line that keeps findings the re-run would change makes the member JSON null (no snapshot, fail closed)")]
    public async Task ACorrection_WithADecidedLineKeepingOldFindings_WritesNull()
    {
        var world = WorldWithSnapshot();
        AddDecidedLine(world);
        var root = Stored(world);
        root["findings"] = new JsonArray(new JsonObject
        {
            ["index"] = 0, ["ruleType"] = RuleType.DailyHoursCap.ToString(), ["policyId"] = Cap.PolicyId.ToString("D"), ["policyCode"] = Cap.PolicyCode,
            ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"), ["subject"] = "Line", ["line"] = 0, ["reasonCode"] = BillingReasonCodes.DailyHoursOverCap,
            ["consequence"] = "Flag",
        });
        world.Submission!["sprk_findingsjson"] = root.ToJsonString();

        // 30 hours on the day (over the cap, finding on the decided first line) become 7.5: the re-run drops that finding, the patch keeps it.
        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var stored = Stored(world);
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: readers treat it as no snapshot");
        ProcedureSnapshot.Read(stored).Should().BeNull();
        stored["findings"]!.AsArray().OfType<JsonObject>().Should().Contain(f => f["line"]!.GetValue<int>() == 0, "the decided line's old finding is kept (control)");
    }

    [Fact(DisplayName = "082d fix round F4 correction control: a decided line whose findings the re-run leaves alone does not stop the re-run's basis from being written")]
    public async Task ACorrection_WithAnUnaffectedDecidedLine_StillWritesTheReRunsBasis()
    {
        var world = WorldWithSnapshot();
        AddDecidedLine(world); // five hours: whole hours, no finding from the increment rule on either side

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        ProcedureSnapshotPatchKit.AppliedOf(Stored(world)).Should().Equal(ProcedureSnapshotPatchKit.AfterVersion);
    }

    [Fact(DisplayName = "082d fix round F4 correction must-not-fire: a correction refused into a Procedure gap writes nothing, so the stored member is byte-identical")]
    public async Task ARefusedCorrection_LeavesTheMemberByteIdentical()
    {
        var world = WorldWithSnapshot();
        var stored = world.Submission!.GetAttributeValue<string>("sprk_findingsjson");
        var member = Stored(world)[ProcedureSnapshot.Member]!.ToJsonString();
        var kit = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 9, 15));
        kit.Rule("Ended", "ENDED-CAP", RuleType.DailyHoursCap);
        var loaded = Kit.Loaded() with { Procedures = kit.Build() };
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            NullLogger<InvoiceCorrectionReevaluation>.Instance);
        world.Seam = reevaluation.ApplyAsync;

        var result = await world.Service().CorrectAsync(
            new CorrectionCommand(W.InvoiceId, W.ExpenseLineId, "Date", "2026-09-20", false, W.Reviewer), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.ProcedureGap);
        world.ValueWrites.Should().NotContain(w => w.Entity == "sprk_invoicesubmission" && w.Fields.ContainsKey("sprk_findingsjson"));
        world.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored);
        Stored(world)[ProcedureSnapshot.Member]!.ToJsonString().Should().Be(member);
    }
}
