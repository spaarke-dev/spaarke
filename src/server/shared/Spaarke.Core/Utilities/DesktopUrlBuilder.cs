namespace Spaarke.Core.Utilities;

/// <summary>
/// Generates Office desktop protocol URLs from SharePoint Embedded web URLs.
/// Uses static methods per ADR-010 (DI Minimalism) as this is a pure function.
/// </summary>
public static class DesktopUrlBuilder
{
    /// <summary>
    /// Maps MIME types to Office desktop protocol schemes.
    /// Supports both modern Office Open XML formats and legacy formats.
    /// </summary>
    private static readonly Dictionary<string, string> MimeToProtocol = new(StringComparer.OrdinalIgnoreCase)
    {
        // Word documents
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "ms-word",
        ["application/msword"] = "ms-word",

        // Excel spreadsheets
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "ms-excel",
        ["application/vnd.ms-excel"] = "ms-excel",

        // PowerPoint presentations
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "ms-powerpoint",
        ["application/vnd.ms-powerpoint"] = "ms-powerpoint"
    };

    /// <summary>
    /// Generates an Office desktop protocol URL from a web URL and MIME type.
    /// </summary>
    /// <param name="webUrl">The SharePoint Embedded web URL for the document.</param>
    /// <param name="mimeType">The MIME type of the document.</param>
    /// <returns>
    /// A protocol URL in the format "ms-{app}:{url}" for supported types,
    /// or null if the inputs are invalid or the MIME type is unsupported.
    /// </returns>
    /// <remarks>
    /// Uses abbreviated protocol format: ms-word:https://...
    ///
    /// The abbreviated format (without ofe|u|) bypasses Windows Security Zone
    /// restrictions that block SharePoint Embedded /contentstorage/ URLs.
    /// Files open in Protected View, allowing users to click "Enable Editing"
    /// to switch to full edit mode.
    ///
    /// Full format (ms-word:ofe|u|{encoded-url}) is blocked by Office when the
    /// URL is in the Restricted Sites zone, which includes SPE contentstorage paths.
    /// </remarks>
    public static string? FromMime(string? webUrl, string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(webUrl) || string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        if (!MimeToProtocol.TryGetValue(mimeType, out var protocol))
        {
            return null;
        }

        // Use abbreviated format to bypass Restricted Sites zone blocking
        return $"{protocol}:{webUrl}";
    }

    /// <summary>
    /// Checks if a MIME type is supported for desktop editing.
    /// </summary>
    /// <param name="mimeType">The MIME type to check.</param>
    /// <returns>True if the MIME type can be opened in a desktop Office application.</returns>
    public static bool IsSupported(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return false;
        }

        return MimeToProtocol.ContainsKey(mimeType);
    }
}
