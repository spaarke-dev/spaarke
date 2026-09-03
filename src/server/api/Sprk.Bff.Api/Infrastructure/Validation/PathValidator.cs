namespace Sprk.Bff.Api.Infrastructure.Validation;

/// <summary>
/// Path validation helper to reject traversal, control chars, and trailing slashes.
/// </summary>
public static class PathValidator
{
    // SmallUploadMaxBytes (4 MiB) DELETED 2026-09-02 (unified-access-control-r2).
    //
    // It had ZERO code references — no validator, no endpoint, no test read it. It survived only in
    // comments that described it as a live cap, and those comments did real damage: the client
    // (Spaarke.SdapClient) refused every file >= 4 MiB citing "PathValidator.SmallUploadMaxBytes,
    // enforced at UploadSessionManager.cs:131", where the guard had already been deleted by
    // spaarkeai-compose-r8 task 015 as a stale Graph limit. So an unenforced constant became a real
    // product limit via prose alone.
    //
    // Graph's simple PUT .../content boundary is 250 MB (since Oct 2023) and SPE documents the same
    // for containers. Do NOT reintroduce a size constant here: the caller that has a genuine product
    // limit enforces its own (e.g. Compose via ComposeSaveLimits), and a second threshold in shared
    // validation is exactly the "two constants" divergence that turns a stated limit into an
    // unexplained failure.

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
