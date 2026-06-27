namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for centralized post-upload RAG indexing
/// (<see cref="Services.Ai.IPostUploadIndexingEnqueuer"/>).
/// </summary>
/// <remarks>
/// Bound from <c>Indexing</c> section of appsettings. All defaults are safe for
/// production — leaving the section out of config keeps the helper enabled with
/// the 200 MB size cap.
/// </remarks>
public sealed class PostUploadIndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>
    /// Whether the post-upload enqueue helper actually submits jobs. When false,
    /// the helper short-circuits with an INFO log and returns a "Skipped" result.
    /// Operators can flip this to <c>false</c> in App Service config + restart to
    /// disable indexing wholesale during incidents (Service Bus saturation,
    /// AI Search outage, runaway costs) without redeploying the BFF.
    /// Default: <c>true</c>.
    /// </summary>
    public bool PostUploadEnqueueEnabled { get; set; } = true;

    /// <summary>
    /// Maximum file size in bytes that is eligible for automatic indexing.
    /// Files larger than this are skipped at enqueue time with an INFO log;
    /// operators can manually re-trigger via <c>POST /api/ai/rag/index-file</c>.
    /// Default: 200 MB (209,715,200 bytes).
    /// </summary>
    /// <remarks>
    /// Rationale: very large files overwhelm the chunker + embedding API costs.
    /// The default of 200 MB covers the vast majority of business documents
    /// (PDFs, Office files, transcripts). Adjust upward if your tenant routinely
    /// processes larger artifacts.
    /// </remarks>
    public long MaxIndexableBytes { get; set; } = 200L * 1024L * 1024L;
}
