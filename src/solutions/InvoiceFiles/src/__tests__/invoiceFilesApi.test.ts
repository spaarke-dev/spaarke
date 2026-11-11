import {
  contentUrl,
  createInvoiceFilesApi,
  fileFailureMessage,
  listFailureMessage,
  listUrl,
  toFailure,
} from '../services/invoiceFilesApi';
import { ApiError, AuthError, INVOICE_ID, PDF_ID, file, problem } from './testKit';

const BASE = 'https://bff.example.com/';

function jsonResponse(body: unknown) {
  return { json: async () => body, blob: async () => new Blob(['x']) } as never;
}

describe('URLs', () => {
  it('lists through the invoice route and serves bytes only through the content route', () => {
    expect(listUrl(BASE, INVOICE_ID.toUpperCase())).toBe(`https://bff.example.com/api/finance/invoices/${INVOICE_ID}/files`);
    expect(contentUrl(BASE, PDF_ID)).toBe(`https://bff.example.com/api/finance/files/${PDF_ID}/content`);
  });
});

describe('listFiles', () => {
  it('returns the list the server sent, in the server order', async () => {
    const items = [file(), file({ fileId: 'b', purpose: 'Supporting' })];
    const fetchFn = jest.fn().mockResolvedValue(jsonResponse(items));
    const result = await createInvoiceFilesApi(fetchFn, BASE).listFiles(INVOICE_ID);

    expect(result).toEqual({ ok: true, value: items });
    expect(fetchFn).toHaveBeenCalledWith(listUrl(BASE, INVOICE_ID), { method: 'GET' });
  });

  it('an empty list is a success (no files), not a failure', async () => {
    const result = await createInvoiceFilesApi(jest.fn().mockResolvedValue(jsonResponse([])), BASE).listFiles(INVOICE_ID);
    expect(result).toEqual({ ok: true, value: [] });
  });

  it('a body that is not a list is a failure, never an empty list', async () => {
    const result = await createInvoiceFilesApi(jest.fn().mockResolvedValue(jsonResponse({ message: 'oops' })), BASE).listFiles(INVOICE_ID);
    expect(result.ok).toBe(false);
  });

  it('a thrown ApiError keeps its status and code; a network error is status 0', async () => {
    const notFound = await createInvoiceFilesApi(jest.fn().mockRejectedValue(problem(404, null)), BASE).listFiles(INVOICE_ID);
    expect(notFound).toMatchObject({ ok: false, failure: { status: 404 } });

    const disabled = await createInvoiceFilesApi(jest.fn().mockRejectedValue(problem(503, 'ebilling.disabled')), BASE).listFiles(INVOICE_ID);
    expect(disabled).toMatchObject({ ok: false, failure: { status: 503, errorCode: 'ebilling.disabled' } });

    const network = await createInvoiceFilesApi(jest.fn().mockRejectedValue(new TypeError('Failed to fetch')), BASE).listFiles(INVOICE_ID);
    expect(network).toMatchObject({ ok: false, failure: { status: 0, signInFailed: false } });
  });

  it('an exhausted sign-in is reported as a sign-in failure', () => {
    expect(toFailure(new AuthError('no token', 'token_unavailable' as never)).signInFailed).toBe(true);
  });
});

describe('fetchFile', () => {
  it('reads the bytes from the content route', async () => {
    const fetchFn = jest.fn().mockResolvedValue(jsonResponse(null));
    const result = await createInvoiceFilesApi(fetchFn, BASE).fetchFile(PDF_ID);

    expect(result.ok).toBe(true);
    expect(fetchFn).toHaveBeenCalledWith(contentUrl(BASE, PDF_ID), { method: 'GET' });
  });

  it('a 409 is a failure with its status (the scan has no verdict)', async () => {
    const result = await createInvoiceFilesApi(jest.fn().mockRejectedValue(problem(409, 'file_scan_pending')), BASE).fetchFile(PDF_ID);
    expect(result).toMatchObject({ ok: false, failure: { status: 409, errorCode: 'file_scan_pending' } });
  });
});

describe('messages', () => {
  const base = { status: 0, errorCode: null, detail: null, signInFailed: false };

  it('the list: the uniform 404, the flag, 503, sign-in and network each say their own thing', () => {
    expect(listFailureMessage({ ...base, status: 404 })).toMatch(/may not have access/);
    expect(listFailureMessage({ ...base, status: 503, errorCode: 'ebilling.disabled' })).toMatch(/not enabled/);
    expect(listFailureMessage({ ...base, status: 503 })).toMatch(/temporarily unavailable/);
    expect(listFailureMessage({ ...base, signInFailed: true })).toMatch(/Sign-in/);
    expect(listFailureMessage(base)).toMatch(/connection/);
  });

  it('a file: scan pending, not found, store unavailable', () => {
    expect(fileFailureMessage({ ...base, status: 409, errorCode: 'file_scan_pending' })).toMatch(/scanned for malware/);
    expect(fileFailureMessage({ ...base, status: 404 })).toMatch(/not available/);
    expect(fileFailureMessage({ ...base, status: 503, errorCode: 'file_store_unavailable' })).toMatch(/file store is unavailable/);
  });

  it('ApiError is the thrown type (the failure shape depends on it)', () => {
    expect(problem(404, null)).toBeInstanceOf(ApiError);
  });
});
