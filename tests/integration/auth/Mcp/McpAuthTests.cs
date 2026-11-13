// spaarkeai-mcp-bff-api-r1 — Task 021 (2026-11-12)
// ADR-028 A10 on the MCP plane (spec FR-01, FR-02, FR-07; design §8 B-7): /mcp/legal-operations accepts only a Spaarke-tenant,
// BFF-audience, user_impersonation token for a user; CIAM (A1) and home-directory (A9) tokens are refused with 401 BEFORE any
// tool runs; every tool runs as the caller (OBO), and a failed OBO never falls back to app-only; per-tool [Authorize] policies apply.
//
// Every test runs the REAL schemes registered by AuthorizationModule.AddAuthorizationModule (Identity Web's AadIssuerValidator and
// signing-key issuer validation included), the REAL AddMcpModule / MapMcpEndpoints and the REAL RateLimitingModule behind a
// TestServer, and asserts what an MCP client sees. Only the network is replaced, exactly as AuthorizationModuleWorkforceHomeTests
// does (static signing metadata shaped like Entra's, an offline discovery document). The probe tools below are in this assembly,
// tagged legal-operations; the decision probe calls the REAL DecisionCapability and SignalCoreRecordAccess, with the caller's
// Dataverse client (OBO) and the BFF's app-only reader mocked at their module boundaries.
//
// KNOWN LIMIT (K): AadIssuerValidator caches one validator per authority for the whole process. The tenant ids here are unique to
// this class (prefix a021), so no other test's cached validator is reused.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp;

public sealed class McpAuthTests
{
    // Unique to this class (see the KNOWN LIMIT above).
    internal const string SpaarkeTenantId = "a0210001-0000-4000-8000-000000000001";
    internal const string BffClientId = "a0210002-0000-4000-8000-000000000002";
    private const string CiamTenantId = "a0210003-0000-4000-8000-000000000003";
    private const string CiamAppId = "a0210004-0000-4000-8000-000000000004";
    private const string CustomerTenantId = "b0210001-0000-4000-8000-000000000001"; // an A9 home directory this stamp lists
    private const string CopilotAudience = "api://auth-a0210006-0000-4000-8000-000000000006";

    private const string CiamIssuer = $"https://{CiamTenantId}.ciamlogin.com/{CiamTenantId}/v2.0";

    // Test-only routes, mapped beside the production MCP route.
    private const string ExternalRoute = "/api/v1/external/probe"; // AuthPolicies.ExternalCollaboration: accepts CIAM and A9 tokens
    private const string WorkforceRoute = "/api/workforce/probe";  // RequireAuthorization(): what the default scheme alone accepts
    private const string UnguardedMcpRoute = "/mcp/zz-fallback-probe"; // under /mcp, NO explicit authorization: the FallbackPolicy

    private static readonly Guid Caller = Guid.Parse("a0210010-0000-4000-8000-000000000010");

    private static readonly RsaSecurityKey EntraKey = new(RSA.Create(2048)) { KeyId = "t021-entra" };
    private static readonly RsaSecurityKey CiamKey = new(RSA.Create(2048)) { KeyId = "t021-ciam" };

    private static string V2Issuer(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    // ───────────────────────────── criterion 1: anonymous ─────────────────────────────

    [Fact]
    public async Task McpRoute_Anonymous_Returns401WithBearerChallenge()
    {
        await using var host = await McpAuthHost.StartAsync();

        using var response = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Select(h => h.Scheme).Should().Contain("Bearer");
        host.Probe.CallerInvocations.Should().Be(0);
    }

    // ───────────────────────────── criterion 2: CIAM and A9 refused before any tool runs ─────────────────────────────

    [Fact]
    public async Task McpRoute_CiamToken_Returns401AndRunsNoTool()
    {
        await using var host = await McpAuthHost.StartAsync();
        var token = CiamToken();

        using var response = await host.CallToolAsync("probe_a10_caller", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Probe.CallerInvocations.Should().Be(0, "a CIAM token is refused before any tool runs (FR-02)");
        (await host.StatusAsync(ExternalRoute, token)).Should().Be(HttpStatusCode.OK,
            "the control: the same token is a valid CIAM token the external plane accepts, so the 401 is the MCP plane refusing it");
    }

    [Fact]
    public async Task McpRoute_HomeDirectoryToken_Returns401AndRunsNoTool()
    {
        await using var host = await McpAuthHost.StartAsync();
        var token = HomeToken();

        using var response = await host.CallToolAsync("probe_a10_caller", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Probe.CallerInvocations.Should().Be(0, "an A9 home-directory token is refused before any tool runs (FR-02)");
        (await host.StatusAsync(ExternalRoute, token)).Should().Be(HttpStatusCode.OK,
            "the control: the same token is a valid A9 token the external plane accepts, so the 401 is the MCP plane refusing it");
    }

    // ───────────────────────────── the A10 rule beyond the default scheme ─────────────────────────────

    public static TheoryData<string, string> TokensTheWorkforceSchemeAcceptsButA10Refuses() => new()
    {
        { "copilot-audience", McpToolAuthorization.Deny.AudienceNotBff },
        { "no-user-impersonation-scope", McpToolAuthorization.Deny.ScopeMissing },
        { "app-token", McpToolAuthorization.Deny.AppToken },
        // Fix round 1: a v1-issuer Spaarke token for the BFF (the workforce scheme accepts v1 and v2 issuers; A10 pins v2).
        { "v1-issuer", McpToolAuthorization.Deny.IssuerNotExactV2 },
        // Fix round 1 (uac-r2): an app-only token WITHOUT idtyp (roles, no scp) is refused for its missing delegated scope.
        { "app-token-without-idtyp", McpToolAuthorization.Deny.ScopeMissing },
    };

    [Theory]
    [MemberData(nameof(TokensTheWorkforceSchemeAcceptsButA10Refuses))]
    public async Task McpRoute_TokenTheWorkforceSchemeAcceptsButA10Refuses_Returns401AndLogsOnlyTheReason(string shape, string reason)
    {
        await using var host = await McpAuthHost.StartAsync();
        var token = shape switch
        {
            "copilot-audience" => SpaarkeToken(audience: CopilotAudience),
            "no-user-impersonation-scope" => SpaarkeToken(edit: c => c["scp"] = "Files.Read"),
            "v1-issuer" => V1SpaarkeToken(),
            "app-token-without-idtyp" => SpaarkeToken(edit: c =>
            {
                c.Remove("scp");
                c["roles"] = new[] { "Data.Read" };
            }),
            _ => SpaarkeToken(edit: c =>
            {
                c.Remove("scp");
                c["idtyp"] = "app";
                c["roles"] = new[] { "Data.Read" };
            }),
        };

        (await host.StatusAsync(WorkforceRoute, token)).Should().Be(HttpStatusCode.OK,
            "the control: the shared workforce scheme accepts this token, so the refusal below is the A10 rule, not the library");

        using var response = await host.CallToolAsync("probe_a10_caller", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer").And.Contain("invalid_token");
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("errorCode").GetString().Should().Be(McpToolAuthorization.RefusedErrorCode);
        }

        host.Probe.CallerInvocations.Should().Be(0);
        host.DenyReasons.Should().Equal([reason]);
        host.LogText.Should().NotContain(token).And.NotContain(Caller.ToString(), "NFR-05: neither the token nor a claim value is logged");
    }

    // ───────────────────────────── criterion 5: a valid A10 token runs the tool as the caller ─────────────────────────────

    [Theory]
    [InlineData(BffClientId)]
    [InlineData("api://" + BffClientId)]
    public async Task McpRoute_ValidA10Token_RunsTheToolAsTheCaller(string audience)
    {
        await using var host = await McpAuthHost.StartAsync();

        var result = await host.CallToolResultAsync("probe_a10_caller", SpaarkeToken(audience: audience));

        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be(Caller.ToString("D"), "the tool sees the caller's own identity");
        host.Probe.CallerInvocations.Should().Be(1);
        host.DenyReasons.Should().BeEmpty();
    }

    // ───────────────────────────── criterion 6: FallbackPolicy covers /mcp ─────────────────────────────

    [Fact]
    public async Task McpPrefix_EndpointWithoutExplicitAuthorization_IsCoveredByTheFallbackPolicy()
    {
        await using var host = await McpAuthHost.StartAsync();

        using var anonymous = await host.PostRpcAsync(UnguardedMcpRoute, "tools/list", new { }, token: null);
        using var signedIn = await host.PostRpcAsync(UnguardedMcpRoute, "tools/list", new { }, SpaarkeToken());

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "FR-01: /mcp/* is under the FallbackPolicy");
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK, "the control: the route exists and answers an authenticated caller");
    }

    // ───────────────────────────── criteria 3 and 4: as the caller, never app-only ─────────────────────────────

    [Fact]
    public async Task DecisionPlanTool_ValidTokenThatResolvesToNoSystemuser_ReturnsTheCallerUnresolvedIsErrorResult()
    {
        var (users, entities) = CallerDataverse(
            signal: DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "test"),
            whoAmI: DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "test"));
        await using var host = await McpAuthHost.StartAsync(Capability(users, entities));

        var result = await host.CallToolResultAsync("probe_a10_plan", SpaarkeToken());

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString()
            .Should().Be(DecisionPlanEndpoints.CallerUnresolvedReasonCode, "the REST route's refusal for a caller with no Dataverse user");
        users.Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("WhoAmI", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Once());
        entities.Invocations.Should().BeEmpty("the app-only reader is never used for a caller the BFF could not resolve");
    }

    [Fact]
    public async Task DecisionPlanTool_OboExchangeFails_ReturnsIsErrorAndNeverCallsTheAppOnlyClient()
    {
        var (users, entities) = CallerDataverse(
            signal: DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed, "test"),
            whoAmI: null);
        await using var host = await McpAuthHost.StartAsync(Capability(users, entities));

        var result = await host.CallToolResultAsync("probe_a10_plan", SpaarkeToken());

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DecisionPlanEndpoints.CallerUnresolvedReasonCode);
        entities.Invocations.Should().BeEmpty("FR-02: a failed OBO never falls back to app-only");
    }

    // ───────────────────────────── per-tool policies (B-7) ─────────────────────────────

    [Fact]
    public async Task ToolPolicy_CallerOutsideTheToolsPolicy_DoesNotSeeTheToolAndCannotRunIt()
    {
        await using var host = await McpAuthHost.StartAsync();
        var member = SpaarkeToken();
        var admin = SpaarkeToken(edit: c => c["roles"] = new[] { "SystemAdmin" });

        var memberList = await host.RpcResultAsync("tools/list", new { }, member);
        using var memberCall = await host.CallToolAsync("probe_a10_admin", member);
        var memberCallBody = await memberCall.Content.ReadAsStringAsync();
        var adminList = await host.RpcResultAsync("tools/list", new { }, admin);
        var adminCall = await host.CallToolResultAsync("probe_a10_admin", admin);

        ToolNames(memberList).Should().Contain("probe_a10_caller").And.NotContain("probe_a10_admin", "tools/list hides a tool whose policy the caller fails");
        memberCallBody.Should().Contain("\"error\"").And.Contain("Access forbidden", "tools/call refuses it");
        ToolNames(adminList).Should().Contain("probe_a10_admin", "the control: a caller the policy admits sees the tool");
        adminCall.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("admin");
        host.Probe.AdminInvocations.Should().Be(1, "only the admitted call ran");
    }

    // ───────────────────────────── fail closed on missing configuration ─────────────────────────────

    [Theory]
    [InlineData(null, BffClientId)]
    [InlineData(SpaarkeTenantId, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", BffClientId)]
    [InlineData(SpaarkeTenantId, "#{BffClientId}#")]
    public void A10DenyReason_WhenTheSpaarkeTenantOrBffAppIsNotConfigured_FailsClosed(string? tenantId, string? clientId)
    {
        McpToolAuthorization.A10DenyReason(A10Principal(), SpaarkeTenantId, BffClientId).Should().BeNull("the control: configured, the caller passes");

        McpToolAuthorization.A10DenyReason(A10Principal(), tenantId, clientId).Should().Be(McpToolAuthorization.Deny.NotConfigured);
    }

    // ───────────────────────────── each A10 check on its own (fix round 1) ─────────────────────────────
    // The route tests cannot reach the tid, iss, oid or scheme checks: the default scheme refuses CIAM and A9 tokens first. These
    // rows call the rule directly with the principal each plane would produce, so deleting any one check turns a row red.

    public static TheoryData<string, string> PrincipalsA10Refuses() => new()
    {
        { "ciam", McpToolAuthorization.Deny.TenantNotSpaarke },
        { "a9-home-directory", McpToolAuthorization.Deny.TenantNotSpaarke },
        { "spaarke-tid-foreign-v2-iss", McpToolAuthorization.Deny.IssuerNotExactV2 },
        { "spaarke-tid-v1-iss", McpToolAuthorization.Deny.IssuerNotExactV2 },
        { "oid-missing", McpToolAuthorization.Deny.ObjectIdMissing },
        { "oid-not-a-guid", McpToolAuthorization.Deny.ObjectIdMissing },
        { "workforce-home-scheme", McpToolAuthorization.Deny.ForeignScheme },
    };

    [Theory]
    [MemberData(nameof(PrincipalsA10Refuses))]
    public void A10DenyReason_PrincipalOutsideA10_ReturnsItsReason(string shape, string reason)
    {
        McpToolAuthorization.A10DenyReason(A10Principal(), SpaarkeTenantId, BffClientId).Should().BeNull("the control: an A10 caller passes");

        var principal = shape switch
        {
            "ciam" => A10Principal(c =>
            {
                c["tid"] = CiamTenantId;
                c["iss"] = CiamIssuer;
                c["aud"] = CiamAppId;
            }),
            "a9-home-directory" => A10Principal(c =>
            {
                c["tid"] = CustomerTenantId;
                c["iss"] = V2Issuer(CustomerTenantId);
                c["idtyp"] = "user";
            }),
            "spaarke-tid-foreign-v2-iss" => A10Principal(c => c["iss"] = V2Issuer(CustomerTenantId)),
            "spaarke-tid-v1-iss" => A10Principal(c => c["iss"] = $"https://sts.windows.net/{SpaarkeTenantId}/"),
            "oid-missing" => A10Principal(c => c.Remove("oid")),
            "oid-not-a-guid" => A10Principal(c => c["oid"] = "not-a-guid"),
            "workforce-home-scheme" => A10Principal(authenticationType: AuthSchemes.WorkforceHome),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        McpToolAuthorization.A10DenyReason(principal, SpaarkeTenantId, BffClientId).Should().Be(reason);
    }

    // ───────────────────────────── every method MapMcp maps (fix round 1, uac-r2) ─────────────────────────────

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task McpRoute_NonPostMethod_NeverOpensAStreamOrRunsATool(string method)
    {
        await using var host = await McpAuthHost.StartAsync();

        using var copilot = await host.SendAsync(new HttpMethod(method), SpaarkeToken(audience: CopilotAudience));
        using var valid = await host.SendAsync(new HttpMethod(method), SpaarkeToken());

        // SDK 2.3.1 in stateless mode maps POST only: every other method is 405 from routing, for either token (recorded 2026-11-12).
        copilot.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        valid.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        foreach (var response in new[] { copilot, valid })
        {
            response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/event-stream");
        }

        host.Probe.CallerInvocations.Should().Be(0);
    }

    // ───────────────────────────── the catalog's per-tool rule (startup refusal) ─────────────────────────────

    public static TheoryData<string> ToolsTheCatalogRefuses() => new()
    {
        nameof(ToolShapes.NoAuthorizeTool), nameof(ToolShapes.ExternalPolicyTool),
        nameof(ToolShapes.NamedSchemeTool), nameof(ToolShapes.NoReadOnly), nameof(ToolShapes.NoTitle),
        // Fix round 1: the enforced metadata must equal the tool class's own [Authorize], with an allow-listed policy.
        ToolShapes.HelperClassHandler, ToolShapes.MetadataOverride, ToolShapes.AnonymityMetadata,
        nameof(ToolShapes.UnlistedPolicyTool), nameof(ToolShapes.MethodLevelAuthorizeTool),
    };

    [Theory]
    [MemberData(nameof(ToolsTheCatalogRefuses))]
    public void ValidateTool_ToolBreakingThePerToolRule_IsRefusedAtStartup(string shape)
    {
        var (tool, definition) = ToolShapes.Build(shape);

        var act = () => McpToolAuthorization.ValidateTool(tool, definition);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{shape}'*");
    }

    [Theory]
    [InlineData(nameof(ToolShapes.Sanctioned))]
    [InlineData(nameof(ToolShapes.AdminTool))]
    public void ValidateTool_AuthorizedReadOnlyTitledTool_IsAccepted(string shape)
    {
        var (tool, definition) = ToolShapes.Build(shape);

        var act = () => McpToolAuthorization.ValidateTool(tool, definition);

        act.Should().NotThrow("the control: bare [Authorize] and the allow-listed SystemAdmin policy, built from the class's own method, pass");
    }

    // ───────────────────────────── startup refusal (fix round 1) ─────────────────────────────

    [Fact]
    public async Task McpHost_ToolWhoseHandlerComesFromAHelperClass_RefusesToStart()
    {
        // The reviewer's reproduction: the tool CLASS says SystemAdmin, its handler's helper class says bare [Authorize]; before
        // the fix the host started and a non-admin saw the tool in tools/list.
        var types = McpProbeTools.Served.Append(typeof(StartupRefusals.McpHelperHandlerRefusedTool));

        var start = async () => await McpAuthHost.StartAsync(toolTypes: types);

        (await start.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*probe_refused_helper_handler*is not the tool class's own*");
    }

    [Fact]
    public async Task McpHost_KillSwitchOff_StartsWithoutValidatingTools()
    {
        var types = McpProbeTools.Served.Append(typeof(StartupRefusals.McpHelperHandlerRefusedTool));

        await using var host = await McpAuthHost.StartAsync(toolTypes: types, mcpEnabled: false);

        using var response = await host.CallToolAsync("probe_a10_caller", SpaarkeToken());
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "switched off, the null catalog validates nothing and the route answers 503");
    }

    [Fact]
    public void McpToolCatalog_ToolNamingAPolicyThatIsNotRegistered_Throws()
    {
        static IServiceProvider Services(bool registerSystemAdmin) => new ServiceCollection()
            .AddLogging()
            .AddAuthorization(o =>
            {
                if (registerSystemAdmin)
                {
                    o.AddPolicy("SystemAdmin", p => p.RequireRole("SystemAdmin"));
                }
            })
            .BuildServiceProvider();

        var registered = () => new McpToolCatalog([typeof(McpA10AdminProbeTool)], Services(registerSystemAdmin: true));
        var missing = () => new McpToolCatalog([typeof(McpA10AdminProbeTool)], Services(registerSystemAdmin: false));

        registered.Should().NotThrow("the control: the allow-listed policy is registered");
        missing.Should().Throw<InvalidOperationException>().WithMessage("*probe_a10_admin*'SystemAdmin' policy, which is not registered*");
    }

    /// <summary>Tool shapes for the per-tool rule; none is tagged [McpArea], so no catalog in this assembly serves them.</summary>
    private static class ToolShapes
    {
        public const string HelperClassHandler = nameof(HelperClassHandler);
        public const string MetadataOverride = nameof(MetadataOverride);
        public const string AnonymityMetadata = nameof(AnonymityMetadata);

        public static (McpServerTool Tool, Type Definition) Build(string shape) => shape switch
        {
            nameof(NoAuthorizeTool) => (Make(NoAuthorizeTool.Run, shape), typeof(NoAuthorizeTool)),
            nameof(ExternalPolicyTool) => (Make(ExternalPolicyTool.Run, shape), typeof(ExternalPolicyTool)),
            nameof(NamedSchemeTool) => (Make(NamedSchemeTool.Run, shape), typeof(NamedSchemeTool)),
            nameof(NoReadOnly) => (Make(Sanctioned.Run, shape, readOnly: null), typeof(Sanctioned)),
            nameof(NoTitle) => (Make(Sanctioned.Run, shape, title: null), typeof(Sanctioned)),
            nameof(AdminTool) => (Make(AdminTool.Run, shape), typeof(AdminTool)),
            nameof(UnlistedPolicyTool) => (Make(UnlistedPolicyTool.Run, shape), typeof(UnlistedPolicyTool)),
            nameof(MethodLevelAuthorizeTool) => (Make(MethodLevelAuthorizeTool.Run, shape), typeof(MethodLevelAuthorizeTool)),
            HelperClassHandler => (Make(BareAuthorizeHelper.Run, shape), typeof(AdminTool)),
            MetadataOverride => (Make(AdminTool.Run, shape, metadata: [new AuthorizeAttribute()]), typeof(AdminTool)),
            AnonymityMetadata => (Make(Sanctioned.Run, shape, metadata: [new AuthorizeAttribute(), new AllowAnonymousAttribute()]), typeof(Sanctioned)),
            _ => (Make(Sanctioned.Run, shape), typeof(Sanctioned)),
        };

        private static McpServerTool Make(Func<string> run, string name, bool? readOnly = true, string? title = "A title", object[]? metadata = null) =>
            McpServerTool.Create(run, new McpServerToolCreateOptions { Name = name, Title = title, ReadOnly = readOnly, Metadata = metadata });

        [Authorize(Policy = "SystemAdmin")]
        public static class AdminTool
        {
            public static string Run() => "x";
        }

        [Authorize]
        public static class BareAuthorizeHelper
        {
            public static string Run() => "x";
        }

        [Authorize(Policy = "SomeOtherWorkforcePolicy")]
        public static class UnlistedPolicyTool
        {
            public static string Run() => "x";
        }

        [Authorize]
        public static class MethodLevelAuthorizeTool
        {
            [Authorize(Policy = "SystemAdmin")]
            public static string Run() => "x";
        }

        public static class NoAuthorizeTool
        {
            public static string Run() => "x";
        }

        [Authorize(Policy = AuthPolicies.ExternalCollaboration)]
        public static class ExternalPolicyTool
        {
            public static string Run() => "x";
        }

        [Authorize(AuthenticationSchemes = AuthSchemes.Ciam)]
        public static class NamedSchemeTool
        {
            public static string Run() => "x";
        }

        [Authorize]
        public static class Sanctioned
        {
            public static string Run() => "x";
        }

        // Shape names reused for the two annotation cases (they share Sanctioned's class).
        public const string NoReadOnly = nameof(NoReadOnly);
        public const string NoTitle = nameof(NoTitle);
    }

    private static ClaimsPrincipal A10Principal(Action<Dictionary<string, string>>? edit = null, string authenticationType = "AuthenticationTypes.Federation")
    {
        var claims = new Dictionary<string, string>
        {
            ["tid"] = SpaarkeTenantId,
            ["iss"] = V2Issuer(SpaarkeTenantId),
            ["aud"] = BffClientId,
            ["scp"] = "user_impersonation",
            ["oid"] = Caller.ToString(),
        };
        edit?.Invoke(claims);
        return new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)), authenticationType));
    }

    private static IEnumerable<string?> ToolNames(JsonElement list) =>
        list.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString());

    // ───────────────────────────── the caller's Dataverse boundary ─────────────────────────────

    private static (Mock<IDataverseUserClient> Users, Mock<IGenericEntityService> Entities) CallerDataverse(
        DataverseUserResponse signal, DataverseUserResponse? whoAmI)
    {
        var users = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(signal);
        if (whoAmI is not null)
        {
            users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("WhoAmI", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(whoAmI);
        }

        // The app-only reader: strict and unscripted, so any call fails the test, and Invocations is the spy.
        return (users, new Mock<IGenericEntityService>(MockBehavior.Strict));
    }

    private static IDecisionCapability Capability(Mock<IDataverseUserClient> users, Mock<IGenericEntityService> entities) =>
        DecisionCapabilityFixtures.Capability(access: new SignalCoreRecordAccess(
            users.Object, entities.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object, NullLogger<SignalCoreRecordAccess>.Instance));

    // ───────────────────────────── tokens ─────────────────────────────

    private static string SpaarkeToken(string audience = BffClientId, Action<Dictionary<string, object>>? edit = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["ver"] = "2.0",
            ["tid"] = SpaarkeTenantId,
            ["oid"] = Caller.ToString(),
            ["sub"] = Guid.NewGuid().ToString(),
            ["scp"] = "user_impersonation",
            ["azp"] = "a0210007-0000-4000-8000-000000000007", // the connector client
            ["azpacr"] = "1",
        };
        edit?.Invoke(claims);
        return Sign(V2Issuer(SpaarkeTenantId), audience, claims, EntraKey);
    }

    /// <summary>A v1 access token for the BFF from the Spaarke tenant: <c>iss</c> = <c>https://sts.windows.net/{tid}/</c>, <c>ver</c> 1.0.</summary>
    private static string V1SpaarkeToken() => Sign($"https://sts.windows.net/{SpaarkeTenantId}/", BffClientId, new Dictionary<string, object>
    {
        ["ver"] = "1.0",
        ["tid"] = SpaarkeTenantId,
        ["oid"] = Caller.ToString(),
        ["sub"] = Guid.NewGuid().ToString(),
        ["scp"] = "user_impersonation",
        ["appid"] = "a0210007-0000-4000-8000-000000000007", // the connector client (v1 form of azp)
        ["appidacr"] = "1",
    }, EntraKey);

    private static string HomeToken() => Sign(V2Issuer(CustomerTenantId), BffClientId, new Dictionary<string, object>
    {
        ["ver"] = "2.0",
        ["tid"] = CustomerTenantId,
        ["oid"] = Caller.ToString(),
        ["sub"] = Guid.NewGuid().ToString(),
        ["scp"] = "user_impersonation",
        ["idtyp"] = "user",
        ["acct"] = 0,
    }, EntraKey);

    private static string CiamToken() => Sign(CiamIssuer, CiamAppId, new Dictionary<string, object>
    {
        ["ver"] = "2.0",
        ["tid"] = CiamTenantId,
        ["oid"] = Caller.ToString(),
        ["scp"] = "user_impersonation",
    }, CiamKey);

    private static string Sign(string issuer, string audience, Dictionary<string, object> claims, SecurityKey key) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });

    // ───────────────────────────── harness ─────────────────────────────

    private sealed class McpAuthHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly CapturingLoggerProvider _logs;
        private readonly HttpClient _client;

        private McpAuthHost(WebApplication app, CapturingLoggerProvider logs, McpAuthProbeState probe)
        {
            _app = app;
            _logs = logs;
            Probe = probe;
            _client = app.GetTestClient();
        }

        public McpAuthProbeState Probe { get; }

        /// <summary>The A10 deny reasons the MCP filter logged.</summary>
        public IReadOnlyList<string> DenyReasons => _logs.Entries
            .Where(e => e.Message.Contains("sdap.auth.deny.mcp_connector_token", StringComparison.Ordinal))
            .SelectMany(e => e.Properties)
            .Where(p => p.Key == "Reason")
            .Select(p => p.Value?.ToString() ?? string.Empty)
            .ToList();

        /// <summary>Every rendered log message and structured property value, for the "nothing from the token is logged" check.</summary>
        public string LogText => string.Join(
            "\n",
            _logs.Entries.SelectMany(e => e.Properties.Select(p => p.Value?.ToString() ?? string.Empty).Prepend(e.Message)));

        /// <param name="toolTypes">The tool scan set; default <see cref="McpProbeTools.Served"/> (never the startup-refusal tools).</param>
        public static async Task<McpAuthHost> StartAsync(IDecisionCapability? capability = null, IEnumerable<Type>? toolTypes = null, bool mcpEnabled = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = mcpEnabled ? "true" : "false",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = SpaarkeTenantId,
                ["AzureAd:ClientId"] = BffClientId,
                ["AzureAd:Audience"] = "api://" + BffClientId,
                ["AgentToken:CopilotAudience"] = CopilotAudience,
                ["Ciam:Instance"] = $"https://{CiamTenantId}.ciamlogin.com/",
                ["Ciam:TenantId"] = CiamTenantId,
                ["Ciam:Audience"] = CiamAppId,
                [$"{WorkforceIdentityOptions.SectionName}:CustomerTenantIds:0"] = CustomerTenantId,
            });

            var logs = new CapturingLoggerProvider();
            var probe = new McpAuthProbeState();
            builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace);

            var services = builder.Services;
            services.AddAuthorizationModule(builder.Configuration);
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType == typeof(ResourceAccessHandler))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddOptions<WorkforceIdentityOptions>().BindConfiguration(WorkforceIdentityOptions.SectionName);
            services.Configure<AadIssuerValidatorOptions>(o => o.HttpClientName = FakeEntraDiscovery.ClientName);
            services.AddSingleton<IHttpClientFactory>(new FakeEntraDiscovery());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = StaticMetadata(V2Issuer(SpaarkeTenantId), (EntraKey, "https://login.microsoftonline.com/{tenantid}/v2.0")));
            services.PostConfigure<JwtBearerOptions>(AuthSchemes.WorkforceHome, options =>
                options.ConfigurationManager = StaticMetadata(
                    "https://login.microsoftonline.com/{tenantid}/v2.0", (EntraKey, "https://login.microsoftonline.com/{tenantid}/v2.0")));
            services.PostConfigure<JwtBearerOptions>(AuthSchemes.Ciam, options =>
                options.ConfigurationManager = StaticMetadata(CiamIssuer, (CiamKey, CiamIssuer)));

            services.AddHttpContextAccessor();
            services.AddSingleton(probe);
            services.AddSingleton(capability ?? new Mock<IDecisionCapability>(MockBehavior.Strict).Object);
            services.AddRateLimitingModule();
            services.AddMcpModule(builder.Configuration, toolTypes ?? McpProbeTools.Served);

            var app = builder.Build();
            try
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseRateLimiter();
                app.MapMcpEndpoints(); // builds the tool catalog: a tool breaking the per-tool rule throws here
                app.MapGet(ExternalRoute, () => "ok").RequireAuthorization(AuthPolicies.ExternalCollaboration);
                app.MapGet(WorkforceRoute, () => "ok").RequireAuthorization();
                app.MapPost(UnguardedMcpRoute, () => "ok"); // no RequireAuthorization: only the FallbackPolicy guards it
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            return new McpAuthHost(app, logs, probe);
        }

        /// <summary>A bodiless request with <paramref name="method"/> to the MCP route, asking for JSON or an event stream.</summary>
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string token)
        {
            var request = new HttpRequestMessage(method, McpEndpoints.LegalOperationsRoute);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return _client.SendAsync(request);
        }

        public async Task<HttpStatusCode> StatusAsync(string route, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _client.SendAsync(request);
            return response.StatusCode;
        }

        public Task<HttpResponseMessage> CallToolAsync(string tool, string token) =>
            PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/call", new { name = tool, arguments = new { } }, token);

        public Task<HttpResponseMessage> PostRpcAsync(string route, string method, object parameters, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return _client.SendAsync(request);
        }

        public Task<JsonElement> CallToolResultAsync(string tool, string token) =>
            RpcResultAsync("tools/call", new { name = tool, arguments = new { } }, token);

        /// <summary>The JSON-RPC <c>result</c> of one call (JSON or SSE body); throws on an HTTP or JSON-RPC error.</summary>
        public async Task<JsonElement> RpcResultAsync(string method, object parameters, string token)
        {
            using var response = await PostRpcAsync(McpEndpoints.LegalOperationsRoute, method, parameters, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
            }

            var json = body.TrimStart().StartsWith('{')
                ? body
                : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
            using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"JSON-RPC error: {error}");
            }

            return doc.RootElement.GetProperty("result").Clone();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> StaticMetadata(string issuer, params (RsaSecurityKey Key, string KeyIssuer)[] keys)
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

    /// <summary>The Entra OpenID discovery documents Identity Web's AadIssuerValidator reads, served offline (as in AuthorizationModuleWorkforceHomeTests).</summary>
    private sealed class FakeEntraDiscovery : HttpMessageHandler, IHttpClientFactory
    {
        public const string ClientName = "t021-entra-discovery";

        private static readonly Regex Discovery = new(@"^/(?<tenant>[^/]+)(?<v2>/v2\.0)?/\.well-known/openid-configuration$", RegexOptions.CultureInvariant);

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
            var tid = tenant is "organizations" or "common" ? "{tenantid}" : tenant;
            var issuer = match.Groups["v2"].Success ? $"https://login.microsoftonline.com/{tid}/v2.0" : $"https://sts.windows.net/{tid}/";
            return Json($$"""{"issuer":"{{issuer}}","jwks_uri":"https://login.microsoftonline.com/{{tenant}}/discovery/v2.0/keys"}""");
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
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

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> kvps ? kvps.ToList() : new List<KeyValuePair<string, object?>>();
                lock (owner._entries) owner._entries.Add(new LogEntry(formatter(state, exception), properties));
            }
        }
    }
}

/// <summary>What the A10 probe tools did in one host.</summary>
internal sealed class McpAuthProbeState
{
    private int _caller;
    private int _admin;

    public int CallerInvocations => Volatile.Read(ref _caller);

    public int AdminInvocations => Volatile.Read(ref _admin);

    public void CountCaller() => Interlocked.Increment(ref _caller);

    public void CountAdmin() => Interlocked.Increment(ref _admin);
}

/// <summary>Probe: counts its runs and returns the caller's <c>oid</c> as the request's <c>HttpContext</c> carries it.</summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class McpA10CallerProbeTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(Run, new McpServerToolCreateOptions { Services = services, Name = "probe_a10_caller", Title = "Probe A10 caller", ReadOnly = true });

    private static string Run(IHttpContextAccessor accessor, McpAuthProbeState state)
    {
        state.CountCaller();
        var user = accessor.HttpContext?.User;
        return user?.FindFirst("oid")?.Value ?? user?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? "none";
    }
}

/// <summary>Probe: what a decision tool does (task 013's mapping): the plan read as the caller, its failure as an isError result.</summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class McpA10PlanProbeTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(RunAsync, new McpServerToolCreateOptions { Services = services, Name = "probe_a10_plan", Title = "Probe A10 plan", ReadOnly = true });

    private static async Task<CallToolResult> RunAsync(IHttpContextAccessor accessor, IDecisionCapability capability, CancellationToken cancellationToken)
    {
        var result = await capability.GetPlanAsync(accessor.HttpContext, DecisionCapabilityFixtures.SignalId, cancellationToken);
        return DecisionResultMapper.ToToolError(result) is { } error
            ? McpToolResults.FromError(error)
            : new CallToolResult { Content = [new TextContentBlock { Text = "plan" }] };
    }
}

/// <summary>Probe: a tool behind an existing named workforce policy (SystemAdmin), to show per-tool policies apply (B-7).</summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize(Policy = "SystemAdmin")]
internal sealed class McpA10AdminProbeTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(Run, new McpServerToolCreateOptions { Services = services, Name = "probe_a10_admin", Title = "Probe A10 admin", ReadOnly = true });

    private static string Run(McpAuthProbeState state)
    {
        state.CountAdmin();
        return "admin";
    }
}
