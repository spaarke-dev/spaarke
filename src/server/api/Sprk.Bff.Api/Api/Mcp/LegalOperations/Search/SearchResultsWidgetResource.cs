using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 035 (spec FR-16, NFR-03, NFR-09).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). One more
// static, state-free MCP resource on the existing /mcp/legal-operations host, registered in McpModule's resource collection: no package,
// no DI registration, no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: BriefingWidgetResource and DecisionsWidgetResource (the ui:// resources), and McpModule's resource collection.
//   Extension: a third instance of that same shape, not an extension of it: each MCP App widget is its own single-file ui:// resource with
//     its own URI, embedded page and metadata (owner Q5), and the two existing resources are static classes with their constants baked in.
//     One shared factory for the three is the refactor to make in one place (#3227, #3228); this task copies the proven shape, as the brief says.
//   Cost of doing nothing: search results reach Copilot Chat and Cowork only as text, and FR-16's search-results widget acceptance fails.
// =====================================================================================================================

/// <summary>
/// The MCP App widget resource <c>ui://spaarke/search-results.html</c> (spaarkeai-mcp-bff-api-r2 task 035, FR-16, NFR-03): the single-file
/// build of <c>src/client/widgets/spaarke-search-results/</c> (the Console's <c>SearchResultsWidget</c>, props only), served through
/// <c>resources/read</c> on <c>/mcp/legal-operations</c> and opened by the <c>_meta.ui.resourceUri</c> of <c>spaarke_search_documents</c> and
/// <c>spaarke_find_records</c>.
/// </summary>
/// <remarks>
/// <para><b>Declares no network origins.</b> <c>_meta.ui.csp</c> is an empty object on both the <c>resources/list</c> entry and the
/// <c>resources/read</c> content item, so the host frame lets the widget talk to nothing but the host (the widget holds no token and
/// calls only <c>callServerTool</c> and <c>openLink</c>).</para>
/// <para><b>Embedded, never stale.</b> The build output is an <c>EmbeddedResource</c> with <c>Condition=Exists</c> (Sprk.Bff.Api.csproj),
/// so a local <c>dotnet build</c> without the widget built still succeeds. <c>resources/read</c> then answers a JSON-RPC error saying the
/// widget is not part of the build; it never serves a leftover or placeholder page. The publish and deploy workflows build the widget
/// first, so a published artifact always embeds it.</para>
/// <para><b>Access.</b> The route's <c>.RequireAuthorization()</c> and the A10 connector-token filter run before the transport
/// (<see cref="McpEndpoints"/>), so an unauthenticated caller never reaches this read. The page carries no data: the hits reach it through
/// the tool result and further pages through the same tools, as the caller.</para>
/// </remarks>
public static class SearchResultsWidgetResource
{
    /// <summary>The resource URI the two search tools' descriptors point at (<c>_meta.ui.resourceUri</c>) and the host asks for.</summary>
    public const string Uri = "ui://spaarke/search-results.html";

    /// <summary>MCP Apps' <c>RESOURCE_MIME_TYPE</c>.</summary>
    public const string MimeType = "text/html;profile=mcp-app";

    /// <summary>The manifest name <c>Sprk.Bff.Api.csproj</c> gives the built <c>dist/index.html</c>.</summary>
    public const string EmbeddedResourceName = "Sprk.Bff.Api.Widgets.search-results.html";

    private static readonly Lazy<string?> Html = new(LoadEmbeddedHtml);

    /// <summary>The resource the <c>legal-operations</c> route serves (built once: it holds no state between calls).</summary>
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = Uri,
            Name = "spaarke_search_results_widget",
            Title = "Spaarke search results",
            Description = "Spaarke document and record search results, rendered inline by hosts that support MCP Apps.",
            MimeType = MimeType,
            Meta = UiMeta(),
        });

    /// <summary>True when the build embedded the widget (false for a local build without <c>npm run build</c> in the widget folder).</summary>
    public static bool IsEmbedded => Html.Value != null;

    private static ReadResourceResult Read()
    {
        var html = Html.Value
            ?? throw new McpProtocolException(
                "The Spaarke search results widget is not part of this build. Build src/client/widgets/spaarke-search-results (npm run build) before building the BFF.",
                McpErrorCode.InternalError);

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = Uri,
                    MimeType = MimeType,
                    Text = html,
                    Meta = UiMeta(),
                },
            ],
        };
    }

    // { "ui": { "csp": {} } }: an empty CSP object means no connect, resource, frame or base-URI origins. A new instance each call:
    // a JsonNode can have one parent.
    private static JsonObject UiMeta() => new() { ["ui"] = new JsonObject { ["csp"] = new JsonObject() } };

    private static string? LoadEmbeddedHtml()
    {
        using var stream = typeof(SearchResultsWidgetResource).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
