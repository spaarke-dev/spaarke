/**
 * Shared builders for the Invoice Files page's tests (ebilling-r1 task 151e).
 */

import { ApiError, AuthError } from '@spaarke/auth';
import type { ApiResult, FetchedFile, InvoiceFile } from '../services/invoiceFilesApi';
import { toFailure } from '../services/invoiceFilesApi';

export const INVOICE_ID = 'a23f043c-e6a1-5754-50d1-8e4f2aa1a2f9';
export const PDF_ID = '11111111-0000-4000-8000-000000000001';
export const SUPPORT_ID = '22222222-0000-4000-8000-000000000002';
export const PENDING_ID = '33333333-0000-4000-8000-000000000003';
export const DOC_ID = 'dddddddd-0000-4000-8000-0000000000d1';

export const ok = <T>(value: T): ApiResult<T> => ({ ok: true, value });

/** An `ApiError` as `authenticatedFetch` throws it, with a ProblemDetails body. */
export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
}

export function failed<T>(err: unknown): ApiResult<T> {
  return { ok: false, failure: toFailure(err) };
}

export function file(overrides: Partial<InvoiceFile> = {}): InvoiceFile {
  return {
    fileId: PDF_ID,
    name: 'INV-792345.pdf',
    purpose: 'InvoicePdf',
    sizeBytes: 123_456,
    contentType: 'application/pdf',
    addedOn: '2026-11-01T09:00:00Z',
    scanState: 'Ready',
    sourceEmail: { documentId: DOC_ID, name: 'Invoice 792345 from Acme.eml' },
    ...overrides,
  };
}

export function fetched(): FetchedFile {
  return { blob: new Blob(['%PDF-1.7'], { type: 'application/pdf' }) };
}

export { ApiError, AuthError };
