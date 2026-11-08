using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// The basis every 082d test stores: an Assigned-records-only Procedure followed on a direct PARENT with a reason; an All-records Procedure that
/// a parent row excludes with a reason; a rule of the first excluded on the parent with a reason; and a legacy client-default TimeIncrement that
/// the Procedure's TimeIncrement (Manual d1) displaces. The outcome is the real processor's over the kit's rows; the JSON is the persister's.
/// </summary>
internal static class ProcedureSnapshotFixture
{
    internal const string ProcedureName = "SENTINELPROC Guidelines";
    internal const string ExcludedName = "SENTINELEXCL Terms";
    internal const string FollowReason = "SENTINEL-REASON-FOLLOW agreed on the parent";
    internal const string ExcludeProcedureReason = "SENTINEL-REASON-XPROC typed on the parent";
    internal const string ExcludeRuleReason = "SENTINEL-REASON-XRULE typed on the parent";

    internal static readonly Guid InvoiceId = Guid.Parse("082d0000-0000-4000-8000-0000000000a1");

    internal static ProcedureKit Kit()
    {
        var kit = new ProcedureKit()
            .Procedure(ProcedureName, ProcedureAppliesTo.AssignedRecordsOnly)
            .Procedure(ExcludedName)
            .Override(ProcedureOverrideMode.FollowLatest, "follow", procedure: ProcedureName, reason: FollowReason)
            .Override(ProcedureOverrideMode.Excluded, "exclude-procedure", procedure: ExcludedName, reason: ExcludeProcedureReason)
            .Override(ProcedureOverrideMode.Excluded, "exclude-rule", ruleCode: "PROC-LIMIT", reason: ExcludeRuleReason);
        // A one-hour increment: the default 1.5-hour fee line is off it, so the Procedure's rule raises a reader-facing finding.
        kit.Rule(ProcedureName, "PROC-INC", RuleType.TimeIncrement, body: """{"type":"TimeIncrement","incrementHours":1.0}""", clauseLocation: "page 2");
        kit.Rule(ProcedureName, "PROC-LIMIT", RuleType.DailyHoursCap, clauseLocation: "page 3");
        kit.Rule(ExcludedName, "EXCL-INC", RuleType.TimeIncrement, clauseLocation: "page 4");
        return kit;
    }

    internal static BillingRuleCandidate Legacy() => BillingEvaluationKit.Version("CD-INC", RuleType.TimeIncrement);

    internal static Guid RowId(string label) => BillingEvaluationKit.Id("procedure-kit:override:" + label);

    internal static Guid PolicyVersionId(string code) => BillingEvaluationKit.Id("procedure-kit:policy-version:" + code);

    /// <summary>Every value the internal part holds and no reader-facing member may: record, row, Procedure and Procedure Version ids (each form),
    /// the override reasons and the Procedure names.</summary>
    internal static IReadOnlyList<string> Secrets()
    {
        Guid[] ids =
        [
            ProcedureKit.Matter.Id, ProcedureKit.Parent.Id, RowId("follow"), RowId("exclude-procedure"), RowId("exclude-rule"),
            ProcedureKit.ProcedureId(ProcedureName), ProcedureKit.ProcedureId(ExcludedName),
            ProcedureKit.ProcedureVersionId(ProcedureName), ProcedureKit.ProcedureVersionId(ExcludedName),
        ];
        return [.. ids.SelectMany(id => new[] { id.ToString("D"), id.ToString("N"), id.ToString("B") }),
            FollowReason, ExcludeProcedureReason, ExcludeRuleReason, "SENTINEL-REASON", ProcedureName, ExcludedName, "SENTINELPROC", "SENTINELEXCL"];
    }

    internal static async Task<(SubmissionEvaluationOutcome Outcome, ProcedureResolution Resolution)> OutcomeAsync(params BillingRuleCandidate[] extra)
    {
        var resolution = Kit().Build();
        var harness = new ProcessorHarness { Procedures = resolution, Rules = [Legacy(), .. extra] };
        if (extra.Length > 0)
        {
            // An aged line (20 January), so the InvoiceAge rule rejects: an automatic rejection with a deciding finding.
            harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        }

        var outcome = await harness.RunAsync();
        return (outcome, resolution);
    }

    internal static async Task<string> FindingsJsonAsync() =>
        InvoiceSubmissionPersister.BuildFindingsJson((await OutcomeAsync()).Outcome, InvoiceId);
}

/// <summary>
/// ebilling-r1 082d (model 7.4; uac-r2's binding snapshot design; the U3 tension of notes/082-procedure-resolver.md): the submission's
/// <c>procedureSnapshot</c> member, its reader compatibility, the Decision Record summary and the K6 printing of the resolution records.
/// </summary>
public sealed class ProcedureSnapshotTests
{
    private static JsonObject SnapshotOf(string json) => JsonNode.Parse(json)!.AsObject()[ProcedureSnapshot.Member]!.AsObject();

    // ── The 7.4 members ──

    [Fact(DisplayName = "082d 7.4 must-fire: the snapshot stores, as ids, the chain, every deciding override (mode, record, distance, reason), the Procedures with their Procedure Version, level and sources, the rules with their Procedure Version, the exclusions and the precedence outcome")]
    public async Task Snapshot_StoresThe74Members()
    {
        var snapshot = SnapshotOf(await ProcedureSnapshotFixture.FindingsJsonAsync());

        snapshot["version"]!.GetValue<int>().Should().Be(ProcedureSnapshot.Version);
        snapshot["chain"]!.AsArray().Select(c => (c!["id"]!.GetValue<string>(), c["distance"]!.GetValue<int>()))
            .Should().Equal((ProcedureKit.Matter.Id.ToString("D"), 0), (ProcedureKit.Parent.Id.ToString("D"), 1));

        var overrides = snapshot["overrides"]!.AsArray().OfType<JsonObject>().ToDictionary(o => o["rowId"]!.GetValue<string>());
        overrides.Keys.Should().BeEquivalentTo(new[] { "follow", "exclude-procedure", "exclude-rule" }.Select(l => ProcedureSnapshotFixture.RowId(l).ToString("D")));
        var follow = overrides[ProcedureSnapshotFixture.RowId("follow").ToString("D")];
        follow["mode"]!.GetValue<string>().Should().Be(nameof(ProcedureOverrideMode.FollowLatest));
        follow["record"]!["id"]!.GetValue<string>().Should().Be(ProcedureKit.Parent.Id.ToString("D"));
        follow["record"]!["distance"]!.GetValue<int>().Should().Be(1);
        follow["reason"]!.GetValue<string>().Should().Be(ProcedureSnapshotFixture.FollowReason);
        overrides[ProcedureSnapshotFixture.RowId("exclude-rule").ToString("D")]["policyId"]!.GetValue<string>()
            .Should().Be(BillingEvaluationKit.Id("procedure-kit:policy:PROC-LIMIT").ToString("D"));

        var resolution = snapshot["resolutions"]!.AsArray().Should().ContainSingle("one date, one outcome").Subject!.AsObject();
        resolution["dates"]!.AsArray().Select(d => d!.GetValue<string>()).Should().Equal("2026-05-20", "2026-06-01");
        var procedure = resolution["procedures"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        procedure["procedureId"]!.GetValue<string>().Should().Be(ProcedureKit.ProcedureId(ProcedureSnapshotFixture.ProcedureName).ToString("D"));
        procedure["procedureVersionId"]!.GetValue<string>().Should().Be(ProcedureKit.ProcedureVersionId(ProcedureSnapshotFixture.ProcedureName).ToString("D"));
        procedure["level"]!.GetValue<string>().Should().Be(nameof(ProcedureLevel.ManualParent));
        procedure["distance"]!.GetValue<int>().Should().Be(1);
        procedure["sources"]!.GetValue<string>().Should().Be(nameof(ProcedureSources.Manual));
        procedure["overrides"]!.AsArray().Select(o => o!.GetValue<string>()).Should().Equal(ProcedureSnapshotFixture.RowId("follow").ToString("D"));

        resolution["rules"]!.AsArray().Select(r => r!["policyVersionId"]!.GetValue<string>())
            .Should().Equal(ProcedureSnapshotFixture.PolicyVersionId("PROC-INC").ToString("D"));
        resolution["rules"]![0]!["procedureVersionId"]!.GetValue<string>()
            .Should().Be(ProcedureKit.ProcedureVersionId(ProcedureSnapshotFixture.ProcedureName).ToString("D"));
        var excludedRule = resolution["excludedRules"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        excludedRule["policyVersionId"]!.GetValue<string>().Should().Be(ProcedureSnapshotFixture.PolicyVersionId("PROC-LIMIT").ToString("D"));
        excludedRule["distance"]!.GetValue<int>().Should().Be(1);
        excludedRule["overrides"]!.AsArray().Select(o => o!.GetValue<string>()).Should().Equal(ProcedureSnapshotFixture.RowId("exclude-rule").ToString("D"));
        resolution["excludedProcedures"]!.AsArray().Should().ContainSingle().Which!["procedureId"]!.GetValue<string>()
            .Should().Be(ProcedureKit.ProcedureId(ProcedureSnapshotFixture.ExcludedName).ToString("D"));
        resolution["gaps"]!.AsArray().Should().BeEmpty();

        var applications = snapshot["applications"]!.AsArray().OfType<JsonObject>().ToDictionary(a => a["policyVersionId"]!.GetValue<string>());
        var applied = applications[ProcedureSnapshotFixture.PolicyVersionId("PROC-INC").ToString("D")];
        applied["source"]!.GetValue<string>().Should().Be(nameof(BillingRuleSource.Procedure));
        applied["applied"]!["invoice"]!.GetValue<bool>().Should().BeTrue();
        applied["applied"]!["lines"]!.AsArray().Select(l => l!.GetValue<int>()).Should().Equal(0);
        var displaced = applications[ProcedureSnapshotFixture.Legacy().PolicyVersionId.ToString("D")];
        displaced["source"]!.GetValue<string>().Should().Be(nameof(BillingRuleSource.Legacy));
        displaced["applied"]!["lines"]!.AsArray().Should().BeEmpty("the Manual d1 TimeIncrement displaced the client default (Replace)");
        displaced["displaced"]!["lines"]!.AsArray().Select(l => l!.GetValue<int>()).Should().Equal(0);
        snapshot.ToJsonString().Should().NotContain(ProcedureSnapshotFixture.ProcedureName, "names are resolved at read time, never frozen");
    }

    [Fact(DisplayName = "082d: the snapshot is the evaluator's own resolution, stored, never re-resolved (building it calls RulesOn on no date)")]
    public async Task Snapshot_IsStored_NotRecomputed()
    {
        var (outcome, resolution) = await ProcedureSnapshotFixture.OutcomeAsync();
        var resolvedByTheEvaluation = resolution.DatesResolved;

        InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId);

        resolvedByTheEvaluation.Should().Be(2, "control: the invoice date and the line date");
        resolution.DatesResolved.Should().Be(resolvedByTheEvaluation);
    }

    [Fact(DisplayName = "082d: with no evaluation (a held resolver fault) the member is written as null, so a re-run replaces an older snapshot; null reads as no snapshot")]
    public async Task NoEvaluation_WritesNull()
    {
        var outcome = await new ProcessorHarness { EvaluationFault = new ProcedureResolutionFaultException("x"), FinalDelivery = true }.RunAsync();

        var root = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId))!.AsObject();

        root.ContainsKey(ProcedureSnapshot.Member).Should().BeTrue();
        root[ProcedureSnapshot.Member].Should().BeNull();
        ProcedureSnapshot.Read(root).Should().BeNull();
    }

    // ── The U3 tension (notes ~:247), resolved: the basis is internal ──

    [Fact(DisplayName = "082d (U3 tension): no reader-facing member of the findings JSON carries a chain record, a parent record id, an override reason, a row id, a Procedure or Procedure Version id or a Procedure name; only the internal member does")]
    public async Task ReaderFacingMembers_CarryNoBasis()
    {
        var root = JsonNode.Parse(await ProcedureSnapshotFixture.FindingsJsonAsync())!.AsObject();
        var internalPart = root[ProcedureSnapshot.Member]!.ToJsonString();
        root.Remove(ProcedureSnapshot.Member);
        var readerFacing = root.ToJsonString();

        root["findings"]!.AsArray().Should().NotBeEmpty("control: the reader-facing members are there");
        internalPart.Should().Contain(ProcedureKit.Parent.Id.ToString("D"), "control: the internal part names the parent").And.Contain(ProcedureSnapshotFixture.FollowReason);
        foreach (var secret in ProcedureSnapshotFixture.Secrets())
        {
            readerFacing.Should().NotContainEquivalentOf(secret);
        }

        ReaderSentinel.Scan(InvoiceSubmissionProcedureHoldTestsAccess.PipelineAndLineFindings(readerFacing)).Should().BeEmpty();
    }

    // ── Reading it back; older rows ──

    [Fact(DisplayName = "082d: Read lists the versions that judged the invoice as applied; a displaced and an excluded version are not applied")]
    public async Task Read_AppliedDisplacedExcluded()
    {
        var stored = ProcedureSnapshot.Read(await ProcedureSnapshotFixture.FindingsJsonAsync());

        stored.Should().NotBeNull();
        stored!.AppliedPolicyVersionIds.Should().Equal(ProcedureSnapshotFixture.PolicyVersionId("PROC-INC"));
        stored.DisplacedPolicyVersionIds.Should().Equal(ProcedureSnapshotFixture.Legacy().PolicyVersionId);
        stored.AppliedPolicyVersionIds.Should().NotContain(ProcedureSnapshotFixture.PolicyVersionId("PROC-LIMIT"), "excluded on the parent");
        (stored.ProcedureCount, stored.GapCount).Should().Be((1, 0));
    }

    public static TheoryData<string, Action<JsonObject>> OlderOrDamaged => new()
    {
        { "pre-v5 row: no member", r => r.Remove(ProcedureSnapshot.Member) },
        { "unknown version", r => r[ProcedureSnapshot.Member]!["version"] = 2 },
        { "member is not an object", r => r[ProcedureSnapshot.Member] = "x" },
        { "an application without an id", r => r[ProcedureSnapshot.Member]!["applications"]!.AsArray().Add(new JsonObject()) },
        { "applications missing", r => r[ProcedureSnapshot.Member]!.AsObject().Remove("applications") },
    };

    [Theory(DisplayName = "082d reader compatibility: a pre-v5 row, an unknown version or a damaged member is NO snapshot (fail closed), and the stored findings still read")]
    [MemberData(nameof(OlderOrDamaged))]
    public async Task OlderOrDamagedSnapshot_IsNoSnapshot_AndTheFindingsStillRead(string _, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(await ProcedureSnapshotFixture.FindingsJsonAsync())!.AsObject();
        ProcedureSnapshot.Read(root).Should().NotBeNull("control");
        change(root);
        var json = root.ToJsonString();

        ProcedureSnapshot.Read(json).Should().BeNull();
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(json);
        stored.Should().NotBeNull("the other readers of the findings are not affected");
        stored!.ProcedureSnapshot.Should().BeNull();
    }

    // ── The bounded Decision Record summary ──

    [Fact(DisplayName = "082d (7.4, ontology O2): the automatic rejection's Decision Record carries a bounded summary and the pointer to the submission, never the basis")]
    public async Task DecisionRecord_CarriesTheBoundedSummary()
    {
        var (outcome, _) = await ProcedureSnapshotFixture.OutcomeAsync(InvoiceSubmissionProcessorTests.InvoiceAgeReject());
        var json = InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId);
        var submissionId = Guid.NewGuid();

        var decision = InvoiceSubmissionAfterSteps.BuildRejectionDecision(InvoiceSubmissionAfterSteps.StoredFindings.Parse(json)!, submissionId, ProcedureSnapshotFixture.InvoiceId);

        decision.Should().NotBeNull("control: the fixture is an automatic rejection");
        var summary = decision!.Facts[ProcedureSnapshot.DecisionRecordFact].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        summary.Keys.Should().BeEquivalentTo(
            ["version", "submissionId", "member", "procedures", "appliedRules", "displacedRules", "gaps"]);
        summary["submissionId"].Should().Be(submissionId.ToString("D"));
        summary["member"].Should().Be(ProcedureSnapshot.Member);
        summary.Where(s => s.Value is int).ToDictionary(s => s.Key, s => (int)s.Value!).Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["version"] = 1, ["procedures"] = 1, ["appliedRules"] = 2, ["displacedRules"] = 1, ["gaps"] = 0,
        }, "PROC-INC and BILL-AGE applied, CD-INC displaced");
        var text = JsonSerializer.Serialize(decision.Facts);
        text.Length.Should().BeLessThan(100_000);
        JsonSerializer.Serialize(summary).Length.Should().BeLessThan(400, "bounded: a fixed set of counts");
        foreach (var secret in ProcedureSnapshotFixture.Secrets())
        {
            text.Should().NotContainEquivalentOf(secret);
        }
    }

    [Fact(DisplayName = "082d fix round K1 must-fire: the Decision Record summary carries no count of Exclude rows (a reader who cannot read a Secure parent learns nothing of the chain from it)")]
    public async Task DecisionRecordSummary_CarriesNoExcludeCount()
    {
        var (outcome, _) = await ProcedureSnapshotFixture.OutcomeAsync(InvoiceSubmissionProcessorTests.InvoiceAgeReject());
        var json = InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId);
        var stored = ProcedureSnapshot.Read(json);
        stored.Should().NotBeNull("control: the fixture stores two Exclude rows (a Procedure and a rule) on the parent");
        JsonNode.Parse(json)![ProcedureSnapshot.Member]!["resolutions"]!.AsArray().Select(r => r!["excludedRules"]!.AsArray().Count + r["excludedProcedures"]!.AsArray().Count)
            .Sum().Should().BeGreaterThan(0, "control: the internal member does hold the exclusions");

        var summary = ProcedureSnapshot.DecisionRecordSummary(stored!, Guid.NewGuid());

        summary.Keys.Should().NotContain(k => k.Contains("exclu", StringComparison.OrdinalIgnoreCase));
        JsonSerializer.Serialize(summary).Should().NotContainEquivalentOf("exclu").And.NotContainEquivalentOf("override");
    }

    [Fact(DisplayName = "082d: a row stored before the snapshot rebuilds the SAME Decision Record facts (no summary member), so a resumed record keeps its hash")]
    public async Task DecisionRecord_PreV5Row_HasNoSummary()
    {
        var (outcome, _) = await ProcedureSnapshotFixture.OutcomeAsync(InvoiceSubmissionProcessorTests.InvoiceAgeReject());
        var root = JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId))!.AsObject();
        var withSnapshot = InvoiceSubmissionAfterSteps.BuildRejectionDecision(
            InvoiceSubmissionAfterSteps.StoredFindings.Parse(root.ToJsonString())!, Guid.Empty, ProcedureSnapshotFixture.InvoiceId)!;
        root.Remove(ProcedureSnapshot.Member);

        var preV5 = InvoiceSubmissionAfterSteps.BuildRejectionDecision(
            InvoiceSubmissionAfterSteps.StoredFindings.Parse(root.ToJsonString())!, Guid.Empty, ProcedureSnapshotFixture.InvoiceId)!;

        preV5.Facts.Should().NotContainKey(ProcedureSnapshot.DecisionRecordFact);
        preV5.Facts.Keys.Should().BeEquivalentTo(withSnapshot.Facts.Keys.Where(k => k != ProcedureSnapshot.DecisionRecordFact));
    }

    // ── K6: ToString of the resolution's records ──

    [Fact(DisplayName = "082d K6: ToString of the resolution, its Chain, a date's resolution, the basis, an application and the stored snapshot prints no id, reason or record name")]
    public async Task ToString_OfTheResolutionAndTheBasis_PrintsNoId()
    {
        var (outcome, resolution) = await ProcedureSnapshotFixture.OutcomeAsync();
        var basis = outcome.Evaluation!.ProcedureBasis!;
        var stored = ProcedureSnapshot.Read(InvoiceSubmissionPersister.BuildFindingsJson(outcome, ProcedureSnapshotFixture.InvoiceId))!;
        var texts = new Dictionary<string, string>
        {
            ["resolution"] = resolution.ToString()!,
            ["chain"] = resolution.Chain.ToString()!,
            ["chainJoined"] = string.Join(", ", resolution.Chain),
            ["chainInterpolated"] = $"{resolution.Chain[1]}",
            ["onDate"] = basis.Dates[0].ToString(),
            ["basis"] = basis.ToString(),
            ["application"] = string.Join(", ", basis.Applications),
            ["stored"] = stored.ToString(),
        };

        texts["chainJoined"].Should().Contain("Distance = 1", "control: the link prints its table and distance");
        foreach (var (name, text) in texts)
        {
            text.Should().NotBeNullOrEmpty(name);
            foreach (var secret in ProcedureSnapshotFixture.Secrets())
            {
                text.Should().NotContainEquivalentOf(secret, name);
            }

            ReaderSentinel.Scan(text).Should().BeEmpty(name);
        }
    }
}

/// <summary>The pipeline and line findings of a stored findings JSON, as text (the members the sentinel scan reads).</summary>
internal static class InvoiceSubmissionProcedureHoldTestsAccess
{
    internal static string PipelineAndLineFindings(string findingsJson)
    {
        var root = JsonNode.Parse(findingsJson)!.AsObject();
        return root["pipelineFindings"]!.ToJsonString() + "\n" + root[InvoiceSubmissionPersister.LineFindingsMember]!.ToJsonString();
    }
}
