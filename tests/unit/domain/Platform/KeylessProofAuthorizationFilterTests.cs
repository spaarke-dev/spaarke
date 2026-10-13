using System.Security.Claims;
using FluentAssertions;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Platform;

/// <summary>
/// Task 230b — who may call the stamp BFF's keyless proof: an app-only token of this tenant, issued for this API, carrying
/// the Provisioning.KeylessProof application role. Every claim shape the inbound mapping can produce is covered.
/// </summary>
public class KeylessProofAuthorizationFilterTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string ClientId = "2f3a9c1e-7b44-4d2e-9a10-5c6d7e8f9a0b";

    [Theory]
    [InlineData("roles", "aud", "api://" + ClientId)]
    [InlineData("roles", "aud", ClientId)]
    [InlineData(ClaimTypes.Role, "aud", "api://" + ClientId)] // the mapped role claim type
    public void IsAdmitted_AnAppOnlyTokenOfThisTenantForThisApiWithTheRole_IsAdmitted(string roleClaim, string audClaim, string audience)
    {
        var user = Principal((roleClaim, KeylessProofContract.AppRoleValue), (audClaim, audience), ("tid", Tenant));

        KeylessProofAuthorizationFilter.IsAdmitted(user, Tenant, ClientId).Should().BeTrue("idtyp is absent from v1 app-only tokens");
    }

    [Fact]
    public void IsAdmitted_TheMappedTenantClaim_IsRead()
    {
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId),
            ("http://schemas.microsoft.com/identity/claims/tenantid", Tenant), ("idtyp", "app"));

        KeylessProofAuthorizationFilter.IsAdmitted(user, Tenant, ClientId).Should().BeTrue();
    }

    [Theory]
    [InlineData("scp")]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope")] // the mapped scope claim type
    public void IsAdmitted_ATokenActingForAUser_IsRefused(string scopeClaim)
    {
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId), ("tid", Tenant), (scopeClaim, "user_impersonation"));

        KeylessProofAuthorizationFilter.IsAdmitted(user, Tenant, ClientId).Should().BeFalse();
    }

    [Theory]
    [InlineData("idtyp", "user")]
    [InlineData("tid", "11111111-1111-1111-1111-111111111111")] // another tenant
    [InlineData("aud", "api://copilot-plugin")]                  // another audience the default scheme accepts
    public void IsAdmitted_ANonAppTokenOrAnotherTenantOrAudience_IsRefused(string claim, string value)
    {
        var claims = new Dictionary<string, string>
        {
            ["roles"] = KeylessProofContract.AppRoleValue,
            ["aud"] = ClientId,
            ["tid"] = Tenant,
        };
        claims[claim] = value;

        KeylessProofAuthorizationFilter.IsAdmitted(Principal(claims.Select(kv => (kv.Key, kv.Value)).ToArray()), Tenant, ClientId)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAdmitted_WithoutTheRole_IsRefused()
    {
        KeylessProofAuthorizationFilter.IsAdmitted(Principal(("roles", "Admin"), ("aud", ClientId), ("tid", Tenant)), Tenant, ClientId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null, ClientId)]
    [InlineData(Tenant, null)]
    [InlineData("", "")]
    public void IsAdmitted_WithoutTheTenantOrClientIdConfigured_RefusesEveryone(string? tenant, string? clientId)
    {
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId), ("tid", Tenant));

        KeylessProofAuthorizationFilter.IsAdmitted(user, tenant, clientId).Should().BeFalse();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test"));
}
