using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Owner decision 2026-10-29 ("records are not owned by named users; they are owned by business units/teams. Whatever the
/// ownership, the family members have the same ownership"): the follow-parents pass of the REAL
/// <see cref="SecureRootInheritance"/> re-owns an ORDINARY work assignment or project to its family's team, through the REAL
/// <see cref="SecureChildReconciler.ReownFiledRootAsync"/> (If-Match on the root's version), over the provisioning fixture's
/// <see cref="SecureChildShareWorld"/>. Report-only unless <c>SecureRoot:Inheritance:RootOwnerWritesEnabled</c> is true.
/// </summary>
[Trait("status", "uac-r2-root-family-owner")]
public class SecureRootFamilyOwnerTests : IClassFixture<ProvisionProjectTestFixture>
{
    private static readonly Guid UnitA = Guid.Parse("a0a0a0a0-1029-4029-8029-0000000000a0");
    private static readonly Guid TeamA = Guid.Parse("a0a0a0a0-1029-4029-8029-0000000000a1");
    private static readonly Guid UnitB = Guid.Parse("b0b0b0b0-1029-4029-8029-0000000000b0");
    private static readonly Guid TeamB = Guid.Parse("b0b0b0b0-1029-4029-8029-0000000000b1");
    private static readonly Guid NamedTeamB = Guid.Parse("b0b0b0b0-1029-4029-8029-0000000000b2");
    private static readonly Guid PersonB = Guid.Parse("b0b0b0b0-1029-4029-8029-0000000000b3");

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureRootFamilyOwnerTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SetConfiguration(SecureRootInheritance.RootOwnerWritesEnabledConfigKey, null);
        _fixture.SetConfiguration(SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey, null);
        _fixture.SetConfiguration(SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey, null);
        World.Add("businessunit", UnitA, ("name", "Unit A"), ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        World.Team(TeamA, UnitA, "Unit A", isDefault: true, teamType: 0);
        World.Add("businessunit", UnitB, ("name", "Unit B"), ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        World.Team(TeamB, UnitB, "Unit B", isDefault: true, teamType: 0);
        World.Team(NamedTeamB, UnitB, "Unit B Litigation", isDefault: false, teamType: 0);
        World.User(PersonB, UnitB);
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    /// <summary>Fix round (A-F1): the step writes only with BOTH switches — its own and the ordinary-moves pass's, which moves a
    /// cross-unit root's documents to the container they now expect.</summary>
    private void WritesOn()
    {
        _fixture.SetConfiguration(SecureRootInheritance.RootOwnerWritesEnabledConfigKey, "true");
        _fixture.SetConfiguration(SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey, "true");
    }

    private async Task<FollowParentsPass> PassAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .FollowParentsPassAsync("trace-root-family", null, 25, 500, CancellationToken.None);
    }

    /// <summary>An ordinary matter owned by unit A's default team.</summary>
    private Guid MatterInA()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, owningTeamId: TeamA, isSecure: false);
        return matter;
    }

    /// <summary>An ordinary work assignment owned by <paramref name="person"/> (or <paramref name="team"/>), holding
    /// <paramref name="filing"/>.</summary>
    private Guid WorkAssignment(Guid? person = null, Guid? team = null, string? container = null, Guid? id = null,
        params (string Column, string Table, Guid Id)[] filing)
    {
        var waId = id ?? Guid.NewGuid();
        _fixture.SeedWorkAssignment(waId, owningTeamId: team, containerId: container, isSecure: false,
            owningUserId: team is null ? person ?? PersonB : null);
        foreach (var (column, table, parent) in filing)
            World.Set("sprk_workassignment", waId, column, new EntityReference(table, parent));
        return waId;
    }

    private static IEnumerable<JsonElement> Lines(FollowParentsPass pass) =>
        JsonSerializer.SerializeToElement(pass.RootOwners.Changes).EnumerateArray();

    private static JsonElement LineOf(FollowParentsPass pass, Guid id) =>
        Lines(pass).Single(l => l.GetProperty("id").GetGuid() == id);

    private void ShouldBeUntouched(Guid id, FollowParentsPass pass, Guid? person = null, Guid? team = null)
    {
        if (team is { } t)
            _fixture.OwningTeamOf(id).Should().Be(t);
        else
            _fixture.OwningUserOf(id).Should().Be(person ?? PersonB);
        _fixture.ConditionalRootWrites.Should().NotContain(w => w.Id == id, "nothing is written to it");
        pass.RootOwners.Written.Should().Be(0);
    }

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Owner 2026-10-29 must fire: a person-owned work assignment filed under a matter in unit A is re-owned to A's default team (If-Match), its container kept")]
    public async Task APersonOwnedWorkAssignmentFiledUnderAMatterInA_IsReownedToA()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(container: "b!wa-container", filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();

        var pass = await PassAsync();

        _fixture.OwningTeamOf(wa).Should().Be(TeamA, "the family's answer is the matter's unit");
        _fixture.ConditionalRootWrites.Should().ContainSingle(w => w.Id == wa && w.EntitySet == "sprk_workassignments"
                                                                && w.ETag.StartsWith("W/\"", StringComparison.Ordinal));
        _fixture.ContainerIdOf(wa).Should().Be("b!wa-container", "the root's container is never changed");
        pass.RootOwners.WritesEnabled.Should().BeTrue();
        pass.RootOwners.Written.Should().Be(1);
        pass.RootOwners.CrossUnit.Should().Be(1, "unit B to unit A: its documents now expect unit A's container");
        var line = LineOf(pass, wa);
        line.GetProperty("state").GetString().Should().Be("written");
        line.GetProperty("from").GetProperty("owner").GetString().Should().Be("user");
        line.GetProperty("from").GetProperty("unit").GetGuid().Should().Be(UnitB);
        line.GetProperty("to").GetProperty("team").GetGuid().Should().Be(TeamA);
        line.GetProperty("to").GetProperty("unit").GetGuid().Should().Be(UnitA);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire (report-only, the default): the same work assignment is listed as planned and nothing is written")]
    public async Task ReportOnly_ListsThePlan_AndWritesNothing()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        pass.RootOwners.WritesEnabled.Should().BeFalse();
        pass.RootOwners.Planned.Should().Be(1);
        LineOf(pass, wa).GetProperty("state").GetString().Should().Be("planned");
        LineOf(pass, wa).GetProperty("to").GetProperty("team").GetGuid().Should().Be(TeamA);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must fire: a person-owned work assignment filed only under an invoice is re-owned to the invoice's matter's unit")]
    public async Task APersonOwnedWorkAssignmentUnderAnInvoice_IsReownedToTheInvoicesMatterUnit()
    {
        var matter = MatterInA();
        var invoice = Guid.NewGuid();
        World.Add("sprk_invoice", invoice, ("owningteam", new EntityReference("team", TeamB)),
            ("sprk_matter", new EntityReference("sprk_matter", matter)));
        var wa = WorkAssignment(filing: ("sprk_regardinginvoice", "sprk_invoice", invoice));
        WritesOn();

        await PassAsync();

        _fixture.OwningTeamOf(wa).Should().Be(TeamA, "a work assignment filed under an invoice belongs to the invoice's matter");
    }

    [Theory(DisplayName = "Fix round (B-F3): a person-owned work assignment filed only under an invoice or event with no matter or project gets the answer creation gives — the intermediate's unit")]
    [InlineData("sprk_regardinginvoice", "sprk_invoice")]
    [InlineData("sprk_regardingevent", "sprk_event")]
    public async Task AWorkAssignmentUnderAnIntermediateWithNoCore_GetsCreationsAnswer(string column, string table)
    {
        var intermediate = Guid.NewGuid();
        World.Add(table, intermediate, ("owningteam", new EntityReference("team", TeamA)),
            ("owningbusinessunit", new EntityReference("businessunit", UnitA)));
        var wa = WorkAssignment(filing: (column, table, intermediate));
        var creation = await AskCreationsQuestionAsync(column, table, intermediate);
        WritesOn();

        await PassAsync();

        creation.IsOwned.Should().BeTrue($"{creation.RefusalCode}: {creation.Reason}");
        creation.OwningTeamId.Should().Be(TeamA, "creation files it under the intermediate it names");
        _fixture.OwningTeamOf(wa).Should().Be(creation.OwningTeamId, "the step asks the question creation asks for the same row");
    }

    /// <summary>The question a BFF create asks for a work assignment holding one typed lookup (its lookups, through what an
    /// invoice or event belongs to: <c>RecordCreationService</c>'s derivation).</summary>
    private async Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution> AskCreationsQuestionAsync(string column, string table, Guid id)
    {
        using var scope = _fixture.Services.CreateScope();
        var dataverse = SecureChildShareWorld.EntitiesOver(() => World).Object;
        var lookups = new[] { KeyValuePair.Create(column, (Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent?)new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(table, id)) };
        var through = await SecureRootInheritance.OwnerLookupsThroughIntermediatesAsync(
            dataverse, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "sprk_workassignment", lookups, CancellationToken.None);
        var question = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForRow("sprk_workassignment", through.Lookups.Concat(lookups), null);
        return await new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
                dataverse, scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance)
            .ResolveOwnerAsync(question, CancellationToken.None);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must fire: a work assignment owned by unit B's default team, filed under a matter in A, is re-owned to A")]
    public async Task ADefaultTeamOfAnotherUnit_IsReownedToTheFamilysUnit()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(team: TeamB, filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();

        var pass = await PassAsync();

        _fixture.OwningTeamOf(wa).Should().Be(TeamA);
        LineOf(pass, wa).GetProperty("from").GetProperty("owner").GetString().Should().Be("defaultTeam");
    }

    [Fact(DisplayName = "Owner 2026-10-29 must fire: an UNFILED person-owned work assignment is re-owned to its person's unit default team")]
    public async Task AnUnfiledPersonOwnedWorkAssignment_IsReownedToItsPersonsUnitTeam()
    {
        var wa = WorkAssignment();
        WritesOn();

        await PassAsync();

        _fixture.OwningTeamOf(wa).Should().Be(TeamB, "records are owned by teams: the person's business-unit team");
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: an UNFILED work assignment owned by any team is left alone")]
    public async Task AnUnfiledTeamOwnedWorkAssignment_IsLeftAlone()
    {
        var wa = WorkAssignment(team: TeamB);
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass, team: TeamB);
        Lines(pass).Should().NotContain(l => l.GetProperty("id").GetGuid() == wa);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must fire: a person-owned project whose pair names a matter in A is re-owned to A, and a work assignment under it follows in the same run")]
    public async Task AProjectFiledByItsPair_IsReowned_AndItsWorkAssignmentFollowsInTheSameRun()
    {
        var matter = MatterInA();
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false, owningUserId: PersonB);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        var wa = WorkAssignment(filing: ("sprk_regardingproject", "sprk_project", project));
        WritesOn();

        var pass = await PassAsync();

        _fixture.OwningTeamOf(project).Should().Be(TeamA, "a project follows the matter its pair names");
        _fixture.OwningTeamOf(wa).Should().Be(TeamA, "the work assignment follows its project in the same run");
        pass.RootOwners.Written.Should().Be(2);
        Lines(pass).Select(l => l.GetProperty("id").GetGuid()).Should().Equal(new[] { project, wa }, "projects come first");
    }

    [Fact(DisplayName = "Decision 2026-10-30 must fire: a person-owned project with an EMPTY pair goes to its person's unit default team, and a work assignment under it follows in the same run")]
    public async Task APersonOwnedProjectWithAnEmptyPair_GoesToItsPersonsUnitTeam_AndItsWorkAssignmentFollows()
    {
        // Until task 195 writes the project pair every project's pair is empty, so a person-owned project counts as unfiled.
        // Projects are not excluded: it goes to its person's unit default team, the unit it already sits in.
        var personA = Guid.NewGuid();
        World.User(personA, UnitA);
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false, owningUserId: PersonB, owningBusinessUnitId: UnitB);
        World.Set("sprk_project", project, "sprk_regardingrecordid", "");
        var wa = WorkAssignment(person: personA, filing: ("sprk_regardingproject", "sprk_project", project));
        WritesOn();

        var pass = await PassAsync();

        _fixture.OwningTeamOf(project).Should().Be(TeamB, "an unfiled person-owned project goes to its person's unit default team");
        _fixture.OwningTeamOf(wa).Should().Be(TeamB, "the work assignment follows its project in the same run, not its own person's unit");
        pass.RootOwners.Written.Should().Be(2);
        Lines(pass).Select(l => l.GetProperty("id").GetGuid()).Should().Equal(new[] { project, wa }, "projects come first");
        var projectLine = LineOf(pass, project);
        projectLine.GetProperty("from").GetProperty("owner").GetString().Should().Be("user");
        projectLine.GetProperty("from").GetProperty("unit").GetGuid().Should().Be(UnitB);
        projectLine.GetProperty("to").GetProperty("unit").GetGuid().Should().Be(UnitB, "the same unit: visibility does not change");
        projectLine.GetProperty("reason").GetString().Should().Be("follows its person's business unit");
        LineOf(pass, wa).GetProperty("reason").GetString().Should().Be("follows its filing");
    }

    [Fact(DisplayName = "Owner 2026-10-29 report-only: a work assignment under a project planned to move is planned to the project's planned team")]
    public async Task ReportOnly_AWorkAssignmentUnderAPlannedProject_IsPlannedWithIt()
    {
        var matter = MatterInA();
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false, owningTeamId: TeamB);
        FilePair(_fixture, "sprk_project", project, matter, RecordTypeRef(_fixture, "sprk_matter"));
        var wa = WorkAssignment(filing: ("sprk_regardingproject", "sprk_project", project));

        var pass = await PassAsync();

        LineOf(pass, project).GetProperty("to").GetProperty("team").GetGuid().Should().Be(TeamA);
        LineOf(pass, wa).GetProperty("to").GetProperty("team").GetGuid().Should().Be(TeamA,
            "the plan reads the project as owned by its planned team, as the write would leave it");
        _fixture.ConditionalRootWrites.Should().BeEmpty();
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: a work assignment under a SECURE parent is the securing loop's, never this step's")]
    public async Task UnderASecureParent_Untouched()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        Lines(pass).Should().NotContain(l => l.GetProperty("id").GetGuid() == wa);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: a work assignment whose sprk_issecure is EMPTY is never touched")]
    public async Task AnEmptySecureFlag_Untouched()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        World.Set("sprk_workassignment", wa, "sprk_issecure", null);
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        Lines(pass).Should().NotContain(l => l.GetProperty("id").GetGuid() == wa);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: a work assignment owned in the Secure Record unit is never touched")]
    public async Task OwnedInTheSecureRecordUnit_Untouched()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(team: SecureTeam, filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass, team: SecureTeam);
        Lines(pass).Should().NotContain(l => l.GetProperty("id").GetGuid() == wa);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: a work assignment owned by a NAMED (non-default) team is reported, not moved")]
    public async Task ANamedOwnerTeam_IsReportedNotMoved()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(team: NamedTeamB, filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass, team: NamedTeamB);
        pass.RootOwners.Reported.Should().Be(1);
        LineOf(pass, wa).GetProperty("state").GetString().Should().Be("reported");
        LineOf(pass, wa).GetProperty("reasonCode").GetString().Should().Be(SecureRootInheritance.ReasonRootOwnerNamedTeam);
    }

    [Fact(DisplayName = "Owner 2026-10-29 must not fire: a work assignment whose filing cannot be told writes nothing and is reported in problems")]
    public async Task AnUnreadableFiling_WritesNothing_AndIsReported()
    {
        var matter = MatterInA();
        var other = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        FilePair(_fixture, "sprk_workassignment", wa, other, RecordTypeRef(_fixture, "sprk_matter")); // a pair no lookup repeats
        WritesOn();

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        pass.RootOwners.Refused.Should().Be(1);
        LineOf(pass, wa).GetProperty("state").GetString().Should().Be("refused");
        pass.Problems.Should().Contain(p => p.Contains(wa.ToString("D")) && p.Contains(SecureRootInheritance.ReasonRootOwnerUnresolved));
    }

    [Fact(DisplayName = "Owner 2026-10-29: at most MaxRootReownsPerRun roots are re-owned a run; the rest are deferred")]
    public async Task TheCapIsRespected()
    {
        var matter = MatterInA();
        var was = Enumerable.Range(0, SecureRootInheritance.MaxRootReownsPerRun + 2)
            .Select(_ => WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter))).ToList();
        WritesOn();

        var pass = await PassAsync();

        pass.RootOwners.Written.Should().Be(SecureRootInheritance.MaxRootReownsPerRun);
        pass.RootOwners.Deferred.Should().Be(2);
        was.Count(id => _fixture.OwningTeamOf(id) == TeamA).Should().Be(SecureRootInheritance.MaxRootReownsPerRun);
    }

    [Fact(DisplayName = "Owner 2026-10-29: the recent-changes emergency stop wins over RootOwnerWritesEnabled = true")]
    public async Task TheEmergencyStopWins()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();
        _fixture.SetConfiguration(SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey, "false");

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        pass.RootOwners.WritesEnabled.Should().BeFalse();
        pass.RootOwners.Planned.Should().Be(1);
    }

    // ── Fix round: switches, bounds and cursor, the If-Match race, a pair naming a deleted record ──────────────────────────

    [Fact(DisplayName = "Fix round (A-F1) must not fire: RootOwnerWritesEnabled alone (no OrdinaryMovesWritesEnabled) stays report-only and warns, naming the missing switch")]
    public async Task TheRootSwitchAlone_StaysReportOnly_AndWarns()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        _fixture.SetConfiguration(SecureRootInheritance.RootOwnerWritesEnabledConfigKey, "true");

        var pass = await PassAsync();

        ShouldBeUntouched(wa, pass);
        pass.RootOwners.WritesEnabled.Should().BeFalse("a cross-unit re-own needs the ordinary-moves pass to move its documents");
        pass.RootOwners.Planned.Should().Be(1);
        pass.RootOwners.CrossUnit.Should().Be(1);
        _fixture.Logs.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                                                          && e.Message.Contains(SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey)
                                                          && e.Message.Contains("REPORT-ONLY"));
    }

    /// <summary>A work assignment whose filing can never be told: a pair naming a matter none of its lookups repeats.</summary>
    private Guid PermanentlyRefusingWorkAssignment(Guid id)
    {
        var wa = WorkAssignment(id: id, filing: ("sprk_regardingmatter", "sprk_matter", MatterInA()));
        FilePair(_fixture, "sprk_workassignment", wa, MatterInA(), RecordTypeRef(_fixture, "sprk_matter"));
        return wa;
    }

    private static Guid Ordered(int prefix, int n) => Guid.Parse($"{prefix:x8}-1030-4030-8030-{n:x12}");

    [Fact(DisplayName = "Fix round (F2) must fire: 30 roots that always refuse, ordered first, never starve the 3 re-ownable roots after them")]
    public async Task RefusingRootsFirst_NeverStarveTheRootsAfterThem()
    {
        foreach (var n in Enumerable.Range(0, 30))
            PermanentlyRefusingWorkAssignment(Ordered(0x00000001, n));
        var matter = MatterInA();
        var reownable = Enumerable.Range(0, 3)
            .Select(n => WorkAssignment(id: Ordered(0x7fffffff, n), filing: ("sprk_regardingmatter", "sprk_matter", matter)))
            .ToList();
        WritesOn();
        var runner = new SecureRootInheritanceJobRunner(_fixture);

        for (var run = 0; run < 3 && reownable.Any(id => _fixture.OwningTeamOf(id) != TeamA); run++)
            await runner.RunAsync();

        reownable.Should().OnlyContain(id => _fixture.OwningTeamOf(id) == TeamA,
            "a refusal before the plan does not spend the run's bound, so the roots after it are reached");
    }

    [Fact(DisplayName = "Fix round (B-F2) must fire: past MaxRootResolvesPerRun the run stops, and its cursor carries the rest to the next run, which wraps round")]
    public async Task TheResolveBound_StopsTheRun_AndTheCursorCarriesTheRest()
    {
        var matter = MatterInA();
        foreach (var n in Enumerable.Range(0, SecureRootInheritance.MaxRootResolvesPerRun))
            WorkAssignment(team: TeamA, id: Ordered(0x00000001, n), filing: ("sprk_regardingmatter", "sprk_matter", matter)); // in step
        var last = WorkAssignment(id: Ordered(0x7fffffff, 0), filing: ("sprk_regardingmatter", "sprk_matter", matter));
        WritesOn();
        var runner = new SecureRootInheritanceJobRunner(_fixture);

        var first = RootOwnersOf(await runner.RunAsync());
        _fixture.OwningTeamOf(last).Should().NotBe(TeamA, "the first run stopped at its resolve bound before it");
        first.GetProperty("resolved").GetInt32().Should().Be(SecureRootInheritance.MaxRootResolvesPerRun);
        first.GetProperty("deferred").GetInt32().Should().Be(1);
        first.GetProperty("resumeAfter").GetString().Should().Be($"sprk_workassignment:{Ordered(0x00000001, SecureRootInheritance.MaxRootResolvesPerRun - 1):D}");

        var second = RootOwnersOf(await runner.RunAsync());
        _fixture.OwningTeamOf(last).Should().Be(TeamA, "the second run continued after the cursor");
        second.GetProperty("written").GetInt32().Should().Be(1);
        second.GetProperty("resolved").GetInt32().Should().Be(SecureRootInheritance.MaxRootResolvesPerRun,
            "it wrapped round to the start after the last root");
        second.GetProperty("resumeAfter").GetString().Should().Be(
            $"sprk_workassignment:{Ordered(0x00000001, SecureRootInheritance.MaxRootResolvesPerRun - 2):D}");
    }

    private static JsonElement RootOwnersOf(Spaarke.Scheduling.JobRunResult run)
    {
        using var json = JsonDocument.Parse(run.ResultJson!);
        return json.RootElement.GetProperty("followParents").GetProperty("rootOwners").Clone();
    }

    [Fact(DisplayName = "Fix round (A-K6) must not fire: a root changed between the writer's guard read and its write is not written (If-Match), and is reported")]
    public async Task ARootChangedBeforeTheWrite_IsNotWritten_AndIsReported()
    {
        var matter = MatterInA();
        var wa = WorkAssignment(filing: ("sprk_regardingmatter", "sprk_matter", matter));
        _fixture.BeforeConditionalRootWrite = (table, id) =>
            World.Set(table, id, "versionnumber", (World.ValueOf<long?>(table, id, "versionnumber") ?? 0L) + 7);
        WritesOn();

        var pass = await PassAsync();

        _fixture.OwningUserOf(wa).Should().Be(PersonB, "the conditional write was refused: nothing changed");
        _fixture.ConditionalRootWrites.Should().ContainSingle(w => w.Id == wa, "the write was attempted once, If-Match");
        pass.RootOwners.Written.Should().Be(0);
        pass.RootOwners.Refused.Should().Be(1);
        LineOf(pass, wa).GetProperty("state").GetString().Should().Be("refused");
        LineOf(pass, wa).GetProperty("reason").GetString().Should().StartWith(nameof(OrdinaryRowReownOutcome.Conflict));
        pass.Problems.Should().Contain(p => p.Contains(wa.ToString("D")) && p.Contains(SecureRootInheritance.ReasonRootOwnerNotWritten));
    }

    [Fact(DisplayName = "Fix round (B-K1) must fire: a person-owned project whose pair names a DELETED record is planned as unfiled — its person's (its own) unit, not refused")]
    public async Task AProjectWhosePairNamesADeletedRecord_IsPlannedAsUnfiled()
    {
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false, owningUserId: PersonB, owningBusinessUnitId: UnitB);
        FilePair(_fixture, "sprk_project", project, Guid.NewGuid(), RecordTypeRef(_fixture, "sprk_matter")); // no such matter

        var pass = await PassAsync();

        var line = LineOf(pass, project);
        line.GetProperty("state").GetString().Should().Be("planned");
        line.GetProperty("to").GetProperty("team").GetGuid().Should().Be(TeamB, "the pair's record is gone: filed under nothing");
        line.GetProperty("reason").GetString().Should().Be("follows its person's business unit");
        pass.RootOwners.Refused.Should().Be(0);
    }

    // ── The writer's own guards (the pass filters these first; the writer refuses them on its own) ─────────────────────────

    private async Task<OrdinaryRowReownResult> ReownAsync(string table, Guid id, Guid team)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SecureChildReconciler>()
            .ReownFiledRootAsync(table, id, team, CancellationToken.None);
    }

    [Fact(DisplayName = "ReownFiledRootAsync must fire: a person-owned ordinary work assignment is re-owned (If-Match) and reads back")]
    public async Task TheWriter_ReownsAPersonOwnedRoot()
    {
        var wa = WorkAssignment();

        var result = await ReownAsync("sprk_workassignment", wa, TeamA);

        result.Outcome.Should().Be(OrdinaryRowReownOutcome.Reowned, result.Detail);
        _fixture.OwningTeamOf(wa).Should().Be(TeamA);
        _fixture.ConditionalRootWrites.Should().ContainSingle(w => w.Id == wa);
    }

    [Theory(DisplayName = "ReownFiledRootAsync must not fire: an EMPTY or true flag, a Secure-unit target, a Secure-unit owner, a named team")]
    [InlineData("emptyFlag", OrdinaryRowReownOutcome.RefusedSecure)]
    [InlineData("flaggedSecure", OrdinaryRowReownOutcome.RefusedSecure)]
    [InlineData("secureTarget", OrdinaryRowReownOutcome.RefusedSecure)]
    [InlineData("secureOwner", OrdinaryRowReownOutcome.RefusedSecure)]
    [InlineData("namedTeam", OrdinaryRowReownOutcome.RefusedNotDefaultTeam)]
    [InlineData("sameUnitDefaultTeam", OrdinaryRowReownOutcome.RefusedNotDefaultTeam)]
    public async Task TheWriter_RefusesWithoutWriting(string shape, OrdinaryRowReownOutcome expected)
    {
        var wa = shape switch
        {
            "secureOwner" => WorkAssignment(team: SecureTeam),
            "namedTeam" => WorkAssignment(team: NamedTeamB),
            "sameUnitDefaultTeam" => WorkAssignment(team: TeamB),
            _ => WorkAssignment(),
        };
        if (shape == "emptyFlag")
            World.Set("sprk_workassignment", wa, "sprk_issecure", null);
        if (shape == "flaggedSecure")
            World.Set("sprk_workassignment", wa, "sprk_issecure", true);
        var target = shape switch
        {
            "secureTarget" => SecureTeam,
            "sameUnitDefaultTeam" => NamedTeamB, // a team of the owner's own unit B
            _ => TeamA,
        };

        var result = await ReownAsync("sprk_workassignment", wa, target);

        result.Outcome.Should().Be(expected, result.Detail);
        _fixture.ConditionalRootWrites.Should().BeEmpty("nothing is written");
    }

    [Fact(DisplayName = "ReownFiledRootAsync: a matter (or any other table) is refused by contract")]
    public async Task TheWriter_RefusesAMatter()
    {
        var act = () => ReownAsync("sprk_matter", MatterInA(), TeamB);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact(DisplayName = "Owner 2026-10-29: the job's ResultJson carries followParents.rootOwners with the plan and the cross-unit count, no ids in the counts")]
    public async Task TheJobsResultJson_CarriesTheRootOwners()
    {
        var matter = MatterInA();
        WorkAssignment(container: "b!wa-container", filing: ("sprk_regardingmatter", "sprk_matter", matter));

        var run = await new SecureRootInheritanceJobRunner(_fixture).RunAsync();

        using var json = JsonDocument.Parse(run.ResultJson!);
        var rootOwners = json.RootElement.GetProperty("followParents").GetProperty("rootOwners");
        rootOwners.GetProperty("writesEnabled").GetBoolean().Should().BeFalse();
        rootOwners.GetProperty("planned").GetInt32().Should().Be(1);
        rootOwners.GetProperty("crossUnit").GetInt32().Should().Be(1, "unit B to unit A: the ordinary-moves pass moves its documents");
        rootOwners.GetProperty("withGrants").GetInt32().Should().Be(0);
        rootOwners.GetProperty("changes").GetArrayLength().Should().Be(1);
    }

    // ── Task 195 (#2352): the project filing census (report only) ────────────────────────────────────────────────────────

    /// <summary>An N:N row of the Project ↔ Matter association (<c>sprk_project_sprk_matter</c>).</summary>
    private void Linked(Guid project, Guid matter) =>
        World.Add(SecureRootInheritance.ProjectMatterLinkEntity, Guid.NewGuid(), ("sprk_projectid", project), ("sprk_matterid", matter));

    private Guid ProjectFiledUnder(Guid? matter, Guid matterType)
    {
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, owningTeamId: TeamA, isSecure: false);
        if (matter is { } m)
            FilePair(_fixture, "sprk_project", project, m, matterType);
        return project;
    }

    [Fact(DisplayName = "Task 195 must fire: the root-owner report counts projects linked to a matter with no pair, and projects whose pair matter has no link row")]
    public async Task TheFilingCensus_CountsBothDisagreements_AndWritesNothing()
    {
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var (m1, m2, m3, m5, m6) = (MatterInA(), MatterInA(), MatterInA(), MatterInA(), MatterInA());
        var linkedNoPair = ProjectFiledUnder(null, matterType);
        Linked(linkedNoPair, m1);
        ProjectFiledUnder(m2, matterType);                     // a pair, no link row
        Linked(ProjectFiledUnder(m3, matterType), m3);         // both agree: neither count
        Linked(ProjectFiledUnder(m6, matterType), m5);         // linked to one matter, filed under another: the second count
        ProjectFiledUnder(null, matterType);                   // neither: no count

        var pass = await PassAsync();

        pass.RootOwners.ProjectsWithMatterLinkButNoPair.Should().Be(1);
        pass.RootOwners.ProjectsWhosePairMatterHasNoLink.Should().Be(2);
        _fixture.ConditionalRootWrites.Should().BeEmpty("report only");
        World.QueriedTables.Should().Contain(SecureRootInheritance.ProjectMatterLinkEntity);
        var rootOwners = JsonSerializer.SerializeToElement(pass.RootOwners, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        rootOwners.GetProperty("projectsWithMatterLinkButNoPair").GetInt32().Should().Be(1, "the job's ResultJson carries it");
    }

    [Fact(DisplayName = "Task 195 must not fire: when the link table cannot be read the counts are not reported (null), a problem line says so, and the pass completes")]
    public async Task TheFilingCensus_WhenTheLinkTableCannotBeRead_ReportsNoCounts()
    {
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        ProjectFiledUnder(MatterInA(), matterType);
        World.FailingQueriesOf(SecureRootInheritance.ProjectMatterLinkEntity);

        var pass = await PassAsync();

        pass.RootOwners.ProjectsWithMatterLinkButNoPair.Should().BeNull();
        pass.RootOwners.ProjectsWhosePairMatterHasNoLink.Should().BeNull();
        pass.Problems.Should().Contain(p => p.Contains("project filing census"));
    }
}
