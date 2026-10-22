// -----------------------------------------------------------------------------
// UnverifiedEmailClaimPlanTests.cs
//
// T264 (#2001 from SPA-r3 ISS-013) — H3 sets authenticationBehaviors.removeUnverifiedEmailClaim = true on every
// per-customer BFF registration: a registration without it (null / false / only other values set) gets a PATCH body
// carrying true with the other two values preserved (Graph replaces the whole complex type), and a registration that
// already has it is not written. The Graph-calling body stays un-unit-tested (project precedent,
// GraphAppRegistrationProvisioner.cs header); the decision is the pure PlanRemoveUnverifiedEmailClaim, pinned here.
// Sibling of AcctOptionalClaimPlanTests.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class UnverifiedEmailClaimPlanTests
{
    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_NoAuthenticationBehaviors_PlansTrue()
    {
        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(null);

        plan.Should().NotBeNull();
        plan!.RemoveUnverifiedEmailClaim.Should().BeTrue();
        plan.BlockAzureADGraphAccess.Should().BeNull("nothing to carry over");
        plan.RequireClientServicePrincipal.Should().BeNull();
        plan.BackingStore.Enumerate().Select(e => e.Key).Should()
            .Contain("removeUnverifiedEmailClaim")
            .And.NotContain(["blockAzureADGraphAccess", "requireClientServicePrincipal"],
                "an unset value is not assigned, so the PATCH carries no explicit null for it");
    }

    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_FlagFalse_PlansTrue()
    {
        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(
            new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = false });

        plan.Should().NotBeNull();
        plan!.RemoveUnverifiedEmailClaim.Should().BeTrue();
    }

    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_FlagAlreadyTrue_ReturnsNullSoNothingIsWritten()
    {
        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(
            new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = true });

        plan.Should().BeNull("a second run sends no PATCH");
    }

    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_OnlyOtherValuesSet_PlansTrueAndPreservesTheOthers()
    {
        var current = new AuthenticationBehaviors
        {
            RemoveUnverifiedEmailClaim = null,
            BlockAzureADGraphAccess = true,
            RequireClientServicePrincipal = true,
        };

        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(current);

        plan.Should().NotBeNull();
        plan!.RemoveUnverifiedEmailClaim.Should().BeTrue();
        plan.BlockAzureADGraphAccess.Should().BeTrue("Graph replaces the whole complex type, so the other values must be carried");
        plan.RequireClientServicePrincipal.Should().BeTrue();
    }

    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_ExplicitFalseOtherValues_AreCarriedAsFalse()
    {
        var current = new AuthenticationBehaviors
        {
            RemoveUnverifiedEmailClaim = false,
            BlockAzureADGraphAccess = false,
            RequireClientServicePrincipal = false,
        };

        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(current);

        plan!.BlockAzureADGraphAccess.Should().BeFalse();
        plan.RequireClientServicePrincipal.Should().BeFalse();
    }

    [Fact]
    public void PlanRemoveUnverifiedEmailClaim_PlanIsANewObject_NotTheModelReadFromGraph()
    {
        var current = new AuthenticationBehaviors { RemoveUnverifiedEmailClaim = false, BlockAzureADGraphAccess = true };

        var plan = GraphAppRegistrationProvisioner.PlanRemoveUnverifiedEmailClaim(current);

        plan.Should().NotBeSameAs(current,
            "a model read from Graph is not re-serialised whole in a PATCH — the SDK's backing store sends only changed values");
        current.RemoveUnverifiedEmailClaim.Should().BeFalse("the input is not mutated");
    }
}
