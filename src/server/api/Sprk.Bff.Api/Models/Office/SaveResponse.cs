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
}
