using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Ontology;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 104 (D-22, FR-56, NFR-10): <c>GET /api/v1/ontology/admin/capabilities</c> through the REAL pipeline
/// (authentication, rate limiting, the production handler). Only the caller-identity Dataverse client is substituted, by a small
/// Dataverse that answers <c>WhoAmI</c> and <c>RetrieveUserPrivilegeByPrivilegeName</c> per user, the shapes measured live on
/// spaarkedev1 (the owner: one Global row; a Console User: no rows). The live leg is
/// <c>tests/integration/Sprk.Bff.Api.IntegrationTests/Ontology/OntologyAdminCapabilityLiveTests.cs</c>.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class OntologyAdminCapabilityEndpointTests : IClassFixture<OntologyAdminCapabilityTestFixture>
{
    private const string Route = "/api/v1/ontology/admin/capabilities";

    private static readonly Guid Admin = Guid.Parse("10400000-0000-4000-8000-0000000000a1");
    private static readonly Guid ConsoleUser = Guid.Parse("10400000-0000-4000-8000-0000000000c1");

    private readonly OntologyAdminCapabilityTestFixture _fixture;

    public OntologyAdminCapabilityEndpointTests(OntologyAdminCapabilityTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.HeldBy.Add(Admin);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> GetAs(Guid systemUserId, string oid, bool withExp = true)
    {
        _fixture.Caller = systemUserId;
        return await _fixture.ClientFor(oid, withExp).GetAsync(Route);
    }

    // ── The two users ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAdministrator_GetsCanAuthorPoliciesTrue_AskedAsTheCallerAboutTheirOwnId()
    {
        var response = await GetAs(Admin, "oid-admin");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("canAuthorPolicies").GetBoolean().Should().BeTrue();
        body.EnumerateObject().Select(p => p.Name).Should().Equal(["canAuthorPolicies"], "the probe returns only a boolean capability set");
        _fixture.Requests.Should().Equal(
            "WhoAmI()",
            $"systemusers({Admin:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='prvCreatesprk_PolicyVersion')");
    }

    [Fact]
    public async Task AConsoleUser_GetsCanAuthorPoliciesFalse()
    {
        var response = await GetAs(ConsoleUser, "oid-console");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("canAuthorPolicies").GetBoolean().Should().BeFalse();
        _fixture.Requests.Should().Contain(r => r.StartsWith($"systemusers({ConsoleUser:D})/", StringComparison.Ordinal));
    }

    // ── 401 and the single 403 ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoToken_Is401_AndDataverseIsNeverAsked()
    {
        var response = await _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DataverseUserClientErrorCodes.UserContextRequired)]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed)]
    [InlineData(DataverseUserClientErrorCodes.OboNotConfigured)]
    [InlineData(DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData(null)] // WhoAmI answered but named nobody
    public async Task AnUnresolvedCaller_IsTheSingle403_AndThePrivilegeIsNeverAsked(string? whoAmIError)
    {
        _fixture.WhoAmIError = whoAmIError;
        _fixture.WhoAmINamesNobody = whoAmIError is null;

        var response = await GetAs(Admin, "oid-unresolved");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.caller_unresolved");
        _fixture.Requests.Should().Equal("WhoAmI()");
    }

    // ── Faults: 503, never a false answer, never cached ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("WhoAmI", DataverseUserClientErrorCodes.ServiceError)]
    [InlineData("WhoAmI", DataverseUserClientErrorCodes.RateLimited)]
    [InlineData("privilege", DataverseUserClientErrorCodes.ServiceError)]
    [InlineData("privilege", DataverseUserClientErrorCodes.AccessDenied)]
    [InlineData("privilege", "unparseable")]
    [InlineData("privilege", "throws")]
    public async Task AFault_Is503_WithTheReasonCode_AndIsNotCached(string failingRead, string fault)
    {
        if (failingRead == "WhoAmI") _fixture.WhoAmIError = fault;
        else _fixture.PrivilegeFault = fault;

        var oid = $"oid-fault-{failingRead}-{fault}"; // the class fixture's cache is shared: one caller per case
        var response = await GetAs(Admin, oid);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.admin.capability_unavailable");

        _fixture.WhoAmIError = null;
        _fixture.PrivilegeFault = null;
        _fixture.Requests.Clear();
        var retry = await GetAs(Admin, oid);
        (await BodyOf(retry)).GetProperty("canAuthorPolicies").GetBoolean().Should().BeTrue("the fault was not cached");
        _fixture.Requests.Should().Contain("WhoAmI()");
    }

    // ── Cache: per caller, at most the token lifetime ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAnswer_IsCachedPerCaller_SoTheSecondCallAsksNothing()
    {
        await GetAs(Admin, "oid-cache-admin");
        _fixture.Requests.Clear();

        var again = await GetAs(Admin, "oid-cache-admin");

        (await BodyOf(again)).GetProperty("canAuthorPolicies").GetBoolean().Should().BeTrue();
        _fixture.Requests.Should().BeEmpty("the same caller's answer came from the cache");
    }

    [Fact]
    public async Task OneCallersCachedTrue_IsNeverAnotherCallersAnswer()
    {
        await GetAs(Admin, "oid-cache-a");

        var other = await GetAs(ConsoleUser, "oid-cache-b");

        (await BodyOf(other)).GetProperty("canAuthorPolicies").GetBoolean().Should().BeFalse();
        _fixture.Requests.Should().Contain(r => r.StartsWith($"systemusers({ConsoleUser:D})/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATokenWithoutExp_IsNeverCached()
    {
        await GetAs(Admin, "oid-no-exp", withExp: false);
        _fixture.Requests.Clear();

        await GetAs(Admin, "oid-no-exp", withExp: false);

        _fixture.Requests.Should().Contain("WhoAmI()", "without exp the token lifetime is unknown, so nothing is cached");
    }

    // ── The lifetime bound (pure) ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3600, 300)]  // a fresh token: capped at five minutes
    [InlineData(120, 120)]   // a token about to expire: never past its expiry
    [InlineData(0, null)]    // expired: not cached
    [InlineData(-60, null)]
    public void CacheLifetime_IsTheTokensRemainingLifetime_CappedAtFiveMinutes(int expInSeconds, int? expectedSeconds)
    {
        var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("exp", now.AddSeconds(expInSeconds).ToUnixTimeSeconds().ToString())], "test"));

        OntologyAdminCapabilityProbe.CacheLifetime(principal, now)
            .Should().Be(expectedSeconds is { } s ? TimeSpan.FromSeconds(s) : null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("99999999999999999")] // past DateTimeOffset's range
    public void CacheLifetime_WithoutAReadableExp_IsNull(string? exp)
    {
        var claims = exp is null ? Array.Empty<Claim>() : [new Claim("exp", exp)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        OntologyAdminCapabilityProbe.CacheLifetime(principal, DateTimeOffset.UtcNow).Should().BeNull();
    }
}

/// <summary>The BFF with only the caller-identity client and the inbound token substituted.</summary>
public sealed class OntologyAdminCapabilityTestFixture : WorkspaceTestFixture
{
    /// <summary>Users who hold <c>prvCreatesprk_PolicyVersion</c>.</summary>
    public HashSet<Guid> HeldBy { get; } = new();

    /// <summary>The systemuser <c>WhoAmI</c> names for the next request.</summary>
    public Guid Caller { get; set; }

    public string? WhoAmIError { get; set; }

    public bool WhoAmINamesNobody { get; set; }

    /// <summary>A DataverseUserClientErrorCodes value, "unparseable" (no RolePrivileges array) or "throws".</summary>
    public string? PrivilegeFault { get; set; }

    public ConcurrentQueue<string> Requests { get; } = new();

    public void Reset()
    {
        HeldBy.Clear();
        Caller = Guid.Empty;
        WhoAmIError = null;
        WhoAmINamesNobody = false;
        PrivilegeFault = null;
        Requests.Clear();
    }

    public HttpClient ClientFor(string oid, bool withExp)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", withExp ? $"{oid}|exp" : oid);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => new PrivilegeWorld(this));

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, CallerTokenHandler>(CallerTokenHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = CallerTokenHandler.SchemeName;
                o.DefaultChallengeScheme = CallerTokenHandler.SchemeName;
            });
        });
    }

    /// <summary>
    /// The bearer value is "oid" or "oid|exp": the principal carries that oid, the fixture tenant and, for "|exp", an expiry an hour
    /// out. No bearer is 401, as JwtBearer answers.
    /// </summary>
    private sealed class CallerTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OntologyCaller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header["Bearer ".Length..].Split('|');
            var claims = new List<Claim>
            {
                new("oid", parts[0]),
                new("tid", WorkspaceTestConstants.TestTenantId),
            };
            if (parts.Length > 1)
            {
                claims.Add(new Claim("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class PrivilegeWorld(OntologyAdminCapabilityTestFixture fixture) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            fixture.Requests.Enqueue(relativePath);
            if (relativePath == "WhoAmI()")
            {
                if (fixture.WhoAmIError is { } error)
                    return Task.FromResult(DataverseUserResponse.Fail(error == DataverseUserClientErrorCodes.AccessDenied ? 401 : 500, error, error));
                var body = fixture.WhoAmINamesNobody
                    ? JsonSerializer.SerializeToElement(new { BusinessUnitId = Guid.NewGuid() })
                    : JsonSerializer.SerializeToElement(new { UserId = fixture.Caller.ToString("D") });
                return Task.FromResult(DataverseUserResponse.Ok(200, body));
            }

            var prefix = "systemusers(";
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal)
                || !relativePath.EndsWith("/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='prvCreatesprk_PolicyVersion')", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The probe asked something unexpected: {relativePath}");
            }

            switch (fixture.PrivilegeFault)
            {
                case "throws":
                    throw new TaskCanceledException("transport timeout");
                case "unparseable":
                    return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { Something = 1 })));
                case { } code:
                    return Task.FromResult(DataverseUserResponse.Fail(code == DataverseUserClientErrorCodes.AccessDenied ? 403 : 500, code, code));
            }

            var user = Guid.Parse(relativePath[prefix.Length..relativePath.IndexOf(')')]);
            object[] rows = fixture.HeldBy.Contains(user)
                ? [new { Depth = "Global", PrivilegeId = "286d6b3e-7395-46f5-aac9-921da76552b1", PrivilegeName = "prvCreatesprk_PolicyVersion" }]
                : [];
            return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { RolePrivileges = rows })));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe never writes.");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe never writes.");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe never writes.");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The probe never writes.");
    }
}
