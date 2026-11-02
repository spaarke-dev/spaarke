import {
  AP_NOT_CONFIGURED_TEXT,
  BATCH_UNAVAILABLE_TEXT,
  MAX_CC,
  MAX_MESSAGE,
  allowsAnotherSend,
  createEmailSender,
  emailFailure,
  emailOutcome,
  emailSuccess,
  splitCc,
  validateEmailInput,
  type NextStep,
} from '../services/emailFlow';
import type { ApiResult, EmailResponse } from '../services/apExportApi';
import { toFailure } from '../services/apExportApi';
import { AuthError, BATCH_ID, failed, ok, problem } from './testKit';

const sent = (over: Partial<EmailResponse> = {}): EmailResponse => ({
  communicationId: 'c1',
  sentTo: 'ap@example.com',
  cc: ['a@example.com'],
  sentToApDate: '2026-11-02T09:30:00+00:00',
  sentToApDateSet: true,
  ...over,
});

const failure = (status: number, errorCode: string | null, extra: Record<string, unknown> = {}) =>
  emailFailure(toFailure(problem(status, errorCode, extra)));

/** Every error code of the 151c-1a notes, with what the dialog must tell the user to do next. */
const CODES: Array<[number, string, NextStep]> = [
  [400, 'ebilling.export.invalid_request', 'edit'],
  [403, 'ebilling.export.caller_unresolved', 'manual'],
  [409, 'ebilling.export.ap_email_not_configured', 'manual'],
  [409, 'ebilling.export.file_not_available', 'manual'],
  [409, 'ebilling.export.ap_email_recipient_monitored', 'manual'],
  [409, 'ebilling.export.attachments_too_large', 'manual'],
  [409, 'ebilling.export.email_owner_refused', 'manual'],
  [409, 'ebilling.export.email_send_refused', 'manual'],
  [503, 'ebilling.export.email_send_failed', 'retry'],
  [503, 'ebilling.export.unavailable', 'retry'],
  [503, 'ebilling.disabled', 'manual'],
];

describe('emailFailure: every error code becomes one plain sentence that says what to do next', () => {
  it.each(CODES)('%i %s', (status, code, next) => {
    const f = failure(status, code, { detail: 'server words' });
    expect(f.next).toBe(next);
    expect(f.code).toBe(code);
    expect(f.message.length).toBeGreaterThan(20);
    // The user is told either to try again or to use Download plus Mark Sent to AP (or to correct the input).
    if (next === 'retry') expect(f.message).toMatch(/Try again/);
    else if (next === 'manual') {
      expect(f.message).toMatch(/Do not try again/);
      expect(f.message).toMatch(/Download/);
      expect(f.message).toMatch(/Mark Sent to AP/);
    } else expect(f.message).toMatch(/send again/);
  });

  it('the non-retryable 409 refusals never offer another send', () => {
    for (const code of [
      'ap_email_recipient_monitored',
      'email_owner_refused',
      'email_send_refused',
      'attachments_too_large',
      'file_not_available',
      'ap_email_not_configured',
    ]) {
      const f = failure(409, `ebilling.export.${code}`);
      expect(allowsAnotherSend(f.next)).toBe(false);
    }
  });

  it('only the two 503 codes are retryable (plus input to correct on a 400)', () => {
    const open = CODES.filter(([, , next]) => allowsAnotherSend(next)).map(([, code]) => code);
    expect(open).toEqual(['ebilling.export.invalid_request', 'ebilling.export.email_send_failed', 'ebilling.export.unavailable']);
  });

  it('the uniform 404 reads "This export batch isn\'t available to you." and offers nothing', () => {
    const f = emailFailure(toFailure(problem(404, null, { reasonCode: 'sdap.access.deny.record_unavailable' })));
    expect(f.message).toBe("This export batch isn't available to you.");
    expect(f.message).toBe(BATCH_UNAVAILABLE_TEXT);
    expect(f.next).toBe('none');
  });

  it('the not-configured message is shared by the 409 and the empty-address read', () => {
    expect(failure(409, 'ebilling.export.ap_email_not_configured').message).toBe(AP_NOT_CONFIGURED_TEXT);
  });

  it('a monitored-mailbox sentence covers the AP address, a CC and the sender\'s own mailbox', () => {
    const m = failure(409, 'ebilling.export.ap_email_recipient_monitored').message;
    expect(m).toMatch(/AP address, a CC address or your own mailbox/);
  });

  it('a 400 names the server detail so the user can correct the input', () => {
    expect(failure(400, 'ebilling.export.invalid_request', { detail: 'Too many CCs' }).message).toContain('Too many CCs');
  });

  it('a failed sign-in asks for a reload and sent nothing', () => {
    const f = emailFailure(toFailure(new AuthError('no token', 'auth_exhausted')));
    expect(f.next).toBe('reload');
    expect(f.message).toMatch(/nothing was sent/);
  });

  it('no answer at all: not known whether it went, so no retry and a check of Sent Items', () => {
    const f = emailFailure(toFailure(new TypeError('Failed to fetch')));
    expect(f.next).toBe('unknown');
    expect(f.message).toMatch(/not known whether the email was sent/);
    expect(f.message).toMatch(/Sent Items/);
    expect(allowsAnotherSend(f.next)).toBe(false);
  });

  it('an unrecognized refusal keeps the server words and is not retryable', () => {
    const f = failure(409, 'ebilling.export.something_new', { detail: 'New refusal.' });
    expect(f.next).toBe('manual');
    expect(f.message).toContain('New refusal. (ebilling.export.something_new)');
    expect(f.message).toMatch(/Mark Sent to AP/);
  });
});

describe('emailSuccess: the three sent states', () => {
  it('date set by this call: "Sent to AP" with the date and the recipients', () => {
    const v = emailSuccess(sent(), 'en-US', 'UTC');
    expect(v.kind).toBe('recorded');
    expect(v.title).toBe('Sent to AP');
    expect(v.sentToApDate).toBe('Nov 2, 2026, 9:30 AM');
    expect(v.sentTo).toBe('ap@example.com');
    expect(v.cc).toEqual(['a@example.com']);
  });

  it('a re-send returns the date already held and says it is unchanged', () => {
    const v = emailSuccess(sent({ sentToApDateSet: false }), 'en-US', 'UTC');
    expect(v.kind).toBe('unchanged');
    expect(v.title).toBe('Sent to AP');
    expect(v.message).toMatch(/unchanged/);
    expect(v.sentToApDate).toBe('Nov 2, 2026, 9:30 AM');
  });

  it('sentToApDateSet:false with no date: the email went, the date was not recorded, use Mark Sent to AP', () => {
    const v = emailSuccess(sent({ sentToApDateSet: false, sentToApDate: null }));
    expect(v.kind).toBe('not_recorded');
    expect(v.message).toBe('The email was sent, but the Sent to AP Date could not be recorded; use Mark Sent to AP.');
    expect(v.sentToApDate).toBe('');
  });

  it('emailOutcome maps a 200 to sent and a failure to failed', () => {
    expect(emailOutcome(ok(sent())).kind).toBe('sent');
    expect(emailOutcome(failed(problem(503, 'ebilling.export.email_send_failed'))).kind).toBe('failed');
  });
});

describe('validateEmailInput: the CC and message rules (the server stays the authority)', () => {
  it('accepts no CC and no message', () => {
    expect(validateEmailInput('', '')).toEqual({ ok: true, request: { cc: [], message: '' } });
  });

  it('splits on commas, semicolons and new lines, trims, ignores blanks, and drops case-insensitive repeats', () => {
    expect(splitCc(' a@x.com, b@x.com;\n c@x.com ,, ')).toEqual(['a@x.com', 'b@x.com', 'c@x.com']);
    const r = validateEmailInput('A@x.com, a@X.com, b@x.com', ' Please pay by Friday. ');
    expect(r).toEqual({ ok: true, request: { cc: ['A@x.com', 'b@x.com'], message: 'Please pay by Friday.' } });
  });

  it('refuses a display name, two addresses in one entry and a missing @', () => {
    for (const bad of ['Jane <j@x.com>', 'a@x.com b@x.com', 'not-an-address', '@x.com', 'a@', '"a"@x.com']) {
      const r = validateEmailInput(bad, '');
      expect(r.ok).toBe(false);
      expect(!r.ok && r.ccError).toContain('Not a valid email address');
    }
  });

  it('accepts exactly 10 CCs and refuses 11', () => {
    const ten = Array.from({ length: MAX_CC }, (_, i) => `u${i}@x.com`).join(',');
    expect(validateEmailInput(ten, '').ok).toBe(true);
    const eleven = `${ten},u10@x.com`;
    const r = validateEmailInput(eleven, '');
    expect(!r.ok && r.ccError).toBe('Enter at most 10 CC addresses (you entered 11).');
  });

  it('accepts a message of exactly 2000 characters and refuses 2001', () => {
    expect(validateEmailInput('', 'x'.repeat(MAX_MESSAGE)).ok).toBe(true);
    const r = validateEmailInput('', 'x'.repeat(MAX_MESSAGE + 1));
    expect(!r.ok && r.messageError).toBe('The message is 2001 characters; the limit is 2000.');
  });

  it('reports a CC error and a message error together', () => {
    const r = validateEmailInput('nope', 'x'.repeat(MAX_MESSAGE + 1));
    expect(r.ok).toBe(false);
    expect(!r.ok && r.ccError).toBeTruthy();
    expect(!r.ok && r.messageError).toBeTruthy();
  });
});

describe('createEmailSender: one send at a time, and none after the email went', () => {
  const REQUEST = { cc: [], message: '' };

  /** An api whose emailBatch waits for `release`, so a second click can arrive while the first is in flight. */
  function api(answer: ApiResult<EmailResponse>, gate = false) {
    let release: () => void = () => undefined;
    const waiting = new Promise<void>(resolve => {
      release = resolve;
    });
    const emailBatch = jest.fn(async () => {
      if (gate) await waiting;
      return answer;
    });
    return { api: { emailBatch }, emailBatch, release };
  }

  it('a double click sends once: the second call is refused while the first is in flight', async () => {
    const h = api(ok(sent()), true);
    const sender = createEmailSender(h.api, BATCH_ID);
    const first = sender.send(REQUEST);
    const second = await sender.send(REQUEST);
    expect(second).toEqual({ kind: 'skipped', reason: 'in_flight' });
    h.release();
    const result = await first;
    expect(result.kind).toBe('sent');
    expect(h.emailBatch).toHaveBeenCalledTimes(1);
  });

  it('sends the batch id and the request to the api', async () => {
    const h = api(ok(sent()));
    await createEmailSender(h.api, BATCH_ID).send({ cc: ['a@x.com'], message: 'hi' });
    expect(h.emailBatch).toHaveBeenCalledWith(BATCH_ID, { cc: ['a@x.com'], message: 'hi' });
  });

  it('no re-send after a send whose Sent to AP Date was not recorded', async () => {
    const h = api(ok(sent({ sentToApDateSet: false, sentToApDate: null })));
    const sender = createEmailSender(h.api, BATCH_ID);
    const first = await sender.send(REQUEST);
    expect(first.kind === 'sent' && first.sent.kind).toBe('not_recorded');
    expect(sender.locked).toBe(true);
    expect(await sender.send(REQUEST)).toEqual({ kind: 'skipped', reason: 'locked' });
    expect(h.emailBatch).toHaveBeenCalledTimes(1);
  });

  it('no re-send after any 200 (date set, or already held)', async () => {
    for (const body of [sent(), sent({ sentToApDateSet: false })]) {
      const h = api(ok(body));
      const sender = createEmailSender(h.api, BATCH_ID);
      await sender.send(REQUEST);
      expect(await sender.send(REQUEST)).toEqual({ kind: 'skipped', reason: 'locked' });
      expect(h.emailBatch).toHaveBeenCalledTimes(1);
    }
  });

  it('a retryable failure leaves the sender open: the same request can be sent again', async () => {
    const h = api(failed(problem(503, 'ebilling.export.email_send_failed')));
    const sender = createEmailSender(h.api, BATCH_ID);
    expect((await sender.send(REQUEST)).kind).toBe('failed');
    expect(sender.locked).toBe(false);
    expect((await sender.send(REQUEST)).kind).toBe('failed');
    expect(h.emailBatch).toHaveBeenCalledTimes(2);
  });

  it('a 400 leaves the sender open so the input can be corrected', async () => {
    const h = api(failed(problem(400, 'ebilling.export.invalid_request')));
    const sender = createEmailSender(h.api, BATCH_ID);
    await sender.send(REQUEST);
    expect(sender.locked).toBe(false);
  });

  it('a non-retryable refusal locks the sender; so do a 404, a missing answer and a sign-in failure', async () => {
    const answers: ApiResult<EmailResponse>[] = [
      failed(problem(409, 'ebilling.export.email_send_refused')),
      failed(problem(404, null)),
      failed(new TypeError('Failed to fetch')),
      failed(new AuthError('no token', 'auth_exhausted')),
    ];
    for (const answer of answers) {
      const h = api(answer);
      const sender = createEmailSender(h.api, BATCH_ID);
      await sender.send(REQUEST);
      expect(sender.locked).toBe(true);
      await sender.send(REQUEST);
      expect(h.emailBatch).toHaveBeenCalledTimes(1);
    }
  });
});
