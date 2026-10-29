// -----------------------------------------------------------------------------
// H7cCustomerOwnerTeamsHandlerTests.cs
//
// #2429 (H7c) — the customer owner teams handler and its procedure (a port of scripts/Set-CustomerOwnerTeams.ps1) against
// the in-memory FakeSecureRecordSetupDataverse (H7b's seam fake; no Moq, ADR-038): a fresh create of both teams and roles,
// the idempotent re-run (no writes), every refusal of the script, owner-decision drift (quarantine, nothing written for
// ANY team), fixable drift (repaired), fail-closed reads, verify-after-write mismatches, and the InterStepState ids
// (written on success only). Also H13's read-only CustomerOwnerTeamsVerifier over the same fake.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.CustomerOwnerTeams;

public sealed class H7cCustomerOwnerTeamsHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h7c-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";

    private static readonly CustomerOwnerRoleSet Set = CustomerOwnerRoleSet.Embedded;
    private static readonly SecureRecordOwnerRoleSet SecureSet = SecureRecordOwnerRoleSet.Embedded;

    // ------------------------------------------------------------ fresh create, idempotency

    [Fact]
    public async Task FreshEnvironment_CreatesBothTeamsAndRoles_AssignsTheCustomerCopies_AndRecordsTheIds()
    {
        var (dv, secureId) = NewEnvironment();
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H7cCustomerOwnerTeamsHandler.BuildIdempotencyKey(
            CustomerId, H7cCustomerOwnerTeamsHandler.ComputeSetHash(Set)));
        var run = repo.LastWrittenRun!;
        run.CompletedPhases.Should().ContainSingle(p => p.Phase == "H7c" && p.IdempotencyKey == success.IdempotencyKey);
        run.GateStates[H7cCustomerOwnerTeamsHandler.SetupGateId].Status.Should().Be(GateState.Verified);

        foreach (var def in Set.Definitions)
        {
            var team = dv.Teams.Single(t => t.Name == def.TeamName);
            team.BusinessUnitId.Should().Be(dv.CustomerUnitId, "the team lives in the customer's top unit, never the root");
            team.IsDefault.Should().BeFalse();
            dv.TeamMembers[team.Id].Should().BeEmpty();

            var rootRole = dv.Role(dv.RootUnitId, def.RoleName)!;
            var copy = dv.Role(dv.CustomerUnitId, def.RoleName)!;
            copy.ParentRootRoleId.Should().Be(rootRole.Id);
            dv.TeamRoles[team.Id].Should().BeEquivalentTo(new[] { copy.Id }, "the team holds its unit's copy and nothing else");
            dv.TeamRoles.Values.Should().NotContain(roles => roles.Contains(rootRole.Id), "the root role is assigned to nobody");
            dv.Role(secureId, def.RoleName).Should().NotBeNull("Dataverse copies the root role into every child unit");

            // Read at Basic on exactly the file's tables: the injected platform privileges are stripped (§5.4).
            dv.RolePrivileges[rootRole.Id].Values.Should().OnlyContain(p => p.Depth == "Basic");
            dv.RolePrivileges[rootRole.Id].Values.Select(p => p.Name)
                .Should().BeEquivalentTo(def.Tables.Select(t => t.PrivilegeName));
        }

        run.InterStepState.RestrictedContentOwnerTeamId.Should().Be(
            dv.Teams.Single(t => t.Name == Set.RestrictedContent.TeamName).Id.ToString("D"));
        run.InterStepState.CustomerConfigurationOwnerTeamId.Should().Be(
            dv.Teams.Single(t => t.Name == Set.CustomerConfiguration.TeamName).Id.ToString("D"));
        dv.Writes.Should().Contain(w => w.StartsWith("CreateOwnerTeam", StringComparison.Ordinal)).And
            .Contain(w => w.StartsWith("CreateRole", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMemberTheCreateAdded_IsRemoved_OnlyFromATeamThisRunCreated()
    {
        var (dv, _) = NewEnvironment();
        dv.CreateOwnerTeamAddsMember = dv.BffAppUser;   // the platform adds the caller
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Count(w => w.StartsWith("RemoveTeamMembers", StringComparison.Ordinal)).Should().Be(2);
        foreach (var def in Set.Definitions)
        {
            dv.TeamMembers[dv.Teams.Single(t => t.Name == def.TeamName).Id].Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ASecondRun_InANewRunRow_WritesNothing_AndRecordsTheSameIds()
    {
        var (dv, _) = NewEnvironment();
        var first = new FakeRepository(BuildRun(dv));
        (await BuildHandler(first, dv).HandleAsync(Envelope(), CancellationToken.None)).Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Clear();
        var second = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(second, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Should().BeEmpty("a second run against a configured environment changes nothing");
        second.LastWrittenRun!.InterStepState.RestrictedContentOwnerTeamId
            .Should().Be(first.LastWrittenRun!.InterStepState.RestrictedContentOwnerTeamId);
        second.LastWrittenRun.InterStepState.CustomerConfigurationOwnerTeamId
            .Should().Be(first.LastWrittenRun.InterStepState.CustomerConfigurationOwnerTeamId);
    }

    [Fact]
    public async Task SameRun_WithTheCompletedPhase_IsANoOp_WithoutTouchingDataverse()
    {
        var (dv, _) = NewEnvironment();
        var run = BuildRun(dv);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H7c",
            IdempotencyKey = H7cCustomerOwnerTeamsHandler.BuildIdempotencyKey(CustomerId, H7cCustomerOwnerTeamsHandler.ComputeSetHash(Set)),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = RunId,
        });
        var repo = new FakeRepository(run);

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.Reads.Should().Be(0);
        repo.LastWrittenRun.Should().BeNull();
    }

    [Fact]
    public async Task AnotherCustomerTopUnitWithoutTheTeams_IsInformationOnly()
    {
        var (dv, _) = NewEnvironment();
        var other = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(other, "Globex", dv.RootUnitId));
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var notes = repo.LastWrittenRun!.GateStates[H7cCustomerOwnerTeamsHandler.SetupGateId].Evidence!.Value.GetProperty("notes");
        notes.EnumerateArray().Select(n => n.GetString()!).Should().HaveCount(2).And.OnlyContain(n => n.Contains("Globex"));
        dv.Teams.Should().NotContain(t => t.BusinessUnitId == other && !t.IsDefault, "each customer top unit is set up by its own run");
    }

    // ------------------------------------------------------------ refusals (the script's REFUSED list) — nothing written

    public static TheoryData<string, string, FailureClass> Refusals() => new()
    {
        { "two-roots", CustomerOwnerTeamsRejectionCodes.RootBusinessUnitUnresolved, FailureClass.Resumable },
        { "no-secure-unit", CustomerOwnerTeamsRejectionCodes.SecureBusinessUnitUnresolved, FailureClass.Resumable },
        { "two-secure-units", CustomerOwnerTeamsRejectionCodes.SecureBusinessUnitUnresolved, FailureClass.Resumable },
        { "customer-missing", CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitMissing, FailureClass.QuarantineRequired },
        { "customer-is-root", CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitIsRoot, FailureClass.QuarantineRequired },
        { "customer-is-secure", CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitIsSecure, FailureClass.QuarantineRequired },
        { "customer-not-direct-child", CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitWrongParent, FailureClass.QuarantineRequired },
        { "privilege-name-casing", CustomerOwnerTeamsRejectionCodes.PrivilegeNameMismatch, FailureClass.Resumable },
        { "table-missing", CustomerOwnerTeamsRejectionCodes.PrivilegeNameMismatch, FailureClass.Resumable },
        { "two-teams-in-unit", CustomerOwnerTeamsRejectionCodes.TeamAmbiguous, FailureClass.QuarantineRequired },
        { "two-roles-in-root", CustomerOwnerTeamsRejectionCodes.RoleAmbiguous, FailureClass.Resumable },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EachRefusal_FailsWithItsCodeAndClass_WritesNothing_AndRecordsNoIds(string scenario, string code, FailureClass failureClass)
    {
        var (dv, secureId) = NewEnvironment();
        var run = BuildRun(dv);
        switch (scenario)
        {
            case "two-roots":
                dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), "second-root", null));
                break;
            case "no-secure-unit":
                dv.Units.RemoveAll(u => u.Id == secureId);
                break;
            case "two-secure-units":
                dv.Units.Add(new SecureSetupBusinessUnit(Guid.NewGuid(), SecureSet.BusinessUnitName, dv.CustomerUnitId));
                break;
            case "customer-missing":
                run.InterStepState.CustomerBusinessUnitId = Guid.NewGuid().ToString();
                break;
            case "customer-is-root":
                run.InterStepState.CustomerBusinessUnitId = dv.RootUnitId.ToString();
                break;
            case "customer-is-secure":
                run.InterStepState.CustomerBusinessUnitId = secureId.ToString();
                break;
            case "customer-not-direct-child":
                var grandchild = Guid.NewGuid();
                dv.Units.Add(new SecureSetupBusinessUnit(grandchild, "Acme Legal", dv.CustomerUnitId));
                run.InterStepState.CustomerBusinessUnitId = grandchild.ToString();
                break;
            case "privilege-name-casing":
                var table = Set.CustomerConfiguration.Tables[0];
                dv.TableReadPrivileges[table.LogicalName] = [new SecureSetupPrivilege(Guid.NewGuid(), table.PrivilegeName.ToLowerInvariant())];
                break;
            case "table-missing":
                dv.TableReadPrivileges.Remove(Set.RestrictedContent.Tables[^1].LogicalName);
                break;
            case "two-teams-in-unit":
                dv.AddTeam(dv.CustomerUnitId, Set.CustomerConfiguration.TeamName, isDefault: false);
                dv.AddTeam(dv.CustomerUnitId, Set.CustomerConfiguration.TeamName, isDefault: false);
                break;
            case "two-roles-in-root":
                dv.AddRole(dv.RootUnitId, Set.RestrictedContent.RoleName);
                dv.AddRole(dv.RootUnitId, Set.RestrictedContent.RoleName);
                break;
        }
        var repo = new FakeRepository(run);

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(code, scenario);
        failure.Class.Should().Be(failureClass, scenario);
        failure.Diagnostic.Should().Contain("Nothing was written");
        dv.Writes.Should().BeEmpty(scenario);
        AssertNoIdsRecorded(repo, failureClass);
    }

    // ------------------------------------------------------------ owner-decision drift — quarantine, nothing written for ANY team

    [Fact]
    public async Task AnExistingTeamWithAMember_IsRefused_QuarantineRequired_BeforeAnyWrite()
    {
        var (dv, _) = NewEnvironment();
        var team = dv.AddTeam(dv.CustomerUnitId, Set.RestrictedContent.TeamName, isDefault: false);
        dv.TeamMembers[team].Add(Guid.NewGuid());
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(CustomerOwnerTeamsRejectionCodes.TeamHasMembers);
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.Diagnostic.Should().Contain("OWNER DECISION NEEDED").And.Contain("TEAM MEMBERS");
        dv.Writes.Should().BeEmpty("-Apply refuses before writing anything, for every declared team — the other team too");
        dv.TeamMembers[team].Should().HaveCount(1, "a member a person added is never removed");
        AssertNoIdsRecorded(repo, FailureClass.QuarantineRequired);
    }

    public static TheoryData<string, string> OwnerDecisions() => new()
    {
        { "access-team", CustomerOwnerTeamsRejectionCodes.TeamNotOwnerTeam },
        { "default-team", CustomerOwnerTeamsRejectionCodes.TeamNotOwnerTeam },
        { "other-role-on-team", CustomerOwnerTeamsRejectionCodes.TeamHasOtherRole },
        { "role-on-a-user", CustomerOwnerTeamsRejectionCodes.RoleAssignedElsewhere },
        { "copy-on-another-team", CustomerOwnerTeamsRejectionCodes.RoleAssignedElsewhere },
        { "root-role-on-a-team", CustomerOwnerTeamsRejectionCodes.RoleAssignedElsewhere },
    };

    [Theory]
    [MemberData(nameof(OwnerDecisions))]
    public async Task OwnerDecisionDrift_IsRefused_QuarantineRequired_WithNothingWritten(string scenario, string code)
    {
        var (dv, _) = NewEnvironment();
        // A configured environment, then one drift the script calls an owner decision.
        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        var def = Set.CustomerConfiguration;
        var team = dv.Teams.Single(t => t.Name == def.TeamName);
        var rootRole = dv.Role(dv.RootUnitId, def.RoleName)!;
        switch (scenario)
        {
            case "access-team":
                dv.TeamTypes[team.Id] = 1;
                break;
            case "default-team":
                dv.Teams[dv.Teams.IndexOf(team)] = team with { IsDefault = true };
                break;
            case "other-role-on-team":
                dv.TeamRoles[team.Id].Add(dv.AddRole(dv.CustomerUnitId, "Basic User"));
                break;
            case "role-on-a-user":
                dv.UserRoles[Guid.NewGuid()] = [dv.Role(dv.CustomerUnitId, def.RoleName)!.Id];
                break;
            case "copy-on-another-team":
                var stranger = dv.AddTeam(dv.CustomerUnitId, "Paralegals", isDefault: false);
                dv.TeamRoles[stranger].Add(dv.Role(dv.CustomerUnitId, def.RoleName)!.Id);
                break;
            case "root-role-on-a-team":
                dv.TeamRoles[dv.RootDefaultTeamId].Add(rootRole.Id);
                break;
        }
        dv.Writes.Clear();
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(code, scenario);
        failure.Class.Should().Be(FailureClass.QuarantineRequired, scenario);
        dv.Writes.Should().BeEmpty(scenario);
        AssertNoIdsRecorded(repo, FailureClass.QuarantineRequired);
    }

    // ------------------------------------------------------------ fixable drift — repaired, then verified

    [Fact]
    public async Task FixableDrift_IsRepaired_DepthNarrowedToBasic_OutsidePrivilegesStripped_AssignmentRestored()
    {
        var (dv, _) = NewEnvironment();
        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        var def = Set.RestrictedContent;
        var team = dv.Teams.Single(t => t.Name == def.TeamName);
        var rootRole = dv.Role(dv.RootUnitId, def.RoleName)!;
        var copy = dv.Role(dv.CustomerUnitId, def.RoleName)!;
        var first = dv.TableReadPrivileges[def.Tables[0].LogicalName][0];
        var second = dv.TableReadPrivileges[def.Tables[1].LogicalName][0];
        dv.RolePrivileges[rootRole.Id][first.Id] = (first.Name, "Global");          // PRIVILEGE DEPTH
        dv.RolePrivileges[rootRole.Id].Remove(second.Id);                          // PRIVILEGE MISSING
        dv.RolePrivileges[rootRole.Id][Guid.NewGuid()] = ("prvReadSharePointData", "Global");   // OUTSIDE THE FILE
        dv.TeamRoles[team.Id].Remove(copy.Id);                                     // ROLE NOT ASSIGNED
        dv.Writes.Clear();
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        dv.RolePrivileges[rootRole.Id].Values.Should().OnlyContain(p => p.Depth == "Basic");
        dv.RolePrivileges[rootRole.Id].Values.Select(p => p.Name).Should().BeEquivalentTo(def.Tables.Select(t => t.PrivilegeName));
        dv.TeamRoles[team.Id].Should().BeEquivalentTo(new[] { copy.Id });
        dv.Writes.Should().NotContain(w => w.StartsWith("Create", StringComparison.Ordinal));
        dv.Writes.Should().Contain($"RemovePrivilege {first.Id}", "a declared privilege at another depth is narrowed, never widened");
    }

    // ------------------------------------------------------------ fail closed — a failed read is a failure, never "absent"

    [Theory]
    [InlineData(nameof(ISecureRecordSetupDataverse.FindTeamsByNameAsync), SecureRecordSetupFaultKind.Other, CustomerOwnerTeamsRejectionCodes.DataverseInvocationFailed)]
    [InlineData(nameof(ISecureRecordSetupDataverse.ListTeamMembersAsync), SecureRecordSetupFaultKind.Other, CustomerOwnerTeamsRejectionCodes.DataverseInvocationFailed)]
    [InlineData(nameof(ISecureRecordSetupDataverse.ListRoleCopiesAsync), SecureRecordSetupFaultKind.Other, CustomerOwnerTeamsRejectionCodes.DataverseInvocationFailed)]
    [InlineData(nameof(ISecureRecordSetupDataverse.ListChildBusinessUnitsAsync), SecureRecordSetupFaultKind.Auth, CustomerOwnerTeamsRejectionCodes.DataverseAuthFailure)]
    [InlineData(nameof(ISecureRecordSetupDataverse.GetReadPrivilegesAsync), SecureRecordSetupFaultKind.RateLimited, CustomerOwnerTeamsRejectionCodes.RateLimited)]
    public async Task AFailedRead_FailsTheRun_Resumable_WithNoWritesAndNoIds(string member, SecureRecordSetupFaultKind kind, string code)
    {
        var (dv, _) = NewEnvironment();
        if (member == nameof(ISecureRecordSetupDataverse.ListTeamMembersAsync))
        {
            dv.AddTeam(dv.CustomerUnitId, Set.RestrictedContent.TeamName, isDefault: false);   // so the member read happens
        }
        if (member == nameof(ISecureRecordSetupDataverse.ListRoleCopiesAsync))
        {
            dv.AddRole(dv.RootUnitId, Set.RestrictedContent.RoleName);   // so the copies read happens
        }
        dv.Fault = m => m == member ? new SecureRecordSetupDataverseException(kind, $"{m} returned 503.") : null;
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(code);
        failure.Class.Should().Be(FailureClass.Resumable);
        dv.Writes.Should().BeEmpty();
        AssertNoIdsRecorded(repo, FailureClass.Resumable);
    }

    // ------------------------------------------------------------ verify after write

    [Fact]
    public async Task AWriteThatDoesNotStick_FailsVerify_Resumable_AndRecordsNoIds()
    {
        var (dv, _) = NewEnvironment();
        dv.AddPrivilegesIsIgnored = true;   // AddPrivilegesRole answers 204 but nothing lands
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(CustomerOwnerTeamsRejectionCodes.VerifyFailed);
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.Diagnostic.Should().Contain("PRIVILEGE MISSING");
        AssertNoIdsRecorded(repo, FailureClass.Resumable);
        repo.LastWrittenRun!.CompletedPhases.Should().NotContain(p => p.Phase == "H7c");
    }

    [Fact]
    public async Task ACreatedTeamWhoseMemberCannotBeRemoved_FailsVerify()
    {
        var (dv, _) = NewEnvironment();
        dv.CreateOwnerTeamAddsMember = dv.BffAppUser;
        dv.RemoveTeamMembersIgnored = true;
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(CustomerOwnerTeamsRejectionCodes.VerifyFailed);
        failure.Diagnostic.Should().Contain("TEAM MEMBERS");
        AssertNoIdsRecorded(repo, FailureClass.Resumable);
    }

    [Fact]
    public async Task ARoleWithNoCopyInTheCustomerUnit_IsRefused_Resumable_AfterTheCreate()
    {
        var (dv, _) = NewEnvironment();
        dv.RoleCopiesOnCreate = false;
        var repo = new FakeRepository(BuildRun(dv));

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(CustomerOwnerTeamsRejectionCodes.RoleCopyMissing);
        failure.Class.Should().Be(FailureClass.Resumable);
        dv.Writes.Should().NotContain(w => w.StartsWith("AssociateTeamRole", StringComparison.Ordinal), "nothing is assigned without the unit's copy");
        AssertNoIdsRecorded(repo, FailureClass.Resumable);
    }

    // ------------------------------------------------------------ upstream guards

    [Theory]
    [InlineData("tenantId")]
    [InlineData("dataverseEnvUrl")]
    [InlineData("bffAppRegId")]
    [InlineData("customerBusinessUnitId")]
    public async Task AMissingUpstreamValue_FailsResumable_BeforeAnyDataverseCall(string missing)
    {
        var (dv, _) = NewEnvironment();
        var run = BuildRun(dv);
        switch (missing)
        {
            case "tenantId": run.Parameters.NonSecret.Remove("tenantId"); break;
            case "dataverseEnvUrl": run.InterStepState.DataverseEnvUrl = null; break;
            case "bffAppRegId": run.InterStepState.BffAppRegId = null; break;
            case "customerBusinessUnitId": run.InterStepState.CustomerBusinessUnitId = "not-a-guid"; break;
        }
        var repo = new FakeRepository(run);

        var result = await BuildHandler(repo, dv).HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(CustomerOwnerTeamsRejectionCodes.MissingUpstreamState);
        dv.Reads.Should().Be(0, "H7c never falls back to the root unit or the caller's unit");
    }

    [Fact]
    public async Task MismatchedHandlerId_Throws()
    {
        var (dv, _) = NewEnvironment();
        var act = () => BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope() with { HandlerId = "H7b" }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ------------------------------------------------------------ H13's read-only re-check

    [Fact]
    public async Task H13Verifier_PassesAConfiguredEnvironment_FailsDrift_AndNeverWrites()
    {
        var (dv, _) = NewEnvironment();
        (await BuildHandler(new FakeRepository(BuildRun(dv)), dv).HandleAsync(Envelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Success>();
        dv.Writes.Clear();
        var verifier = new CustomerOwnerTeamsVerifier(dv, NullLogger<CustomerOwnerTeamsVerifier>.Instance);
        var request = new CustomerOwnerTeamsVerificationRequest(TenantId, EnvUrl, BffAppRegId, dv.CustomerUnitId.ToString());

        (await verifier.VerifyAsync(request, CancellationToken.None)).Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Passed>();

        dv.TeamMembers[dv.Teams.Single(t => t.Name == Set.RestrictedContent.TeamName).Id].Add(Guid.NewGuid());
        var drifted = await verifier.VerifyAsync(request, CancellationToken.None);
        drifted.Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Failed>()
            .Which.Problems.Should().ContainSingle(p => p.Contains("TEAM MEMBERS"));

        dv.Teams.RemoveAll(t => t.Name == Set.CustomerConfiguration.TeamName);
        (await verifier.VerifyAsync(request, CancellationToken.None)).Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Failed>()
            .Which.Problems.Should().Contain(p => p.Contains("TEAM MISSING"));
        dv.Writes.Should().BeEmpty("the verifier must not alter what it verifies");
    }

    [Fact]
    public async Task H13Verifier_ARefusedUnitIsAFailure_AMissingInputOrAFaultIsInconclusive()
    {
        var (dv, _) = NewEnvironment();
        var verifier = new CustomerOwnerTeamsVerifier(dv, NullLogger<CustomerOwnerTeamsVerifier>.Instance);

        (await verifier.VerifyAsync(new CustomerOwnerTeamsVerificationRequest(TenantId, EnvUrl, BffAppRegId, dv.RootUnitId.ToString()), CancellationToken.None))
            .Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Failed>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitIsRoot));
        (await verifier.VerifyAsync(new CustomerOwnerTeamsVerificationRequest(TenantId, EnvUrl, BffAppRegId, string.Empty), CancellationToken.None))
            .Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Inconclusive>();
        dv.Fault = m => m == nameof(ISecureRecordSetupDataverse.FindTeamsByNameAsync)
            ? new SecureRecordSetupDataverseException(SecureRecordSetupFaultKind.Other, "503") : null;
        (await verifier.VerifyAsync(new CustomerOwnerTeamsVerificationRequest(TenantId, EnvUrl, BffAppRegId, dv.CustomerUnitId.ToString()), CancellationToken.None))
            .Should().BeOfType<CustomerOwnerTeamsVerificationOutcome.Inconclusive>("a read that fails is no verdict, never a pass");
        dv.Writes.Should().BeEmpty();
    }

    // ------------------------------------------------------------ the declared set

    [Fact]
    public void SetHash_ChangesWhenADeclaredRoleChanges()
    {
        static string Doc(string team, string role, params string[] privileges) =>
            "{\"schemaVersion\":1,\"teamName\":\"" + team + "\",\"roleName\":\"" + role + "\",\"businessUnit\":\"customer\"," +
            "\"access\":\"Read\",\"depth\":\"Basic\",\"tables\":[" +
            string.Join(",", privileges.Select(p => "{\"logicalName\":\"" + p + "\",\"privilegeName\":\"prvRead" + p + "\",\"reason\":\"r\",\"evidence\":\"e\"}")) + "]}";

        var ab = CustomerOwnerRoleSet.Parse(Doc("T1", "R1", "a"), Doc("T2", "R2", "b"));
        var abc = CustomerOwnerRoleSet.Parse(Doc("T1", "R1", "a", "c"), Doc("T2", "R2", "b"));

        H7cCustomerOwnerTeamsHandler.ComputeSetHash(abc).Should().NotBe(H7cCustomerOwnerTeamsHandler.ComputeSetHash(ab));
    }

    public static TheoryData<string, string> InvalidSets() => new()
    {
        { "depth", "\"depth\":\"Deep\"" },
        { "access", "\"access\":\"Write\"" },
        { "businessUnit", "\"businessUnit\":\"root\"" },
        { "schemaVersion", "\"schemaVersion\":2" },
    };

    [Theory]
    [MemberData(nameof(InvalidSets))]
    public void Parse_RefusesWhatTheScriptRefuses(string field, string replacement)
    {
        var good = File.ReadAllText(Path.Combine(RepoRoot(), "config", CustomerOwnerRoleSet.CustomerConfigurationFile));
        var restricted = File.ReadAllText(Path.Combine(RepoRoot(), "config", CustomerOwnerRoleSet.RestrictedContentFile));
        var original = field switch
        {
            "depth" => "\"depth\": \"Basic\"",
            "access" => "\"access\": \"Read\"",
            "businessUnit" => "\"businessUnit\": \"customer\"",
            _ => "\"schemaVersion\": 1",
        };
        good.Should().Contain(original);

        var act = () => CustomerOwnerRoleSet.Parse(restricted, good.Replace(original, replacement, StringComparison.Ordinal));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Parse_RefusesATableDeclaredByBothRoles()
    {
        var restricted = File.ReadAllText(Path.Combine(RepoRoot(), "config", CustomerOwnerRoleSet.RestrictedContentFile));
        var configuration = File.ReadAllText(Path.Combine(RepoRoot(), "config", CustomerOwnerRoleSet.CustomerConfigurationFile))
            .Replace("\"sprk_aisearchindex\"", "\"sprk_policy\"", StringComparison.Ordinal);

        var act = () => CustomerOwnerRoleSet.Parse(restricted, configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*declared by two roles*");
    }

    // ------------------------------------------------------------ helpers

    /// <summary>
    /// H7b's environment after H7b: the root, the Secure Record unit (a direct child of the root), H10's customer unit (also a
    /// direct child), and the package's metadata for every table of both declared roles.
    /// </summary>
    private static (FakeSecureRecordSetupDataverse Dv, Guid SecureId) NewEnvironment()
    {
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(SecureSet);
        var secureId = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(secureId, SecureSet.BusinessUnitName, dv.RootUnitId));
        foreach (var table in Set.Definitions.SelectMany(d => d.Tables))
        {
            dv.TableReadPrivileges[table.LogicalName] = [new SecureSetupPrivilege(Guid.NewGuid(), table.PrivilegeName)];
        }
        return (dv, secureId);
    }

    private static void AssertNoIdsRecorded(FakeRepository repo, FailureClass failureClass)
    {
        var run = repo.LastWrittenRun!;
        run.InterStepState.RestrictedContentOwnerTeamId.Should().BeNull("team ids are recorded on success only");
        run.InterStepState.CustomerConfigurationOwnerTeamId.Should().BeNull("team ids are recorded on success only");
        run.Status.Should().Be(failureClass == FailureClass.QuarantineRequired ? RunStatus.Quarantined : RunStatus.Failed);
    }

    private static H7cCustomerOwnerTeamsHandler BuildHandler(IProvisioningRunRepository repo, ISecureRecordSetupDataverse dv)
        => new(repo, dv, Options(), TimeProvider.System, NullLogger<H7cCustomerOwnerTeamsHandler>.Instance);

    private static IOptions<EnvVarValuesOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new EnvVarValuesOptions { ClientSecret = "test-client-secret-placeholder" });

    private static HandlerEnvelope Envelope() => new()
    {
        HandlerId = "H7c",
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(FakeSecureRecordSetupDataverse dv)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model1",
            Profile = "spaarke-hosted-model2",
            Status = RunStatus.Running,
        };
        run.Parameters.NonSecret["tenantId"] = TenantId;
        run.InterStepState.DataverseEnvUrl = EnvUrl;
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.CustomerBusinessUnitId = dv.CustomerUnitId.ToString();   // H10
        return run;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "config", CustomerOwnerRoleSet.RestrictedContentFile)))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string _etag = "etag-1";

        public FakeRepository(ProvisioningRun? run) => _run = run;

        public ProvisioningRun? LastWrittenRun { get; private set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null ? null : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }
}
