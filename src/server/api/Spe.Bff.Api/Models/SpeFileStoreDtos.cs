namespace Spe.Bff.Api.Models;

public record FileHandleDto(
    string Id,
    string Name,
    string? ParentId,
    long? Size,
    DateTimeOffset CreatedDateTime,
    DateTimeOffset LastModifiedDateTime,
    string? ETag,
    bool IsFolder,
    string? WebUrl);

public record UploadSessionDto(
    string UploadUrl,
    DateTimeOffset ExpirationDateTime);

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
