import { classifyMarkPaid, runMarkPaid, statusLabel, summarizeMarkPaid } from '../services/markPaidFlow';
import type { ApiResult, MarkPaidResponse } from '../services/apExportApi';
import { AuthError, INV_A, INV_B, INV_C, INV_D, failed, ok, problem } from './testKit';

const paid = (id: string): ApiResult<MarkPaidResponse> => ok({ invoiceId: id, status: 'Paid' });

describe('classifyMarkPaid', () => {
  it('200 is Paid', () => {
    expect(classifyMarkPaid(INV_A, paid(INV_A))).toMatchObject({ invoiceId: INV_A, kind: 'paid', currentStatus: 'Paid', stopsRun: false });
  });

  it('409 transition_not_allowed names the current status', () => {
    const o = classifyMarkPaid(INV_A, failed(problem(409, 'invoice.transition_not_allowed', { currentStatus: 'Approved' })));
    expect(o).toMatchObject({ kind: 'refused', currentStatus: 'Approved', errorCode: 'invoice.transition_not_allowed', stopsRun: false });
    expect(o.message).toBe('Not changed: the invoice is Approved, and only a Sent to AP invoice can be marked Paid.');
  });

  it('409 on an invoice already Paid says so', () => {
    const o = classifyMarkPaid(INV_A, failed(problem(409, 'invoice.transition_not_allowed', { currentStatus: 'Paid' })));
    expect(o.kind).toBe('refused');
    expect(o.message).toBe('Already Paid. Not changed.');
  });

  it('409 status_unknown and concurrent_change are not changed', () => {
    expect(classifyMarkPaid(INV_A, failed(problem(409, 'invoice.status_unknown'))).kind).toBe('refused');
    const concurrent = classifyMarkPaid(INV_A, failed(problem(409, 'invoice.concurrent_change', { currentStatus: 'SentToAp' })));
    expect(concurrent.kind).toBe('failed');
    expect(concurrent.message).toContain('try again');
  });

  it('404 (the uniform record-not-found answer) is not available', () => {
    const o = classifyMarkPaid(INV_A, failed(problem(404, null, { reasonCode: 'sdap.access.deny.record_unavailable' })));
    expect(o).toMatchObject({ kind: 'unavailable', stopsRun: false });
  });

  it('503 payment_unavailable shows the server words', () => {
    const o = classifyMarkPaid(INV_A, failed(problem(503, 'ebilling.invoice.payment_unavailable', { detail: 'Check the status and try again.' })));
    expect(o).toMatchObject({ kind: 'failed', stopsRun: false, message: 'Check the status and try again. (ebilling.invoice.payment_unavailable)' });
  });

  it('a sign-in failure and ebilling.disabled stop the run', () => {
    expect(classifyMarkPaid(INV_A, failed(new AuthError('x'))).stopsRun).toBe(true);
    expect(classifyMarkPaid(INV_A, failed(problem(503, 'ebilling.disabled'))).stopsRun).toBe(true);
  });

  it('labels the lifecycle status names', () => {
    expect(statusLabel('SentToAp')).toBe('Sent to AP');
    expect(statusLabel('ReturnedToFirm')).toBe('Returned to Firm');
    expect(statusLabel('Something')).toBe('Something');
    expect(statusLabel(null)).toBe('unknown');
  });
});

describe('runMarkPaid', () => {
  it('a partial run: each invoice gets its own answer, in order, one call at a time', async () => {
    const answers: Record<string, ApiResult<MarkPaidResponse>> = {
      [INV_A]: paid(INV_A),
      [INV_B]: failed(problem(409, 'invoice.transition_not_allowed', { currentStatus: 'Approved' })),
      [INV_C]: failed(problem(404, null)),
      [INV_D]: paid(INV_D),
    };
    let inFlight = 0;
    let maxInFlight = 0;
    const calls: string[] = [];
    const markPaid = jest.fn(async (id: string) => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      calls.push(id);
      await Promise.resolve();
      inFlight--;
      return answers[id];
    });
    const progress: Array<[string, number]> = [];

    const outcomes = await runMarkPaid([INV_A, INV_B, INV_C, INV_D], markPaid, (o, i) => progress.push([o.kind, i]));

    expect(calls).toEqual([INV_A, INV_B, INV_C, INV_D]);
    expect(maxInFlight).toBe(1);
    expect(outcomes.map(o => o.kind)).toEqual(['paid', 'refused', 'unavailable', 'paid']);
    expect(outcomes[1].currentStatus).toBe('Approved');
    expect(progress).toEqual([
      ['paid', 0],
      ['refused', 1],
      ['unavailable', 2],
      ['paid', 3],
    ]);
    expect(summarizeMarkPaid(outcomes)).toEqual({
      total: 4,
      paid: 2,
      notPaid: 2,
      text: '2 of 4 marked Paid; 2 not changed. See each invoice below.',
    });
  });

  it('stops at ebilling.disabled: the rest are not attempted and not called', async () => {
    const markPaid = jest
      .fn()
      .mockResolvedValueOnce(paid(INV_A))
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.disabled')));
    const outcomes = await runMarkPaid([INV_A, INV_B, INV_C], markPaid);
    expect(markPaid).toHaveBeenCalledTimes(2);
    expect(outcomes.map(o => o.kind)).toEqual(['paid', 'failed', 'skipped']);
    expect(outcomes[2]).toMatchObject({ invoiceId: INV_C, message: 'Not attempted.' });
  });

  it('a call that rejects counts as failed and the run goes on', async () => {
    const markPaid = jest.fn().mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(paid(INV_B));
    const outcomes = await runMarkPaid([INV_A, INV_B], markPaid);
    expect(outcomes.map(o => o.kind)).toEqual(['failed', 'paid']);
    expect(outcomes[0].message).toBe('boom');
  });

  it('all paid', async () => {
    const outcomes = await runMarkPaid([INV_A], async id => paid(id));
    expect(summarizeMarkPaid(outcomes).text).toBe('1 of 1 marked Paid.');
  });
});
