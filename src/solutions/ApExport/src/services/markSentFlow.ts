/**
 * Mark Sent to AP: what the dialog shows for each answer of `POST /api/finance/export-batches/{batchId}/mark-sent-to-ap`
 * (task 151c-1a; EB-D-182).
 *
 * The call records that the batch's files reached AP another way (a portal, a download and a hand delivery). It sets the
 * Sent to AP Date once and never overwrites it: an already-set date comes back unchanged (`alreadySet`), so the call is
 * safe to repeat and "Try again" is offered for the transient failures.
 */

import type { ApiFailure, ApiResult, MarkSentResponse } from './apExportApi';
import { describeFailure } from './apExportApi';
import { formatDateTime } from './batch';
import { BATCH_UNAVAILABLE_TEXT, CODE_EBILLING_DISABLED, CODE_UNAVAILABLE } from './emailFlow';

export const CODE_BATCH_NOT_COMPLETED = 'ebilling.export.batch_not_completed';

export type MarkSentOutcome =
  | { kind: 'marked'; title: string; message: string; sentToApDate: string; alreadySet: boolean }
  | { kind: 'failed'; message: string; retryable: boolean; code: string | null };

export function markSentFailure(f: ApiFailure): Extract<MarkSentOutcome, { kind: 'failed' }> {
  const code = f.errorCode;
  const failed = (message: string, retryable: boolean) => ({ kind: 'failed' as const, message, retryable, code });
  if (f.signInFailed || f.status === 401) {
    return failed('Sign-in could not be verified, so nothing was changed. Close this dialog, reload the page and try again.', false);
  }
  if (f.status === 404) return failed(BATCH_UNAVAILABLE_TEXT, false);
  if (f.status === 0) return failed('No answer arrived, so it is not known whether the date was recorded. Try again; repeating it is safe.', true);
  if (code === CODE_BATCH_NOT_COMPLETED) {
    return failed('This batch is not Completed, so it cannot be marked Sent to AP. Nothing was changed.', false);
  }
  if (code === CODE_UNAVAILABLE) {
    return failed('A service this step needs is temporarily unavailable. Nothing was changed. Try again in a moment.', true);
  }
  if (code === CODE_EBILLING_DISABLED) {
    return failed('eBilling is turned off in this environment. Nothing was changed; ask an administrator.', false);
  }
  return failed(`${describeFailure(f)} Nothing was changed.`, false);
}

export function markSentOutcome(result: ApiResult<MarkSentResponse>, locale?: string, timeZone?: string): MarkSentOutcome {
  if (!result.ok) return markSentFailure(result.failure);
  const date = formatDateTime(result.value.sentToApDate, locale, timeZone);
  return result.value.alreadySet
    ? {
        kind: 'marked',
        title: 'Already marked Sent to AP',
        message: 'This batch already had a Sent to AP Date. It is unchanged.',
        sentToApDate: date,
        alreadySet: true,
      }
    : {
        kind: 'marked',
        title: 'Marked Sent to AP',
        message: 'The Sent to AP Date was recorded.',
        sentToApDate: date,
        alreadySet: false,
      };
}
