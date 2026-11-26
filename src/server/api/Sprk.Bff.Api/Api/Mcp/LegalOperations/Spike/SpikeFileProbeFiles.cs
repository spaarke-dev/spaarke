using ModelContextProtocol.Protocol;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// =====================================================================================================================
// THROWAWAY SPIKE (issue #3257, gap 1): branch spike/spe-file-probe, never merged to master. Remove with the branch.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: DecisionsWidgetResource (an embedded static page served through McpModule's ResourceCollection).
//   Extension: that resource is HTML for one URI; the probe needs two fixed binary files at two URIs, so it gets its own two resources.
//   Cost of doing nothing: the owner cannot test whether a Cowork sandbox receives a tool result as a file (variants a, b, c).
// =====================================================================================================================

/// <summary>
/// The two fixed sample .docx files of the file-delivery spike, embedded in the BFF assembly (<c>Sprk.Bff.Api.csproj</c>), and the one
/// place that turns them into protocol content. The files carry a <c>.bin</c> suffix on disk only so Git LFS (<c>*.docx</c>) never
/// replaces them with a pointer; the bytes are the .docx bytes, pinned by SHA-256 in the contract test.
/// </summary>
internal static class SpikeFileProbeFiles
{
    public const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public const string SmallUri = "spaarke-probe://file/small.docx";

    public const string LargeUri = "spaarke-probe://file/large.docx";

    internal const string SmallResourceName = "Sprk.Bff.Api.SpikeFileProbe.small.docx";
    internal const string LargeResourceName = "Sprk.Bff.Api.SpikeFileProbe.large.docx";

    private static readonly Lazy<byte[]> SmallBytes = new(() => Load(SmallResourceName));
    private static readonly Lazy<byte[]> LargeBytes = new(() => Load(LargeResourceName));

    /// <summary>One sample file: <paramref name="Size"/> is <c>small</c> or <c>large</c>.</summary>
    public sealed record SampleFile(string Size, string FileName, string Uri, byte[] Bytes)
    {
        public string Base64 => Convert.ToBase64String(Bytes);
    }

    /// <summary>The sample file of <paramref name="size"/> (<c>small</c> | <c>large</c>), or null for anything else.</summary>
    public static SampleFile? ForSize(string? size) =>
        string.Equals(size, "small", StringComparison.Ordinal) ? Small()
        : string.Equals(size, "large", StringComparison.Ordinal) ? Large()
        : null;

    public static SampleFile Small() => new("small", "spaarke-file-probe-small.docx", SmallUri, SmallBytes.Value);

    public static SampleFile Large() => new("large", "spaarke-file-probe-large.docx", LargeUri, LargeBytes.Value);

    /// <summary>The <c>resources/read</c> answer for <paramref name="file"/>: one blob item with the file's bytes.</summary>
    public static ReadResourceResult ToReadResult(SampleFile file) =>
        new() { Contents = [BlobResourceContents.FromBytes(file.Bytes, file.Uri, DocxMimeType)] };

    private static byte[] Load(string resourceName)
    {
        using var stream = typeof(SpikeFileProbeFiles).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The spike file '{resourceName}' is not embedded in this build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
