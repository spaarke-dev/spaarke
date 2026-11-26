using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Briefing;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 034 (spec FR-09, FR-16, NFR-03, NFR-09).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). One more
// static, state-free MCP resource on the existing /mcp/legal-operations host, registered in McpModule's resource collection: no package,
// no DI registration, no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: DecisionsWidgetResource (the ui://spaarke/decisions.html resource) and McpModule's resource collection.
//   Extension: it is a second instance of that same shape, not an extension of it: each MCP App widget is its own single-file ui://
//     resource with its own URI, embedded page and metadata (owner Q5: one Console component per widget), and DecisionsWidgetResource
//     is r1's static class with its constants baked in. A shared factory for the three r2 widgets and the decisions one is the refactor to
//     make once the third copy lands (notes/defer-issues.md); this task copies the proven shape, as the brief says.
//   Cost of doing nothing: Copilot Chat and Cowork cannot render the Daily Briefing (FR-09 / FR-16 acceptance fails).
// =====================================================================================================================

/// <summary>
/// The MCP App widget resource <c>ui://spaarke/briefing.html</c> (spaarkeai-mcp-bff-api-r2 task 034, FR-09, FR-16, NFR-03): the single-file
/// build of <c>src/client/widgets/spaarke-briefing/</c> (the Console's <c>DailyBriefingApp</c> behind task 010's data seam), served through
/// <c>resources/read</c> on <c>/mcp/legal-operations</c> and opened by the <c>spaarke_get_briefing</c> tool's <c>_meta.ui.resourceUri</c>.
/// </summary>
/// <remarks>
/// <para><b>Declares no network origins.</b> <c>_meta.ui.csp</c> is an empty object on both the <c>resources/list</c> entry and the
/// <c>resources/read</c> content item, so the host frame lets the widget talk to nothing but the host (the widget holds no token and
/// calls only <c>callServerTool</c>).</para>
/// <para><b>Embedded, never stale.</b> The build output is an <c>EmbeddedResource</c> with <c>Condition=Exists</c> (Sprk.Bff.Api.csproj),
/// so a local <c>dotnet build</c> without the widget built still succeeds. <c>resources/read</c> then answers a JSON-RPC error saying the
/// widget is not part of the build; it never serves a leftover or placeholder page. The publish and deploy workflows build the widget
/// first, so a published artifact always embeds it.</para>
/// <para><b>Access.</b> The route's <c>.RequireAuthorization()</c> and the A10 connector-token filter run before the transport
/// (<see cref="McpEndpoints"/>), so an unauthenticated caller never reaches this read. The page carries no data: the briefing reaches it
/// through the tool result and later reads through the same tool, as the caller.</para>
/// </remarks>
public static class BriefingWidgetResource
{
    /// <summary>The resource URI the tool descriptor points at (<c>_meta.ui.resourceUri</c>) and the host asks for.</summary>
    public const string Uri = "ui://spaarke/briefing.html";

    /// <summary>MCP Apps' <c>RESOURCE_MIME_TYPE</c>.</summary>
    public const string MimeType = "text/html;profile=mcp-app";

    /// <summary>The manifest name <c>Sprk.Bff.Api.csproj</c> gives the built <c>dist/index.html</c>.</summary>
    public const string EmbeddedResourceName = "Sprk.Bff.Api.Widgets.briefing.html";

    private static readonly Lazy<string?> Html = new(LoadEmbeddedHtml);

    /// <summary>The resource the <c>legal-operations</c> route serves (built once: it holds no state between calls).</summary>
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = Uri,
            Name = "spaarke_briefing_widget",
            Title = "Spaarke daily briefing",
            Description = "The Spaarke AI Daily Briefing, rendered inline by hosts that support MCP Apps.",
            MimeType = MimeType,
            Meta = UiMeta(),
        });

    /// <summary>True when the build embedded the widget (false for a local build without <c>npm run build</c> in the widget folder).</summary>
    public static bool IsEmbedded => Html.Value != null;

    private static ReadResourceResult Read()
    {
        var html = Html.Value
            ?? throw new McpProtocolException(
                "The Spaarke daily briefing widget is not part of this build. Build src/client/widgets/spaarke-briefing (npm run build) before building the BFF.",
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
        using var stream = typeof(BriefingWidgetResource).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
