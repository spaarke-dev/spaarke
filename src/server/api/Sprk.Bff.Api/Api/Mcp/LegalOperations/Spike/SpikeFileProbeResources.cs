using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// THROWAWAY SPIKE (issue #3257, gap 1; branch spike/spe-file-probe): the two resources variant c's resource links point at. They are
// named *WidgetResource and shaped exactly like DecisionsWidgetResource (static, one parameterless Read, no request or caller) because
// that is the only resource registration the MCP guard sanctions (RouteAuthorizationGuardTests.Mcp.cs, McpWidgetResourceProblem):
// a fixed page of bytes that reads nothing as anyone. They are registered in McpModule's ResourceCollection beside the decisions widget.

/// <summary><c>resources/read</c> for <c>spaarke-probe://file/small.docx</c>: the small sample .docx, as a blob.</summary>
internal static class SpikeFileProbeSmallWidgetResource
{
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = SpikeFileProbeFiles.SmallUri,
            Name = "spaarke_spike_file_probe_small",
            Title = "Spike sample file (small)",
            Description = "Spike for a file-delivery test only; a fixed sample .docx. Not a real document.",
            MimeType = SpikeFileProbeFiles.DocxMimeType,
        });

    private static ReadResourceResult Read() => SpikeFileProbeFiles.ToReadResult(SpikeFileProbeFiles.Small());
}

/// <summary><c>resources/read</c> for <c>spaarke-probe://file/large.docx</c>: the large sample .docx, as a blob.</summary>
internal static class SpikeFileProbeLargeWidgetResource
{
    public static McpServerResource Instance { get; } = McpServerResource.Create(
        Read,
        new McpServerResourceCreateOptions
        {
            UriTemplate = SpikeFileProbeFiles.LargeUri,
            Name = "spaarke_spike_file_probe_large",
            Title = "Spike sample file (large)",
            Description = "Spike for a file-delivery test only; a fixed sample .docx. Not a real document.",
            MimeType = SpikeFileProbeFiles.DocxMimeType,
        });

    private static ReadResourceResult Read() => SpikeFileProbeFiles.ToReadResult(SpikeFileProbeFiles.Large());
}
