using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/{analysisId}/save.
/// Saves the working document to SharePoint Embedded and creates a new Document record.
/// </summary>
public record AnalysisSaveRequest
{
    /// <summary>
    /// File name for the saved document.
    /// Example: "Agreement Summary.docx"
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "File name cannot be empty")]
    [MaxLength(255, ErrorMessage = "File name cannot exceed 255 characters")]
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Output format for the saved document.
    /// </summary>
    public SaveDocumentFormat Format { get; init; } = SaveDocumentFormat.Docx;
}

/// <summary>
/// Document format for saving analysis output.
/// </summary>
public enum SaveDocumentFormat
{
    /// <summary>Microsoft Word document.</summary>
    Docx = 0,

    /// <summary>PDF document (requires additional library).</summary>
    Pdf = 1,

    /// <summary>Markdown text file.</summary>
    Md = 2,

    /// <summary>Plain text file.</summary>
    Txt = 3
}

/// <summary>
/// Response model for POST /api/ai/analysis/{analysisId}/save.
/// </summary>
public record SavedDocumentResult
{
    /// <summary>
    /// The new Document record ID in Dataverse.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// SharePoint Embedded drive ID.
    /// </summary>
    public string DriveId { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded item ID.
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// Web URL to view the document.
    /// </summary>
    public string WebUrl { get; init; } = string.Empty;
}
