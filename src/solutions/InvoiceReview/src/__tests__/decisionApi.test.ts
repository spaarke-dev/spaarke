import {
  createInvoiceDecisionApi,
  decisionFailureMessage,
  decisionRoutes,
  isDecisionIncomplete,
  newAdjustmentId,
} from '../services/decisionApi';
import { toFailure } from '../services/invoiceReviewApi';
import { INVOICE_ID, SUBMISSION_ID, approval, blockedSubmission, invoiceContext, lineId, problem } from './testKit';

const BASE = 'https://bff.example.test/';

const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
const fetchReturning = (body: unknown) => jest.fn(async (_url: string, _init?: RequestInit) => okResponse(body) as never);

describe('decision routes (tasks 102 to 107)', () => {
  it('builds every route, ids lower-cased and without a doubled slash', () => {
    const inv = INVOICE_ID.toUpperCase();
    const root = `https://bff.example.test/api/finance/invoices/${INVOICE_ID}`;
    expect(decisionRoutes.approval(BASE, inv)).toBe(`${root}/approval`);
    expect(decisionRoutes.approve(BASE, inv)).toBe(`${root}/approve`);
    expect(decisionRoutes.reject(BASE, inv)).toBe(`${root}/reject`);
    expect(decisionRoutes.returnToFirm(BASE, inv)).toBe(`${root}/return`);
    expect(decisionRoutes.adjustmentRules(BASE, inv)).toBe(`${root}/adjustment-rules`);
    expect(decisionRoutes.adjustments(BASE, inv)).toBe(`${root}/adjustments`);
    expect(decisionRoutes.adjustment(BASE, inv, lineId(9))).toBe(`${root}/adjustments/${lineId(9)}`);
    expect(decisionRoutes.context(BASE, inv)).toBe(`${root}/context`);
    expect(decisionRoutes.relatedWork(BASE, inv, lineId(2))).toBe(`${root}/lines/${lineId(2)}/related-work`);
    expect(decisionRoutes.blocked(BASE, SUBMISSION_ID)).toBe(`https://bff.example.test/api/finance/submissions/${SUBMISSION_ID}/blocked`);
    expect(decisionRoutes.override(BASE, SUBMISSION_ID)).toBe(`https://bff.example.test/api/finance/submissions/${SUBMISSION_ID}/override`);
    expect(decisionRoutes.returnBlocked(BASE, SUBMISSION_ID)).toBe(`https://bff.example.test/api/finance/submissions/${SUBMISSION_ID}/return`);
  });
});

describe('createInvoiceDecisionApi', () => {
  it('reads the approval state and refuses a body that is not one', async () => {
    const good = createInvoiceDecisionApi(fetchReturning(approval()), BASE);
    expect(await good.getApproval(INVOICE_ID)).toEqual({ ok: true, value: approval() });
    const bad = await createInvoiceDecisionApi(fetchReturning({ status: 5 }), BASE).getApproval(INVOICE_ID);
    expect(bad.ok).toBe(false);
  });

  it('approves with an empty body and sends a reason only to reject and return', async () => {
    const fetchFn = fetchReturning({ outcome: 'approved' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.approve(INVOICE_ID);
    await api.reject(INVOICE_ID, 'Not payable');
    await api.returnToFirm(INVOICE_ID, 'Please correct the rates');
    const sent = fetchFn.mock.calls.map(c => [c[0], c[1]?.method, JSON.parse(String(c[1]?.body))]);
    expect(sent).toEqual([
      [decisionRoutes.approve(BASE, INVOICE_ID), 'POST', {}],
      [decisionRoutes.reject(BASE, INVOICE_ID), 'POST', { reason: 'Not payable' }],
      [decisionRoutes.returnToFirm(BASE, INVOICE_ID), 'POST', { reason: 'Please correct the rates' }],
    ]);
  });

  it('adds an adjustment with the client id, method, value, reason and rule only, and removes one with DELETE', async () => {
    const fetchFn = fetchReturning({ adjustment: null });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.addAdjustment(INVOICE_ID, { adjustmentId: 'a1', method: 'percent', value: 5, reason: 'Courtesy', ruleId: null });
    await api.removeAdjustment(INVOICE_ID, lineId(9));
    expect(JSON.parse(String(fetchFn.mock.calls[0][1]?.body))).toEqual({ adjustmentId: 'a1', method: 'percent', value: 5, reason: 'Courtesy', ruleId: null });
    expect(fetchFn.mock.calls[1][1]?.method).toBe('DELETE');
    expect(fetchFn.mock.calls[1][0]).toBe(decisionRoutes.adjustment(BASE, INVOICE_ID, lineId(9)));
  });

  it('overrides and returns a blocked submission with the reason only', async () => {
    const fetchFn = fetchReturning({ outcome: 'Overridden' });
    const api = createInvoiceDecisionApi(fetchFn, BASE);
    await api.overrideBlocked(SUBMISSION_ID, 'Known duplicate resubmission');
    await api.returnBlocked(SUBMISSION_ID, 'Please resubmit');
    expect(fetchFn.mock.calls.map(c => [c[0], JSON.parse(String(c[1]?.body))])).toEqual([
      [decisionRoutes.override(BASE, SUBMISSION_ID), { reason: 'Known duplicate resubmission' }],
      [decisionRoutes.returnBlocked(BASE, SUBMISSION_ID), { reason: 'Please resubmit' }],
    ]);
  });

  it('reads the blocked submission, the context and the related work, refusing a body of the wrong shape', async () => {
    expect((await createInvoiceDecisionApi(fetchReturning(blockedSubmission()), BASE).getBlocked(SUBMISSION_ID)).ok).toBe(true);
    expect((await createInvoiceDecisionApi(fetchReturning({ state: 'Blocked' }), BASE).getBlocked(SUBMISSION_ID)).ok).toBe(false);
    expect((await createInvoiceDecisionApi(fetchReturning(invoiceContext()), BASE).getContext(INVOICE_ID)).ok).toBe(true);
    expect((await createInvoiceDecisionApi(fetchReturning({}), BASE).getContext(INVOICE_ID)).ok).toBe(false);
    expect((await createInvoiceDecisionApi(fetchReturning({ status: 'empty', items: [] }), BASE).getRelatedWork(INVOICE_ID, lineId(1))).ok).toBe(true);
    expect((await createInvoiceDecisionApi(fetchReturning({ status: 'empty' }), BASE).getRelatedWork(INVOICE_ID, lineId(1))).ok).toBe(false);
  });

  it('turns a thrown ProblemDetails into a failure that keeps the status, the code and the detail', async () => {
    const fetchFn = jest.fn(async () => {
      throw problem(409, 'ebilling.approval.open_lines', { detail: '2 lines still need a decision.' });
    });
    const r = await createInvoiceDecisionApi(fetchFn as never, BASE).approve(INVOICE_ID);
    expect(r).toEqual({ ok: false, failure: expect.objectContaining({ status: 409, errorCode: 'ebilling.approval.open_lines', detail: '2 lines still need a decision.' }) });
  });
});

describe('decisionFailureMessage', () => {
  const fail = (status: number, code: string | null, detail?: string) => toFailure(problem(status, code, detail ? { detail } : {}));

  it('shows the server detail with its stable code in brackets (a 403 or 409 is shown with its code)', () => {
    expect(decisionFailureMessage(fail(403, 'ebilling.approval.not_eligible', 'You are not an approver for this invoice.'))).toBe(
      'You are not an approver for this invoice. (ebilling.approval.not_eligible)'
    );
    expect(decisionFailureMessage(fail(409, 'ebilling.approval.open_lines', '2 lines still need a decision.'))).toBe('2 lines still need a decision. (ebilling.approval.open_lines)');
  });

  it('falls back to a fixed sentence per status when the server sent no detail, still with the code', () => {
    expect(decisionFailureMessage(fail(403, 'x.y'))).toBe('You do not have permission to do this. (x.y)');
    expect(decisionFailureMessage(fail(404, null))).toBe('This invoice or record could not be found.');
    expect(decisionFailureMessage(fail(409, 'x.z'))).toContain('(x.z)');
    expect(decisionFailureMessage(fail(500, null))).toBe('The request could not be completed. Try again in a moment.');
  });

  it('says a 503 that saved part of the change must be repeated, and only for the incomplete codes', () => {
    const incomplete = fail(503, 'ebilling.approval.incomplete', 'The approval was saved but the invoice did not close.');
    expect(isDecisionIncomplete(incomplete)).toBe(true);
    expect(decisionFailureMessage(incomplete)).toContain('It was saved in part: repeat it to finish.');
    const other = fail(503, 'ebilling.something.else', 'Busy.');
    expect(isDecisionIncomplete(other)).toBe(false);
    expect(decisionFailureMessage(other)).not.toContain('repeat');
    for (const code of ['ebilling.invoice.decision_incomplete', 'ebilling.adjustment.incomplete', 'ebilling.blocked.unavailable']) {
      expect(isDecisionIncomplete(fail(503, code))).toBe(true);
    }
  });

  it('names the disabled switch and never shows transport text', () => {
    expect(decisionFailureMessage(fail(503, 'ebilling.disabled'))).toBe('eBilling is not enabled in this environment.');
    const network = toFailure(new TypeError('Failed to fetch'));
    expect(decisionFailureMessage(network)).toBe('The request could not be sent. Check your connection and retry.');
    expect(decisionFailureMessage(network)).not.toContain('Failed to fetch');
  });
});

describe('newAdjustmentId', () => {
  it('returns a different uuid-shaped id each time', () => {
    const a = newAdjustmentId();
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    expect(newAdjustmentId()).not.toBe(a);
  });
});
