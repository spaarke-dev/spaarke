/**
 * Email to AP: what the dialog shows for each answer of `POST /api/finance/export-batches/{batchId}/email`
 * (task 151c-1a; EB-D-181 to EB-D-184).
 *
 * Pure functions and one small state holder, so the tests can pin every state without rendering:
 *   - {@link validateEmailInput}: the CC and message rules, checked on the client to save a round trip; the server
 *     stays the authority and its 400 is mapped too;
 *   - {@link emailOutcome}: a 200 as one of three "sent" states, any failure as one plain sentence plus what to do next;
 *   - {@link createEmailSender}: the one place a send is started. It refuses a second call while one is in flight (a
 *     double click) and refuses any call once the email went or a failure said "do not try again": the user must not be
 *     able to send the same batch twice from the dialog.
 *
 * Every failure sentence says whether to try again or to use Download plus Mark Sent to AP. The refusals that cannot
 * change on a second attempt (409 `ap_email_recipient_monitored`, `email_owner_refused`, `email_send_refused`,
 * `attachments_too_large`, `file_not_available`, `ap_email_not_configured`) are not retryable; 503 `email_send_failed`
 * and `unavailable` are.
 */

import type { ApExportApi, ApiFailure, ApiResult, EmailRequest, EmailResponse } from './apExportApi';
import { describeFailure } from './apExportApi';
import { formatDateTime } from './batch';
import { createSingleFlight } from './singleFlight';

export const CODE_INVALID_REQUEST = 'ebilling.export.invalid_request';
export const CODE_CALLER_UNRESOLVED = 'ebilling.export.caller_unresolved';
export const CODE_AP_EMAIL_NOT_CONFIGURED = 'ebilling.export.ap_email_not_configured';
export const CODE_FILE_NOT_AVAILABLE = 'ebilling.export.file_not_available';
export const CODE_RECIPIENT_MONITORED = 'ebilling.export.ap_email_recipient_monitored';
export const CODE_ATTACHMENTS_TOO_LARGE = 'ebilling.export.attachments_too_large';
export const CODE_EMAIL_OWNER_REFUSED = 'ebilling.export.email_owner_refused';
export const CODE_EMAIL_SEND_REFUSED = 'ebilling.export.email_send_refused';
export const CODE_EMAIL_SEND_FAILED = 'ebilling.export.email_send_failed';
export const CODE_UNAVAILABLE = 'ebilling.export.unavailable';
export const CODE_EBILLING_DISABLED = 'ebilling.disabled';

export const MAX_CC = 10;
export const MAX_MESSAGE = 2000;
/** The longest address an email column or header can hold. */
export const MAX_ADDRESS = 320;

// ─────────────────────────────────────────────────────────────────────────────
// Client-side validation (mirrors the server's rules; the server decides)
// ─────────────────────────────────────────────────────────────────────────────

/** A bare address: one `@`, no space, no display name (`Name <a@b>`), no brackets, quotes or separators. */
const ADDRESS = /^[^\s@<>()[\]\\,;:"]+@[^\s@<>()[\]\\,;:"]+$/;

/** The CC entries typed in one box: separated by a comma, a semicolon or a new line; blank entries are ignored. */
export function splitCc(text: string): string[] {
  return text
    .split(/[,;\n\r]+/)
    .map(part => part.trim())
    .filter(part => part.length > 0);
}

export type EmailInputCheck =
  | { ok: true; request: EmailRequest }
  | { ok: false; ccError: string | null; messageError: string | null };

/**
 * Check the CC box and the message. Rules (task 151c-1a): at most {@link MAX_CC} CC entries; each a bare, valid address
 * (no display name, no two addresses in one entry); the sent list is de-duplicated case-insensitively; the message is
 * at most {@link MAX_MESSAGE} characters.
 */
export function validateEmailInput(ccText: string, message: string): EmailInputCheck {
  const entries = splitCc(ccText);
  let ccError: string | null = null;
  if (entries.length > MAX_CC) {
    ccError = `Enter at most ${MAX_CC} CC addresses (you entered ${entries.length}).`;
  } else {
    const bad = entries.filter(entry => entry.length > MAX_ADDRESS || !ADDRESS.test(entry));
    if (bad.length > 0) {
      ccError = `Not a valid email address: ${bad.join(', ')}. Enter bare addresses, one per entry, separated by commas (no names).`;
    }
  }
  const messageError =
    message.length > MAX_MESSAGE ? `The message is ${message.length} characters; the limit is ${MAX_MESSAGE}.` : null;
  if (ccError || messageError) return { ok: false, ccError, messageError };

  const seen = new Set<string>();
  const cc: string[] = [];
  for (const entry of entries) {
    const key = entry.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    cc.push(entry);
  }
  return { ok: true, request: { cc, message: message.trim() } };
}

// ─────────────────────────────────────────────────────────────────────────────
// Outcomes
// ─────────────────────────────────────────────────────────────────────────────

/**
 * What the user can do after a failure:
 *   - `retry`   the same request may succeed; the dialog offers Try again.
 *   - `edit`    the input was not accepted; correct it and send again.
 *   - `manual`  the email will not go from here; Download the files, deliver them, then Mark Sent to AP.
 *   - `unknown` no answer arrived, so it is not known whether the email went; check Sent Items before anything else.
 *   - `reload`  sign-in could not be verified; reload the page.
 *   - `none`    the batch is not available to this user; nothing more can be done here.
 */
export type NextStep = 'retry' | 'edit' | 'manual' | 'unknown' | 'reload' | 'none';

export interface EmailFailureView {
  message: string;
  next: NextStep;
  /** The stable `errorCode`, when the server sent one. */
  code: string | null;
}

/** The uniform answer for a batch the caller may not use (EB-D-178): the same words for "unknown" and "not allowed". */
export const BATCH_UNAVAILABLE_TEXT = "This export batch isn't available to you.";

const MANUAL = 'Use Download to get the files, deliver them to AP yourself, then use Mark Sent to AP.';

/** Shown when the settings hold no AP address (the 409 answer, or an empty address read before sending). */
export const AP_NOT_CONFIGURED_TEXT = `No usable AP email address is set in the eBilling settings (it is empty or is not a single valid address), so nothing was sent. Do not try again. ${MANUAL}`;

/** Turn a failed Email to AP call into one plain sentence and the next step. */
export function emailFailure(f: ApiFailure): EmailFailureView {
  // Some refusals (403 caller_unresolved, ProblemDetailsHelper.Forbidden) carry their stable code in `reasonCode`.
  const code = f.errorCode ?? f.reasonCode;
  const view = (message: string, next: NextStep): EmailFailureView => ({ message, next, code });

  if (f.signInFailed || f.status === 401) {
    return view('Sign-in could not be verified, so nothing was sent. Close this dialog, reload the page and try again.', 'reload');
  }
  if (f.status === 404) return view(BATCH_UNAVAILABLE_TEXT, 'none');
  if (f.status === 0) {
    return view(
      'No answer arrived, so it is not known whether the email was sent. Check your Sent Items before doing anything else. ' +
        'If it was sent, use Mark Sent to AP; if it was not, use Download and deliver the files yourself, then Mark Sent to AP.',
      'unknown'
    );
  }

  switch (code) {
    case CODE_INVALID_REQUEST:
      return view(
        `The email was not sent: a CC address or the message was not accepted${f.detail ? ` (${f.detail})` : ''}. Correct it and send again.`,
        'edit'
      );
    case CODE_CALLER_UNRESOLVED:
      return view(`Your user account could not be identified, so nothing was sent. Do not try again. ${MANUAL}`, 'manual');
    case CODE_AP_EMAIL_NOT_CONFIGURED:
      return view(AP_NOT_CONFIGURED_TEXT, 'manual');
    case CODE_FILE_NOT_AVAILABLE:
      return view(
        `The batch is not Completed, or a file is missing or still being scanned, so nothing was sent. Do not try again from here. ${MANUAL}`,
        'manual'
      );
    case CODE_RECIPIENT_MONITORED:
      return view(
        `The AP address, a CC address or your own mailbox is a mailbox that Spaarke monitors, so nothing was sent. Do not try again. ${MANUAL}`,
        'manual'
      );
    case CODE_ATTACHMENTS_TOO_LARGE:
      return view(`The export files are too large to send by email (the limit is 3 MB), so nothing was sent. Do not try again. ${MANUAL}`, 'manual');
    case CODE_EMAIL_OWNER_REFUSED:
      return view(`The email could not be tied to this batch's owning team, so nothing was sent. Do not try again. ${MANUAL}`, 'manual');
    case CODE_EMAIL_SEND_REFUSED:
      return view(`The mail service refused to send the email, so nothing was sent. Do not try again. ${MANUAL}`, 'manual');
    case CODE_EMAIL_SEND_FAILED:
      return view(
        'The mail service had a temporary problem. The email may not have been sent, and the Sent to AP Date was not set. Check your Sent Items, then try again if it is not there.',
        'retry'
      );
    case CODE_UNAVAILABLE:
      return view('A service this step needs is temporarily unavailable, so nothing was sent. Try again in a moment.', 'retry');
    case CODE_EBILLING_DISABLED:
      return view(`eBilling is turned off in this environment, so nothing was sent. Do not try again; ask an administrator. ${MANUAL}`, 'manual');
    default:
      break;
  }
  return view(
    `${describeFailure(f)} If you are not sure whether the email went, check your Sent Items before sending again; otherwise do not try again. ${MANUAL}`,
    'manual'
  );
}

export type SentKind = 'recorded' | 'unchanged' | 'not_recorded';

export interface EmailSentView {
  kind: SentKind;
  title: string;
  message: string;
  sentTo: string;
  cc: string[];
  /** The Sent to AP Date, as the user's locale shows it; '' when it could not be recorded. */
  sentToApDate: string;
}

/** A 200 as one of three states. `not_recorded` (the date write failed after the send) must never be sent again. */
export function emailSuccess(r: EmailResponse, locale?: string, timeZone?: string): EmailSentView {
  const base = { sentTo: r.sentTo, cc: r.cc ?? [] };
  if (!r.sentToApDate) {
    return {
      ...base,
      kind: 'not_recorded',
      title: 'Email sent',
      message: 'The email was sent, but the Sent to AP Date could not be recorded; use Mark Sent to AP.',
      sentToApDate: '',
    };
  }
  const date = formatDateTime(r.sentToApDate, locale, timeZone);
  if (r.sentToApDateSet) {
    return { ...base, kind: 'recorded', title: 'Sent to AP', message: 'The email was sent and the Sent to AP Date was recorded.', sentToApDate: date };
  }
  return {
    ...base,
    kind: 'unchanged',
    title: 'Sent to AP',
    message: 'The email was sent. The Sent to AP Date was already set and is unchanged.',
    sentToApDate: date,
  };
}

export type EmailOutcome = { kind: 'sent'; sent: EmailSentView } | { kind: 'failed'; failure: EmailFailureView };

export function emailOutcome(result: ApiResult<EmailResponse>, locale?: string, timeZone?: string): EmailOutcome {
  return result.ok ? { kind: 'sent', sent: emailSuccess(result.value, locale, timeZone) } : { kind: 'failed', failure: emailFailure(result.failure) };
}

/** True when a failure leaves the dialog able to send again (the user may try again or correct the input). */
export function allowsAnotherSend(next: NextStep): boolean {
  return next === 'retry' || next === 'edit';
}

// ─────────────────────────────────────────────────────────────────────────────
// The sender: one send at a time, and none after the email went
// ─────────────────────────────────────────────────────────────────────────────

export type SendResult = EmailOutcome | { kind: 'skipped'; reason: 'in_flight' | 'locked' };

/** What the Email to AP step shows. Derived from the sender's last outcome, so a step that is closed and reopened (Back) keeps it. */
export type EmailPhase =
  | { name: 'compose' }
  | { name: 'sending' }
  | { name: 'sent'; sent: EmailSentView }
  | { name: 'failed'; failure: EmailFailureView };

/** The phase a step opens in: compose for a sender that has not sent, else the state its last send ended in. */
export function phaseOf(outcome: EmailOutcome | null): EmailPhase {
  if (!outcome) return { name: 'compose' };
  return outcome.kind === 'sent' ? { name: 'sent', sent: outcome.sent } : { name: 'failed', failure: outcome.failure };
}

/** The sent view once Mark Sent to AP has recorded the date after an email whose date could not be recorded. */
export function withMarkedDate(sent: EmailSentView, markedDate: string | null): EmailSentView {
  if (sent.kind !== 'not_recorded' || !markedDate) return sent;
  return {
    ...sent,
    kind: 'recorded',
    title: 'Sent to AP',
    message: 'The email was sent and the Sent to AP Date was recorded with Mark Sent to AP.',
    sentToApDate: markedDate,
  };
}

/**
 * Whether the Mark Sent to AP section shows. It is hidden only for a date that was already held when the step opened,
 * and once an email recorded the date; it stays after Mark Sent itself ran, so the user sees the answer.
 */
export function showMarkSent(dateHeldAtOpen: boolean, phase: EmailPhase): boolean {
  if (dateHeldAtOpen || phase.name === 'sending') return false;
  return !(phase.name === 'sent' && phase.sent.kind !== 'not_recorded');
}

/** A caution above Send: the batch already has a date, or the read that would tell failed. */
export type ResendCaution = 'held' | 'unknown' | null;

export const RESEND_HELD_TITLE = 'This batch already has a Sent to AP Date';
export const RESEND_HELD_TEXT = 'Sending again emails AP another time; the Sent to AP Date stays as it is.';
export const RESEND_UNKNOWN_TEXT =
  "Couldn't check whether this batch was already sent. Sending may email AP again; the server keeps an existing Sent to AP Date.";

export function resendCaution(sentToApDate: string | null, readFailed: boolean): ResendCaution {
  if (sentToApDate) return 'held';
  return readFailed ? 'unknown' : null;
}

/** The Send button's label: "Send again" when a date is already held, "Try again" after a retryable failure. */
export function sendLabel(caution: ResendCaution, phase: EmailPhase, sending: boolean): string {
  if (sending) return 'Sending…';
  if (phase.name === 'failed') return 'Try again';
  return caution === 'held' ? 'Send again' : 'Send to AP';
}

export interface EmailSender {
  /** The outcome of the last send that ran (null before one did). A reopened step starts from it. */
  readonly lastOutcome: EmailOutcome | null;
  /** Start a send. Skipped (nothing sent) while another is in flight, and once the email went or a failure was final. */
  send(request: EmailRequest): Promise<SendResult>;
  /** True once no further send may start: the email went, or a failure said not to try again. */
  readonly locked: boolean;
}

export function createEmailSender(
  api: Pick<ApExportApi, 'emailBatch'>,
  batchId: string,
  locale?: string,
  timeZone?: string
): EmailSender {
  const flight = createSingleFlight();
  let locked = false;
  let lastOutcome: EmailOutcome | null = null;
  return {
    get locked() {
      return locked;
    },
    get lastOutcome() {
      return lastOutcome;
    },
    async send(request) {
      if (locked) return { kind: 'skipped', reason: 'locked' };
      const ran = await flight.run(async () => emailOutcome(await api.emailBatch(batchId, request), locale, timeZone));
      if (!ran.ran) return { kind: 'skipped', reason: 'in_flight' };
      const outcome = ran.value;
      lastOutcome = outcome;
      if (outcome.kind === 'sent' || !allowsAnotherSend(outcome.failure.next)) locked = true;
      return outcome;
    },
  };
}
