// CONTRACT — the MCP App widget resource ui://spaarke/decisions.html on /mcp/legal-operations (spaarkeai-mcp-bff-api-r1 task 030, FR-12,
// NFR-03). KEEP path: endpoint-contract.
//
// WHAT RUNS: the production AddMcpModule / MapMcpEndpoints on a real Kestrel listener (McpTestHost) behind a real JwtBearer scheme; the
// resource served is the production DecisionsWidgetResource, so these tests exercise the real registration, not a probe.
// BUILD-DEPENDENT CASES: the page is embedded only when src/client/widgets/spaarke-decisions was built before this assembly
// (EmbeddedResource Condition=Exists). The two cases that depend on it are SkippableFacts, so each run reports one of them as Skipped
// (never a vacuous pass): "built" runs after `npm run build` in the widget folder (CI publish), "not built" runs in a plain local build.
// NOT HERE: the widget's own behaviour (its jest suite) and the tool descriptors that point at this URI (tasks 022-024).

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class DecisionsWidgetResourceTests
{
    private static readonly Guid Caller = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private const string WidgetUri = "ui://spaarke/decisions.html";

    private static readonly string ExpectedMimeType = "text/html;profile=mcp-app";

    private static Task<JsonElement> ReadAsync(McpTestHost host, string uri) =>
        host.RpcAsync(McpEndpoints.LegalOperationsRoute, "resources/read", new { uri }, McpTestHost.MintToken(Caller));

    // ---- always runs: the registration and the declared metadata ---------------------------------------------------------

    [Fact]
    public async Task ResourcesList_ServesExactlyTheWidget_WithMcpAppMimeType_AndAnEmptyCsp()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var list = await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "resources/list", new { }, McpTestHost.MintToken(Caller));

        var resources = list.GetProperty("resources").EnumerateArray().ToList();
        resources.Should().ContainSingle();
        var widget = resources[0];
        widget.GetProperty("uri").GetString().Should().Be(WidgetUri);
        widget.GetProperty("mimeType").GetString().Should().Be(ExpectedMimeType);
        AssertEmptyCsp(widget.GetProperty("_meta"));
    }

    [Fact]
    public async Task Initialize_AdvertisesTheResourcesCapability()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var initialize = await host.RpcAsync(
            McpEndpoints.LegalOperationsRoute,
            "initialize",
            new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "widget-contract-test", version = "0.0.1" } },
            McpTestHost.MintToken(Caller));

        initialize.GetProperty("capabilities").TryGetProperty("resources", out _).Should().BeTrue("a host asks for ui:// resources only when the server advertises them");
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
        }
    }

    // ---- built: the page is embedded -----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ResourcesRead_WhenTheWidgetIsBuilt_ReturnsTheEmbeddedHtml_WithMimeType_AndAnEmptyCsp()
    {
        Skip.IfNot(DecisionsWidgetResource.IsEmbedded, "The widget is not built: run `npm run build` in src/client/widgets/spaarke-decisions, then rebuild the BFF.");
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var result = await ReadAsync(host, WidgetUri);

        var content = result.GetProperty("contents").EnumerateArray().Single();
        content.GetProperty("uri").GetString().Should().Be(WidgetUri);
        content.GetProperty("mimeType").GetString().Should().Be(ExpectedMimeType);
        AssertEmptyCsp(content.GetProperty("_meta"));

        var html = content.GetProperty("text").GetString();
        html.Should().Be(EmbeddedHtml(), "the resource serves exactly the bytes the build embedded");
        html.Should().StartWithEquivalentOf("<!doctype html", "it is the single-file page, not a placeholder");
        html!.Length.Should().BeLessThan(3 * 1024 * 1024, "NFR-03: the build fails above 3 MB, so the embedded page is under it");
    }

    // ---- not built: an error, never stale html --------------------------------------------------------------------------

    [SkippableFact]
    public async Task ResourcesRead_WhenTheWidgetIsNotBuilt_IsAnErrorResult_NotStaleHtml()
    {
        Skip.If(DecisionsWidgetResource.IsEmbedded, "The widget is built into this assembly; this case runs in a build made without it.");
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var act = () => ReadAsync(host, WidgetUri);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not part of this build*", "the host is told the page is missing instead of being served an old or empty one");
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    private static void AssertEmptyCsp(JsonElement meta)
    {
        var csp = meta.GetProperty("ui").GetProperty("csp");
        csp.ValueKind.Should().Be(JsonValueKind.Object, "an empty csp object declares no network origins");
        csp.EnumerateObject().Should().BeEmpty("the widget connects to nothing but its host (FR-12)");
    }

    private static string EmbeddedHtml()
    {
        using var stream = typeof(DecisionsWidgetResource).Assembly.GetManifestResourceStream(DecisionsWidgetResource.EmbeddedResourceName)
            ?? throw new InvalidOperationException("The widget is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
