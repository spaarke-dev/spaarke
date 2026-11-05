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
    private static readonly DateOnly Jan20 = new(2026, 1, 20);
    private static readonly DateOnly Feb1 = new(2026, 2, 1);
    private static readonly DateOnly Feb15 = new(2026, 2, 15);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Apr1 = new(2026, 4, 1);
    private static readonly DateOnly Apr15 = new(2026, 4, 15);
    private static readonly DateOnly May1 = new(2026, 5, 1);
    private static readonly DateOnly Jun1 = new(2026, 6, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Mock<IGenericEntityService> _entities;
    private readonly Dictionary<Guid, Guid> _setOf = new();
    private DateTime _created = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

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

    private Guid AddProcedure(string? name, ProcedureAppliesTo? appliesTo, Guid? firm = null, int status = ProcedureEntity.StatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.StatusColumn, Option(status)),
        };
        if (name is not null) columns.Add((ProcedureEntity.NameColumn, name));
        if (appliesTo is { } a) columns.Add((ProcedureEntity.AppliesToColumn, Option((int)a)));
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

    /// <summary>The next <c>createdon</c>: each call is a minute later, so a later write is always the newer one.</summary>
    private DateTime Tick() => _created = _created.AddMinutes(1);

    private Guid AddRule(Guid set, string code, bool enabled = true, Guid? id = null)
    {
        id ??= Guid.NewGuid();
        _world.Add(Policy, id.Value,
            ("sprk_policycode", code), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", enabled), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
        return id.Value;
    }

    private Guid AddProcedureRule(Guid procedure, string code, bool enabled = true) => AddRule(_setOf[procedure], code, enabled);

    private Guid AddRuleVersion(
        Guid policy, Guid? procedureVersion, DateOnly from, int number = 1, bool published = true, Guid? basedOn = null,
        RuleType type = RuleType.ApprovedRate, DateTime? created = null)
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
        if (created is { } c) columns.Add(("createdon", c));
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

    private Guid ExcludeRule(string? table, Guid record, Guid policy, Guid? firm = null, DateTime? created = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, policy)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
        };
        if (firm is { } f) columns.Add((ProcedureAssignmentEntity.FirmColumn, Ref("sprk_organization", f)));
        if (created is { } c) columns.Add(("createdon", c));
        return AddRow(table, record, columns.ToArray());
    }

    /// <summary>The record's record-scoped set under the id the BFF derives for it (one per core table, record and firm or none).</summary>
    private Guid AddRecordSet(string? table, Guid record, Guid? firm, int? scope = null)
    {
        var id = table is null ? Guid.NewGuid() : ProcedureAssignmentService.RecordRuleSetIdFor(table, record, firm);
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

    /// <summary>The record rule of <paramref name="basePolicy"/> in the record's set, under the id the BFF derives for it (089c, D-202).</summary>
    private Guid AddRecordRule(string table, Guid record, Guid? firm, Guid basePolicy, string code)
    {
        var set = AddRecordSet(table, record, firm);
        return AddRule(set, code, id: ProcedureAssignmentService.RecordRulePolicyIdFor(set, basePolicy));
    }

    /// <summary>
    /// A changed rule (089c): the Exclude row of the base rule plus the record rule's first version based on <paramref name="baseVersion"/>
    /// (null: the link is cleared), written at <paramref name="created"/> (default: the next minute) in one transaction as the BFF writes it.
    /// </summary>
    private Guid ChangeRule(
        string table, Guid record, Guid basePolicy, Guid? baseVersion, DateOnly from, Guid? firm = null, string code = "BRR-1", DateTime? created = null)
    {
        var at = created ?? Tick();
        ExcludeRule(table, record, basePolicy, firm, at);
        var recordRule = AddRecordRule(table, record, firm, basePolicy, code);
        AddRuleVersion(recordRule, null, from, basedOn: baseVersion, created: at);
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

    [Fact(DisplayName = "082b sources (F3, O-20): an All-records Procedure naming firm F applies to F's invoices and not to G's, on a matter and with no core record")]
    public async Task AllRecordsProcedure_NamingAFirm_AppliesOnlyToThatFirmsInvoices()
    {
        Simple("ALL-F", ProcedureAppliesTo.AllRecords, F);
        var noCoreF = await ProcedureResolver.ReadAsync(_entities.Object, NullLogger.Instance, null, F, CancellationToken.None);
        var noCoreG = await ProcedureResolver.ReadAsync(_entities.Object, NullLogger.Instance, null, G, CancellationToken.None);

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().Equal("ALL-F");
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty("the Procedure names firm F");
        noCoreF.RulesOn(Feb1).Rules.Select(r => r.Version.PolicyCode).Should().Equal("ALL-F");
        noCoreG.RulesOn(Feb1).Rules.Should().BeEmpty();
        noCoreG.RulesOn(Feb1).Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b sources (F3, O-20): an All-records Procedure naming firm F that is also assigned on the record still does not apply to G's invoice")]
    public async Task AllRecordsProcedure_NamingAFirm_AlsoAssigned_StillAppliesOnlyToThatFirmsInvoices()
    {
        var (procedure, _, _, _) = Simple("ALL-F", ProcedureAppliesTo.AllRecords, F);
        Assign(Matter, M, procedure);

        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty("the Procedure names firm F, whichever source brings it");
        var onF = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1);
        onF.Rules.Select(r => r.Version.PolicyCode).Should().Equal("ALL-F");
        onF.Procedures.Single().Sources.Should().Be(ProcedureSources.AllRecords | ProcedureSources.Manual);
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
        AddRuleVersion(AddRecordRule(Matter, M, G, Guid.NewGuid(), "BRR-DELETED-BASE"), null, Jan1, basedOn: Guid.NewGuid(), created: Tick());

        (await CodesOn(CoreRecordKind.Matter, M, F, Feb1)).Should().BeEmpty();
        (await CodesOn(CoreRecordKind.Matter, M, G, Feb1)).Should().BeEmpty();
    }

    [Fact(DisplayName = "082b record rules (D-203 must-fire / must-not-fire): on an earlier date a change's Exclude keeps its base, while a plain Exclude of another rule still removes its base")]
    public async Task OnAnEarlierDate_TheChangeKeepsItsBase_AndAPlainExcludeStillRemovesIt()
    {
        var changed = Simple("CHANGED");
        var plain = Simple("PLAIN");
        ChangeRule(Matter, M, changed.Rule, changed.RuleVersion, Mar1, code: "BRR-C");
        ExcludeRule(Matter, M, plain.Rule);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var early = resolution.RulesOn(Feb15);
        early.Rules.Select(r => r.Version.PolicyCode).Should().Equal("CHANGED");
        early.ExcludedRules.Should().ContainSingle().Which.Should().Match<ExcludedRule>(e => e.Version.PolicyCode == "PLAIN" && !e.ReplacedByRecordRule);

        var later = resolution.RulesOn(Mar15);
        later.Rules.Select(r => r.Version.PolicyCode).Should().Equal("BRR-C");
        later.ExcludedRules.Select(e => e.Version.PolicyCode).Should().BeEquivalentTo("CHANGED", "PLAIN");
    }

    [Fact(DisplayName = "082b record rules (F1, D-203 (5)-(7)): a change removed and made again: the removed change's version never comes back; the base runs until the new version's from-date")]
    public async Task RecordRule_RemovedAndMadeAgain_NeverRevivesTheRemovedVersion()
    {
        var general = Simple("GENERAL");
        // Episode 1: the change is made (v1 from Jan 15, 10%) and removed on Mar 1: the Exclude row is deleted and the record rule switched Off.
        var first = Tick();
        var firstRow = ExcludeRule(Matter, M, general.Rule, created: first);
        var recordRule = AddRecordRule(Matter, M, null, general.Rule, "BRR-M");
        AddRuleVersion(recordRule, null, Jan15, number: 1, basedOn: general.RuleVersion, created: first);
        _world.Remove(ProcedureAssignmentEntity.Entity, firstRow);

        // Episode 2: the same rule is changed again on Jun 1 (v2, 5%): a new Exclude row, the same record rule switched On, a new version.
        var second = Tick();
        ExcludeRule(Matter, M, general.Rule, created: second);
        AddRuleVersion(recordRule, null, Jun1, number: 2, basedOn: general.RuleVersion, created: second);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        foreach (var date in new[] { Jan20, Apr1 })
        {
            var onDate = resolution.RulesOn(date);
            onDate.Rules.Should().ContainSingle(because: $"{date}").Which.Should().Match<ResolvedRule>(r =>
                r.Version.PolicyVersionId == general.RuleVersion && r.RecordRule == null, "v1 is never used once v2 exists: the base runs");
            onDate.ExcludedRules.Should().BeEmpty();
        }

        var after = resolution.RulesOn(Jul1);
        after.Rules.Single().Version.VersionNumber.Should().Be(2);
        after.Rules.Single().RecordRule.Should().NotBeNull();
        after.ExcludedRules.Single().ReplacedByRecordRule.Should().BeTrue();
    }

    [Fact(DisplayName = "082b record rules (F1, D-203 (5)-(7)): an edit inside one episode is not a removal: v2 applies between its from-date and v3's, v3 from its own")]
    public async Task RecordRule_EditedWithinOneEpisode_AppliesEachVersionFromItsDate()
    {
        var general = Simple("GENERAL");
        var recordRule = ChangeRule(Matter, M, general.Rule, general.RuleVersion, Jan15, code: "BRR-M"); // v1, the episode's first
        AddRuleVersion(recordRule, null, Mar1, number: 2, basedOn: general.RuleVersion, created: Tick());
        AddRuleVersion(recordRule, null, May1, number: 3, basedOn: general.RuleVersion, created: Tick());
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        resolution.RulesOn(Jan1).Rules.Single().RecordRule.Should().BeNull("before the episode's first from-date the base runs");
        resolution.RulesOn(Feb1).Rules.Single().Version.VersionNumber.Should().Be(1);
        resolution.RulesOn(Mar15).Rules.Single().Version.VersionNumber.Should().Be(2);
        resolution.RulesOn(Jun1).Rules.Single().Version.VersionNumber.Should().Be(3);
    }

    [Fact(DisplayName = "082b record rules (D-203 (8b), same second): a version written in the Exclude row's own transaction counts when the two createdon stamps are equal; one a second before it does not")]
    public async Task RecordRule_VersionWithTheSameCreatedOnAsItsExcludeRow_Counts_OneSecondEarlierDoesNot()
    {
        var general = Simple("GENERAL");
        var stamp = new DateTime(2026, 1, 1, 9, 30, 15, DateTimeKind.Utc);
        ExcludeRule(Matter, M, general.Rule, created: stamp);
        var recordRule = AddRecordRule(Matter, M, null, general.Rule, "BRR-TIE");
        AddRuleVersion(recordRule, null, Jan15, number: 1, basedOn: general.RuleVersion, created: stamp.AddSeconds(-1)); // an older change, a second before
        AddRuleVersion(recordRule, null, Mar1, number: 2, basedOn: general.RuleVersion, created: stamp); // the row's own transaction: the same stamp
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var beforeV2 = resolution.RulesOn(Feb1);
        var fromV2 = resolution.RulesOn(Mar15);

        beforeV2.Rules.Single().Should().Match<ResolvedRule>(r => r.Version.PolicyVersionId == general.RuleVersion && r.RecordRule == null,
            "the version a second older than the row is a removed change's: the base runs until the tied version's from-date");
        fromV2.Rules.Single().Should().Match<ResolvedRule>(r => r.Version.PolicyCode == "BRR-TIE" && r.Version.VersionNumber == 2 && r.RecordRule != null);
    }

    [Theory(DisplayName = "082b record rules (re-check K2): an enabled record rule none of whose versions is in the current episode (all older than its Exclude row, or with no createdon) faults; the base never runs in its place")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordRule_WithNoVersionInTheCurrentEpisode_Faults(bool olderThanTheRow)
    {
        var general = Simple("GENERAL");
        var rowAt = Tick();
        ExcludeRule(Matter, M, general.Rule, created: rowAt);
        var recordRule = AddRecordRule(Matter, M, null, general.Rule, "BRR-STALE");
        AddRuleVersion(recordRule, null, Jan15, basedOn: general.RuleVersion, created: olderThanTheRow ? rowAt.AddMinutes(-5) : null);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var resolve = () => resolution.RulesOn(Feb1);

        resolve.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*none was created on or after*");
    }

    [Fact(DisplayName = "082b record rules (re-check K2, must-not-fire): a date before the current episode's first effective-from is not a fault: the episode has a version, and the base runs")]
    public async Task RecordRule_BeforeTheEpisodesFirstEffectiveFrom_IsNotAFault()
    {
        var general = Simple("GENERAL");
        var recordRule = ChangeRule(Matter, M, general.Rule, general.RuleVersion, Mar1, code: "BRR-LATER");
        AddRuleVersion(recordRule, null, Jun1, number: 2, basedOn: general.RuleVersion, created: Tick());
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var before = resolution.RulesOn(Feb1);

        before.Rules.Single().Should().Match<ResolvedRule>(r => r.Version.PolicyVersionId == general.RuleVersion && r.RecordRule == null);
        resolution.RulesOn(Apr15).Rules.Single().Version.PolicyCode.Should().Be("BRR-LATER");
    }

    [Theory(DisplayName = "082b record rules (re-check K3): an enabled record rule found by its derived id with no Exclude row of its own faults; it is not read as a change with every version in force")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordRule_WithNoPairedExcludeRow_Faults(bool plainExcludeForAnotherFirmContext)
    {
        var general = Simple("GENERAL");
        var firm = plainExcludeForAnotherFirmContext ? F : (Guid?)null;
        if (plainExcludeForAnotherFirmContext)
        {
            ExcludeRule(Matter, M, general.Rule); // a plain Exclude for every firm: not the Firm-record change's row (its firm is F)
        }

        var recordRule = AddRecordRule(Matter, M, firm, general.Rule, "BRR-NOROW");
        AddRuleVersion(recordRule, null, Jan15, basedOn: general.RuleVersion, created: Tick());
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var resolve = () => resolution.RulesOn(Feb1);

        resolve.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*no Exclude row*");
    }

    [Theory(DisplayName = "082b record rules (K1): a record rule is paired by its derived id; a cleared based-on link, or one naming another rule's version, faults and is never read as a plain Exclude")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordRule_WithABrokenBasedOnLink_Faults(bool namesAnotherRule)
    {
        var general = Simple("GENERAL");
        var other = Simple("OTHER");
        ChangeRule(Matter, M, general.Rule, namesAnotherRule ? other.RuleVersion : null, Jan15, code: "BRR-BROKEN");
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var resolve = () => resolution.RulesOn(Feb1);

        resolve.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*does not name a version of its base rule*");
    }

    // ── Versions (7.2) ───────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082b versions: v1's window uses v1's Published rules, v2's window v2's; a rule dropped by v2 applies only in v1's window (enabled is required but not sufficient)")]
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
        var withdrawn = AddVersion(procedure, 2, Feb1, Mar1, ProcedureEntity.VersionStatusRetired);
        AddRuleVersion(AddProcedureRule(procedure, "HOLEY-V2"), withdrawn, Feb1); // it was in force: a Published rule sits under it (D-203 (9))
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

    [Fact(DisplayName = "082b versions (D-203 Q2): a pin dated before the pinned version's window takes the pinned version's EARLIEST policy version")]
    public async Task Pin_BeforeItsOwnWindow_TakesTheEarliestPolicyVersionUnderIt()
    {
        var procedure = AddProcedure("Pinned early", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var rule = AddProcedureRule(procedure, "EARLY");
        AddRuleVersion(rule, v1, Jan1);
        var first = AddRuleVersion(rule, v2, Mar1, number: 2);
        AddRuleVersion(rule, v2, Apr15, number: 3); // a correction inside v2
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v2);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var onDate = resolution.RulesOn(Feb1);

        onDate.Rules.Single().Version.PolicyVersionId.Should().Be(first, "work dated before v2's first policy version takes that first one");
        onDate.Procedures.Single().Should().Match<AppliedProcedure>(p => p.ProcedureVersionId == v2 && p.Pinned);
        resolution.RulesOn(Mar15).Rules.Single().Version.PolicyVersionId.Should().Be(first);
        resolution.RulesOn(May1).Rules.Single().Version.VersionNumber.Should().Be(3, "inside its window the pinned version's correction applies from its date");
    }

    [Theory(DisplayName = "082b versions (D-203 Q2): a pin dated before the Procedure's first version opened is not applicable, whatever it names (the F2-d test runs first); after it the pin decides")]
    [InlineData(ProcedureEntity.VersionStatusActive, null)]
    [InlineData(ProcedureEntity.VersionStatusRetired, ProcedureGapKind.PinnedVersionRetired)]
    [InlineData(ProcedureEntity.VersionStatusDraft, ProcedureGapKind.PinnedVersionNotInForce)]
    public async Task Pin_BeforeTheProceduresFirstVersion_IsNotApplicable(int pinnedStatus, ProcedureGapKind? gapAfter)
    {
        var procedure = AddProcedure("Pinned too early", ProcedureAppliesTo.AllRecords);
        var first = AddVersion(procedure, 1, Mar1, null);
        AddRuleVersion(AddProcedureRule(procedure, "TOO-EARLY"), first, Mar1);
        var pinned = pinnedStatus == ProcedureEntity.VersionStatusActive ? first : AddVersion(procedure, 2, Mar15, null, pinnedStatus);
        Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, pinned);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var before = resolution.RulesOn(Jan15);
        var after = resolution.RulesOn(Apr15);

        before.Rules.Should().BeEmpty();
        before.Gaps.Should().BeEmpty("not applicable before the Procedure's first version, pinned or not");
        before.Procedures.Should().BeEmpty();
        if (gapAfter is { } kind)
        {
            after.Gaps.Should().ContainSingle().Which.Kind.Should().Be(kind, "control: after that date the pin's own outcome decides");
        }
        else
        {
            after.Rules.Select(r => r.Version.PolicyCode).Should().Equal("TOO-EARLY");
        }
    }

    // ── Retired versions are gaps, not 'not applicable' (F1, model 3.1 I-P5, D-203) ──────────────────────────

    [Fact(DisplayName = "082b gaps (F1): retiring the first version leaves its window a gap, not 'not applicable'; before it opened is still not applicable")]
    public async Task RetiringTheFirstVersion_LeavesItsWindowAGap()
    {
        var procedure = AddProcedure("First retired", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusRetired); // superseded on Mar 1, then retired
        AddRuleVersion(AddProcedureRule(procedure, "V1-RULE"), v1, Jan1); // it was in force: a Published rule sits under it (D-203 (9))
        var v2 = AddVersion(procedure, 2, Mar1, null);
        AddRuleVersion(AddProcedureRule(procedure, "V2-RULE"), v2, Mar1);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var inWindow = resolution.RulesOn(Feb1);

        inWindow.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g =>
            g.ProcedureId == procedure && g.WorkDate == Feb1 && g.Kind == ProcedureGapKind.HoleBetweenVersions);
        inWindow.Rules.Should().BeEmpty();
        var current = resolution.RulesOn(Mar15);
        current.Gaps.Should().BeEmpty();
        current.Rules.Select(r => r.Version.PolicyCode).Should().Equal("V2-RULE");
        var before = resolution.RulesOn(new DateOnly(2025, 12, 31));
        before.Gaps.Should().BeEmpty("work dated before the first version opened is not applicable");
        before.Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b gaps (F1): when the only version is Retired, every date at or after its from-date is a gap; a date before it is not applicable")]
    public async Task TheOnlyVersionRetired_IsAGapFromItsFromDate()
    {
        var procedure = AddProcedure("Only retired", ProcedureAppliesTo.AllRecords);
        var only = AddVersion(procedure, 1, Feb1, null, ProcedureEntity.VersionStatusRetired);
        AddRuleVersion(AddProcedureRule(procedure, "ONLY-RULE"), only, Feb1); // it was in force: a Published rule sits under it (D-203 (9))
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        foreach (var date in new[] { Feb1, Mar15, Jul1 })
        {
            resolution.RulesOn(date).Gaps.Should().ContainSingle(because: $"{date}").Which.Should().Match<ProcedureGap>(g =>
                g.ProcedureId == procedure && g.Kind == ProcedureGapKind.HoleBetweenVersions);
        }

        var before = resolution.RulesOn(Jan15);
        before.Gaps.Should().BeEmpty();
        before.Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "082b gaps (F1): when the only version is Retired and had an end, work after the end is a gap too (nothing follows it)")]
    public async Task TheOnlyVersionRetired_WithAnEnd_IsAGapAfterTheEnd()
    {
        var procedure = AddProcedure("Only retired, ended", ProcedureAppliesTo.AllRecords);
        var only = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusRetired);
        AddRuleVersion(AddProcedureRule(procedure, "ENDED-RULE"), only, Jan1); // it was in force: a Published rule sits under it (D-203 (9))

        (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Apr15).Gaps.Should().ContainSingle().Which.Kind.Should().Be(ProcedureGapKind.AfterLastVersion);
    }

    // ── A Retired version opens a window only if it was in force (round 2, D-203 (5a), (9)) ─────────────────

    [Fact(DisplayName = "082b gaps (round 2, D-203 (9), must-fire): retiring an Active version that had Published policy versions leaves its window a gap")]
    public async Task RetiringAnActiveVersionThatHadPublishedRules_IsAGap()
    {
        var procedure = AddProcedure("Retired after use", ProcedureAppliesTo.AllRecords);
        var version = AddVersion(procedure, 1, Feb1, null, ProcedureEntity.VersionStatusRetired); // was Active, then retired
        AddRuleVersion(AddProcedureRule(procedure, "WAS-IN-FORCE"), version, Feb1);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var inWindow = resolution.RulesOn(Mar15);

        inWindow.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g =>
            g.ProcedureId == procedure && g.Kind == ProcedureGapKind.HoleBetweenVersions);
        inWindow.Rules.Should().BeEmpty();
        resolution.RulesOn(Jan15).Gaps.Should().BeEmpty("work dated before the version opened is not applicable");
    }

    [Fact(DisplayName = "082b gaps (round 2, D-203 (9), must-not-fire): a dated Draft that was retired without ever being published opens no window: not applicable, no gap")]
    public async Task RetiringANeverPublishedDatedDraft_IsNotApplicable_NoGap()
    {
        var procedure = AddProcedure("Discarded draft", ProcedureAppliesTo.AllRecords);
        var discarded = AddVersion(procedure, 1, Feb1, null, ProcedureEntity.VersionStatusRetired); // a Draft with an effective-from, retired to discard it
        AddRuleVersion(AddProcedureRule(procedure, "NEVER-PUBLISHED"), discarded, Feb1, published: false); // its rules stayed Draft
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        foreach (var date in new[] { Jan15, Feb1, Mar15, Jul1 })
        {
            var onDate = resolution.RulesOn(date);
            onDate.Gaps.Should().BeEmpty($"a Draft that never governed anything holds nothing ({date})");
            onDate.Rules.Should().BeEmpty();
            onDate.Procedures.Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "082b gaps (round 2, D-203 (9)): a Draft v2 dated before Active v1, refused at publish and then retired, raises no gap; the same version with a Published rule (control) does")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADraftDatedBeforeTheActiveVersion_RefusedAtPublishThenRetired_RaisesNoGap(bool hadPublishedRule)
    {
        var procedure = AddProcedure("Refused draft", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Mar1, null); // Active from Mar 1
        AddRuleVersion(AddProcedureRule(procedure, "V1-RULE"), v1, Mar1);
        var v2 = AddVersion(procedure, 2, Jan1, null, ProcedureEntity.VersionStatusRetired); // dated Jan 1: publish refused (overlap), so it was retired
        AddRuleVersion(AddProcedureRule(procedure, "V2-RULE"), v2, Jan1, published: hadPublishedRule);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var early = resolution.RulesOn(Jan15);
        var current = resolution.RulesOn(Apr15);

        current.Gaps.Should().BeEmpty();
        current.Rules.Select(r => r.Version.PolicyCode).Should().Equal("V1-RULE");
        if (hadPublishedRule)
        {
            early.Gaps.Should().ContainSingle(because: "control: a version that was in force and was withdrawn leaves its window a gap").Which.Kind
                .Should().Be(ProcedureGapKind.HoleBetweenVersions);
        }
        else
        {
            early.Gaps.Should().BeEmpty("work dated before Active v1 opened is not applicable; the discarded Draft opened nothing");
            early.Rules.Should().BeEmpty();
            early.Procedures.Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "082b gaps (round 2, D-203 (9)): the gap kind counts a Retired version's window only when it was in force (hole) and reads a discarded Draft's window as past the last end")]
    [InlineData(true, ProcedureGapKind.HoleBetweenVersions)]
    [InlineData(false, ProcedureGapKind.AfterLastVersion)]
    public async Task GapKind_CountsARetiredWindow_OnlyWhenTheVersionWasInForce(bool hadPublishedRule, ProcedureGapKind expected)
    {
        var procedure = AddProcedure("Window kinds", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Feb1, ProcedureEntity.VersionStatusSuperseded);
        AddRuleVersion(AddProcedureRule(procedure, "V1-RULE"), v1, Jan1);
        var v2 = AddVersion(procedure, 2, Feb1, Mar1, ProcedureEntity.VersionStatusRetired);
        AddRuleVersion(AddProcedureRule(procedure, "V2-RULE"), v2, Feb1, published: hadPublishedRule);

        var onDate = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb15);

        onDate.Gaps.Should().ContainSingle().Which.Kind.Should().Be(expected);
    }

    [Theory(DisplayName = "082b gaps (F1, ontology-r1 D-203): retiring the whole Procedure is a normal end of life: no gap, no rules, assigned or not")]
    [InlineData(ProcedureEntity.VersionStatusActive)]
    [InlineData(ProcedureEntity.VersionStatusSuperseded)]
    [InlineData(ProcedureEntity.VersionStatusRetired)]
    public async Task RetiringTheProcedure_RaisesNoGap(int versionStatus)
    {
        var procedure = AddProcedure("Ended", ProcedureAppliesTo.AllRecords, status: ProcedureEntity.StatusRetired);
        var version = AddVersion(procedure, 1, Jan1, versionStatus == ProcedureEntity.VersionStatusActive ? null : Mar1, versionStatus);
        AddRuleVersion(AddProcedureRule(procedure, "ENDED", enabled: false), version, Jan1); // retiring a Procedure switches its rules Off
        Assign(Matter, M, procedure);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        foreach (var date in new[] { Feb15, Apr15 })
        {
            var onDate = resolution.RulesOn(date);
            onDate.Gaps.Should().BeEmpty();
            onDate.Rules.Should().BeEmpty();
            onDate.Procedures.Should().BeEmpty();
        }
    }

    [Fact(DisplayName = "082b versions (D-203): a Superseded version is in force for its own window; a Retired version's window is a gap")]
    public async Task Superseded_IsInForceForItsWindow_RetiredIsAGap()
    {
        var procedure = AddProcedure("Mixed", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, May1, ProcedureEntity.VersionStatusRetired);
        var v3 = AddVersion(procedure, 3, May1, null);
        var rule = AddProcedureRule(procedure, "MIXED");
        var r1 = AddRuleVersion(rule, v1, Jan1);
        AddRuleVersion(rule, v2, Mar1, number: 2); // the retired version was in force: a Published rule sits under it (D-203 (9))
        var r3 = AddRuleVersion(rule, v3, May1, number: 3);
        var resolution = await Resolve(CoreRecordKind.Matter, M, F);

        var superseded = resolution.RulesOn(Feb1);
        superseded.Gaps.Should().BeEmpty();
        superseded.Procedures.Single().ProcedureVersionId.Should().Be(v1);
        superseded.Rules.Single().Version.PolicyVersionId.Should().Be(r1);
        var retired = resolution.RulesOn(Mar15);
        retired.Rules.Should().BeEmpty();
        retired.Gaps.Should().ContainSingle().Which.Kind.Should().Be(ProcedureGapKind.HoleBetweenVersions);
        resolution.RulesOn(Jun1).Rules.Single().Version.PolicyVersionId.Should().Be(r3);
    }

    // ── Follow beside Pin (D-177 (3), ontology-r1 D-203 Q1) ──────────────────────────────────────────────────

    /// <summary>A work assignment W filed under both M and P, and an assigned-only Procedure: v1 [Jan 1, v1Ends) (Superseded), then v2 from Mar 1.</summary>
    private (Guid Procedure, Guid V1, Guid V2, Guid R1, Guid R2) FollowAndPinWorld(DateOnly v1Ends)
    {
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var procedure = AddProcedure("Follow and Pin", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = AddVersion(procedure, 1, Jan1, v1Ends, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var rule = AddProcedureRule(procedure, "FP-1");
        var r1 = AddRuleVersion(rule, v1, Jan1);
        var r2 = AddRuleVersion(rule, v2, Mar1, number: 2);
        return (procedure, v1, v2, r1, r2);
    }

    private void FollowAndPin(Guid procedure, Guid pinned, bool pinOnMatter)
    {
        Assign(pinOnMatter ? Matter : Project, pinOnMatter ? M : P, procedure, ProcedureAssignmentEntity.ModePinned, pinned);
        Assign(pinOnMatter ? Project : Matter, pinOnMatter ? P : M, procedure);
    }

    [Theory(DisplayName = "082b overrides (D-203 Q1): a Follow and a Pin of another version, at one distance, raise one ConflictingParents gap, whichever parent holds which")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FollowBesideAPinOfAnotherVersion_IsAConflict_InEitherOrder(bool pinOnMatter)
    {
        var (procedure, v1, _, _, _) = FollowAndPinWorld(Mar1);
        FollowAndPin(procedure, v1, pinOnMatter); // on Mar 15 the Follow takes v2, the Pin v1

        var onDate = (await Resolve(CoreRecordKind.WorkAssignment, W, F)).RulesOn(Mar15);

        onDate.Rules.Should().BeEmpty();
        onDate.Gaps.Should().ContainSingle().Which.Should().Match<ProcedureGap>(g =>
            g.ProcedureId == procedure && g.Kind == ProcedureGapKind.ConflictingParents);
    }

    [Theory(DisplayName = "082b overrides (D-203 Q1): a version beside none (the Follow falls in a hole, the Pin names a version) is a ConflictingParents gap, in either order")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AVersionBesideNone_IsAConflict_InEitherOrder(bool pinOnMatter)
    {
        var (procedure, v1, _, _, _) = FollowAndPinWorld(Feb1); // v1 ends Feb 1, v2 starts Mar 1: Feb 15 is a hole for a Follow
        FollowAndPin(procedure, v1, pinOnMatter);

        var onDate = (await Resolve(CoreRecordKind.WorkAssignment, W, F)).RulesOn(Feb15);

        onDate.Rules.Should().BeEmpty();
        onDate.Gaps.Should().ContainSingle().Which.Kind.Should().Be(ProcedureGapKind.ConflictingParents);
    }

    [Theory(DisplayName = "082b overrides (D-203 Q1): a Follow and a Pin that choose the same version raise no gap, whichever parent holds which")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FollowBesideAPinOfTheSameVersion_IsNoGap_InEitherOrder(bool pinOnMatter)
    {
        var (procedure, _, v2, _, r2) = FollowAndPinWorld(Mar1);
        FollowAndPin(procedure, v2, pinOnMatter); // on Mar 15 both choose v2

        var onDate = (await Resolve(CoreRecordKind.WorkAssignment, W, F)).RulesOn(Mar15);

        onDate.Gaps.Should().BeEmpty();
        onDate.Rules.Single().Version.PolicyVersionId.Should().Be(r2);
        onDate.Procedures.Single().Should().Match<AppliedProcedure>(p => p.ProcedureVersionId == v2 && p.Pinned,
            "pinned if any deciding row pins, whatever order the rows are read in");
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

    [Theory(DisplayName = "082b fail closed: an assigned Billing Procedure with an unknown or empty Applies to faults the resolution")]
    [InlineData(100000009)]
    [InlineData(null)]
    public async Task AnUnknownAppliesTo_Faults(int? appliesTo)
    {
        var procedure = AddProcedure("Procedure ODD", (ProcedureAppliesTo?)appliesTo);
        var version = AddVersion(procedure, 1, Jan1, null);
        AddRuleVersion(AddProcedureRule(procedure, "ODD-1"), version, Jan1);
        Assign(Matter, M, procedure);

        var read = () => Resolve(CoreRecordKind.Matter, M, F);

        await read.Should().ThrowAsync<ProcedureResolutionFaultException>().WithMessage("*Applies to*");
    }

    [Fact(DisplayName = "082b U3: a Procedure with no name reads as '(unnamed Procedure)'; its row id never reaches the reader view")]
    public async Task AnUnnamedProcedure_ReadsAsUnnamed_NotItsId()
    {
        var procedure = AddProcedure(null, ProcedureAppliesTo.AllRecords);
        var version = AddVersion(procedure, 1, Jan1, null);
        AddRuleVersion(AddProcedureRule(procedure, "NONAME-1"), version, Jan1);

        var readerView = (await Resolve(CoreRecordKind.Matter, M, F)).RulesOn(Feb1).ForReaders();

        readerView.Rules.Single().ProcedureName.Should().Be("(unnamed Procedure)");
        var json = JsonSerializer.Serialize(readerView);
        foreach (var form in new[] { procedure.ToString("D"), procedure.ToString("N") })
        {
            json.Should().NotContainEquivalentOf(form);
        }
    }

    [Fact(DisplayName = "082b fail closed (K6): an override row that names neither a Procedure nor a rule faults the resolution")]
    public async Task AnOverrideRowNamingNothing_Faults()
    {
        Simple("ALL-1");
        AddRow(Matter, M, (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)));

        var read = () => Resolve(CoreRecordKind.Matter, M, F);

        await read.Should().ThrowAsync<ProcedureResolutionFaultException>().WithMessage("*neither a Procedure nor a rule*");
    }

    [Fact(DisplayName = "082b fail closed (K6): an override row that names both a Procedure and a rule faults the resolution")]
    public async Task AnOverrideRowNamingAProcedureAndARule_Faults()
    {
        var general = Simple("ALL-1");
        AddRow(Matter, M,
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, general.Procedure)),
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, general.Rule)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)));

        var read = () => Resolve(CoreRecordKind.Matter, M, F);

        await read.Should().ThrowAsync<ProcedureResolutionFaultException>().WithMessage("*both a Procedure and a rule*");
    }

    [Fact(DisplayName = "082b K2: ToString of an override row, applied Procedure, record-rule slot and gap prints no record id, row id, Procedure id or reason")]
    public void ToString_OfTheInternalRecords_PrintsNoId()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        const string reason = "Agreed with " + MatterName;
        var parent = new CoreRecordLink(Matter, ids[0], 1);
        var row = new ProcedureOverrideRow(ids[1], ProcedureOverrideMode.Pinned, parent, ids[2], ids[3], null, ids[4], reason);
        IReadOnlyList<ProcedureOverrideRow> rows = [row];
        var applied = new AppliedProcedure(
            ids[2], "Rate card", ids[3], 2, new ProcedureRank(ProcedureLevel.ManualParent, 1, false), ProcedureSources.Manual, true, rows);
        var slot = new RecordRuleSlot(ids[5], "RATE-1", ids[6], parent, row);
        var gap = new ProcedureGap(ids[2], "Rate card", Feb1, ProcedureGapKind.ConflictingParents, rows);

        foreach (var text in new[] { row.ToString(), applied.ToString(), slot.ToString(), gap.ToString() })
        {
            text.Should().NotBeNullOrEmpty();
            foreach (var id in ids)
            {
                text.Should().NotContainEquivalentOf(id.ToString("D")).And.NotContainEquivalentOf(id.ToString("N"));
            }

            text.Should().NotContain(reason).And.NotContain(MatterName);
        }

        row.ToString().Should().Contain("Pinned").And.Contain("Distance = 1");
        applied.ToString().Should().Contain("Rate card").And.Contain("ManualParent");
        slot.ToString().Should().Contain("RATE-1");
        gap.ToString().Should().Contain("ConflictingParents");
    }

    [Fact(DisplayName = "082c C17: ToString of ExcludedProcedure, ExcludedRule, ResolvedRule, ResolvedPolicyVersion and CoreRecordLink prints no id, rule body or reason")]
    public void ToString_OfTheRecordsThe082cEvaluatorCarries_PrintsNoIdBodyOrReason()
    {
        Guid[] ids = [.. Enumerable.Range(0, 10).Select(_ => Guid.NewGuid())];
        const string reason = "Agreed with " + MatterName;
        const string body = """{"type":"TimeIncrement","incrementHours":0.1,"note":"SECRET-BODY"}""";
        const string template = "SECRET-TEMPLATE";
        var parent = new CoreRecordLink(Matter, ids[0], 1);
        var row = new ProcedureOverrideRow(ids[1], ProcedureOverrideMode.Excluded, parent, null, null, ids[2], ids[3], reason);
        IReadOnlyList<ProcedureOverrideRow> rows = [row];
        var version = new ResolvedPolicyVersion
        {
            PolicyId = ids[2], PolicyCode = "INC-1", PolicyVersionId = ids[4], RuleSetId = ids[5], VersionNumber = 3,
            RuleType = "100000005", RuleBody = body, MessageTemplate = template, ShortHeadline = template, ClauseReference = template,
            ClauseLocation = template, InForceFrom = Feb1, ProcedureVersionId = ids[6], BasedOnVersionId = ids[7],
        };
        var applied = new AppliedProcedure(
            ids[8], "Billing guidelines", ids[6], 2, new ProcedureRank(ProcedureLevel.ManualParent, 1, false), ProcedureSources.Manual, false, rows);
        var slot = new RecordRuleSlot(ids[9], "BASE-1", ids[7], parent, row);
        // K2: a record rule's code is BRR- plus its policy id, so no printed form may carry it.
        var recordRuleVersion = version with { PolicyCode = ProcedureAssignmentService.RecordRuleCode(ids[2]) };
        var texts = new Dictionary<string, string>
        {
            ["recordRuleVersion"] = recordRuleVersion.ToString(),
            ["recordRule"] = new ResolvedRule(recordRuleVersion, applied, slot).ToString(),
            ["excludedRecordRule"] = new ExcludedRule(recordRuleVersion, applied, rows, 1, ReplacedByRecordRule: false).ToString(),
            ["link"] = parent.ToString(),
            ["version"] = version.ToString(),
            ["rule"] = new ResolvedRule(version, applied, slot).ToString(),
            ["excludedRule"] = new ExcludedRule(version, applied, rows, 1, ReplacedByRecordRule: true).ToString(),
            ["excludedProcedure"] = new ExcludedProcedure(ids[8], "Billing guidelines", rows).ToString(),
        };

        foreach (var (name, text) in texts)
        {
            text.Should().NotBeNullOrEmpty(name);
            foreach (var id in ids)
            {
                text.Should().NotContainEquivalentOf(id.ToString("D"), name).And.NotContainEquivalentOf(id.ToString("N"), name);
            }

            text.Should().NotContain("SECRET", name).And.NotContain(reason, name).And.NotContain(MatterName, name).And.NotContain("incrementHours", name);
        }

        foreach (var name in new[] { "recordRuleVersion", "recordRule", "excludedRecordRule" })
        {
            texts[name].Should().Contain("record rule", name).And.NotContain(ProcedureAssignmentService.RecordRuleCodePrefix, name);
        }

        texts["link"].Should().Contain(Matter).And.Contain("Distance = 1");
        texts["version"].Should().Contain("INC-1").And.Contain("VersionNumber = 3").And.Contain("100000005");
        texts["rule"].Should().Contain("INC-1").And.Contain("Billing guidelines").And.Contain("ManualParent").And.Contain("RecordRule = True");
        texts["excludedRule"].Should().Contain("INC-1").And.Contain("Billing guidelines").And.Contain("ReplacedByRecordRule = True").And.Contain("Overrides = 1");
        texts["excludedProcedure"].Should().Contain("Billing guidelines").And.Contain("Overrides = 1");
    }

    [Fact(DisplayName = "082b K2: ToString of the records a resolution returns, built over a Secure matter's override with a reason, prints no id or reason")]
    public async Task ToString_OfResolvedRecords_PrintsNoId()
    {
        _world.SecureRoot(Matter, M);
        var assigned = Simple("MATTER-PROC", ProcedureAppliesTo.AssignedRecordsOnly);
        Assign(Matter, M, assigned.Procedure, reason: "Agreed with " + MatterName);
        var changed = Simple("CHANGED");
        ChangeRule(Matter, M, changed.Rule, changed.RuleVersion, Jan15);
        var gapped = AddProcedure("Gapped", ProcedureAppliesTo.AssignedRecordsOnly);
        AddVersion(gapped, 1, Jan1, Feb1, ProcedureEntity.VersionStatusSuperseded);
        Assign(Matter, M, gapped, reason: "Agreed with " + MatterName);

        var onDate = (await Resolve(CoreRecordKind.Project, P, F)).RulesOn(Mar15);
        var texts = new List<string>
        {
            onDate.Procedures.First(p => p.ProcedureName == "Procedure MATTER-PROC").ToString(),
            onDate.Procedures.First(p => p.ProcedureName == "Procedure MATTER-PROC").Overrides.Single().ToString(),
            onDate.Rules.Single(r => r.RecordRule is not null).RecordRule!.ToString(),
            onDate.Rules.Single(r => r.RecordRule is not null).RecordRule!.Exclude!.ToString(),
            onDate.Gaps.Single().ToString(),
            onDate.Gaps.Single().Overrides.Single().ToString(),
        };

        foreach (var text in texts)
        {
            foreach (var form in new[] { M.ToString("D"), M.ToString("N"), P.ToString("D"), assigned.Procedure.ToString("D"), assigned.Procedure.ToString("N"), gapped.ToString("D"), MatterName })
            {
                text.Should().NotContainEquivalentOf(form);
            }
        }

        texts.Should().OnlyContain(t => t.Length > 0);
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
