namespace Sprk.Bff.Api.Models.Ai;

// unified-access-control-r2 task 162 (owner round 10 item 1): AnalysisSaveRequest and SavedDocumentResult were
// DELETED with POST /api/ai/analysis/{analysisId}/save. SaveDocumentFormat remains: ExportOptions.AttachmentFormat
// carries it.

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
