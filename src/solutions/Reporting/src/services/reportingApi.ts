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

import { isApiError, isAuthFailure, problemOf } from "@spaarke/auth";
import type { IProblemDetails } from "@spaarke/auth";
import { authenticatedFetch } from "./authInit";
import { getBffBaseUrl } from "../config/runtimeConfig";
import {
  REPORTING_EMBED_TOKEN_PATH,
  REPORTING_CATALOG_PATH,
  REPORTING_STATUS_PATH,
} from "../config/reportingConfig";
import type { ExportFormat, UserPrivilege, ReportCatalogItem } from "../types";

// Re-export so callers that imported from reportingApi.ts continue to work
export type { ReportCatalogItem };

// ---------------------------------------------------------------------------
// BFF path constants (supplement reportingConfig.ts)
// ---------------------------------------------------------------------------

const REPORTING_EXPORT_PATH = "/api/reporting/export";

// ---------------------------------------------------------------------------
// Response shapes
// ---------------------------------------------------------------------------

/**
 * Embed token response from GET /api/reporting/embed-token.
 *
 * unified-access-control-r2 task 166 r1: field names match the BFF's EmbedConfig (`expiry`, not `expiration`), and
 * `reportId` / `workspaceId` are the Power BI ids the BFF DERIVED from the catalog row — the client never sends them.
 */
export interface EmbedTokenResponse {
  token: string;
  expiry: string; // ISO-8601 date string
  /**
   * ISO-8601 timestamp at which the client should proactively refresh the
   * token via report.setAccessToken(). Set by the BFF at 80% of token TTL.
   */
  refreshAfter: string;
  embedUrl: string;
  reportId: string;
  workspaceId: string;
}

/**
 * The exported file from POST /api/reporting/export.
 *
 * unified-access-control-r2 task 166 r1: the BFF runs the Power BI export job to completion and streams the file in
 * the response — there is no export id and no status endpoint (the client used to poll a route that never existed).
 */
export interface ExportedFile {
  blob: Blob;
  fileName: string;
}

/** Generic typed result to avoid raw Response handling in components. */
export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: string; status?: number };

/** The failure arm of {@link ApiResult}. `error` is a sentence a user can read; `status` is the HTTP status. */
export type ApiFailure = Extract<ApiResult<never>, { ok: false }>;

// ---------------------------------------------------------------------------
// Failure mapping
// ---------------------------------------------------------------------------

const SIGN_IN_EXPIRED = "Your sign-in has expired. Refresh the page to sign in again.";

/** The 404 sentence for a call about one report (embed token, export, create, update, delete). */
const REPORT_NOT_FOUND = "The report was not found.";

/**
 * The 404 sentence for the module-level calls (catalog list, privilege/status): there the 404 is the reporting
 * module gate (sprk_ReportingModuleEnabled off — see ModuleGate), not a missing report.
 */
const REPORTING_NOT_AVAILABLE = "Reporting is not available in this environment.";

/** The sentence for an HTTP failure the server did not explain (no ProblemDetails `detail`). */
function statusSentence(status: number, notFound: string): string | null {
  if (status === 401) return SIGN_IN_EXPIRED;
  if (status === 403) return "You do not have permission to do this.";
  if (status === 404) return notFound;
  if (status === 409) return "The report was changed at the same time by someone else. Refresh and try again.";
  if (status === 429) return "Too many requests. Wait a moment and try again.";
  if (status >= 500) return "The reporting service is temporarily unavailable. Try again in a few minutes.";
  return null;
}

/** The server's `detail`, else the status sentence, else its `title`, else "Request failed (HTTP n)." */
function httpFailure(status: number, problem: IProblemDetails | null, notFound: string): ApiFailure {
  const detail = typeof problem?.detail === "string" ? problem.detail.trim() : "";
  const title = typeof problem?.title === "string" ? problem.title.trim() : "";
  const error = detail || statusSentence(status, notFound) || title || `Request failed (HTTP ${status}).`;
  return { ok: false, error, status };
}

/**
 * The result for a failure `authenticatedFetch` THREW. It never returns a non-2xx Response: it throws
 * `ApiError` (status + ProblemDetails) or, once its 401 retries are spent, `AuthError`. Never the error's
 * class name or `String(err)` — the components put `error` straight into the message the user reads.
 *
 * @param notFound  The sentence for a 404 the server did not explain — what "not found" means for this call.
 */
export function failureFromError(err: unknown, notFound: string = REPORT_NOT_FOUND): ApiFailure {
  if (isApiError(err)) return httpFailure(err.status, problemOf(err), notFound);
  if (isAuthFailure(err)) return { ok: false, error: SIGN_IN_EXPIRED, status: 401 };
  return { ok: false, error: "The reporting service could not be reached. Check your connection and try again." };
}

/** The result for a non-2xx Response from a fetch that returns failures instead of throwing. */
async function failureFromResponse(response: Response, notFound: string = REPORT_NOT_FOUND): Promise<ApiFailure> {
  let problem: IProblemDetails | null = null;
  try {
    const body: unknown = await response.json();
    if (body && typeof body === "object") problem = body as IProblemDetails;
  } catch {
    // Not JSON — the status sentence stands in for the body.
  }
  return httpFailure(response.status, problem, notFound);
}

// ---------------------------------------------------------------------------
// Embed token
// ---------------------------------------------------------------------------

/**
 * Fetch an embed token for the given report.
 *
 * @param reportId   The sprk_report catalog row GUID — the only report id the BFF accepts (task 166 r1)
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

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response);

    const data = (await response.json()) as EmbedTokenResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchEmbedToken failed", err);
    return failureFromError(err);
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

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response, REPORTING_NOT_AVAILABLE);

    const data = (await response.json()) as ReportCatalogItem[];
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchReports failed", err);
    return failureFromError(err, REPORTING_NOT_AVAILABLE);
  }
}

// ---------------------------------------------------------------------------
// Export
// ---------------------------------------------------------------------------

/**
 * Export a catalog report via POST /api/reporting/export. The BFF runs the Power BI ExportToFile job to completion
 * (it can take 30-60 seconds) and returns the file itself.
 *
 * @param reportId  The sprk_report catalog row GUID
 * @param format    "PDF" or "PPTX"
 */
export async function exportReport(
  reportId: string,
  format: ExportFormat
): Promise<ApiResult<ExportedFile>> {
  try {
    const url = `${getBffBaseUrl()}${REPORTING_EXPORT_PATH}`;
    const response = await authenticatedFetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reportId, format }),
    });

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response);

    const blob = await response.blob();
    const extension = format === "PDF" ? "pdf" : "pptx";
    return { ok: true, data: { blob, fileName: fileNameFrom(response, `report.${extension}`) } };
  } catch (err) {
    console.error("[reportingApi] exportReport failed", err);
    return failureFromError(err);
  }
}

/** The file name the BFF set in Content-Disposition, or the fallback. */
function fileNameFrom(response: Response, fallback: string): string {
  const disposition = response.headers.get("Content-Disposition") ?? "";
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      // fall through to the plain form
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  return plain ? plain[1] : fallback;
}

// ---------------------------------------------------------------------------
// Report management — create (also used by Save As: a server-side clone, task 166 r2), update
// ---------------------------------------------------------------------------

/**
 * Request body for POST /api/reporting/reports — a new report based on a catalog report the user can read.
 *
 * unified-access-control-r2 task 166 r1: the BFF derives the Power BI workspace and dataset from the SOURCE catalog
 * row (read as the user) and clones its report; the client no longer names a dataset or a workspace.
 */
export interface CreateReportRequest {
  /** Display name for the new report. */
  name: string;
  /** The sprk_report catalog row the new report is based on (its dataset is inherited). */
  sourceReportId: string;
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
 *  1. Reads the source catalog row as the user and clones its Power BI report (same workspace and dataset)
 *  2. Creates a sprk_report catalog row as the user
 *
 * @param request  Name and source catalog row for the new report
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

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response);

    const data = (await response.json()) as CreateReportResponse;
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] createReport failed", err);
    return failureFromError(err);
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

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response);

    return { ok: true, data: undefined };
  } catch (err) {
    console.error("[reportingApi] updateReport failed", err);
    return failureFromError(err);
  }
}

export async function fetchUserPrivilege(): Promise<ApiResult<{ privilege: UserPrivilege }>> {
  try {
    // Fixed 2026-09-02: this called GET /api/reporting/privilege, which has never existed —
    // a guaranteed 404, so the hook always fell back to "Viewer" and Author/Admin controls
    // were unreachable for everyone.
    //
    // No new endpoint was needed. GET /api/reporting/status ALREADY returns the resolved
    // privilege: ReportingAuthorizationFilter maps the caller's Dataverse roles
    // (sprk_ReportingAccess / sprk_ReportingAuthor / sprk_ReportingAdmin) to a
    // ReportingPrivilegeLevel, and ReportingStatusResponse carries it as `privilege`
    // ("Viewer" | "Author" | "Admin") — exactly this function's return shape.
    //
    // NOTE: ModuleGate already probes this same endpoint on mount and discards the body.
    // Collapsing the two into one call means threading privilege through the gate into a
    // context; deliberately not done here to keep this fix off a working auth gate. The
    // duplicate request is one lightweight probe.
    const url = `${getBffBaseUrl()}${REPORTING_STATUS_PATH}`;
    const response = await authenticatedFetch(url, { method: "GET" });

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response, REPORTING_NOT_AVAILABLE);

    const data = (await response.json()) as { privilege: UserPrivilege };
    return { ok: true, data };
  } catch (err) {
    console.error("[reportingApi] fetchUserPrivilege failed", err);
    return failureFromError(err, REPORTING_NOT_AVAILABLE);
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

    // A fetch that returns failures (not `@spaarke/auth`'s, which throws — see the catch).
    if (!response.ok) return failureFromResponse(response);

    return { ok: true, data: undefined };
  } catch (err) {
    console.error("[reportingApi] deleteReport failed", err);
    return failureFromError(err);
  }
}
