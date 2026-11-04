using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals.Billing;

/// <summary>
/// ebilling-r1 task 082b (procedure model 6.1-6.5, 7.1, 7.2; ontology D-172, D-175, D-177; the 082a review criteria U3, O1 and the K1 / K3
/// date rule; the owner's record-rule date window): <see cref="ProcedureResolver"/> over an in-memory Dataverse that evaluates each query
/// (<see cref="SecureChildShareWorld"/>). In scope: sources and the firm condition, Procedure- and rule-level overrides with distance,
/// version choice and gaps, record rules in their base rule's slot, fail-closed reads, the cost bound and the reader-facing projection.
/// The loader, evaluator, gap finding and snapshot are 082c / 082d.
/// </summary>
public sealed class ProcedureResolverTests
{
    private const string Matter = SecureRootInheritance.Matter;
    private const string Project = SecureRootInheritance.Project;
    private const string WorkAssignment = SecureRootInheritance.WorkAssignment;
    private const string RecordType = SecureRootInheritance.RecordTypeRefEntity;
    private const string RuleSet = "sprk_ruleset";
    private const string Policy = "sprk_policy";
    private const string PolicyVersion = "sprk_policyversion";
    private const string MatterName = "Acme Holdings v. Secret Counterparty";

    private static readonly Guid F = Guid.Parse("08b00000-0000-4000-8000-0000000000f1");
    private static readonly Guid G = Guid.Parse("08b00000-0000-4000-8000-0000000000f2");
    private static readonly Guid M = Guid.Parse("08b00000-0000-4000-8000-0000000000a1");
    private static readonly Guid P = Guid.Parse("08b00000-0000-4000-8000-0000000000b1");
    private static readonly Guid W = Guid.Parse("08b00000-0000-4000-8000-0000000000c1");
    private static readonly Guid MatterType = Guid.Parse("08b00000-0000-4000-8000-0000000000e1");

    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jan15 = new(2026, 1, 15);
    private static readonly DateOnly Feb1 = new(2026, 2, 1);
    private static readonly DateOnly Feb15 = new(2026, 2, 15);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Apr15 = new(2026, 4, 15);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Mock<IGenericEntityService> _entities;
    private readonly Dictionary<Guid, Guid> _setOf = new();

    public ProcedureResolverTests()
    {
        _entities = SecureChildShareWorld.EntitiesOver(() => _world);
        _world.Add(RecordType, MatterType, ("sprk_recordlogicalname", "sprk_matter"));
        _world.Add(Matter, M, ("sprk_name", MatterName));
        // P is filed under M (its pair); W is filed under P only (typed).
        _world.Add(Project, P, (SecureRootInheritance.PairIdColumn, M.ToString()), (SecureRootInheritance.PairTypeColumn, Ref(RecordType, MatterType)));
        _world.Add(WorkAssignment, W, ("sprk_regardingproject", Ref(Project, P)));
    }

    // ── Seeding ─────────────────────────────────────────────────────────────────────────────────────────────

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    private static OptionSetValue Option(int value) => new(value);

    private static DateTime Utc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static DateTime Stored(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private Guid AddProcedure(string name, ProcedureAppliesTo appliesTo, Guid? firm = null, int status = ProcedureEntity.StatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.NameColumn, name),
            (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.AppliesToColumn, Option((int)appliesTo)),
            (ProcedureEntity.StatusColumn, Option(status)),
        };
        if (firm is { } f) columns.Add((ProcedureEntity.FirmColumn, Ref("sprk_organization", f)));
        _world.Add(ProcedureEntity.Entity, id, columns.ToArray());

        var set = Guid.NewGuid();
        _world.Add(RuleSet, set,
            ("sprk_procedure", Ref(ProcedureEntity.Entity, id)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)));
        _setOf[id] = set;
        return id;
    }

    private Guid AddVersion(Guid procedure, int number, DateOnly? from, DateOnly? to, int status = ProcedureEntity.VersionStatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureEntity.VersionNumberColumn, number),
            (ProcedureEntity.VersionStatusColumn, Option(status)),
        };
        if (from is { } f) columns.Add((ProcedureEntity.VersionEffectiveFromColumn, Stored(f)));
        if (to is { } t) columns.Add((ProcedureEntity.VersionEffectiveToColumn, Stored(t)));
        _world.Add(ProcedureEntity.VersionEntity, id, columns.ToArray());
        return id;
    }

    private Guid AddRule(Guid set, string code, bool enabled = true)
    {
        var id = Guid.NewGuid();
        _world.Add(Policy, id,
            ("sprk_policycode", code), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", enabled), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
        return id;
    }

    private Guid AddProcedureRule(Guid procedure, string code, bool enabled = true) => AddRule(_setOf[procedure], code, enabled);

    private Guid AddRuleVersion(
        Guid policy, Guid? procedureVersion, DateOnly from, int number = 1, bool published = true, Guid? basedOn = null,
        RuleType type = RuleType.ApprovedRate)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_policy", Ref(Policy, policy)),
            ("sprk_versionnumber", number),
            ("sprk_ruletype", Option((int)type)),
            ("sprk_inforcefrom", Utc(from)),
            (RuleSetService.PublishStateColumn, Option(published ? RuleSetService.PublishStatePublished : RuleSetService.PublishStateDraft)),
        };
        if (procedureVersion is { } pv) columns.Add((RuleSetService.ProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, pv)));
        if (basedOn is { } b) columns.Add((ProcedureAssignmentService.BasedOnVersionColumn, Ref(PolicyVersion, b)));
        _world.Add(PolicyVersion, id, columns.ToArray());
        return id;
    }

    private Guid AddRow(string? table, Guid record, params (string Column, object Value)[] columns)
    {
        var id = Guid.NewGuid();
        var all = new List<(string, object)>(columns) { ("statecode", Option(0)) };
        if (table is not null) all.Add((table, Ref(table, record)));
        _world.Add(ProcedureAssignmentEntity.Entity, id, all.ToArray());
        return id;
    }

    private Guid Assign(string? table, Guid record, Guid procedure, int mode = ProcedureAssignmentEntity.ModeFollowLatest, Guid? pinned = null,
        string? reason = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(mode)),
        };
        if (pinned is { } v) columns.Add((ProcedureAssignmentEntity.PinnedVersionColumn, Ref(ProcedureEntity.VersionEntity, v)));
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        return AddRow(table, record, columns.ToArray());
    }

    private Guid ExcludeRule(string? table, Guid record, Guid policy, Guid? firm = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, policy)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
        };
        if (firm is { } f) columns.Add((ProcedureAssignmentEntity.FirmColumn, Ref("sprk_organization", f)));
        return AddRow(table, record, columns.ToArray());
    }

    private Guid AddRecordSet(string? table, Guid record, Guid? firm, int? scope = null)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_scope", Option(scope ?? (firm is null ? RecordRuleSet.ScopeRecord : RecordRuleSet.ScopeFirmRecord))),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)),
        };
        if (table is not null) columns.Add((table, Ref(table, record)));
        if (firm is { } f) columns.Add(("sprk_firm", Ref("sprk_organization", f)));
        _world.Add(RuleSet, id, columns.ToArray());
        return id;
    }

    /// <summary>A changed rule (089c): the Exclude row of the base rule plus a record rule based on <paramref name="baseVersion"/>.</summary>
    private Guid ChangeRule(string table, Guid record, Guid basePolicy, Guid baseVersion, DateOnly from, Guid? firm = null, string code = "BRR-1")
    {
        ExcludeRule(table, record, basePolicy, firm);
        var set = AddRecordSet(table, record, firm);
        var recordRule = AddRule(set, code);
        AddRuleVersion(recordRule, null, from, basedOn: baseVersion);
        return recordRule;
    }

    private Task<ProcedureResolution> Resolve(CoreRecordKind kind, Guid id, Guid firm) =>
        ProcedureResolver.ReadAsync(_entities.Object, NullLogger.Instance, new CoreRecordReference(kind, id), firm, CancellationToken.None);

    private async Task<IReadOnlyList<string>> CodesOn(CoreRecordKind kind, Guid id, Guid firm, DateOnly date) =>
        (await Resolve(kind, id, firm)).RulesOn(date).Rules.Select(r => r.Version.PolicyCode).ToList();

    /// <summary>A Procedure with one version [from, open) and one rule <paramref name="code"/> published under it.</summary>
    private (Guid Procedure, Guid Version, Guid Rule, Guid RuleVersion) Simple(
        string code, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null, DateOnly? from = null)
    {
        var procedure = AddProcedure("Procedure " + code, appliesTo, firm);
        var version = AddVersion(procedure, 1, from ?? Jan1, null);
        var rule = AddProcedureRule(procedure, code);
        var ruleVersion = AddRuleVersion(rule, version, from ?? Jan1);
        return (procedure, version, rule, ruleVersion);
    }

    // ── Sources and the firm condition (6.1, O-20) ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b sources: an All-records Procedure applies to every invoice, with or without a core record")]
    public async Task AllRecords_AppliesToEveryInvoice()
    {
        Simple("ALL-1");

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("ALL-1");
        (await CodesOn(CoreRecordKind.WorkAssignment, W, G, Feb1)).Should().Equal("ALL-1");
        var noCore = await ProcedureResolver.ReadAsync(_entities.Object, NullLogger.Instance, null, F, CancellationToken.None);
        noCore.RulesOn(Feb1).Rules.Select(r => r.Version.PolicyCode).Should().Equal("ALL-1");
        noCore.RulesOn(Feb1).Procedures.Single().Rank.Level.Should().Be(ProcedureLevel.AllRecords);
    }

    [Fact(DisplayName = "082b sources: a Firm Procedure for F applies to F's invoices and not to G's")]
    public async Task FirmProcedure_AppliesToItsFirmOnly()
    {
        Simple("FIRM-F", ProcedureAppliesTo.Firm, F);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("FIRM-F");
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty();
    }

    [Fact(DisplayName = "082b sources: a manually assigned Procedure naming firm F does not apply to G's invoice on that record (firm condition on every source)")]
    public async Task ManualAssignment_NamingAFirm_AppliesOnlyToThatFirmsInvoices()
    {
        var (procedure, _, _, _) = Simple("ASSIGNED-F", ProcedureAppliesTo.AssignedRecordsOnly, F);
        Assign(Matter, M, procedure);

        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty("the Procedure names firm F");
        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("ASSIGNED-F");
    }

    [Fact(DisplayName = "082b sources: a Procedure assigned on M applies on P (d1) and does not apply on W1 under P (no second hop)")]
    public async Task AssignedOnMatter_AppliesOnItsProject_NotOnTheProjectsWorkAssignment()
    {
        var (procedure, _, _, _) = Simple("M-ONLY", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(Matter, M, procedure);

        var onProject = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1);
        onProject.Rules.Select(r => r.Version.PolicyCode).Should().Equal("M-ONLY");
        onProject.Procedures.Single().Rank.Should().Be(new ProcedureRank(ProcedureLevel.ManualParent, 1, false));
        (await CodesOn(CoreRecordKind.WorkAssignment, W, F, Feb1)).Should().BeEmpty("M's Procedure does not apply on W1");
    }

    [Fact(DisplayName = "082b sources (081 MUST-DO): an Assigned-records-only Procedure with no assignment on the chain applies to nothing, with no gap")]
    public async Task AssignedRecordsOnly_WithNoAssignment_IsNotApplicable()
    {
        Simple("UNASSIGNED", ProcedureAppliesTo.AssignedRecordsOnly);

        var onDate = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1);

        onDate.Rules.Should().BeEmpty();
        onDate.Gaps.Should().BeEmpty();
        onDate.Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b sources (089b K1): an assignment to a Retired Procedure is ignored")]
    public async Task AssignmentToARetiredProcedure_IsIgnored()
    {
        var procedure = AddProcedure("Retired", ProcedureAppliesTo.AssignedRecordsOnly, status: ProcedureEntity.StatusRetired);
        var version = AddVersion(procedure, 1, Jan1, null);
        AddRuleVersion(AddProcedureRule(procedure, "RETIRED-1"), version, Jan1);
        Assign(Matter, M, procedure);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().BeEmpty();
    }

    [Fact(DisplayName = "082b sources: one Procedure from several sources takes the highest level")]
    public async Task OneProcedureFromSeveralSources_TakesTheHighestLevel()
    {
        var (procedure, _, _, _) = Simple("BOTH", ProcedureAppliesTo.AllRecords);
        Assign(Matter, M, procedure);

        var applied = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1).Procedures.Single();

        applied.Rank.Level.Should().Be(ProcedureLevel.ManualParent);
        applied.Sources.Should().Be(ProcedureSources.AllRecords | ProcedureSources.Manual);
    }

    [Theory(DisplayName = "082b (080 limit, ontology-r1 must-fire): an assignment row with every core lookup null contributes nothing")]
    [InlineData("follow")]
    [InlineData("exclude")]
    [InlineData("rule-exclude")]
    public async Task ARowWithNoCoreLookup_ContributesNothing(string kind)
    {
        var assigned = Simple("ASSIGNED", ProcedureAppliesTo.AssignedRecordsOnly);
        var general = Simple("GENERAL");
        _ = kind switch
        {
            "follow" => Assign(null, Guid.Empty, assigned.Procedure),
            "exclude" => Assign(null, Guid.Empty, general.Procedure, ProcedureAssignmentEntity.ModeExcluded),
            _ => ExcludeRule(null, Guid.Empty, general.Rule),
        };

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("GENERAL");
    }

    [Fact(DisplayName = "082b (080 limit, must-not-fire): a row with one live core lookup applies")]
    public async Task ARowWithOneCoreLookup_Applies()
    {
        var assigned = Simple("ASSIGNED", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(Matter, M, assigned.Procedure);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("ASSIGNED");
    }

    // ── Procedure-level overrides (6.2, 6.5, D-177 (3)) ──────────────────────────────────────────────────────

    [Fact(DisplayName = "082b overrides: an Exclude at d1 removes the Procedure on the child, and only a d0 assignment of it lifts that")]
    public async Task ExcludeAtParent_IsLiftedOnlyByAnOwnAssignment()
    {
        var (procedure, _, _, _) = Simple("ALL-1");
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModeExcluded);

        var excluded = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1);
        excluded.Rules.Should().BeEmpty();
        excluded.ExcludedProcedures.Single().ProcedureId.Should().Be(procedure);

        Assign(Project, P, procedure);
        var lifted = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1);
        lifted.Rules.Select(r => r.Version.PolicyCode).Should().Equal("ALL-1");
        lifted.Procedures.Single().Rank.Level.Should().Be(ProcedureLevel.ManualOwn);
    }

    [Theory(DisplayName = "082b overrides (O1, D-177 (3)): between a work assignment's two d1 parents Exclude wins, whichever parent holds it")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoParents_ExcludeWins_InEitherOrder(bool excludeOnMatter)
    {
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var (procedure, _, _, _) = Simple("ASSIGNED", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(excludeOnMatter ? Matter : Project, excludeOnMatter ? M : P, procedure, ProcedureAssignmentEntity.ModeExcluded);
        Assign(excludeOnMatter ? Project : Matter, excludeOnMatter ? P : M, procedure);

        var resolution = await Resolve(CoreRecordKind.WorkAssignment, W, F);
        resolution.Chain.Select(l => l.Distance).Should().Equal(0, 1, 1);
        var onDate = resolution.RulesOn(Feb1);

        onDate.Rules.Should().BeEmpty();
        onDate.ExcludedProcedures.Single().ProcedureId.Should().Be(procedure);
    }

    [Theory(DisplayName = "082b overrides (O1, D-177 (3)): two different d1 pins raise one blocking gap, whichever parent holds which")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoParents_DifferentPins_AreAGap_InEitherOrder(bool firstOnMatter)
    {
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var procedure = AddProcedure("Pinned twice", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var rule = AddProcedureRule(procedure, "PIN-1");
        AddRuleVersion(rule, v1, Jan1);
        AddRuleVersion(rule, v2, Mar1, number: 2);
        Assign(firstOnMatter ? Matter : Project, firstOnMatter ? M : P, procedure, ProcedureAssignmentEntity.ModePinned, v1);
        Assign(firstOnMatter ? Project : Matter, firstOnMatter ? P : M, procedure, ProcedureAssignmentEntity.ModePinned, v2);

        var onDate = (await Resolve(CoreRecordKind.WorkAssignment, W, F)).RulesOn(Mar15);

        onDate.Rules.Should().BeEmpty();
        onDate.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g =>
            g.ProcedureId == procedure && g.ProcedureName == "Pinned twice" && g.WorkDate == Mar15 && g.Kind == ProcedureGapKind.ConflictingParents);
    }

    [Fact(DisplayName = "082b overrides (O1): the chain's table-then-id order is for stability only; resolving the same rows over the reversed parents gives the same answer")]
    public async Task TwoParents_ChainOrder_DoesNotChangeTheResult()
    {
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var general = Simple("GENERAL");
        var assigned = Simple("ASSIGNED", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(Matter, M, general.Procedure, ProcedureAssignmentEntity.ModeExcluded);
        Assign(Project, P, general.Procedure);
        Assign(Project, P, assigned.Procedure);
        ExcludeRule(Matter, M, assigned.Rule);

        var rows = await ProcedureResolver.ReadRowsAsync(_entities.Object, NullLogger.Instance, new CoreRecordReference(CoreRecordKind.WorkAssignment, W), F, CancellationToken.None);
        var reversed = rows with { Chain = [rows.Chain[0], rows.Chain[2], rows.Chain[1]] };

        var asRead = new ProcedureResolution(rows).RulesOn(Feb1);
        var swapped = new ProcedureResolution(reversed).RulesOn(Feb1);

        swapped.Should().BeEquivalentTo(asRead, o => o.WithStrictOrdering());
        asRead.Rules.Should().BeEmpty("GENERAL is excluded (Exclude wins) and ASSIGNED's one rule is excluded on M");
    }

    [Fact(DisplayName = "082b overrides: a Pin keeps its version for work dated in a later version's window")]
    public async Task Pin_KeepsItsVersion_InALaterWindow()
    {
        var procedure = AddProcedure("Rate card", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var rule = AddProcedureRule(procedure, "RATE");
        var r1 = AddRuleVersion(rule, v1, Jan1);
        var r2 = AddRuleVersion(rule, v2, Mar1, number: 2);
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Apr15);

        onDate.Rules.Single().Version.PolicyVersionId.Should().Be(r1);
        onDate.Procedures.Single().Should().Match<AppliedProcedure>(p => p.ProcedureVersionId == v1 && p.Pinned);
        (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Apr15).Rules.Single().Version.PolicyVersionId
            .Should().Be(r1, "P inherits M's pin at d1");
        (await Resolve(CoreRecordKind.WorkAssignment, W, F)).RulesOn(Apr15).Rules.Single().Version.PolicyVersionId
            .Should().Be(r2, "W is not under M, so it follows the date");
    }

    // ── Rule-level overrides and record rules (6.3) ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b rule overrides: a rule excluded for firm F is removed on F's invoice and still runs on G's")]
    public async Task ExcludedRuleForAFirm_StillRunsForAnotherFirm()
    {
        var general = Simple("GENERAL");
        ExcludeRule(Matter, M, general.Rule, F);

        var onF = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1);
        onF.Rules.Should().BeEmpty();
        onF.ExcludedRules.Single().Should().Match<ExcludedRule>(e => e.Version.PolicyCode == "GENERAL" && !e.ReplacedByRecordRule && e.Distance == 0);
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().Equal("GENERAL");
    }

    [Fact(DisplayName = "082b record rules: a record rule for firm F runs in its base rule's slot on F's invoices only")]
    public async Task RecordRule_RunsInTheBaseSlot_ForItsFirmOnly()
    {
        var general = Simple("GENERAL");
        ChangeRule(Matter, M, general.Rule, general.RuleVersion, Jan15, F, "BRR-F");

        var onF = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1);
        onF.Rules.Single().Should().Match<ResolvedRule>(r =>
            r.Version.PolicyCode == "BRR-F" && r.RecordRule!.BasePolicyId == general.Rule && r.Procedure.ProcedureId == general.Procedure
            && r.Procedure.Rank.Level == ProcedureLevel.AllRecords);
        onF.ExcludedRules.Single().ReplacedByRecordRule.Should().BeTrue();
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().Equal("GENERAL");
    }

    [Fact(DisplayName = "082b record rules: a record-scoped set with a scope but no record lookup applies to nothing")]
    public async Task RecordScopedSet_WithNoRecordLookup_AppliesToNothing()
    {
        var general = Simple("GENERAL");
        var set = AddRecordSet(null, Guid.Empty, null);
        AddRuleVersion(AddRule(set, "BRR-ORPHAN"), null, Jan1, basedOn: general.RuleVersion);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("GENERAL");
    }

    [Fact(DisplayName = "082b record rules (owner 2026-11-04): a record rule runs only from its own effective-from; earlier dates are judged by the base version, unchanged")]
    public async Task RecordRule_BeforeItsOwnEffectiveFrom_LeavesTheBaseInForce()
    {
        var general = Simple("GENERAL");
        ChangeRule(Matter, M, general.Rule, general.RuleVersion, Mar1, code: "BRR-M");
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var before = resolution.RulesOn(Feb15);
        before.Rules.Single().Version.PolicyVersionId.Should().Be(general.RuleVersion);
        before.ExcludedRules.Should().BeEmpty();
        resolution.RulesOn(Mar15).Rules.Single().Version.PolicyCode.Should().Be("BRR-M");
    }

    [Fact(DisplayName = "082b record rules (089c K1/K3): a record rule runs only where its base rule resolves for the date; a rule a later version dropped strands it")]
    public async Task RecordRule_WhoseBaseIsNotInTheDatesVersion_HasNoSlot()
    {
        var procedure = AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        AddVersion(procedure, 2, Mar1, null);
        var dropped = AddProcedureRule(procedure, "DROPPED"); // enabled, but published under v1 only
        var droppedVersion = AddRuleVersion(dropped, v1, Jan1);
        ChangeRule(Matter, M, dropped, droppedVersion, Jan15, code: "BRR-DROPPED");
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        resolution.RulesOn(Feb1).Rules.Select(r => r.Version.PolicyCode).Should().Equal("BRR-DROPPED");
        resolution.RulesOn(Mar15).Rules.Should().BeEmpty("v2 dropped the base rule, so neither it nor its record rule applies");
    }

    [Fact(DisplayName = "082b record rules (089c K3): a retired Procedure's record rule, and a record rule whose base version is gone, have no slot")]
    public async Task RecordRule_OfARetiredProcedureOrADeletedBase_HasNoSlot()
    {
        var retired = AddProcedure("Retired", ProcedureAppliesTo.AllRecords, status: ProcedureEntity.StatusRetired);
        var rv = AddVersion(retired, 1, Jan1, null);
        var retiredRule = AddProcedureRule(retired, "RETIRED");
        ChangeRule(Matter, M, retiredRule, AddRuleVersion(retiredRule, rv, Jan1), Jan1, code: "BRR-RETIRED");
        var set = AddRecordSet(Matter, M, G);
        AddRuleVersion(AddRule(set, "BRR-DELETED-BASE"), null, Jan1, basedOn: Guid.NewGuid());

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().BeEmpty();
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty();
    }

    // ── Versions (7.2) ───────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b versions: v1's window uses v1's Published rules, v2's window v2's; a rule dropped by v2 applies only in v1's window, whatever sprk_enabled says")]
    public async Task Versions_EachWindowUsesItsOwnRules()
    {
        var procedure = AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var kept = AddProcedureRule(procedure, "KEPT");
        var k1 = AddRuleVersion(kept, v1, Jan1);
        var k2 = AddRuleVersion(kept, v2, Mar1, number: 2);
        AddRuleVersion(AddProcedureRule(procedure, "DROPPED"), v1, Jan1);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var inV1 = resolution.RulesOn(Feb1);
        inV1.Rules.Select(r => r.Version.PolicyVersionId).Should().BeEquivalentTo(new[] { k1, inV1.Rules.Single(r => r.Version.PolicyCode == "DROPPED").Version.PolicyVersionId });
        inV1.Procedures.Single().ProcedureVersionId.Should().Be(v1);
        var inV2 = resolution.RulesOn(Mar15);
        inV2.Rules.Select(r => r.Version.PolicyVersionId).Should().Equal(k2);
        inV2.Procedures.Single().ProcedureVersionId.Should().Be(v2);
    }

    [Fact(DisplayName = "082b versions: a Draft policy version never loads")]
    public async Task DraftPolicyVersion_NeverLoads()
    {
        var (procedure, version, _, _) = Simple("PUBLISHED");
        AddRuleVersion(AddProcedureRule(procedure, "DRAFT"), version, Jan1, published: false);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("PUBLISHED");
        _world.Queries.Where(q => q.EntityName == PolicyVersion).Should().OnlyContain(q =>
            q.Criteria.Conditions.Any(c => c.AttributeName == RuleSetService.PublishStateColumn));
    }

    [Fact(DisplayName = "082b versions: a correction inside v2 applies from its own effective-from")]
    public async Task Correction_AppliesFromItsEffectiveFrom()
    {
        var (_, version, rule, original) = Simple("RATE");
        var correction = AddRuleVersion(rule, version, Mar1, number: 2);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        resolution.RulesOn(Feb15).Rules.Single().Version.PolicyVersionId.Should().Be(original);
        resolution.RulesOn(Mar1).Rules.Single().Version.PolicyVersionId.Should().Be(correction);
    }

    [Fact(DisplayName = "082b versions (I-P1): two versions in force containing the date fault the resolution")]
    public async Task TwoVersionsContainingTheDate_Fault()
    {
        var procedure = AddProcedure("Overlap", ProcedureAppliesTo.AllRecords);
        AddVersion(procedure, 1, Jan1, null);
        AddVersion(procedure, 2, Feb1, null);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var resolve = () => resolution.RulesOn(Feb15);

        resolve.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*2 versions in force*");
        resolution.RulesOn(Jan15).Gaps.Should().BeEmpty("only the overlapping dates fault");
    }

    [Fact(DisplayName = "082b deviation 5: an Off rule, and a set with no rules, contribute nothing; BILL-SYSTEM-SUBMISSION is never resolved (D-125)")]
    public async Task OffRule_EmptySet_AndTheSystemPolicy_ContributeNothing()
    {
        var (procedure, version, _, _) = Simple("ON");
        AddRuleVersion(AddProcedureRule(procedure, "OFF", enabled: false), version, Jan1);
        AddRuleVersion(AddProcedureRule(procedure, BillingRuleCatalogue.SystemPolicyCode), version, Jan1, type: RuleType.SubmissionIntegrity);
        var empty = AddProcedure("Empty", ProcedureAppliesTo.AllRecords);
        AddVersion(empty, 1, Jan1, null);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1);

        onDate.Rules.Select(r => r.Version.PolicyCode).Should().Equal("ON");
        onDate.Procedures.Should().HaveCount(2, "the empty Procedure applies but judges nothing");
    }

    // ── Gaps (7.1 gap reading, owner F2-d; O-22) ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b gaps: work before the first version's effective-from is not applicable (no gap); a Draft-only Procedure is not applicable")]
    public async Task BeforeTheFirstVersion_AndDraftOnly_AreNotApplicable()
    {
        Simple("LATER", from: Feb1);
        var draftOnly = AddProcedure("Draft only", ProcedureAppliesTo.AllRecords);
        AddVersion(draftOnly, 1, Jan1, null, ProcedureEntity.VersionStatusDraft);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Jan15);

        onDate.Rules.Should().BeEmpty();
        onDate.Gaps.Should().BeEmpty();
        onDate.Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b gaps: a hole between versions (a withdrawn version's window) raises exactly one gap naming the Procedure and date")]
    public async Task HoleBetweenVersions_IsOneGap()
    {
        var procedure = AddProcedure("Holey", ProcedureAppliesTo.AllRecords);
        AddVersion(procedure, 1, Jan1, Feb1, ProcedureEntity.VersionStatusSuperseded);
        AddVersion(procedure, 2, Feb1, Mar1, ProcedureEntity.VersionStatusRetired);
        AddVersion(procedure, 3, Mar1, null);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb15);

        onDate.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g =>
            g.ProcedureId == procedure && g.ProcedureName == "Holey" && g.WorkDate == Feb15 && g.Kind == ProcedureGapKind.HoleBetweenVersions);
    }

    [Fact(DisplayName = "082b gaps: a version that ends with no successor is a gap only for work dated after its end")]
    public async Task AfterTheLastEnd_IsAGap_OnlyAfterTheEnd()
    {
        var procedure = AddProcedure("Ended", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        AddRuleVersion(AddProcedureRule(procedure, "ENDED"), v1, Jan1);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        resolution.RulesOn(Feb15).Gaps.Should().BeEmpty();
        resolution.RulesOn(Mar1).Gaps.Should().ContainSingle().Which.Kind.Should().Be(ProcedureGapKind.AfterLastVersion);
        resolution.RulesOn(Mar1).Rules.Should().BeEmpty();
    }

    [Theory(DisplayName = "082b gaps: a pin to a Retired version, or to a version not in force, is one gap; a pinned version applies whatever the date")]
    [InlineData(ProcedureEntity.VersionStatusRetired, ProcedureGapKind.PinnedVersionRetired)]
    [InlineData(ProcedureEntity.VersionStatusDraft, ProcedureGapKind.PinnedVersionNotInForce)]
    public async Task PinToAVersionNotInForce_IsAGap(int status, ProcedureGapKind kind)
    {
        var procedure = AddProcedure("Pinned", ProcedureAppliesTo.AllRecords);
        AddVersion(procedure, 1, Jan1, null);
        var pinned = AddVersion(procedure, 2, Jan1, null, status);
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, pinned);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1);

        onDate.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g => g.Kind == kind && g.ProcedureId == procedure);
        onDate.Rules.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b versions: a pinned version's rules apply before its effective-from (whatever the date), taking its first rule version")]
    public async Task Pin_AppliesBeforeItsOwnWindow()
    {
        var (procedure, version, _, ruleVersion) = Simple("LATE", from: Mar1);
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, version);

        (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Jan15).Rules.Single().Version.PolicyVersionId.Should().Be(ruleVersion);
    }

    // ── Fail closed (7.1) ────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "082b fail closed: a fault reading the chain, assignment rows, Procedures, versions, rule sets, policies or policy versions faults the resolution")]
    [InlineData(Project)]
    [InlineData(ProcedureAssignmentEntity.Entity)]
    [InlineData(ProcedureEntity.Entity)]
    [InlineData(ProcedureEntity.VersionEntity)]
    [InlineData(RuleSet)]
    [InlineData(Policy)]
    [InlineData(PolicyVersion)]
    public async Task AReadFault_FaultsTheResolution(string table)
    {
        var (procedure, _, _, _) = Simple("ALL-1");
        Assign(Matter, M, procedure);
        _world.FailingQueriesOf(table);

        var read = () => Resolve(CoreRecordKind.Project, P, F);

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{table}*");
    }

    [Fact(DisplayName = "082b fail closed: an override row with a mode the BFF never writes faults the resolution")]
    public async Task AnUnknownMode_Faults()
    {
        var (procedure, _, _, _) = Simple("ALL-1");
        Assign(Matter, M, procedure, mode: 100000009);

        var read = () => Resolve(CoreRecordKind.Matter, M, F);

        await read.Should().ThrowAsync<ProcedureResolutionFaultException>();
    }

    // ── Cost bound (8.2) ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b cost bound: the reads are made once; resolving 1 or 150 work dates adds no query")]
    public async Task Reads_DoNotDependOnTheNumberOfWorkDates()
    {
        var (procedure, _, rule, ruleVersion) = Simple("ALL-1");
        Assign(Project, P, procedure);
        ChangeRule(Matter, M, rule, ruleVersion, Jan15);
        var resolution = await Resolve(CoreRecordKind.Project, P, F);
        var afterRead = _world.Queries.Count;

        resolution.RulesOn(Feb1);
        var afterOne = _world.Queries.Count;
        for (var i = 0; i < 150; i++)
        {
            resolution.RulesOn(Jan1.AddDays(i));
        }

        afterOne.Should().Be(afterRead);
        _world.Queries.Count.Should().Be(afterRead);
    }

    // ── U3: reader-facing output names no record ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b U3: a project under a Secure matter, judged with the matter's Procedure: the reader-facing output carries no matter id or name")]
    public async Task ReaderView_UnderASecureMatter_NamesNoParentRecord()
    {
        _world.SecureRoot(Matter, M);
        var (procedure, _, rule, ruleVersion) = Simple("MATTER-PROC", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(Matter, M, procedure, reason: "Agreed with " + MatterName);
        var general = Simple("GENERAL");
        ExcludeRule(Matter, M, general.Rule);
        ChangeRule(Matter, M, rule, ruleVersion, Jan15);

        var onDate = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Feb1);
        var internalBasis = JsonSerializer.Serialize(onDate);
        var readerView = JsonSerializer.Serialize(onDate.ForReaders());

        internalBasis.Should().Contain(M.ToString("D"), "control: the internal basis does name the deciding record");
        readerView.Should().Contain("Procedure MATTER-PROC").And.Contain("GENERAL");
        foreach (var form in new[] { M.ToString("D"), M.ToString("N"), M.ToString("B"), MatterName })
        {
            readerView.Should().NotContainEquivalentOf(form);
        }

        readerView.Should().NotContainEquivalentOf(P.ToString("D"), "no record id at all, not even the invoice's own");
    }

    // ── Rank (6.5) ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b rank: (Level, -Distance, FirmSpecific): Manual d0 > Manual d1 > Firm > All records, then firm-specific; equal ranks tie")]
    public void Rank_OrdersByLevelThenDistanceThenFirmSpecific()
    {
        var ordered = new[]
        {
            new ProcedureRank(ProcedureLevel.AllRecords, 0, false),
            new ProcedureRank(ProcedureLevel.AllRecords, 0, true),
            new ProcedureRank(ProcedureLevel.Firm, 0, true),
            new ProcedureRank(ProcedureLevel.ManualParent, 1, false),
            new ProcedureRank(ProcedureLevel.ManualParent, 1, true),
            new ProcedureRank(ProcedureLevel.ManualOwn, 0, false),
        };

        ordered.AsEnumerable().Reverse().Order().Should().Equal(ordered);
        new ProcedureRank(ProcedureLevel.ManualOwn, 0, false).CompareTo(new ProcedureRank(ProcedureLevel.ManualOwn, 0, false)).Should().Be(0);
        new ProcedureRank(ProcedureLevel.Firm, 0, false).CompareTo(new ProcedureRank(ProcedureLevel.Firm, 1, false)).Should().BePositive(
            "within a level the nearer record ranks higher");
    }
}
