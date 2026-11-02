/**
 * AP export API client (ebilling-r1 task 151b, EB-D-181): the three BFF calls the AP Export dialog makes, through
 * `authenticatedFetch` (ADR-028).
 *
 *   - POST /api/finance/export-batches                              (task 150) create one batch from a selection
 *   - POST /api/finance/invoices/{invoiceId}/mark-paid              (task 151a) one invoice per call, Sent to AP to Paid
 *   - GET  /api/finance/export-batches/{batchId}/files/{csv|json}    (task 151a) the stored file, byte for byte
 *
 * The page never writes Dataverse rows itself (ADR-002) and never decides who may export or pay: the server does (FR-41,
 * FR-42, EB-D-178). This module carries the calls and turns every failure into one {@link ApiFailure} shape, keeping the
 * ProblemDetails extension members the dialog shows: the per-invoice refusals of a 400 `selection_refused`, and the
 * `currentStatus` of a Mark Paid 409.
 *
 * `authenticatedFetch` THROWS on any non-2xx (`ApiError` with the parsed ProblemDetails) and on an exhausted 401
 * (`AuthError`); it never returns a failed response, so every outcome is decided in the `catch`, through the
 * `@spaarke/auth` guards. Mirrors `src/solutions/RuleReview/src/services/ruleSetApi.ts`.
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (the BFF's JSON, camelCase)
// ─────────────────────────────────────────────────────────────────────────────

export type ExportFormat = 'csv' | 'json';

export const EXPORT_FORMATS: readonly ExportFormat[] = ['csv', 'json'];

/** `ApExportFileDto`: a servable file of a Completed batch. */
export interface ExportBatchFile {
  fileId: string;
  format: string;
  name: string;
  contentType: string;
}

/** `ApExportBatchDto`: the 201 body of create-batch. */
export interface ExportBatch {
  batchId: string;
  name: string;
  /** `Completed` on a 201. */
  status: string;
  invoiceCount: number;
  /** Sum of the invoices' approved totals (task 150; one currency in R1). */
  totalAmount: number;
  formats: string[];
  files: ExportBatchFile[];
  invoiceIds: string[];
  failureReason: string | null;
}

/** One entry of the 400 `ebilling.export.selection_refused` body's `invoices[]`. */
export interface InvoiceRefusal {
  invoiceId: string;
  /** `invoice_not_approved`, `invoice_already_exported` or `invoice_not_found`. */
  reason: string;
}

/** `InvoiceMarkPaidResponse`: the 200 body of Mark Paid. */
export interface MarkPaidResponse {
  invoiceId: string;
  /** `Paid`. */
  status: string;
}

/** A downloaded batch file. */
export interface DownloadedFile {
  blob: Blob;
  fileName: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/** Everything the dialog needs of a failed call, whatever raised it. */
export interface ApiFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  /** `reasonCode` (the uniform record-not-found body carries `sdap.access.deny.record_unavailable`). */
  reasonCode: string | null;
  title: string | null;
  detail: string | null;
  /** The per-invoice refusals of a 400 `selection_refused` (empty otherwise). */
  invoices: InvoiceRefusal[];
  /** The invoice's status on a Mark Paid 409 (`InReview`, `Approved`, `SentToAp`, `Paid`, ...), else null. */
  currentStatus: string | null;
  /** The batch named by an export 409 / 500 / 503, else null. */
  batchId: string | null;
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export const CODE_EBILLING_DISABLED = 'ebilling.disabled';

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function refusals(value: unknown): InvoiceRefusal[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((e): e is Record<string, unknown> => typeof e === 'object' && e !== null)
    .map(e => ({ invoiceId: (str(e.invoiceId) ?? '').toLowerCase(), reason: str(e.reason) ?? '' }))
    .filter(r => r.invoiceId.length > 0);
}

/** Turn whatever `authenticatedFetch` threw into an {@link ApiFailure}. Exported for the tests. */
export function toFailure(err: unknown): ApiFailure {
  if (isApiError(err)) {
    const p = problemOf(err) as Record<string, unknown> | null;
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      reasonCode: str(p?.reasonCode),
      title: str(p?.title),
      detail: str(p?.detail) ?? str(err.message),
      invoices: refusals(p?.invoices),
      currentStatus: str(p?.currentStatus),
      batchId: str(p?.batchId),
      signInFailed: isAuthFailure(err),
    };
  }
  const signIn = isAuthFailure(err);
  return {
    status: 0,
    errorCode: null,
    reasonCode: null,
    title: signIn ? 'Sign-in failed' : 'The request could not be sent',
    detail: err instanceof Error ? err.message : null,
    invoices: [],
    currentStatus: null,
    batchId: null,
    signInFailed: signIn,
  };
}

/** One line for a failure: the server's detail, with its stable code. */
export function describeFailure(f: ApiFailure): string {
  const text = f.detail ?? f.title ?? (f.status ? `The server answered ${f.status}.` : 'The request failed.');
  return f.errorCode ? `${text} (${f.errorCode})` : text;
}

// ─────────────────────────────────────────────────────────────────────────────
// URLs
// ─────────────────────────────────────────────────────────────────────────────

function trimBase(bffBaseUrl: string): string {
  return bffBaseUrl.replace(/\/+$/, '');
}

/** 151a's download route for one batch file. `bffBaseUrl` is the BFF host without `/api` (runtime config). */
export function batchFileUrl(bffBaseUrl: string, batchId: string, format: ExportFormat): string {
  return `${trimBase(bffBaseUrl)}/api/finance/export-batches/${encodeURIComponent(batchId.toLowerCase())}/files/${format}`;
}

/** The name 151a gives the file (`ap-export-{batchId}.{format}`). */
export function defaultFileName(batchId: string, format: ExportFormat): string {
  return `ap-export-${batchId.toLowerCase()}.${format}`;
}

/**
 * The file name of a download: the `Content-Disposition` name when the browser exposes the header (a cross-origin call
 * sees it only when the BFF lists it in `Access-Control-Expose-Headers`), else {@link defaultFileName}, which is the name
 * the server sends.
 */
export function fileNameOf(contentDisposition: string | null | undefined, batchId: string, format: ExportFormat): string {
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition ?? '');
  if (match?.[1]) {
    const raw = match[1].trim();
    try {
      return decodeURIComponent(raw);
    } catch {
      return raw;
    }
  }
  return defaultFileName(batchId, format);
}

/** The formats a batch can be downloaded in: the formats of its servable files, in csv, json order. */
export function downloadableFormats(batch: Pick<ExportBatch, 'files'>): ExportFormat[] {
  const present = new Set(batch.files.map(f => f.format.toLowerCase()));
  return EXPORT_FORMATS.filter(f => present.has(f));
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface ApExportApi {
  /** Both formats (the route's default when `formats` is omitted). */
  createBatch(invoiceIds: string[]): Promise<ApiResult<ExportBatch>>;
  markPaid(invoiceId: string): Promise<ApiResult<MarkPaidResponse>>;
  downloadFile(batchId: string, format: ExportFormat): Promise<ApiResult<DownloadedFile>>;
}

export function createApExportApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): ApExportApi {
  const base = `${trimBase(bffBaseUrl)}/api/finance`;
  const enc = encodeURIComponent;

  async function call<T>(url: string, init?: RequestInit): Promise<ApiResult<T>> {
    try {
      const res = await fetchFn(url, init);
      return { ok: true, value: (await res.json()) as T };
    } catch (err) {
      return { ok: false, failure: toFailure(err) };
    }
  }

  return {
    createBatch: invoiceIds =>
      call<ExportBatch>(`${base}/export-batches`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ invoiceIds }),
      }),

    markPaid: invoiceId => call<MarkPaidResponse>(`${base}/invoices/${enc(invoiceId)}/mark-paid`, { method: 'POST' }),

    async downloadFile(batchId, format) {
      try {
        const res = await fetchFn(batchFileUrl(bffBaseUrl, batchId, format), { method: 'GET' });
        const fileName = fileNameOf(res.headers?.get('Content-Disposition'), batchId, format);
        return { ok: true, value: { blob: await res.blob(), fileName } };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },
  };
}
