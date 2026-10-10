// Task 261 (G31): POST /api/spe/search/items runs an app-only /search/query, which Microsoft documents as unsupported for
// SharePoint Embedded content (delegated only), and which stamps no longer hold Files.Read.All to attempt. When Graph
// refuses, the route returns a defined 501 `spe-search-requires-delegated`, never a raw 403 or a silent empty list.

using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.SpeAdmin;

public class SearchItemsNotSupportedTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void GraphRefusal_IsAnExplicit501_WithAStableCode(int graphStatus)
    {
        var result = SearchItemsEndpoints.NotSupportedForIdentity(
            new SpaarkeStorageException("Access denied", statusCode: graphStatus), "trace-1");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(501);
        problem.ProblemDetails.Extensions["code"].Should().Be("spe-search-requires-delegated");
        problem.ProblemDetails.Extensions["traceId"].Should().Be("trace-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public void AnyOtherGraphFailure_IsNotRelabelled(int? graphStatus)
    {
        SearchItemsEndpoints.NotSupportedForIdentity(
            new SpaarkeStorageException("boom", statusCode: graphStatus), "t").Should().BeNull();
    }
}
