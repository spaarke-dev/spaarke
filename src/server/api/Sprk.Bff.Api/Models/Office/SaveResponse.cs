using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for the save endpoint.
/// Corresponds to POST /office/save response.
/// </summary>
public record SaveResponse
{
    /// <summary>
    /// Whether the save operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Indicates if this is a duplicate request (idempotent replay).
    /// When true, the response contains the existing job information.
    /// </summary>
    public bool Duplicate { get; init; }

    /// <summary>
    /// Processing job ID for tracking async operations.
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>
    /// URL to poll for job status.
    /// </summary>
    public string? StatusUrl { get; init; }

    /// <summary>
    /// URL for SSE streaming of job updates.
    /// </summary>
    public string? StreamUrl { get; init; }

    /// <summary>
    /// Created artifact information.
    /// </summary>
    public CreatedArtifact? Artifact { get; init; }

    /// <summary>
    /// Error information if save failed.
    /// </summary>
    public SaveError? Error { get; init; }
}

/// <summary>
/// Information about the created artifact.
/// </summary>
public record CreatedArtifact
{
    /// <summary>
    /// Type of artifact created.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ArtifactType Type { get; init; }

    /// <summary>
    /// Dataverse record ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// SPE file ID (if file was uploaded).
    /// </summary>
    public string? SpeFileId { get; init; }

    /// <summary>
    /// SPE container ID.
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// URL to access the artifact.
    /// </summary>
    public string? WebUrl { get; init; }
}

/// <summary>
/// Types of artifacts created.
/// </summary>
public enum ArtifactType
{
    /// <summary>
    /// Email artifact (spe_emailartifact).
    /// </summary>
    EmailArtifact,

    /// <summary>
    /// Attachment artifact (spe_attachmentartifact).
    /// </summary>
    AttachmentArtifact,

    /// <summary>
    /// Document (sprk_document).
    /// </summary>
    Document
}

/// <summary>
/// Error information for failed save operations.
/// </summary>
public record SaveError
{
    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Additional error details.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Whether the operation can be retried.
    /// </summary>
    public bool Retryable { get; init; }

    /// <summary>
    /// Task 025 (OFFICE_020 name-collision only): the file name that collided, so the pane can restate it
    /// in the two-option choice without re-parsing <see cref="Message"/>.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Task 025 (OFFICE_020 name-collision only): the <c>sprk_document</c> that already holds
    /// <see cref="FileName"/> in the target drive, when it could be resolved. <c>null</c> when the lookup
    /// found no row (an unowned SPE item), was unavailable (fail-open, per
    /// <c>OfficeDocumentPersistence.FindDocumentIdByLocationAsync</c>), or the caller holds no <c>Read</c> on
    /// that document (stripped at the endpoint — see <see cref="ExistingDocumentName"/>).
    /// </summary>
    /// <remarks>
    /// Task 088 (UAT-5): its PRESENCE no longer means "a version retry is offered" — the pane uses it to offer
    /// <b>Open</b> (the other file, through <c>GET /api/documents/{id}/open-links</c>, which re-checks
    /// <c>Read</c>). Whether "Save as new version" is offered is <see cref="CanSaveAsVersion"/>'s alone.
    /// </remarks>
    public Guid? ExistingDocumentId { get; init; }

    /// <summary>
    /// Task 055 (OFFICE_020 name-collision only; #1005 / ISS-006): the DISPLAY NAME
    /// (<c>sprk_documentname</c> — not <see cref="FileName"/>; task 020 split them) of the document that
    /// already holds the collided name, so the pane can say WHICH document it is instead of an opaque id.
    /// </summary>
    /// <remarks>
    /// <para><b>Travels with <see cref="ExistingDocumentId"/>, never without it, and both are withheld when the
    /// caller does not hold <c>Read</c> on that document</b> — decided at the ENDPOINT (ADR-008), because a
    /// name plus what it is filed to is the description of a document and is materially more disclosive than
    /// an opaque id. The pane then shows "Keep both" only.</para>
    /// <para>Task 088 (UAT-5): no longer withheld merely because the document is filed to a DIFFERENT record
    /// than the one the caller is filing to. That rule (task 055's #1005 fix) governs the version retry, and
    /// is now carried by <see cref="CanSaveAsVersion"/> instead of by withholding the identity — so a caller
    /// who can read the other document can still open it.</para>
    /// </remarks>
    public string? ExistingDocumentName { get; init; }

    /// <summary>
    /// Task 088 (OFFICE_020 name-collision only; UAT-5): whether the pane may offer "Save as new version" of
    /// <see cref="ExistingDocumentId"/>. <c>true</c> only when the colliding document is EDITABLE content (a
    /// Document — FR-11's version save is Document-only) AND is filed to the record this save targets (task
    /// 055's #1005 rule: a version retry against a document filed elsewhere silently discards the caller's
    /// chosen record).
    /// </summary>
    /// <remarks>
    /// A pure comparison decided in <c>OfficeService.ResolveNameCollisionAsync</c>, not an authorization
    /// decision. Forced back to <c>false</c> at the endpoint whenever the caller's missing <c>Read</c> strips the
    /// identity, and emitted on the wire only alongside an id — so a caller who cannot read the other document
    /// receives exactly the payload it received before task 088.
    /// </remarks>
    public bool CanSaveAsVersion { get; init; }
}
