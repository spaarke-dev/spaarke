using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 186, review round 2 F3: the run-time default-deny of
/// <see cref="WorkforceCeilingEndpointFilter"/>, driven through <see cref="WorkforceCeilingEndpointFilter.InvokeAsync"/>
/// directly. An endpoint with no <see cref="WorkforceCeilingMetadata"/> (or no endpoint at all) refuses a workforce contact
/// with 403 <c>workforce_baseline_privilege_missing</c> and never calls the rest of the pipeline (must-fire); a systemuser
/// on the same endpoint goes through untouched (must-not-fire).
/// </summary>
[Trait("category", "external-access")]
public sealed class WorkforceCeilingEndpointFilterTests
{
    private static readonly Guid Me = Guid.Parse("99999999-9999-9999-9999-99999999f186");

    public static TheoryData<bool> UndeclaredEndpoints => new() { true, false };

    [Theory]
    [MemberData(nameof(UndeclaredEndpoints))]
    public async Task AWorkforceContact_OnAnUndeclaredEndpoint_Is403_AndNextIsNeverCalled(bool hasEndpoint)
    {
        var ceiling = new WorkforceCeilingDouble();
        var context = Invocation(CeilingRouteTestData.WorkforceContact(Me), ceiling, hasEndpoint);
        var nextCalls = 0;

        var result = await new WorkforceCeilingEndpointFilter().InvokeAsync(context, _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        nextCalls.Should().Be(0, "an endpoint that declares no ceiling is denied, not waved through");
        ceiling.Reads.Should().Be(0, "the denial needs no role read");
    }

    [Theory]
    [MemberData(nameof(UndeclaredEndpoints))]
    public async Task ASystemuser_OnTheSameUndeclaredEndpoint_GoesThrough(bool hasEndpoint)
    {
        var principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Me,
            SystemUserId = Guid.NewGuid(),
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };
        var ceiling = new WorkforceCeilingDouble();
        var context = Invocation(principal, ceiling, hasEndpoint);
        var nextCalls = 0;
        var passed = Results.Ok();

        var result = await new WorkforceCeilingEndpointFilter().InvokeAsync(context, _ =>
        {
            nextCalls++;
            return ValueTask.FromResult<object?>(passed);
        });

        result.Should().BeSameAs(passed);
        nextCalls.Should().Be(1);
        ceiling.Reads.Should().Be(0, "the ceiling does not apply to a systemuser");
    }

    private static DefaultEndpointFilterInvocationContext Invocation(
        CallerPrincipal principal, WorkforceCeilingDouble ceiling, bool hasEndpoint)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDataversePrivilegeChecker>(ceiling)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Items[CallerPrincipal.HttpContextItemsKey] = principal;
        if (hasEndpoint)
            httpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "GET /api/v1/external/undeclared"));
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }
}
