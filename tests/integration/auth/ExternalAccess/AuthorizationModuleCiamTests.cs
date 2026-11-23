// spaarke-SPA-external-access-platform-r3 — Task 010 (2026-10-10)
// FR-22: the Ciam JwtBearer scheme accepts the per-customer audience in both forms ({appId}, api://{appId}).
// FR-23: the default (workforce) JwtBearer scheme rejects any CIAM token (iss ∋ ciamlogin.com, or tid == Ciam:TenantId).
// Task 077 (2026-10-19): the default scheme also accepts the bare app-id audience (v2 access tokens, ADR-028 A9).
//
// Every test runs the REAL schemes registered by AuthorizationModule.AddAuthorizationModule behind a TestServer
// and asserts the HTTP status a route returns. Only two things are replaced, both test-side and both outside the
// behaviour under test:
//   1. Signing keys/issuer metadata come from a StaticConfigurationManager holding a local RSA key, so no request
//      reaches login.microsoftonline.com or ciamlogin.com.
//   2. On the DEFAULT scheme only, the issuer validator accepts any issuer. That models the worst case FR-23 is
//      written for — the library's issuer validation letting a CIAM-tenant token through — so the 401s below are
//      produced by the guard alone, not by library issuer defaults (the spec forbids relying on those).
//
// ASSUMPTION — token shapes NOT yet proven live (provisioning T240d spike S1 (iv) is pending; see
// projects/spaarke-SPA-external-access-platform-r3/notes/coordination/2026-10-10-from-provisioning-t240d-s1-partial.md):
//   - a CIAM user access token carries iss = https://{ciamTenantId}.ciamlogin.com/{ciamTenantId}/v2.0,
//     tid = {ciamTenantId}, and aud = the stamp's BFF app id as a bare GUID (v2) or api://{appId} (v1);
//   - those are the spec's two forms (FR-22). When S1 (iv) lands, replace the Ciam token cases with its shape;
//     if it contradicts the two forms, escalate (POML 010 escalation trigger 2).
// Tests that depend on this assumption carry "AssumedS1Shape" in their name.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public sealed class AuthorizationModuleCiamTests
{
    private const string WorkforceTenantId = "11111111-1111-1111-1111-111111111111";
    private const string WorkforceBffClientId = "22222222-2222-2222-2222-222222222222";
    // The workforce BFF audience as deployed (appsettings.template.json AzureAd:Audience = api://{API_APP_ID}).
    private const string WorkforceBffAudience = "api://" + WorkforceBffClientId;
    private const string CiamTenantId = "33333333-3333-3333-3333-333333333333";
    private const string StampCiamAppId = "44444444-4444-4444-4444-444444444444";
    private const string OtherCustomerCiamAppId = "55555555-5555-5555-5555-555555555555";
    private const string CopilotAudience = "api://auth-66666666-6666-6666-6666-666666666666";

    private const string CiamIssuer = $"https://{CiamTenantId}.ciamlogin.com/{CiamTenantId}/v2.0";
    private const string WorkforceIssuerV2 = $"https://login.microsoftonline.com/{WorkforceTenantId}/v2.0";
    private const string WorkforceIssuerV1 = $"https://sts.windows.net/{WorkforceTenantId}/";
    // The CIAM tenant issuing through the workforce authority (app-only tokens use this authority — S1 (i)).
    private const string CiamTenantViaWorkforceAuthorityIssuer = $"https://login.microsoftonline.com/{CiamTenantId}/v2.0";

    private const string WorkforceRoute = "/workforce";     // default policy → default (workforce) scheme
    private const string CiamRoute = "/ciam";               // AuthPolicies.CiamExternal → Ciam scheme only
    private const string CollaborationRoute = "/collab";    // AuthPolicies.ExternalCollaboration → both schemes

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "task010-test-key" };

    // ───────────────────────────── FR-22: Ciam scheme audiences ─────────────────────────────

    [Theory]
    [InlineData(StampCiamAppId, StampCiamAppId)]
    [InlineData(StampCiamAppId, "api://" + StampCiamAppId)]
    [InlineData("api://" + StampCiamAppId, StampCiamAppId)]
    [InlineData("api://" + StampCiamAppId, "api://" + StampCiamAppId)]
    public async Task CiamScheme_AssumedS1Shape_StampAudienceInEitherForm_Returns200(string configuredAudience, string tokenAudience)
    {
        using var host = await StartHostAsync(ciamAudience: configuredAudience, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, CiamRoute, CiamToken(tokenAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(OtherCustomerCiamAppId)]
    [InlineData("api://" + OtherCustomerCiamAppId)]
    public async Task CiamScheme_AssumedS1Shape_OtherCustomersAudience_Returns401(string tokenAudience)
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, CiamRoute, CiamToken(tokenAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CiamScheme_AudienceUnconfigured_RejectsEveryAudience_Returns401(string configuredAudience)
    {
        using var host = await StartHostAsync(ciamAudience: configuredAudience, ciamTenantId: CiamTenantId);

        (await GetAsync(host, CiamRoute, CiamToken(StampCiamAppId))).Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, CiamRoute, CiamToken("api://" + StampCiamAppId))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CiamScheme_NonGuidIdentifierUri_DoesNotDeriveABareForm_Returns401()
    {
        // The other form is derived only for a GUID app id; a non-GUID identifier URI is not widened.
        using var host = await StartHostAsync(ciamAudience: "api://spaarke-bff", ciamTenantId: CiamTenantId);

        (await GetAsync(host, CiamRoute, CiamToken("spaarke-bff"))).Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, CiamRoute, CiamToken("api://spaarke-bff"))).Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── FR-23: default-scheme CIAM guard ─────────────────────────────

    [Fact]
    public async Task DefaultScheme_WorkforceToken_Returns200()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_CopilotAudienceToken_Returns200()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(WorkforceIssuerV1, WorkforceTenantId, CopilotAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(CiamIssuer)]
    [InlineData("https://CONTOSO.CIAMLOGIN.COM/" + CiamTenantId + "/v2.0")]
    public async Task DefaultScheme_AssumedS1Shape_CiamIssuerToken_Returns401(string issuer)
    {
        // tid is deliberately NOT the CIAM tenant, so only the issuer rule can reject this token; the audience is
        // the workforce BFF's, so audience validation cannot be what rejects it.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(issuer, WorkforceTenantId, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_CiamTenantIdToken_Returns401()
    {
        // Issuer is the workforce authority, so only the tid rule can reject this token.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(CiamTenantViaWorkforceAuthorityIssuer, CiamTenantId, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_CiamTenantIdUnset_StillRejectsCiamIssuer_Returns401()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: null);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(CiamIssuer, WorkforceTenantId, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_CiamTenantIdUnset_OtherwiseBehavesAsBefore_Returns200()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: null);

        // With no Ciam:TenantId there is no tid rule: a non-ciamlogin token is judged exactly as before.
        (await GetAsync(host, WorkforceRoute,
            WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, WorkforceBffAudience)))
            .Should().Be(HttpStatusCode.OK);
        (await GetAsync(host, WorkforceRoute,
            WorkforceToken(CiamTenantViaWorkforceAuthorityIssuer, CiamTenantId, WorkforceBffAudience)))
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExternalCollaborationPolicy_AssumedS1Shape_CiamToken_StillReturns200()
    {
        // The guard runs on the default scheme, which ExternalCollaboration also lists; a CIAM token must still
        // authenticate there through the Ciam scheme (the guard must not break the external plane).
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, CollaborationRoute, CiamToken(StampCiamAppId));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_NoTidClaim_CiamTenantInIssuerPath_Returns401()
    {
        // K4: a token with NO tid claim issued through the workforce authority by the CIAM tenant. Neither the
        // ciamlogin.com rule nor the tid rule can see it; only the issuer-path tenant rule can reject it.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(CiamTenantViaWorkforceAuthorityIssuer, tenantId: null, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_NoTidClaim_CustomDomainIssuerWithCiamTenantInPath_Returns401()
    {
        // K4: CIAM custom URL domain (not *.ciamlogin.com), tid absent. Relies on Ciam:TenantId being set
        // (provisioning H4b always sets it).
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken($"https://login.contoso.com/{CiamTenantId}/v2.0", tenantId: null, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_NoTidClaim_V1IssuerWithCiamTenantInPath_Returns401()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken($"https://sts.windows.net/{CiamTenantId}/", tenantId: null, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_NoTidClaim_WorkforceIssuerPath_Returns200()
    {
        // The issuer-path rule only fires on the CIAM tenant; a workforce tenant in the path is untouched.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(WorkforceIssuerV2, tenantId: null, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_CiamTenantIdUnset_IssuerPathRuleDoesNotFire_Returns200()
    {
        // Behaviour is unchanged when Ciam:TenantId is unset: no issuer-path match is possible.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: null);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(CiamTenantViaWorkforceAuthorityIssuer, tenantId: null, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_CiamToken_DenyWarningCarriesIssuerHostAndTid_NeverTheToken()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId, loggerProvider: logs);
        var token = WorkforceToken(CiamTenantViaWorkforceAuthorityIssuer, CiamTenantId, WorkforceBffAudience);

        var status = await GetAsync(host, WorkforceRoute, token);

        status.Should().Be(HttpStatusCode.Unauthorized);
        var deny = logs.Entries
            .Where(e => e.Message.Contains("sdap.auth.deny.ciam_token_on_workforce_scheme"))
            .Should().ContainSingle().Subject;
        deny.Properties.Should().Contain(new KeyValuePair<string, object?>("IssuerHost", "login.microsoftonline.com"));
        deny.Properties.Should().Contain(new KeyValuePair<string, object?>("Tid", CiamTenantId));
        logs.Entries.Any(e => e.Message.Contains(token) ||
                              e.Properties.Any(p => (p.Value?.ToString() ?? string.Empty).Contains(token)))
            .Should().BeFalse("the token must never be logged");
    }

    // ───────────────────── startup refusal: Ciam:TenantId must differ from AzureAd:TenantId ─────────────────────

    [Theory]
    [InlineData(WorkforceTenantId)]
    [InlineData("11111111111111111111111111111111")]            // same GUID, "N" format
    [InlineData("  11111111-1111-1111-1111-111111111111  ")]    // same GUID, padded
    public void AddAuthorizationModule_CiamTenantEqualsWorkforceTenant_RefusesToStart(string ciamTenantId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = WorkforceTenantId,
            ["Ciam:TenantId"] = ciamTenantId,
        }).Build();

        var act = () => new ServiceCollection().AddAuthorizationModule(configuration);

        act.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("Ciam:TenantId") && e.Message.Contains("AzureAd:TenantId"));
    }

    [Fact]
    public void AddAuthorizationModule_CiamTenantEqualsWorkforceTenant_UppercaseGuid_RefusesToStart()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            ["Ciam:TenantId"] = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
        }).Build();

        var act = () => new ServiceCollection().AddAuthorizationModule(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(CiamTenantId)]                  // distinct GUIDs
    [InlineData(null)]                          // unset
    [InlineData("")]                            // blank
    [InlineData("#{CIAM_TENANT_ID}#")]          // unreplaced deployment placeholder
    [InlineData("not-a-guid")]                  // non-GUID
    public void AddAuthorizationModule_CiamTenantDistinctOrUnsetOrPlaceholder_Starts(string? ciamTenantId)
    {
        var settings = new Dictionary<string, string?> { ["AzureAd:TenantId"] = WorkforceTenantId };
        if (ciamTenantId is not null) settings["Ciam:TenantId"] = ciamTenantId;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var act = () => new ServiceCollection().AddAuthorizationModule(configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddAuthorizationModule_NonGuidTenantsThatAreEqualStrings_Starts()
    {
        // Only GUID values are compared: two equal non-GUID strings (placeholders, test fixtures) never trigger it.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "#{TENANT_ID}#",
            ["Ciam:TenantId"] = "#{TENANT_ID}#",
        }).Build();

        var act = () => new ServiceCollection().AddAuthorizationModule(configuration);

        act.Should().NotThrow();
    }

    // ─────────── Task 077: default scheme accepts the bare app-id audience (v2 access tokens) ───────────
    // ADR-028 A9 sets requestedAccessTokenVersion = 2 on the BFF app, which makes aud the bare app id for EVERY caller.
    // The default scheme must accept that form as well as api://{appId}, derived from AzureAd:ClientId, and nothing else.

    [Theory]
    [InlineData(WorkforceIssuerV2)]
    [InlineData(WorkforceIssuerV1)]
    public async Task DefaultScheme_BareClientIdAudience_Returns200(string issuer)
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute, WorkforceToken(issuer, WorkforceTenantId, WorkforceBffClientId));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_ApiUriAudienceWithV1Issuer_StillReturns200()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute,
            WorkforceToken(WorkforceIssuerV1, WorkforceTenantId, WorkforceBffAudience));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(OtherCustomerCiamAppId)]
    [InlineData("api://" + OtherCustomerCiamAppId)]
    [InlineData("api://")]
    public async Task DefaultScheme_AnotherAppsAudience_Returns401(string audience)
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute, WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, audience));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(CiamIssuer, CiamTenantId)]
    [InlineData(CiamTenantViaWorkforceAuthorityIssuer, CiamTenantId)]
    public async Task DefaultScheme_CiamTokenWithBareClientIdAudience_Returns401(string issuer, string tenantId)
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var status = await GetAsync(host, WorkforceRoute, WorkforceToken(issuer, tenantId, WorkforceBffClientId));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefaultScheme_BareClientIdAudience_EveryHostInTheProcess_Returns200()
    {
        // The bare-id audience (task 077) comes from its own PostConfigure, not the Copilot merge; every host gets it.
        using var first = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);
        using var second = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var token = WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, WorkforceBffClientId);

        (await GetAsync(first, WorkforceRoute, token)).Should().Be(HttpStatusCode.OK);
        (await GetAsync(second, WorkforceRoute, token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DefaultScheme_ResolvedOptions_AudiencesAddedAndValidationFlagsKept()
    {
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);

        var parameters = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        parameters.ValidAudiences.Should().Contain(new[] { WorkforceBffClientId, WorkforceBffAudience, CopilotAudience });
        parameters.ValidAudiences.Should().NotContain(a => a == "api://" || string.IsNullOrWhiteSpace(a));
        parameters.ValidateAudience.Should().BeTrue();
        parameters.ValidateIssuer.Should().BeTrue();
        parameters.ValidateLifetime.Should().BeTrue();
        parameters.RequireSignedTokens.Should().BeTrue();
    }

    [Fact]
    public async Task GuestTokenParameters_V2GuestTokenWithBareStampAppId_Validates()
    {
        // SpaarkeGuestTokenValidator clones the default scheme's parameters and hands them the scheme's configuration
        // manager; this repeats exactly that against the REAL registered scheme.
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId);
        var options = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters.Clone();
        parameters.ConfigurationManager = (BaseConfigurationManager)options.ConfigurationManager!;

        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(
            WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, WorkforceBffClientId), parameters);

        result.IsValid.Should().BeTrue(result.Exception?.Message);
    }

    [Fact]
    public void GuestTokenDenyReason_CopilotAudience_StillRefused()
    {
        var jwt = new JsonWebToken(WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, CopilotAudience));

        var reason = SpaarkeGuestTokenValidator.DenyReason(
            jwt, Guid.NewGuid(), "someone@example.com", WorkforceTenantId, WorkforceBffClientId, out _);

        reason.Should().Be(GuestTokenDeny.Invalid);
    }

    [Fact]
    public async Task DefaultScheme_IdTokenForTheApp_WithRoles_Returns401()
    {
        // 077 review F4: an ID token issued to a client signing in AS the BFF app has aud = the bare app id. With an app
        // role it carries roles (so Identity Web's scp-or-roles check passes) but never azpacr/appidacr.
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(ciamAudience: StampCiamAppId, ciamTenantId: CiamTenantId, loggerProvider: logs);

        var status = await GetAsync(host, WorkforceRoute, IdToken(WorkforceBffClientId));

        status.Should().Be(HttpStatusCode.Unauthorized);
        logs.Entries.Should().ContainSingle(e => e.Message.Contains("sdap.auth.deny.id_token_on_workforce_scheme"),
            "the refusal must come from the 077 guard, not another 401 source");
    }

    [Theory]
    [InlineData(WorkforceBffClientId, false, true)]
    [InlineData("22222222-2222-2222-2222-222222222222", true, false)]  // v2 access token (azpacr)
    [InlineData(WorkforceBffAudience, false, false)]                   // other audiences are not judged here
    public void IsIdTokenForApp_JudgesOnlyTheBareAppIdAudience(string audience, bool accessToken, bool expected)
    {
        var token = accessToken
            ? WorkforceToken(WorkforceIssuerV2, WorkforceTenantId, audience)
            : IdToken(audience);

        var jwt = new JsonWebToken(token);
        AuthorizationModule.IsIdTokenForApp(jwt, null, WorkforceBffClientId).Should().Be(expected);
        AuthorizationModule.IsIdTokenForApp(jwt, null, null).Should().BeFalse();

        // Any other token type: judged from the principal's claims (077 re-check K2), with the same answer.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test"));
        AuthorizationModule.IsIdTokenForApp(null, principal, WorkforceBffClientId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddWorkforceAudiences_ClientIdUnsetOrBlank_LeavesParametersUnchanged(string? clientId)
    {
        var parameters = new TokenValidationParameters { ValidAudience = WorkforceBffAudience };

        AuthorizationModule.AddWorkforceAudiences(parameters, clientId);

        parameters.ValidAudience.Should().Be(WorkforceBffAudience);
        parameters.ValidAudiences.Should().BeNull();
    }

    [Fact]
    public void AddWorkforceAudiences_MergesBothFormsKeepsExistingAndIsIdempotent()
    {
        IssuerValidator issuerValidator = (issuer, _, _) => issuer;
        var parameters = new TokenValidationParameters
        {
            ValidAudience = WorkforceBffAudience,
            ValidAudiences = new[] { CopilotAudience },
            ValidIssuers = new[] { WorkforceIssuerV2 },
            IssuerValidator = issuerValidator,
        };

        AuthorizationModule.AddWorkforceAudiences(parameters, WorkforceBffClientId);
        AuthorizationModule.AddWorkforceAudiences(parameters, WorkforceBffClientId);

        parameters.ValidAudience.Should().BeNull();
        parameters.ValidAudiences.Should().BeEquivalentTo(new[] { WorkforceBffAudience, WorkforceBffClientId, CopilotAudience });
        parameters.ValidIssuers.Should().BeEquivalentTo(new[] { WorkforceIssuerV2 });
        parameters.IssuerValidator.Should().BeSameAs(issuerValidator);
    }

    // ───────────────────────────── harness ─────────────────────────────

    private static async Task<IHost> StartHostAsync(
        string ciamAudience, string? ciamTenantId, ILoggerProvider? loggerProvider = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = WorkforceTenantId,
            ["AzureAd:ClientId"] = WorkforceBffClientId,
            ["AzureAd:Audience"] = WorkforceBffAudience,
            ["AgentToken:CopilotAudience"] = CopilotAudience,
            ["Ciam:Instance"] = $"https://{CiamTenantId}.ciamlogin.com/",
            ["Ciam:Audience"] = ciamAudience,
        };
        if (ciamTenantId is not null)
        {
            settings["Ciam:TenantId"] = ciamTenantId;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddLogging(logging =>
                    {
                        if (loggerProvider is not null) logging.AddProvider(loggerProvider);
                    });
                    services.AddRouting();
                    services.AddAuthorizationModule(configuration);
                    // ResourceAccessHandler serves per-record policies none of these routes use, and needs the
                    // Dataverse authorization stack; authentication is what is under test.
                    foreach (var descriptor in services
                                 .Where(d => d.ServiceType == typeof(IAuthorizationHandler) &&
                                             d.ImplementationType == typeof(ResourceAccessHandler))
                                 .ToList())
                    {
                        services.Remove(descriptor);
                    }

                    services.PostConfigure<JwtBearerOptions>(AuthSchemes.Ciam, options =>
                        UseLocalSigningMetadata(options, CiamIssuer));

                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        UseLocalSigningMetadata(options, WorkforceIssuerV2);
                        // Worst case for FR-23: library issuer validation accepts every issuer.
                        options.TokenValidationParameters.IssuerValidator = (issuer, _, _) => issuer;
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(WorkforceRoute, () => Results.Ok()).RequireAuthorization();
                        endpoints.MapGet(CiamRoute, () => Results.Ok()).RequireAuthorization(AuthPolicies.CiamExternal);
                        endpoints.MapGet(CollaborationRoute, () => Results.Ok())
                            .RequireAuthorization(AuthPolicies.ExternalCollaboration);
                    });
                }))
            .Build();

        await host.StartAsync();
        return host;
    }

    private static void UseLocalSigningMetadata(JwtBearerOptions options, string issuer)
    {
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
        metadata.SigningKeys.Add(SigningKey);
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
    }

    private static async Task<HttpStatusCode> GetAsync(IHost host, string route, string token)
    {
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string CiamToken(string audience)
        => CreateToken(CiamIssuer, CiamTenantId, audience);

    private static string WorkforceToken(string issuer, string? tenantId, string audience)
        => CreateToken(issuer, tenantId, audience);

    // An Entra v2 ID token's shape: aud = the client id, no scp, no azpacr/appidacr; roles when the user has an app role.
    private static string IdToken(string audience)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = WorkforceIssuerV2,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object>
            {
                ["ver"] = "2.0",
                ["tid"] = WorkforceTenantId,
                ["oid"] = Guid.NewGuid().ToString(),
                ["sub"] = Guid.NewGuid().ToString(),
                ["nonce"] = "n",
                ["roles"] = new[] { "Admin" },
            },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

    private static string CreateToken(string issuer, string? tenantId, string audience)
    {
        var v2 = issuer.EndsWith("/v2.0", StringComparison.Ordinal);
        var claims = new List<Claim>
        {
            // Entra stamps ver = 2.0 on tokens from a /v2.0 issuer and 1.0 otherwise; Microsoft.Identity.Web's
            // workforce audience validator keys the accepted audience form off it.
            new("ver", v2 ? "2.0" : "1.0"),
            new("oid", Guid.NewGuid().ToString()),
            new("sub", Guid.NewGuid().ToString()),
            new("scp", "user_impersonation"),
            // Every Entra ACCESS token carries azpacr (v2) or appidacr (v1); ID tokens carry neither (task 077 F4).
            new(v2 ? "azpacr" : "appidacr", "0"),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim("tid", tenantId));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> kvps
                    ? kvps.ToList()
                    : new List<KeyValuePair<string, object?>>();
                lock (owner._entries) owner._entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
            }
        }
    }
}
