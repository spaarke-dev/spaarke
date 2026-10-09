/**
 * describeSendFailure.ts
 *
 * Turns a failed email send into the words the user sees (owner decision 2026-10-09: a failed send
 * shows a DESCRIPTIVE dialog — never nothing, never a bare "HTTP 500" or raw JSON).
 *
 * Two pure helpers, exported for unit tests and for hosts that render their own failure UI:
 *   - {@link toSendCommunicationError} — normalizes ANY thrown value from the send path into a
 *     `SendCommunicationError` (a fetch `TypeError` → status 0 / code `NETWORK`), so a host's
 *     `onError` fires for every failure, not only for server refusals.
 *   - {@link describeSendFailure} — `{ title, message, reference? }` for the dialog. The message is
 *     built from `status`/`code` plus the server's ProblemDetails `detail` (ADR-019) when that detail
 *     is a real sentence. `reference` is the correlation id, shown as "Reference: …" for support.
 */
import { SendCommunicationError } from '../../services/communicationApi';
import { isApiError, isAuthFailure, problemOf } from '../../utils/thrownFetchError';

/** What the failure dialog shows. */
export interface ISendFailureDescription {
  /** Dialog title. */
  title: string;
  /** One or more sentences: what happened, what to do, and that the draft is still there. */
  message: string;
  /** Correlation id for support (rendered as "Reference: …"). Absent when the server sent none. */
  reference?: string;
}

/** Code for a send that never reached the server (offline, DNS, CORS, blocked request). */
export const SEND_FAILURE_NETWORK_CODE = 'NETWORK';
/** Code for a client-side failure that is neither a server refusal nor a network failure. */
export const SEND_FAILURE_CLIENT_CODE = 'CLIENT_ERROR';
/**
 * Code for a send the server ACCEPTED (2xx) without returning the record id — the email most likely went
 * out but Spaarke did not confirm/record it. Resending would duplicate it.
 */
export const SEND_FAILURE_UNCONFIRMED_CODE = 'SENT_UNCONFIRMED';

/** The sentence `sendCommunication` already uses for an expired sign-in (kept identical). */
const SIGN_IN_EXPIRED = 'Your sign-in has expired. Refresh the page and sign in again, then resend.';

const DRAFT_KEPT = 'Your draft is still here — nothing was lost.';

// `sendCommunication` (communicationApi.ts) throws these plain Errors only AFTER a 2xx response, when the body
// carries no `communicationId` — the server accepted the send. Kept in step with that module's messages.
const UNCONFIRMED_SEND_MESSAGE =
  /^sendCommunication: response (did not include communicationId|body was not valid JSON)/;

// Browser wording for a fetch that never got a response: Chromium, Firefox, Safari, React Native/polyfills.
const NETWORK_MESSAGE = /failed to fetch|networkerror|load failed|network request failed|network error/i;

// Generic status titles and framework boilerplate that say nothing the status does not already say.
const GENERIC_DETAILS = new Set(
  [
    'bad request',
    'unauthorized',
    'forbidden',
    'not found',
    'conflict',
    'precondition failed',
    'payload too large',
    'request entity too large',
    'unprocessable entity',
    'too many requests',
    'internal server error',
    'bad gateway',
    'service unavailable',
    'gateway timeout',
    'an error occurred while processing your request.',
    'one or more validation errors occurred.',
    'an unexpected error occurred.',
    'an unexpected error occurred',
  ].map(s => s.toLowerCase())
);

const MAX_DETAIL_LENGTH = 300;

/**
 * Normalize anything the send path can throw into a {@link SendCommunicationError}.
 *
 * - `SendCommunicationError` → itself.
 * - `@spaarke/auth` `ApiError` / `AuthError` (defensive — `sendCommunication` already converts them).
 * - A fetch `TypeError` ("Failed to fetch") or a `TimeoutError` → status 0, code `NETWORK`.
 * - `sendCommunication`'s "2xx without a record id" errors → status 0, code `SENT_UNCONFIRMED`.
 * - Any other `Error` (an argument guard, an unreadable 2xx body) → status 0, code `CLIENT_ERROR`,
 *   `detail` = its message (a developer string — {@link describeSendFailure} does not show it).
 */
export function toSendCommunicationError(err: unknown): SendCommunicationError {
  if (err instanceof SendCommunicationError) return err;
  if (isApiError(err)) {
    if (err.status === 401) return new SendCommunicationError(401, 'HTTP_401', SIGN_IN_EXPIRED);
    return SendCommunicationError.fromProblem(err.status, problemOf(err), err.message);
  }
  if (isAuthFailure(err)) return new SendCommunicationError(401, 'HTTP_401', SIGN_IN_EXPIRED);

  const name = typeof err === 'object' && err !== null ? (err as { name?: unknown }).name : undefined;
  const message = err instanceof Error ? err.message : typeof err === 'string' ? err : '';
  if (UNCONFIRMED_SEND_MESSAGE.test(message)) {
    return new SendCommunicationError(0, SEND_FAILURE_UNCONFIRMED_CODE, message);
  }
  if ((err instanceof TypeError && NETWORK_MESSAGE.test(message)) || name === 'TimeoutError') {
    return new SendCommunicationError(0, SEND_FAILURE_NETWORK_CODE, message || 'Network request failed');
  }
  return new SendCommunicationError(0, SEND_FAILURE_CLIENT_CODE, message || 'Unknown error');
}

/**
 * The server's `detail` as a sentence the user can read, or `undefined` when it carries nothing
 * useful: empty, "HTTP 500", a bare status title, raw JSON/HTML, or one of our own developer strings.
 */
export function meaningfulDetail(detail: string | undefined): string | undefined {
  const text = (detail ?? '').replace(/\s+/g, ' ').trim();
  if (!text) return undefined;
  if (/^HTTP[ _]?\d{3}\b/i.test(text)) return undefined;
  if (/^[[{<]/.test(text)) return undefined; // raw JSON / HTML error page
  if (/^(sendCommunication|EmailComposer)\b/.test(text)) return undefined; // developer strings
  if (NETWORK_MESSAGE.test(text) && text.length < 80) return undefined; // browser wording, not the server's
  if (GENERIC_DETAILS.has(text.toLowerCase())) return undefined;
  const clipped = text.length > MAX_DETAIL_LENGTH ? `${text.slice(0, MAX_DETAIL_LENGTH - 1).trimEnd()}…` : text;
  return /[.!?…]$/.test(clipped) ? clipped : `${clipped}.`;
}

function join(...parts: Array<string | undefined>): string {
  return parts.filter((p): p is string => !!p).join(' ');
}

/**
 * Describe a failed send for the user. Accepts anything (it normalizes via
 * {@link toSendCommunicationError}); the composer is still open with the draft intact when this is
 * shown, so every message says so — except where the suggested next step (refresh, reopen) would
 * itself clear the draft, where it says to copy the text first.
 */
export function describeSendFailure(err: unknown): ISendFailureDescription {
  const e = toSendCommunicationError(err);
  const detail = meaningfulDetail(e.detail);
  const reference = e.correlationId && e.correlationId.trim() ? e.correlationId.trim() : undefined;
  let title = 'Email not sent';
  let message: string;

  if (e.code === SEND_FAILURE_UNCONFIRMED_CODE) {
    // The server accepted it: say so, and warn against a duplicate.
    title = 'Email may have been sent';
    message = join(
      "Spaarke accepted the email but didn't confirm it, so it may not appear on the record.",
      'Check your Sent Items before sending it again.',
      'Your draft is still here.'
    );
  } else if (e.status === 0 && e.code === SEND_FAILURE_NETWORK_CODE) {
    // No response at all: usually the request never left, but a connection dropped after the send looks the
    // same — so never claim it definitely did not go out.
    title = 'Email may not have been sent';
    message = join(
      "Couldn't get a response from the Spaarke server. Check your network connection.",
      "If you're not sure whether it went out, check your Sent Items before sending again.",
      DRAFT_KEPT
    );
  } else if (e.status === 0) {
    message = join(
      'Something went wrong on this page and the email was not sent. Try sending again; if it keeps happening, contact support.',
      DRAFT_KEPT
    );
  } else if (e.status === 400 || e.status === 422) {
    message = detail
      ? join(detail, 'Correct it and send again.', DRAFT_KEPT)
      : join(
          "The email couldn't be accepted as written. Check the recipients, subject and attachments, then send again.",
          DRAFT_KEPT
        );
  } else if (e.status === 401) {
    message = join(
      SIGN_IN_EXPIRED,
      'Your draft is still here, but refreshing clears it — copy anything you want to keep first.'
    );
  } else if (e.status === 403) {
    message = join(
      "You don't have permission to send this email from the selected mailbox.",
      detail,
      'Choose a different From mailbox or ask your administrator for access.',
      DRAFT_KEPT
    );
  } else if (e.status === 404) {
    message = join(
      "The record or mailbox this email depends on couldn't be found. It may have been deleted or moved.",
      detail,
      DRAFT_KEPT
    );
  } else if (e.status === 409 || e.status === 412) {
    message = join(
      'This email or its record was changed elsewhere while you were working. Close it, reopen it and try again.',
      detail,
      'Your draft is still here — copy anything you want to keep before you close it.'
    );
  } else if (e.status === 413) {
    message = join('The attachments are too large to send. Remove some attachments, then send again.', DRAFT_KEPT);
  } else if (e.status === 429) {
    message = join('Too many emails are being sent right now. Wait a moment, then send again.', DRAFT_KEPT);
  } else if (e.status >= 500) {
    message = join("The email service couldn't send this right now. Try again shortly.", detail, DRAFT_KEPT);
  } else {
    message = join("The email couldn't be sent.", detail, 'Try sending again.', DRAFT_KEPT);
  }

  return reference ? { title, message, reference } : { title, message };
}

/**
 * Describe a failed Save Draft for the user. `unavailable` = the host wired no draft persistence
 * (there is no BFF draft endpoint yet), so the button cannot work here.
 */
export function describeDraftSaveFailure(err: unknown, unavailable: boolean): ISendFailureDescription {
  const title = 'Draft not saved';
  if (unavailable) {
    return {
      title,
      message:
        "Saving a draft isn't available here yet. Your message is still open — send it when it's ready, or copy anything you want to keep before you close it.",
    };
  }
  const e = toSendCommunicationError(err);
  const detail = e.status === 0 ? undefined : meaningfulDetail(e.detail);
  const reference = e.correlationId && e.correlationId.trim() ? e.correlationId.trim() : undefined;
  const message = join(
    e.code === SEND_FAILURE_NETWORK_CODE
      ? "Couldn't reach the Spaarke server, so the draft wasn't saved. Check your network connection, then try again."
      : "The draft couldn't be saved.",
    detail,
    'Your message is still here — nothing was lost.'
  );
  return reference ? { title, message, reference } : { title, message };
}
