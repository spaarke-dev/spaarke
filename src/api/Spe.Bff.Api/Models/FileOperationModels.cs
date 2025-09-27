namespace Spe.Bff.Api.Models;

public record UpdateFileRequest(
    string? Name = null,
    string? ParentReferenceId = null
);

public record RangeHeader(
    long Start,
    long End
)
{
    public static RangeHeader? Parse(string? rangeHeader)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader))
            return null;

        // Expected format: "bytes=0-1023" or "bytes=0-"
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return null;

        var rangeValue = rangeHeader["bytes=".Length..];
        var parts = rangeValue.Split('-');

        if (parts.Length != 2)
            return null;

        if (!long.TryParse(parts[0], out var start) || start < 0)
            return null;

        // Handle "bytes=0-" (open-ended range)
        if (string.IsNullOrEmpty(parts[1]))
            return new RangeHeader(start, long.MaxValue);

        if (!long.TryParse(parts[1], out var end) || end < start)
            return null;

        return new RangeHeader(start, end);
    }

    public bool IsValid => Start >= 0 && End >= Start;

    public long RequestedLength => End == long.MaxValue ? long.MaxValue : End - Start + 1;
}

public record FileContentResponse(
    Stream Content,
    long ContentLength,
    string ContentType,
    string? ETag,
    long? RangeStart = null,
    long? RangeEnd = null,
    long? TotalSize = null
)
{
    public bool IsRangeRequest => RangeStart.HasValue && RangeEnd.HasValue;

    public string? ContentRangeHeader => IsRangeRequest
        ? $"bytes {RangeStart}-{RangeEnd}/{(TotalSize?.ToString() ?? "*")}"
        : null;
}

public static class FileOperationExtensions
{
    public static bool IsValidFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Check for invalid characters
        var invalidChars = new[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
        if (name.Any(c => invalidChars.Contains(c) || char.IsControl(c)))
            return false;

        // Check reserved names
        var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (reservedNames.Contains(nameWithoutExtension))
            return false;

        // Check length
        if (name.Length > 255)
            return false;

        // Cannot end with space or period
        if (name.EndsWith(' ') || name.EndsWith('.'))
            return false;

        return true;
    }

    public static bool IsValidItemId(string? itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && itemId.Length <= 128;
    }
}