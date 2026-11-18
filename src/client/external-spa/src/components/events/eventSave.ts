/**
 * eventSave — what the Edit and Change status dialogs say when `PATCH /events/{id}` does not answer 204
 * (spaarke-SPA-external-access-platform-r4 task 015, FR-36).
 *
 * The BFF answers a refused PATCH with ProblemDetails (a stable `errorCode` per field on a 400). Its `detail` is written for the
 * user and is shown as it is (`problemMessage`), EXCEPT where the answer changes what the SPA should do next:
 *   - 403: the right changed since the record loaded (`sdap.access.deny.event_update`): one short line, and the record is
 *     re-read so its actions follow the new right. A server detail is not echoed (a 403 is deliberately uniform).
 *   - 404: the event is gone or no longer readable: nothing to edit. The dialog STAYS OPEN showing "This event is no longer
 *     available." (closing it would hide the reason), and the record is re-read, so it shows its not-found state behind it.
 *   - 503: an unknown right or a Dataverse fault, nothing was written: retryable.
 *   - everything else (400): the server's own message, in the dialog, for the user to correct.
 * No refusal closes the dialog: the line stays until the user presses Cancel (Esc is not handled locally; the draft is kept).
 * The multi-parent assignee refusal (400 `assignee_not_allowed`: a person who may not see ONE of the event's other parents) is
 * shown as its message: the SPA knows only the parent the event was opened from, so it cannot tell beforehand (accepted limit).
 */
import { ApiError } from '../../types';
import { problemMessage } from '../../auth/bff-client';

export interface EventSaveFailure {
  /** The one line shown in the dialog. */
  message: string;
  /** The record should be re-read (the right or the event changed under the caller). */
  rereadRecord: boolean;
}

export const EVENT_SAVE_TEXT = {
  denied: 'You can no longer edit this event.',
  unavailable: 'The event could not be saved just now, and nothing was changed. Please try again in a moment.',
  fallback: 'The event could not be saved. Check your connection and try again.',
} as const;

export function eventSaveFailureOf(err: unknown): EventSaveFailure {
  const status = err instanceof ApiError ? err.statusCode : null;
  if (status === 403) return { message: EVENT_SAVE_TEXT.denied, rereadRecord: true };
  if (status === 404) return { message: 'This event is no longer available.', rereadRecord: true };
  if (status === 503) return { message: EVENT_SAVE_TEXT.unavailable, rereadRecord: false };
  return { message: problemMessage(err, EVENT_SAVE_TEXT.fallback), rereadRecord: false };
}
