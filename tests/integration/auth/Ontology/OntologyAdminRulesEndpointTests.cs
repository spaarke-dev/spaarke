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
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.Ontology;

/// <summary>
/// Ontology platform R1 task 100 (D-22, FR-57, NFR-10): the four Rules routes under <c>/api/v1/ontology/admin/rules</c> through the REAL
/// pipeline (authentication, the group's authorization and rate limit, the production handlers and the production service). Only the
/// caller-identity Dataverse client (<see cref="OntologyAdminRulesWorld"/>) and the Signal closure are substituted. The status mapping is
/// the contract: 401 with no token; the single 403 for a caller who cannot be resolved; 403 when Dataverse refuses a Console User's
/// write; the uniform 404 for a missing or non-Ontology rule with zero writes; 400 for no reason; 409; 503 for a fault.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class OntologyAdminRulesEndpointTests : IClassFixture<OntologyAdminRulesTestFixture>
{
    private const string Base = "/api/v1/ontology/admin/rules";

    private readonly OntologyAdminRulesTestFixture _fixture;

    public OntologyAdminRulesEndpointTests(OntologyAdminRulesTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private HttpClient Client() => _fixture.ClientFor("oid-admin");

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    // ── 401 ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/{id}")]
    [InlineData("PUT", "/{id}/enabled")]
    [InlineData("POST", "/{id}/retire")]
    public async Task NoToken_Is401_OnEveryRoute_AndDataverseIsNeverAsked(string method, string suffix)
    {
        var rule = _fixture.World.AddPolicy("POL-401");
        var anonymous = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), Base + suffix.Replace("{id}", rule.Id.ToString("D")))
        {
            Content = method == "GET" ? null : Json("{}"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.World.Requests.Should().BeEmpty();
    }

    // ── The single 403 for an unresolved caller ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DataverseUserClientErrorCodes.UserContextRequired)]
    [InlineData(DataverseUserClientErrorCodes.OboNotConfigured)]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed)]
    public async Task AnUnresolvedCaller_IsTheSingle403_OnEveryRoute_WithNoWrite(string error)
    {
        var rule = _fixture.World.AddPolicy("POL-UNRESOLVED");
        _fixture.World.ReadError = error;
        var id = rule.Id.ToString("D");

        var responses = new[]
        {
            await Client().GetAsync(Base),
            await Client().GetAsync($"{Base}/{id}"),
            await Client().PutAsync($"{Base}/{id}/enabled", Json("{\"enabled\":false}")),
            await Client().PostAsync($"{Base}/{id}/retire", Json("{\"reason\":\"gone\"}")),
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
            (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("sdap.access.deny.caller_unresolved");
        }

        _fixture.World.WriteCount.Should().Be(0);
        _fixture.ClosureCalls.Should().BeEmpty();
    }

    // ── A Console User's write is refused by Dataverse ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AConsoleUser_PutEnabled_Is403_FromDataverse_AndTheRuleIsUnchanged()
    {
        var rule = _fixture.World.AddPolicy("POL-CONSOLE-PUT");
        _fixture.World.WriteRefusal = DataverseUserClientErrorCodes.AccessDenied;

        var response = await Client().PutAsync($"{Base}/{rule.Id:D}/enabled", Json("{\"enabled\":false}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.admin.forbidden");
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task AConsoleUser_Retire_Is403_FromDataverse_AndNothingIsClosed()
    {
        var rule = _fixture.World.AddPolicy("POL-CONSOLE-RETIRE");
        _fixture.World.WriteRefusal = DataverseUserClientErrorCodes.AccessDenied;

        var response = await Client().PostAsync($"{Base}/{rule.Id:D}/retire", Json("{\"reason\":\"No longer needed\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.admin.forbidden");
        rule.StatusCode.Should().Be(1);
        _fixture.ClosureCalls.Should().BeEmpty("a refused write never reaches the Signal closure");
    }

    [Fact]
    public async Task AConsoleUser_CanStillReadTheList_AndThePage()
    {
        var rule = _fixture.World.AddPolicy("POL-CONSOLE-READ");
        _fixture.World.AddVersion(rule);
        _fixture.World.WriteRefusal = DataverseUserClientErrorCodes.AccessDenied;

        (await Client().GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client().GetAsync($"{Base}/{rule.Id:D}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── 404, uniform ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "/enabled")]
    [InlineData("POST", "/retire")]
    public async Task ABillingRule_IsTheUniform404_WithZeroWrites_AndNeverClosed(string method, string suffix)
    {
        var billing = _fixture.World.AddPolicy("POL-BILLING", domain: OntologyAdminRulesWorld.Billing);
        var missing = Guid.NewGuid();

        async Task<HttpResponseMessage> Call(Guid id) => await Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{Base}/{id:D}{suffix}")
        {
            Content = method switch
            {
                "PUT" => Json("{\"enabled\":false}"),
                "POST" => Json("{\"reason\":\"gone\"}"),
                _ => null,
            },
        });

        var forBilling = await Call(billing.Id);
        var forMissing = await Call(missing);

        forBilling.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forMissing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyOf(forBilling)).GetProperty("reasonCode").GetString().Should().Be("ontology.rule.not_found");
        var billingBody = await BodyOf(forBilling);
        var missingBody = await BodyOf(forMissing);
        foreach (var field in new[] { "title", "detail", "status", "reasonCode" })
        {
            billingBody.GetProperty(field).ToString().Should().Be(missingBody.GetProperty(field).ToString(),
                "a rule that exists in another domain is indistinguishable from one that does not exist");
        }

        _fixture.World.WriteCount.Should().Be(0);
        _fixture.ClosureCalls.Should().BeEmpty();
    }

    // ── Validation, conflict, fault ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reason\":\"\"}")]
    [InlineData("{\"reason\":\"   \"}")]
    public async Task Retire_WithoutAReason_Is400_AndNothingIsReadOrWritten(string body)
    {
        var rule = _fixture.World.AddPolicy("POL-NOREASON");

        var response = await Client().PostAsync($"{Base}/{rule.Id:D}/retire", Json(body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("errors").TryGetProperty("reason", out _).Should().BeTrue();
        _fixture.World.Requests.Should().BeEmpty();
        _fixture.ClosureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PutEnabled_WithoutAValue_Is400_AndNothingIsReadOrWritten()
    {
        var rule = _fixture.World.AddPolicy("POL-NOENABLED");

        var response = await Client().PutAsync($"{Base}/{rule.Id:D}/enabled", Json("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("errors").TryGetProperty("enabled", out _).Should().BeTrue();
        _fixture.World.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ARetiredRule_CannotBeSwitched_OrRetiredAgain_409()
    {
        var rule = _fixture.World.AddPolicy("POL-RETIRED", configure: p => { p.StateCode = 1; p.StatusCode = 100000000; p.Enabled = false; });

        var put = await Client().PutAsync($"{Base}/{rule.Id:D}/enabled", Json("{\"enabled\":true}"));
        var retire = await Client().PostAsync($"{Base}/{rule.Id:D}/retire", Json("{\"reason\":\"again\"}"));

        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        retire.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.World.WriteCount.Should().Be(0);
        _fixture.ClosureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Retire_WhenTheRuleChangedSinceItWasOpened_Is409_WithNoWriteAndNoClosure()
    {
        var rule = _fixture.World.AddPolicy("POL-RACE-ROUTE");
        _fixture.World.BeforePatch = p => p.Version++; // another admin writes between this admin's read and write

        var response = await Client().PostAsync($"{Base}/{rule.Id:D}/retire", Json("{\"reason\":\"mine\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("reasonCode").GetString().Should().Be("ontology.rule.changed_since_read");
        body.GetProperty("detail").GetString().Should().Contain("changed since you opened it");
        _fixture.World.PreconditionFailures.Should().ContainSingle();
        _fixture.World.WriteCount.Should().Be(0);
        rule.StatusCode.Should().Be(1);
        _fixture.ClosureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AFaultReadingTheRules_Is503_NeverAShorterList()
    {
        _fixture.World.ReadError = DataverseUserClientErrorCodes.ServiceError;

        var response = await Client().GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await BodyOf(response)).GetProperty("reasonCode").GetString().Should().Be("ontology.admin.rules_unavailable");
    }

    // ── The shapes ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheList_Is200_WithLaneState_VersionInForce_AndTheFilterNumbers()
    {
        var rule = _fixture.World.AddPolicy("POL-LIST", configure: p => p.Lane = 100000000);
        _fixture.World.AddVersion(rule, number: 3);
        _fixture.World.AddPolicy("POL-OFF", enabled: false);

        var response = await Client().GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("metricsAvailable").GetBoolean().Should().BeTrue();
        var rules = body.GetProperty("rules").EnumerateArray().ToList();
        rules.Select(r => r.GetProperty("code").GetString()).Should().Equal("POL-LIST", "POL-OFF");
        var first = rules[0];
        first.GetProperty("lane").GetString().Should().Be("Decide");
        first.GetProperty("state").GetString().Should().Be("On");
        first.GetProperty("versionNumber").GetInt32().Should().Be(3);
        rules[1].GetProperty("state").GetString().Should().Be("Off");
    }

    [Fact]
    public async Task PutEnabled_False_Is200_AndWritesOnlyTheEnabledColumn_AsTheCaller()
    {
        var rule = _fixture.World.AddPolicy("POL-OFF-ROUTE");

        var response = await Client().PutAsync($"{Base}/{rule.Id:D}/enabled", Json("{\"enabled\":false}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.World.Patches.Should().ContainSingle().Which.Body.Should().Be("{\"sprk_enabled\":false}");
        rule.Enabled.Should().BeFalse();
        rule.StatusCode.Should().Be(1, "Off does not retire");
        _fixture.ClosureCalls.Should().BeEmpty("Off leaves open items for the next pass");
    }

    [Fact]
    public async Task Retire_WithAReason_Is200_WritesTheRuleFirst_ThenClosesItsSignals_AndReportsTheClosure()
    {
        var rule = _fixture.World.AddPolicy("POL-RETIRE-ROUTE");

        var response = await Client().PostAsync($"{Base}/{rule.Id:D}/retire", Json("{\"reason\":\"  Replaced by POL-NEW  \"}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("state").GetString().Should().Be("Retired");
        body.GetProperty("reason").GetString().Should().Be("Replaced by POL-NEW");
        body.GetProperty("closure").GetProperty("closed").GetInt32().Should().Be(3);
        body.GetProperty("closure").GetProperty("complete").GetBoolean().Should().BeTrue();
        rule.StatusCode.Should().Be(100000000);
        rule.RetiredReason.Should().Be("Replaced by POL-NEW");
        _fixture.ClosureCalls.Should().Equal(rule.Id);
        _fixture.World.Order.Should().ContainInOrder($"PATCH sprk_policies({rule.Id:D})", $"CLOSE {rule.Id:D}");
    }

    [Fact]
    public async Task ThePage_Is200_AndReadOnly()
    {
        var rule = _fixture.World.AddPolicy("POL-PAGE-ROUTE");
        _fixture.World.AddVersion(rule);

        var response = await Client().GetAsync($"{Base}/{rule.Id:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await BodyOf(response);
        body.GetProperty("summary").GetProperty("code").GetString().Should().Be("POL-PAGE-ROUTE");
        foreach (var section in new[] { "condition", "wording", "plan", "scope", "noise", "behaviour", "versions", "history" })
        {
            body.TryGetProperty(section, out _).Should().BeTrue($"the page carries a '{section}' section");
        }

        _fixture.World.WriteCount.Should().Be(0);
    }
}

/// <summary>The BFF with only the caller-identity client, the Signal closure and the inbound token substituted.</summary>
public class OntologyAdminRulesTestFixture : WorkspaceTestFixture
{
    public OntologyAdminRulesWorld World { get; private set; } = new();

    public List<Guid> ClosureCalls { get; } = new();

    public void Reset()
    {
        World = new OntologyAdminRulesWorld();
        ClosureCalls.Clear();
    }

    public HttpClient ClientFor(string oid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oid);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => World);

            ConfigureClosure(services);

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
    /// How Retire's closure is wired. Here the delegate is substituted (it records the call and answers a fixed outcome); the real-closure
    /// fixture overrides this to keep the production <c>PolicyRetiredClosure</c> registration and substitute only the Signal writer's
    /// Dataverse clients.
    /// </summary>
    protected virtual void ConfigureClosure(IServiceCollection services)
    {
        services.RemoveAll<PolicyRetiredClosure>();
        services.AddScoped<PolicyRetiredClosure>(_ => (policyId, _) =>
        {
            ClosureCalls.Add(policyId);
            World.RecordClosure(policyId);
            return Task.FromResult(new SignalClosureOutcome(Closed: 3, ChangedSinceRead: 0, Failed: 0));
        });
    }

    /// <summary>The bearer value is the oid; the principal carries it and the fixture tenant. No bearer is 401, as JwtBearer answers.</summary>
    private sealed class CallerTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OntologyRulesCaller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new("oid", header["Bearer ".Length..]),
                new("tid", WorkspaceTestConstants.TestTenantId),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
