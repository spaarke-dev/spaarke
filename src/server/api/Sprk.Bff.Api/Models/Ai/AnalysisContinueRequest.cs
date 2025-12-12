using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/{analysisId}/continue.
/// Continues an existing analysis via conversational chat.
/// </summary>
public record AnalysisContinueRequest
{
    /// <summary>
    /// User's message for refining the analysis.
    /// Example: "Make this more concise and focus on liability clauses"
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "Message cannot be empty")]
    [MaxLength(10000, ErrorMessage = "Message cannot exceed 10,000 characters")]
    public string Message { get; init; } = string.Empty;
}
