namespace Spe.Bff.Api.Infrastructure.Validation;

/// <summary>
/// Path validation helper to reject traversal, control chars, and trailing slashes.
/// </summary>
public static class PathValidator
{
    public const int SmallUploadMaxBytes = 4 * 1024 * 1024; // 4 MiB

    public static (bool ok, string? error) ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, "path is required");
        if (path.EndsWith("/", StringComparison.Ordinal)) return (false, "path must not end with '/'");
        if (path.Contains("..")) return (false, "path must not contain '..'");
        foreach (var ch in path) if (char.IsControl(ch)) return (false, "path contains control characters");
        if (path.Length > 1024) return (false, "path too long");
        return (true, null);
    }
}