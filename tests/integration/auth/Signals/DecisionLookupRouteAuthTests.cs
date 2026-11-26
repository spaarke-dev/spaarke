using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Sprk.Bff.Api.Tests.Integration.Contract.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.AccessControl.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 016 (B-3, spec FR-07; ADR-028; ADR-008; ADR-002 fail closed): the lookup search runs only as a signed-in
/// caller and answers an unreadable Signal exactly as the decision-plan route does. With no request context or an unauthenticated principal
/// it answers <c>UserContextRequired</c> before any Dataverse call; an anonymous HTTP request is 401; the route carries the Signals group's
/// workforce authorization and never the external-plane <c>ExternalCollaboration</c> policy.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public sealed class DecisionLookupRouteAuthTests
{
    private const string BudgetPlan = """{"actions":["revise-budget"]}""";

    public static TheoryData<string> NoCaller => new() { "no-context", "anonymous", "claims-without-authentication" };

    private static HttpContext? Request(string caller) => caller switch
    {
        "no-context" => null,
        "anonymous" => new DefaultHttpContext(),
        _ => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", TenantId), new Claim("oid", CallerOid.ToString("D"))])) },
    };

    [Theory]
    [MemberData(nameof(NoCaller))]
    public async Task SearchAsync_WithNoSignedInCaller_ReturnsUserContextRequired_AndMakesNoDataverseCall(string caller)
    {
        var (access, plans, users, entities) = DecideSignalOnAMatter(plan: BudgetPlan);

        var result = await new DecisionLookupSearch(access, plans, users.Object, NullLogger<DecisionLookupSearch>.Instance)
            .SearchAsync(Request(caller), SignalId, "budget", "Out", CancellationToken.None);

        result.Should().Be(DecisionLookupResult.UserContextRequired);
        var tool = DecisionResultMapper.ToToolError(result)!;
        (tool.ReasonCode, tool.Status).Should().Be((DataverseUserClientErrorCodes.UserContextRequired, StatusCodes.Status401Unauthorized));
        var problem = DecisionResultMapper.ToHttp(result, SignedIn()).Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status401Unauthorized, "REST and MCP share the answer");
        users.VerifyNoOtherCalls();
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SearchAsync_WhenTheCallerClientHasNoUserContext_ReturnsUserContextRequired_NeverAnAppOnlyRead()
    {
        // The OBO client fails closed without a user context (no token); the search passes that on and reads nothing else.
        var (access, plans, users, entities) = DecideSignalOnAMatter(plan: BudgetPlan);
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_budgets?", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.UserContextRequired, "no user"));

        var result = await new DecisionLookupSearch(access, plans, users.Object, NullLogger<DecisionLookupSearch>.Instance)
            .SearchAsync(SignedIn(), SignalId, "budget", "Out", CancellationToken.None);

        result.Should().Be(DecisionLookupResult.UserContextRequired);
        entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAnonymousRequest_Is401_AndNothingIsRead()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync(BudgetPlan);

        using var response = await host.GetAsync($"/api/v1/signals/{SignalId:D}/lookup?parameterCode=budget&query=Out", authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Users.Invocations.Should().BeEmpty("the group's RequireAuthorization answers before the handler runs");
    }

    [Fact]
    public async Task ASignalTheCallerCannotRead_GetsTheDecisionPlanRoutesRefusal_AndNothingIsSearched()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync(BudgetPlan, matterReadable: false);

        using var lookup = await host.GetAsync($"/api/v1/signals/{SignalId:D}/lookup?parameterCode=budget&query=Other%20matter%20budget");
        using var plan = await host.GetAsync($"/api/v1/signals/{SignalId:D}/decision-plan");

        lookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        plan.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (lookupBody, planBody) = (await Problem(lookup), await Problem(plan));
        lookupBody.Should().Equal(planBody, "the same uniform refusal, apart from the per-request correlation fields");
        lookupBody["reasonCode"].Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        host.World.Paths.Should().BeEmpty("no lookup table is read for a Signal the caller cannot read");
    }

    [Fact]
    public async Task TheRoute_CarriesOnlyTheSignalsGroupsWorkforceAuthorization_NeverTheExternalCollaborationPolicy()
    {
        await using var host = await DecisionLookupRouteHost.StartAsync(BudgetPlan);

        var lookup = host.Endpoint(DecisionLookupRouteHost.LookupPattern).Metadata.GetOrderedMetadata<IAuthorizeData>();
        var plan = host.Endpoint(DecisionLookupRouteHost.PlanPattern).Metadata.GetOrderedMetadata<IAuthorizeData>();

        lookup.Should().NotBeEmpty("the route requires an authenticated caller");
        lookup.Select(a => a.Policy).Should().NotContain(AuthPolicies.ExternalCollaboration);
        lookup.Select(a => (a.Policy, a.Roles, a.AuthenticationSchemes)).Should().Equal(
            plan.Select(a => (a.Policy, a.Roles, a.AuthenticationSchemes)), "the lookup adds no policy beyond the plan route's group");
        host.Endpoint(DecisionLookupRouteHost.LookupPattern).Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
    }

    /// <summary>A problem body's members, without the per-request ones (<c>traceId</c>, <c>correlationId</c>).</summary>
    private static async Task<Dictionary<string, string>> Problem(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "correlationId"))
            .ToDictionary(p => p.Name, p => p.Value.ToString());
    }
}
