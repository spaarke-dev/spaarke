import {
  batchFileUrl,
  createApExportApi,
  defaultFileName,
  describeFailure,
  downloadableFormats,
  fileNameOf,
  toFailure,
} from '../services/apExportApi';
import { AuthError, BATCH_ID, INV_A, INV_B, batch, problem } from './testKit';

const BASE = 'https://bff.example.test';

function fetchReturning(body: unknown, headers: Record<string, string> = {}) {
  return jest.fn().mockResolvedValue({
    json: async () => body,
    blob: async () => ({ size: 3, body }),
    headers: { get: (name: string) => headers[name] ?? null },
  });
}

describe('toFailure', () => {
  it('keeps the per-invoice refusals of a 400 selection_refused, lower-casing the ids', () => {
    const f = toFailure(
      problem(400, 'ebilling.export.selection_refused', {
        invoices: [
          { invoiceId: INV_A.toUpperCase(), reason: 'invoice_not_approved' },
          { invoiceId: INV_B, reason: 'invoice_already_exported' },
          { reason: 'no id' },
        ],
      })
    );
    expect(f.status).toBe(400);
    expect(f.errorCode).toBe('ebilling.export.selection_refused');
    expect(f.invoices).toEqual([
      { invoiceId: INV_A, reason: 'invoice_not_approved' },
      { invoiceId: INV_B, reason: 'invoice_already_exported' },
    ]);
  });

  it('keeps the currentStatus of a Mark Paid 409', () => {
    const f = toFailure(problem(409, 'invoice.transition_not_allowed', { currentStatus: 'Approved', detail: 'Only Sent to AP.' }));
    expect(f.currentStatus).toBe('Approved');
    expect(f.detail).toBe('Only Sent to AP.');
    expect(describeFailure(f)).toBe('Only Sent to AP. (invoice.transition_not_allowed)');
  });

  it('keeps the batch id of an export 500', () => {
    expect(toFailure(problem(500, 'ebilling.export.transition_incomplete', { batchId: BATCH_ID })).batchId).toBe(BATCH_ID);
  });

  it('a sign-in failure has no status and is flagged', () => {
    const f = toFailure(new AuthError('no token', 'auth_exhausted'));
    expect(f.status).toBe(0);
    expect(f.signInFailed).toBe(true);
    expect(f.invoices).toEqual([]);
  });

  it('a network error is status 0, not a sign-in failure', () => {
    const f = toFailure(new TypeError('Failed to fetch'));
    expect(f.status).toBe(0);
    expect(f.signInFailed).toBe(false);
    expect(f.detail).toBe('Failed to fetch');
  });
});

describe('download URL and file name', () => {
  it('builds 151a route from the base without a trailing slash, the id lower case', () => {
    expect(batchFileUrl(`${BASE}/`, BATCH_ID.toUpperCase(), 'csv')).toBe(`${BASE}/api/finance/export-batches/${BATCH_ID}/files/csv`);
    expect(batchFileUrl(BASE, BATCH_ID, 'json')).toBe(`${BASE}/api/finance/export-batches/${BATCH_ID}/files/json`);
  });

  it('uses the Content-Disposition name when the browser exposes it', () => {
    expect(fileNameOf('attachment; filename=ap-export-x.csv', BATCH_ID, 'csv')).toBe('ap-export-x.csv');
    expect(fileNameOf('attachment; filename="quoted name.json"', BATCH_ID, 'json')).toBe('quoted name.json');
    expect(fileNameOf("attachment; filename*=UTF-8''a%20b.csv", BATCH_ID, 'csv')).toBe('a b.csv');
  });

  it("falls back to the server's own name, ap-export-{batchId}.{format}", () => {
    expect(fileNameOf(null, BATCH_ID.toUpperCase(), 'csv')).toBe(`ap-export-${BATCH_ID}.csv`);
    expect(fileNameOf('inline', BATCH_ID, 'json')).toBe(defaultFileName(BATCH_ID, 'json'));
  });

  it('offers only the formats the batch has a file for, csv first', () => {
    expect(downloadableFormats(batch())).toEqual(['csv', 'json']);
    expect(downloadableFormats(batch({ files: [{ fileId: 'f', format: 'JSON', name: 'n', contentType: 'c' }] }))).toEqual(['json']);
    expect(downloadableFormats(batch({ files: [] }))).toEqual([]);
  });
});

describe('createApExportApi', () => {
  it('createBatch posts the selection to the export route (both formats: no formats field)', async () => {
    const fetchFn = fetchReturning(batch());
    const result = await createApExportApi(fetchFn, `${BASE}/`).createBatch([INV_A, INV_B]);
    expect(result).toEqual({ ok: true, value: batch() });
    expect(fetchFn).toHaveBeenCalledWith(`${BASE}/api/finance/export-batches`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ invoiceIds: [INV_A, INV_B] }),
    });
  });

  it('createBatch turns a thrown 400 into a failure with its refusals', async () => {
    const fetchFn = jest.fn().mockRejectedValue(
      problem(400, 'ebilling.export.selection_refused', { invoices: [{ invoiceId: INV_A, reason: 'invoice_not_found' }] })
    );
    const result = await createApExportApi(fetchFn, BASE).createBatch([INV_A]);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.invoices).toEqual([{ invoiceId: INV_A, reason: 'invoice_not_found' }]);
  });

  it('markPaid posts to one invoice, no body', async () => {
    const fetchFn = fetchReturning({ invoiceId: INV_A, status: 'Paid' });
    const result = await createApExportApi(fetchFn, BASE).markPaid(INV_A);
    expect(result).toEqual({ ok: true, value: { invoiceId: INV_A, status: 'Paid' } });
    expect(fetchFn).toHaveBeenCalledWith(`${BASE}/api/finance/invoices/${INV_A}/mark-paid`, { method: 'POST' });
  });

  it('downloadFile gets the file route and names the file', async () => {
    const fetchFn = fetchReturning('a,b', {});
    const result = await createApExportApi(fetchFn, BASE).downloadFile(BATCH_ID, 'csv');
    expect(fetchFn).toHaveBeenCalledWith(`${BASE}/api/finance/export-batches/${BATCH_ID}/files/csv`, { method: 'GET' });
    expect(result.ok && result.value.fileName).toBe(`ap-export-${BATCH_ID}.csv`);
  });

  it('downloadFile turns a 409 scan pending into a failure with the server words', async () => {
    const fetchFn = jest.fn().mockRejectedValue(problem(409, 'file_scan_pending', { detail: 'The file is waiting for its malware scan.' }));
    const result = await createApExportApi(fetchFn, BASE).downloadFile(BATCH_ID, 'json');
    expect(result.ok).toBe(false);
    if (!result.ok) expect(describeFailure(result.failure)).toBe('The file is waiting for its malware scan. (file_scan_pending)');
  });
});
