using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals.Billing;

/// <summary>
/// ebilling-r1 082c-1 (brief C1-C5, C9, C10): the loader reads the legacy rule sets and the Procedure resolution, once each, over the same
/// in-memory Dataverse. A Procedure's rule set is written with a legacy scope (Client default or Firm, as <c>RuleSetService</c> writes it), so
/// the legacy read's partition (<c>sprk_procedure</c> null) is what keeps it out of the legacy stream.
/// </summary>
public partial class BillingFactLoaderTests
{
    private const string HalfHour = """{"type":"TimeIncrement","incrementHours":0.5}""";

    /// <summary>
    /// A Procedure with one Active version from <see cref="RulesFrom"/> and one rule of <paramref name="type"/> published under it. Its rule set
    /// carries <paramref name="setScope"/> and is Published, as a legacy set would be: only <c>sprk_procedure</c> tells them apart.
    /// </summary>
    private (Guid Procedure, Guid Set, Guid Policy, Guid Version) ProcedureRule(
        string code, RuleType type, string body, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, RuleSetScope setScope = RuleSetScope.ClientDefault)
    {
        var procedure = Guid.NewGuid();
        _world.Add(ProcedureEntity.Entity, procedure,
            (ProcedureEntity.AreaColumn, new OptionSetValue(Billing)), (ProcedureEntity.StatusColumn, new OptionSetValue(ProcedureEntity.StatusActive)),
            (ProcedureEntity.NameColumn, "Procedure " + code), (ProcedureEntity.AppliesToColumn, new OptionSetValue((int)appliesTo)));
        var procedureVersion = Guid.NewGuid();
        _world.Add(ProcedureEntity.VersionEntity, procedureVersion,
            (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)), (ProcedureEntity.VersionNumberColumn, 1),
            (ProcedureEntity.VersionStatusColumn, new OptionSetValue(ProcedureEntity.VersionStatusActive)),
            (ProcedureEntity.VersionEffectiveFromColumn, new DateTime(2026, 1, 1)));

        var set = RuleSet(setScope, setScope == RuleSetScope.Firm ? FirmId : null);
        _world.Set("sprk_ruleset", set, "sprk_procedure", Ref(ProcedureEntity.Entity, procedure));
        _world.Set("sprk_ruleset", set, "statecode", new OptionSetValue(0));
        var (policy, version) = Policy(set, code, type, body);
        _world.Set("sprk_policyversion", version, RuleSetService.PublishStateColumn, new OptionSetValue(RuleSetService.PublishStatePublished));
        _world.Set("sprk_policyversion", version, RuleSetService.ProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, procedureVersion));
        return (procedure, set, policy, version);
    }

    /// <summary>One fee line of 0.3 hours: off a 0.5 increment.</summary>
    private static BillingEvaluationRequest OffIncrement(Guid? coreMatter, int lines = 1) =>
        Request(coreMatter, lines) with { Lines = Request(coreMatter, lines).Lines.Select(l => l with { Hours = 0.3m }).ToList() };

    [Fact(DisplayName = "082c C2 must-fire, end to end: a rule of an All-records Procedure's Active version raises a finding through the loader")]
    public async Task AProceduresRule_IsLoadedThroughTheResolver_AndRaisesAFinding_C2()
    {
        var (procedure, _, _, version) = ProcedureRule("PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var loaded = await Loader().LoadAsync(OffIncrement(null), CancellationToken.None);
        var result = await Evaluator().EvaluateAsync(OffIncrement(null), CancellationToken.None);

        loaded.Rules.Should().BeEmpty("the legacy stream holds no Procedure set");
        loaded.Procedures.PolicyVersions.Select(v => v.PolicyVersionId).Should().Equal(version);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.PolicyCode.Should().Be("PROC-INCREMENT");
        result.RulesByVersion[version].Source.Should().Be(BillingRuleSource.Procedure);
        result.RulesByVersion[version].ProcedureId.Should().Be(procedure);
        result.Gaps.Should().BeEmpty();
        AssertWroteNothing();
    }

    [Fact(DisplayName = "082c C3 must-fire: an Assigned-records-only Procedure with no assignment on the chain gives no finding and no gap")]
    public async Task AnAssignedRecordsOnlyProcedure_WithNoAssignment_GivesNoFindingAndNoGap_C3()
    {
        ProcedureRule("PROC-ASSIGNED", RuleType.TimeIncrement, HalfHour, ProcedureAppliesTo.AssignedRecordsOnly);

        var result = await Evaluator().EvaluateAsync(OffIncrement(MatterA), CancellationToken.None);

        result.Findings.Should().BeEmpty();
        result.Gaps.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c C1/C3 second must-fire: a Procedure's set with scope Client default or Firm is not in the legacy stream (the partition); an ordinary set beside it is")]
    public async Task AProceduresRuleSet_WithALegacyScope_IsNotInTheLegacyStream_ThePartition_C1_C3()
    {
        // Replaces 081's "not evaluated here": a Procedure set is now judged, through the resolver only (the test above and C2).
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, HalfHour);
        var assigned = ProcedureRule("PROC-ASSIGNED", RuleType.TimeIncrement, HalfHour, ProcedureAppliesTo.AssignedRecordsOnly);
        var firm = ProcedureRule("PROC-FIRM", RuleType.TimeIncrement, HalfHour, setScope: RuleSetScope.Firm);

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.Rules.Select(r => r.PolicyCode).Should().Equal("CD");
        loaded.Rules.Should().OnlyContain(r => r.Source == BillingRuleSource.Legacy && r.Rank == BillingFactLoader.LegacyRank(RuleSetScope.ClientDefault));
        loaded.Procedures.PolicyVersions.Select(v => v.PolicyVersionId).Should().Equal(
            [firm.Version], "the all-records Procedure is read by the resolver; the unassigned one has no source, so it is not even read");
        loaded.Procedures.PolicyVersions.Should().NotContain(v => v.PolicyVersionId == assigned.Version);
        AssertWroteNothing();
    }

    [Fact(DisplayName = "082c C5: the loader ranks a legacy client-default rule (AllRecords, 0, false) and a legacy firm rule (Firm, 0, true)")]
    public async Task LegacyCandidates_CarryTheirScopesRank_C5()
    {
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, HalfHour);
        Policy(RuleSet(RuleSetScope.Firm, FirmId), "FIRM", RuleType.TimeIncrement, HalfHour);

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);

        loaded.Rules.ToDictionary(r => r.PolicyCode, r => r.Rank).Should().BeEquivalentTo(new Dictionary<string, ProcedureRank>
        {
            ["CD"] = new(ProcedureLevel.AllRecords, 0, false),
            ["FIRM"] = new(ProcedureLevel.Firm, 0, true),
        });
    }

    [Fact(DisplayName = "082c C10 must-fire: a Duplicate rule in the Procedure stream alone still reads the priors")]
    public async Task ADuplicateRuleOnlyInTheProcedureStream_StillReadsThePriors_C10()
    {
        ProcedureRule("PROC-DUPLICATE", RuleType.Duplicate, DuplicateByNumber);
        PriorWithInvoice("INV-100.1", ("sprk_matter", "sprk_matter", OrdinaryMatter));
        _world.OrdinaryRoot("sprk_matter", OrdinaryMatter);

        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);
        var result = await Evaluator().EvaluateAsync(Request(null), CancellationToken.None);

        loaded.Rules.Should().BeEmpty();
        loaded.PriorSubmissions.Should().ContainSingle();
        result.Findings.Should().ContainSingle(f => f.RuleType == RuleType.Duplicate).Which.PolicyCode.Should().Be("PROC-DUPLICATE");
    }

    [Fact(DisplayName = "082c C9: with a Procedure in force, the reads for 150 lines are the reads for one")]
    public async Task TheReads_AreBounded_WithAProcedure_C9()
    {
        ProcedureRule("PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, HalfHour);

        await Evaluator().EvaluateAsync(OffIncrement(MatterA, lines: 1), CancellationToken.None);
        var few = _world.QueriedTables.ToList();
        _world.QueriedTables.Clear();
        var result = await Evaluator().EvaluateAsync(OffIncrement(MatterA, lines: 150), CancellationToken.None);

        few.Should().Contain([ProcedureEntity.Entity, ProcedureEntity.VersionEntity], "the resolver read the Procedures");
        _world.QueriedTables.Should().Equal(few);
        result.Findings.Should().HaveCount(300, "both rules judge every line");
    }

    [Fact(DisplayName = "082c C4: RequireDisjoint throws when a policy version is in both streams, and passes when they are disjoint")]
    public async Task RequireDisjoint_ThrowsOnASharedVersion_C4()
    {
        ProcedureRule("PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, HalfHour);
        var loaded = await Loader().LoadAsync(Request(null), CancellationToken.None);
        var shared = loaded.Rules.Single() with { PolicyVersionId = loaded.Procedures.PolicyVersions.Single().PolicyVersionId };

        var disjoint = () => BillingFactLoader.RequireDisjoint(loaded.Rules, loaded.Procedures);
        var overlapping = () => BillingFactLoader.RequireDisjoint([shared], loaded.Procedures);

        disjoint.Should().NotThrow();
        overlapping.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*both as a legacy rule and as a Procedure rule*");
    }

    [Fact(DisplayName = "082c (adjustment caller): LoadRulesOnAsync returns the legacy version in force on the date and the Procedure rules, with no precedence")]
    public async Task LoadRulesOnAsync_ReturnsBothStreamsOnTheDate_NoPrecedence()
    {
        ProcedureRule("PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        Policy(RuleSet(RuleSetScope.ClientDefault), "CD", RuleType.TimeIncrement, HalfHour);
        Policy(RuleSet(RuleSetScope.Firm, FirmId), "FIRM", RuleType.TimeIncrement, HalfHour);

        var rules = await Loader().LoadRulesOnAsync(FirmId, new CoreRecordReference(CoreRecordKind.Matter, MatterA), new DateOnly(2026, 3, 2), null, CancellationToken.None);
        var before = await Loader().LoadRulesOnAsync(FirmId, null, new DateOnly(2025, 12, 31), null, CancellationToken.None);

        rules.Select(r => (r.PolicyCode, r.Source)).Should().BeEquivalentTo(new[]
        {
            ("CD", BillingRuleSource.Legacy), ("FIRM", BillingRuleSource.Legacy), ("PROC-INCREMENT", BillingRuleSource.Procedure),
        });
        before.Should().BeEmpty("nothing is in force before 1 January");
    }
}
