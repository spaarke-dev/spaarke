/**
 * Task 134 slice B2 (ebilling 095f, EB-D-225): the Correction Note on the wire. `POST .../from-email` carries
 * `correctionNote` for a Correction (trimmed) and never for a New invoice; a retry resends the same note under the same
 * `X-Idempotency-Key` (the note is part of the key's cache binding, contract section 4).
 */
import type { AttachmentInfo } from '@shared/adapters/types';
import {
  createInvoiceSubmitHandler,
  submitInvoiceFromEmail,
  type InvoiceEmailSource,
} from '../invoiceSubmissionService';
import { buildInvoiceSubmissionInput } from '../invoiceSubmissionSeam';

const config = { apiBaseUrl: 'https://bff.example', getAccessToken: async () => 'token' };
const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

function res(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => null },
    json: async () => body,
  };
}

const ATTACHMENTS: AttachmentInfo[] = [
  {
    id: 'att-1',
    name: 'INV-1.pdf',
    size: 1000,
    contentType: 'application/pdf',
    isInline: false,
    attachmentType: 'file',
  },
];
const email: InvoiceEmailSource = {
  readContext: async () => ({
    internetMessageId: '<CA+abc123@mail.firm.example>',
    subject: 'Invoice 1',
    senderEmail: 'billing@firm.example',
    recipients: [],
  }),
  listAttachments: async () => ATTACHMENTS,
  reader: { getBody: async () => '<p>Attached.</p>', getAttachmentContent: async id => ({ content: `B64-${id}` }) },
};

const FIRM = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';
const MATTER = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const INVOICE = '3c9d2b1a-7e4f-4a6b-8c2d-1e0f9a8b7c65';
const ACCEPTED = { submissionId: '0b9e', jobId: '5d2a', trackingNumber: 'INV-1.1', statusUrl: '/api/x/0b9e' };

function inputFor(mode: 'New' | 'Correction', correctionNote?: string) {
  return buildInvoiceSubmissionInput({
    target: { entityType: 'Matter', entityId: MATTER },
    attachments: [{ id: 'att-1', fileName: 'INV-1.pdf', contentType: 'application/pdf', sizeBytes: 1000 }],
    marking: { 'att-1': 'invoice' },
    firmId: FIRM,
    firmInvoiceNumber: 'INV-1',
    poNumber: '',
    mode,
    invoiceId: mode === 'Correction' ? INVOICE : null,
    ...(correctionNote !== undefined ? { correctionNote } : {}),
  });
}

const sentBody = (call = 0): Record<string, unknown> =>
  JSON.parse((mockFetch.mock.calls[call]![1] as RequestInit).body as string);
const sentHeaders = (call = 0): Record<string, string> =>
  (mockFetch.mock.calls[call]![1] as RequestInit).headers as Record<string, string>;

const BASE_KEYS = ['email', 'firmId', 'firmInvoiceNumber', 'invoiceId', 'marks', 'mode', 'poNumber', 'targetEntity'];

beforeEach(() => mockFetch.mockReset());

describe('from-email body — correctionNote (095f)', () => {
  it('must-fire: a Correction body has correctionNote as the trimmed value, beside the usual keys', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    await submitInvoiceFromEmail(inputFor('Correction', '  Wrong rate on line 3.\n'), {
      config,
      email,
      idempotencyKey: 'action-1',
    });
    const body = sentBody();
    expect(body.correctionNote).toBe('Wrong rate on line 3.');
    expect(Object.keys(body).sort()).toEqual([...BASE_KEYS, 'correctionNote'].sort());
    expect(body).toMatchObject({ mode: 'Correction', invoiceId: INVOICE });
  });

  it('must-not-fire: a New body has exactly the keys it had before and no correctionNote property', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    // Even a stray note on the New input never reaches the wire.
    await submitInvoiceFromEmail(inputFor('New', 'ignored'), { config, email, idempotencyKey: 'action-2' });
    const body = sentBody();
    expect(Object.keys(body).sort()).toEqual([...BASE_KEYS].sort());
    expect(Object.prototype.hasOwnProperty.call(body, 'correctionNote')).toBe(false);
    expect((mockFetch.mock.calls[0]![1] as RequestInit).body).not.toContain('correctionNote');
  });

  it('must-fire: a retry (same input, same action id) resends the same note under the same X-Idempotency-Key', async () => {
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.disabled' }));
    mockFetch.mockResolvedValueOnce(res(500, {}));
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    const input = inputFor('Correction', 'The quantity was 4, not 40.');
    const submit = () => submitInvoiceFromEmail(input, { config, email, idempotencyKey: 'action-3' });
    await submit();
    await submit();
    await submit();
    expect(mockFetch).toHaveBeenCalledTimes(3);
    for (const call of [0, 1, 2]) {
      expect(sentBody(call).correctionNote).toBe('The quantity was 4, not 40.');
      expect(sentHeaders(call)['X-Idempotency-Key']).toBe('action-3');
    }
  });

  it('must-fire: the handler turns 400 correction_note_required into its sentence, as a refusal', async () => {
    mockFetch.mockResolvedValueOnce(
      res(400, {
        errorCode: 'ebilling.submission.correction_note_required',
        detail: 'A correction needs a note saying why the invoice is corrected.',
      })
    );
    const handler = createInvoiceSubmitHandler({ config, email });
    const outcome = await handler(inputFor('Correction', 'Why'), { actionId: 'action-4' });
    expect(outcome).toMatchObject({
      kind: 'refused',
      message: 'Enter a correction note saying why the invoice is corrected. Nothing was submitted.',
    });
  });
});
