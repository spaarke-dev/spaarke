using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    private const string LegacyTenantClaim = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string OtherTenant = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void IsAdmitted_WhenTidMatchesTheConfiguredTenantAndNoLegacyClaimIsPresent_IsAdmitted()
    {
        // `tid` is the primary tenant claim: with it present and matching, the legacy mapped claim is not needed.
        // (Which claim wins when BOTH are present and disagree is deliberately not pinned: real Entra tokens never
        // carry both with different values, and a stricter "conflicting claims -> refuse" policy must stay possible.
        // The sibling test below is what pins that `tid` is read before the legacy claim.)
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId),
            ("tid", Tenant));

        KeylessProofAuthorizationFilter.IsAdmitted(user, Tenant, ClientId).Should().BeTrue();
    }

    [Fact]
    public void IsAdmitted_WhenTidIsAnotherTenantButLegacyTenantClaimMatches_IsRefused()
    {
        // The security direction: the legacy claim must never be able to launder a token whose `tid` names
        // another tenant (the app registration is multi-tenant).
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId),
            ("tid", OtherTenant), (LegacyTenantClaim, Tenant));

        KeylessProofAuthorizationFilter.IsAdmitted(user, Tenant, ClientId).Should().BeFalse();
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

    // ─────────────────────────────────────────────────────────────────────────
    // The filter's own responses. The census contract tests drive the route through the host, where the
    // authorization middleware answers an unauthenticated caller before this filter runs, so the filter's
    // own 401 (defence in depth) has no other coverage, and its 403 body is only pinned by status code.
    // ─────────────────────────────────────────────────────────────────────────

    private const string Corr = "corr-1800-keyless";

    private static async Task<(int Status, JsonElement Body, bool NextCalled)> InvokeAsync(ClaimsPrincipal user)
    {
        var ctx = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = Corr,
            RequestServices = new ServiceCollection()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AzureAd:TenantId"] = Tenant,
                        ["AzureAd:ClientId"] = ClientId,
                    })
                    .Build())
                .BuildServiceProvider(),
        };
        var nextCalled = false;

        var result = await new KeylessProofAuthorizationFilter().InvokeAsync(
            new DefaultEndpointFilterInvocationContext(ctx),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });

        if (!nextCalled)
        {
            ctx.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
            using var buffer = new MemoryStream();
            ctx.Response.Body = buffer;
            await ((IResult)result!).ExecuteAsync(ctx);
            return (ctx.Response.StatusCode, JsonDocument.Parse(buffer.ToArray()).RootElement.Clone(), false);
        }

        return (StatusCodes.Status200OK, default, true);
    }

    [Fact]
    public async Task InvokeAsync_ForAnUnauthenticatedCaller_Answers401WithTheUnauthenticatedReasonCodeAndTraceId()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var (status, body, nextCalled) = await InvokeAsync(anonymous);

        status.Should().Be(401);
        nextCalled.Should().BeFalse();
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.GetProperty("detail").GetString().Should().Be("An application token for this API is required.");
        body.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.unauthenticated");
        body.GetProperty("traceId").GetString().Should().Be(Corr);
    }

    [Fact]
    public async Task InvokeAsync_ForAnAuthenticatedCallerWithoutTheRole_Answers403NamingTheRoleAndCarryingTheTraceId()
    {
        var user = Principal(("roles", "Admin"), ("aud", ClientId), ("tid", Tenant));

        var (status, body, nextCalled) = await InvokeAsync(user);

        status.Should().Be(403);
        nextCalled.Should().BeFalse();
        body.GetProperty("title").GetString().Should().Be("Forbidden");
        body.GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.keyless_proof_role");
        body.GetProperty("detail").GetString().Should().Be(
            $"Only an application holding the '{KeylessProofContract.AppRoleValue}' role may call this endpoint.");
        body.GetProperty("traceId").GetString().Should().Be(Corr);
    }

    [Fact]
    public async Task InvokeAsync_ForAnAdmittedApplicationToken_RunsTheHandler()
    {
        // Positive control: a filter that denied everyone would satisfy both tests above.
        var user = Principal(("roles", KeylessProofContract.AppRoleValue), ("aud", ClientId), ("tid", Tenant));

        var (_, _, nextCalled) = await InvokeAsync(user);

        nextCalled.Should().BeTrue();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test"));
}
