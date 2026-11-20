/**
 * Task 132ui: the pure rules of the notice to the firm (services/noticeModel.ts): which kind an outcome asks for, how the limits are counted,
 * what holds Next, and the one rule that decides whether a notice is in the request (`noticeToSend`).
 */

import {
  EMPTY_NOTICE,
  NOTICE_PROSE_MAX,
  NOTICE_SUBJECT_MAX,
  isNoticeRefusal,
  noticeBlocker,
  noticeEntryOfLock,
  noticeFieldProblems,
  noticeKindOf,
  noticeLockOf,
  noticeToSend,
  subjectCount,
  subjectSuffixOf,
  type NoticeEntry,
} from '../services/noticeModel';

const ready = (over: Partial<NoticeEntry> = {}): NoticeEntry => ({ ...EMPTY_NOTICE, status: 'ready', subject: 'Subject (T-1)', prose: 'A message.', send: true, subjectSuffix: ' (T-1)', ...over });
const failure = (status: number, errorCode: string | null) => ({ status, errorCode, detail: null, signInFailed: false, failedStep: null, committedLineIds: [] });

describe('noticeKindOf', () => {
  it('Reject asks InvoiceRejected, Return asks ReturnedToFirm, Approve and nothing ask for none', () => {
    expect(noticeKindOf('reject')).toBe('InvoiceRejected');
    expect(noticeKindOf('return')).toBe('ReturnedToFirm');
    expect(noticeKindOf('approve')).toBeUndefined();
    expect(noticeKindOf(undefined)).toBeUndefined();
  });
});

describe('the subject and message limits (hints that mirror the server)', () => {
  it('reads the tracking suffix of a drafted subject, and nothing from a subject without one', () => {
    expect(subjectSuffixOf('Your invoice was rejected (INV-2025-78232.1)')).toBe(' (INV-2025-78232.1)');
    expect(subjectSuffixOf('  Your invoice was rejected (T-1)  ')).toBe(' (T-1)');
    expect(subjectSuffixOf('Your invoice was rejected')).toBe('');
    expect(subjectSuffixOf('(only a group)')).toBe('');
  });

  it('counts the trimmed subject without the suffix the draft ended with, and the whole text when the suffix is gone', () => {
    expect(subjectCount('abc (T-1)', ' (T-1)')).toBe(3);
    expect(subjectCount('  abc (T-1)  ', ' (T-1)')).toBe(3);
    expect(subjectCount('abc', ' (T-1)')).toBe(3);
    expect(subjectCount('abc (T-1)', '')).toBe(9);
  });

  it('holds a blank or over-limit text, exactly at the limits', () => {
    expect(NOTICE_SUBJECT_MAX).toBe(120);
    expect(NOTICE_PROSE_MAX).toBe(1500);
    expect(noticeFieldProblems(ready())).toEqual({ subject: null, prose: null });
    expect(noticeFieldProblems(ready({ subject: 'a'.repeat(120) + ' (T-1)' })).subject).toBeNull();
    expect(noticeFieldProblems(ready({ subject: 'a'.repeat(121) + ' (T-1)' })).subject).toBe('The subject may be at most 120 characters.');
    expect(noticeFieldProblems(ready({ prose: 'b'.repeat(1500) })).prose).toBeNull();
    expect(noticeFieldProblems(ready({ prose: 'b'.repeat(1501) })).prose).toBe('The message may be at most 1500 characters.');
    expect(noticeFieldProblems(ready({ subject: '   ' })).subject).toBe('Enter a subject.');
    expect(noticeFieldProblems(ready({ prose: '' })).prose).toBe('Enter a message.');
  });
});

describe('noticeBlocker: what holds Next', () => {
  it('holds while the draft is out (idle or loading), and for an invalid notice with sending on', () => {
    expect(noticeBlocker({ ...EMPTY_NOTICE, status: 'idle' })).toBe('loading');
    expect(noticeBlocker({ ...EMPTY_NOTICE, status: 'loading' })).toBe('loading');
    expect(noticeBlocker(ready({ prose: '' }))).toBe('invalid');
    expect(noticeBlocker(ready())).toBeNull();
  });

  it('(must-not-fire) holds nothing for a failed draft, or for sending off', () => {
    expect(noticeBlocker({ ...EMPTY_NOTICE, status: 'failed', failure: 'x' })).toBeNull();
    expect(noticeBlocker(ready({ prose: '', send: false }))).toBeNull();
  });
});

describe('noticeToSend: the one rule of what the request carries', () => {
  const free = { skipped: false, locked: false };

  it('sends the trimmed text of a loaded draft with sending on', () => {
    expect(noticeToSend(ready({ subject: '  S (T-1) ', prose: '  P  ' }), free)).toEqual({ subject: 'S (T-1)', prose: 'P' });
  });

  it('(must-not-fire) sends nothing for: sending off, a draft that is not loaded or failed, a skipped step, blank text, no entry', () => {
    expect(noticeToSend(ready({ send: false }), free)).toBeNull();
    expect(noticeToSend({ ...EMPTY_NOTICE, status: 'loading' }, free)).toBeNull();
    expect(noticeToSend({ ...EMPTY_NOTICE, status: 'failed' }, free)).toBeNull();
    expect(noticeToSend(ready(), { skipped: true, locked: false })).toBeNull();
    expect(noticeToSend(ready({ subject: '  ' }), free)).toBeNull();
    expect(noticeToSend(ready({ prose: '' }), free)).toBeNull();
    expect(noticeToSend(undefined, free)).toBeNull();
  });

  it('a locked request keeps its choice whatever Skip says', () => {
    expect(noticeToSend(ready(), { skipped: true, locked: true })).toEqual({ subject: 'Subject (T-1)', prose: 'A message.' });
    expect(noticeToSend(ready({ send: false }), { skipped: true, locked: true })).toBeNull();
  });
});

describe('the lock of a notice', () => {
  it('records what was sent, or that nothing was, with the text as typed', () => {
    expect(noticeLockOf(ready(), { subject: 'S', prose: 'P' })).toEqual({ send: true, subject: 'S', prose: 'P' });
    expect(noticeLockOf(ready({ subject: 'typed', prose: 'typed too' }), null)).toEqual({ send: false, subject: 'typed', prose: 'typed too' });
    expect(noticeLockOf(undefined, null)).toEqual({ send: false, subject: '', prose: '' });
  });

  it('a reopened entry is the lock as it was: ready, no draft, no new request', () => {
    expect(noticeEntryOfLock({ send: true, subject: 'S', prose: 'P' })).toMatchObject({ status: 'ready', send: true, subject: 'S', prose: 'P', draft: null });
    expect(noticeEntryOfLock(undefined)).toMatchObject({ status: 'ready', send: false, subject: '', prose: '' });
  });
});

describe('isNoticeRefusal', () => {
  it('is 400 with the notice_refused code and nothing else', () => {
    expect(isNoticeRefusal(failure(400, 'ebilling.notice_refused'))).toBe(true);
    expect(isNoticeRefusal(failure(400, 'ebilling.reason_required'))).toBe(false);
    expect(isNoticeRefusal(failure(409, 'ebilling.notice_refused'))).toBe(false);
    expect(isNoticeRefusal(failure(400, null))).toBe(false);
  });
});
