// -----------------------------------------------------------------------------
// AcctOptionalClaimPlanTests.cs
//
// T255 (INCOMING-141 §5, unified-access-control-r2 task 141) — H3 puts the `acct` optional claim on every per-customer
// BFF registration's ACCESS tokens: a new registration is created with it, an existing one gets it added with every
// other optional claim preserved, and a registration that has it is not written. The Graph-calling body stays
// un-unit-tested (project precedent, GraphAppRegistrationProvisioner.cs header); the decision is the pure
// PlanAcctOptionalClaim, pinned here. Parity with the retired script's Get-SpaarkeAccessTokenOptionalClaimsWithAcct.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class AcctOptionalClaimPlanTests
{
    [Fact]
    public void NewRegistration_IsCreatedWithAcctOnTheAccessTokens()
    {
        var plan = GraphAppRegistrationProvisioner.PlanAcctOptionalClaim(null);

        plan.Should().NotBeNull();
        plan!.AccessToken.Should().ContainSingle().Which.Name.Should().Be("acct");
        plan.AccessToken![0].Essential.Should().BeFalse();
        plan.IdToken.Should().BeEmpty();
        plan.Saml2Token.Should().BeEmpty();
    }

    [Fact]
    public void ExistingRegistrationWithoutAcct_GetsItAdded_KeepingEveryOtherClaim()
    {
        // The dev registration carries email, preferred_username and upn on its access tokens (retired script note).
        var current = new OptionalClaims
        {
            AccessToken =
            [
                new OptionalClaim { Name = "email", Essential = false, AdditionalProperties = [] },
                new OptionalClaim { Name = "upn", Essential = true, Source = null, AdditionalProperties = ["include_externally_authenticated_upn"] },
            ],
            IdToken = [new OptionalClaim { Name = "preferred_username", Essential = false, AdditionalProperties = [] }],
            Saml2Token = [new OptionalClaim { Name = "groups", Source = "user", AdditionalProperties = [] }],
        };

        var plan = GraphAppRegistrationProvisioner.PlanAcctOptionalClaim(current);

        plan.Should().NotBeNull("acct is missing");
        plan!.AccessToken!.Select(c => c.Name).Should().Equal("email", "upn", "acct");
        plan.AccessToken![1].Essential.Should().BeTrue();
        plan.AccessToken![1].AdditionalProperties.Should().Equal("include_externally_authenticated_upn");
        plan.IdToken!.Select(c => c.Name).Should().Equal(["preferred_username"],
            "Graph replaces the whole optionalClaims object, so the id-token claims must be carried");
        plan.Saml2Token!.Single().Source.Should().Be("user");
        plan.AccessToken.Should().NotContain(c => current.AccessToken!.Contains(c),
            "every claim is copied into a new object — a model read from Graph is not re-serialised whole in a PATCH");
    }

    [Fact]
    public void ExistingRegistrationWithAcct_IsNotWritten()
    {
        var current = new OptionalClaims
        {
            AccessToken = [new OptionalClaim { Name = "email" }, new OptionalClaim { Name = "acct" }],
        };

        GraphAppRegistrationProvisioner.PlanAcctOptionalClaim(current).Should().BeNull("a second run sends no PATCH");
    }

    [Fact]
    public void AcctOnlyOnTheIdToken_DoesNotCount_TheBffReadsAccessTokens()
    {
        var current = new OptionalClaims { IdToken = [new OptionalClaim { Name = "acct" }] };

        var plan = GraphAppRegistrationProvisioner.PlanAcctOptionalClaim(current);

        plan!.AccessToken!.Should().ContainSingle(c => c.Name == "acct");
        plan.IdToken!.Should().ContainSingle(c => c.Name == "acct", "the id-token claim is kept as it was");
    }
}
