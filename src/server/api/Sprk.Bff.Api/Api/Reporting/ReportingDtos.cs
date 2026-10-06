using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Result returned to callers after generating a Power BI embed token.
/// Contains everything the powerbi-client-react component needs to render a report.
/// </summary>
/// <param name="Token">The embed token string (not a bearer token — PBI-specific format).</param>
/// <param name="EmbedUrl">The embed URL for the report (from the PBI REST API).</param>
/// <param name="ReportId">The Power BI report GUID (derived server-side from the catalog row — never client input).</param>
/// <param name="Expiry">UTC expiry of the embed token (typically ~1 hour from issue).</param>
/// <param name="RefreshAfter">
///   UTC time at which the client should proactively call <c>report.setAccessToken()</c> to
///   refresh the embed token. Set to 80% of the token's remaining lifetime, so the refresh
///   happens before expiry rather than at or after it.
/// </param>
/// <param name="WorkspaceId">
///   The Power BI workspace the report lives in — derived server-side from the <c>sprk_report</c> catalog row
///   (unified-access-control-r2 task 166 r1). Informational: the client never sends it back (task 166 r2 removed the
///   in-editor Save As registration it was once returned for; a copy is a server-side clone of a catalog row).
/// </param>
public record EmbedConfig(
    string Token,
    string EmbedUrl,
    Guid ReportId,
    DateTimeOffset Expiry,
    DateTimeOffset RefreshAfter,
    Guid WorkspaceId = default);

/// <summary>
/// Lightweight report descriptor returned by the Power BI create/get operations.
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
/// Export format options for Power BI report export operations. Serialized as its NAME ("PDF" / "PPTX") — the
/// Reporting client sends the name (task 166 r1: without the converter a "PDF" body failed to bind).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExportFormat>))]
public enum ExportFormat
{
    /// <summary>Export as a PDF document.</summary>
    PDF,

    /// <summary>Export as a PowerPoint presentation.</summary>
    PPTX
}

// ─────────────────────────────────────────────────────────────────────────────
// Request models (used by ReportingEndpoints.cs)
//
// unified-access-control-r2 task 166 r1 (owner round 21 item 2, option A): EVERY report id on the wire is the
// sprk_report CATALOG ROW id. The Power BI report id, workspace id and dataset id are derived server-side from that
// row, which is read AS THE CALLER — the client can no longer name a workspace, a Power BI report or a dataset.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/reporting/reports</c> — a new catalog entry derived from an existing one the caller
/// can read.
/// </summary>
/// <param name="Name">Display name for the new report.</param>
/// <param name="SourceReportId">
///   The <c>sprk_report</c> row the new report is based on. Its workspace and dataset are inherited, and the server
///   CLONES its Power BI report.
/// </param>
/// <remarks>
/// Task 166 r2 (owner round 23 item 2): the optional <c>PbiReportId</c> ("Save As" registration of a report the client
/// named) is REMOVED. Embed tokens are view-only, so the SDK's saveAs could never create a report; the property could
/// only point a new catalog row at an existing Power BI report (an alias). A client that still sends it is ignored
/// (System.Text.Json skips unknown members) and gets a clone.
/// </remarks>
public record CreateReportRequest(
    string Name,
    Guid SourceReportId);

/// <summary>Response for <c>POST /api/reporting/reports</c>: the new catalog row.</summary>
/// <param name="ReportId">The new <c>sprk_report</c> row id.</param>
/// <param name="EmbedUrl">The new report's Power BI embed URL.</param>
/// <param name="Name">The display name as stored.</param>
public record CreateReportResponse(
    Guid ReportId,
    string EmbedUrl,
    string Name);

/// <summary>
/// Request body for <c>PATCH /api/reporting/reports/{reportId}</c> — updates the catalog row (as the caller).
/// </summary>
/// <param name="Name">Updated display name (optional — null keeps the current name and only touches the row).</param>
public record UpdateReportRequest(
    string? Name);

/// <summary>
/// Request body for POST /api/reporting/export.
/// Triggers a server-side Power BI export job and streams the result.
/// Renamed to <c>ReportingExportRequest</c> to avoid a name collision with
/// <c>Microsoft.PowerBI.Api.Models.ExportReportRequest</c> used internally by
/// <see cref="ReportingEmbedService"/>.
/// </summary>
/// <param name="ReportId">The <c>sprk_report</c> catalog row id to export.</param>
/// <param name="Format">Output format: <see cref="ExportFormat.PDF"/> or <see cref="ExportFormat.PPTX"/>.</param>
/// <param name="FileName">Optional suggested file name (without extension). Defaults to the report's name.</param>
public record ReportingExportRequest(
    Guid ReportId,
    ExportFormat Format,
    string? FileName = null);

// ─────────────────────────────────────────────────────────────────────────────
// Response models (used by ReportingEndpoints.cs)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One <c>sprk_report</c> catalog entry the caller can read — the shape the Reporting client's
/// <c>ReportCatalogItem</c> expects (task 166 r1).
/// </summary>
/// <param name="Id">The <c>sprk_report</c> row id — the ONLY report id the client ever sends back.</param>
/// <param name="Name">Display name.</param>
/// <param name="EmbedUrl">The Power BI embed URL recorded on the row (empty when not recorded).</param>
/// <param name="DatasetId">The Power BI dataset id recorded on the row (informational).</param>
/// <param name="Category">Financial | Operational | Compliance | Documents | Custom.</param>
/// <param name="IsCustom">True for a user-created report.</param>
public record ReportCatalogItem(
    Guid Id,
    string Name,
    string EmbedUrl,
    string? DatasetId,
    string Category,
    bool IsCustom);

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
