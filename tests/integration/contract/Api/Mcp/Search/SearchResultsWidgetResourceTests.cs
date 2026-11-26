// CONTRACT — the MCP App widget resource ui://spaarke/search-results.html on /mcp/legal-operations (spaarkeai-mcp-bff-api-r2 task 035, FR-16,
// NFR-03). KEEP path: endpoint-contract. Same shape as BriefingWidgetResourceTests (task 034).
//
// WHAT RUNS: the production AddMcpModule / MapMcpEndpoints on a real Kestrel listener (McpTestHost) behind a real JwtBearer scheme; the resource
// served is the production SearchResultsWidgetResource, so these tests exercise the real registration, not a probe. The two search tools'
// descriptors come from the production tools on SearchToolsHost.
// BUILD-DEPENDENT CASES: the page is embedded only when src/client/widgets/spaarke-search-results was built before this assembly
// (EmbeddedResource Condition=Exists). The two cases that depend on it are SkippableFacts, so each run reports one of them as Skipped (never a
// vacuous pass): "built" runs after `npm run build` in the widget folder (CI publish), "not built" runs in a plain local build.
// NOT HERE: the widget's own behaviour (its jest suite) and the tools' text and structuredContent (SearchToolsContractTests, unchanged by 035).

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Search;

public sealed class SearchResultsWidgetResourceTests
{
    private static readonly Guid Caller = Guid.Parse("66666666-7777-4888-9999-cccccccccccc");

    private const string WidgetUri = "ui://spaarke/search-results.html";

    private const string ExpectedMimeType = "text/html;profile=mcp-app";

    private static Task<JsonElement> ReadAsync(McpTestHost host, string uri) =>
        host.RpcAsync(McpEndpoints.LegalOperationsRoute, "resources/read", new { uri }, McpTestHost.MintToken(Caller));

    // ---- always runs: the registration and the declared metadata ---------------------------------------------------------

    [Fact]
    public async Task ResourcesList_ServesTheSearchResultsWidget_WithMcpAppMimeType_AndAnEmptyCsp()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var list = await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "resources/list", new { }, McpTestHost.MintToken(Caller));

        var widget = list.GetProperty("resources").EnumerateArray().Single(r => r.GetProperty("uri").GetString() == WidgetUri);
        widget.GetProperty("mimeType").GetString().Should().Be(ExpectedMimeType);
        AssertEmptyCsp(widget.GetProperty("_meta"));
    }

    [Fact]
    public async Task BothSearchTools_PointAtThisResource_AndNothingElseAboutThemChanged()
    {
        SearchDocumentsTool.WidgetUri.Should().Be(WidgetUri).And.Be(SearchResultsWidgetResource.Uri);
        SearchResultsWidgetResource.Uri.Should().Be(FindRecordsTool.WidgetUri);

        await using var host = await SearchToolsHost.StartAsync();
        var list = await host.RpcAsync("tools/list", new { });

        var tools = list.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(["spaarke_search_documents", "spaarke_find_records"]);
        foreach (var tool in tools)
        {
            var ui = tool.GetProperty("_meta").GetProperty("ui");
            ui.GetProperty("resourceUri").GetString().Should().Be(WidgetUri, $"{tool.GetProperty("name").GetString()} renders in the search results widget");
            ui.EnumerateObject().Select(p => p.Name).Should().Equal(["resourceUri"], "only the resourceUri is added to the descriptor (the default visibility is model and app)");
        }
    }

    [Fact]
    public async Task ResourcesRead_ForAnUnknownUri_IsAnErrorNotTheWidget()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var act = () => ReadAsync(host, "ui://spaarke/other.html");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*JSON-RPC error*");
    }

    [Fact]
    public async Task ResourcesRead_WithoutAToken_Returns401_AndWhenMcpIsOff_Returns503()
    {
        await using (var host = await McpTestHost.StartAsync(mcpEnabled: true))
        {
            using var anonymous = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "resources/read", new { uri = WidgetUri }, token: null);
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the widget is served to authenticated callers only");
        }

        await using (var off = await McpTestHost.StartAsync(mcpEnabled: false))
        {
            using var response = await off.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "resources/read", new { uri = WidgetUri }, McpTestHost.MintToken(Caller));
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the kill switch answers before the transport runs");

            using var list = await off.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "resources/list", new { }, McpTestHost.MintToken(Caller));
            list.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "with MCP off the resource is not listed either");
        }
    }

    // ---- built: the page is embedded -----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ResourcesRead_WhenTheWidgetIsBuilt_ReturnsTheEmbeddedHtml_WithMimeType_AndAnEmptyCsp()
    {
        Skip.IfNot(SearchResultsWidgetResource.IsEmbedded, "The widget is not built: run `npm run build` in src/client/widgets/spaarke-search-results, then rebuild the BFF.");
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var result = await ReadAsync(host, WidgetUri);

        var content = result.GetProperty("contents").EnumerateArray().Single();
        content.GetProperty("uri").GetString().Should().Be(WidgetUri);
        content.GetProperty("mimeType").GetString().Should().Be(ExpectedMimeType);
        AssertEmptyCsp(content.GetProperty("_meta"));

        var html = content.GetProperty("text").GetString();
        html.Should().Be(EmbeddedHtml(), "the resource serves exactly the bytes the build embedded");
        html.Should().StartWithEquivalentOf("<!doctype html", "it is the single-file page, not a placeholder");
        html.Should().Contain(SearchDocumentsTool.ToolName).And.Contain(FindRecordsTool.ToolName, "it is the search results widget (it reads and continues these tools), not another widget's page");
        html!.Length.Should().BeLessThan(3 * 1024 * 1024, "NFR-03: the build fails above 3 MB, so the embedded page is under it");
    }

    // ---- not built: an error, never stale html --------------------------------------------------------------------------

    [SkippableFact]
    public async Task ResourcesRead_WhenTheWidgetIsNotBuilt_IsAnErrorResult_NotStaleHtml()
    {
        Skip.If(SearchResultsWidgetResource.IsEmbedded, "The widget is built into this assembly; this case runs in a build made without it.");
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var act = () => ReadAsync(host, WidgetUri);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not part of this build*", "the host is told the page is missing instead of being served an old or empty one");
    }

    // ---- the startup warning when the build embeds no widget ---------------------------------------------------------------

    [Theory]
    [InlineData(true, false, 1)] // enabled, no widget in this build: one Warning naming the fix
    [InlineData(true, true, 0)] // enabled, widget embedded: nothing
    [InlineData(false, false, 0)] // switched off: nothing (the route answers 503 anyway)
    public void WarnIfWidgetMissing_LogsOneWarningNamingTheFix_OnlyWhenEnabledAndTheWidgetIsNotEmbedded(bool enabled, bool embedded, int warnings)
    {
        var logs = new DecisionToolsTestHost.CapturingLoggerProvider();

        McpEndpoints.WarnIfWidgetMissing(logs.CreateLogger("test"), enabled, embedded, "search-results", SearchResultsWidgetResource.Uri, "spaarke-search-results");

        var entries = logs.Entries.ToList();
        entries.Should().HaveCount(warnings);
        foreach (var entry in entries)
        {
            entry.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
            entry.Message.Should().Contain("Build src/client/widgets/spaarke-search-results before publishing").And.Contain(WidgetUri);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private static void AssertEmptyCsp(JsonElement meta)
    {
        var csp = meta.GetProperty("ui").GetProperty("csp");
        csp.ValueKind.Should().Be(JsonValueKind.Object, "an empty csp object declares no network origins");
        csp.EnumerateObject().Should().BeEmpty("the widget connects to nothing but its host (FR-16)");
    }

    private static string EmbeddedHtml()
    {
        using var stream = typeof(SearchResultsWidgetResource).Assembly.GetManifestResourceStream(SearchResultsWidgetResource.EmbeddedResourceName)
            ?? throw new InvalidOperationException("The widget is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
