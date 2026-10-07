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

/// <summary>One SharePoint Embedded version of a file.</summary>
/// <param name="Id">The version id — for SharePoint also its label ("1.0", "2.0", …).</param>
/// <param name="ETag">Not populated by the version list (null).</param>
/// <param name="LastModifiedDateTime">When the version was written.</param>
/// <param name="Size">The version's size in bytes.</param>
/// <param name="LastModifiedBy">
///   Who wrote the version (Graph <c>lastModifiedBy</c> display name). For a version a relocation REPLAYED into a moved
///   file, the version history routes report the ORIGINAL author and date the relocation recorded (unified-access-control-r2
///   task 166, owner round 45 item 1; <c>Services.Documents.RelocatedVersionHistory</c>), because Graph cannot set them.
/// </param>
public record VersionInfoDto(
    string Id,
    string? ETag,
    DateTimeOffset LastModifiedDateTime,
    long Size,
    string? LastModifiedBy = null)
{
    /// <summary>
    /// The writer's Entra object id (<c>lastModifiedBy.user.id</c>), when a person wrote it. Server-side only (never
    /// serialized): the relocation records it as the replayed version's original author.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LastModifiedByUserId { get; init; }

    /// <summary>The writing application's id (<c>lastModifiedBy.application.id</c>) for an app-only write. Server-side only.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LastModifiedByApplicationId { get; init; }
}

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
/// Who created a SharePoint Embedded drive item — Graph's <c>createdBy</c> identity set, without SDK types (ADR-007).
/// The document-pointer check (unified-access-control-r2 task 166 r2, owner round 23 item 1) compares it with the
/// <c>sprk_document</c> row's creator before the BFF follows the row's pointer as the application.
/// </summary>
/// <param name="Name">The item's name (an archive-path item is named <c>{communicationId:N}_…</c>).</param>
/// <param name="UserObjectId">
///   <c>createdBy.user.id</c>: the Entra object id of the person who uploaded it (delegated / OBO uploads). Null for an
///   app-only upload (SharePoint reports "SharePoint App" with no id).
/// </param>
/// <param name="ApplicationId"><c>createdBy.application.id</c>: the Entra app (client) id the upload ran under.</param>
/// <param name="Size">The item's size in bytes (task 166 f1: the relocation copy is verified against it).</param>
/// <param name="QuickXorHash">The SPE content identity, when Graph has computed it (task 166 f1: copy verification).</param>
/// <param name="WebUrl">The item's web URL (task 166 f1: <c>sprk_filepath</c> of a server-attached or relocated file).</param>
/// <param name="LastModified">
///   <c>lastModifiedDateTime</c> (task 166, owner round 54 item 3): the time a relocation's witness records when Graph lists
///   no version, so the versions written after the move can still be put in order by their times.
/// </param>
public record SpeItemCreator(
    string? Name,
    string? UserObjectId,
    string? ApplicationId,
    long? Size = null,
    string? QuickXorHash = null,
    string? WebUrl = null,
    DateTimeOffset? LastModified = null,
    DateTimeOffset? Created = null,
    string? ParentPath = null);

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
