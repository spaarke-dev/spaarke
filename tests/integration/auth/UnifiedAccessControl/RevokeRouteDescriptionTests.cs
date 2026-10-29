using FluentAssertions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C2b: the revoke route's published description states the rule the shared revoke applies, read from the route
/// the host actually maps. No portal web role is assigned or removed by a revoke.
/// </summary>
public class RevokeRouteDescriptionTests : IClassFixture<WorkspaceTestFixture>
{
    private readonly WorkspaceTestFixture _fixture;

    public RevokeRouteDescriptionTests(WorkspaceTestFixture fixture) => _fixture = fixture;

    private string? MappedDescription()
        => _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/external-access/revoke"
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains("POST"))
            .Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description;

    [Fact]
    public void TheMappedRoute_PublishesTheRuleLevelDescription()
        => MappedDescription().Should().Be(RevokeExternalAccessEndpoint.RouteDescription);

    [Fact]
    public void TheDescription_StatesTheRevokeSteps_AndThatNoWebRoleIsInvolved()
    {
        var description = RevokeExternalAccessEndpoint.RouteDescription;

        description.Should().Contain("every active sprk_externalrecordaccess row of the grant is deactivated");
        description.Should().Contain("SPE container permissions");
        description.Should().Contain("grant cache is invalidated");
        description.Should().Contain("Assigned-To");
        description.Should().Contain("No portal web role is assigned or removed.");
        description.Should().NotContain("removes the Power Pages web role");
    }
}
