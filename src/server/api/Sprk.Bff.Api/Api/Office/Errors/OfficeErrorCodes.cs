namespace Sprk.Bff.Api.Api.Office.Errors;

/// <summary>
/// Error codes for Office integration endpoints (OFFICE_001-020).
/// Per spec.md Error Code Catalog.
/// </summary>
/// <remarks>
/// Each error code has a corresponding ProblemDetails type URI and title.
/// Clients can use these codes to provide localized error messages.
/// </remarks>
public static class OfficeErrorCodes
{
    // Validation errors (400)

    /// <summary>OFFICE_001: Invalid source type - sourceType not recognized</summary>
    public const string InvalidSourceType = "OFFICE_001";

    /// <summary>OFFICE_002: Invalid association type - associationType not recognized</summary>
    public const string InvalidAssociationType = "OFFICE_002";

    /// <summary>OFFICE_003: Association required - associationId missing</summary>
    public const string AssociationRequired = "OFFICE_003";

    /// <summary>OFFICE_004: Attachment too large - Single file > 25MB</summary>
    public const string AttachmentTooLarge = "OFFICE_004";

    /// <summary>OFFICE_005: Total size exceeded - Combined > 100MB</summary>
    public const string TotalSizeExceeded = "OFFICE_005";

    /// <summary>OFFICE_006: Blocked file type - Dangerous extension</summary>
    public const string BlockedFileType = "OFFICE_006";

    // Not found errors (404)

    /// <summary>OFFICE_007: Association target not found - Entity doesn't exist</summary>
    public const string AssociationNotFound = "OFFICE_007";

    /// <summary>OFFICE_008: Job not found - Job ID invalid or expired</summary>
    public const string JobNotFound = "OFFICE_008";

    // Authorization errors (403)

    /// <summary>OFFICE_009: Access denied - User lacks permission</summary>
    public const string AccessDenied = "OFFICE_009";

    /// <summary>OFFICE_010: Cannot create entity - User lacks create permission</summary>
    public const string CannotCreateEntity = "OFFICE_010";

    // Conflict errors (409)

    /// <summary>OFFICE_011: Document already exists - Duplicate detected</summary>
    public const string DocumentAlreadyExists = "OFFICE_011";

    // Service errors (502)

    /// <summary>OFFICE_012: SPE upload failed - SharePoint Embedded error</summary>
    public const string SpeUploadFailed = "OFFICE_012";

    /// <summary>OFFICE_013: Graph API error - Microsoft Graph failure</summary>
    public const string GraphApiError = "OFFICE_013";

    /// <summary>OFFICE_014: Dataverse error - Dataverse operation failed</summary>
    public const string DataverseError = "OFFICE_014";

    // Service unavailable (503)

    /// <summary>OFFICE_015: Processing unavailable - Workers offline</summary>
    public const string ProcessingUnavailable = "OFFICE_015";

    // FR-11 version save (spaarkeai-word-add-in-r1 task 023). Every one of these is a refusal that
    // wrote NOTHING — no sprk_document row, no SPE item, no SPE version — and none of them falls back to
    // creating a new document, which would mint exactly the duplicate row a version save exists to prevent.

    /// <summary>OFFICE_016 (404): Version target not found - document.existingDocumentId resolved to no sprk_document</summary>
    public const string VersionTargetNotFound = "OFFICE_016";

    /// <summary>OFFICE_017 (409): Version target has no file - the sprk_document carries no SPE pointers, or they no longer resolve</summary>
    public const string VersionTargetHasNoFile = "OFFICE_017";

    /// <summary>OFFICE_018 (400): Version intent mismatch - existingDocumentId sent without isNewVersion=true</summary>
    public const string VersionIntentMismatch = "OFFICE_018";

    /// <summary>OFFICE_019 (423): Version target locked - SPE refused the version write because the item is locked</summary>
    public const string VersionTargetLocked = "OFFICE_019";

    /// <summary>
    /// OFFICE_020 (409): Name collision on create - a same-named file already exists in this location.
    /// Refused BEFORE any bytes moved (task 025, spaarkeai-word-add-in-r1): the upload call passed
    /// ConflictBehavior.Fail, so Graph refused the PUT atomically and the existing item is untouched.
    /// Distinct from OFFICE_011 (DocumentAlreadyExists), which is content-hash dedup, not a filename
    /// collision — the two layers stay separate per DEDUP-AND-SAVE-BACK-IDENTITY.md §2.
    /// </summary>
    public const string NameCollision = "OFFICE_020";

    /// <summary>
    /// OFFICE_021 (400): the uploaded document carries a zip signature but cannot be read as an Office
    /// package — truncated or damaged bytes. Refused BEFORE any SPE write (FR-02, task 014,
    /// spaarkeai-word-add-in-r1), so nothing partial is stored and the save's job is marked Failed.
    /// </summary>
    /// <remarks>
    /// <para>This is a deliberate BEHAVIOUR CHANGE: before FR-02 stamping, a corrupt <c>.docx</c> was stored
    /// as-is. Refusing is the acceptance criterion ("corrupt → handled ProblemDetails, no partial bytes"), and
    /// it is strictly better than accepting bytes no reader can open.</para>
    /// <para>Distinct from every pass-through case: a PDF, an EML, an arbitrary binary or a readable non-Word
    /// zip is NOT an error — those bytes are stored untouched. Only "claims to be a package, is not one" lands
    /// here.</para>
    /// <para>⚠️ <b>Not 020.</b> <c>OFFICE_020</c> is task 025's name-collision refusal. The task-014 design
    /// note predates 025 landing and assigned 020 to this refusal; 021 is the next free code.</para>
    /// </remarks>
    public const string CorruptDocumentPackage = "OFFICE_021";

    /// <summary>
    /// Base URI for Office error types.
    /// </summary>
    public const string TypeBaseUri = "https://spaarke.com/errors/office/";

    /// <summary>
    /// Gets the error type URI for the given error code.
    /// </summary>
    /// <param name="errorCode">The error code (e.g., "OFFICE_001")</param>
    /// <returns>The full type URI for use in ProblemDetails</returns>
    public static string GetTypeUri(string errorCode)
    {
        var type = errorCode switch
        {
            InvalidSourceType => "validation-error",
            InvalidAssociationType => "validation-error",
            AssociationRequired => "validation-error",
            AttachmentTooLarge => "validation-error",
            TotalSizeExceeded => "validation-error",
            BlockedFileType => "validation-error",
            VersionIntentMismatch => "validation-error",
            CorruptDocumentPackage => "validation-error",
            AssociationNotFound => "not-found",
            JobNotFound => "not-found",
            VersionTargetNotFound => "not-found",
            AccessDenied => "forbidden",
            CannotCreateEntity => "forbidden",
            DocumentAlreadyExists => "conflict",
            VersionTargetHasNoFile => "conflict",
            NameCollision => "conflict",
            VersionTargetLocked => "locked",
            SpeUploadFailed => "service-error",
            GraphApiError => "service-error",
            DataverseError => "service-error",
            ProcessingUnavailable => "unavailable",
            _ => "error"
        };

        return $"{TypeBaseUri}{type}";
    }

    /// <summary>
    /// Gets the default title for the given error code.
    /// </summary>
    /// <param name="errorCode">The error code (e.g., "OFFICE_001")</param>
    /// <returns>A human-readable title for the error</returns>
    public static string GetTitle(string errorCode)
    {
        return errorCode switch
        {
            InvalidSourceType => "Invalid Source Type",
            InvalidAssociationType => "Invalid Association Type",
            AssociationRequired => "Association Required",
            AttachmentTooLarge => "Attachment Too Large",
            TotalSizeExceeded => "Total Size Exceeded",
            BlockedFileType => "Blocked File Type",
            AssociationNotFound => "Association Target Not Found",
            JobNotFound => "Job Not Found",
            AccessDenied => "Access Denied",
            CannotCreateEntity => "Cannot Create Entity",
            DocumentAlreadyExists => "Document Already Exists",
            SpeUploadFailed => "SPE Upload Failed",
            GraphApiError => "Graph API Error",
            DataverseError => "Dataverse Error",
            ProcessingUnavailable => "Processing Unavailable",
            VersionTargetNotFound => "Version Target Not Found",
            VersionTargetHasNoFile => "Version Target Has No File",
            VersionIntentMismatch => "Version Intent Mismatch",
            VersionTargetLocked => "Version Target Locked",
            NameCollision => "File Already Exists",
            CorruptDocumentPackage => "Document File Unreadable",
            _ => "Error"
        };
    }

    /// <summary>
    /// Gets the HTTP status code for the given error code.
    /// </summary>
    /// <param name="errorCode">The error code (e.g., "OFFICE_001")</param>
    /// <returns>The appropriate HTTP status code</returns>
    public static int GetStatusCode(string errorCode)
    {
        return errorCode switch
        {
            InvalidSourceType => 400,
            InvalidAssociationType => 400,
            AssociationRequired => 400,
            AttachmentTooLarge => 400,
            TotalSizeExceeded => 400,
            BlockedFileType => 400,
            VersionIntentMismatch => 400,
            CorruptDocumentPackage => 400,
            AssociationNotFound => 404,
            JobNotFound => 404,
            VersionTargetNotFound => 404,
            AccessDenied => 403,
            CannotCreateEntity => 403,
            DocumentAlreadyExists => 409,
            VersionTargetHasNoFile => 409,
            NameCollision => 409,
            VersionTargetLocked => 423,
            SpeUploadFailed => 502,
            GraphApiError => 502,
            DataverseError => 502,
            ProcessingUnavailable => 503,
            _ => 500
        };
    }
}
