using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Request model for POST /api/ai/chat/export/word.
/// Converts markdown content from a chat analysis to a DOCX file,
/// uploads it to the matter's SPE container, and returns a Word Online URL.
/// </summary>
public sealed record WordExportRequest
{
    /// <summary>
    /// Markdown content to convert to DOCX (typically AI-generated analysis output).
    /// </summary>
    [Required]
    public required string Content { get; init; }

    /// <summary>
    /// Filename for the exported document. Must end in ".docx".
    /// Example: "Analysis-2026-03-17.docx"
    /// </summary>
    [Required]
    public required string Filename { get; init; }

    /// <summary>
    /// Chat session ID that produced this content. Used for authorization
    /// (session must belong to the caller) and container resolution via ChatHostContext.
    /// </summary>
    [Required]
    public required string SessionId { get; init; }

    /// <summary>
    /// Optional metadata to include in the document (e.g. matterName, analysisDate).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
