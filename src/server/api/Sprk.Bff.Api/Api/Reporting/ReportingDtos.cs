namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Result returned to callers after generating a Power BI embed token.
/// Contains everything the powerbi-client-react component needs to render a report.
/// </summary>
/// <param name="Token">The embed token string (not a bearer token — PBI-specific format).</param>
/// <param name="EmbedUrl">The embed URL for the report (from the PBI REST API).</param>
/// <param name="ReportId">The Power BI report GUID.</param>
/// <param name="Expiry">UTC expiry of the embed token (typically ~1 hour from issue).</param>
/// <param name="RefreshAfter">
///   UTC time at which the client should proactively call <c>report.setAccessToken()</c> to
///   refresh the embed token. Set to 80% of the token's remaining lifetime, so the refresh
///   happens before expiry rather than at or after it.
/// </param>
public record EmbedConfig(
    string Token,
    string EmbedUrl,
    Guid ReportId,
    DateTimeOffset Expiry,
    DateTimeOffset RefreshAfter);

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

// ─────────────────────────────────────────────────────────────────────────────
// Request models (used by ReportingEndpoints.cs)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/reporting/reports.
/// Creates a new report in the specified Power BI workspace by cloning a template report.
/// </summary>
/// <param name="WorkspaceId">Target Power BI workspace GUID.</param>
/// <param name="Name">Display name for the new report.</param>
/// <param name="DatasetId">Dataset GUID the cloned report will be bound to.</param>
/// <param name="TemplateReportId">Source report GUID to clone the canvas from.</param>
public record CreateReportRequest(
    Guid WorkspaceId,
    string Name,
    Guid DatasetId,
    Guid TemplateReportId);

/// <summary>
/// Request body for PUT /api/reporting/reports/{reportId}.
/// Updates catalog metadata for an existing report entry.
/// </summary>
/// <param name="WorkspaceId">Power BI workspace GUID containing the report.</param>
/// <param name="Name">Updated display name for the report (optional — pass null to leave unchanged).</param>
public record UpdateReportRequest(
    Guid WorkspaceId,
    string? Name);

/// <summary>
/// Request body for POST /api/reporting/export.
/// Triggers a server-side Power BI export job and streams the result.
/// Renamed to <c>ReportingExportRequest</c> to avoid a name collision with
/// <c>Microsoft.PowerBI.Api.Models.ExportReportRequest</c> used internally by
/// <see cref="ReportingEmbedService"/>.
/// </summary>
/// <param name="WorkspaceId">Power BI workspace GUID containing the report.</param>
/// <param name="ReportId">Report GUID to export.</param>
/// <param name="Format">Output format: <see cref="ExportFormat.PDF"/> or <see cref="ExportFormat.PPTX"/>.</param>
/// <param name="FileName">Optional suggested file name (without extension). Defaults to report-{reportId}.</param>
public record ReportingExportRequest(
    Guid WorkspaceId,
    Guid ReportId,
    ExportFormat Format,
    string? FileName = null);

// ─────────────────────────────────────────────────────────────────────────────
// Response models (used by ReportingEndpoints.cs)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Response for GET /api/reporting/status.
/// Used by the ModuleGate UI component to determine whether to render the Reporting tab.
/// </summary>
/// <param name="Enabled">Always true when this endpoint returns 200 (the auth filter enforces the gate).</param>
/// <param name="Version">API version string for the Reporting module.</param>
/// <param name="Privilege">The authenticated user's privilege level: Viewer, Author, or Admin.</param>
public record ReportingStatusResponse(
    bool Enabled,
    string Version,
    string Privilege);
