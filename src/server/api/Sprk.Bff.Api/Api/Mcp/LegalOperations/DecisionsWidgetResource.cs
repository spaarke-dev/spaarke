using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations;

/// <summary>
/// The MCP App widget resource <c>ui://spaarke/decisions.html</c> (spaarkeai-mcp-bff-api-r1 task 030, FR-12, NFR-03): the single-file
/// build of <c>src/client/widgets/spaarke-decisions/</c>, served through <c>resources/read</c> on <c>/mcp/legal-operations</c>.
/// </summary>
/// <remarks>
/// <para><b>Declares no network origins.</b> <c>_meta.ui.csp</c> is an empty object on both the <c>resources/list</c> entry and the
/// <c>resources/read</c> content item, so the host frame lets the widget talk to nothing but the host (the widget holds no token and
/// calls only <c>callServerTool</c>).</para>
/// <para><b>Embedded, never stale.</b> The build output is an <c>EmbeddedResource</c> with <c>Condition=Exists</c> (Sprk.Bff.Api.csproj),
/// so a local <c>dotnet build</c> without the widget built still succeeds. <c>resources/read</c> then answers a JSON-RPC error saying the
/// widget is not part of the build; it never serves a leftover or placeholder page. <c>publish-bff-artifact.yml</c> builds the widget first,
/// so a published artifact always embeds it.</para>
/// <para><b>Access.</b> The route's <c>.RequireAuthorization()</c> and the A10 connector-token filter run before the transport
/// (<see cref="McpEndpoints"/>), so an unauthenticated caller never reaches this read. The page carries no data: every Work Item reaches
/// it later through the app-only tools, as the caller.</para>
/// </remarks>
public static class DecisionsWidgetResource
{
    /// <summary>The resource URI the tool descriptors point at (<c>_meta.ui.resourceUri</c>) and the host asks for.</summary>
    public const string Uri = "ui://spaarke/decisions.html";

    /// <summary>MCP Apps' <c>RESOURCE_MIME_TYPE</c>.</summary>
    public const string MimeType = "text/html;profile=mcp-app";

    /// <summary>The manifest name <c>Sprk.Bff.Api.csproj</c> gives the built <c>dist/index.html</c>.</summary>
    public const string EmbeddedResourceName = "Sprk.Bff.Api.Widgets.decisions.html";

    private static readonly Lazy<string?> Html = new(LoadEmbeddedHtml);

    /// <summary>The resource the <c>legal-operations</c> route serves (built once: it holds no state between calls).</summary>
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = Uri,
            Name = "spaarke_decisions_widget",
            Title = "Spaarke decisions",
            Description = "The Spaarke Decide lane and Decision wizard, rendered inline by hosts that support MCP Apps.",
            MimeType = MimeType,
            Meta = UiMeta(),
        });

    /// <summary>True when the build embedded the widget (false for a local build without <c>npm run build</c> in the widget folder).</summary>
    public static bool IsEmbedded => Html.Value is not null;

    private static ReadResourceResult Read()
    {
        var html = Html.Value
            ?? throw new McpProtocolException(
                "The Spaarke decisions widget is not part of this build. Build src/client/widgets/spaarke-decisions (npm run build) before building the BFF.",
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
        using var stream = typeof(DecisionsWidgetResource).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
