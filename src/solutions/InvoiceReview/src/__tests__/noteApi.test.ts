/**
 * Task 095f-2ui: the optional note on every decision call, at the wire. For each call: `note` is sent as typed when there is one, and the key
 * is ABSENT (not null, not "") when the note is missing, empty or whitespace-only. Accept all Clean never sends one (must-not-fire).
 *
 * The note is part of what the server hashes (a `note: null` would hash differently from no key), so "absent" is a contract, not a style.
 */

import { createInvoiceDecisionApi, decisionRoutes } from '../services/decisionApi';
import { createInvoiceReviewApi, routes } from '../services/invoiceReviewApi';
import { NOTE_MAX_CHARS, noteArgs, noteCount, noteMember, noteProblem, noteToSend } from '../services/noteModel';
import { INVOICE_ID, SUBMISSION_ID, lineId } from './testKit';

const BASE = 'https://bff.example.test/';
const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
const fetchReturning = (body: unknown) => jest.fn(async (_url: string, _init?: RequestInit) => okResponse(body) as never);

type Fetch = ReturnType<typeof fetchReturning>;
const bodyOf = (f: Fetch, i = 0): Record<string, unknown> => JSON.parse(String(f.mock.calls[i][1]?.body));

/** What each blank note looks like when it arrives: undefined, empty, spaces, and a line break and tab. */
const BLANKS: Array<[string, string | undefined]> = [
  ['none', undefined],
  ['an empty string', ''],
  ['spaces', '    '],
  ['a line break and a tab', ' \n\t '],
];

describe('the note helpers', () => {
  it('sends the text as typed (the trim is for the blank check only) and nothing for a blank', () => {
    expect(noteToSend('  Approved by the partner \n')).toBe('  Approved by the partner \n');
    expect(noteToSend('')).toBeUndefined();
    expect(noteToSend('   ')).toBeUndefined();
    expect(noteToSend(null)).toBeUndefined();
    expect(noteMember('  x ')).toEqual({ note: '  x ' });
    expect(Object.keys(noteMember(' '))).toEqual([]);
    expect(noteArgs('x')).toEqual(['x']);
    expect(noteArgs('  ')).toEqual([]);
  });

  it('counts after trimming, and only a note over 2000 characters is a problem (exactly 2000 is not)', () => {
    expect(NOTE_MAX_CHARS).toBe(2000);
    expect(noteCount(` ${'x'.repeat(2000)} `)).toBe(2000);
    expect(noteProblem(` ${'x'.repeat(2000)} `)).toBeNull();
    expect(noteProblem('x'.repeat(2001))).toBe('The note may be at most 2000 characters.');
    expect(noteProblem(undefined)).toBeNull();
  });
});

describe('decisionApi sends the note', () => {
  it('approve: { note } when typed, {} when blank (the old body)', async () => {
    const fetchFn = fetchReturning({ outcome: 'approved' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.approve(INVOICE_ID, 'Agreed with the partner');
    expect(fetchFn.mock.calls[0][0]).toBe(decisionRoutes.approve(BASE, INVOICE_ID));
    expect(bodyOf(fetchFn)).toEqual({ note: 'Agreed with the partner' });
    for (const [, blank] of BLANKS) await api.approve(INVOICE_ID, blank);
    fetchFn.mock.calls.slice(1).forEach((_, i) => expect(bodyOf(fetchFn, i + 1)).toEqual({}));
  });

  it.each(BLANKS)('reject and return with %s: the note key is absent (not null, not "")', async (_, blank) => {
    const fetchFn = fetchReturning({ outcome: 'decided' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.reject(INVOICE_ID, 'Not payable', blank);
    await api.returnToFirm(INVOICE_ID, 'Please correct', blank);
    expect(bodyOf(fetchFn, 0)).toEqual({ reason: 'Not payable' });
    expect(bodyOf(fetchFn, 1)).toEqual({ reason: 'Please correct' });
    expect(Object.keys(bodyOf(fetchFn, 0))).not.toContain('note');
    expect(Object.keys(bodyOf(fetchFn, 1))).not.toContain('note');
  });

  it('reject and return send the note as typed, beside the reason', async () => {
    const fetchFn = fetchReturning({ outcome: 'decided' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.reject(INVOICE_ID, 'Not payable', '  Second duplicate this month ');
    await api.returnToFirm(INVOICE_ID, 'Please correct', 'Rates only');
    expect(bodyOf(fetchFn, 0)).toEqual({ reason: 'Not payable', note: '  Second duplicate this month ' });
    expect(bodyOf(fetchFn, 1)).toEqual({ reason: 'Please correct', note: 'Rates only' });
  });

  it('add adjustment: note beside the id, method, value, reason and rule; absent when blank', async () => {
    const fetchFn = fetchReturning({ adjustment: null });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    const base = { adjustmentId: 'a1', method: 'percent' as const, value: 5, reason: 'Courtesy', ruleId: null };
    await api.addAdjustment(INVOICE_ID, { ...base, note: 'Partner agreed' });
    for (const [, blank] of BLANKS) await api.addAdjustment(INVOICE_ID, { ...base, note: blank });
    expect(bodyOf(fetchFn, 0)).toEqual({ ...base, note: 'Partner agreed' });
    for (let i = 1; i <= BLANKS.length; i++) {
      expect(bodyOf(fetchFn, i)).toEqual(base);
      expect(Object.keys(bodyOf(fetchFn, i))).not.toContain('note');
    }
  });

  it('remove adjustment: a DELETE with a JSON { note } body when typed, and no body at all when blank', async () => {
    const fetchFn = fetchReturning({ adjustment: null });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.removeAdjustment(INVOICE_ID, lineId(9), 'Entered twice');
    expect(fetchFn.mock.calls[0][0]).toBe(decisionRoutes.adjustment(BASE, INVOICE_ID, lineId(9)));
    expect(fetchFn.mock.calls[0][1]?.method).toBe('DELETE');
    expect(bodyOf(fetchFn, 0)).toEqual({ note: 'Entered twice' });
    expect((fetchFn.mock.calls[0][1]?.headers as Record<string, string>)['Content-Type']).toBe('application/json');
    for (const [, blank] of BLANKS) await api.removeAdjustment(INVOICE_ID, lineId(9), blank);
    for (let i = 1; i <= BLANKS.length; i++) {
      expect(fetchFn.mock.calls[i][1]).toEqual({ method: 'DELETE' });
    }
  });

  it('override and blocked return: note beside the reason; absent when blank', async () => {
    const fetchFn = fetchReturning({ outcome: 'Overridden' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.overrideBlocked(SUBMISSION_ID, 'Known resubmission', 'Checked with the firm');
    await api.returnBlocked(SUBMISSION_ID, 'Please resubmit', 'Duplicate of INV-7');
    expect(fetchFn.mock.calls[0][0]).toBe(decisionRoutes.override(BASE, SUBMISSION_ID));
    expect(fetchFn.mock.calls[1][0]).toBe(decisionRoutes.returnBlocked(BASE, SUBMISSION_ID));
    expect(bodyOf(fetchFn, 0)).toEqual({ reason: 'Known resubmission', note: 'Checked with the firm' });
    expect(bodyOf(fetchFn, 1)).toEqual({ reason: 'Please resubmit', note: 'Duplicate of INV-7' });
    for (const [, blank] of BLANKS) {
      await api.overrideBlocked(SUBMISSION_ID, 'Known resubmission', blank);
      await api.returnBlocked(SUBMISSION_ID, 'Please resubmit', blank);
    }
    fetchFn.mock.calls.slice(2).forEach((_, i) => {
      const b = bodyOf(fetchFn, i + 2);
      expect(Object.keys(b)).toEqual(['reason']);
    });
  });
});

describe('invoiceReviewApi sends the note', () => {
  const lineBody = { decisionId: 'd1', action: 'reduce' as const, reason: 'rate', approvedAmount: 1012.5 };
  const linesBody = { decisionId: 'd2', action: 'disallow' as const, reason: 'dup', lines: [{ lineId: lineId(1), approvedAmount: null }] };

  it('decideLine and decideLines: note as typed, one key; absent when blank', async () => {
    const fetchFn = fetchReturning({ decisionId: 'd', lines: [], totals: null });
    const api = createInvoiceReviewApi(fetchFn, BASE);
    await api.decideLine(INVOICE_ID, lineId(1), { ...lineBody, note: ' Rate card 2025 ' });
    await api.decideLines(INVOICE_ID, { ...linesBody, note: 'Same entry twice' });
    expect(fetchFn.mock.calls[0][0]).toBe(routes.decideLine(BASE, INVOICE_ID, lineId(1)));
    expect(fetchFn.mock.calls[1][0]).toBe(routes.decideLines(BASE, INVOICE_ID));
    expect(bodyOf(fetchFn, 0)).toEqual({ ...lineBody, note: ' Rate card 2025 ' });
    expect(bodyOf(fetchFn, 1)).toEqual({ ...linesBody, note: 'Same entry twice' });
    for (const [, blank] of BLANKS) {
      await api.decideLine(INVOICE_ID, lineId(1), { ...lineBody, note: blank });
      await api.decideLines(INVOICE_ID, { ...linesBody, note: blank });
    }
    fetchFn.mock.calls.slice(2).forEach((_, i) => {
      const b = bodyOf(fetchFn, i + 2);
      expect(Object.keys(b)).not.toContain('note');
    });
    expect(bodyOf(fetchFn, 2)).toEqual(lineBody);
    expect(bodyOf(fetchFn, 3)).toEqual(linesBody);
  });

  it('correctLine and correctHeader, correct and confirm: note as typed; absent when blank', async () => {
    const fetchFn = fetchReturning({ correction: {}, alreadyApplied: false });
    const api = createInvoiceReviewApi(fetchFn, BASE);
    await api.correctLine(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false, note: 'Per the timesheet' });
    await api.correctLine(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true, note: 'Matches the PDF' });
    await api.correctHeader(INVOICE_ID, { field: 'FirmInvoiceNumber', value: 'INV-9', confirm: false, note: 'Typo on the scan' });
    await api.correctHeader(INVOICE_ID, { field: 'Currency', value: null, confirm: true, note: 'Confirmed with the firm' });
    expect(fetchFn.mock.calls[0][0]).toBe(routes.correctLine(BASE, INVOICE_ID, lineId(7)));
    expect(fetchFn.mock.calls[2][0]).toBe(routes.correctHeader(BASE, INVOICE_ID));
    expect(bodyOf(fetchFn, 0)).toEqual({ field: 'Hours', value: '2.5', confirm: false, note: 'Per the timesheet' });
    expect(bodyOf(fetchFn, 1)).toEqual({ field: 'Hours', value: null, confirm: true, note: 'Matches the PDF' });
    expect(bodyOf(fetchFn, 2)).toEqual({ field: 'FirmInvoiceNumber', value: 'INV-9', confirm: false, note: 'Typo on the scan' });
    expect(bodyOf(fetchFn, 3)).toEqual({ field: 'Currency', value: null, confirm: true, note: 'Confirmed with the firm' });
    for (const [, blank] of BLANKS) {
      await api.correctLine(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true, note: blank });
      await api.correctHeader(INVOICE_ID, { field: 'Currency', value: '', confirm: false, note: blank });
    }
    fetchFn.mock.calls.slice(4).forEach((_, i) => expect(Object.keys(bodyOf(fetchFn, i + 4))).toEqual(['field', 'value', 'confirm']));
  });

  it('acceptClean never sends a note (must-not-fire), whatever the caller passes', async () => {
    const fetchFn = fetchReturning({ decisionId: 'd', lines: [], totals: null });
    const api = createInvoiceReviewApi(fetchFn, BASE);
    // The signature has no note; a caller that tried to smuggle one through still sends the decision id only.
    await (api.acceptClean as unknown as (i: string, d: string, note?: string) => Promise<unknown>)(INVOICE_ID, 'd3', 'a note');
    expect(fetchFn.mock.calls[0][0]).toBe(routes.acceptClean(BASE, INVOICE_ID));
    expect(bodyOf(fetchFn)).toEqual({ decisionId: 'd3' });
  });
});
