/**
 * Invoice files API client (ebilling-r1 task 151e): the two BFF calls the Files section makes, through `authenticatedFetch` (ADR-028).
 *
 *   - GET /api/finance/invoices/{invoiceId}/files   the invoice's files the content route would serve this caller (Invoice PDF first)
 *   - GET /api/finance/files/{fileId}/content        the bytes of one file (task 090), for Open and Download
 *
 * The page decides nothing about who may see a file: the list is the server's answer (the same decision as the content route, no second
 * copy), and the bytes come only from the content route. `authenticatedFetch` THROWS on any non-2xx (`ApiError` with the parsed
 * ProblemDetails) and on an exhausted 401 (`AuthError`); every outcome is decided in the `catch` through the `@spaarke/auth` guards.
 * Mirrors `src/solutions/ApExport/src/services/apExportApi.ts`.
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (the BFF's JSON, camelCase)
// ─────────────────────────────────────────────────────────────────────────────

export type FilePurpose = 'InvoicePdf' | 'Supporting';

/** `Ready`: Open serves it. `Pending`: its malware scan has no verdict yet (Open answers 409 until it has). */
export type ScanState = 'Ready' | 'Pending';

/** The email Document a file was copied from (present only when the caller may read it). */
export interface SourceEmail {
  documentId: string;
  name: string | null;
}

/** `InvoiceFileListItem`. */
export interface InvoiceFile {
  fileId: string;
  name: string;
  purpose: FilePurpose;
  sizeBytes: number | null;
  contentType: string | null;
  /** ISO 8601, UTC. */
  addedOn: string | null;
  scanState: ScanState;
  sourceEmail: SourceEmail | null;
}

/** The bytes of one file as the content route sent them. */
export interface FetchedFile {
  blob: Blob;
}

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/** Everything the page needs of a failed call, whatever raised it. */
export interface ApiFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  detail: string | null;
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export const CODE_EBILLING_DISABLED = 'ebilling.disabled';
export const CODE_SCAN_PENDING = 'file_scan_pending';

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** Turn whatever `authenticatedFetch` threw into an {@link ApiFailure}. Exported for the tests. */
export function toFailure(err: unknown): ApiFailure {
  if (isApiError(err)) {
    const p = problemOf(err) as Record<string, unknown> | null;
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      detail: str(p?.detail) ?? str(err.message),
      signInFailed: isAuthFailure(err),
    };
  }
  return {
    status: 0,
    errorCode: null,
    detail: err instanceof Error ? err.message : null,
    signInFailed: isAuthFailure(err),
  };
}

/** The sentence the Files section shows for a failed list. The 404 is the uniform one (no Read, or no such invoice). */
export function listFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Retry, or reload the form.';
  if (f.errorCode === CODE_EBILLING_DISABLED) return 'eBilling is not enabled in this environment.';
  if (f.status === 404) return 'The files could not be shown. You may not have access to this invoice.';
  if (f.status === 503) return 'The files are temporarily unavailable. Retry in a moment.';
  if (f.status === 0) return 'The files could not be loaded. Check your connection and retry.';
  return 'The files could not be loaded. Retry in a moment.';
}

/** The sentence shown for a failed Open or Download of one file. */
export function fileFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return 'Sign-in could not be verified for this session. Retry, or reload the form.';
  if (f.errorCode === CODE_SCAN_PENDING || f.status === 409) {
    return 'This file is still being scanned for malware. Try again in a few minutes.';
  }
  if (f.errorCode === CODE_EBILLING_DISABLED) return 'eBilling is not enabled in this environment.';
  if (f.status === 404) return 'This file is not available.';
  if (f.status === 503) return 'The file store is unavailable. Try again later.';
  return 'The file could not be opened. Try again in a moment.';
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface InvoiceFilesApi {
  listFiles(invoiceId: string): Promise<ApiResult<InvoiceFile[]>>;
  fetchFile(fileId: string): Promise<ApiResult<FetchedFile>>;
}

function trimBase(bffBaseUrl: string): string {
  return bffBaseUrl.replace(/\/+$/, '');
}

/** The list route. `bffBaseUrl` is the BFF host without `/api` (runtime config). */
export function listUrl(bffBaseUrl: string, invoiceId: string): string {
  return `${trimBase(bffBaseUrl)}/api/finance/invoices/${encodeURIComponent(invoiceId.toLowerCase())}/files`;
}

/** The content route (task 090): the only route that serves bytes. */
export function contentUrl(bffBaseUrl: string, fileId: string): string {
  return `${trimBase(bffBaseUrl)}/api/finance/files/${encodeURIComponent(fileId.toLowerCase())}/content`;
}

export function createInvoiceFilesApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): InvoiceFilesApi {
  return {
    async listFiles(invoiceId) {
      try {
        const res = await fetchFn(listUrl(bffBaseUrl, invoiceId), { method: 'GET' });
        const body = (await res.json()) as unknown;
        // A body that is not the list is a failure, never an empty list (an empty list means "no files").
        if (!Array.isArray(body)) {
          return { ok: false, failure: { status: 200, errorCode: null, detail: 'The server answered with an unexpected body.', signInFailed: false } };
        }
        return { ok: true, value: body as InvoiceFile[] };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },

    async fetchFile(fileId) {
      try {
        const res = await fetchFn(contentUrl(bffBaseUrl, fileId), { method: 'GET' });
        return { ok: true, value: { blob: await res.blob() } };
      } catch (err) {
        return { ok: false, failure: toFailure(err) };
      }
    },
  };
}
