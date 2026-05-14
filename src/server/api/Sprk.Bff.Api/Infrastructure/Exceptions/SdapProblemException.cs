namespace Sprk.Bff.Api.Infrastructure.Exceptions;

/// <summary>
/// Exception that maps to RFC 7807 Problem Details with stable error codes.
/// Per senior dev spec (FILE-VIEWER-V5-FIX.md section 8).
///
/// Stable error codes:
/// - invalid_id: Parameter is not a GUID
/// - document_not_found: No Dataverse row for GUID
/// - no_file_attached: Dataverse row exists, sprk_hasfile=false (no file uploaded yet)
/// - mapping_missing_drive: Dataverse row has sprk_hasfile=true but DriveId mapping incomplete (partial/in-flight upload)
/// - mapping_missing_item: Dataverse row has sprk_hasfile=true but ItemId mapping incomplete
/// - invalid_drive_id: DriveId field is populated but malformed (does not start with "b!")
/// - invalid_item_id: ItemId field is populated but too short (&lt;20 chars)
/// - storage_not_found: Graph 404, file removed in storage
/// - throttled_retry: Transient Graph throttling with backoff
/// </summary>
public sealed class SdapProblemException : Exception
{
    /// <summary>
    /// Stable error code for client-side handling
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Error title (short description)
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Detailed error message (optional)
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// HTTP status code (400, 404, 409, 500, etc.)
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Additional extensions (optional)
    /// </summary>
    public Dictionary<string, object>? Extensions { get; }

    public SdapProblemException(
        string code,
        string title,
        string? detail = null,
        int statusCode = 400,
        Dictionary<string, object>? extensions = null)
        : base($"{code}: {title}")
    {
        Code = code;
        Title = title;
        Detail = detail;
        StatusCode = statusCode;
        Extensions = extensions;
    }
}
