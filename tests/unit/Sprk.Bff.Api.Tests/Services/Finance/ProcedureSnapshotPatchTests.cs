using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using ProcedureKit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// What the two patch paths (a correction, an added line) write into the <c>procedureSnapshot</c> member of the stored findings (ebilling-r1 082d
/// fix rounds 1 and 2, F4). A re-run evaluates "before" and "after" under ONE rule load, so both sides carry the same versions and differ only in
/// subjects. The re-run's snapshot (<c>Build(after)</c>) is written only when the stored member is what the "before" side builds now, is not null,
/// and no decided line differs; else JSON null; an absent member stays absent.
/// </summary>
internal static class ProcedureSnapshotPatchKit
{
    internal static readonly Guid SeededVersion = Guid.Parse("082d0000-0000-4000-8000-00000000f401");

    /// <summary>The rule version both sides of a re-run carry (one rule load).</summary>
    internal static readonly Guid RuleVersion = Guid.Parse("082d0000-0000-4000-8000-00000000f404");

    /// <summary>A rule version the stored snapshot was judged with that the current rule load no longer carries (a Procedure changed since submission).</summary>
    internal static readonly Guid VersionSinceChanged = Guid.Parse("082d0000-0000-4000-8000-00000000f405");

    /// <summary>A result that carries <paramref name="basis"/> (the rest of the evaluation is not read by the snapshot).</summary>
    internal static BillingEvaluationResult ResultOf(BillingProcedureBasis basis) =>
        Kit.Evaluator().Evaluate(Kit.Request(), Kit.Loaded()) with { ProcedureBasis = basis };

    /// <summary>The snapshot the persister (or an earlier patch) would store for <paramref name="basis"/>.</summary>
    internal static JsonObject SnapshotOf(BillingProcedureBasis basis) => ProcedureSnapshot.Build(ResultOf(basis))!;

    /// <summary><paramref name="basis"/> plus <paramref name="count"/> more applications (a snapshot big enough to push the findings over the column).</summary>
    internal static BillingProcedureBasis Padded(BillingProcedureBasis basis, int count) =>
        basis with
        {
            Applications = [.. basis.Applications, .. Enumerable.Range(0, count).Select(_ =>
                new BillingRuleApplication(Guid.NewGuid(), BillingRuleSource.Procedure, [BillingSubject.ForInvoice()], [], []))],
        };

    /// <summary>Enough padded applications that the findings JSON is over <see cref="InvoiceSubmissionPersister.FindingsJsonMaxLength"/> (about 115 characters each).</summary>
    internal const int OverTheColumn = 12_000;

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

    /// <summary>
    /// What the submission step would have evaluated for the world's stored invoice (header from the provenance, the two lines from their rows and
    /// extraction JSON), built independently of <c>RequestBasis</c> so a difference in how a rebuild reads the facts shows up. The one fact the world
    /// does not store is the submission-time timekeeper resolution: the roster is empty, so it is NotOnRoster here, and the rebuild reads None (no
    /// correction row has stored a reason).
    /// </summary>
    internal static BillingEvaluationRequest SubmissionTimeRequest()
    {
        Dictionary<string, decimal> LineConfidence() => new()
        {
            ["Hours"] = 0.55m,
            ["Rate"] = 0.88m,
            ["Amount"] = 0.93m,
            ["Narrative"] = 0.79m,
            [BillingFieldNames.TaskCode] = 0.77m,
            [BillingFieldNames.ActivityCode] = 0.77m,
            [BillingFieldNames.ExpenseCode] = 0.77m,
        };

        return new BillingEvaluationRequest
        {
            SubmissionId = W.SubmissionId,
            FirmId = W.Firm,
            CoreRecord = new CoreRecordReference(CoreRecordKind.Project, W.Core),
            TrackingNumber = "INV-1001.1",
            FirmInvoiceNumber = "INV-1001",
            ReceivedOn = new DateOnly(2026, 10, 1),
            BillingPeriodStart = new DateOnly(2026, 9, 1),
            BillingPeriodEnd = new DateOnly(2026, 9, 30),
            Currency = "USD",
            HeaderConfidence = new Dictionary<string, decimal>
            {
                ["FirmInvoiceNumber"] = 0.9m,
                ["BillingPeriodStart"] = 0.8m,
                ["BillingPeriodEnd"] = 0.8m,
                ["Currency"] = 0.99m,
                ["TotalDue"] = 0.65m,
            },
            Lines =
            [
                new BillingLineFacts
                {
                    Index = 2, LineType = InvoiceLineType.Fee, Date = new DateOnly(2026, 9, 12), TimekeeperName = "A. Lee",
                    Timekeeper = new TimekeeperResolution { Reason = TimekeeperUnmatchedReason.NotOnRoster },
                    ApprovedRate = new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.TimekeeperUnmatched },
                    Hours = 25m, BilledRate = 100m, Amount = 2500m, Narrative = W.Narrative, TaskCode = "L110", ActivityCode = "A101", BilledTaskCode = "L100",
                    FieldConfidence = LineConfidence(),
                },
                new BillingLineFacts
                {
                    Index = 3, LineType = InvoiceLineType.Expense, Amount = 40m, Narrative = "Court filing fee", TaskCode = "L110", ExpenseCode = "E101",
                    ApprovedRate = new RateResolution { Status = RateResolutionStatus.NotApplicable }, FieldConfidence = LineConfidence(),
                },
            ],
        };
    }

    internal static IReadOnlyList<Guid>? AppliedOf(JsonObject root) => ProcedureSnapshot.Read(root)?.AppliedPolicyVersionIds;

    /// <summary>True when the stored member is structurally the snapshot of <paramref name="basis"/>.</summary>
    internal static bool Is(JsonObject root, BillingProcedureBasis basis) => JsonNode.DeepEquals(root[ProcedureSnapshot.Member], SnapshotOf(basis));
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

    /// <summary>The re-run's two sides under ONE rule load: the same rule version on both, differing only in the subjects it is applied to (the invoice joins on the "after" side).</summary>
    private static BillingProcedureBasis BeforeBasis => ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.RuleVersion, false, FeeIndex);

    private static BillingProcedureBasis AfterBasis => ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.RuleVersion, true, FeeIndex);

    private static BillingEvaluationResult Basis(BillingEvaluationResult result, bool isBefore, BillingProcedureBasis? after = null) =>
        result with { ProcedureBasis = isBefore ? BeforeBasis : after ?? AfterBasis };

    private static CorrectionWorld WorldWithSnapshot() => WorldWith(ProcedureSnapshotPatchKit.Seeded());

    /// <summary>A stored submission whose <c>procedureSnapshot</c> is <paramref name="member"/>; a C# null is an ABSENT member (a row stored before 082d), and
    /// <see cref="JsonNull"/> is a stored JSON null.</summary>
    private static CorrectionWorld WorldWith(JsonNode? member)
    {
        var world = new CorrectionWorld();
        InvoiceCorrectionReevaluationTests.Store(world);
        var root = Stored(world);
        if (member is not null)
        {
            root[ProcedureSnapshot.Member] = ReferenceEquals(member, JsonNull) ? null : member;
        }

        world.Submission!["sprk_findingsjson"] = root.ToJsonString();
        return world;
    }

    private static readonly JsonNode JsonNull = JsonValue.Create(0)!;

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

    [Fact(DisplayName = "082d fix round 2 F4 correction must-fire: the stored snapshot equals what the re-run's before side builds, so the after-snapshot is written")]
    public async Task ACorrection_WhoseStoredSnapshotIsTheBeforeBasis_WritesTheAfterSnapshot()
    {
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));
        ProcedureSnapshotPatchKit.Is(Stored(world), BeforeBasis).Should().BeTrue("control: the stored member is Build(before)");

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        ProcedureSnapshotPatchKit.Is(Stored(world), AfterBasis).Should().BeTrue("the member is Build(after)");
        ProcedureSnapshotPatchKit.Is(Stored(world), BeforeBasis).Should().BeFalse("and it changed");
    }

    [Fact(DisplayName = "082d fix round 2 F4 correction must-null: a stored snapshot that differs from the before side (a Procedure changed since submission) is replaced by JSON null, never by the re-run's snapshot")]
    public async Task ACorrection_WhoseStoredSnapshotDiffersFromTheBeforeBasis_WritesNull()
    {
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(ProcedureSnapshotPatchKit.BasisOf(ProcedureSnapshotPatchKit.VersionSinceChanged, false, FeeIndex)));

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var stored = Stored(world);
        stored.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        stored[ProcedureSnapshot.Member].Should().BeNull("JSON null: the untouched stored entries keep the old basis, so no snapshot describes them");
        ProcedureSnapshot.Read(stored).Should().BeNull();
    }

    [Fact(DisplayName = "082d fix round 2 F4 correction: a stored JSON null stays null even when the before side would have matched (null is sticky)")]
    public async Task ACorrection_WithAStoredNull_KeepsNull()
    {
        var world = WorldWith(JsonNull);
        Stored(world).ContainsKey(ProcedureSnapshot.Member).Should().BeTrue("control");

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Stored(world).ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        Stored(world)[ProcedureSnapshot.Member].Should().BeNull();
    }

    [Fact(DisplayName = "082d fix round 2 F4 correction: an absent member (a row stored before 082d) stays absent; the key is not added")]
    public async Task ACorrection_WithAnAbsentMember_StaysAbsent()
    {
        var world = WorldWith(null);
        Stored(world).ContainsKey(ProcedureSnapshot.Member).Should().BeFalse("control");

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Stored(world).ContainsKey(ProcedureSnapshot.Member).Should().BeFalse();
    }

    [Fact(DisplayName = "082d fix round F4 correction: a decided line that keeps findings the re-run would change makes the member JSON null (no snapshot, fail closed)")]
    public async Task ACorrection_WithADecidedLineKeepingOldFindings_WritesNull()
    {
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis)); // (b) holds: only the decided line (c) can make the member null
        AddDecidedLine(world);
        var root = Stored(world);
        root["findings"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["ruleType"] = RuleType.DailyHoursCap.ToString(),
            ["policyId"] = Cap.PolicyId.ToString("D"),
            ["policyCode"] = Cap.PolicyCode,
            ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"),
            ["subject"] = "Line",
            ["line"] = 0,
            ["reasonCode"] = BillingReasonCodes.DailyHoursOverCap,
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
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));
        AddDecidedLine(world); // five hours: whole hours, no finding from the increment rule on either side

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        ProcedureSnapshotPatchKit.Is(Stored(world), AfterBasis).Should().BeTrue();
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

    // ── 082d fix round 2 (K): the size cap ──

    [Fact(DisplayName = "082d fix round 2 K correction: a snapshot that pushes the patched findings over the column is dropped (JSON null) and the write succeeds within the cap")]
    public async Task ACorrection_WithAnOverSizeSnapshot_DropsTheSnapshotAndWrites()
    {
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));
        var padded = ProcedureSnapshotPatchKit.Padded(AfterBasis, ProcedureSnapshotPatchKit.OverTheColumn);
        ProcedureSnapshot.Build(ProcedureSnapshotPatchKit.ResultOf(padded))!.ToJsonString().Length.Should().BeGreaterThan(InvoiceSubmissionPersister.FindingsJsonMaxLength, "control: the snapshot alone is over the column");

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore, padded), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var text = world.Submission!.GetAttributeValue<string>("sprk_findingsjson");
        text.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength);
        Stored(world).ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        Stored(world)[ProcedureSnapshot.Member].Should().BeNull();
        world.ValueWrites.Should().Contain(w => w.Entity == "sprk_invoicesubmission" && w.Fields.ContainsKey("sprk_findingsjson"), "control: the findings were patched");
    }

    [Fact(DisplayName = "082d fix round 2 K correction: findings still over the column without the snapshot are refused (409 findings_too_large) and nothing is written to them")]
    public async Task ACorrection_WithFindingsOverTheColumnAlone_IsRefused_AndWritesNothing()
    {
        var world = WorldWith(ProcedureSnapshotPatchKit.SnapshotOf(BeforeBasis));
        var root = Stored(world);
        root["padding"] = new string('p', InvoiceSubmissionPersister.FindingsJsonMaxLength);
        world.Submission!["sprk_findingsjson"] = root.ToJsonString();
        var stored = world.Submission!.GetAttributeValue<string>("sprk_findingsjson");

        var result = await Service(world, (r, isBefore) => Basis(r, isBefore), Cap, WholeHours).CorrectAsync(Hours("2.5"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.TooLarge);
        result.Code.Should().Be(CorrectionCodes.FindingsTooLarge).And.Be("ebilling.correction.findings_too_large");
        world.ValueWrites.Should().NotContain(w => w.Entity == "sprk_invoicesubmission" && w.Fields.ContainsKey("sprk_findingsjson"));
        world.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored);
    }

    // ── 082d follow-up K1: the round trip with the REAL evaluator on both sides ──

    [Fact(DisplayName = "082d follow-up K1 correction round trip: a snapshot the REAL evaluator built at submission survives a normal correction of an undecided line (preserved, equals Build(after), not null)")]
    public async Task ACorrection_OfAnUndecidedLine_WithARealSubmissionSnapshot_PreservesTheSnapshot()
    {
        var procedures = new ProcedureKit().Procedure("Round trip", assigned: true);
        procedures.Rule("Round trip", "RT-CAP", RuleType.DailyHoursCap);
        procedures.Rule("Round trip", "RT-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");
        var loaded = procedures.Loaded();
        var evaluator = Kit.Evaluator();

        // The stored snapshot: the real evaluator over the submission-time facts, the way the persister stores it.
        var submissionResult = evaluator.Evaluate(ProcedureSnapshotPatchKit.SubmissionTimeRequest(), loaded);
        var snapshot = ProcedureSnapshot.Build(submissionResult);
        snapshot.Should().NotBeNull();
        snapshot!["applications"]!.AsArray().Should().NotBeEmpty("control: the Procedure's rules applied, so the snapshot is not the trivial empty one");
        var world = WorldWith(snapshot);

        // The correction re-runs the REAL evaluator over the rebuilt facts, before and after (recorded, not shaped).
        var results = new List<BillingEvaluationResult>();
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), (request, facts) => { var r = evaluator.Evaluate(request, facts); results.Add(r); return r; },
            world.WriteSignalAsync, world.CloseSignalAsync, NullLogger<InvoiceCorrectionReevaluation>.Instance);
        world.Seam = reevaluation.ApplyAsync;

        var result = await world.Service().CorrectAsync(Hours("2"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        results.Should().HaveCount(2, "control: one before and one after evaluation");
        JsonNode.DeepEquals(ProcedureSnapshot.Build(results[0]), snapshot).Should().BeTrue("control: the rebuilt before side reads the same Procedure basis the submission stored");
        var stored = Stored(world);
        stored[ProcedureSnapshot.Member].Should().NotBeNull("normal use keeps the snapshot");
        JsonNode.DeepEquals(stored[ProcedureSnapshot.Member], ProcedureSnapshot.Build(results[1])).Should().BeTrue("the member is the real Build(after)");
        ProcedureSnapshot.Read(stored).Should().NotBeNull();
    }
}
