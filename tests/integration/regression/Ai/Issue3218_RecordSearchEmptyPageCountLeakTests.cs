using FluentAssertions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Tests.Integration.Auth.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Ai;

/// <summary>
/// GitHub #3218 (spaarkeai-mcp-bff-api-r2 task 027, D-027-2): <c>POST /api/ai/search/records</c> answered a page past the end with
/// <c>metadata.totalCount</c> = the index's UNFILTERED tenant-wide count, because the per-row authorization returned an empty
/// response untouched. A caller who could read none of the matches learned how many matters match a query in the tenant, the same
/// number the non-empty path already hides. Fixed in commit 48b79df4a5: the count is the rows in the response, for every page.
/// </summary>
/// <remarks>
/// Observation point: the REAL route (filter, handler, <see cref="Spaarke.Core.Auth.AuthorizationService"/>) and the shared method
/// <see cref="RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync"/> over the SAME stubs (the record index at its module
/// boundary; the caller's access data), via the host the task 027 shared-method suite uses. Before the fix both answered
/// <c>totalCount: 7</c> for the case below; the route's body was
/// <c>{"results":[],"metadata":{"totalCount":7,"searchTime":1,"hybridMode":"rrf"}}</c>.
/// </remarks>
public sealed class Issue3218_RecordSearchEmptyPageCountLeakTests
{
    [Fact(DisplayName = "#3218: an empty record page past the end carries no unfiltered tenant-wide count, by the method and by the route")]
    public async Task EmptyPagePastTheEnd_DoesNotLeakTheUnfilteredCount()
    {
        await using var host = await SearchAuthorizationSharedMethodTests.SearchAuthHost.StartAsync();

        // The index holds 7 matching matters tenant-wide; the caller pages past the end (offset 1000, limit 1), so the
        // page is empty but the index still reports the unfiltered total. None of the 7 is known to the caller.
        host.Records.ReturnsEmptyPage(unfilteredTotal: 7);
        var request = SearchAuthorizationSharedMethodTests.RecordRequest(RecordEntityType.Matter) with
        {
            Options = new RecordSearchOptions { Offset = 1000, Limit = 1 },
        };

        var (direct, rest) = await host.RecordsDirectThenRestAsync(request);

        direct.Outcome.Should().Be(RecordSearchAccessOutcome.Allowed);
        direct.Response!.Results.Should().BeEmpty();
        direct.Response.Metadata.TotalCount.Should().Be(0, "an empty page reports the count of rows in the response, as a filtered page does");
        rest.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await SearchAuthorizationSharedMethodTests.BodyAsync(rest);
        body["results"]!.AsArray().Should().BeEmpty();
        body["metadata"]!["totalCount"]!.GetValue<int>().Should().Be(0,
            $"the route must not disclose how many matters match tenant-wide. body: {body.ToJsonString()}");
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Access.Calls.Should().BeEmpty("an empty page has no record to ask Dataverse about");
    }
}
