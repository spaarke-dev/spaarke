/**
 * emailInvoiceSubmission.test.ts (ebilling-r1 task 118)
 *
 * The wire contract of the from-document POST (094 + 118b) and the way every failure shape becomes the screen's
 * `InvoiceSubmissionError`. The server decides; the client only carries the marks and shows the answer.
 */
jest.mock('@spaarke/auth', () => ({ __esModule: true, authenticatedFetch: jest.fn() }));

import { InvoiceSubmissionError } from '@spaarke/ui-components';
import {
  buildFromDocumentBody,
  createEmailInvoiceSubmission,
  toMarkableAttachments,
  toSubmissionError,
  NO_DOCUMENT_REASON,
  SUBMIT_NOT_FOUND_TEXT,
  type SubmitInvoiceFromDocumentInput,
} from '../emailInvoiceSubmission';
import type { IAttachmentItem } from '../../attachments';

const input = (over: Partial<SubmitInvoiceFromDocumentInput> = {}): SubmitInvoiceFromDocumentInput => ({
  communicationId: 'comm-1',
  invoiceDocumentId: 'doc-inv',
  supportingDocumentIds: [],
  record: { kind: 'matter', recordId: 'rec-1' },
  firmId: 'firm-1',
  firmInvoiceNumber: 'INV-1',
  ...over,
});

const item = (over: Partial<IAttachmentItem>): IAttachmentItem =>
  ({
    attachmentId: 'a1',
    name: 'a.pdf',
    documentId: 'd1',
    documentType: undefined,
    ...over,
  }) as unknown as IAttachmentItem;

function jsonResponse(status: number, body?: unknown, rawText?: string): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => {
      if (rawText !== undefined) throw new SyntaxError('bad json');
      return body;
    },
  } as unknown as Response;
}

const dataService = { retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }) };

describe('buildFromDocumentBody', () => {
  it('carries the communication, the invoice document, the record, the firm and the number', () => {
    expect(buildFromDocumentBody(input())).toEqual({
      communicationId: 'comm-1',
      documentId: 'doc-inv',
      coreRecord: { kind: 'matter', id: 'rec-1' },
      firmId: 'firm-1',
      firmInvoiceNumber: 'INV-1',
    });
  });

  it('negative: no supportingDocumentIds key when there are none, and no PO key when blank, and never a mode', () => {
    const body = buildFromDocumentBody(input({ poNumber: '' }));
    expect(body).not.toHaveProperty('supportingDocumentIds');
    expect(body).not.toHaveProperty('poNumber');
    expect(body).not.toHaveProperty('mode');
  });

  it('includes supporting documents and the PO number when given', () => {
    const body = buildFromDocumentBody(input({ supportingDocumentIds: ['s1', 's2'], poNumber: 'PO-1' }));
    expect(body.supportingDocumentIds).toEqual(['s1', 's2']);
    expect(body.poNumber).toBe('PO-1');
  });
});

describe('toMarkableAttachments', () => {
  it('uses the document id as the row id and the profile Invoice flag as a hint', () => {
    const rows = toMarkableAttachments([item({ documentId: 'd1', name: 'x.pdf', documentType: 100000001 as never })]);
    expect(rows).toEqual([{ id: 'd1', fileName: 'x.pdf', suggestedInvoice: true }]);
  });

  it('counts two rows of one document once (case-insensitive)', () => {
    const rows = toMarkableAttachments([
      item({ attachmentId: 'a1', documentId: 'ABC' }),
      item({ attachmentId: 'a2', documentId: 'abc' }),
    ]);
    expect(rows).toHaveLength(1);
  });

  it('negative: an attachment with no document is listed disabled and carries its attachment id, never a document id', () => {
    const rows = toMarkableAttachments([item({ attachmentId: 'a9', documentId: undefined, name: 'orphan.pdf' })]);
    expect(rows).toEqual([{ id: 'attachment:a9', fileName: 'orphan.pdf', disabledReason: NO_DOCUMENT_REASON }]);
  });
});

describe('toSubmissionError', () => {
  it('passes an InvoiceSubmissionError through', () => {
    const e = new InvoiceSubmissionError({ statusCode: 409 });
    expect(toSubmissionError(e)).toBe(e);
  });

  it('reads an ApiError structurally (status + problemDetails), without instanceof', () => {
    const apiError = Object.assign(new Error('x'), {
      name: 'ApiError',
      status: 413,
      problemDetails: { errorCode: 'ebilling.submission.too_large', detail: 'Too big.' },
    });
    const e = toSubmissionError(apiError);
    expect(e.statusCode).toBe(413);
    expect(e.errorCode).toBe('ebilling.submission.too_large');
    expect(e.detail).toBe('Too big.');
  });

  it('negative: anything without a numeric status is an unconfirmed outcome (no status code)', () => {
    expect(toSubmissionError(new Error('network down')).statusCode).toBeUndefined();
    expect(toSubmissionError({ name: 'ApiError', status: 'nope' }).statusCode).toBeUndefined();
    expect(toSubmissionError('string').statusCode).toBeUndefined();
  });
});

describe('createEmailInvoiceSubmission.submit', () => {
  it('POSTs the from-document body as JSON to <base>/api/finance/invoice-submissions/from-document and returns the accepted view', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(202, { submissionId: 's1', trackingNumber: 'TRK-1' }));
    const sub = createEmailInvoiceSubmission({
      authenticatedFetch: fetchMock,
      dataService,
      bffBaseUrl: 'https://bff.example/',
    });

    const accepted = await sub.submit(input({ supportingDocumentIds: ['s1'] }));

    expect(accepted).toEqual({ submissionId: 's1', trackingNumber: 'TRK-1' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('https://bff.example/api/finance/invoice-submissions/from-document');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual(buildFromDocumentBody(input({ supportingDocumentIds: ['s1'] })));
  });

  it('with no base URL the path is relative (the auth fetch adds its own base)', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(202, { submissionId: 's1' }));
    await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService }).submit(input());
    expect(fetchMock.mock.calls[0][0]).toBe('/api/finance/invoice-submissions/from-document');
  });

  it.each([
    [403, 'ebilling.submission.record_forbidden'],
    [409, 'ebilling.submission.document_core_record_mismatch'],
    [413, 'ebilling.submission.too_large'],
    [422, 'ebilling.submission.document_unavailable'],
    [503, 'ebilling.submission.record_unavailable'],
  ])(
    'a returned %i response becomes an InvoiceSubmissionError carrying its status and errorCode',
    async (status, errorCode) => {
      const fetchMock = jest.fn().mockResolvedValue(jsonResponse(status, { errorCode, detail: 'server says' }));
      const sub = createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService });
      const err = await sub.submit(input()).catch(e => e);
      expect(err).toBeInstanceOf(InvoiceSubmissionError);
      expect(err.statusCode).toBe(status);
      expect(err.errorCode).toBe(errorCode);
      expect(err.detail).toBe('server says');
    }
  );

  describe('the uniform 404 (reasonCode record_unavailable, no errorCode)', () => {
    const uniform404 = {
      title: 'Not Found',
      detail: 'The requested record was not found.',
      reasonCode: 'sdap.access.deny.record_unavailable',
    };

    it('a returned 404 says this route\'s wording (attachment or record), not the generic "record was not found"', async () => {
      const fetchMock = jest.fn().mockResolvedValue(jsonResponse(404, uniform404));
      const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
        .submit(input())
        .catch(e => e);
      expect(err).toBeInstanceOf(InvoiceSubmissionError);
      expect(err.statusCode).toBe(404);
      expect(err.errorCode).toBeUndefined();
      expect(err.detail).toBe(SUBMIT_NOT_FOUND_TEXT);
      expect(err.detail).toContain('marked attachments');
    });

    it('a thrown ApiError 404 (what @spaarke/auth does) gets the same wording', async () => {
      const apiError = Object.assign(new Error('x'), { name: 'ApiError', status: 404, problemDetails: uniform404 });
      const fetchMock = jest.fn().mockRejectedValue(apiError);
      const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
        .submit(input())
        .catch(e => e);
      expect(err.statusCode).toBe(404);
      expect(err.detail).toBe(SUBMIT_NOT_FOUND_TEXT);
    });

    it('negative: a 404 that carries an errorCode, or another reasonCode, keeps the server detail', async () => {
      for (const body of [
        { ...uniform404, errorCode: 'ebilling.submission.document_not_found', detail: 'Document gone.' },
        { ...uniform404, reasonCode: 'something.else', detail: 'Other 404.' },
      ]) {
        const fetchMock = jest.fn().mockResolvedValue(jsonResponse(404, body));
        const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
          .submit(input())
          .catch(e => e);
        expect(err.detail).toBe(body.detail);
      }
    });

    it('negative: the same reasonCode on a status other than 404 is not remapped', async () => {
      const fetchMock = jest.fn().mockResolvedValue(jsonResponse(403, uniform404));
      const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
        .submit(input())
        .catch(e => e);
      expect(err.detail).toBe('The requested record was not found.');
    });
  });

  it('a returned failure whose body is not JSON keeps the status', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(503, undefined, '<html>'));
    const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
      .submit(input())
      .catch(e => e);
    expect(err).toBeInstanceOf(InvoiceSubmissionError);
    expect(err.statusCode).toBe(503);
    expect(err.errorCode).toBeUndefined();
  });

  it('a thrown ApiError (what @spaarke/auth does for non-2xx) becomes the same error', async () => {
    const apiError = Object.assign(new Error('x'), {
      name: 'ApiError',
      status: 413,
      problemDetails: { errorCode: 'ebilling.submission.too_large' },
    });
    const fetchMock = jest.fn().mockRejectedValue(apiError);
    const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
      .submit(input())
      .catch(e => e);
    expect(err).toBeInstanceOf(InvoiceSubmissionError);
    expect(err.statusCode).toBe(413);
    expect(err.errorCode).toBe('ebilling.submission.too_large');
  });

  it('a network failure has no status (the screen says the outcome is unconfirmed)', async () => {
    const fetchMock = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
      .submit(input())
      .catch(e => e);
    expect(err).toBeInstanceOf(InvoiceSubmissionError);
    expect(err.statusCode).toBeUndefined();
  });

  it('a 2xx whose body cannot be read is an error, not a silent success', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(202, undefined, '<html>'));
    const err = await createEmailInvoiceSubmission({ authenticatedFetch: fetchMock, dataService })
      .submit(input())
      .catch(e => e);
    expect(err).toBeInstanceOf(InvoiceSubmissionError);
  });
});
