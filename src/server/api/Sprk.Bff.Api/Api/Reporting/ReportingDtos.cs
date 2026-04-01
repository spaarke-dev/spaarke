namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Result returned to callers after generating a Power BI embed token.
/// Contains everything the powerbi-client-react component needs to render a report.
/// </summary>
/// <param name="Token">The embed token string (not a bearer token — PBI-specific format).</param>
/// <param name="EmbedUrl">The embed URL for the report (from the PBI REST API).</param>
/// <param name="ReportId">The Power BI report GUID.</param>
/// <param name="Expiry">UTC expiry of the embed token (typically ~1 hour from issue).</param>
public record EmbedConfig(
    string Token,
    string EmbedUrl,
    Guid ReportId,
    DateTimeOffset Expiry);

/// <summary>
/// Lightweight report descriptor returned by list/get operations.
/// Shields callers from Microsoft.PowerBI.Api SDK types (ADR-007).
/// </summary>
/// <param name="Id">The Power BI report GUID.</param>
/// <param name="Name">Display name of the report.</param>
/// <param name="EmbedUrl">Embed URL for the report.</param>
/// <param name="DatasetId">The dataset GUID the report is bound to.</param>
public record PowerBiReport(
    Guid Id,
    string Name,
    string EmbedUrl,
    Guid DatasetId);

/// <summary>
/// Export format options for Power BI report export operations.
/// </summary>
public enum ExportFormat
{
    /// <summary>Export as a PDF document.</summary>
    PDF,

    /// <summary>Export as a PowerPoint presentation.</summary>
    PPTX
}
