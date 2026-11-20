/**
 * The notice to the firm on a Reject or Return (ebilling-r1 task 132ui; server 131 drafts it, 132 screens and sends it). One place for what the
 * Notify step, the Confirm step and the decision call share, so the page and the server's rules read the same words:
 *
 *   - the notice is the reviewer's explicit choice: it is sent only when a draft has loaded, "Send this notice to the firm" is on and the step
 *     was not skipped; otherwise the request carries neither `noticeSubject` nor `noticeProse` (never empty strings) and the firm gets no email;
 *   - the limits are hints that mirror the server's caps (ADR-002: the server screens and stays the authority): the subject at most 120
 *     characters not counting the tracking number the server appends in brackets, the prose at most 1500, both counted after trimming; the
 *     boxes never cut what is typed or pasted (no `maxLength`, as the Note box), they count and say when a text is over;
 *   - a notice the server refuses is 400 `ebilling.notice_refused` with the reason in its detail, shown under the notice fields.
 *
 * The limits and the code are pinned to the server's source by `__tests__/serverWordsPin.test.ts`.
 */

import type { FirmNotice, FirmNoticeDraft, FirmNoticeKind } from './decisionApi';
import type { InvoiceOutcome } from './decisionModel';
import type { ApiFailure } from './invoiceReviewApi';

/** The most characters of a subject (`FirmNoticeProse.MaxSubjectLength`), not counting the tracking-number suffix code writes. */
export const NOTICE_SUBJECT_MAX = 120;
/** The most characters of the prose (`FirmNoticeProse.MaxProseLength`). */
export const NOTICE_PROSE_MAX = 1500;
/** 400 (`InvoiceRejectReturnCodes.NoticeRefused`): the confirmed notice failed the firm screen; nothing was decided. */
export const CODE_NOTICE_REFUSED = 'ebilling.notice_refused';

/** The draft kind of an outcome: Reject asks for `InvoiceRejected`, Return to firm for `ReturnedToFirm`; an Approve has no notice. */
export function noticeKindOf(outcome: InvoiceOutcome | undefined): FirmNoticeKind | undefined {
  return outcome === 'reject' ? 'InvoiceRejected' : outcome === 'return' ? 'ReturnedToFirm' : undefined;
}

/** What the reviewer sees for one kind. `idle` = not asked for yet; `ready` = the text below is the notice (drafted, or restored from a locked request). */
export interface NoticeEntry {
  status: 'idle' | 'loading' | 'ready' | 'failed';
  /** The draft as the server returned it; null until it loads, and for an entry restored from a locked request (nothing to restore to). */
  draft: FirmNoticeDraft | null;
  /** The tracking-number suffix of the drafted subject (` (tracking)`, or ''), which the server removes before it counts the subject. */
  subjectSuffix: string;
  subject: string;
  prose: string;
  /** "Send this notice to the firm". On once a draft has loaded; off for a failed or restored-unsent entry. */
  send: boolean;
  /** A failed draft: the server's sentence with its code. */
  failure: string | null;
}

export const EMPTY_NOTICE: NoticeEntry = { status: 'idle', draft: null, subjectSuffix: '', subject: '', prose: '', send: false, failure: null };

export type NoticeEntries = Readonly<Record<FirmNoticeKind, NoticeEntry>>;

export const EMPTY_NOTICES: NoticeEntries = { InvoiceRejected: EMPTY_NOTICE, ReturnedToFirm: EMPTY_NOTICE };

/** The tracking-number suffix of a drafted subject: the trailing bracketed group the server appends (` (ABC-1)`), or ''. */
export function subjectSuffixOf(subject: string): string {
  return /\s\([^()]*\)$/.exec(subject.trim())?.[0] ?? '';
}

/** How many characters count against the subject limit: trimmed, and without the tracking suffix the draft ended with (the server removes it too). */
export function subjectCount(subject: string, suffix: string): number {
  const t = subject.trim();
  return suffix !== '' && t.endsWith(suffix) ? t.slice(0, t.length - suffix.length).trimEnd().length : t.length;
}

export function proseCount(prose: string): number {
  return prose.trim().length;
}

export interface NoticeProblems {
  subject: string | null;
  prose: string | null;
}

/** Why the typed notice cannot be sent (blank or over the limit), per field; the server owns every other rule (digits, links, amounts). */
export function noticeFieldProblems(e: Pick<NoticeEntry, 'subject' | 'prose' | 'subjectSuffix'>): NoticeProblems {
  const sc = subjectCount(e.subject, e.subjectSuffix);
  const pc = proseCount(e.prose);
  return {
    subject: e.subject.trim() === '' ? 'Enter a subject.' : sc > NOTICE_SUBJECT_MAX ? `The subject may be at most ${NOTICE_SUBJECT_MAX} characters.` : null,
    prose: pc === 0 ? 'Enter a message.' : pc > NOTICE_PROSE_MAX ? `The message may be at most ${NOTICE_PROSE_MAX} characters.` : null,
  };
}

/** `12 / 120 characters`. */
export function noticeCounterText(count: number, max: number): string {
  return `${count.toLocaleString('en-US')} / ${max.toLocaleString('en-US')} characters`;
}

/**
 * What holds Next on the Notify step: `loading` while the draft is out (Next would otherwise leave the Confirm step's words to change when it
 * arrives), `invalid` for a blank or over-limit notice with sending on, else null. A failed draft, or sending off, holds nothing: the decision
 * is never blocked by the notice.
 */
export function noticeBlocker(e: NoticeEntry): 'loading' | 'invalid' | null {
  if (e.status === 'idle' || e.status === 'loading') return 'loading';
  if (e.status === 'ready' && e.send) {
    const p = noticeFieldProblems(e);
    if (p.subject !== null || p.prose !== null) return 'invalid';
  }
  return null;
}

/**
 * The notice the decision request carries, or null (no notice fields at all). Only a loaded draft with sending on, not skipped, and valid is
 * sent; a decision that came back unsure keeps its choice whatever Skip says (`locked`: the repeat is the same request). The text is trimmed.
 */
export function noticeToSend(entry: NoticeEntry | undefined, opts: { skipped: boolean; locked: boolean }): FirmNotice | null {
  if (!entry || entry.status !== 'ready' || !entry.send) return null;
  if (opts.skipped && !opts.locked) return null;
  const subject = entry.subject.trim();
  const prose = entry.prose.trim();
  return subject === '' || prose === '' ? null : { subject, prose };
}

/** The firm notice part of a lock: what the request carried, and the text as typed. */
export function noticeLockOf(entry: NoticeEntry | undefined, sent: FirmNotice | null): { send: boolean; subject: string; prose: string } {
  return { send: sent !== null, subject: sent ? sent.subject : (entry?.subject ?? ''), prose: sent ? sent.prose : (entry?.prose ?? '') };
}

/** The entry of a locked request, reopened: the text and the choice as sent, no draft to restore to, no new request. */
export function noticeEntryOfLock(lock: { send: boolean; subject: string; prose: string } | undefined): NoticeEntry {
  return { ...EMPTY_NOTICE, status: 'ready', subject: lock?.subject ?? '', prose: lock?.prose ?? '', send: lock?.send ?? false };
}

/** True when the server refused the notice itself (400 `ebilling.notice_refused`): nothing was decided, and the notice is shown the reason. */
export function isNoticeRefusal(f: ApiFailure): boolean {
  return f.status === 400 && f.errorCode === CODE_NOTICE_REFUSED;
}

/** The standing words of the source line under the Notify fields. */
export function noticeSourceText(draft: FirmNoticeDraft | null): string | null {
  if (!draft) return null;
  return draft.source === 'Action'
    ? 'Drafted by AI from the findings. Read it and edit it before it is sent.'
    : 'The standard wording, because the AI draft was not available. Read it and edit it before it is sent.';
}
