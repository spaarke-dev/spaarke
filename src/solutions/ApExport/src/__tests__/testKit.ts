/**
 * Shared builders for the AP Export page's logic tests (ebilling-r1 task 151b).
 */

import { ApiError, AuthError } from '@spaarke/auth';
import type { ApiResult, ExportBatch } from '../services/apExportApi';
import { toFailure } from '../services/apExportApi';

export const INV_A = 'aaaaaaaa-0000-4000-8000-000000000001';
export const INV_B = 'bbbbbbbb-0000-4000-8000-000000000002';
export const INV_C = 'cccccccc-0000-4000-8000-000000000003';
export const INV_D = 'dddddddd-0000-4000-8000-000000000004';
export const BATCH_ID = 'eeeeeeee-0000-4000-8000-000000000005';

export const ok = <T>(value: T): ApiResult<T> => ({ ok: true, value });

/** An `ApiError` as `authenticatedFetch` throws it, with a ProblemDetails body. */
export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
}

/** The result a client method returns for a thrown error. */
export function failed<T>(err: unknown): ApiResult<T> {
  return { ok: false, failure: toFailure(err) };
}

export function batch(overrides: Partial<ExportBatch> = {}): ExportBatch {
  return {
    batchId: BATCH_ID,
    name: 'AP-2026-11-01-001',
    status: 'Completed',
    invoiceCount: 2,
    totalAmount: 1500.5,
    formats: ['csv', 'json'],
    files: [
      { fileId: 'f1', format: 'csv', name: 'ap-export.csv', contentType: 'text/csv' },
      { fileId: 'f2', format: 'json', name: 'ap-export.json', contentType: 'application/json' },
    ],
    invoiceIds: [INV_A, INV_B],
    failureReason: null,
    ...overrides,
  };
}

export { ApiError, AuthError };
