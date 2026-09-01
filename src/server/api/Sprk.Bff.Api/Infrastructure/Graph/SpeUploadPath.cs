namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// The ONE filename sanitizer for every value that becomes a SharePoint Embedded upload path.
///
/// <para><b>The defect this exists to close.</b> <c>SpeFileStore.UploadSmallAsync</c> resolves to
/// <c>graphClient.Drives[id].Root.ItemWithPath(path).Content.PutAsync(...)</c>, and Graph creates EVERY
/// <c>'/'</c>-delimited segment of that path as a FOLDER, implicitly, as a side effect of the upload. A
/// server-side upload therefore has no "file name" concept at all — <b>the file name IS the whole path</b>.
/// On 2026-08-28 that turned the Word add-in's free-text "Document Name" box into a folder factory: a user
/// typed a DATE, <c>"New Word Document from Word Web Add In 8/24/2026"</c>, and Graph minted a folder
/// <c>…Add In 8</c> containing a folder <c>24</c> containing an extension-less file <c>2026</c>. That is the
/// origin of the mystery folders in SPE Admin — our own app-only upload, which is also why SPE Admin showed
/// no human creator and the folders looked externally created.</para>
///
/// <para><b>Why the character set is stated explicitly rather than inherited from the host OS.</b> The BFF
/// publishes <c>linux-x64</c>, where <c>Path.GetInvalidFileNameChars()</c> returns only
/// <c>{'\0', '/'}</c>. Two consequences, both load-bearing:</para>
/// <list type="bullet">
///   <item><description><c>'/'</c> IS stripped on Linux — the character that matters most, per above.</description></item>
///   <item><description><c>'\\'</c>, <c>'&lt;'</c>, <c>'&gt;'</c>, <c>':'</c>, <c>'"'</c>, <c>'|'</c>,
///     <c>'?'</c>, <c>'*'</c> are NOT stripped on Linux, so they survive into names SharePoint rejects or
///     that break on Windows clients (Outlook opens the .eml files this produces). <c>'\\'</c> also reads as
///     a separator to some SharePoint surfaces. A test asserting those chars are stripped therefore PASSES on
///     a Windows dev box and would have been wrong in production — exactly what
///     <c>GraphMessageToEmlConverterTests.ConvertToEml_FileName_ContainsSanitizedSubjectAndDate</c> was doing
///     before this consolidation.</description></item>
/// </list>
///
/// <para><b>Why here and not in the facade.</b> Sanitizing inside <c>SpeFileStore</c> would also rewrite
/// <c>PUT /api/obo/containers/{id}/files/{*path}</c>, whose <c>{*path}</c> is a wildcard route where the
/// caller may legitimately address a sub-path inside a container it already holds. That capability is
/// deliberate, so the facade stays path-transparent and sanitization happens at each site that knows its
/// value is a FILE NAME. <c>tests/Spaarke.ArchTests/SpeUploadPathIsFlatGuardTests.cs</c> is what stops a new
/// site from forgetting.</para>
///
/// <para><b>Why here and not in <c>Services/Office/OfficeEmailEnricher</c>, where the implementation used to
/// live.</b> Callers span <c>Api/</c>, <c>Services/Communication/</c>, <c>Services/Workspace/</c>,
/// <c>Services/Ai/</c>, <c>Services/Compose/</c> and <c>Workers/</c>. Reaching into <c>Services/Office</c>
/// from all of those is the inverted dependency that made four separate authors write their own private copy
/// instead (root CLAUDE.md §11 — one component that works, not five that overlap). <c>Infrastructure/Graph</c>
/// is where <c>SpeFileStore</c> lives, which is the thing whose contract this encodes.</para>
/// </summary>
public static class SpeUploadPath
{
    /// <summary>The default replacement when sanitizing removes everything.</summary>
    public const string DefaultFallback = "untitled";

    /// <summary>
    /// Windows reserves a stricter superset of invalid filename characters than Linux; this is that
    /// superset, unioned with whatever the host OS reports so nothing is lost on a Windows runner either.
    /// </summary>
    private static readonly HashSet<char> InvalidFileNameChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    /// <summary>
    /// Sanitizes a value for use as a filename AND, because a filename is the whole SPE upload path, as a
    /// single safe path segment: every path separator is removed, so the result can never cause Graph to
    /// implicitly create a folder.
    /// </summary>
    /// <param name="name">The raw name. May be null/blank — the fallback is returned.</param>
    /// <param name="maxLength">
    /// Optional cap on the sanitized result, applied AFTER stripping. <c>0</c> (the default) means no cap.
    /// The .eml/transcript generators pass 50 because they compose <c>{subject}_{timestamp}.eml</c> and a
    /// long subject would otherwise dominate the name.
    /// </param>
    /// <param name="fallback">
    /// Returned when the input is blank or sanitizes to nothing. Never return an empty string here: an empty
    /// upload path makes the Graph PUT target the drive ROOT itself rather than an item in it.
    /// </param>
    public static string SanitizeFileName(string? name, int maxLength = 0, string fallback = DefaultFallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var sanitized = new string(name.Where(c => !InvalidFileNameChars.Contains(c)).ToArray()).Trim();

        if (maxLength > 0 && sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    /// <summary>
    /// True when <paramref name="segment"/> is usable as ONE segment of an SPE path: non-blank, not a
    /// relative-navigation token, and free of every character that is invalid in a name.
    /// </summary>
    /// <remarks>
    /// <para>The sibling of <see cref="SanitizeFileName"/> for the one surface that cannot simply strip:
    /// <c>PUT /api/obo/containers/{id}/files/{*path}</c>, whose <c>{*path}</c> is a wildcard route where the
    /// caller MAY legitimately address a location inside a container it already holds. Silently rewriting a
    /// caller's path would change where their bytes land without telling them, so that route REJECTS
    /// (400 ValidationProblem) instead of sanitizing — which is why this returns a verdict rather than a
    /// cleaned string.</para>
    ///
    /// <para><c>'/'</c> is excluded from the check because it is the SEPARATOR the caller splits on; the
    /// caller must split first and validate each segment. That split is what preserves the sub-path
    /// capability while still closing unsafe segments.</para>
    /// </remarks>
    public static bool IsSafeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        // "." and ".." are relative navigation, never names. Graph's behaviour for either is not something
        // to find out in production.
        if (segment is "." or "..")
        {
            return false;
        }

        foreach (var ch in segment)
        {
            if (ch != '/' && InvalidFileNameChars.Contains(ch))
            {
                return false;
            }
        }

        return true;
    }
}
