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

/**
 * Placeholder stubs for report management operations.
 * Implemented in later tasks (022, 024) — defined here for completeness
 * so consumers can import from a single service module.
 */

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
