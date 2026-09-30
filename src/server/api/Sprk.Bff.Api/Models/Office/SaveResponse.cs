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
    /// <see cref="FileName"/> in the target drive, when it could be resolved (Document saves only — the
    /// only content type FR-11's version-save can target). <c>null</c> when the lookup found no row (an
    /// unowned SPE item) or was unavailable (fail-open, per <c>OfficeDocumentPersistence.FindDocumentIdByLocationAsync</c>).
    /// When present, the pane may retry as a version save of this document (<c>ExistingDocumentId</c> +
    /// <c>IsNewVersion: true</c>) instead of a second create.
    /// </summary>
    public Guid? ExistingDocumentId { get; init; }

    /// <summary>
    /// Task 055 (OFFICE_020 name-collision only; #1005 / ISS-006): the DISPLAY NAME
    /// (<c>sprk_documentname</c> — not <see cref="FileName"/>; task 020 split them) of the document that
    /// already holds the collided name, so the pane can say WHICH document "Save as new version" would
    /// write into instead of offering that retry against an opaque id.
    /// </summary>
    /// <remarks>
    /// <para><b>Withheld in two cases, and then <see cref="ExistingDocumentId"/> is withheld with it.</b>
    /// (1) The colliding document is filed to a record OTHER than the one the caller is filing to — a
    /// version retry there would silently discard the caller's chosen record, which is the #1005 defect.
    /// Decided in <c>OfficeService.ResolveNameCollisionAsync</c>, which is a pure comparison, not an
    /// authorization decision. (2) The caller does not hold <c>Read</c> on that document — decided at the
    /// ENDPOINT (ADR-008), because a name plus what it is filed to is the description of a document and is
    /// materially more disclosive than an opaque id.</para>
    /// <para>Both cases land the pane in its already-shipped "Keep both only" state, so neither needs new
    /// client behaviour.</para>
    /// </remarks>
    public string? ExistingDocumentName { get; init; }
}
