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

/// <summary>ebilling-r1 082d fix rounds 1 and 2: what Add line writes into the <c>procedureSnapshot</c> member of the stored findings (see <see cref="ProcedureSnapshotPatchKit"/>).</summary>
public sealed partial class InvoiceLineAddServiceTests
{
    private static void SeedSnapshot(AddLineWorld add) =>
        StoreRoot(add, root => root[ProcedureSnapshot.Member] = ProcedureSnapshotPatchKit.Seeded());

    private static void StoreMember(AddLineWorld add, JsonNode? member, bool absent = false) =>
        StoreRoot(add, root =>
        {
            if (absent)
            {
                root.Remove(ProcedureSnapshot.Member);
            }
            else
            {
                root[ProcedureSnapshot.Member] = member;
            }
        });

    /// <summary>The re-run's two sides under ONE rule load: the same rule version on both; the stored two lines on the "before" side, the added line too on the "after" side.</summary>
    private static BillingProcedureBasis BeforeBasis => ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.RuleVersion, false, 2);

    private static BillingProcedureBasis AfterBasis => ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.RuleVersion, false, 2, 3);

    private static AddLineWorld ReRun(BillingProcedureBasis? after = null) => new() { Shape = BasisPerSide(BeforeBasis, after ?? AfterBasis) };

    /// <summary>The "before" side has the stored two lines; the "after" side has the added line too.</summary>
    private static Func<BillingEvaluationRequest, BillingEvaluationResult, BillingEvaluationResult> BasisPerSide(
        BillingProcedureBasis before, BillingProcedureBasis after) =>
        (request, result) => result with { ProcedureBasis = request.Lines.Count > 2 ? after : before };

    [Fact(DisplayName = "082d fix round 2 F4 add line must-fire: the stored snapshot equals what the re-run's before side builds, so the after-snapshot is written")]
    public async Task AddLine_WhoseStoredSnapshotIsTheBeforeBasis_WritesTheAfterSnapshot()
    {
        var add = ReRun();
        StoreMember(add, ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));
        ProcedureSnapshotPatchKit.Is(add.Findings(), BeforeBasis).Should().BeTrue("control: the stored member is Build(before)");

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        ProcedureSnapshotPatchKit.Is(add.Findings(), AfterBasis).Should().BeTrue("the member is Build(after)");
        ProcedureSnapshotPatchKit.Is(add.Findings(), BeforeBasis).Should().BeFalse("and it changed");
    }

    [Fact(DisplayName = "082d fix round 2 F4 add line must-null: a stored snapshot that differs from the before side (a Procedure changed since submission) is replaced by JSON null, never by the re-run's snapshot")]
    public async Task AddLine_WhoseStoredSnapshotDiffersFromTheBeforeBasis_WritesNull()
    {
        var add = ReRun();
        StoreMember(add, ProcedureSnapshotPatchKit.SnapshotOf(ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.VersionSinceChanged, false, 2)));

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        var stored = add.Findings();
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: the untouched stored entries keep the old basis, so no snapshot describes them");
        ProcedureSnapshot.Read(stored).Should().BeNull();
    }

    [Fact(DisplayName = "082d fix round 2 F4 add line: a stored JSON null stays null even when the before side would have matched (null is sticky)")]
    public async Task AddLine_WithAStoredNull_KeepsNull()
    {
        var add = ReRun();
        StoreMember(add, null);
        add.Findings().ContainsKey(ProcedureSnapshot.Member).Should().BeTrue("control: a stored null is a present key");

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.Findings().ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        add.Findings()[ProcedureSnapshot.Member].Should().BeNull();
    }

    [Fact(DisplayName = "082d fix round 2 F4 add line: an absent member (a row stored before 082d) stays absent; the key is not added")]
    public async Task AddLine_WithAnAbsentMember_StaysAbsent()
    {
        var add = ReRun();
        StoreMember(add, null, absent: true);
        add.Findings().ContainsKey(ProcedureSnapshot.Member).Should().BeFalse("control");

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.Findings().ContainsKey(ProcedureSnapshot.Member).Should().BeFalse();
    }

    [Fact(DisplayName = "082d fix round F4 add line: a decided line whose applied rules the re-run changes makes the member JSON null (no snapshot, fail closed)")]
    public async Task AddLine_WithADecidedLineTheReRunChanges_WritesNull()
    {
        var add = new AddLineWorld
        {
            // The decided fee line (index 2) is judged by the version before the add and by no line after it: the stored per-line entries of a
            // decided line are not patched, so the whole-invoice basis of the re-run would not describe them. (b) holds: only (c) makes the null.
            Shape = BasisPerSide(BeforeBasis, ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.RuleVersion, true)),
        };
        add.World.Line(W.FeeLineId)["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        StoreMember(add, ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));

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
        var add = ReRun();
        add.World.Line(W.FeeLineId)["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        StoreMember(add, ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        ProcedureSnapshotPatchKit.Is(add.Findings(), AfterBasis).Should().BeTrue();
        add.Findings()[ProcedureSnapshot.Member]!["applications"]![0]!["applied"]!["lines"]!.AsArray().Select(l => l!.GetValue<int>()).Should().Equal([2, 3], "the re-run applies the version to the added line too: the member is the re-run's, not null");
    }

    // ── 082d fix round 2 (K): the size cap ──

    [Fact(DisplayName = "082d fix round 2 K add line: a snapshot that pushes the patched findings over the column is dropped (JSON null) and the line is added within the cap")]
    public async Task AddLine_WithAnOverSizeSnapshot_DropsTheSnapshotAndWrites()
    {
        var add = ReRun(ProcedureSnapshotPatchKit.Padded(AfterBasis, ProcedureSnapshotPatchKit.OverTheColumn));
        StoreMember(add, ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson").Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength);
        add.Findings().ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        add.Findings()[ProcedureSnapshot.Member].Should().BeNull();
        add.Findings()[InvoiceLineAddService.AddLineMember].Should().NotBeNull("control: the add itself committed");
    }

    [Fact(DisplayName = "082d fix round 2 K add line: findings still over the column without the snapshot are refused (409 too_large) and nothing is written")]
    public async Task AddLine_WithFindingsOverTheColumnAlone_IsRefused_AndWritesNothing()
    {
        var add = ReRun();
        StoreRoot(add, root =>
        {
            root[ProcedureSnapshot.Member] = ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis);
            root["padding"] = new string('p', InvoiceSubmissionPersister.FindingsJsonMaxLength);
        });
        var stored = add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson");

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.TooLarge);
        result.Code.Should().Be(AddLineCodes.TooLarge).And.Be("ebilling.line_add.too_large");
        result.Detail.Should().Contain("Nothing was changed.").And.EndWith("Contact support.", "082d follow-up K3: the refusal names a next step, as the correction's findings_too_large does");
        add.Transactions.Should().BeEmpty();
        add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored);
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

    [Fact(DisplayName = "082d follow-up K1 add line round trip: a snapshot the REAL evaluator built at submission survives a normal add (preserved, equals Build(after), not null)")]
    public async Task AddLine_WithARealSubmissionSnapshot_PreservesTheSnapshot()
    {
        var procedures = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit().Procedure("Round trip", assigned: true);
        procedures.Rule("Round trip", "RT-CAP", RuleType.DailyHoursCap);
        procedures.Rule("Round trip", "RT-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");
        var loaded = procedures.Loaded();
        var evaluator = Kit.Evaluator();
        var snapshot = ProcedureSnapshot.Build(evaluator.Evaluate(ProcedureSnapshotPatchKit.SubmissionTimeRequest(), loaded));
        snapshot.Should().NotBeNull();
        snapshot!["applications"]!.AsArray().Should().NotBeEmpty("control: the Procedure's rules applied, so the snapshot is not the trivial empty one");

        // The real evaluator on both sides of the re-run (no Shape); the results are recorded to build the expectation.
        var add = new AddLineWorld();
        StoreMember(add, snapshot);
        var results = new List<BillingEvaluationResult>();
        add.Shape = (_, result) => { results.Add(result); return result; };

        var result = await add.Service(loaded).AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        results.Should().HaveCount(2, "control: one before and one after evaluation");
        JsonNode.DeepEquals(ProcedureSnapshot.Build(results[0]), snapshot).Should().BeTrue("control: the rebuilt before side reads the same Procedure basis the submission stored");
        var stored = add.Findings();
        stored[ProcedureSnapshot.Member].Should().NotBeNull("normal use keeps the snapshot");
        JsonNode.DeepEquals(stored[ProcedureSnapshot.Member], ProcedureSnapshot.Build(results[1])).Should().BeTrue("the member is the real Build(after)");
    }
}
