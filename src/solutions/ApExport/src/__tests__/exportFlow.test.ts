import {
  downloadFailureMessage,
  exportFailureMessage,
  exportOutcome,
  isRetryable,
  refusalText,
  withoutRefused,
} from '../services/exportFlow';
import { toFailure } from '../services/apExportApi';
import { AuthError, BATCH_ID, INV_A, INV_B, INV_C, INV_D, batch, failed, ok, problem } from './testKit';

describe('exportOutcome: created', () => {
  it('shows the batch and the formats it can be downloaded in', () => {
    const o = exportOutcome(ok(batch()), [INV_A, INV_B]);
    expect(o.kind).toBe('created');
    if (o.kind === 'created') {
      expect(o.batch.invoiceCount).toBe(2);
      expect(o.batch.totalAmount).toBe(1500.5);
      expect(o.formats).toEqual(['csv', 'json']);
    }
  });

  it('a batch with only a JSON file offers only Download JSON', () => {
    const o = exportOutcome(ok(batch({ files: [{ fileId: 'f', format: 'json', name: 'n', contentType: 'c' }] })), [INV_A]);
    expect(o.kind === 'created' && o.formats).toEqual(['json']);
  });
});

describe('exportOutcome: refused per invoice (400 selection_refused)', () => {
  const refusal = problem(400, 'ebilling.export.selection_refused', {
    invoices: [
      { invoiceId: INV_B.toUpperCase(), reason: 'invoice_not_approved' },
      { invoiceId: INV_D, reason: 'invoice_already_exported' },
    ],
  });

  it('lists each refused invoice with its reason, and the rest of the selection in order', () => {
    const o = exportOutcome(failed(refusal), [INV_A, INV_B, INV_C, INV_D]);
    expect(o.kind).toBe('refused');
    if (o.kind !== 'refused') return;
    expect(o.refused).toEqual([
      { invoiceId: INV_B, reason: 'invoice_not_approved', text: 'Not Approved: only an Approved invoice can be exported.' },
      { invoiceId: INV_D, reason: 'invoice_already_exported', text: 'Already in an export batch.' },
    ]);
    expect(o.remaining).toEqual([INV_A, INV_C]);
    expect(o.message).toBe('2 of 4 selected invoices cannot be exported. Nothing was exported.');
  });

  it('every invoice refused leaves nothing to export', () => {
    const all = problem(400, 'ebilling.export.selection_refused', { invoices: [{ invoiceId: INV_A, reason: 'invoice_not_found' }] });
    const o = exportOutcome(failed(all), [INV_A]);
    expect(o.kind === 'refused' && o.remaining).toEqual([]);
    expect(o.kind === 'refused' && o.message).toBe('1 of 1 selected invoice cannot be exported. Nothing was exported.');
    expect(o.kind === 'refused' && o.refused[0].text).toBe('Not found, or not available to you.');
  });

  it('another 400 (a total out of range) is not a per-invoice refusal', () => {
    const o = exportOutcome(failed(problem(400, 'ebilling.export.batch_total_out_of_range', { detail: 'The total is out of range.' })), [INV_A]);
    expect(o.kind).toBe('failed');
    expect(o.kind === 'failed' && o.message).toBe('The total is out of range. (ebilling.export.batch_total_out_of_range)');
  });

  it('an unknown reason still shows a line', () => {
    expect(refusalText('invoice_on_hold')).toBe('Refused (invoice_on_hold).');
    expect(refusalText('')).toBe('Refused (no reason given).');
  });

  it('withoutRefused compares ids case-insensitively and keeps the selection order', () => {
    expect(withoutRefused([INV_C, INV_A, INV_B], [{ invoiceId: INV_A.toUpperCase() }])).toEqual([INV_C, INV_B]);
    expect(withoutRefused([INV_A], [])).toEqual([INV_A]);
  });
});

describe('exportOutcome: other refusals', () => {
  it('403 not_permitted names no invoice and is not retried', () => {
    const f = toFailure(problem(403, 'ebilling.export.not_permitted'));
    expect(exportFailureMessage(f)).toBe('You may not export this selection. Nothing was exported.');
    expect(isRetryable(f)).toBe(false);
    const o = exportOutcome(failed(problem(403, 'ebilling.export.not_permitted')), [INV_A, INV_B]);
    expect(o.kind === 'failed' && o.message).not.toContain(INV_A);
  });

  it('503 authorization_unavailable and 409 invoice_conflict can be tried again', () => {
    const unavailable = exportOutcome(failed(problem(503, 'ebilling.export.authorization_unavailable')), [INV_A]);
    expect(unavailable).toEqual({
      kind: 'failed',
      message: 'Your rights could not be confirmed. Nothing was exported; try again.',
      retryable: true,
      batchId: null,
    });
    const conflict = exportOutcome(failed(problem(409, 'ebilling.export.invoice_conflict', { batchId: BATCH_ID })), [INV_A]);
    expect(conflict.kind === 'failed' && conflict.retryable).toBe(true);
    expect(conflict.kind === 'failed' && conflict.message).toContain('Nothing was exported');
  });

  it('ebilling.disabled is not retried', () => {
    const o = exportOutcome(failed(problem(503, 'ebilling.disabled')), [INV_A]);
    expect(o).toEqual({ kind: 'failed', message: 'eBilling is turned off in this environment. Nothing was exported.', retryable: false, batchId: null });
  });

  it('a 500 *_incomplete names the batch for an administrator and is not retried', () => {
    const o = exportOutcome(
      failed(problem(500, 'ebilling.export.transition_incomplete', { batchId: BATCH_ID, detail: 'Some invoices were sent.' })),
      [INV_A]
    );
    expect(o.kind === 'failed' && o.message).toBe(
      `Some invoices were sent. (ebilling.export.transition_incomplete) Give an administrator the batch id ${BATCH_ID}.`
    );
    expect(o.kind === 'failed' && o.retryable).toBe(false);
  });

  it('a sign-in failure says so and is not retried', () => {
    const o = exportOutcome(failed(new AuthError('no token', 'auth_exhausted')), [INV_A]);
    expect(o.kind === 'failed' && o.message).toContain('Sign-in could not be verified');
    expect(o.kind === 'failed' && o.retryable).toBe(false);
  });

  it('a network error can be tried again', () => {
    expect(isRetryable(toFailure(new TypeError('Failed to fetch')))).toBe(true);
  });
});

describe('downloadFailureMessage', () => {
  it("shows the server's words with the code", () => {
    expect(downloadFailureMessage(toFailure(problem(404, 'ebilling.export.file_not_available', { detail: 'This batch has no csv file to download.' })))).toBe(
      'This batch has no csv file to download. (ebilling.export.file_not_available)'
    );
  });

  it('a sign-in failure says so', () => {
    expect(downloadFailureMessage(toFailure(new AuthError('x')))).toContain('Sign-in could not be verified');
  });
});
