using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

// unified-access-control-r2 task 162 (owner round 10 item 1): AnalysisExportRequest, ExportResult and
// ExportDetails were DELETED with POST /api/ai/analysis/{analysisId}/export. ExportFormat and ExportOptions
// remain: IExportService / ExportContext (DocxExportService, used by ChatWordExportEndpoints) carry them.

/// <summary>
/// Export destination format.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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
