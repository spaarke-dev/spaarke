using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/{analysisId}/export.
/// Exports analysis output to various destinations.
/// </summary>
public record AnalysisExportRequest
{
    /// <summary>
    /// Export destination format.
    /// </summary>
    [Required]
    public ExportFormat Format { get; init; }

    /// <summary>
    /// Format-specific export options.
    /// </summary>
    public ExportOptions? Options { get; init; }
}

/// <summary>
/// Export destination format.
/// </summary>
public enum ExportFormat
{
    /// <summary>Create email activity in Dataverse.</summary>
    Email = 0,

    /// <summary>Post to Teams channel (Phase 2).</summary>
    Teams = 1,

    /// <summary>Export as PDF file.</summary>
    Pdf = 2,

    /// <summary>Export as Word document.</summary>
    Docx = 3
}

/// <summary>
/// Export options for various formats.
/// </summary>
public record ExportOptions
{
    /// <summary>
    /// Email recipients (for Email format).
    /// </summary>
    public string[]? EmailTo { get; init; }

    /// <summary>
    /// Email CC recipients (for Email format).
    /// </summary>
    public string[]? EmailCc { get; init; }

    /// <summary>
    /// Email subject line (for Email format).
    /// </summary>
    public string? EmailSubject { get; init; }

    /// <summary>
    /// Include link to source document in export.
    /// </summary>
    public bool IncludeSourceLink { get; init; } = true;

    /// <summary>
    /// Include analysis output as file attachment.
    /// </summary>
    public bool IncludeAnalysisFile { get; init; } = true;

    /// <summary>
    /// Format for file attachment (when IncludeAnalysisFile is true).
    /// </summary>
    public SaveDocumentFormat AttachmentFormat { get; init; } = SaveDocumentFormat.Pdf;
}

/// <summary>
/// Response model for POST /api/ai/analysis/{analysisId}/export.
/// </summary>
public record ExportResult
{
    /// <summary>
    /// Type of export performed.
    /// </summary>
    public ExportFormat ExportType { get; init; }

    /// <summary>
    /// Whether the export succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Export-specific details.
    /// </summary>
    public ExportDetails? Details { get; init; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Export-specific details.
/// </summary>
public record ExportDetails
{
    /// <summary>
    /// Email activity ID (for Email exports).
    /// </summary>
    public Guid? EmailActivityId { get; init; }

    /// <summary>
    /// Email metadata record ID (for Email exports).
    /// </summary>
    public Guid? EmailMetadataId { get; init; }

    /// <summary>
    /// Export status.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// URL to open the exported item (e.g., email in MDA).
    /// </summary>
    public string? OpenUrl { get; init; }

    /// <summary>
    /// Document ID (for PDF/DOCX exports).
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// Web URL for the exported file.
    /// </summary>
    public string? WebUrl { get; init; }
}
