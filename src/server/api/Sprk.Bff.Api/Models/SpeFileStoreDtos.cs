namespace Sprk.Bff.Api.Models;

public record FileHandleDto(
    string Id,
    string Name,
    string? ParentId,
    long? Size,
    DateTimeOffset CreatedDateTime,
    DateTimeOffset LastModifiedDateTime,
    string? ETag,
    bool IsFolder,
    string? WebUrl,
    // multi-container-multi-index-r1 indexer-routing-fix: SPE drive ID resolved during upload.
    // Carried in the response so post-upload pipelines (RAG indexing via /api/ai/rag/index-file)
    // do not need a second round-trip to GET /api/obo/containers/{containerId}/drive. Required
    // by `IFileIndexingService.IndexFileAsync`. Optional for backwards compatibility with
    // pre-existing consumers that ignore it.
    string? DriveId = null);

// UploadSessionDto DELETED 2026-08-27 by unified-access-control-r2, dead by transitivity once task 073
// removed Api/UploadEndpoints.cs and the app-only chunked pair went with it. It carried a Graph
// PRE-AUTHENTICATED upload URL, which is why the retirement regression guard treats handing one to a
// caller as the interesting property. The OBO path uses UploadSessionResponse — a different type, still
// live, and NOT this one.

public record VersionInfoDto(
    string Id,
    string? ETag,
    DateTimeOffset LastModifiedDateTime,
    long Size);

public record ContainerDto(
    string Id,
    string DisplayName,
    string? Description,
    DateTimeOffset CreatedDateTime);

/// <summary>
/// File preview response (for iframe embedding)
/// Microsoft Docs: https://learn.microsoft.com/en-us/graph/api/driveitem-preview
/// </summary>
public record FilePreviewDto(
    string PreviewUrl,
    string? PostUrl,
    DateTimeOffset ExpiresAt,
    string? ContentType);

/// <summary>
/// File download response (for direct download or browser viewing)
/// Microsoft Docs: https://learn.microsoft.com/en-us/graph/api/driveitem-get-content
/// </summary>
public record FileDownloadDto(
    string DownloadUrl,
    string ContentType,
    string FileName,
    long Size,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Office web viewer response (for Word/Excel/PowerPoint files)
/// Microsoft Docs: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences
/// </summary>
public record OfficeViewerDto(
    string ViewerUrl,
    string EditorUrl,
    string FileType,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Lightweight Spaarke-domain summary of a SharePoint Embedded drive item — exposes only
/// the fields that <c>FileAccessEndpoints</c> consumes, so that the Graph SDK <c>DriveItem</c>
/// type stays inside <c>Infrastructure.Graph</c> per ADR-007 §1.
///
/// Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088b.
/// </summary>
public record SpeDriveItemSummary(
    string Id,
    string Name,
    long? Size,
    string? WebUrl,
    string? WebDavUrl,
    string? MimeType,
    string? ParentReferencePath,
    DateTimeOffset? LastModifiedDateTime,
    DateTimeOffset? CreatedDateTime);

/// <summary>
/// What Graph <c>/shares</c> said about an absolute document URL (FR-01, task 012).
/// </summary>
public enum SpeSharedItemOutcome
{
    /// <summary>Graph returned a drive item with an id and a parent drive id.</summary>
    Resolved,

    /// <summary>Every encoding form came back 400/404: the URL names nothing Graph can resolve.</summary>
    NotFound,

    /// <summary>Graph answered 401/403 for the caller and no form resolved.</summary>
    AccessDenied,

    /// <summary>
    /// Throttled, a Graph 5xx, or a transport failure. INDETERMINATE — it says nothing about whether the URL is
    /// a Spaarke document, and a caller must never read it as "not one".
    /// </summary>
    Unavailable,
}

/// <summary>One <c>/shares</c> call: which encoding form, and the HTTP status / Graph error code it produced.</summary>
public sealed record SpeSharedItemAttempt(string Form, int? StatusCode, string? ErrorCode);

/// <summary>
/// Result of resolving a document URL through Graph <c>/shares</c>. <see cref="Attempts"/> records every form
/// tried so the logs carry the evidence for which encoding Graph accepts (task 012, SPIKE-1 link 2).
/// </summary>
public sealed record SpeSharedItemResolution(
    SpeSharedItemOutcome Outcome,
    string? DriveId,
    string? ItemId,
    string? ResolvedForm,
    IReadOnlyList<SpeSharedItemAttempt> Attempts);
