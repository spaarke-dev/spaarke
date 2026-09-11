namespace Sprk.Bff.Api.Models;

/// <summary>
/// Response for /api/documents/{documentId}/open-links endpoint.
/// Contains URLs for opening documents in desktop Office applications and web.
/// </summary>
public record OpenLinksResponse(
    /// <summary>Desktop protocol URL (e.g., "ms-word:ofe|u|...") for opening in Office desktop app.</summary>
    string? DesktopUrl,
    /// <summary>SharePoint Embedded web URL for the file.</summary>
    string WebUrl,
    /// <summary>MIME type of the document.</summary>
    string MimeType,
    /// <summary>File name of the document.</summary>
    string FileName
);

/// <summary>
/// Request body for POST /api/documents/{documentId}/share-link (unified-access-control-r2 task 072).
/// </summary>
/// <remarks>
/// <para>
/// Optional in full — the route previously took no body at all and its live caller posts <c>{}</c>, so an
/// absent or empty body binds to the SAFE defaults (organization scope). That is deliberate: the
/// dangerous option must be something a caller ASKS for, never something it gets by omission.
/// </para>
/// </remarks>
public record ShareLinkRequest(
    /// <summary>
    /// <c>true</c> → mint an <c>anonymous</c> (anyone-with-the-link) URL that opens for recipients outside
    /// the tenant. <c>false</c>/omitted (default) → mint an <c>organization</c>-scoped URL that requires a
    /// tenant sign-in. Anonymous additionally requires <c>Documents:ShareLinks:AnonymousLinksEnabled</c>
    /// and is capped at the shorter anonymous lifetime.
    /// </summary>
    bool? AllowExternalRecipients = null
);

/// <summary>
/// Response for POST /api/documents/{documentId}/share-link (email-communication-solution-r5 R2 item 12;
/// expiry + scope added by unified-access-control-r2 task 072).
/// </summary>
public record ShareLinkResponse(
    /// <summary>The sharing link URL (Graph createLink WebUrl) that opens the file.</summary>
    string Url,
    /// <summary>
    /// When the link stops working (UTC, ISO-8601). Never null — task 072 removed the non-expiring path,
    /// and this is surfaced so a sender can see the lifetime rather than assume it is permanent.
    /// </summary>
    DateTimeOffset ExpiresAt,
    /// <summary>The Graph link scope actually granted — <c>"organization"</c> or <c>"anonymous"</c>.</summary>
    string Scope
);

/// <summary>
/// Request body for POST /api/documents/resolve-identity (spaarkeai-word-add-in-r1 task 012, FR-01).
/// </summary>
/// <remarks>
/// A body, not a query string, on purpose: the URL's path carries the file name, and request URLs are what
/// telemetry records.
/// </remarks>
public record ResolveDocumentIdentityRequest(
    /// <summary>The open document's absolute URL — <c>Office.context.document.url</c>, sent exactly as returned.</summary>
    string? DocumentUrl
);

/// <summary>
/// Response for POST /api/documents/resolve-identity. <see cref="Resolved"/> = <c>false</c> is a SUCCESSFUL
/// answer, not an error. For every <see cref="Reason"/> except <c>identity_conflict</c> the open document is not a
/// Spaarke document and the pane treats it as new. A 503 means "could not determine" — never treat it as new.
/// </summary>
public record DocumentIdentityResponse(
    /// <summary><c>true</c> when the URL resolved to a <c>sprk_document</c> the caller may read.</summary>
    bool Resolved,
    /// <summary>The <c>sprk_documentid</c>, bare lowercase (ADR-044). Null when not resolved.</summary>
    string? DocumentId,
    /// <summary><c>sprk_documentname</c>.</summary>
    string? DocumentName,
    /// <summary><c>sprk_filename</c>.</summary>
    string? FileName,
    /// <summary>The record the document belongs to, from its direct association slot. Null when unassociated.</summary>
    RelatedRecordIdentity? RelatedRecord,
    /// <summary>
    /// Why there is no identity, when <see cref="Resolved"/> is false: <c>not_cloud_document</c> (a local file),
    /// <c>not_resolvable</c> (Graph will not resolve the URL for this caller — no such item, or not visible to them;
    /// SharePoint does not distinguish the two), <c>not_spaarke_document</c> (the file exists but no
    /// <c>sprk_document</c> tracks it), or <c>identity_conflict</c> (a row holds this file's item id under a different
    /// drive — NOT a new document; do not offer save-as-new). Null when resolved.
    /// </summary>
    string? Reason
)
{
    public static DocumentIdentityResponse NoIdentity(string reason) => new(false, null, null, null, null, reason);
}

/// <summary>The record a resolved document is associated with (spaarkeai-word-add-in-r1 task 012).</summary>
public record RelatedRecordIdentity(
    /// <summary>Dataverse logical name, e.g. <c>sprk_matter</c>.</summary>
    string EntityType,
    /// <summary>Record id, bare lowercase (ADR-044).</summary>
    string Id,
    /// <summary>The record's primary name, when Dataverse supplied it.</summary>
    string? Name
);

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
