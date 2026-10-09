// -----------------------------------------------------------------------------
// ClientAccessPlanTests.cs
//
// T240a — H3 sets the customer BFF app registration's spa.redirectUris to exactly the customer's Dataverse origin and its
// api.preAuthorizedApplications to exactly the platform's shared clients on user_impersonation. The Graph-calling body
// stays un-unit-tested (project precedent, see GraphAppRegistrationProvisioner.cs header); the decision that makes it
// exact and idempotent is the pure PlanClientAccess, pinned here.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ClientAccessPlanTests
{
    private const string BffAppId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string AddInClientId = "c1258e2d-1688-49d2-ac99-a7485ebd9995";
    private const string TeamsClientId = "11111111-2222-3333-4444-555555555555";
    private const string Origin = "https://spaarke-acme.crm.dynamics.com";
    private static readonly Guid ScopeId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static Application App(
        IEnumerable<string>? spa = null,
        IEnumerable<PreAuthorizedApplication>? preAuthorized = null,
        bool scopeEnabled = true,
        bool withScope = true) => new()
    {
        AppId = BffAppId,
        Spa = new SpaApplication { RedirectUris = (spa ?? []).ToList() },
        Api = new ApiApplication
        {
            Oauth2PermissionScopes = withScope
                ? [new PermissionScope { Id = ScopeId, Value = "user_impersonation", IsEnabled = scopeEnabled, Type = "User" }]
                : [],
            PreAuthorizedApplications = (preAuthorized ?? []).ToList(),
        },
    };

    private static PreAuthorizedApplication Pre(string appId) => new()
    {
        AppId = appId,
        DelegatedPermissionIds = [ScopeId.ToString("D")],
    };

    [Fact]
    public void FreshApp_PlansTheSpaRedirect_AndThePreAuthorization_CarryingTheScopes()
    {
        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(App(), [Origin], [AddInClientId]);

        plan.Error.Should().BeNull();
        plan.Patch!.Spa!.RedirectUris.Should().Equal(Origin);
        var pre = plan.Patch.Api!.PreAuthorizedApplications.Should().ContainSingle().Subject;
        pre.AppId.Should().Be(AddInClientId);
        pre.DelegatedPermissionIds.Should().Equal(ScopeId.ToString("D"));
        plan.Patch.Api.Oauth2PermissionScopes.Should().ContainSingle()
            .Which.Id.Should().Be(ScopeId, "a PATCH of api must never drop the exposed scope");
    }

    [Fact]
    public void ConvergedApp_PlansNothing_SoASecondRunSendsNoPatch()
    {
        var current = App(spa: [Origin], preAuthorized: [Pre(AddInClientId)]);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId.ToUpperInvariant()]);

        plan.Should().Be(new GraphAppRegistrationProvisioner.ClientAccessPlan(null, null));
    }

    [Fact]
    public void StaleRedirectAndStalePreAuthorization_AreRemoved_TheResultIsExact()
    {
        var current = App(
            spa: [Origin, "https://evil.example.com"],
            preAuthorized: [Pre(AddInClientId), Pre(TeamsClientId)]);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId]);

        plan.Patch!.Spa!.RedirectUris.Should().Equal(Origin);
        plan.Patch.Api!.PreAuthorizedApplications!.Select(p => p.AppId).Should().Equal(AddInClientId);
    }

    [Fact]
    public void OnlyTheRedirectDiffers_ThePatchLeavesApiAlone()
    {
        var current = App(spa: [], preAuthorized: [Pre(AddInClientId)]);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId]);

        plan.Patch!.Spa!.RedirectUris.Should().Equal(Origin);
        plan.Patch.Api.Should().BeNull();
    }

    [Fact]
    public void PreAuthorizationWithTheWrongScope_IsReplaced()
    {
        var wrong = new PreAuthorizedApplication { AppId = AddInClientId, DelegatedPermissionIds = [Guid.NewGuid().ToString("D")] };

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(App(spa: [Origin], preAuthorized: [wrong]), [Origin], [AddInClientId]);

        plan.Patch!.Api!.PreAuthorizedApplications.Should().ContainSingle()
            .Which.DelegatedPermissionIds.Should().Equal(ScopeId.ToString("D"));
    }

    [Fact]
    public void TheAppsOwnId_IsNeverPreAuthorizedOnItself()
    {
        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(App(spa: [Origin]), [Origin], [BffAppId, AddInClientId]);

        plan.Patch!.Api!.PreAuthorizedApplications!.Select(p => p.AppId).Should().Equal(AddInClientId);
    }

    [Fact]
    public void NoClientsConfigured_AndNonePreAuthorized_OnlyTheRedirectIsPlanned()
    {
        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(App(), [Origin], []);

        plan.Patch!.Spa!.RedirectUris.Should().Equal(Origin);
        plan.Patch.Api.Should().BeNull();
    }

    [Theory]
    [InlineData(false, true)]   // the scope exists but is disabled
    [InlineData(true, false)]   // no user_impersonation scope at all
    public void ClientsConfigured_ButNoEnabledScope_IsAnError_NotASilentSkip(bool scopeEnabled, bool withScope)
    {
        var current = App(scopeEnabled: scopeEnabled, withScope: withScope);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId]);

        plan.Patch.Should().BeNull();
        plan.Error.Should().Contain("user_impersonation");
    }

    [Fact]
    public void ExistingApiValues_AreCarriedOnlyWhenSet()
    {
        var current = App(spa: [Origin]);
        current.Api!.RequestedAccessTokenVersion = 2;

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId]);

        plan.Patch!.Api!.RequestedAccessTokenVersion.Should().Be(2);
        plan.Patch.Api.KnownClientApplications.Should().BeNull("an explicit null would ask Graph to clear the list");
        plan.Patch.Api.AcceptMappedClaims.Should().BeNull();
    }

    [Fact]
    public void KnownClientsAndAcceptMappedClaims_AreCarriedWhenSet()
    {
        var known = Guid.NewGuid();
        var current = App(spa: [Origin]);
        current.Api!.KnownClientApplications = [known];
        current.Api.AcceptMappedClaims = true;

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], [AddInClientId]);

        plan.Patch!.Api!.KnownClientApplications.Should().Equal(known);
        plan.Patch.Api.AcceptMappedClaims.Should().BeTrue();
    }

    [Fact]
    public void NoClientsConfigured_ButSomePreAuthorized_RemovesThemAll()
    {
        var current = App(spa: [Origin], preAuthorized: [Pre(AddInClientId), Pre(TeamsClientId)]);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], []);

        plan.Patch!.Spa.Should().BeNull("the redirect already matches");
        plan.Patch.Api!.PreAuthorizedApplications.Should().BeEmpty();
        plan.Patch.Api.Oauth2PermissionScopes.Should().ContainSingle();
    }

    [Fact]
    public void NullLists_LeaveTheRegistrationUntouched_NeverClearIt()
    {
        var current = App(spa: ["https://somewhere.example.com"], preAuthorized: [Pre(TeamsClientId)]);

        GraphAppRegistrationProvisioner.PlanClientAccess(current, null, null).Patch.Should().BeNull();
        var spaOnly = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin], null);
        spaOnly.Patch!.Spa!.RedirectUris.Should().Equal(Origin);
        spaOnly.Patch.Api.Should().BeNull("null pre-authorizations are left as they are");
    }

    [Fact]
    public void RedirectOrderAndDuplicates_DoNotCountAsAChange()
    {
        const string api = "https://spaarke-acme.api.crm.dynamics.com";
        var current = App(spa: [api, Origin], preAuthorized: []);

        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(current, [Origin, api, Origin], []);

        plan.Patch.Should().BeNull();
    }
}
