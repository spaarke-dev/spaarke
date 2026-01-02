namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result model for POST /api/ai/analysis/{analysisId}/resume.
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
