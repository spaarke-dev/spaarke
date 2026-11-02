import { markSentFailure, markSentOutcome } from '../services/markSentFlow';
import {
  AP_ADDRESS_QUERY,
  BATCH_COLUMNS,
  batchFromExport,
  formatDateTime,
  readApAddress,
  readBatch,
  toBatchInfo,
  unknownBatch,
  type RecordReaderLike,
} from '../services/batch';
import { createSingleFlight } from '../services/singleFlight';
import { createApExportApi, toFailure } from '../services/apExportApi';
import { parseLaunch } from '../services/launch';
import { AuthError, BATCH_ID, batch, failed, ok, problem } from './testKit';

const F = '@OData.Community.Display.V1.FormattedValue';

describe('markSentOutcome', () => {
  it('200 alreadySet:false: marked, with the date', () => {
    const o = markSentOutcome(ok({ sentToApDate: '2026-11-02T09:30:00+00:00', alreadySet: false }), 'en-US', 'UTC');
    expect(o).toEqual({
      kind: 'marked',
      title: 'Marked Sent to AP',
      message: 'The Sent to AP Date was recorded.',
      sentToApDate: 'Nov 2, 2026, 9:30 AM',
      alreadySet: false,
    });
  });

  it('200 alreadySet:true: says the existing date is unchanged', () => {
    const o = markSentOutcome(ok({ sentToApDate: '2026-11-01T08:00:00+00:00', alreadySet: true }), 'en-US', 'UTC');
    expect(o.kind === 'marked' && o.alreadySet).toBe(true);
    expect(o.kind === 'marked' && o.title).toBe('Already marked Sent to AP');
    expect(o.kind === 'marked' && o.sentToApDate).toBe('Nov 1, 2026, 8:00 AM');
  });

  it('409 batch_not_completed: not retryable, nothing changed', () => {
    const o = markSentOutcome(failed(problem(409, 'ebilling.export.batch_not_completed')));
    expect(o).toMatchObject({ kind: 'failed', retryable: false, code: 'ebilling.export.batch_not_completed' });
    expect(o.kind === 'failed' && o.message).toBe('This batch is not Completed, so it cannot be marked Sent to AP. Nothing was changed.');
  });

  it('uniform 404 reads "This export batch isn\'t available to you."', () => {
    const o = markSentOutcome(failed(problem(404, null)));
    expect(o).toMatchObject({ kind: 'failed', message: "This export batch isn't available to you.", retryable: false });
  });

  it('503 unavailable is retryable; the kill switch, a sign-in failure and an unknown refusal are not', () => {
    expect(markSentFailure(toFailure(problem(503, 'ebilling.export.unavailable'))).retryable).toBe(true);
    expect(markSentFailure(toFailure(problem(503, 'ebilling.disabled'))).retryable).toBe(false);
    expect(markSentFailure(toFailure(new AuthError('x', 'auth_exhausted'))).retryable).toBe(false);
    const unknown = markSentFailure(toFailure(problem(500, 'x.y', { detail: 'Boom.' })));
    expect(unknown.retryable).toBe(false);
    expect(unknown.message).toBe('Boom. (x.y) Nothing was changed.');
  });

  it('no answer: safe to repeat, because the server never overwrites a date', () => {
    expect(markSentFailure(toFailure(new TypeError('Failed to fetch'))).retryable).toBe(true);
  });
});

describe('the API client: Email to AP and Mark Sent to AP', () => {
  const BASE = 'https://bff.example.test';
  const fetchReturning = (body: unknown) => jest.fn().mockResolvedValue({ json: async () => body });

  it('emailBatch posts the CC list and the message to the batch route', async () => {
    const fetchFn = fetchReturning({ sentTo: 'ap@example.com' });
    await createApExportApi(fetchFn, `${BASE}/`).emailBatch(BATCH_ID.toUpperCase(), { cc: ['a@x.com'], message: 'Please pay.' });
    expect(fetchFn).toHaveBeenCalledWith(`${BASE}/api/finance/export-batches/${BATCH_ID}/email`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ cc: ['a@x.com'], message: 'Please pay.' }),
    });
  });

  it('emailBatch omits an empty message', async () => {
    const fetchFn = fetchReturning({});
    await createApExportApi(fetchFn, BASE).emailBatch(BATCH_ID, { cc: [], message: '' });
    expect(JSON.parse(fetchFn.mock.calls[0][1].body)).toEqual({ cc: [] });
  });

  it('markSentToAp posts to the batch route with no body', async () => {
    const fetchFn = fetchReturning({ sentToApDate: 'd', alreadySet: false });
    const r = await createApExportApi(fetchFn, BASE).markSentToAp(BATCH_ID);
    expect(fetchFn).toHaveBeenCalledWith(`${BASE}/api/finance/export-batches/${BATCH_ID}/mark-sent-to-ap`, { method: 'POST' });
    expect(r).toEqual({ ok: true, value: { sentToApDate: 'd', alreadySet: false } });
  });

  it('a refusal comes back as a failure with its stable code', async () => {
    const fetchFn = jest.fn().mockRejectedValue(problem(409, 'ebilling.export.ap_email_recipient_monitored'));
    const r = await createApExportApi(fetchFn, BASE).emailBatch(BATCH_ID, { cc: [], message: '' });
    expect(r.ok).toBe(false);
    expect(!r.ok && r.failure.errorCode).toBe('ebilling.export.ap_email_recipient_monitored');
  });
});

describe('batch display read', () => {
  const entity = {
    sprk_name: 'AP-2026-11-01-001',
    sprk_exportstatus: 100000001,
    sprk_invoicecount: 2,
    sprk_totalamount: 1500.5,
    [`sprk_totalamount${F}`]: '$1,500.50',
    sprk_senttoapdate: '2026-11-02T09:30:00Z',
  };

  it('reads the batch columns and maps a Completed batch with a held date', async () => {
    const reads: Array<[string, string, string]> = [];
    const webApi = {
      retrieveRecord: async (e: string, id: string, o: string) => {
        reads.push([e, id, o]);
        return entity;
      },
      retrieveMultipleRecords: async () => ({ entities: [] }),
    } as RecordReaderLike;
    const r = await readBatch(webApi, BATCH_ID);
    expect(reads).toEqual([['sprk_billingexport', BATCH_ID, `?$select=${BATCH_COLUMNS.join(',')}`]]);
    expect(r).toEqual({
      readFailed: false,
      batch: {
        batchId: BATCH_ID,
        name: 'AP-2026-11-01-001',
        completed: true,
        invoiceCount: 2,
        totalLabel: '$1,500.50',
        sentToApDate: '2026-11-02T09:30:00Z',
      },
    });
  });

  it('a Generating or Failed batch is not completed; an empty date is null; a missing status is unknown', () => {
    expect(toBatchInfo(BATCH_ID, { sprk_exportstatus: 100000000 }).completed).toBe(false);
    expect(toBatchInfo(BATCH_ID, { sprk_exportstatus: 100000002 }).completed).toBe(false);
    expect(toBatchInfo(BATCH_ID, { sprk_senttoapdate: null }).sentToApDate).toBeNull();
    expect(toBatchInfo(BATCH_ID, {}).completed).toBeNull();
  });

  it('a failed read leaves the batch unknown, never "not completed"', async () => {
    const webApi = {
      retrieveRecord: async () => {
        throw new Error('no');
      },
      retrieveMultipleRecords: async () => ({ entities: [] }),
    } as RecordReaderLike;
    expect(await readBatch(webApi, BATCH_ID)).toEqual({ batch: unknownBatch(BATCH_ID), readFailed: true });
    expect(await readBatch(undefined, BATCH_ID)).toEqual({ batch: unknownBatch(BATCH_ID), readFailed: true });
    expect(unknownBatch(BATCH_ID).completed).toBeNull();
  });

  it('a just-exported batch is Completed with no Sent to AP Date', () => {
    expect(batchFromExport(batch(), '$1,500.50')).toEqual({
      batchId: BATCH_ID,
      name: 'AP-2026-11-01-001',
      completed: true,
      invoiceCount: 2,
      totalLabel: '$1,500.50',
      sentToApDate: null,
    });
  });

  it('formats a date for the locale, and returns text that is not a date unchanged', () => {
    expect(formatDateTime('2026-11-02T09:30:00+00:00', 'en-US', 'UTC')).toBe('Nov 2, 2026, 9:30 AM');
    expect(formatDateTime('not a date')).toBe('not a date');
    expect(formatDateTime(null)).toBe('');
  });
});

describe('AP address read', () => {
  const reader = (entities: Array<Record<string, unknown>> | 'throw') => ({
    retrieveMultipleRecords: jest.fn(async () => {
      if (entities === 'throw') throw new Error('no read');
      return { entities };
    }),
  });

  it('reads the active settings row', async () => {
    const w = reader([{ sprk_apexportemail: ' ap@example.com ' }]);
    expect(await readApAddress(w)).toEqual({ kind: 'address', address: 'ap@example.com' });
    expect(w.retrieveMultipleRecords).toHaveBeenCalledWith('sprk_ebillingsetting', AP_ADDRESS_QUERY);
    expect(AP_ADDRESS_QUERY).toContain('statecode eq 0');
  });

  it('no row, or an empty column, is "none"', async () => {
    expect(await readApAddress(reader([]))).toEqual({ kind: 'none' });
    expect(await readApAddress(reader([{ sprk_apexportemail: '' }]))).toEqual({ kind: 'none' });
    expect(await readApAddress(reader([{ sprk_apexportemail: null }]))).toEqual({ kind: 'none' });
  });

  it('a failed read is "unknown", not "none" (the server still answers)', async () => {
    expect(await readApAddress(reader('throw'))).toEqual({ kind: 'unknown' });
    expect(await readApAddress(undefined)).toEqual({ kind: 'unknown' });
  });
});

describe('parseLaunch: the batch modes', () => {
  it('reads the mode and the batch id', () => {
    expect(parseLaunch({ mode: 'email', batch: BATCH_ID })).toEqual({ ok: true, mode: 'email', batchId: BATCH_ID });
    expect(parseLaunch({ mode: 'MarkSent', batch: `{${BATCH_ID.toUpperCase()}}` })).toEqual({ ok: true, mode: 'marksent', batchId: BATCH_ID });
  });

  it('refuses a missing or malformed batch id', () => {
    expect(parseLaunch({ mode: 'email' }).ok).toBe(false);
    expect(parseLaunch({ mode: 'marksent', batch: 'nope' }).ok).toBe(false);
    expect(parseLaunch({ mode: 'email', ids: BATCH_ID }).ok).toBe(false);
  });
});

describe('createSingleFlight', () => {
  it('refuses a call while one runs and accepts the next after it ends', async () => {
    const flight = createSingleFlight();
    let release: () => void = () => undefined;
    const fn = jest.fn(() => new Promise<string>(resolve => (release = () => resolve('done'))));
    const first = flight.run(fn);
    expect(flight.busy).toBe(true);
    expect(await flight.run(fn)).toEqual({ ran: false });
    release();
    expect(await first).toEqual({ ran: true, value: 'done' });
    expect(flight.busy).toBe(false);
    const second = flight.run(async () => 'again');
    expect(await second).toEqual({ ran: true, value: 'again' });
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it('frees the flight when the call throws', async () => {
    const flight = createSingleFlight();
    await expect(
      flight.run(async () => {
        throw new Error('x');
      })
    ).rejects.toThrow('x');
    expect(flight.busy).toBe(false);
  });
});
