// -----------------------------------------------------------------------------
// SecureRecordSetupProcedureTests.cs
//
// T256 (H7b) — the Secure Record setup steps against an in-memory environment (FakeSecureRecordSetupDataverse).
// INCOMING-145 §2.5: empty env → full setup; configured env → no writes; each refusal; the S6 strip removes injected
// privileges and keeps every file entry; dry run writes nothing. Plus §6 T2/T4, S8, S10–S14 and the sprk_noaccessentry
// prerequisite. Uses the REAL embedded codified set (26 tables), so a change to the file is exercised here too.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class SecureRecordSetupProcedureTests
{
    private static readonly SecureRecordOwnerRoleSet Set = SecureRecordOwnerRoleSet.Embedded;
    private static readonly SecureRecordSetupTarget Target = new("https://spaarke-acme.crm.dynamics.com/", "tenant", "bff-app");

    private static Task<SecureRecordSetupOutcome> RunAsync(FakeSecureRecordSetupDataverse dv, bool dryRun = false)
        => new SecureRecordSetupProcedure(dv, NullLogger.Instance)
            .RunAsync(new SecureRecordSetupRequest(Target, Set, [dv.BffAppUser, dv.MiAppUser], dryRun), CancellationToken.None);

    private static FakeSecureRecordSetupDataverse NewEnv() => FakeSecureRecordSetupDataverse.NewEnvironment(Set);

    private static async Task<FakeSecureRecordSetupDataverse> ConfiguredEnvAsync()
    {
        var dv = NewEnv();
        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Clear();
        return dv;
    }

    private static SecureRecordSetupOutcome.Refused Refused(SecureRecordSetupOutcome outcome, string code, FailureClass failureClass)
    {
        var refused = outcome.Should().BeOfType<SecureRecordSetupOutcome.Refused>().Subject;
        refused.RejectionCode.Should().Be(code);
        refused.Class.Should().Be(failureClass);
        return refused;
    }

    // ------------------------------------------------------------ empty → configured

    [Fact]
    public async Task EmptyEnvironment_IsSetUpCompletely_AndVerified()
    {
        var dv = NewEnv();

        var outcome = await RunAsync(dv);

        var applied = outcome.Should().BeOfType<SecureRecordSetupOutcome.Applied>().Subject;
        var unit = dv.Unit(Set.BusinessUnitName)!;
        unit.ParentId.Should().Be(dv.RootUnitId, "§6 T2: a DIRECT child of the root unit");
        var team = dv.Team(unit.Id, SecureRecordSetupProcedure.OwnerTeamName, isDefault: false)!;
        dv.TeamMembers[team.Id].Should().BeEmpty();
        var role = dv.Role(unit.Id, Set.RoleName)!;
        role.ParentRootRoleId.Should().Be(role.Id, "created IN the secure unit (guide §5.2), not a replica");

        var expected = Set.Tables.Select(t => dv.TableReadPrivileges[t.LogicalName][0].Id).ToHashSet();
        dv.RolePrivileges[role.Id].Keys.Should().BeEquivalentTo(expected, "exactly the file's set — the injected nine are stripped");
        dv.RolePrivileges[role.Id].Values.Should().OnlyContain(v => v.Depth == "Basic");

        dv.TeamRoles.Where(kv => kv.Value.Contains(role.Id)).Select(kv => kv.Key).Should().Equal(team.Id);
        dv.TeamRoles[dv.DefaultTeamOf(unit.Id).Id].Should().BeEmpty("the new unit's default team arrived with System Administrator (§5.5)");
        dv.ProfileTeams[dv.ReaderProfileId].Should().BeEquivalentTo(new[] { dv.RootDefaultTeamId, dv.DefaultTeamOf(unit.Id).Id });
        dv.ProfileUsers[dv.WriterProfileId].Should().BeEquivalentTo(new[] { dv.BffAppUser, dv.MiAppUser });

        applied.State.BusinessUnitId.Should().Be(unit.Id);
        applied.State.OwnerTeamId.Should().Be(team.Id);
        applied.State.RoleId.Should().Be(role.Id);
        applied.State.PrivilegeCount.Should().Be(Set.Tables.Count);
        applied.State.LockedTables.Should().BeEquivalentTo(FakeSecureRecordSetupDataverse.LockedTables);
        applied.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SecondRun_AgainstAConfiguredEnvironment_WritesNothing()
    {
        var dv = await ConfiguredEnvAsync();

        var outcome = await RunAsync(dv);

        outcome.Should().BeOfType<SecureRecordSetupOutcome.Applied>().Which.Actions.Should().BeEmpty();
        dv.Writes.Should().BeEmpty("every step reads first and writes only what is missing (INCOMING-145 §2.3)");
    }

    // ------------------------------------------------------------ dry run

    [Fact]
    public async Task DryRun_OnAnEmptyEnvironment_WritesNothing_AndPlansEveryStep()
    {
        var dv = NewEnv();

        var outcome = await RunAsync(dv, dryRun: true);

        var plan = outcome.Should().BeOfType<SecureRecordSetupOutcome.Planned>().Subject.Actions;
        dv.Writes.Should().BeEmpty();
        dv.Units.Should().ContainSingle("nothing was created");
        plan.Should().Contain(a => a.StartsWith("create business unit", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("create the Owner team", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("create the role", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("add Read at Basic", StringComparison.Ordinal));
        plan.Should().Contain(a => a.Contains("SharePoint four", StringComparison.Ordinal), "the plan names the §5.4 strip");
        plan.Should().Contain(a => a.StartsWith("give ", StringComparison.Ordinal));
        plan.Should().Contain(a => a.Contains("default team", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith($"add BFF application user {dv.BffAppUser}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRun_OnAConfiguredEnvironment_PlansNothing()
    {
        var dv = await ConfiguredEnvAsync();

        var outcome = await RunAsync(dv, dryRun: true);

        outcome.Should().BeOfType<SecureRecordSetupOutcome.Planned>().Which.Actions.Should().BeEmpty();
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_StillRefuses_WithoutWriting()
    {
        var dv = NewEnv();
        dv.ShareToPreviousOwnerOnAssign = true;

        var refused = Refused(await RunAsync(dv, dryRun: true),
            SecureRecordSetupRejectionCodes.OrgShareToPreviousOwnerOn, FailureClass.Resumable);

        refused.Diagnostic.Should().StartWith("(dry run)");
        dv.Writes.Should().BeEmpty();
    }

    // ------------------------------------------------------------ S5 / S6

    [Fact]
    public async Task Strip_RemovesInjectedAndForeignPrivileges_AndKeepsEveryFileEntry_EvenWhenNothingWasAdded()
    {
        var dv = await ConfiguredEnvAsync();
        var unit = dv.Unit(Set.BusinessUnitName)!;
        var role = dv.Role(unit.Id, Set.RoleName)!;
        var foreign = Guid.NewGuid();
        dv.RolePrivileges[role.Id][foreign] = ("prvCreatesprk_Project", "Basic");
        foreach (var (id, name) in FakeSecureRecordSetupDataverse.SharePointFour)
        {
            dv.RolePrivileges[role.Id][id] = (name, "Global");
        }

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.Writes.Should().NotContain(w => w.StartsWith("AddBasicPrivileges", StringComparison.Ordinal), "S5 had nothing to add");
        dv.Writes.Count(w => w.StartsWith("RemovePrivilege", StringComparison.Ordinal)).Should().Be(5, "S6 runs every time");
        dv.RolePrivileges[role.Id].Should().HaveCount(Set.Tables.Count);
        dv.RolePrivileges[role.Id].Values.Select(v => v.Name).Should().BeEquivalentTo(Set.Tables.Select(t => t.PrivilegeName),
            "the keep-list is the file's privilegeName set — never a literal list (prvReadsprk_Document survives)");
    }

    [Fact]
    public async Task MissingFileEntry_IsAdded_AndTheSharePointFourItReInjects_AreStripped()
    {
        var dv = await ConfiguredEnvAsync();
        var role = dv.Role(dv.Unit(Set.BusinessUnitName)!.Id, Set.RoleName)!;
        var todo = dv.TableReadPrivileges["sprk_todo"][0].Id;
        dv.RolePrivileges[role.Id].Remove(todo);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.Writes.Should().Contain("AddBasicPrivileges 1");
        dv.Writes.Count(w => w.StartsWith("RemovePrivilege", StringComparison.Ordinal)).Should().Be(4);
        dv.RolePrivileges[role.Id][todo].Depth.Should().Be("Basic");
        dv.RolePrivileges[role.Id].Should().HaveCount(Set.Tables.Count);
    }

    // ------------------------------------------------------------ S8 contain

    [Fact]
    public async Task Contain_TakesTheRoleOffTheDefaultTeam_AndSystemAdministratorOffBothTeams()
    {
        var dv = await ConfiguredEnvAsync();
        var unit = dv.Unit(Set.BusinessUnitName)!;
        var role = dv.Role(unit.Id, Set.RoleName)!;
        var defaultTeam = dv.DefaultTeamOf(unit.Id).Id;
        var named = dv.Team(unit.Id, SecureRecordSetupProcedure.OwnerTeamName, false)!.Id;
        var admin = dv.SystemAdministratorRoleIn(unit.Id);
        dv.TeamRoles[defaultTeam].UnionWith([role.Id, admin]);
        dv.TeamRoles[named].Add(admin);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.TeamRoles[defaultTeam].Should().BeEmpty();
        dv.TeamRoles[named].Should().Equal(role.Id);
    }

    // ------------------------------------------------------------ S11 / S12 / S13

    [Fact]
    public async Task ReaderProfile_GainsADefaultTeamOfAUnitCreatedLater()
    {
        var dv = await ConfiguredEnvAsync();
        var later = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(later, "acme-litigation", dv.RootUnitId));
        var laterDefault = dv.AddTeam(later, "acme-litigation", isDefault: true);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.Writes.Should().ContainSingle().Which.Should().Contain(laterDefault.ToString());
        dv.ProfileTeams[dv.ReaderProfileId].Should().Contain(laterDefault);
    }

    [Fact]
    public async Task NullSecureFlags_AreSetFalse_OnEveryLockedTable_ThenNothingRemains()
    {
        var dv = NewEnv();
        var rows = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        dv.NullFlagRows["sprk_project"].UnionWith(rows);
        dv.NullFlagRows["sprk_invoice"].Add(Guid.NewGuid());

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.Writes.Count(w => w.StartsWith("SetColumnFalse", StringComparison.Ordinal)).Should().Be(4);
        dv.NullFlagRows.Values.Should().OnlyContain(set => set.Count == 0);
    }

    [Fact]
    public async Task NullSecureFlag_ThatDoesNotStick_IsRefused()
    {
        var dv = NewEnv();
        dv.NullFlagRows["sprk_matter"].Add(Guid.NewGuid());
        dv.SetColumnFalseIsIgnored = true;

        Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable)
            .Diagnostic.Should().Contain("sprk_matter");
    }

    // ------------------------------------------------------------ S9 verify

    [Fact]
    public async Task Verify_RefusesWhenTheGrantDidNotStick()
    {
        var dv = NewEnv();
        dv.AddPrivilegesIsIgnored = true;

        Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable)
            .Diagnostic.Should().Contain("holds 0 privileges");
    }

    // ------------------------------------------------------------ refusals — none writes anything

    public static TheoryData<string, Action<FakeSecureRecordSetupDataverse>, string, FailureClass> Refusals()
    {
        var data = new TheoryData<string, Action<FakeSecureRecordSetupDataverse>, string, FailureClass>
        {
            { "no sprk_noaccessentry", dv => dv.NoAccessEntryPresent = false,
                SecureRecordSetupRejectionCodes.NoAccessEntryMissing, FailureClass.Resumable },
            { "share to previous owner on", dv => dv.ShareToPreviousOwnerOnAssign = true,
                SecureRecordSetupRejectionCodes.OrgShareToPreviousOwnerOn, FailureClass.Resumable },
            { "two roots", dv => dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), "other-root", null)),
                SecureRecordSetupRejectionCodes.RootBusinessUnitUnresolved, FailureClass.Resumable },
            { "metadata name differs", dv => dv.TableReadPrivileges["sprk_todo"][0] = new SecureSetupPrivilege(Guid.NewGuid(), "prvReadsprk_todo"),
                SecureRecordSetupRejectionCodes.PrivilegeNameMismatch, FailureClass.Resumable },
            { "table missing", dv => dv.TableReadPrivileges.Remove("sprk_budget"),
                SecureRecordSetupRejectionCodes.PrivilegeNameMismatch, FailureClass.Resumable },
            { "two units", dv =>
                {
                    dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), Set.BusinessUnitName, dv.RootUnitId));
                    dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), Set.BusinessUnitName, dv.RootUnitId));
                },
                SecureRecordSetupRejectionCodes.BusinessUnitAmbiguous, FailureClass.Resumable },
            { "unit under the customer unit", dv =>
                {
                    var customer = Guid.NewGuid();
                    dv.Units.Add(new SecureSetupBusinessUnit(customer, "acme", dv.RootUnitId));
                    dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), Set.BusinessUnitName, customer));
                },
                SecureRecordSetupRejectionCodes.BusinessUnitWrongParent, FailureClass.QuarantineRequired },
            { "user in the unit", dv => dv.UserUnit[Guid.NewGuid()] = SecureUnit(dv),
                SecureRecordSetupRejectionCodes.BusinessUnitHasUsers, FailureClass.QuarantineRequired },
            { "two named teams", dv =>
                {
                    var unit = SecureUnit(dv);
                    dv.AddTeam(unit, SecureRecordSetupProcedure.OwnerTeamName, false);
                    dv.AddTeam(unit, SecureRecordSetupProcedure.OwnerTeamName, false);
                },
                SecureRecordSetupRejectionCodes.OwnerTeamAmbiguous, FailureClass.Resumable },
            { "team has a member", dv =>
                {
                    var team = dv.AddTeam(SecureUnit(dv), SecureRecordSetupProcedure.OwnerTeamName, false);
                    dv.TeamMembers[team].Add(Guid.NewGuid());
                },
                SecureRecordSetupRejectionCodes.OwnerTeamHasMembers, FailureClass.QuarantineRequired },
            { "two roles", dv =>
                {
                    var unit = SecureUnit(dv);
                    dv.AddRole(unit, Set.RoleName);
                    dv.AddRole(unit, Set.RoleName);
                },
                SecureRecordSetupRejectionCodes.RoleAmbiguous, FailureClass.Resumable },
            { "replica role", dv => dv.AddRole(SecureUnit(dv), Set.RoleName, parentRootRoleId: Guid.NewGuid()),
                SecureRecordSetupRejectionCodes.RoleIsReplica, FailureClass.Resumable },
            { "the role in the ROOT unit (before the secure unit exists)", dv => dv.AddRole(dv.RootUnitId, Set.RoleName),
                SecureRecordSetupRejectionCodes.RoleIsReplica, FailureClass.Resumable },
            { "privilege at Global", dv =>
                {
                    var role = dv.AddRole(SecureUnit(dv), Set.RoleName);
                    var project = dv.TableReadPrivileges["sprk_project"][0];
                    dv.RolePrivileges[role][project.Id] = (project.Name, "Global");
                },
                SecureRecordSetupRejectionCodes.PrivilegeWrongDepth, FailureClass.Resumable },
            { "reader profile missing", dv => dv.Profiles.Remove(SecureRecordSetupProcedure.ReaderProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "two writer profiles", dv => dv.AddProfile(SecureRecordSetupProcedure.WriterProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "a human in the writer profile", dv => dv.ProfileUsers[dv.WriterProfileId].Add(Guid.NewGuid()),
                SecureRecordSetupRejectionCodes.FieldWriterHasOtherMember, FailureClass.QuarantineRequired },
            { "a team in the writer profile", dv => dv.ProfileTeams[dv.WriterProfileId].Add(dv.RootDefaultTeamId),
                SecureRecordSetupRejectionCodes.FieldWriterHasOtherMember, FailureClass.QuarantineRequired },
            { "column not secured", dv => dv.SecuredColumns["sprk_matter"] = false,
                SecureRecordSetupRejectionCodes.FieldLockIncomplete, FailureClass.Resumable },
            { "reader cannot read", dv =>
                {
                    dv.FieldPermissions.RemoveAll(p => p.ProfileId == dv.ReaderProfileId && p.EntityName == "sprk_project");
                },
                SecureRecordSetupRejectionCodes.FieldLockIncomplete, FailureClass.Resumable },
            { "writer grants nothing", dv => dv.FieldPermissions.RemoveAll(p => p.ProfileId == dv.WriterProfileId),
                SecureRecordSetupRejectionCodes.FieldLockIncomplete, FailureClass.Resumable },
            { "another profile may update the flag", dv =>
                    dv.FieldPermissions.Add(new SecureSetupFieldPermission(dv.AddProfile("Matter Admins"), "sprk_matter", 4, 0, 4)),
                SecureRecordSetupRejectionCodes.FieldLockOtherWriter, FailureClass.QuarantineRequired },
            { "root default team reads projects at Deep", dv =>
                {
                    var basicUser = dv.AddRole(dv.RootUnitId, "Spaarke Basic User");
                    var project = dv.TableReadPrivileges["sprk_project"][0];
                    dv.RolePrivileges[basicUser][project.Id] = (project.Name, "Deep");
                    dv.TeamRoles[dv.RootDefaultTeamId].Add(basicUser);
                },
                SecureRecordSetupRejectionCodes.RootDefaultTeamReachesSecureUnit, FailureClass.QuarantineRequired },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EachRefusal_HasItsCodeAndClass_AndWritesNothing(
        string because, Action<FakeSecureRecordSetupDataverse> arrange, string code, FailureClass failureClass)
    {
        var dv = NewEnv();
        arrange(dv);

        var refused = Refused(await RunAsync(dv), code, failureClass);

        refused.Diagnostic.Should().NotBeNullOrWhiteSpace(because);
        refused.Actions.Should().BeEmpty(because);
        dv.Writes.Should().BeEmpty($"{because}: every refusal comes before the first write");
    }

    [Fact]
    public async Task SystemAdministratorProfile_MayWriteTheFlag_OwnerDecisionF4()
    {
        var dv = NewEnv();   // NewEnvironment grants the System Administrator profile read/create/update

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
    }

    [Fact]
    public async Task RootDefaultTeam_WithBasicOrLocalRead_IsAccepted()
    {
        var dv = NewEnv();
        var role = dv.AddRole(dv.RootUnitId, "Spaarke Basic User");
        var project = dv.TableReadPrivileges["sprk_project"][0];
        dv.RolePrivileges[role][project.Id] = (project.Name, "Local");
        dv.TeamRoles[dv.RootDefaultTeamId].Add(role);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>("Local depth stays in the root unit");
    }

    [Fact]
    public async Task DataverseFault_Propagates_AsTheSeamException()
    {
        var dv = NewEnv();
        dv.Fault = member => member == nameof(ISecureRecordSetupDataverse.FindRolesAsync)
            ? new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.RateLimited, "429")
            : null;
        dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), Set.BusinessUnitName, dv.RootUnitId));

        var act = () => RunAsync(dv);

        (await act.Should().ThrowAsync<SecureRecordSetupDataverseException>()).Which.Kind.Should().Be(SecureRecordSetupFaultKind.RateLimited);
        dv.Writes.Should().BeEmpty();
    }

    private static Guid SecureUnit(FakeSecureRecordSetupDataverse dv)
    {
        var id = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(id, Set.BusinessUnitName, dv.RootUnitId));
        dv.AddTeam(id, Set.BusinessUnitName, isDefault: true);
        return id;
    }
}
