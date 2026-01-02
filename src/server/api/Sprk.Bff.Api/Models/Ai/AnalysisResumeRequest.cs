namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/{analysisId}/resume.
/// Resumes an existing analysis session with optional chat history.
/// </summary>
public record AnalysisResumeRequest
{
    /// <summary>
    /// The document ID associated with the analysis.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Optional document name for display purposes.
    /// </summary>
    public string? DocumentName { get; init; }

    /// <summary>
    /// Whether to include previous chat history in the resumed session.
    /// </summary>
    public bool IncludeChatHistory { get; init; }

    /// <summary>
    /// Serialized chat history JSON if IncludeChatHistory is true.
    /// </summary>
    public string? ChatHistory { get; init; }

    /// <summary>
    /// Working document content (intermediate analysis output).
    /// </summary>
    public string? WorkingDocument { get; init; }
}
