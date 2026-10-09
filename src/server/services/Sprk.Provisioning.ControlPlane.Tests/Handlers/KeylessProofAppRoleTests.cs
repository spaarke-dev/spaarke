// -----------------------------------------------------------------------------
// KeylessProofAppRoleTests.cs
//
// Task 230b — H3 defines the keyless-proof application role on the customer's BFF app registration and assigns it to
// the L2 Worker identity, idempotently. The provisioner's Graph-calling bodies stay un-unit-tested (project precedent,
// see GraphAppRegistrationProvisioner.cs header); the decisions that make create / reconcile / assignment idempotent
// are pure internal statics and are pinned here.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Spaarke.Contracts.Provisioning;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class KeylessProofAppRoleTests
{
    private static readonly Guid ContractRoleId = Guid.Parse(KeylessProofContract.AppRoleId);

    [Fact]
    public void BuildKeylessProofAppRole_IsAnEnabledApplicationOnlyRole_WithTheContractIdAndValue()
    {
        var role = GraphAppRegistrationProvisioner.BuildKeylessProofAppRole();

        role.Value.Should().Be(KeylessProofContract.AppRoleValue);
        role.Id.Should().Be(ContractRoleId);
        role.AllowedMemberTypes.Should().Equal("Application"); // no user can ever be assigned it
        role.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void PlanKeylessProofAppRoles_AnAppWithoutTheRole_AddsIt_AndKeepsEveryOtherRole()
    {
        var other = new AppRole { Id = Guid.NewGuid(), Value = "Admin", AllowedMemberTypes = ["User"], IsEnabled = true };

        var planned = GraphAppRegistrationProvisioner.PlanKeylessProofAppRoles([other]);

        planned.Should().NotBeNull();
        planned!.Should().HaveCount(2);
        planned.Should().Contain(other);
        planned.Should().ContainSingle(r => r.Value == KeylessProofContract.AppRoleValue && r.Id == ContractRoleId);
    }

    [Fact]
    public void PlanKeylessProofAppRoles_TheRoleAlreadyCorrect_ChangesNothing()
    {
        var planned = GraphAppRegistrationProvisioner.PlanKeylessProofAppRoles([GraphAppRegistrationProvisioner.BuildKeylessProofAppRole()]);

        planned.Should().BeNull("a re-run against a reconciled app must not PATCH it");
    }

    [Fact]
    public void PlanKeylessProofAppRoles_ADisabledOrUserAssignableRole_IsRepaired_KeepingItsId()
    {
        var existingId = Guid.NewGuid();
        var drifted = new AppRole
        {
            Id = existingId,
            Value = KeylessProofContract.AppRoleValue,
            AllowedMemberTypes = ["User", "Application"],
            IsEnabled = false,
        };

        var planned = GraphAppRegistrationProvisioner.PlanKeylessProofAppRoles([drifted]);

        planned.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = (Guid?)existingId,
            Value = KeylessProofContract.AppRoleValue,
            AllowedMemberTypes = new[] { "Application" },
            IsEnabled = (bool?)true,
        }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public void KeylessProofRoleId_UsesTheAppsExistingRoleId_ElseTheContractId()
    {
        var existingId = Guid.NewGuid();

        GraphAppRegistrationProvisioner.KeylessProofRoleId([new AppRole { Id = existingId, Value = KeylessProofContract.AppRoleValue }])
            .Should().Be(existingId);
        GraphAppRegistrationProvisioner.KeylessProofRoleId(null).Should().Be(ContractRoleId);
    }

    [Fact]
    public void HasRoleAssignment_MatchesOnPrincipalAndRole_Only()
    {
        var l2 = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var assignments = new[]
        {
            new AppRoleAssignment { PrincipalId = someoneElse, AppRoleId = ContractRoleId },
            new AppRoleAssignment { PrincipalId = l2, AppRoleId = Guid.NewGuid() },
        };

        GraphAppRegistrationProvisioner.HasRoleAssignment(assignments, l2, ContractRoleId).Should().BeFalse();
        GraphAppRegistrationProvisioner.HasRoleAssignment(
                assignments.Append(new AppRoleAssignment { PrincipalId = l2, AppRoleId = ContractRoleId }), l2, ContractRoleId)
            .Should().BeTrue("an existing assignment makes the step a no-op");
        GraphAppRegistrationProvisioner.HasRoleAssignment(null, l2, ContractRoleId).Should().BeFalse();
    }

    [Fact]
    public void ForeignRoleHolders_AreEveryOtherHolderOfThisRole_Only()
    {
        var l2 = Guid.NewGuid();
        var assignments = new[]
        {
            new AppRoleAssignment { Id = "a", PrincipalId = l2, AppRoleId = ContractRoleId },
            new AppRoleAssignment { Id = "b", PrincipalId = Guid.NewGuid(), AppRoleId = ContractRoleId }, // a test app: removed
            new AppRoleAssignment { Id = "c", PrincipalId = Guid.NewGuid(), AppRoleId = Guid.NewGuid() }, // another role: kept
        };

        GraphAppRegistrationProvisioner.ForeignRoleHolders(assignments, l2, ContractRoleId).Select(a => a.Id).Should().Equal("b");
    }
}
