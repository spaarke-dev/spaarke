namespace Spe.Bff.Api.Models;

public record FileHandleDto(
    string Id,
    string Name,
    string? ParentId,
    long? Size,
    DateTimeOffset CreatedDateTime,
    DateTimeOffset LastModifiedDateTime,
    string? ETag,
    bool IsFolder);

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
