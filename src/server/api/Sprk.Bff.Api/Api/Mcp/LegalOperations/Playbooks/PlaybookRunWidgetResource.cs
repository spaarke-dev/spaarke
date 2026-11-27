using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037c (spec FR-05, FR-16, NFR-03, NFR-09; owner Q17).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). One more
// static, state-free MCP resource on the existing /mcp/legal-operations host, registered in McpModule's resource collection: no package,
// no DI registration, no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: DecisionsWidgetResource, BriefingWidgetResource and SearchResultsWidgetResource (the ui:// resources), and McpModule's
//     resource collection.
//   Extension: a fourth instance of that same shape, not an extension of it: each MCP App widget is its own single-file ui:// resource with
//     its own URI, embedded page and metadata (owner Q5), and the existing resources are static classes with their constants baked in.
//     One shared factory for all of them is the refactor to make in one place (#3227, #3228); this task copies 035's shape.
//   Cost of doing nothing: a Copilot user who runs a playbook sees no progress and no results in the conversation, and the app-only results
//     tool (037b) has no widget to call it (owner Q17; the task's acceptance fails).
// =====================================================================================================================

/// <summary>
/// The MCP App widget resource <c>ui://spaarke/playbook-run.html</c> (spaarkeai-mcp-bff-api-r2 task 037c, FR-16, NFR-03): the single-file
/// build of <c>src/client/widgets/spaarke-playbook-run/</c> (a JPS playbook run's progress, then its results), served through
/// <c>resources/read</c> on <c>/mcp/legal-operations</c> and opened by the <c>_meta.ui.resourceUri</c> of <c>spaarke_run_playbook</c> and
/// <c>spaarke_get_playbook_run</c>.
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
/// (<see cref="McpEndpoints"/>), so an unauthenticated caller never reaches this read. The page carries no data: the run reaches it through
/// the tool result, its progress through <c>spaarke_get_playbook_run</c> and its results through the app-only
/// <c>spaarke_get_playbook_run_results</c>, as the caller.</para>
/// </remarks>
public static class PlaybookRunWidgetResource
{
    /// <summary>The resource URI the two playbook run tools' descriptors point at (<c>_meta.ui.resourceUri</c>) and the host asks for.</summary>
    public const string Uri = "ui://spaarke/playbook-run.html";

    /// <summary>MCP Apps' <c>RESOURCE_MIME_TYPE</c>.</summary>
    public const string MimeType = "text/html;profile=mcp-app";

    /// <summary>The manifest name <c>Sprk.Bff.Api.csproj</c> gives the built <c>dist/index.html</c>.</summary>
    public const string EmbeddedResourceName = "Sprk.Bff.Api.Widgets.playbook-run.html";

    private static readonly Lazy<string?> Html = new(LoadEmbeddedHtml);

    /// <summary>The resource the <c>legal-operations</c> route serves (built once: it holds no state between calls).</summary>
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = Uri,
            Name = "spaarke_playbook_run_widget",
            Title = "Spaarke playbook run",
            Description = "A Spaarke JPS playbook run's progress and results, rendered inline by hosts that support MCP Apps.",
            MimeType = MimeType,
            Meta = UiMeta(),
        });

    /// <summary>True when the build embedded the widget (false for a local build without <c>npm run build</c> in the widget folder).</summary>
    public static bool IsEmbedded => Html.Value != null;

    private static ReadResourceResult Read()
    {
        var html = Html.Value
            ?? throw new McpProtocolException(
                "The Spaarke playbook run widget is not part of this build. Build src/client/widgets/spaarke-playbook-run (npm run build) before building the BFF.",
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
        using var stream = typeof(PlaybookRunWidgetResource).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream == null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
