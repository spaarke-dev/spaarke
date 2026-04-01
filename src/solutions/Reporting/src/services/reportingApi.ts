/**
 * reportingApi.ts
 * Centralized API service for all Reporting BFF calls.
 *
 * All functions use authenticatedFetch from authInit.ts to ensure the
 * Bearer token is attached. Errors are surfaced as typed results so
 * callers do not need to inspect raw HTTP responses.
 *
 * @see ADR-008 - Endpoint filters for auth; BFF returns ProblemDetails on error
 * @see ADR-009 - Redis-first caching; embed tokens are cached BFF-side
 */

import { authenticatedFetch } from "./authInit";
import { getBffBaseUrl } from "../config/runtimeConfig";
import {
  REPORTING_EMBED_TOKEN_PATH,
  REPORTING_CATALOG_PATH,
} from "../config/reportingConfig";
import type { ExportFormat, ExportStatus, UserPrivilege, ReportCatalogItem } from "../types";

// Re-export so callers that imported from reportingApi.ts continue to work
export type { ReportCatalogItem };

// ---------------------------------------------------------------------------
// BFF path constants (supplement reportingConfig.ts)
// ---------------------------------------------------------------------------

const REPORTING_EXPORT_PATH = "/api/reporting/export";

// ---------------------------------------------------------------------------
// Response shapes
// ---------------------------------------------------------------------------

/** Embed token response from GET /api/reporting/embed-token */
export interface EmbedTokenResponse {
  token: string;
  expiration: string; // ISO-8601 date string
  /**
   * ISO-8601 timestamp at which the client should proactively refresh the
   * token via report.setAccessToken(). Set by the BFF at 80% of token TTL.
   */
  refreshAfter: string;
  embedUrl: string;
  reportId: string;
  workspaceId: string;
}

/** Response from POST /api/reporting/export */
export interface ExportInitResponse {
  exportId: string;
  status: ExportStatus;
}

/** Response from GET /api/reporting/export/{exportId}/status */
export interface ExportStatusResponse {
  exportId: string;
  status: ExportStatus;
  /** Populated when status is "completed" — presigned or relative URL */
  downloadUrl?: string;
  /** File name suggestion for the download */
  fileName?: string;
}

/** Generic typed result to avoid raw Response handling in components. */
export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: string; status?: number };

// ---------------------------------------------------------------------------
// Embed token
// ---------------------------------------------------------------------------

/**
 * Fetch an embed token for the given report.
 *
 * @param reportId   The sprk_report record GUID (Dataverse)
 * @param allowEdit  When true, requests an edit-capable token (Author/Admin only)
 */
export async function fetchEmbedToken(
  reportId: string,
  allowEdit = false
): Promise<ApiResult<EmbedTokenResponse>> {
  try {
    const params = new URLSearchParams({ reportId });
    if (allowEdit) {
      params.set("allowEdit", "true");
    }
    const url = `${getBffBaseUrl()}${REPORTING_EMBED_TOKEN_PATH}?${params.toString()}`;
    const response = await authenticatedFetch(url, { method: "GET" });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as EmbedTokenResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchEmbedToken failed", err);
    return { ok: false, error: String(err) };
  }
}

// ---------------------------------------------------------------------------
// Report catalog
// ---------------------------------------------------------------------------

/**
 * Fetch the list of reports available to the current user
 * from GET /api/reporting/reports.
 */
export async function fetchReports(): Promise<ApiResult<ReportCatalogItem[]>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_CATALOG_PATH}`;
    const response = await authenticatedFetch(url, { method: "GET" });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as ReportCatalogItem[];
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchReports failed", err);
    return { ok: false, error: String(err) };
  }
}

// ---------------------------------------------------------------------------
// Export
// ---------------------------------------------------------------------------

/**
 * Initiate an export operation via POST /api/reporting/export.
 * The BFF calls Power BI ExportToFile and polls for completion.
 *
 * @param reportId  Power BI report GUID
 * @param format    "PDF" or "PPTX"
 */
export async function exportReport(
  reportId: string,
  format: ExportFormat
): Promise<ApiResult<ExportInitResponse>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_EXPORT_PATH}`;
    const response = await authenticatedFetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reportId, format }),
    });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as ExportInitResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] exportReport failed", err);
    return { ok: false, error: String(err) };
  }
}

/**
 * Poll for export status via GET /api/reporting/export/{exportId}/status.
 *
 * @param exportId  The export job ID returned by exportReport()
 */
export async function getExportStatus(
  exportId: string
): Promise<ApiResult<ExportStatusResponse>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_EXPORT_PATH}/${encodeURIComponent(exportId)}/status`;
    const response = await authenticatedFetch(url, { method: "GET" });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as ExportStatusResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] getExportStatus failed", err);
    return { ok: false, error: String(err) };
  }
}

// ---------------------------------------------------------------------------
// Report management — create, update, save-as
// ---------------------------------------------------------------------------

/**
 * Request body for POST /api/reporting/reports — create a blank report.
 * The BFF creates the report in the PBI workspace using CreateReport with
 * the provided datasetId, then creates a sprk_report Dataverse record.
 */
export interface CreateReportRequest {
  /** Display name for the new report. */
  name: string;
  /**
   * Power BI dataset (semantic model) ID to bind the new report to.
   * Must belong to the customer's PBI workspace.
   */
  datasetId: string;
}

/**
 * Response from POST /api/reporting/reports — new catalog entry.
 */
export interface CreateReportResponse {
  /** sprk_report Dataverse record ID for the new report. */
  reportId: string;
  /** Power BI embed URL for the new report. */
  embedUrl: string;
  /** Display name as confirmed by the BFF. */
  name: string;
}

/**
 * Create a blank Power BI report bound to the customer's semantic model.
 *
 * Calls POST /api/reporting/reports. The BFF:
 *  1. Calls PBI CreateReport with the given datasetId
 *  2. Creates a sprk_report Dataverse record to track the report in the catalog
 *
 * @param request  Name and datasetId for the new report
 */
export async function createReport(
  request: CreateReportRequest
): Promise<ApiResult<CreateReportResponse>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_CATALOG_PATH}`;
    const response = await authenticatedFetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as CreateReportResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] createReport failed", err);
    return { ok: false, error: String(err) };
  }
}

/**
 * Request body for PATCH /api/reporting/reports/{id} — update report metadata.
 * Used after a save operation to sync the modified date (and optional name change)
 * in the sprk_report Dataverse record.
 */
export interface UpdateReportRequest {
  /** Optional new display name (when the user renamed the report on save). */
  name?: string;
}

/**
 * Update an existing sprk_report Dataverse record after an in-place save.
 *
 * Calls PATCH /api/reporting/reports/{id} to update the modified date
 * (and optionally the name) on the sprk_report catalog entry.
 *
 * @param reportId  The sprk_report Dataverse record GUID
 * @param request   Fields to update
 */
export async function updateReport(
  reportId: string,
  request: UpdateReportRequest
): Promise<ApiResult<void>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_CATALOG_PATH}/${encodeURIComponent(reportId)}`;
    const response = await authenticatedFetch(url, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    return { ok: true, data: undefined };
  } catch (err) {
    console.error("[reportingApi] updateReport failed", err);
    return { ok: false, error: String(err) };
  }
}

/**
 * Request body for POST /api/reporting/reports (save-as variant).
 * Creates a copy of an existing report with a new name, marked as custom.
 */
export interface SaveAsReportRequest {
  /** New display name for the copied report. */
  name: string;
  /** Source report's Power BI report ID (used by the BFF to call SaveAs). */
  sourceReportId: string;
  /** Power BI workspace ID where the copy should be saved. */
  targetWorkspaceId: string;
  /** Always true for SaveAs — flags the record as user-created. */
  isCustom: boolean;
}

/**
 * Create a copy of an existing report (Save As).
 *
 * Calls POST /api/reporting/reports with isCustom=true. The BFF calls
 * report.saveAs() in PBI, then creates a new sprk_report Dataverse record
 * with is_custom=true.
 *
 * @param request  SaveAs parameters including new name and source report info
 */
export async function saveAsReport(
  request: SaveAsReportRequest
): Promise<ApiResult<CreateReportResponse>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_CATALOG_PATH}`;
    const response = await authenticatedFetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as CreateReportResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] saveAsReport failed", err);
    return { ok: false, error: String(err) };
  }
}

export async function fetchUserPrivilege(): Promise<ApiResult<{ privilege: UserPrivilege }>> {
  try {
    const url = `${getBffBaseUrl()}/api/reporting/privilege`;
    const response = await authenticatedFetch(url, { method: "GET" });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    const data = (await response.json()) as { privilege: UserPrivilege };
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchUserPrivilege failed", err);
    return { ok: false, error: String(err) };
  }
}

/**
 * Delete a report via DELETE /api/reporting/reports/{reportId}.
 * Admin-only operation — the BFF enforces this via ReportingAuthorizationFilter.
 *
 * @param reportId  The sprk_report record GUID to delete
 */
export async function deleteReport(reportId: string): Promise<ApiResult<void>> {
  try {
    const url = `${getBffBaseUrl()}/api/reporting/reports/${encodeURIComponent(reportId)}`;
    const response = await authenticatedFetch(url, { method: "DELETE" });

    if (!response.ok) {
      const body = await response.text();
      return { ok: false, error: body || response.statusText, status: response.status };
    }

    return { ok: true, data: undefined };
  } catch (err) {
    console.error("[reportingApi] deleteReport failed", err);
    return { ok: false, error: String(err) };
  }
}
