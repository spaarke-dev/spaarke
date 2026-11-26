// CONTRACT — what spaarke_search_documents and spaarke_find_records SAY about paging (spaarkeai-mcp-bff-api-r2 task 022 verifier fixes F1;
// FR-02, NFR-02). KEEP path: endpoint-contract. Same host as SearchToolsContractTests (the production MCP route and the real REST routes over
// module-boundary doubles).
//
// The facts pinned here: "Showing N ... best match first" (never "matched"); a short, non-empty page hedges ("There may be more than this");
// hasMore is "the page came back full" and is false over every record, where no offset continues a list; a page past the end says so; a page the
// size bound cut advises offset + kept and carries nextOffset. hasMore = false is NOT the end of the list (task 035 must not read it so).

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Mcp.Search.SearchToolsTestSupport;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Search;

public sealed class SearchToolsPagingTests
{
    private const string DocumentsTool = "spaarke_search_documents";
    private const string RecordsTool = "spaarke_find_records";

    private static JsonElement Content(JsonElement result) => result.GetProperty("structuredContent");

    private static bool HasMore(JsonElement result) => Content(result).GetProperty("hasMore").GetBoolean();

    /// <summary>The <c>nextOffset</c> the result carries: null when it is absent or JSON null (the property is always written).</summary>
    private static int? NextOffset(JsonElement result)
    {
        Content(result).TryGetProperty("nextOffset", out var next).Should().BeTrue("nextOffset is always written, null when no continuation is advised");
        return next.ValueKind == JsonValueKind.Null ? null : next.GetInt32();
    }

    // ---- wording ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task BothTools_SayShowing_BestMatchFirst_NeverMatched()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent));
        host.RecordIndex.Returns(Record(parent));

        var documents = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent });
        var records = await host.CallToolAsync(RecordsTool, new { query = "q" });

        Text(documents).Should().StartWith("Showing 1 document(s), best match first.").And.NotContain("matched");
        Text(records).Should().StartWith("Showing 1 record(s), best match first.").And.NotContain("matched");
    }

    // ---- a short page hedges ---------------------------------------------------------------------------------------

    [Fact]
    public async Task BothTools_AShortNonEmptyPage_SaysThereMayBeMore_AndAdvisesNoOffset()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent), Doc(parent));
        host.RecordIndex.Returns(Record(parent), Record(parent));

        var inRecord = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent, limit = 3 });
        var everywhere = await host.CallToolAsync(DocumentsTool, new { query = "q", limit = 3 });
        var records = await host.CallToolAsync(RecordsTool, new { query = "q", limit = 3 });

        foreach (var result in new[] { inRecord, everywhere, records })
        {
            Text(result).Should().Contain("There may be more than this.").And.NotContainEquivalentOf("send offset");
            HasMore(result).Should().BeFalse("the page did not come back full");
            NextOffset(result).Should().BeNull();
        }
    }

    // ---- hasMore: the page came back full --------------------------------------------------------------------------

    [Fact]
    public async Task SearchDocuments_InsideOneRecord_AFullPage_IsHasMore_AndAdvisesOffsetPlusLimit()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Enumerable.Range(0, 10).Select(_ => Doc(parent)).ToArray());

        var result = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent, limit = 3, offset = 3 });

        HasMore(result).Should().BeTrue();
        NextOffset(result).Should().Be(6);
        Text(result).Should().Contain("The page was full; send offset 6 for the next page.");
        host.DocumentIndex.Requests.Should().ContainSingle().Which.Options!.Offset.Should().Be(3, "the stub honours the offset it is sent");
    }

    [Fact]
    public async Task FindRecords_AFullPage_IsHasMore_AndAdvisesOffsetPlusLimit()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        ids.ForEach(id => host.Access.Grant(SearchToolsHost.Matters, id, AccessRights.Read));
        host.RecordIndex.Returns(ids.Select(id => Record(id)).ToArray());

        var result = await host.CallToolAsync(RecordsTool, new { query = "q", limit = 3, offset = 3 });

        HasMore(result).Should().BeTrue();
        NextOffset(result).Should().Be(6);
        Text(result).Should().Contain("The page was full; send offset 6 for the next page.");
    }

    // ---- over every record: no offset continues the list ------------------------------------------------------------

    [Fact]
    public async Task SearchDocuments_OverEveryRecord_AFullPage_NeverAdvisesAnOffset_AndSaysToSearchInsideOneRecord()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Enumerable.Range(0, 10).Select(_ => Doc(parent)).ToArray());

        var result = await host.CallToolAsync(DocumentsTool, new { query = "q", limit = 3 });

        Items(result).Should().HaveCount(3, "the page is full");
        HasMore(result).Should().BeFalse("no offset continues a list over every record");
        NextOffset(result).Should().BeNull();
        Text(result).Should().Contain("For more, search inside one matter, project or invoice (entityType + entityId).");
        Text(result).Should().NotContainEquivalentOf("send offset").And.NotContain("offset 3");
    }

    [Fact]
    public async Task SearchDocuments_OverEveryRecord_PageTwoWouldRepeatHits_SoItIsNeverAdvised()
    {
        // WHY the advice is withheld: rows under a parent the caller cannot read are skipped AFTER the index ranks them, so the first page
        // consumes more candidate rows than it returns, and "offset + limit" starts inside what it already showed.
        await using var host = await SearchToolsHost.StartAsync();
        var readable = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, readable, AccessRights.Read);
        host.DocumentIndex.Returns(
            Doc(readable, "A1"), Doc(hidden, "H1"), Doc(readable, "A2"), Doc(hidden, "H2"), Doc(readable, "A3"), Doc(readable, "A4"), Doc(readable, "A5"));

        var first = await host.CallToolAsync(DocumentsTool, new { query = "q", limit = 3 });
        var second = await host.CallToolAsync(DocumentsTool, new { query = "q", limit = 3, offset = 3 });

        Items(first).Select(i => i.GetProperty("name").GetString()).Should().Equal("A1", "A2", "A3");
        Items(second).Select(i => i.GetProperty("name").GetString()).Should().Contain("A3", "offset 3 starts at the fourth ranked row, inside what page one consumed");
        NextOffset(first).Should().BeNull("so the tool does not send the model there");
        HasMore(first).Should().BeFalse();
        Text(first).Should().NotContain("offset 3");
    }

    // ---- a page past the end ---------------------------------------------------------------------------------------

    [Fact]
    public async Task BothTools_APagePastTheEnd_SaysSo_AndAnEmptyFirstPageKeepsItsText()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Doc(parent), Doc(parent), Doc(parent));
        host.RecordIndex.Returns(Record(parent), Record(parent), Record(parent));

        var documentsPast = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent, offset = 5 });
        var recordsPast = await host.CallToolAsync(RecordsTool, new { query = "q", offset = 5 });

        Text(documentsPast).Should().StartWith("No more documents after the first 5.");
        Text(recordsPast).Should().StartWith("No more records after the first 5.");

        host.DocumentIndex.Returns();
        host.RecordIndex.Returns();
        var documentsFirst = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent });
        var recordsFirst = await host.CallToolAsync(RecordsTool, new { query = "q" });

        Text(documentsFirst).Should().StartWith("No documents you can open matched that search.");
        Text(recordsFirst).Should().StartWith("No records you can open matched that search.");

        foreach (var result in new[] { documentsPast, recordsPast, documentsFirst, recordsFirst })
        {
            Items(result).Should().BeEmpty();
            HasMore(result).Should().BeFalse();
            NextOffset(result).Should().BeNull();
        }
    }

    // ---- the size cut ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SearchDocuments_WhenTheSizeBoundCutsHits_AdvisesOffsetPlusKept_NotOffsetPlusLimit()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Enumerable.Range(0, 70)
            .Select(_ => Doc(parent, new string('語', 600), new string('語', 300)) with { Highlights = [new string('語', 1000)], FileType = new string('f', 80) })
            .ToArray());

        var result = await host.CallToolAsync(DocumentsTool, new { query = "q", entityType = "matter", entityId = parent, limit = 50, offset = 10 });

        Content(result).GetProperty("truncated").GetBoolean().Should().BeTrue("the maximum-size fixture does not fit in 64 KiB at 50 hits");
        var kept = Items(result).Count;
        kept.Should().BeInRange(1, 49);
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(SearchToolSupport.ResultLimitBytes);
        NextOffset(result).Should().Be(10 + kept, "the next page starts at the first hit that was cut");
        Text(result).Should().Contain($"Send offset {10 + kept} for the next page.").And.NotContain("offset 60");
        Text(result).Should().StartWith($"Showing {kept} document(s), best match first.").And.Contain("The list was cut to fit.");
    }

    [Fact]
    public async Task FindRecords_WhenTheSizeBoundCutsHits_AdvisesOffsetPlusKept_NotOffsetPlusLimit()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.RecordIndex.Returns(Enumerable.Range(0, 70)
            .Select(_ => Record(parent, new string('語', 600)) with
            {
                RecordDescription = new string('語', 1000),
                ReferenceNumbers = [new string('9', 200), new string('8', 200), new string('7', 200), new string('6', 200)],
            })
            .ToArray());

        var result = await host.CallToolAsync(RecordsTool, new { query = "q", limit = 50, offset = 10 });

        Content(result).GetProperty("truncated").GetBoolean().Should().BeTrue();
        var kept = Items(result).Count;
        kept.Should().BeInRange(1, 49);
        NextOffset(result).Should().Be(10 + kept);
        Text(result).Should().Contain($"Send offset {10 + kept} for the next page.").And.NotContain("offset 60");
    }

    [Fact]
    public async Task SearchDocuments_OverEveryRecord_WhenTheSizeBoundCutsHits_AdvisesNoOffset()
    {
        await using var host = await SearchToolsHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(SearchToolsHost.Matters, parent, AccessRights.Read);
        host.DocumentIndex.Returns(Enumerable.Range(0, 70)
            .Select(_ => Doc(parent, new string('語', 600), new string('語', 300)) with { Highlights = [new string('語', 1000)], FileType = new string('f', 80) })
            .ToArray());

        var result = await host.CallToolAsync(DocumentsTool, new { query = "q", limit = 50 });

        Content(result).GetProperty("truncated").GetBoolean().Should().BeTrue();
        NextOffset(result).Should().BeNull();
        HasMore(result).Should().BeFalse();
        Text(result).Should().Contain("The list was cut to fit.").And.Contain(SearchToolSupport.NarrowToOneRecordAdvice).And.NotContainEquivalentOf("send offset");
    }

    // ---- the descriptions ------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_TheDocumentDescriptionSaysOffsetContinuesAListOnlyInsideOneRecord_AndHasMoreMeansAFullPage()
    {
        await using var host = await SearchToolsHost.StartAsync();

        var list = await host.RpcAsync("tools/list", new { });

        var tools = list.GetProperty("tools").EnumerateArray().ToList();
        var documents = tools.Single(t => t.GetProperty("name").GetString() == DocumentsTool);
        documents.GetProperty("description").GetString().Should().Contain("only there does offset continue a list").And.Contain("hasMore means the page came back full");
        documents.GetProperty("inputSchema").GetProperty("properties").GetProperty("offset").GetProperty("description").GetString()
            .Should().Contain("only inside one record");
        var records = tools.Single(t => t.GetProperty("name").GetString() == RecordsTool);
        records.GetProperty("description").GetString().Should().Contain("hasMore means the page came back full");
    }
}
