// -----------------------------------------------------------------------------
// SecureRecordSetupProcedureTests.cs
//
// T256 (H7b) — the Secure Record setup steps against an in-memory environment (FakeSecureRecordSetupDataverse).
// INCOMING-145 §2.5: empty env → full setup; configured env → no writes; each refusal; the S6 strip removes injected
// privileges and keeps every file entry; dry run writes nothing. Plus §6 T2/T4, S8, S10–S14, the sprk_noaccessentry
// prerequisite, and S15–S18 (the contact identity-binding profiles' memberships and lock, INCOMING-141 / T255). Uses
// the REAL embedded codified set (26 tables), so a change to the file is exercised here too.
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

    private static Task<SecureRecordSetupOutcome> RunAsync(FakeSecureRecordSetupDataverse dv, bool dryRun = false, Guid? customerUnit = null)
        => new SecureRecordSetupProcedure(dv, NullLogger.Instance)
            .RunAsync(new SecureRecordSetupRequest(Target, Set, [dv.BffAppUser, dv.MiAppUser], customerUnit ?? dv.CustomerUnitId, dryRun),
                CancellationToken.None);

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
        dv.ProfileTeams[dv.ReaderProfileId].Should().BeEquivalentTo(
            new[] { dv.RootDefaultTeamId, dv.CustomerDefaultTeamId, dv.DefaultTeamOf(unit.Id).Id });
        dv.ProfileUsers[dv.WriterProfileId].Should().BeEquivalentTo(new[] { dv.BffAppUser, dv.MiAppUser });
        dv.ProfileTeams[dv.LinkReaderProfileId].Should().BeEquivalentTo(
            new[] { dv.RootDefaultTeamId, dv.CustomerDefaultTeamId, dv.DefaultTeamOf(unit.Id).Id },
            "S16: every default team reads the identity-binding columns");
        dv.ProfileUsers[dv.LinkReaderProfileId].Should().BeEmpty();
        dv.ProfileUsers[dv.LinkWriterProfileId].Should().BeEquivalentTo(new[] { dv.BffAppUser, dv.MiAppUser },
            "S17: the BFF's application users, and only them, write the bindings");
        dv.ProfileTeams[dv.LinkWriterProfileId].Should().BeEmpty();

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
        dv.Units.Should().HaveCount(2, "nothing was created (the root and the customer unit only)");
        plan.Should().Contain(a => a.StartsWith("create business unit", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("create the Owner team", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("create the role", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith("add Read at Basic", StringComparison.Ordinal));
        plan.Should().Contain(a => a.Contains("SharePoint four", StringComparison.Ordinal), "the plan names the §5.4 strip");
        plan.Should().Contain(a => a.StartsWith("give ", StringComparison.Ordinal));
        plan.Should().Contain(a => a.Contains("default team", StringComparison.Ordinal));
        plan.Should().Contain(a => a.StartsWith($"add BFF application user {dv.BffAppUser}", StringComparison.Ordinal));
        plan.Should().Contain($"add BFF application user {dv.BffAppUser} to '{SecureRecordSetupProcedure.IdentityLinkWriterProfileName}'");
        plan.Should().Contain($"add BFF application user {dv.MiAppUser} to '{SecureRecordSetupProcedure.IdentityLinkWriterProfileName}'");
        plan.Should().Contain($"add default team 'spaarke-acme' ({dv.RootDefaultTeamId}) to '{SecureRecordSetupProcedure.IdentityLinkReaderProfileName}'");
        plan.Should().Contain($"add the new unit's default team to '{SecureRecordSetupProcedure.IdentityLinkReaderProfileName}'");
        dv.ProfileUsers[dv.LinkWriterProfileId].Should().BeEmpty("a dry run associates nobody");
        dv.ProfileTeams[dv.LinkReaderProfileId].Should().BeEmpty();
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

    [Fact]
    public async Task Contain_MatchesSystemAdministratorByRoleTemplateId_WhateverItIsNamed()
    {
        var dv = await ConfiguredEnvAsync();
        var unit = dv.Unit(Set.BusinessUnitName)!;
        var defaultTeam = dv.DefaultTeamOf(unit.Id).Id;
        var named = dv.Team(unit.Id, SecureRecordSetupProcedure.OwnerTeamName, false)!.Id;
        var renamedAdmin = dv.AddRole(unit.Id, "Platform Administrator",
            roleTemplateId: SecureRecordSetupProcedure.SystemAdministratorRoleTemplateId);
        dv.TeamRoles[defaultTeam].Add(renamedAdmin);
        dv.TeamRoles[named].Add(renamedAdmin);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.TeamRoles[defaultTeam].Should().NotContain(renamedAdmin, "the role is System Administrator by its template id");
        dv.TeamRoles[named].Should().NotContain(renamedAdmin);
    }

    [Fact]
    public async Task Contain_LeavesACustomRoleNamedSystemAdministrator()
    {
        var dv = await ConfiguredEnvAsync();
        var unit = dv.Unit(Set.BusinessUnitName)!;
        var defaultTeam = dv.DefaultTeamOf(unit.Id).Id;
        var lookalike = dv.AddRole(unit.Id, SecureRecordSetupProcedure.SystemAdministratorRoleName);   // no template id
        dv.TeamRoles[defaultTeam].Add(lookalike);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>(
            "a custom role that only carries the name is not the platform role, so S9 does not report it either");

        dv.TeamRoles[defaultTeam].Should().Contain(lookalike);
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_RefusesWhenSystemAdministratorRemovalDidNotStick_MatchedByTemplateId()
    {
        var dv = await ConfiguredEnvAsync();
        var unit = dv.Unit(Set.BusinessUnitName)!;
        var defaultTeam = dv.DefaultTeamOf(unit.Id).Id;
        var renamedAdmin = dv.AddRole(unit.Id, "Platform Administrator",
            roleTemplateId: SecureRecordSetupProcedure.SystemAdministratorRoleTemplateId);
        dv.TeamRoles[defaultTeam].Add(renamedAdmin);
        dv.TeamRoleDisassociationsIgnored = true;

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain($"still holds '{SecureRecordSetupProcedure.SystemAdministratorRoleName}'");
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

        dv.Writes.Should().BeEquivalentTo(
            [$"AssociateProfileTeam {dv.ReaderProfileId} {laterDefault}", $"AssociateProfileTeam {dv.LinkReaderProfileId} {laterDefault}"],
            "S11 and S16 each add the new default team, and nothing else is written");
        dv.ProfileTeams[dv.ReaderProfileId].Should().Contain(laterDefault);
        dv.ProfileTeams[dv.LinkReaderProfileId].Should().Contain(laterDefault);
    }

    // ------------------------------------------------------------ S15–S18 identity-link profiles (INCOMING-141)

    [Fact]
    public async Task IdentityLinkMemberships_OnAnEnvironmentConfiguredBeforeThem_AreAddedAlone()
    {
        // An environment H7b set up before S15–S18 existed: everything but the identity-link memberships is in place.
        var dv = await ConfiguredEnvAsync();
        dv.ProfileTeams[dv.LinkReaderProfileId].Clear();
        dv.ProfileUsers[dv.LinkWriterProfileId].Clear();
        var defaultTeams = dv.Teams.Where(t => t.IsDefault).Select(t => t.Id).ToArray();

        var outcome = await RunAsync(dv);

        outcome.Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Should().BeEquivalentTo(
            defaultTeams.Select(id => $"AssociateProfileTeam {dv.LinkReaderProfileId} {id}")
                .Concat([$"AssociateProfileUser {dv.LinkWriterProfileId} {dv.BffAppUser}", $"AssociateProfileUser {dv.LinkWriterProfileId} {dv.MiAppUser}"]),
            "only the missing identity-link memberships are written");

        dv.Writes.Clear();
        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Which.Actions.Should().BeEmpty();
        dv.Writes.Should().BeEmpty("the second run writes nothing");
    }

    [Fact]
    public async Task IdentityLinkWriter_WithOneBffUserAlready_GainsOnlyTheOther()
    {
        var dv = await ConfiguredEnvAsync();
        dv.ProfileUsers[dv.LinkWriterProfileId].Remove(dv.MiAppUser);

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.Writes.Should().Equal($"AssociateProfileUser {dv.LinkWriterProfileId} {dv.MiAppUser}");
    }

    [Fact]
    public async Task DryRun_OnAnEnvironmentMissingOnlyTheIdentityLinkMemberships_PlansThem_AndWritesNothing()
    {
        var dv = await ConfiguredEnvAsync();
        dv.ProfileTeams[dv.LinkReaderProfileId].Clear();
        dv.ProfileUsers[dv.LinkWriterProfileId].Clear();

        var plan = (await RunAsync(dv, dryRun: true)).Should().BeOfType<SecureRecordSetupOutcome.Planned>().Subject.Actions;

        dv.Writes.Should().BeEmpty();
        plan.Should().OnlyContain(a => a.EndsWith($"'{SecureRecordSetupProcedure.IdentityLinkReaderProfileName}'", StringComparison.Ordinal)
                                       || a.EndsWith($"'{SecureRecordSetupProcedure.IdentityLinkWriterProfileName}'", StringComparison.Ordinal));
        plan.Should().HaveCount(dv.Teams.Count(t => t.IsDefault) + 2);
    }

    [Fact]
    public async Task IdentityLinkWriter_ForeignMember_IsRefusedEvenOnADryRun_WithoutWriting()
    {
        var dv = NewEnv();
        dv.ProfileUsers[dv.LinkWriterProfileId].Add(Guid.NewGuid());

        var refused = Refused(await RunAsync(dv, dryRun: true),
            SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember, FailureClass.QuarantineRequired);

        refused.Diagnostic.Should().StartWith("(dry run)");
        refused.Actions.Should().BeEmpty();
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task IdentityLinkWriter_ForeignMember_OnAConfiguredEnvironment_IsRefused_AndNothingIsRemoved()
    {
        var dv = await ConfiguredEnvAsync();
        var foreign = Guid.NewGuid();
        dv.ProfileUsers[dv.LinkWriterProfileId].Add(foreign);

        var refused = Refused(await RunAsync(dv),
            SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember, FailureClass.QuarantineRequired);

        refused.Diagnostic.Should().Contain(foreign.ToString()).And.Contain(SecureRecordSetupProcedure.IdentityLinkWriterProfileName);
        dv.Writes.Should().BeEmpty();
        dv.ProfileUsers[dv.LinkWriterProfileId].Should().Contain(foreign, "a member is never removed here — it is an operator decision");
    }

    [Fact]
    public async Task IdentityLinkLock_IgnoresAnotherTableCarryingTheSameColumnName()
    {
        var dv = NewEnv();
        dv.ColumnPermissions("sprk_externalobjectid")
            .Add(new SecureSetupFieldPermission(dv.AddProfile("Portal Admins"), "sprk_externalparty", 4, 4, 4));

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>(
            "the lock is contact.sprk_externalobjectid — a same-named column on another table is not part of it");
    }

    [Fact]
    public async Task Verify_RefusesWhenAnIdentityLinkMembershipDidNotStick()
    {
        var dv = NewEnv();
        dv.ProfileAssociationsIgnored.Add(dv.LinkWriterProfileId);

        Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable)
            .Diagnostic.Should().Contain($"'{SecureRecordSetupProcedure.IdentityLinkWriterProfileName}' members are users []");
    }

    [Fact]
    public async Task Verify_RefusesWhenAnIdentityLinkReaderMembershipDidNotStick()
    {
        var dv = NewEnv();
        dv.ProfileAssociationsIgnored.Add(dv.LinkReaderProfileId);

        Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable)
            .Diagnostic.Should().Contain($"are not in '{SecureRecordSetupProcedure.IdentityLinkReaderProfileName}'");
    }

    // ------------------------------------------------------------ S19–S21 (ISS-020 / #1565)

    [Fact]
    public async Task StandingGrantProfile_GainsTheBffApplicationUsers_AndNobodyElse()
    {
        var dv = NewEnv();
        var administrator = Guid.NewGuid();
        dv.ProfileUsers[dv.StandingGrantProfileId].Add(administrator);   // a grant-privileged human: an operator decision, kept

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();

        dv.ProfileUsers[dv.StandingGrantProfileId].Should().BeEquivalentTo(new[] { administrator, dv.BffAppUser, dv.MiAppUser });
        dv.ProfileTeams[dv.StandingGrantProfileId].Should().BeEmpty();
        dv.Writes.Where(w => w.Contains(dv.StandingGrantProfileId.ToString(), StringComparison.Ordinal))
            .Should().OnlyContain(w => w.StartsWith("AssociateProfileUser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StandingGrantProfile_WithOneBffUserAlready_GainsOnlyTheOther()
    {
        var dv = await ConfiguredEnvAsync();
        dv.ProfileUsers[dv.StandingGrantProfileId].Remove(dv.MiAppUser);

        var outcome = await RunAsync(dv);

        outcome.Should().BeOfType<SecureRecordSetupOutcome.Applied>().Which.Actions.Should().ContainSingle()
            .Which.Should().Be($"add BFF application user {dv.MiAppUser} to '{SecureRecordSetupProcedure.StandingGrantProfileName}'");
        dv.Writes.Should().ContainSingle().Which.Should().Be($"AssociateProfileUser {dv.StandingGrantProfileId} {dv.MiAppUser}");
    }

    [Fact]
    public async Task StandingGrantProfile_OnAnEnvironmentConfiguredBeforeS19_IsAddedAlone_AndADryRunPlansItWithoutWriting()
    {
        var dv = await ConfiguredEnvAsync();
        dv.ProfileUsers[dv.StandingGrantProfileId].Clear();

        var plan = (await RunAsync(dv, dryRun: true)).Should().BeOfType<SecureRecordSetupOutcome.Planned>().Subject.Actions;

        dv.Writes.Should().BeEmpty();
        plan.Should().BeEquivalentTo(new[]
        {
            $"add BFF application user {dv.BffAppUser} to '{SecureRecordSetupProcedure.StandingGrantProfileName}'",
            $"add BFF application user {dv.MiAppUser} to '{SecureRecordSetupProcedure.StandingGrantProfileName}'",
        });

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Should().OnlyContain(w => w.StartsWith("AssociateProfileUser", StringComparison.Ordinal));
        dv.ProfileUsers[dv.StandingGrantProfileId].Should().BeEquivalentTo(new[] { dv.BffAppUser, dv.MiAppUser });
    }

    [Fact]
    public async Task Verify_RefusesWhenAStandingGrantMembershipDidNotStick()
    {
        var dv = NewEnv();
        dv.ProfileAssociationsIgnored.Add(dv.StandingGrantProfileId);

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.VerifyFailed, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain($"are not members of '{SecureRecordSetupProcedure.StandingGrantProfileName}'");
    }

    [Fact]
    public async Task NoAccessEntryRoleSplit_IsNeverRepairedHere()
    {
        var dv = NewEnv();
        dv.RolePrivileges[dv.CoreUserRoleId][dv.NoAccessEntryReadPrivilegeId] = ("prvReadsprk_noaccessentry", "Global");

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain("Set-NoAccessEntryRolePrivileges.ps1");
        dv.RolePrivileges[dv.CoreUserRoleId].Should().ContainKey(dv.NoAccessEntryReadPrivilegeId, "a refusal changes nothing");
        dv.Writes.Should().BeEmpty();
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

            // ---- T259 (ISS-010, owner 2026-10-09): §6 T1/T3 — the customer's unit and the BFF's application users ----
            { "customer unit missing", dv => dv.Units.RemoveAll(u => u.Id == dv.CustomerUnitId),
                SecureRecordSetupRejectionCodes.CustomerBusinessUnitMissing, FailureClass.QuarantineRequired },
            { "customer unit under another unit (Deep there could reach a sibling's records)", dv =>
                {
                    var other = Guid.NewGuid();
                    dv.Units.Add(new SecureSetupBusinessUnit(other, "Holding", dv.RootUnitId));
                    dv.Units.RemoveAll(u => u.Id == dv.CustomerUnitId);
                    dv.Units.Add(new SecureSetupBusinessUnit(dv.CustomerUnitId, FakeSecureRecordSetupDataverse.CustomerUnitName, other));
                },
                SecureRecordSetupRejectionCodes.CustomerBusinessUnitWrongParent, FailureClass.QuarantineRequired },
            { "BFF application user in the ROOT unit", dv => dv.UserUnit[dv.BffAppUser] = dv.RootUnitId,
                SecureRecordSetupRejectionCodes.AppUserOutsideCustomerBusinessUnit, FailureClass.QuarantineRequired },
            { "managed-identity application user in the Secure Record unit", dv => dv.UserUnit[dv.MiAppUser] = SecureUnit(dv),
                SecureRecordSetupRejectionCodes.AppUserOutsideCustomerBusinessUnit, FailureClass.QuarantineRequired },
            { "BFF application user does not exist", dv => dv.UserUnit.Remove(dv.BffAppUser),
                SecureRecordSetupRejectionCodes.AppUserOutsideCustomerBusinessUnit, FailureClass.QuarantineRequired },
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
            { "column not secured", dv => dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("sprk_matter", "sprk_issecure")] = false,
                SecureRecordSetupRejectionCodes.FieldLockIncomplete, FailureClass.Resumable },
            { "the BFF-managed READER profile may update the flag (every default team is its member)", dv =>
                {
                    dv.FieldPermissions.RemoveAll(p => p.ProfileId == dv.ReaderProfileId && p.EntityName == "sprk_matter");
                    dv.FieldPermissions.Add(new SecureSetupFieldPermission(dv.ReaderProfileId, "sprk_matter", 4, 0, 4));
                },
                SecureRecordSetupRejectionCodes.FieldLockOtherWriter, FailureClass.QuarantineRequired },
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

            // ---- S15–S18: the contact identity-binding profiles (INCOMING-141) ----
            { "identity-link reader profile missing", dv => dv.Profiles.Remove(SecureRecordSetupProcedure.IdentityLinkReaderProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "identity-link writer profile missing", dv => dv.Profiles.Remove(SecureRecordSetupProcedure.IdentityLinkWriterProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "two identity-link writer profiles", dv => dv.AddProfile(SecureRecordSetupProcedure.IdentityLinkWriterProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "a human in the identity-link writer profile", dv => dv.ProfileUsers[dv.LinkWriterProfileId].Add(Guid.NewGuid()),
                SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember, FailureClass.QuarantineRequired },
            { "a BFF user plus a stranger in the identity-link writer profile", dv =>
                    dv.ProfileUsers[dv.LinkWriterProfileId].UnionWith([dv.BffAppUser, Guid.NewGuid()]),
                SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember, FailureClass.QuarantineRequired },
            { "a team in the identity-link writer profile", dv => dv.ProfileTeams[dv.LinkWriterProfileId].Add(dv.RootDefaultTeamId),
                SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember, FailureClass.QuarantineRequired },
            { "contact.sprk_externalobjectid not secured", dv =>
                    dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalobjectid")] = false,
                SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable },
            { "systemuser.sprk_primarycontact absent", dv =>
                    dv.SecuredColumns.Remove(FakeSecureRecordSetupDataverse.ColumnKey("systemuser", "sprk_primarycontact")),
                SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable },
            { "identity-link reader cannot read systemuser.sprk_primarycontact", dv =>
                    dv.ColumnPermissions("sprk_primarycontact").RemoveAll(p => p.ProfileId == dv.LinkReaderProfileId),
                SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable },
            { "identity-link writer cannot update contact.sprk_externalobjectid", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_externalobjectid");
                    permissions.RemoveAll(p => p.ProfileId == dv.LinkWriterProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.LinkWriterProfileId, "contact", 4, 4, 0));
                },
                SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable },
            { "identity-link writer grants on another table only", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_externalobjectid");
                    permissions.RemoveAll(p => p.ProfileId == dv.LinkWriterProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.LinkWriterProfileId, "sprk_externalparty", 4, 4, 4));
                },
                SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable },
            { "another profile may create contact.sprk_externalobjectid", dv =>
                    dv.ColumnPermissions("sprk_externalobjectid")
                        .Add(new SecureSetupFieldPermission(dv.AddProfile("Contact Admins"), "contact", 4, 4, 0)),
                SecureRecordSetupRejectionCodes.IdentityLinkLockOtherWriter, FailureClass.QuarantineRequired },
            { "the identity-link READER profile may update systemuser.sprk_primarycontact", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_primarycontact");
                    permissions.RemoveAll(p => p.ProfileId == dv.LinkReaderProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.LinkReaderProfileId, "systemuser", 4, 0, 4));
                },
                SecureRecordSetupRejectionCodes.IdentityLinkLockOtherWriter, FailureClass.QuarantineRequired },

            // ---- S19–S21: the standing-grant environment (ISS-020 / #1565) ----
            { "standing-grant profile missing", dv => dv.Profiles.Remove(SecureRecordSetupProcedure.StandingGrantProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "two standing-grant profiles", dv => dv.AddProfile(SecureRecordSetupProcedure.StandingGrantProfileName),
                SecureRecordSetupRejectionCodes.FieldProfileUnresolved, FailureClass.Resumable },
            { "contact.sprk_standinggrant not secured", dv =>
                    dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_standinggrant")] = false,
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "contact.sprk_standinggrant absent", dv =>
                    dv.SecuredColumns.Remove(FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_standinggrant")),
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "standing-grant profile grants no Read", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_standinggrant");
                    permissions.RemoveAll(p => p.ProfileId == dv.StandingGrantProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.StandingGrantProfileId, "contact", 0, 4, 4));
                },
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "standing-grant profile grants Read on another table only", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_standinggrant");
                    permissions.RemoveAll(p => p.ProfileId == dv.StandingGrantProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.StandingGrantProfileId, "sprk_externalparty", 4, 4, 4));
                },
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "Standing Grant Administrators cannot create the standing grant (4/0/4)", dv =>
                    Replace(dv, SecureRecordSetupProcedure.StandingGrantColumn, dv.StandingGrantProfileId, 4, 0, 4),
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "Standing Grant Administrators cannot update the standing grant (4/4/0)", dv =>
                    Replace(dv, SecureRecordSetupProcedure.StandingGrantColumn, dv.StandingGrantProfileId, 4, 4, 0),
                SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable },
            { "Readers may update the standing grant (4/0/4)", dv =>
                    dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn).Add(new SecureSetupFieldPermission(
                        dv.ReaderProfileId, SecureRecordSetupProcedure.StandingGrantTable, 4, 0, 4)),
                SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter, FailureClass.QuarantineRequired },
            { "Writers may update the standing grant (4/0/4)", dv =>
                    dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn).Add(new SecureSetupFieldPermission(
                        dv.WriterProfileId, SecureRecordSetupProcedure.StandingGrantTable, 4, 0, 4)),
                SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may create the standing grant (0/4/0)", dv =>
                    dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.StandingGrantTable, 0, 4, 0)),
                SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may only update the standing grant (0/0/4)", dv =>
                    dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.StandingGrantTable, 0, 0, 4)),
                SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter, FailureClass.QuarantineRequired },
            { "Access Administrator role missing from the root unit", dv => dv.Roles.RemoveAll(r => r.Id == dv.AccessAdministratorRoleId),
                SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable },
            { "Core User role missing from the root unit", dv => dv.Roles.RemoveAll(r => r.Id == dv.CoreUserRoleId),
                SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable },
            { "Access Administrator reads entries only at Basic", dv =>
                    dv.RolePrivileges[dv.AccessAdministratorRoleId][dv.NoAccessEntryReadPrivilegeId] = ("prvReadsprk_noaccessentry", "Basic"),
                SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable },
            { "Access Administrator lacks Read on entries", dv => dv.RolePrivileges[dv.AccessAdministratorRoleId].Clear(),
                SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable },
            { "Core User still reads entries (task 154 not applied)", dv =>
                    dv.RolePrivileges[dv.CoreUserRoleId][dv.NoAccessEntryReadPrivilegeId] = ("prvReadsprk_noaccessentry", "Global"),
                SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete, FailureClass.Resumable },

            // ---- S22: the external-invite columns' locks (ISS-025 / #1803) ----
            { "contact.sprk_externalinvitedon not secured", dv =>
                    dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalinvitedon")] = false,
                SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable },
            { "contact.sprk_externalinvitedon absent (it ships in 1.2.2.0)", dv =>
                    dv.SecuredColumns.Remove(FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalinvitedon")),
                SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable },
            { "Writers cannot update contact.sprk_externalinvitedon", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_externalinvitedon");
                    permissions.RemoveAll(p => p.ProfileId == dv.WriterProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.WriterProfileId, "contact", 4, 4, 0));
                },
                SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable },
            { "Writers have no row on contact.sprk_externalinvitedon", dv =>
                    dv.ColumnPermissions("sprk_externalinvitedon").RemoveAll(p => p.ProfileId == dv.WriterProfileId),
                SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable },
            { "Readers cannot read contact.sprk_externalinvitedon", dv =>
                    dv.ColumnPermissions("sprk_externalinvitedon").RemoveAll(p => p.ProfileId == dv.ReaderProfileId),
                SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable },
            { "Readers may update contact.sprk_externalinvitedon", dv =>
                {
                    var permissions = dv.ColumnPermissions("sprk_externalinvitedon");
                    permissions.RemoveAll(p => p.ProfileId == dv.ReaderProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.ReaderProfileId, "contact", 4, 0, 4));
                },
                SecureRecordSetupRejectionCodes.InviteMarkerLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may create contact.sprk_externalinvitedon", dv =>
                    dv.ColumnPermissions("sprk_externalinvitedon")
                        .Add(new SecureSetupFieldPermission(dv.AddProfile("Contact Admins"), "contact", 4, 4, 0)),
                SecureRecordSetupRejectionCodes.InviteMarkerLockOtherWriter, FailureClass.QuarantineRequired },
            { "contact.sprk_externalinvitecodehash present but not secured", dv =>
                {
                    dv.AddInviteCodeHash();
                    dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalinvitecodehash")] = false;
                },
                SecureRecordSetupRejectionCodes.InviteCodeHashLockIncomplete, FailureClass.Resumable },
            { "Writers cannot update contact.sprk_externalinvitecodehash", dv =>
                {
                    dv.AddInviteCodeHash();
                    var permissions = dv.ColumnPermissions("sprk_externalinvitecodehash");
                    permissions.RemoveAll(p => p.ProfileId == dv.WriterProfileId);
                    permissions.Add(new SecureSetupFieldPermission(dv.WriterProfileId, "contact", 4, 4, 0));
                },
                SecureRecordSetupRejectionCodes.InviteCodeHashLockIncomplete, FailureClass.Resumable },
            { "Readers can READ contact.sprk_externalinvitecodehash", dv =>
                {
                    dv.AddInviteCodeHash();
                    dv.ColumnPermissions("sprk_externalinvitecodehash")
                        .Add(new SecureSetupFieldPermission(dv.ReaderProfileId, "contact", 4, 0, 0));
                },
                SecureRecordSetupRejectionCodes.InviteCodeHashLockOtherAccess, FailureClass.QuarantineRequired },
            { "Readers can update contact.sprk_externalinvitecodehash (0/0/4)", dv =>
                {
                    dv.AddInviteCodeHash();
                    dv.ColumnPermissions("sprk_externalinvitecodehash")
                        .Add(new SecureSetupFieldPermission(dv.ReaderProfileId, "contact", 0, 0, 4));
                },
                SecureRecordSetupRejectionCodes.InviteCodeHashLockOtherAccess, FailureClass.QuarantineRequired },
            { "another profile can read contact.sprk_externalinvitecodehash", dv =>
                {
                    dv.AddInviteCodeHash();
                    dv.ColumnPermissions("sprk_externalinvitecodehash")
                        .Add(new SecureSetupFieldPermission(dv.AddProfile("Contact Admins"), "contact", 4, 0, 0));
                },
                SecureRecordSetupRejectionCodes.InviteCodeHashLockOtherAccess, FailureClass.QuarantineRequired },

            // ---- S19 (#2317): the access level's lock ----
            { "contact.sprk_accesspermissiongrant not secured", dv =>
                    dv.SecuredColumns[LevelKey] = false,
                SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable },
            { "contact.sprk_accesspermissiongrant absent", dv => dv.SecuredColumns.Remove(LevelKey),
                SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable },
            { "Standing Grant Administrators cannot create the access level", dv =>
                    Replace(dv, Level, dv.StandingGrantProfileId, 4, 0, 4),
                SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable },
            { "Readers cannot read the access level", dv =>
                    dv.ColumnPermissions(Level).RemoveAll(p => p.ProfileId == dv.ReaderProfileId),
                SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable },
            { "Writers cannot update the access level", dv => Replace(dv, Level, dv.WriterProfileId, 4, 0, 0),
                SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable },
            { "Readers may update the access level (4/0/4)", dv => Replace(dv, Level, dv.ReaderProfileId, 4, 0, 4),
                SecureRecordSetupRejectionCodes.AccessLevelLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may create the access level", dv =>
                    dv.ColumnPermissions(Level).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.StandingGrantTable, 4, 4, 0)),
                SecureRecordSetupRejectionCodes.AccessLevelLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may only update the access level (0/0/4)", dv =>
                    dv.ColumnPermissions(Level).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.StandingGrantTable, 0, 0, 4)),
                SecureRecordSetupRejectionCodes.AccessLevelLockOtherWriter, FailureClass.QuarantineRequired },

            // ---- S22 (#2317): the BFF-only columns and the removed-on marker, once they ship ----
            { "contact.sprk_externalinvitedemailhash present but not secured", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.SecuredColumns[InviteKey(SecureRecordSetupProcedure.InvitedEmailHashColumn)] = false;
                },
                SecureRecordSetupRejectionCodes.BffOnlyColumnLockIncomplete, FailureClass.Resumable },
            { "Writers cannot update contact.sprk_externalinvitedtenantid", dv =>
                {
                    dv.AddBffOnlyColumns();
                    Replace(dv, SecureRecordSetupProcedure.InvitedTenantIdColumn, dv.WriterProfileId, 4, 4, 0);
                },
                SecureRecordSetupRejectionCodes.BffOnlyColumnLockIncomplete, FailureClass.Resumable },
            { "Readers can READ contact.sprk_externalinvitedemailhash", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.ColumnPermissions(SecureRecordSetupProcedure.InvitedEmailHashColumn).Add(new SecureSetupFieldPermission(
                        dv.ReaderProfileId, SecureRecordSetupProcedure.InviteTable, 4, 0, 0));
                },
                SecureRecordSetupRejectionCodes.BffOnlyColumnLockOtherAccess, FailureClass.QuarantineRequired },
            { "another profile can read contact.sprk_externalinvitedtenantid", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.ColumnPermissions(SecureRecordSetupProcedure.InvitedTenantIdColumn).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.InviteTable, 4, 0, 0));
                },
                SecureRecordSetupRejectionCodes.BffOnlyColumnLockOtherAccess, FailureClass.QuarantineRequired },
            { "contact.sprk_externalaccessremovedon present but not secured", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.SecuredColumns[InviteKey(SecureRecordSetupProcedure.AccessRemovedOnColumn)] = false;
                },
                SecureRecordSetupRejectionCodes.AccessRemovedOnLockIncomplete, FailureClass.Resumable },
            { "Readers cannot read contact.sprk_externalaccessremovedon", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.ColumnPermissions(SecureRecordSetupProcedure.AccessRemovedOnColumn).RemoveAll(p => p.ProfileId == dv.ReaderProfileId);
                },
                SecureRecordSetupRejectionCodes.AccessRemovedOnLockIncomplete, FailureClass.Resumable },
            { "Readers may update contact.sprk_externalaccessremovedon", dv =>
                {
                    dv.AddBffOnlyColumns();
                    Replace(dv, SecureRecordSetupProcedure.AccessRemovedOnColumn, dv.ReaderProfileId, 4, 0, 4);
                },
                SecureRecordSetupRejectionCodes.AccessRemovedOnLockOtherWriter, FailureClass.QuarantineRequired },
            { "another profile may create contact.sprk_externalaccessremovedon", dv =>
                {
                    dv.AddBffOnlyColumns();
                    dv.ColumnPermissions(SecureRecordSetupProcedure.AccessRemovedOnColumn).Add(new SecureSetupFieldPermission(
                        dv.AddProfile("Contact Admins"), SecureRecordSetupProcedure.InviteTable, 4, 4, 0));
                },
                SecureRecordSetupRejectionCodes.AccessRemovedOnLockOtherWriter, FailureClass.QuarantineRequired },
        };
        return data;
    }

    // #2317 helpers.
    private const string Level = SecureRecordSetupProcedure.AccessLevelColumn;

    private static readonly string LevelKey =
        FakeSecureRecordSetupDataverse.ColumnKey(SecureRecordSetupProcedure.StandingGrantTable, Level);

    private static string InviteKey(string column) => FakeSecureRecordSetupDataverse.ColumnKey(SecureRecordSetupProcedure.InviteTable, column);

    /// <summary>Replaces the row of <paramref name="profileId"/> on <paramref name="column"/> with read/create/update as given.</summary>
    private static void Replace(FakeSecureRecordSetupDataverse dv, string column, Guid profileId, int read, int create, int update)
    {
        var permissions = dv.ColumnPermissions(column);
        var table = permissions.First(p => p.ProfileId == profileId).EntityName;
        permissions.RemoveAll(p => p.ProfileId == profileId);
        permissions.Add(new SecureSetupFieldPermission(profileId, table, read, create, update));
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
    public async Task CustomerUnit_ThatIsTheRoot_IsRefused_QuarantineRequired_WithoutWriting()
    {
        var dv = NewEnv();

        var refused = Refused(await RunAsync(dv, customerUnit: dv.RootUnitId),
            SecureRecordSetupRejectionCodes.CustomerBusinessUnitWrongParent, FailureClass.QuarantineRequired);

        refused.Diagnostic.Should().Contain("it is the root");
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerUnit_NamedLikeTheSecureUnit_IsRefused_BecauseItsUsersWouldBeInTheSecureUnit()
    {
        // Intake refuses this name; if a unit called "Secure Record" were recorded as the customer's anyway, S1 adopts it as
        // the Secure Record unit and S2 refuses the BFF's application users found in it.
        var dv = NewEnv();
        dv.Units.RemoveAll(u => u.Id == dv.CustomerUnitId);
        dv.Units.Add(new SecureSetupBusinessUnit(dv.CustomerUnitId, Set.BusinessUnitName, dv.RootUnitId));

        Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.BusinessUnitHasUsers, FailureClass.QuarantineRequired);
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SystemAdministratorProfile_MayWriteTheFlag_OwnerDecisionF4()
    {
        var dv = NewEnv();   // NewEnvironment grants the System Administrator profile read/create/update

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
    }

    // ------------------------------------------------------------ S22: the external-invite columns (ISS-025 / #1803)

    private const string CodeHashAbsentNote = "absent — ships in a later SpaarkeMaster release";

    [Fact]
    public async Task InviteColumns_CodeHashAbsent_Passes_AndRecordsTheAbsentOutcome_WithoutWriting()
    {
        var dv = NewEnv();   // SpaarkeMaster 1.2.2.0: sprk_externalinvitedon shipped and locked, the code hash not there yet

        var applied = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Subject;

        applied.State.Notes.Should().ContainSingle(n => n.Contains("sprk_externalinvitecodehash", StringComparison.Ordinal)
                                                       && n.Contains(CodeHashAbsentNote, StringComparison.Ordinal));
        dv.Writes.Should().NotContain(w => w.Contains("invite", StringComparison.OrdinalIgnoreCase), "S22 verifies, never writes");
    }

    [Fact]
    public async Task InviteColumns_BothCorrect_Pass_WithNoReadersRowOnTheCodeHash()
    {
        var dv = NewEnv();
        dv.AddInviteCodeHash();   // the platform System Administrator profile also holds 4/4/4 (exempt)

        var applied = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Subject;

        var codeHashNotes = applied.State.Notes!
            .Where(n => n.Contains(SecureRecordSetupProcedure.InviteCodeHashColumn, StringComparison.Ordinal)).ToArray();
        codeHashNotes.Should().ContainSingle().Which.Should().Contain("present, secured");
        codeHashNotes.Should().NotContain(n => n.Contains("absent", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ S19/S22 (#2317): access level, BFF-only columns, removed-on

    [Fact]
    public async Task AccessLevel_AsShipped_Passes_TheReadersProfileMayRead_AndSystemAdministratorIsExempt()
    {
        var dv = NewEnv();   // Standing Grant Administrators 4/4/4, Readers 4/0/0, Writers 4/0/4, System Administrator 4/4/4

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Should().NotContain(w => w.Contains(Level, StringComparison.OrdinalIgnoreCase), "S19 verifies the lock, never writes it");
    }

    [Fact]
    public async Task StandingGrant_AsShipped_Passes_TheReadersProfileMayRead_AndSystemAdministratorIsExempt()
    {
        var dv = NewEnv();   // Standing Grant Administrators 4/4/4
        var standing = dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn);
        standing.Add(new SecureSetupFieldPermission(dv.SystemAdministratorProfileId, SecureRecordSetupProcedure.StandingGrantTable, 4, 4, 4));
        standing.Add(new SecureSetupFieldPermission(dv.ReaderProfileId, SecureRecordSetupProcedure.StandingGrantTable, 4, 0, 0));
        // A grant on the same column name on another table is not this column's lock.
        standing.Add(new SecureSetupFieldPermission(dv.AddProfile("Contact Admins"), "sprk_externalparty", 4, 4, 4));

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Should().NotContain(w => w.Contains(SecureRecordSetupProcedure.StandingGrantColumn, StringComparison.OrdinalIgnoreCase),
            "S19 verifies the lock, never writes it");
    }

    [Fact]
    public async Task StandingGrantOtherWriter_NamesTheProfile_AndTheRule()
    {
        var dv = NewEnv();
        var admins = dv.AddProfile("Contact Admins");
        dv.ColumnPermissions(SecureRecordSetupProcedure.StandingGrantColumn).Add(new SecureSetupFieldPermission(
            admins, SecureRecordSetupProcedure.StandingGrantTable, 0, 0, 4));

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter,
            FailureClass.QuarantineRequired);

        refused.Diagnostic.Should().Contain(admins.ToString()).And.Contain("contact.sprk_standinggrant")
            .And.Contain("Only Standing Grant Administrators and System Administrator may create or update the standing grant");
    }

    // ------------------------------------------------------------ S18/S19: an environment on SpaarkeMaster 1.2.3.1

    /// <summary>
    /// The contact columns as SpaarkeMaster 1.2.3.1 leaves them: the identity plane, collision and access-level columns exist
    /// but are not secured and carry no profile rows; the three BFF-only columns do not exist (NewEnvironment omits them).
    /// </summary>
    private static void ShapeAsSpaarkeMaster1231(FakeSecureRecordSetupDataverse dv, bool identityPlaneAndCollisionToo)
    {
        if (identityPlaneAndCollisionToo)
        {
            foreach (var (table, column) in SecureRecordSetupProcedure.IdentityLinkColumns
                         .Where(c => c.Column.StartsWith("sprk_identity", StringComparison.Ordinal)))
            {
                dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey(table, column)] = false;
                dv.ColumnPermissions(column).Clear();
            }
        }
        dv.SecuredColumns[LevelKey] = false;
        dv.ColumnPermissions(Level).Clear();
    }

    [Fact]
    public async Task SpaarkeMaster1231Environment_IsRefusedAtS18_NamingTheMinimumVersionAndTheScript()
    {
        var dv = NewEnv();
        ShapeAsSpaarkeMaster1231(dv, identityPlaneAndCollisionToo: true);

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain("contact.sprk_identityplane")
            .And.Contain("SpaarkeMaster 1.2.4.0 or later")
            .And.Contain(ResumeRemedy);
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SpaarkeMaster1231AccessLevel_IsRefusedAtS19_NamingTheMinimumVersionAndTheScript()
    {
        var dv = NewEnv();
        ShapeAsSpaarkeMaster1231(dv, identityPlaneAndCollisionToo: false);

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain("contact.sprk_accesspermissiongrant")
            .And.Contain("SpaarkeMaster 1.2.4.0 or later")
            .And.Contain(ResumeRemedy);
        dv.Writes.Should().BeEmpty();
    }

    /// <summary>A resume does not re-run a completed H6, so the remedy names the script for an environment past it.</summary>
    private const string ResumeRemedy =
        "a new run imports it at H6 once it is published; in an environment already past H6 (a resume does not re-run a " +
        "completed H6) run scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Apply, then -Verify (exit 0), then resume. " +
        "Nothing was written.";

    /// <summary>
    /// The standing-grant field and its profile ship only in the package, so the remedy for an environment past H6 is a hand
    /// import of the release, never the contact schema script (which writes neither).
    /// </summary>
    private const string StandingGrantRemedy =
        "It requires SpaarkeMaster 1.2.0.0 or later: a new run imports it at H6; in an environment already past H6 (a resume " +
        "does not re-run a completed H6), import it by hand with scripts/solution-authoring/Import-SpaarkeMasterPackage.ps1 " +
        "-EnvironmentUrl <org> -PackageType managed, then resume. Nothing was written.";

    [Fact]
    public async Task StandingGrantLockIncomplete_NamesTheMinimumVersionAndTheImportScriptForAnEnvironmentPastH6()
    {
        var dv = NewEnv();
        dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_standinggrant")] = false;

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain("contact.sprk_standinggrant")
            .And.Contain(StandingGrantRemedy)
            .And.NotContain(SecureRecordSetupProcedure.ContactSecuritySchemaScript);
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task BffOnlyColumnsAndRemovedOn_Absent_Pass_AndRecordTheAbsentOutcome()
    {
        var dv = NewEnv();   // the release that carries them has not landed

        var applied = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Subject;

        foreach (var column in SecureRecordSetupProcedure.BffOnlyColumns.Append(SecureRecordSetupProcedure.AccessRemovedOnColumn))
        {
            applied.State.Notes.Should().ContainSingle(n => n.Contains(column, StringComparison.Ordinal)
                                                           && n.Contains(CodeHashAbsentNote, StringComparison.Ordinal));
        }
        dv.Writes.Should().NotContain(w => w.Contains("external", StringComparison.OrdinalIgnoreCase), "S22 verifies, never writes");
    }

    [Fact]
    public async Task BffOnlyColumnsAndRemovedOn_AsShipped_Pass_WithPresentNotes()
    {
        var dv = NewEnv();
        dv.AddBffOnlyColumns();

        var applied = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Subject;

        foreach (var column in SecureRecordSetupProcedure.BffOnlyColumns.Append(SecureRecordSetupProcedure.AccessRemovedOnColumn))
        {
            applied.State.Notes.Should().ContainSingle(n => n.Contains(column, StringComparison.Ordinal)
                                                           && n.Contains("present, secured", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task BffOnlyColumn_WithAZeroZeroZeroReadersRow_Passes()
    {
        var dv = NewEnv();
        dv.AddBffOnlyColumns();
        dv.ColumnPermissions(SecureRecordSetupProcedure.InvitedEmailHashColumn).Add(new SecureSetupFieldPermission(
            dv.ReaderProfileId, SecureRecordSetupProcedure.InviteTable, 0, 0, 0));

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>("0/0/0 grants nothing, the same as no row");
    }

    [Fact]
    public async Task InviteCodeHash_WithAZeroZeroZeroReadersRow_Passes()
    {
        var dv = NewEnv();
        dv.AddInviteCodeHash();
        dv.ColumnPermissions("sprk_externalinvitecodehash")
            .Add(new SecureSetupFieldPermission(dv.ReaderProfileId, "contact", 0, 0, 0));

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>("0/0/0 grants nothing, the same as no row");
    }

    [Fact]
    public async Task SecondRun_WithBothInviteColumns_WritesNothing()
    {
        var dv = NewEnv();
        dv.AddInviteCodeHash();
        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>();
        dv.Writes.Clear();

        (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>().Which.Actions.Should().BeEmpty();
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task InviteCodeHash_ReadFault_Propagates_NeverTreatedAsAbsent()
    {
        var dv = NewEnv();
        var codeHashKey = FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalinvitecodehash");
        dv.ColumnReadFault = key => key.Equals(codeHashKey, StringComparison.OrdinalIgnoreCase)
            ? new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other, "503")
            : null;

        var act = () => RunAsync(dv);

        await act.Should().ThrowAsync<SecureRecordSetupDataverseException>("a failed read is not 'column absent'");
        dv.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task InviteMarkerRefusal_NamesTheColumn_AndTheRemedy()
    {
        var dv = NewEnv();
        dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey("contact", "sprk_externalinvitedon")] = false;

        var refused = Refused(await RunAsync(dv), SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete, FailureClass.Resumable);

        refused.Diagnostic.Should().Contain("contact.sprk_externalinvitedon").And.Contain("IsSecured=False").And.Contain("1.2.2.0");
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
