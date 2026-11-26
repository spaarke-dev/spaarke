// Regression anchor for GitHub issue #3165 (Matter Type search filter did nothing). The filter now reaches the index, and a request
// that names more matter types than the cap is refused with a 400 problem+json before any query is built, on both search routes.

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3165_MatterTypeFilterCapTests
{
    private static List<string> MatterTypes(int count) =>
        Enumerable.Range(0, count).Select(i => Guid.Parse($"3165d000-0000-4000-8000-{i:D12}").ToString("D")).ToList();

    private static DefaultHttpContext Caller() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "aaaaaaaa-3165-3165-3165-aaaaaaaaaaaa")], "test")),
    };

    [Fact]
    public async Task SemanticSearch_MoreMatterTypesThanTheCap_Is400WithTheTooManyMatterTypesCode()
    {
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Filters = new SearchFilters { MatterTypes = MatterTypes(SearchFilters.MaxMatterTypes + 1) },
        };

        var result = SemanticSearchEndpoints.ValidateFilters(request);

        result.Should().NotBeNull();
        var httpContext = Caller();
        var (status, contentType, body) = await ProblemJsonAssert.ExecuteAsync(result!, httpContext);
        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith(ProblemJsonAssert.ProblemJson);
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.TooManyMatterTypes);
    }

    [Fact]
    public void SemanticSearch_ExactlyTheCap_IsAccepted()
    {
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Filters = new SearchFilters { MatterTypes = MatterTypes(SearchFilters.MaxMatterTypes) },
        };

        SemanticSearchEndpoints.ValidateFilters(request).Should().BeNull();
    }

    [Fact]
    public async Task RecordSearch_MoreMatterTypesThanTheCap_Is400WithTheTooManyMatterTypesCode()
    {
        var httpContext = Caller();

        var result = await RecordSearchEndpoints.PostRecordSearch(
            new RecordSearchRequest
            {
                Query = "acme",
                RecordTypes = [RecordEntityType.Matter],
                Filters = new RecordSearchFilters { MatterTypes = MatterTypes(SearchFilters.MaxMatterTypes + 1) },
            },
            recordSearchService: null!, // never reached: the cap refuses first
            authorizationService: null!,
            httpContext,
            NullLoggerFactory.Instance,
            CancellationToken.None);
        var (status, contentType, body) = await ProblemJsonAssert.ExecuteAsync(result, httpContext);

        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith(ProblemJsonAssert.ProblemJson);
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.TooManyMatterTypes);
    }
}
