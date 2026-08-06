// teams-app-r1 Task 060 (2026-08-03) — `tid`→environment routing filter tests.
//
// Proves the ADR-008 endpoint filter maps the router's deny/resolve outcome to the HTTP contract
// (spec FR-09 acceptance "DENIED (401/403)"): missing tid → 401, unmapped/ambiguous/malformed → 403
// (no environment attached, next NOT invoked), resolved → environment on HttpContext.Items + next
// invoked. Uses a REAL router over real options (no mocked collaborator-of-CUT) — the mock is only
// the framework EndpointFilterInvocationContext boundary, mirroring the existing filter tests.

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Routing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Routing;

public class TenantEnvironmentRoutingFilterTests
{
    private const string MappedTid = "11111111-1111-1111-1111-111111111111";
    private const string UnmappedTid = "99999999-9999-9999-9999-999999999999";

    private static TenantEnvironmentRoutingFilter BuildFilter(params TenantEnvironmentMapping[] tenants)
    {
        var router = new TenantEnvironmentRouter(
            Options.Create(new TenantEnvironmentRoutingOptions { Tenants = tenants.ToList() }),
            NullLogger<TenantEnvironmentRouter>.Instance);
        return new TenantEnvironmentRoutingFilter(router, NullLogger<TenantEnvironmentRoutingFilter>.Instance);
    }

    private static TenantEnvironmentMapping MappedDedicated => new()
    {
        Tid = MappedTid,
        DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
        EnvironmentId = "env-dedicated-acme",
        TenantScoped = false
    };

    private static (Mock<EndpointFilterInvocationContext> context, DefaultHttpContext http) CreateContext(string? tid)
    {
        var claims = new List<Claim>();
        if (tid is not null)
        {
            claims.Add(new Claim("tid", tid));
        }
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(http);
        return (contextMock, http);
    }

    [Fact]
    public async Task InvokeAsync_MappedTid_AttachesEnvironmentAndCallsNext()
    {
        var filter = BuildFilter(MappedDedicated);
        var (context, http) = CreateContext(MappedTid);
        var nextCalled = false;

        ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok("ok"));
        }

        var result = await filter.InvokeAsync(context.Object, Next);

        nextCalled.Should().BeTrue();
        http.Items[ResolvedTenantEnvironment.HttpContextItemsKey]
            .Should().BeOfType<ResolvedTenantEnvironment>()
            .Which.EnvironmentId.Should().Be("env-dedicated-acme");
        result.Should().NotBeOfType<ProblemHttpResult>();
    }

    [Fact]
    public async Task InvokeAsync_MissingTid_Returns401_NoEnvironment_NextNotCalled()
    {
        var filter = BuildFilter(MappedDedicated);
        var (context, http) = CreateContext(tid: null);
        var nextCalled = false;

        ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        }

        var result = await filter.InvokeAsync(context.Object, Next);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(401);
        nextCalled.Should().BeFalse();
        http.Items.Should().NotContainKey(ResolvedTenantEnvironment.HttpContextItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_UnmappedTid_Returns403_NoEnvironment_NextNotCalled()
    {
        var filter = BuildFilter(MappedDedicated);
        var (context, http) = CreateContext(UnmappedTid);
        var nextCalled = false;

        ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        }

        var result = await filter.InvokeAsync(context.Object, Next);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        nextCalled.Should().BeFalse();
        http.Items.Should().NotContainKey(ResolvedTenantEnvironment.HttpContextItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_AmbiguousTid_Returns403_NextNotCalled()
    {
        var dupA = new TenantEnvironmentMapping
        {
            Tid = MappedTid,
            DeploymentModel = TenantDeploymentModel.SpaarkeHostedDedicated,
            EnvironmentId = "env-A",
            TenantScoped = false
        };
        var dupB = new TenantEnvironmentMapping
        {
            Tid = MappedTid,
            DeploymentModel = TenantDeploymentModel.CustomerHosted,
            EnvironmentId = "env-B",
            TenantScoped = false
        };
        var filter = BuildFilter(dupA, dupB);
        var (context, http) = CreateContext(MappedTid);
        var nextCalled = false;

        ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        }

        var result = await filter.InvokeAsync(context.Object, Next);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        nextCalled.Should().BeFalse();
        http.Items.Should().NotContainKey(ResolvedTenantEnvironment.HttpContextItemsKey);
    }
}
