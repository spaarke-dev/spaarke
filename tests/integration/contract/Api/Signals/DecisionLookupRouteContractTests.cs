using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.Integration.Contract.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 016 (B-3, FR-07): the HTTP contract of <c>GET /api/v1/signals/{signalId}/lookup</c> through the REAL
/// <c>MapDecisionPlanEndpoints</c> (routing, the group's <c>RequireAuthorization</c>, binding, the handler, the JSON writer) and the real
/// <see cref="DecisionLookupSearch"/> over the module-boundary fakes. A new route, so no golden baseline: the shape is asserted here.
/// </summary>
public sealed class DecisionLookupRouteContractTests
{
    private static string Route(string queryString) => $"/api/v1/signals/{SignalId:D}/lookup{queryString}";

    [Fact]
    public async Task TheRoute_IsTheSignalsGroupsLookup_InTheDecisionPlanFile()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync("""{"actions":["revise-budget"]}""");

        var endpoint = host.Endpoint(DecisionLookupRouteHost.LookupPattern);

        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName.Should().Be("SearchSignalDecisionLookup");
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
    }

    [Fact]
    public async Task Found_Is200Json_WithTheParameter_TheTable_AndTheCandidates()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync("""{"actions":["revise-budget"]}""");

        using var response = await host.GetAsync(Route("?parameterCode=budget&query=Out"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("parameterCode", "entity", "candidates");
        root.GetProperty("parameterCode").GetString().Should().Be("budget");
        root.GetProperty("entity").GetString().Should().Be("sprk_budget");
        var candidate = root.GetProperty("candidates").EnumerateArray().Should().ContainSingle().Subject;
        candidate.EnumerateObject().Select(p => p.Name).Should().Equal("id", "name", "email");
        candidate.GetProperty("id").GetGuid().Should().Be(DecisionLookupWorld.MatterBudget);
        candidate.GetProperty("name").GetString().Should().Be("Outside counsel 2026");
        candidate.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AContact_CarriesItsEmail()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync("""{"actions":["assign-task"]}""");

        using var response = await host.GetAsync(Route("?parameterCode=assignee&query=Ada"));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var candidate = body.RootElement.GetProperty("candidates").EnumerateArray().Should().ContainSingle().Subject;
        candidate.GetProperty("email").GetString().Should().Be("ada@acme.example");
    }

    [Theory]
    [InlineData("?parameterCode=contactid&query=Ac")] // not in the plan
    [InlineData("?parameterCode=amount&query=1")]    // in the plan, not searchable
    [InlineData("?query=Out")]                       // no parameter at all: the same refusal, never a 400
    public async Task AParameterNotSearchableInThePlan_Is422_WithTheInvalidParameterReasonCode(string queryString)
    {
        await using var host = await DecisionLookupRouteHost.StartAsync("""{"actions":["revise-budget"]}""");

        using var response = await host.GetAsync(Route(queryString));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("reasonCode").GetString().Should().Be("ontology.decision.invalid_parameter");
        body.RootElement.GetProperty("title").GetString().Should().Be("Unprocessable Entity");
        body.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        host.World.Paths.Should().BeEmpty("nothing is searched for a parameter the plan does not offer");
    }

    [Fact]
    public async Task ASearchThatFails_Is503_WithTheAccessUnavailableReasonCode()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync("""{"actions":["revise-budget"]}""");
        host.World.Fault = 500;

        using var response = await host.GetAsync(Route("?parameterCode=budget&query=Out"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("reasonCode").GetString().Should().Be(DecisionPlanEndpoints.AccessUnavailableReasonCode);
        body.RootElement.TryGetProperty("candidates", out _).Should().BeFalse("a failure never carries a partial list");
    }
}

/// <summary>
/// Task 016: a minimal TestServer host of <c>MapDecisionPlanEndpoints</c> (the plan, suppression and lookup routes) with the real
/// <see cref="DecisionCapability"/> and <see cref="DecisionLookupSearch"/> over the module-boundary fakes of
/// <see cref="DecisionCapabilityFixtures.DecideSignalOnAMatter"/> and <see cref="DecisionLookupWorld"/>. A request is authenticated only
/// when it carries the test caller header, so an anonymous request meets the group's <c>RequireAuthorization</c>.
/// </summary>
internal sealed class DecisionLookupRouteHost : IAsyncDisposable
{
    public const string LookupPattern = "/api/v1/signals/{signalId:guid}/lookup";
    public const string PlanPattern = "/api/v1/signals/{signalId:guid}/decision-plan";

    private WebApplication? _app;
    private HttpClient? _client;

    private DecisionLookupRouteHost(Mock<IDataverseUserClient> users, Mock<IGenericEntityService> entities)
    {
        Users = users;
        Entities = entities;
        World = new DecisionLookupWorld(users);
    }

    public Mock<IDataverseUserClient> Users { get; }

    public Mock<IGenericEntityService> Entities { get; }

    public DecisionLookupWorld World { get; }

    public static async Task<DecisionLookupRouteHost> StartAsync(string plan, bool matterReadable = true)
    {
        var (access, plans, users, entities) = DecideSignalOnAMatter(matterReadable, plan);
        var host = new DecisionLookupRouteHost(users, entities);
        await host.InitializeAsync(access, plans);
        return host;
    }

    private async Task InitializeAsync(SignalCoreRecordAccess access, DecisionPlanService plans)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(opt => opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

        builder.Services.AddSingleton(access);
        builder.Services.AddSingleton(plans);
        builder.Services.AddSingleton(Users.Object);
        builder.Services.AddSingleton(Describer());
        // The plan route's collaborators that these tests never reach: stateless placeholders, so a call into one fails loudly.
        builder.Services.AddSingleton((SignalReadService)RuntimeHelpers.GetUninitializedObject(typeof(SignalReadService)));
        builder.Services.AddSingleton((SignalSuppressionService)RuntimeHelpers.GetUninitializedObject(typeof(SignalSuppressionService)));
        // Task 014: DecisionCapability also takes the commit service; the lookup route never commits.
        builder.Services.AddSingleton((DecisionCommitService)RuntimeHelpers.GetUninitializedObject(typeof(DecisionCommitService)));
        // Task 018: the capability takes the FR-04 privilege derivation (registered by SignalsModule in the real host).
        builder.Services.AddScoped<WorkItemPrivilege>();
        builder.Services.AddScoped<IDecisionCapability, DecisionCapability>();
        builder.Services.AddScoped<IDecisionLookupSearch, DecisionLookupSearch>();

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapDecisionPlanEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task<HttpResponseMessage> GetAsync(string url, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(TestAuthHandler.CallerHeader, "1");
        }

        return await _client!.SendAsync(request);
    }

    /// <summary>The mapped endpoint whose route pattern is <paramref name="pattern"/>.</summary>
    public RouteEndpoint Endpoint(string pattern) =>
        _app!.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == pattern);

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "DecisionLookupTest";
        public const string CallerHeader = "X-Test-Caller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(CallerHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("tid", TenantId), new Claim("oid", CallerOid.ToString("D"))], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
