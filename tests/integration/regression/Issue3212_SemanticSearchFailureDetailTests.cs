// REGRESSION — issue #3212: POST /api/ai/search and POST /api/ai/search/count answered a 500 whose `detail` was the raw exception message
// (`Search Failed` / `Count Failed`). Exception text from Azure AI Search or the Dataverse enrichment can name the search endpoint, the index,
// a filter fragment or a Dataverse URL, and the signed-in user (or an MCP tool relaying the answer to a model) saw it. ADR-019: a 500 carries
// fixed text and a stable code; the exception is logged. The record route (RecordSearchEndpoints) already did this with RECORD_SEARCH_FAILED.
//
// Fails before the fix (detail == the exception's message, no `code`), passes after. KEEP path: regression (ADR-038).

using System.Net;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.Api.Mcp.Search;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

public sealed class Issue3212_SemanticSearchFailureDetailTests
{
    private const string Secret = "SECRET-ENDPOINT https://search.example.windows.net/indexes/knowledge-idx?api-key=SECRET-KEY&$filter=tenantId eq 'x'";

    [Fact]
    public async Task Search_AnUnexpectedFailure_Answers500WithFixedTextAndACode_NeverTheExceptionMessage()
    {
        await using var host = await SearchToolsHost.StartAsync();
        host.DocumentIndex.Fault = new InvalidOperationException(Secret);

        var (status, body) = await host.RestDocumentsAsync(new { query = "indemnity", scope = "all" });

        status.Should().Be(HttpStatusCode.InternalServerError, body.GetRawText());
        body.GetProperty("title").GetString().Should().Be("Search Failed");
        body.GetProperty("detail").GetString().Should().Be("The search could not be completed. Try again, and contact support if it persists.");
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.SemanticSearchFailed);
        body.GetRawText().Should().NotContain("SECRET").And.NotContain("windows.net").And.NotContain("$filter");
    }

    [Fact]
    public async Task Count_AnUnexpectedFailure_Answers500WithFixedTextAndACode_NeverTheExceptionMessage()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Fault = new InvalidOperationException(Secret);

        // The count route refuses scope=all, so it counts inside a readable parent.
        var (status, body) = await host.PostRestAsync(
            "/api/ai/search/count", new { query = "indemnity", scope = "entity", entityType = "matter", entityId = parent });

        status.Should().Be(HttpStatusCode.InternalServerError, body.GetRawText());
        body.GetProperty("title").GetString().Should().Be("Count Failed");
        body.GetProperty("detail").GetString().Should().Be("The count could not be completed. Try again, and contact support if it persists.");
        body.GetProperty("code").GetString().Should().Be(SearchErrorCodes.SemanticSearchCountFailed);
        body.GetRawText().Should().NotContain("SECRET").And.NotContain("windows.net").And.NotContain("$filter");
    }
}
