using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084 Part A, what the two lists say (procedure model 6.1-6.6, EB-D-218, EB-D-219): the REAL
/// <see cref="RecordProcedureReadService"/> over an in-memory Dataverse. The HTTP contract (step 0, the record's 404, the 503s) is
/// <see cref="RecordProcedureEndpointsContractTests"/>.
/// </summary>
public sealed class RecordProcedureReadModelContractTests
{
    private const string Token = "caller-token";

    private static async Task<RecordProceduresView> Procedures(
        RecordProcedureRig rig, string table, Guid id, Guid? firm = null, DateOnly? date = null)
    {
        var result = await rig.Service.ListProceduresAsync(table, id, firm, date, Token, CancellationToken.None);
        result.Status.Should().Be(RecordProcedureReadStatus.Done);
        return result.Value!;
    }

    private static async Task<RecordRulesInForceView> Rules(
        RecordProcedureRig rig, string table, Guid id, Guid? firm = null, DateOnly? date = null)
    {
        var result = await rig.Service.ListRulesInForceAsync(table, id, firm, date, Token, CancellationToken.None);
        result.Status.Should().Be(RecordProcedureReadStatus.Done);
        return result.Value!;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The Procedures list: firm selection, sources, overrides, gaps
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084 procedures: billing firm F lists F's Procedures, All-records ones and manual ones on the chain, each with its sources and the date's version")]
    public async Task BillingFirm_ListsItsOwnAllRecordsAndManualProcedures()
    {
        var rig = new RecordProcedureRig();
        rig.BillingFirm(Matter, M, F);
        rig.Simple("FIRM-F", ProcedureAppliesTo.Firm, F);
        rig.Simple("FIRM-G", ProcedureAppliesTo.Firm, G);
        rig.Simple("ALL");
        rig.Assign(Matter, M, rig.Simple("MANUAL", ProcedureAppliesTo.AssignedRecordsOnly).Procedure);

        var view = await Procedures(rig, Matter, M, date: Feb1);

        view.FirmSource.Should().Be("Record");
        view.Firm.Should().Be(new FirmView(F, "Firm F"));
        view.Procedures.Select(p => p.Name).Should().BeEquivalentTo("Procedure FIRM-F", "Procedure ALL", "Procedure MANUAL");
        view.Procedures.Should().OnlyContain(p => p.Status == "InForce" && p.VersionInForce!.Number == 1 && p.VersionInForce.From == Jan1 && p.VersionInForce.To == null);
        view.Procedures.Single(p => p.Name == "Procedure ALL").Sources.Should().ContainSingle()
            .Which.Should().Match<ProcedureSourceView>(s => s.Kind == "AllRecords" && s.Winning);
        view.Procedures.Single(p => p.Name == "Procedure FIRM-F").Level.Should().Be("Firm");
        view.Procedures.Single(p => p.Name == "Procedure MANUAL").Sources.Should().ContainSingle()
            .Which.Should().Match<ProcedureSourceView>(s => s.Kind == "ManualOwn" && s.Mode == "FollowLatest" && s.RowId != null && s.Winning);
        view.Gaps.Should().BeEmpty();
    }

    [Fact(DisplayName = "084 procedures: firmId G lists G's Procedures instead of F's, and the query wins over the record's billing firm")]
    public async Task FirmIdG_ListsGsProceduresInsteadOfFs()
    {
        var rig = new RecordProcedureRig();
        rig.BillingFirm(Matter, M, F);
        rig.Simple("FIRM-F", ProcedureAppliesTo.Firm, F);
        rig.Simple("FIRM-G", ProcedureAppliesTo.Firm, G);

        var view = await Procedures(rig, Matter, M, firm: G, date: Feb1);

        view.FirmSource.Should().Be("Query");
        view.Firm!.Id.Should().Be(G);
        view.Procedures.Select(p => p.Name).Should().Equal("Procedure FIRM-G");
    }

    [Fact(DisplayName = "084 procedures: a Procedure naming firm F that is assigned manually is absent for firm G (firm condition)")]
    public async Task ManualProcedureNamingF_IsAbsentForG()
    {
        var rig = new RecordProcedureRig();
        rig.Assign(Matter, M, rig.Simple("ASSIGNED-F", ProcedureAppliesTo.AssignedRecordsOnly, F).Procedure);

        (await Procedures(rig, Matter, M, firm: F, date: Feb1)).Procedures.Select(p => p.Name).Should().Equal("Procedure ASSIGNED-F");
        (await Procedures(rig, Matter, M, firm: G, date: Feb1)).Procedures.Should().BeEmpty();
    }

    [Fact(DisplayName = "084 procedures: with no query firm and no billing firm only Procedures with no Firm are listed, and the answer says there is no firm")]
    public async Task NoFirm_ListsOnlyProceduresWithNoFirm()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-F", ProcedureAppliesTo.Firm, F);
        rig.Simple("ALL-F", ProcedureAppliesTo.AllRecords, F);
        rig.Simple("ALL");

        var view = await Procedures(rig, Matter, M, date: Feb1);

        view.FirmSource.Should().Be("None");
        view.Firm.Should().BeNull();
        view.Procedures.Select(p => p.Name).Should().Equal("Procedure ALL");
        rig.World.QueriedTables.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "084 procedures: the default date is today from the clock")]
    public async Task NoDate_IsToday()
    {
        var rig = new RecordProcedureRig();

        (await Procedures(rig, Matter, M)).Date.Should().Be(Feb10);
    }

    [Fact(DisplayName = "084 procedures: an Exclude on the record returns the Procedure with status Excluded and the row's mode, reason, who, when and distance")]
    public async Task ExcludeOnTheRecord_IsReturnedAsExcluded()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.Simple("ALL").Procedure;
        var row = rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModeExcluded, reason: "not billed here", by: Reviewer);

        var item = (await Procedures(rig, Matter, M, date: Feb1)).Procedures.Should().ContainSingle().Subject;

        item.Status.Should().Be("Excluded");
        item.Overrides.Should().ContainSingle().Which.Should().Match<ProcedureOverrideView>(o =>
            o.RowId == row && o.Mode == "Excluded" && o.Decisive && o.Distance == 0 && o.Reason == "not billed here"
            && o.ChainRecord.Id == M && o.ChainRecord.Name == "Matter M" && o.Stamp.ById == Reviewer && o.Stamp.ByName == "Rita Reviewer" && o.Stamp.On != null);
    }

    [Fact(DisplayName = "084 procedures: a Procedure from several sources lists every one, highest first, the winner marked; a parent's source names the parent")]
    public async Task SeveralSources_AreAllListed_WinnerMarked()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.Simple("ALL").Procedure;
        rig.Assign(Matter, M, procedure);
        rig.Assign(Project, P, procedure);

        var onProject = (await Procedures(rig, Project, P, date: Feb1)).Procedures.Single();

        onProject.Level.Should().Be("ManualOwn");
        onProject.Sources.Select(s => (s.Kind, s.Winning)).Should().Equal(("ManualOwn", true), ("ManualParent", false), ("AllRecords", false));
        onProject.Sources.Single(s => s.Kind == "ManualParent").Via.Should().Match<ChainRecordView>(v => v.Id == M && v.Distance == 1 && v.Name == "Matter M");
        (await Procedures(rig, Matter, M, date: Feb1)).Procedures.Single().Sources.Select(s => s.Kind).Should().Equal("ManualOwn", "AllRecords");
    }

    [Fact(DisplayName = "084 procedures: a pin shows the pinned version, the newer version it skips, and the same on its override row")]
    public async Task Pin_ShowsTheNewerVersionItSkips()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.AddProcedure("Pinned", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        rig.AddVersion(procedure, 2, Mar1, null);
        rig.AddRuleVersion(rig.AddProcedureRule(procedure, "PIN-1"), v1, Jan1);
        rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1);

        var item = (await Procedures(rig, Matter, M, date: Mar15)).Procedures.Single();

        item.Status.Should().Be("InForce");
        item.Pinned.Should().BeTrue();
        item.VersionInForce!.Number.Should().Be(1);
        item.NewerVersion!.Number.Should().Be(2);
        item.Overrides.Single().Should().Match<ProcedureOverrideView>(o => o.Mode == "Pinned" && o.PinnedVersion!.Number == 1 && o.NewerVersion!.Number == 2);
    }

    [Fact(DisplayName = "084 procedures: two d1 parents with different pins appear in the gaps array (and the row says Gap), in either order")]
    public async Task TwoParentsWithDifferentPins_AreInTheGapsArray()
    {
        var rig = new RecordProcedureRig();
        rig.World.Add(WorkAssignment, W, ("sprk_name", "Work W"), ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var procedure = rig.AddProcedure("Pinned twice", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = rig.AddVersion(procedure, 2, Mar1, null);
        var rule = rig.AddProcedureRule(procedure, "PIN-1");
        rig.AddRuleVersion(rule, v1, Jan1);
        rig.AddRuleVersion(rule, v2, Mar1, number: 2);
        rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1);
        rig.Assign(Project, P, procedure, ProcedureAssignmentEntity.ModePinned, v2);

        var view = await Procedures(rig, WorkAssignment, W, date: Mar15);

        var gap = view.Gaps.Should().ContainSingle().Subject;
        gap.Should().Match<ProcedureGapView>(g => g.ProcedureName == "Pinned twice" && g.Kind == "ConflictingParents" && g.WorkDate == Mar15);
        gap.Text.Should().NotBeNullOrWhiteSpace();
        gap.Overrides.Select(o => o.PinnedVersion!.Number).Should().BeEquivalentTo(new[] { 1, 2 });
        gap.Overrides.Should().OnlyContain(o => o.ChainRecord.Distance == 1);
        view.Procedures.Single().Status.Should().Be("Gap");
    }

    [Fact(DisplayName = "084 procedures: a Procedure whose first version opens after the date is NotYetInForce, not a gap and not applied")]
    public async Task BeforeTheFirstVersion_IsNotYetInForce()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("LATER", from: new DateOnly(2026, 5, 1));
        rig.Simple("NOW");

        var view = await Procedures(rig, Matter, M, date: Feb1);

        view.Procedures.Select(p => (p.Name, p.Status)).Should().Equal(("Procedure NOW", "InForce"), ("Procedure LATER", "NotYetInForce"));
        view.Procedures.Last().VersionInForce.Should().BeNull();
        view.Gaps.Should().BeEmpty();
    }

    [Fact(DisplayName = "084 procedures: the Procedure's type is its name")]
    public async Task Type_IsNamed()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.AddProcedure("Rates", ProcedureAppliesTo.AllRecords, type: ProcedureType.RateCard);
        rig.AddVersion(procedure, 1, Jan1, null);

        (await Procedures(rig, Matter, M, date: Feb1)).Procedures.Single().Type.Should().Be("RateCard");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The rules in force: one resolution, and the marks
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084 rules: the in-force list equals the resolver's own output for the same record, date and firm, read once")]
    public async Task InForceRules_EqualTheResolversOutput_FromOneResolution()
    {
        var rig = new RecordProcedureRig();
        var general = rig.Simple("GENERAL");
        rig.AddRuleVersion(rig.AddProcedureRule(general.Procedure, "SECOND"), general.Version, Jan1);
        var third = rig.AddProcedureRule(general.Procedure, "THIRD");
        rig.AddRuleVersion(third, general.Version, Jan1);
        rig.ExcludeRule(Matter, M, third);
        var changed = rig.AddProcedureRule(general.Procedure, "CHANGED");
        rig.ChangeRule(Matter, M, changed, rig.AddRuleVersion(changed, general.Version, Jan1), Jan15, F);
        rig.Simple("FOR-F", ProcedureAppliesTo.Firm, F);
        rig.Simple("FOR-G", ProcedureAppliesTo.Firm, G);
        var before = ResolverReads(rig);
        await rig.ResolverSays(CoreRecordKind.Matter, M, F, Feb1);
        var oneResolution = ResolverReads(rig) - before;

        before = ResolverReads(rig);
        var view = await Rules(rig, Matter, M, F, Feb1);
        var viaService = ResolverReads(rig) - before;
        var resolver = await rig.ResolverSays(CoreRecordKind.Matter, M, F, Feb1);

        view.Rules.Where(r => r.InForce).Select(r => (r.PolicyId, r.PolicyVersionId))
            .Should().Equal(resolver.Rules.Select(r => (r.Version.PolicyId, (Guid?)r.Version.PolicyVersionId)));
        view.Rules.Where(r => r.InForce).Select(r => r.Procedure!.Name).Should().Equal(resolver.Rules.Select(r => r.Procedure.ProcedureName));
        view.Rules.Where(r => !r.InForce).Select(r => r.PolicyId).Should().Equal(resolver.ExcludedRules.Where(e => !e.ReplacedByRecordRule).Select(e => e.Version.PolicyId));
        view.Rules.Should().NotBeEmpty();
        viaService.Should().Be(oneResolution, "the lists share one resolution; there is no second selection");
        view.Rules.Where(r => r.InForce).Should().OnlyContain(r => r.Name == (r.RecordRule ? "Changed rate" : "Approved rate") && r.RuleType == "ApprovedRate" && !string.IsNullOrWhiteSpace(r.ParameterSummary));
    }

    [Fact(DisplayName = "084 rules: a rule excluded for firm F is an Excluded mark with reason, firm, distance, record, who and when on F's list; on G's list it runs with no mark")]
    public async Task ExcludedMark_CarriesTheRowsFacts_AndOnlyForTheFirmItNames()
    {
        var rig = new RecordProcedureRig();
        var general = rig.Simple("GENERAL");
        var row = rig.ExcludeRule(Matter, M, general.Rule, F, reason: "client carve-out", by: Reviewer);

        var onF = (await Rules(rig, Matter, M, F, Feb1)).Rules.Should().ContainSingle().Subject;
        var onG = (await Rules(rig, Matter, M, G, Feb1)).Rules.Should().ContainSingle().Subject;

        onF.InForce.Should().BeFalse();
        onF.Code.Should().Be("GENERAL");
        onF.Marks.Should().ContainSingle().Which.Should().Match<RuleMarkView>(m =>
            m.Kind == "Excluded" && m.Reason == "client carve-out" && m.Firm!.Id == F && m.Firm.Name == "Firm F" && m.Distance == 0
            && m.ChainRecord!.Id == M && m.OverrideRowId == row && m.Stamp!.ByName == "Rita Reviewer" && m.Stamp.On != null);
        onG.InForce.Should().BeTrue();
        onG.Marks.Should().BeEmpty();
    }

    [Fact(DisplayName = "084 rules: a changed rule is one row under the base rule's code (never BRR-) with a Changed mark naming the record rule, the version it is based on and the base rule; no Stale yet")]
    public async Task ChangedMark_NamesTheRecordRuleAndItsBase()
    {
        var rig = new RecordProcedureRig();
        var general = rig.Simple("GENERAL");
        var recordRule = rig.ChangeRule(Matter, M, general.Rule, general.RuleVersion, Jan15, F, "BRR-F", reason: "cap raised");

        var view = await Rules(rig, Matter, M, F, Feb1);

        var item = view.Rules.Should().ContainSingle("the changed base rule is shown by the record rule, not twice").Subject;
        item.Should().Match<RuleInForceView>(r => r.InForce && r.RecordRule && r.PolicyId == recordRule && r.Code == "GENERAL" && r.Name == "Changed rate");
        item.Marks.Select(m => m.Kind).Should().Equal("Changed");
        item.Marks.Single().Should().Match<RuleMarkView>(m =>
            m.RecordRuleId == recordRule && m.BasedOn!.Id == general.RuleVersion && m.BasedOn.Number == 1 && m.BaseCode == "GENERAL"
            && m.Reason == "cap raised" && m.Firm!.Id == F && m.ChainRecord!.Id == M);
        JsonText(view).Should().NotContain("BRR-");
    }

    [Fact(DisplayName = "084 rules: a changed rule whose base has a newer version in force is Stale (based-on is not the base's current version); before that version it is not")]
    public async Task StaleMark_FiresOnlyOnceTheBaseMovedOn()
    {
        var rig = new RecordProcedureRig();
        var general = rig.Simple("GENERAL");
        rig.ChangeRule(Matter, M, general.Rule, general.RuleVersion, Jan15, code: "BRR-M");
        var v2 = rig.AddRuleVersion(general.Rule, general.Version, new DateOnly(2026, 2, 15), number: 2);

        var before = (await Rules(rig, Matter, M, F, Feb1)).Rules.Single();
        var after = (await Rules(rig, Matter, M, F, Mar15)).Rules.Single();

        before.Marks.Select(m => m.Kind).Should().Equal("Changed");
        after.Marks.Select(m => m.Kind).Should().Equal("Changed", "Stale");
        after.Marks.Single(m => m.Kind == "Stale").Should().Match<RuleMarkView>(m =>
            m.BasedOn!.Id == general.RuleVersion && m.BasedOn.Number == 1 && m.CurrentBase!.Id == v2 && m.CurrentBase.Number == 2);
    }

    [Fact(DisplayName = "084 rules: a record rule whose base rule is no longer in force on the date is a BaseRetired row that does not run; on a date the base still runs it is not")]
    public async Task BaseRetiredMark_FiresWhereTheBaseIsGone()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        rig.AddVersion(procedure, 2, Mar1, null);
        var dropped = rig.AddProcedureRule(procedure, "DROPPED"); // published under v1 only
        var droppedVersion = rig.AddRuleVersion(dropped, v1, Jan1);
        var recordRule = rig.ChangeRule(Matter, M, dropped, droppedVersion, Jan15, code: "BRR-DROPPED");

        var running = (await Rules(rig, Matter, M, F, Feb1)).Rules.Single();
        var stranded = (await Rules(rig, Matter, M, F, Mar15)).Rules.Single();

        running.InForce.Should().BeTrue();
        running.Marks.Select(m => m.Kind).Should().Equal("Changed");
        // One row per changed rule, keyed by the record rule's id (as an in-force changed rule is); the base rule's id is in the mark's code only.
        stranded.Should().Match<RuleInForceView>(r => !r.InForce && r.RecordRule && r.Code == "DROPPED" && r.PolicyId == recordRule && r.PolicyId != dropped);
        stranded.Marks.Select(m => m.Kind).Should().Equal("Changed", "BaseRetired");
        stranded.Marks.Single(m => m.Kind == "BaseRetired").Should().Match<RuleMarkView>(m =>
            m.RecordRuleId == recordRule && m.BasedOn!.Id == droppedVersion && m.BaseCode == "DROPPED");
    }

    [Fact(DisplayName = "084 rules (K2): a base-retired record rule shows the version of its CURRENT episode, the one the resolver counts, not the latest version of an earlier removed change")]
    public async Task BaseRetired_ShowsTheCurrentEpisodesVersion_AsTheResolverCountsIt()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        rig.AddVersion(procedure, 2, Mar1, null);
        var dropped = rig.AddProcedureRule(procedure, "DROPPED"); // published under v1 only, so it is gone on Mar15
        var baseOld = rig.AddRuleVersion(dropped, v1, Jan1);
        var baseNew = rig.AddRuleVersion(dropped, v1, Jan1, number: 2);
        // Episode 1: a change, in force from Feb15, based on the first base version. It is removed (its Exclude row deleted).
        var first = rig.ChangeRuleRows(Project, P, dropped, baseOld, new DateOnly(2026, 2, 15), code: "BRR-DROPPED");
        rig.World.Remove(ProcedureAssignmentEntity.Entity, first.Exclude);
        // Episode 2: the same record rule changed again (a newer Exclude row), in force from Jan15 (EARLIER than episode 1's version), based on the second base version.
        var episodeStart = rig.Tick();
        rig.ExcludeRule(Project, P, dropped, created: episodeStart);
        var second = rig.AddRuleVersion(first.RecordRule, null, Jan15, number: 2, basedOn: baseNew, created: episodeStart);

        // The resolver's own answer while the base still runs (Feb 20): only the current episode counts.
        var resolver = await rig.ResolverSays(CoreRecordKind.Project, P, F, new DateOnly(2026, 2, 20));
        var resolved = resolver.Rules.Should().ContainSingle(r => r.RecordRule != null).Subject;
        resolved.Version.PolicyVersionId.Should().Be(second, "the resolver counts the current episode only");
        resolved.Version.BasedOnVersionId.Should().Be(baseNew);

        var stranded = (await Rules(rig, Project, P, date: Mar15)).Rules.Single();

        stranded.Marks.Select(m => m.Kind).Should().Equal("Changed", "BaseRetired");
        stranded.Marks.Single(m => m.Kind == "BaseRetired").BasedOn.Should().Match<PolicyVersionRefView>(b => b.Id == baseNew && b.Number == 2,
            "BaseRetired must name the version the resolver would have run, not the latest version of a removed change");
        stranded.Marks.Single(m => m.Kind == "Changed").BasedOn!.Id.Should().Be(baseNew);
    }

    [Fact(DisplayName = "084 rules (K2): a base-retired record rule whose current change has no version at all (a state the BFF never writes) is Unavailable, as the resolver faults on it, never a guess from an older change")]
    public async Task BaseRetired_WithNoVersionInTheCurrentEpisode_IsUnavailable()
    {
        var rig = new RecordProcedureRig();
        var procedure = rig.AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        rig.AddVersion(procedure, 2, Mar1, null);
        var dropped = rig.AddProcedureRule(procedure, "DROPPED");
        var droppedVersion = rig.AddRuleVersion(dropped, v1, Jan1);
        var first = rig.ChangeRuleRows(Project, P, dropped, droppedVersion, Jan15, code: "BRR-DROPPED");
        rig.World.Remove(ProcedureAssignmentEntity.Entity, first.Exclude);
        rig.ExcludeRule(Project, P, dropped, created: rig.Tick()); // a newer change, and no version created for it

        var result = await rig.Service.ListRulesInForceAsync(Project, P, null, Mar15, Token, CancellationToken.None);

        result.Status.Should().Be(RecordProcedureReadStatus.Unavailable);
        result.Value.Should().BeNull();
    }

    [Fact(DisplayName = "084 rules: the marks array is open and carries only the four kinds (the outranked mark is task 084e)")]
    public async Task Marks_AreFourKindsOnly()
    {
        var rig = new RecordProcedureRig();
        var general = rig.Simple("GENERAL");
        rig.ExcludeRule(Matter, M, rig.AddProcedureRule(general.Procedure, "OTHER"), F);
        rig.ChangeRule(Matter, M, general.Rule, general.RuleVersion, Jan15, code: "BRR-1");

        var kinds = (await Rules(rig, Matter, M, F, Mar15)).Rules.SelectMany(r => r.Marks).Select(m => m.Kind).Distinct();

        kinds.Should().BeSubsetOf(new[] { "Excluded", "Changed", "Stale", "BaseRetired" });
    }

    [Fact(DisplayName = "084: every row the answer exposes is in the ONE batched rights check, with the caller established first")]
    public async Task EveryExposedRow_IsInTheOneBatchedCheck()
    {
        var rig = new RecordProcedureRig();
        rig.BillingFirm(Project, P, F);
        var a = rig.Simple("RULE-A");
        var assignment = rig.Assign(Matter, M, a.Procedure, by: Reviewer);

        await Procedures(rig, Project, P, date: Feb1);

        rig.Probe.CallerLookups.Should().Be(1);
        rig.Probe.Batches.Should().ContainSingle();
        rig.Probe.Asked.Should().BeEquivalentTo(new (string, Guid)[]
        {
            ("sprk_projects", P), ("sprk_matters", M), (ProcedureEntity.EntitySet, a.Procedure), (ProcedureEntity.VersionEntitySet, a.Version),
            (ProcedureAssignmentEntity.EntitySet, assignment), ("systemusers", Reviewer),
            // Not ("sprk_organizations", F): F is the record's billing firm, a lookup value on a row already checked (fix round 3, F-D).
        });
    }

    [Fact(DisplayName = "084 rules: every row a changed rule and a base-retired record rule expose is in the ONE batched check, exactly")]
    public async Task EveryRowOfAChangedAndABaseRetiredRule_IsInTheOneBatchedCheck()
    {
        var rig = new RecordProcedureRig();
        var s = rig.SeedChangedAndBaseRetired();

        var view = await Rules(rig, Project, P, date: Mar15);

        view.Rules.Select(r => (r.InForce, r.Code, r.PolicyId)).Should().BeEquivalentTo(new[]
        {
            (true, "GENERAL", s.Changed.RecordRule), (false, "DROPPED", s.Retired.RecordRule),
        });
        rig.Probe.CallerLookups.Should().Be(1);
        rig.Probe.Batches.Should().ContainSingle();
        rig.Probe.Asked.Should().BeEquivalentTo(new (string, Guid)[]
        {
            ("sprk_projects", P), ("sprk_matters", M), (ProcedureEntity.EntitySet, s.Procedure), (ProcedureEntity.VersionEntitySet, s.Version),
            // every version of the Procedure, not only the one in force: the choice of version derives from the whole set (fix round 3, F-B)
            (ProcedureEntity.VersionEntitySet, s.FirstVersion),
            // the changed rule: the base rule, the version it is based on, the base's version in force, the record rule, its version and set, the Exclude row
            ("sprk_policies", s.ChangedBase), ("sprk_policyversions", s.ChangedBaseV1), ("sprk_policyversions", s.ChangedBaseV2),
            ("sprk_policies", s.Changed.RecordRule), ("sprk_policyversions", s.Changed.RuleVersion), ("sprk_rulesets", s.Changed.RuleSet),
            (ProcedureAssignmentEntity.EntitySet, s.Changed.Exclude),
            // the base-retired record rule: the base rule, the record rule, its set, the record rule's latest version (basedOn is read from it:
            // fix round 3, F-A), the version it is based on, the Exclude row
            ("sprk_policies", s.RetiredBase), ("sprk_policies", s.Retired.RecordRule), ("sprk_rulesets", s.Retired.RuleSet),
            ("sprk_policyversions", s.Retired.RuleVersion), ("sprk_policyversions", s.RetiredBaseVersion), (ProcedureAssignmentEntity.EntitySet, s.Retired.Exclude),
        });
    }

    // The tables only the resolver reads (the service reads Procedures and override rows once more, for a Type and a creator).
    private static int ResolverReads(RecordProcedureRig rig) => rig.World.QueriedTables.Count(t => t is RuleSet or PolicyVersion);

    private static string JsonText(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}
