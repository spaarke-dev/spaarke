namespace Spe.Bff.Api.Models;

public record DriveItemDto(
    string Id,
    string Name,
    long? Size,
    string? ETag,
    DateTimeOffset? LastModifiedDateTime,
    string? ContentType,
    FolderDto? Folder
);

public record FolderDto(
    int? ChildCount
);

public record ListingResponse(
    IList<DriveItemDto> Items,
    string? NextLink
);

public record ListingParameters(
    int Top = 50,
    int Skip = 0,
    string OrderBy = "name",
    string OrderDir = "asc"
)
{
    public int ValidatedTop => Math.Min(Math.Max(Top, 1), 200);
    public int ValidatedSkip => Math.Max(Skip, 0);
    public string ValidatedOrderBy => IsValidOrderBy(OrderBy) ? OrderBy.ToLowerInvariant() : "name";
    public string ValidatedOrderDir => IsValidOrderDir(OrderDir) ? OrderDir.ToLowerInvariant() : "asc";

    private static bool IsValidOrderBy(string orderBy) =>
        orderBy?.ToLowerInvariant() is "name" or "lastmodifieddatetime" or "size";

    private static bool IsValidOrderDir(string orderDir) =>
        orderDir?.ToLowerInvariant() is "asc" or "desc";
}