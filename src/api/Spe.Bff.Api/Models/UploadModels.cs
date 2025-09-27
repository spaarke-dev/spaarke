namespace Spe.Bff.Api.Models;

public record UploadSessionResponse(
    string UploadUrl,
    DateTimeOffset ExpirationDateTime
);

public record ChunkUploadRequest(
    string UploadSessionUrl,
    string ContentRange,
    byte[] ChunkData
);

public record ChunkUploadResponse(
    int StatusCode,
    DriveItemDto? CompletedItem = null
);

public enum ConflictBehavior
{
    Replace,
    Rename,
    Fail
}

public static class ConflictBehaviorExtensions
{
    public static ConflictBehavior ParseConflictBehavior(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "replace" => ConflictBehavior.Replace,
            "rename" => ConflictBehavior.Rename,
            "fail" => ConflictBehavior.Fail,
            _ => ConflictBehavior.Replace // Default
        };
    }

    public static string ToGraphString(this ConflictBehavior behavior)
    {
        return behavior switch
        {
            ConflictBehavior.Replace => "replace",
            ConflictBehavior.Rename => "rename",
            ConflictBehavior.Fail => "fail",
            _ => "replace"
        };
    }
}

public record ContentRangeHeader(
    long Start,
    long End,
    long? Total
)
{
    public static ContentRangeHeader? Parse(string? contentRange)
    {
        if (string.IsNullOrWhiteSpace(contentRange))
            return null;

        // Expected format: "bytes 0-1023/2048" or "bytes 0-1023/*"
        if (!contentRange.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
            return null;

        var rangeAndTotal = contentRange["bytes ".Length..].Split('/');
        if (rangeAndTotal.Length != 2)
            return null;

        var rangeParts = rangeAndTotal[0].Split('-');
        if (rangeParts.Length != 2)
            return null;

        if (!long.TryParse(rangeParts[0], out var start) ||
            !long.TryParse(rangeParts[1], out var end))
            return null;

        long? total = null;
        if (rangeAndTotal[1] != "*" && long.TryParse(rangeAndTotal[1], out var totalValue))
            total = totalValue;

        return new ContentRangeHeader(start, end, total);
    }

    public bool IsValid => Start >= 0 && End >= Start;

    public long ChunkSize => End - Start + 1;
}