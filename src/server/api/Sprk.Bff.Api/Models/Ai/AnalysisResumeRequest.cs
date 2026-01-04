using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/{analysisId}/resume.
/// Resumes an existing analysis session with optional chat history.
/// </summary>
public record AnalysisResumeRequest
{
    /// <summary>
    /// Document ID associated with this analysis.
    /// References sprk_document entity.
    /// </summary>
    [Required]
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Document name for display purposes.
    /// </summary>
    public string? DocumentName { get; init; }

    /// <summary>
    /// Working document content from Dataverse (Markdown).
    /// If provided, will be restored to the in-memory session.
    /// </summary>
    public string? WorkingDocument { get; init; }

    /// <summary>
    /// Chat history from Dataverse (JSON serialized).
    /// If provided, will be deserialized and restored to the in-memory session.
    /// </summary>
    public string? ChatHistory { get; init; }

    /// <summary>
    /// Whether to include the provided chat history in the session.
    /// If false, starts a fresh session even if ChatHistory is provided.
    /// </summary>
    public bool IncludeChatHistory { get; init; } = true;
}

/// <summary>
/// Response model for POST /api/ai/analysis/{analysisId}/resume.
/// </summary>
public record AnalysisResumeResult
{
    /// <summary>
    /// The analysis ID that was resumed.
    /// </summary>
    public required Guid AnalysisId { get; init; }

    /// <summary>
    /// Whether the resume operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Number of chat messages restored from history.
    /// </summary>
    public int ChatMessagesRestored { get; init; }

    /// <summary>
    /// Whether document context was loaded.
    /// </summary>
    public bool HasDocumentContext { get; init; }

    /// <summary>
    /// Whether working document content was restored.
    /// </summary>
    public bool WorkingDocumentRestored { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? Error { get; init; }
}
