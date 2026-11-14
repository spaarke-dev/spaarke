using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Api.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b: the two browser re-file routes that can unfile a communication publish
/// every refusal the unfile rule answers, read from the routes the host actually maps: 409 for an owner that cannot be used, 503
/// for one that cannot be read now (<c>ProblemDetailsHelper.RecordOwnerRefused</c>).
/// </summary>
public class DirectUnfileRouteContractTests : IClassFixture<WorkspaceTestFixture>
{
    private readonly WorkspaceTestFixture _fixture;

    public DirectUnfileRouteContractTests(WorkspaceTestFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("/api/v1/child-records/{table}/{id:guid}")]
    [InlineData("/api/communications/{id:guid}/filing")]
    public void TheReFileRoute_PublishesTheOwnerRefusals_409And503(string route)
    {
        var statuses = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route
                         && e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains("PATCH"))
            .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(m => m.StatusCode)
            .ToArray();

        statuses.Should().Contain(StatusCodes.Status409Conflict);
        statuses.Should().Contain(StatusCodes.Status503ServiceUnavailable);
    }
}
