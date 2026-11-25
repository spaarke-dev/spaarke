using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 009 (adversarial-review finding F1-low): <c>POST /api/ai/search/records</c>
/// enforces the request bounds its DTO declares, and never returns raw exception text.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> <c>[StringLength(1000)]</c> and <c>[Range]</c> on <see cref="RecordSearchRequest"/> /
/// <see cref="RecordSearchOptions"/> are metadata only — Minimal APIs run no validation filter here — so
/// <c>limit:0</c> / <c>offset:-1</c> reached Azure AI Search, which refused them, and the catch-all handler
/// answered 500 with <c>detail: ex.Message</c>: raw exception text on the wire, which the BFF rule forbids.
/// </para>
/// <para>
/// <b>Observation point.</b> Same as <c>RecordSearchRowAuthorizationContractTests</c>: the SHIPPED
/// <c>PostRecordSearch</c> handler runs; only the search service (the module boundary where the Azure call
/// would happen) is replaced, by a double that counts calls and can be told to throw. Helpers are local.
/// </para>
/// </remarks>
[Trait("category", "contract")]
public sealed class RecordSearchRequestBoundsContractTests
{
    private const string TenantId = "aaaaaaaa-2222-3333-4444-555555555555";
    private const string CallerOid = "cccccccc-8888-7777-6666-555555555555";

    public static TheoryData<string, RecordSearchRequest, string> OutOfRangeRequests() => new()
    {
        {
            "query over the limit",
            Request(query: new string('q', RecordSearchRequest.MaxQueryLength + 1)),
            SearchErrorCodes.QueryTooLong
        },
        { "limit zero", Request(limit: 0), SearchErrorCodes.InvalidLimit },
        { "limit negative", Request(limit: -1), SearchErrorCodes.InvalidLimit },
        { "limit over the maximum", Request(limit: RecordSearchOptions.MaxLimit + 1), SearchErrorCodes.InvalidLimit },
        { "offset negative", Request(offset: -1), SearchErrorCodes.InvalidOffset },
        { "offset over the maximum", Request(offset: RecordSearchOptions.MaxOffset + 1), SearchErrorCodes.InvalidOffset },
    };

    [Theory]
    [MemberData(nameof(OutOfRangeRequests))]
    public async Task OutOfRangeInput_Is400_AndNeverReachesTheSearchService(
        string _, RecordSearchRequest request, string expectedCode)
    {
        var search = new RecordingSearchService();

        var result = await InvokeAsync(search, request);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be(expectedCode);
        search.Calls.Should().Be(0, "a bad bound is refused before any Azure AI Search call is made");
    }

    [Theory]
    [InlineData(RecordSearchRequest.MaxQueryLength, RecordSearchOptions.MinLimit, RecordSearchOptions.MinOffset)]
    [InlineData(1, RecordSearchOptions.MaxLimit, RecordSearchOptions.MaxOffset)]
    public async Task InputExactlyAtTheBounds_IsAccepted(int queryLength, int limit, int offset)
    {
        var search = new RecordingSearchService();

        var result = await InvokeAsync(search, Request(query: new string('q', queryLength), limit: limit, offset: offset));

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(200);
        search.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AServiceFailure_Is500_AndTheResponseCarriesNoExceptionText()
    {
        const string Leak = "Azure.RequestFailedException: Invalid skip value at https://secret-search.search.windows.net/indexes('x')";
        var search = new RecordingSearchService { Throw = new InvalidOperationException(Leak) };

        var result = await InvokeAsync(search, Request());

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Extensions["code"].Should().Be(SearchErrorCodes.RecordSearchFailed);
        var wire = $"{problem.ProblemDetails.Title}|{problem.ProblemDetails.Detail}|"
            + string.Join("|", problem.ProblemDetails.Extensions.Values);
        wire.Should().NotContain("secret-search").And.NotContain("RequestFailedException").And.NotContain("skip",
            "the exception is logged structurally; the caller gets fixed text only");
        problem.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace("the failure is still described, just not with exception text");
    }

    // =====================================================================================
    // Helpers — local to this file
    // =====================================================================================

    private static RecordSearchRequest Request(string query = "acme", int? limit = null, int? offset = null) => new()
    {
        Query = query,
        RecordTypes = [RecordEntityType.Matter],
        Options = limit is null && offset is null
            ? null
            : new RecordSearchOptions { Limit = limit ?? 20, Offset = offset ?? 0 },
    };

    private static Task<IResult> InvokeAsync(RecordingSearchService search, RecordSearchRequest request) =>
        RecordSearchEndpoints.PostRecordSearch(
            request,
            search,
            new AuthorizationService(new AllowAllAccessDataSource(), Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance),
            CallerContext(),
            NullLoggerFactory.Instance,
            CancellationToken.None);

    private static DefaultHttpContext CallerContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim(CallerResolution.ObjectIdClaim, CallerOid)], "Test")),
        };
        httpContext.Request.Headers.Authorization = "Bearer test-caller-token";
        httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey] = new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = new Dictionary<string, string> { [RecordEntityType.Matter] = "sprk_matters" },
        };
        return httpContext;
    }

    private sealed class AllowAllAccessDataSource : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken,
            CancellationToken ct = default) =>
            Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = AccessRights.Read,
            });
    }

    /// <summary>The module boundary where the Azure call would happen: counts calls, can be told to throw.</summary>
    private sealed class RecordingSearchService : IRecordSearchService
    {
        public int Calls { get; private set; }

        public Exception? Throw { get; init; }

        public Task<RecordSearchResponse> SearchAsync(
            RecordSearchRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(new RecordSearchResponse
            {
                Results = [],
                Metadata = new RecordSearchMetadata
                {
                    TotalCount = 0,
                    SearchTime = 1,
                    HybridMode = RecordHybridSearchMode.Rrf,
                },
            });
        }
    }
}
