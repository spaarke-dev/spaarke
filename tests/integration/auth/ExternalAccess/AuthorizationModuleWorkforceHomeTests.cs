// spaarke-SPA-external-access-platform-r3 — Task 045 (2026-10-13)
// ADR-028 A9: the WorkforceHome JwtBearer scheme accepts a workforce contact's token issued by their CUSTOMER'S HOME
// DIRECTORY on the ExternalCollaboration policy only, and only when every A9 condition holds (design notes/a9-bff-design.md §2).
//
// Every test runs the REAL schemes registered by AuthorizationModule.AddAuthorizationModule — Microsoft.Identity.Web's own
// configuration of the WorkforceHome scheme included (its AadIssuerValidator and its signing-key issuer validation) —
// behind a TestServer, and asserts the HTTP status a route returns. Only the network is replaced, test-side:
//   1. The JwtBearer signing metadata of each scheme is a StaticConfigurationManager holding local RSA keys, shaped like
//      Entra's (the organizations metadata issuer is the {tenantid} template; each JWK carries an "issuer").
//   2. Microsoft.Identity.Web's AadIssuerValidator reads the issuer from the OpenID discovery document. Its HttpClient is
//      named through AadIssuerValidatorOptions.HttpClientName and served by FakeEntraDiscovery, which answers the same
//      documents Entra serves: a tenant-specific authority names that tenant, organizations/common name the template.
//      Nothing is accepted that Entra's real documents would refuse, and nothing leaves the machine.
//
// KNOWN LIMIT (K): AadIssuerValidator caches one validator per authority for the whole process (s_issuerValidators). The
// tenant ids here are unique to this class so no other test's validator (built with a null HttpClient) is reused; the
// organizations authority is built by no other unit test. If one ever is, these tests fail loudly (network blocked or
// unreachable), never silently pass.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public sealed class AuthorizationModuleWorkforceHomeTests
{
    // Unique to this class (see the KNOWN LIMIT above).
    private const string SpaarkeTenantId = "a0450001-0000-4000-8000-000000000001";
    private const string BffClientId = "a0450002-0000-4000-8000-000000000002";
    private const string CiamTenantId = "a0450003-0000-4000-8000-000000000003";
    private const string CiamAppId = "a0450004-0000-4000-8000-000000000004";
    private const string CustomerTenantId = "b0450001-0000-4000-8000-000000000001";       // listed
    private const string OtherCustomerTenantId = "b0450002-0000-4000-8000-000000000002";  // never listed
    private const string OtherBffClientId = "a0450005-0000-4000-8000-000000000005";
    private const string CopilotAudience = "api://auth-a0450006-0000-4000-8000-000000000006";

    private const string Instance = "https://login.microsoftonline.com/";
    private const string CiamIssuer = $"https://{CiamTenantId}.ciamlogin.com/{CiamTenantId}/v2.0";

    private const string ExternalRoute = "/api/v1/external/probe";   // AuthPolicies.ExternalCollaboration
    private const string WorkforceOboRoute = "/api/workforce/probe"; // RequireAuthorization() — the default scheme
    private const string MembershipRoute = "/api/v1/membership";     // as MembershipEndpoints maps it: RequireAuthorization()

    // Entra signs every directory's tokens with its common keys, whose JWK "issuer" is the {tenantid} template.
    private static readonly RsaSecurityKey EntraKey = new(RSA.Create(2048)) { KeyId = "t045-entra" };
    // A key Entra scopes to ONE directory (its JWK issuer names that tenant) — here another customer's.
    private static readonly RsaSecurityKey OtherTenantScopedKey = new(RSA.Create(2048)) { KeyId = "t045-other-tenant" };
    // The positive control for the key above: a key scoped to the listed customer's own directory.
    private static readonly RsaSecurityKey CustomerScopedKey = new(RSA.Create(2048)) { KeyId = "t045-customer-tenant" };
    private static readonly RsaSecurityKey CiamKey = new(RSA.Create(2048)) { KeyId = "t045-ciam" };

    private static string V2Issuer(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    // ───────────────────────────── accepted ─────────────────────────────

    [Fact]
    public async Task ValidHomeToken_OnExternalRoute_Returns200_AuthenticatedByWorkforceHomeOnly()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var (status, body) = await host.GetAsync(ExternalRoute, HomeToken());

        status.Should().Be(HttpStatusCode.OK);
        // The identity carries the scheme name (task 046 branches on it, never on iss), and no other scheme
        // authenticated the same token alongside it.
        body.Should().Be(AuthSchemes.WorkforceHome);
    }

    [Theory]
    [InlineData(BffClientId)]
    [InlineData("api://" + BffClientId)]
    public async Task ValidHomeToken_StampAudienceInEitherForm_Returns200(string audience)
    {
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.GetAsync(ExternalRoute, HomeToken(audience: audience))).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidHomeToken_AcctAsJsonString_Returns200()
    {
        // acct is an integer claim in Entra tokens; the string form is the same value and is accepted too.
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.GetAsync(ExternalRoute, HomeToken(edit: c => c["acct"] = "0"))).Status.Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── must reject: the A9 allowlist ─────────────────────────────
    // Each of these passes the library (signature, issuer chain, audience, lifetime), so the 401 is the allowlist's; the
    // logged reason proves which rule fired.

    [Fact]
    public async Task UnlistedTenant_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(tenantId: OtherCustomerTenantId));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.TenantNotListed);
    }

    [Fact]
    public async Task EmptyCustomerTenantIds_RejectsEveryHomeToken_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync(customerTenantIds: Array.Empty<string>());

        var status = await host.StatusAsync(ExternalRoute, HomeToken());

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.TenantListEmpty);
    }

    [Fact]
    public async Task SpaarkeTenantToken_IsRejectedByWorkforceHome_EvenWhenListed()
    {
        // Dev lists Spaarke's own tenant (NFR-07); the explicit rule still refuses it on this scheme. The route answers
        // 200 through the DEFAULT scheme (its owner), so the body proves WorkforceHome did not authenticate it too.
        using var host = await WorkforceHomeHost.StartAsync();

        var (status, body) = await host.GetAsync(ExternalRoute, HomeToken(tenantId: SpaarkeTenantId, audience: "api://" + BffClientId));

        status.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain(AuthSchemes.WorkforceHome);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.SpaarkeTenant);
    }

    [Fact]
    public async Task SpaarkeTenantToken_OnWorkforceHomeAlone_Returns401()
    {
        // On the external route the default scheme authenticates this token; a policy naming WorkforceHome alone shows
        // this scheme's own answer.
        using var host = await WorkforceHomeHost.StartAsync(workforceHomeOnlyRoute: true);

        var status = await host.StatusAsync(WorkforceHomeHost.WorkforceHomeOnlyRoute, HomeToken(tenantId: SpaarkeTenantId));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.SpaarkeTenant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#{AzureAdTenantId}#")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void SpaarkeTenantNotAGuid_RejectsEveryHomeToken_FailClosed(string? spaarkeTenantId)
    {
        // Review K3: the "tid is not the Spaarke tenant" rule used to be SKIPPED when AzureAd:TenantId was not a GUID
        // (fail open). It must reject instead - even a token the rest of the allowlist would accept.
        var token = new JsonWebToken(HomeToken());
        var listed = new HashSet<Guid> { Guid.Parse(CustomerTenantId) };

        AuthorizationModule.WorkforceHomeDenyReason(token, listed, spaarkeTenantId, CiamTenantId, out _)
            .Should().Be(AuthorizationModule.WorkforceHomeDeny.SpaarkeTenantUnknown);

        // Control: with a real Spaarke tenant the same token is not refused for that reason.
        AuthorizationModule.WorkforceHomeDenyReason(token, listed, SpaarkeTenantId, CiamTenantId, out _)
            .Should().NotBe(AuthorizationModule.WorkforceHomeDeny.SpaarkeTenantUnknown);
    }

    [Fact]
    public async Task CiamTenantToken_Returns401_EvenWhenListed()
    {
        // The CIAM tenant issuing through the workforce authority, signed by Entra's common keys, listed (worst case).
        using var host = await WorkforceHomeHost.StartAsync(
            customerTenantIds: new[] { CustomerTenantId, CiamTenantId }, workforceHomeOnlyRoute: true);

        var status = await host.StatusAsync(WorkforceHomeHost.WorkforceHomeOnlyRoute, HomeToken(tenantId: CiamTenantId));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.CiamTenant);
    }

    [Fact]
    public async Task V1Issuer_Returns401()
    {
        // A v1 token (sts.windows.net, ver 1.0, aud api://{appId}) passes Identity Web's issuer validator — the common
        // metadata names the v1 template — so only A9's exact-v2 rule refuses it.
        using var host = await WorkforceHomeHost.StartAsync();

        var token = HomeToken(
            issuer: $"https://sts.windows.net/{CustomerTenantId}/",
            audience: "api://" + BffClientId,
            edit: c => c["ver"] = "1.0");
        var status = await host.StatusAsync(ExternalRoute, token);

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.IssuerNotExactV2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData("1")]
    public async Task AcctOne_Guest_Returns401(object acct)
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(edit: c => c["acct"] = acct));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.AcctNotMember);
    }

    [Fact]
    public async Task AcctMissing_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(edit: c => c.Remove("acct")));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.AcctMissing);
    }

    [Fact]
    public async Task AppOnlyToken_IdtypApp_WithRoles_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var token = HomeToken(edit: c =>
        {
            c.Remove("scp");
            c["idtyp"] = "app";
            c["roles"] = new[] { "Files.Read.All" };
        });
        var status = await host.StatusAsync(ExternalRoute, token);

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.AppToken);
    }

    [Fact]
    public async Task AppOnlyToken_NoIdtyp_NoScp_WithRoles_Returns401()
    {
        // idtyp is optional; an app token without it is still refused, because it carries no delegated scope.
        using var host = await WorkforceHomeHost.StartAsync();

        var token = HomeToken(edit: c =>
        {
            c.Remove("scp");
            c.Remove("idtyp");
            c["roles"] = new[] { "Files.Read.All" };
        });
        var status = await host.StatusAsync(ExternalRoute, token);

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.ScopeMissing);
    }

    [Fact]
    public async Task IdtypApp_EvenWithUserImpersonationScope_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(edit: c => c["idtyp"] = "app"));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.AppToken);
    }

    [Theory]
    [InlineData("Files.Read")]
    [InlineData("user_impersonation_extra")]
    [InlineData("User_Impersonation")]
    public async Task ScopeWithoutUserImpersonation_Returns401(string scp)
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(edit: c => c["scp"] = scp));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.DenyReasons.Should().Contain(AuthorizationModule.WorkforceHomeDeny.ScopeMissing);
    }

    [Fact]
    public async Task UserImpersonationAmongOtherScopes_Returns200()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.StatusAsync(ExternalRoute, HomeToken(edit: c => c["scp"] = "Files.Read user_impersonation")))
            .Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── must reject: the library's validation on this scheme ─────────────────────────────

    [Fact]
    public async Task IssuerTenantMismatch_Returns401()
    {
        // iss names another directory than tid — Identity Web's issuer chain and A9's exact rule both refuse it.
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(issuer: V2Issuer(OtherCustomerTenantId)));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.Failures.Should().Contain(e => e is SecurityTokenInvalidIssuerException,
            "the WorkforceHome scheme itself must refuse the token in validation, before the allowlist runs");
    }

    [Theory]
    [InlineData(OtherBffClientId)]
    [InlineData("api://" + OtherBffClientId)]
    [InlineData(CopilotAudience)]
    public async Task WrongAudience_IncludingCopilot_Returns401(string audience)
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(audience: audience));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.Failures.Should().Contain(e => e is SecurityTokenInvalidAudienceException);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(expires: DateTime.UtcNow.AddMinutes(-30)));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.Failures.Should().Contain(e => e is SecurityTokenExpiredException);
    }

    [Fact]
    public async Task ExpiredToken_ReportsExpired_EvenAfterAnotherHostWasDisposed()
    {
        // ISS-012 / #1930. A real BFF host (WebApplication.CreateBuilder: Windows EventLog provider included) is built and
        // disposed first. Without IdentityModelLoggerIsolation, Identity Web binds IdentityModel's process-wide logger
        // (LogHelper.Logger) to THAT host's logger factory, and the validation below writes through its disposed EventLog:
        // ObjectDisposedException replaces the SecurityTokenExpiredException this scheme should report.
        var disposedHost = new ExternalWorkforceLinkEndpointsTests.LinkFixture();
        _ = disposedHost.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        await disposedHost.DisposeAsync();

        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(expires: DateTime.UtcNow.AddMinutes(-30)));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.Failures.Should().ContainSingle()
            .Which.Should().BeOfType<SecurityTokenExpiredException>(
                "a disposed host's logger must not replace the validation outcome: {0}", string.Join(" | ", host.Failures));
    }

    [Fact]
    public async Task SignedByKeyWhoseIssuerIsAnotherDirectory_Returns401()
    {
        // Signing-key issuer validation (EnableAadSigningKeyIssuerValidation, design §9) is ON for this scheme: a key the
        // metadata scopes to another directory cannot sign this directory's token.
        using var host = await WorkforceHomeHost.StartAsync();

        var status = await host.StatusAsync(ExternalRoute, HomeToken(key: OtherTenantScopedKey));

        status.Should().Be(HttpStatusCode.Unauthorized);
        host.Failures.Should().Contain(e => e.Message.Contains("IDX40005"),
            "the refusal must come from the AAD signing-key issuer check, not from some other validation step");
    }

    [Fact]
    public async Task SignedByKeyScopedToTheTokensOwnDirectory_Returns200()
    {
        // Positive control for the test above: the same check passes a key scoped to the token's own directory.
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.StatusAsync(ExternalRoute, HomeToken(key: CustomerScopedKey))).Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── only on ExternalCollaboration ─────────────────────────────

    [Theory]
    [InlineData(WorkforceOboRoute)]
    [InlineData(MembershipRoute)]
    public async Task ValidHomeToken_OnRouteOutsideExternalCollaboration_Returns401(string route)
    {
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.StatusAsync(route, HomeToken())).Should().Be(HttpStatusCode.Unauthorized);
        // Positive control: the same token on the external route.
        (await host.StatusAsync(ExternalRoute, HomeToken())).Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── existing planes unchanged ─────────────────────────────

    [Fact]
    public async Task SpaarkeTenantWorkforceToken_StillAuthenticatesOnWorkforceRoute()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        (await host.StatusAsync(WorkforceOboRoute, HomeToken(tenantId: SpaarkeTenantId, audience: "api://" + BffClientId)))
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CiamToken_StillAuthenticatesOnExternalRoute_ThroughCiamOnly()
    {
        using var host = await WorkforceHomeHost.StartAsync();

        var (status, body) = await host.GetAsync(ExternalRoute, CiamToken());

        status.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain(AuthSchemes.WorkforceHome);
    }

    // ───────────────────────────── configuration applies without restart ─────────────────────────────

    [Fact]
    public async Task CustomerTenantIdsChange_AppliesWithoutRestart()
    {
        using var host = await WorkforceHomeHost.StartAsync(customerTenantIds: Array.Empty<string>());

        (await host.StatusAsync(ExternalRoute, HomeToken())).Should().Be(HttpStatusCode.Unauthorized);

        host.Configuration[$"{WorkforceIdentityOptions.SectionName}:CustomerTenantIds:0"] = CustomerTenantId;
        host.Configuration.Reload();

        (await host.StatusAsync(ExternalRoute, HomeToken())).Should().Be(HttpStatusCode.OK);
    }

    // ───────────────────────────── tokens ─────────────────────────────

    private static string HomeToken(
        string tenantId = CustomerTenantId,
        string? issuer = null,
        string audience = BffClientId,
        Action<Dictionary<string, object>>? edit = null,
        SecurityKey? key = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["ver"] = "2.0",
            ["tid"] = tenantId,
            ["oid"] = Guid.NewGuid().ToString(),
            ["sub"] = Guid.NewGuid().ToString(),
            ["scp"] = "user_impersonation",
            ["idtyp"] = "user",
            ["acct"] = 0,
            ["preferred_username"] = "pat@customer.example",
        };
        edit?.Invoke(claims);

        var notBefore = (expires ?? DateTime.UtcNow.AddMinutes(30)).AddMinutes(-60);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? V2Issuer(tenantId),
            Audience = audience,
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? EntraKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static string CiamToken()
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = CiamIssuer,
            Audience = CiamAppId,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = new Dictionary<string, object>
            {
                ["ver"] = "2.0",
                ["tid"] = CiamTenantId,
                ["oid"] = Guid.NewGuid().ToString(),
                ["scp"] = "user_impersonation",
            },
            SigningCredentials = new SigningCredentials(CiamKey, SecurityAlgorithms.RsaSha256),
        });

    // ───────────────────────────── harness ─────────────────────────────

    private sealed class WorkforceHomeHost : IDisposable
    {
        public const string WorkforceHomeOnlyRoute = "/api/v1/external/workforce-home-only";
        private const string WorkforceHomeOnlyPolicy = "t045-workforce-home-only";

        private readonly IHost _host;
        private readonly CapturingLoggerProvider _logs;
        private readonly List<Exception> _failures;

        private WorkforceHomeHost(IHost host, IConfigurationRoot configuration, CapturingLoggerProvider logs, List<Exception> failures)
        {
            _host = host;
            Configuration = configuration;
            _logs = logs;
            _failures = failures;
        }

        public IConfigurationRoot Configuration { get; }

        /// <summary>The A9 allowlist reasons logged by the WorkforceHome scheme so far.</summary>
        public IReadOnlyList<string> DenyReasons => _logs.Entries
            .SelectMany(e => e.Properties)
            .Where(p => p.Key == "Reason")
            .Select(p => p.Value?.ToString() ?? string.Empty)
            .ToList();

        /// <summary>The validation exceptions the WorkforceHome scheme reported (OnAuthenticationFailed).</summary>
        public IReadOnlyList<Exception> Failures
        {
            get { lock (_failures) return _failures.ToList(); }
        }

        public static async Task<WorkforceHomeHost> StartAsync(
            IReadOnlyList<string>? customerTenantIds = null, bool workforceHomeOnlyRoute = false)
        {
            customerTenantIds ??= new[] { CustomerTenantId, SpaarkeTenantId };
            var settings = new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = Instance,
                ["AzureAd:TenantId"] = SpaarkeTenantId,
                ["AzureAd:ClientId"] = BffClientId,
                ["AzureAd:Audience"] = "api://" + BffClientId,
                ["AgentToken:CopilotAudience"] = CopilotAudience,
                ["Ciam:Instance"] = $"https://{CiamTenantId}.ciamlogin.com/",
                ["Ciam:TenantId"] = CiamTenantId,
                ["Ciam:Audience"] = CiamAppId,
            };
            for (var i = 0; i < customerTenantIds.Count; i++)
            {
                settings[$"{WorkforceIdentityOptions.SectionName}:CustomerTenantIds:{i}"] = customerTenantIds[i];
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var logs = new CapturingLoggerProvider();
            var failures = new List<Exception>();

            var host = new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<IConfiguration>(configuration);
                        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
                        services.AddRouting();
                        services.AddAuthorizationModule(configuration);
                        foreach (var descriptor in services
                                     .Where(d => d.ServiceType == typeof(IAuthorizationHandler) &&
                                                 d.ImplementationType == typeof(ResourceAccessHandler))
                                     .ToList())
                        {
                            services.Remove(descriptor);
                        }

                        // The same binding ExternalAccessModule makes (BindConfiguration → reloads with configuration).
                        services.AddOptions<WorkforceIdentityOptions>().BindConfiguration(WorkforceIdentityOptions.SectionName);

                        // Identity Web's AadIssuerValidator reads the discovery document through this client.
                        services.Configure<AadIssuerValidatorOptions>(o => o.HttpClientName = FakeEntraDiscovery.ClientName);
                        services.AddSingleton<IHttpClientFactory>(new FakeEntraDiscovery());

                        services.PostConfigure<JwtBearerOptions>(AuthSchemes.WorkforceHome, options =>
                        {
                            options.ConfigurationManager = StaticMetadata(
                                "https://login.microsoftonline.com/{tenantid}/v2.0",
                                (EntraKey, "https://login.microsoftonline.com/{tenantid}/v2.0"),
                                (OtherTenantScopedKey, V2Issuer(OtherCustomerTenantId)),
                                (CustomerScopedKey, V2Issuer(CustomerTenantId)));
                            var existing = options.Events.OnAuthenticationFailed;
                            options.Events.OnAuthenticationFailed = async context =>
                            {
                                lock (failures) failures.Add(context.Exception);
                                await existing(context);
                            };
                        });
                        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                            options.ConfigurationManager = StaticMetadata(
                                V2Issuer(SpaarkeTenantId),
                                (EntraKey, "https://login.microsoftonline.com/{tenantid}/v2.0")));
                        services.PostConfigure<JwtBearerOptions>(AuthSchemes.Ciam, options =>
                            options.ConfigurationManager = StaticMetadata(CiamIssuer, (CiamKey, CiamIssuer)));

                        if (workforceHomeOnlyRoute)
                        {
                            services.Configure<AuthorizationOptions>(o => o.AddPolicy(WorkforceHomeOnlyPolicy, p =>
                            {
                                p.AuthenticationSchemes = new[] { AuthSchemes.WorkforceHome };
                                p.RequireAuthenticatedUser();
                            }));
                        }
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet(ExternalRoute, AuthenticationTypes)
                                .RequireAuthorization(AuthPolicies.ExternalCollaboration);
                            endpoints.MapGet(WorkforceOboRoute, AuthenticationTypes).RequireAuthorization();
                            endpoints.MapGet(MembershipRoute, AuthenticationTypes).RequireAuthorization();
                            if (workforceHomeOnlyRoute)
                            {
                                endpoints.MapGet(WorkforceHomeOnlyRoute, AuthenticationTypes)
                                    .RequireAuthorization(WorkforceHomeOnlyPolicy);
                            }
                        });
                    }))
                .Build();

            await host.StartAsync();
            return new WorkforceHomeHost(host, configuration, logs, failures);
        }

        private static string AuthenticationTypes(HttpContext context)
            => string.Join(",", context.User.Identities
                .Where(i => i.IsAuthenticated)
                .Select(i => i.AuthenticationType)
                .Order(StringComparer.Ordinal));

        public async Task<(HttpStatusCode Status, string Body)> GetAsync(string route, string token)
        {
            using var client = _host.GetTestClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<HttpStatusCode> StatusAsync(string route, string token) => (await GetAsync(route, token)).Status;

        public void Dispose() => _host.Dispose();
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> StaticMetadata(
        string issuer, params (RsaSecurityKey Key, string KeyIssuer)[] keys)
    {
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = new JsonWebKeySet() };
        foreach (var (key, keyIssuer) in keys)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
            jwk.AdditionalData["issuer"] = keyIssuer;
            metadata.JsonWebKeySet.Keys.Add(jwk);
            metadata.SigningKeys.Add(key);
        }

        return new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
    }

    /// <summary>
    /// The Entra OpenID discovery endpoints Identity Web's AadIssuerValidator reads, served offline. It answers what Entra
    /// answers: <c>/{tenant}/v2.0</c> names that tenant's v2 issuer, <c>/{tenant}</c> its v1 issuer, and the multitenant
    /// <c>organizations</c>/<c>common</c> documents the <c>{tenantid}</c> template. Anything else is a 404, never a pass.
    /// </summary>
    private sealed class FakeEntraDiscovery : HttpMessageHandler, IHttpClientFactory
    {
        public const string ClientName = "t045-entra-discovery";

        private static readonly Regex Discovery = new(
            @"^/(?<tenant>[^/]+)(?<v2>/v2\.0)?/\.well-known/openid-configuration$", RegexOptions.CultureInvariant);

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "login.microsoftonline.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (uri.AbsolutePath.EndsWith("/keys", StringComparison.Ordinal))
            {
                return Json("""{"keys":[]}""");
            }

            var match = Discovery.Match(uri.AbsolutePath);
            if (!match.Success)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var tenant = match.Groups["tenant"].Value;
            var multitenant = tenant is "organizations" or "common";
            var tid = multitenant ? "{tenantid}" : tenant;
            var issuer = match.Groups["v2"].Success
                ? $"https://login.microsoftonline.com/{tid}/v2.0"
                : $"https://sts.windows.net/{tid}/";
            return Json($$"""{"issuer":"{{issuer}}","jwks_uri":"https://login.microsoftonline.com/{{tenant}}/discovery/v2.0/keys"}""");
        }

        private static Task<HttpResponseMessage> Json(string body)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed record LogEntry(string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

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
                lock (owner._entries) owner._entries.Add(new LogEntry(formatter(state, exception), properties));
            }
        }
    }
}
