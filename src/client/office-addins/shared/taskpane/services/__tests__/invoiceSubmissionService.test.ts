/**
 * Task 133 A1: the from-email invoice submission client against ebilling's agreed contract
 * (projects/ebilling-r1/notes/095-from-email-contract.md @ 717cf238d9): the §2 body, the replay key, the §6 code rule
 * (`errorCode ?? reasonCode`) and retry classes, §3 polling, and the §8 firm list.
 */
import type { AttachmentInfo } from '@shared/adapters/types';
import {
  createInvoiceSubmitHandler,
  FROM_EMAIL_ROUTE,
  loadInvoiceFirms,
  pollSubmission,
  submitInvoiceFromEmail,
  type InvoiceEmailSource,
} from '../invoiceSubmissionService';
import type { InvoiceSubmissionInput } from '../invoiceSubmissionSeam';

const BASE = 'https://bff.example';
const config = { apiBaseUrl: BASE, getAccessToken: async () => 'token' };
const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

function res(status: number, body: unknown, headers: Record<string, string> = {}) {
  const lower = Object.fromEntries(Object.entries(headers).map(([k, v]) => [k.toLowerCase(), v]));
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (name: string) => lower[name.toLowerCase()] ?? null },
    json: async () => {
      if (body === undefined) throw new Error('no body');
      return body;
    },
  };
}

/** The §2 example's email: two files and an inline logo. */
const ATTACHMENTS: AttachmentInfo[] = [
  {
    id: 'AAMkAD...1',
    name: 'INV-4711.pdf',
    size: 182344,
    contentType: 'application/pdf',
    isInline: false,
    attachmentType: 'file',
  },
  {
    id: 'AAMkAD...2',
    name: 'timesheet.csv',
    size: 40211,
    contentType: 'text/csv',
    isInline: false,
    attachmentType: 'file',
  },
  { id: 'AAMkAD...3', name: 'logo.png', size: 5120, contentType: 'image/png', isInline: true, attachmentType: 'file' },
];

function emailSource(
  over: Partial<{ internetMessageId: string; attachments: AttachmentInfo[] }> = {}
): InvoiceEmailSource {
  const attachments = over.attachments ?? ATTACHMENTS;
  return {
    readContext: async () => ({
      internetMessageId: over.internetMessageId ?? '<CA+abc123@mail.firm.example>',
      subject: 'Invoice 4711',
      senderEmail: 'billing@firm.example',
      recipients: [],
    }),
    listAttachments: async () => attachments,
    reader: {
      getBody: async () => '<p>Attached.</p>',
      getAttachmentContent: async id => ({ content: `B64-${id}` }),
    },
  };
}

/** The §2 example's input (marks as the view builds them, with the Outlook attachment id). */
const INPUT: InvoiceSubmissionInput = {
  targetEntity: { entityType: 'Matter', entityId: '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41' },
  marks: [
    {
      attachmentId: 'AAMkAD...1',
      fileName: 'INV-4711.pdf',
      size: 182344,
      contentType: 'application/pdf',
      mark: 'Invoice',
    },
    { attachmentId: 'AAMkAD...2', fileName: 'timesheet.csv', size: 40211, contentType: 'text/csv', mark: 'Supporting' },
  ],
  firmId: '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77',
  firmInvoiceNumber: 'INV-4711',
  poNumber: null,
  mode: 'New',
  invoiceId: null,
};

const ACCEPTED = {
  submissionId: '0b9e',
  jobId: '5d2a',
  trackingNumber: 'INV-4711.1',
  statusUrl: '/api/finance/invoice-submissions/0b9e',
  emlDocumentId: 'a41c',
};

beforeEach(() => mockFetch.mockReset());

function sentBody(call = 0): Record<string, unknown> {
  return JSON.parse((mockFetch.mock.calls[call]![1] as RequestInit).body as string);
}
function sentHeaders(call = 0): Record<string, string> {
  return (mockFetch.mock.calls[call]![1] as RequestInit).headers as Record<string, string>;
}

describe('submitInvoiceFromEmail — the §2 request', () => {
  it("builds the §2 example body: target, marks as sent, firm fields, mode, and /office/save's email", async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));

    await submitInvoiceFromEmail(INPUT, { config, email: emailSource(), idempotencyKey: 'action-1' });

    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}${FROM_EMAIL_ROUTE}`);
    expect((mockFetch.mock.calls[0]![1] as RequestInit).method).toBe('POST');
    expect(sentHeaders()['X-Idempotency-Key']).toBe('action-1');
    const body = sentBody();
    expect(body).toMatchObject({
      targetEntity: { entityType: 'Matter', entityId: '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41' },
      marks: [
        { fileName: 'INV-4711.pdf', size: 182344, contentType: 'application/pdf', mark: 'Invoice' },
        { fileName: 'timesheet.csv', size: 40211, contentType: 'text/csv', mark: 'Supporting' },
      ],
      firmId: '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77',
      firmInvoiceNumber: 'INV-4711',
      poNumber: null,
      mode: 'New',
      invoiceId: null,
      email: {
        subject: 'Invoice 4711',
        senderEmail: 'billing@firm.example',
        internetMessageId: '<CA+abc123@mail.firm.example>',
        body: '<p>Attached.</p>',
        isBodyHtml: true,
        isNameSystemDerived: true,
      },
    });
    // No attachment id on the wire marks; one mark per non-inline attachment.
    expect((body.marks as object[]).every(m => !('attachmentId' in m))).toBe(true);
    const email = body.email as { attachments: Array<Record<string, unknown>> };
    expect(email.attachments.map(a => [a.fileName, a.size, a.contentType, a.isInline])).toEqual([
      ['INV-4711.pdf', 182344, 'application/pdf', false],
      ['timesheet.csv', 40211, 'text/csv', false],
      ['logo.png', 5120, 'image/png', true],
    ]);
    expect(email.attachments[0]!.contentBase64).toBe('B64-AAMkAD...1');
    // `email` only — never a nested SaveRequest, never a body idempotency key (§2, §4).
    for (const key of ['contentType', 'idempotencyKey', 'aiOptions', 'triggerAiProcessing', 'documentMetadata']) {
      expect(body).not.toHaveProperty(key);
    }
    expect(body.email).not.toHaveProperty('targetEntity');
  });

  it('a mark is matched AS SENT: an attached Outlook item is renamed .eml and sent as octet-stream', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    const item: AttachmentInfo = {
      id: 'item-1',
      name: 'Fwd: engagement',
      size: 900,
      contentType: 'message/rfc822',
      isInline: false,
      attachmentType: 'item',
    };
    const input: InvoiceSubmissionInput = {
      ...INPUT,
      marks: [
        INPUT.marks[0]!,
        {
          attachmentId: 'item-1',
          fileName: 'Fwd: engagement',
          size: 900,
          contentType: 'message/rfc822',
          mark: 'Supporting',
        },
      ],
    };

    await submitInvoiceFromEmail(input, {
      config,
      email: emailSource({ attachments: [ATTACHMENTS[0]!, item] }),
      idempotencyKey: 'k',
    });

    expect((sentBody().marks as unknown[])[1]).toEqual({
      fileName: 'Fwd: engagement.eml',
      size: 900,
      contentType: 'application/octet-stream',
      mark: 'Supporting',
    });
  });

  it('must-fire: no RFC internetMessageId → refused locally, nothing sent', async () => {
    const result = await submitInvoiceFromEmail(INPUT, {
      config,
      email: emailSource({ internetMessageId: '' }),
      idempotencyKey: 'k',
    });
    expect(result).toEqual({
      kind: 'refused',
      status: 0,
      code: 'ebilling.submission.email_unidentifiable',
      reason: 'no_message_id',
      local: true,
    });
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('must-fire: a Supporting file the capture could not send (cloud link) → marked_file_missing, nothing sent', async () => {
    const cloud: AttachmentInfo = { ...ATTACHMENTS[1]!, attachmentType: 'cloud' };
    const result = await submitInvoiceFromEmail(INPUT, {
      config,
      email: emailSource({ attachments: [ATTACHMENTS[0]!, cloud] }),
      idempotencyKey: 'k',
    });
    expect(result).toMatchObject({
      kind: 'refused',
      code: 'ebilling.submission.marked_file_missing',
      fileName: 'timesheet.csv',
      mark: 'Supporting',
      local: true,
    });
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it("must-not-fire: a DontSave cloud link keeps Outlook's values and the request is sent", async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    const cloud: AttachmentInfo = { ...ATTACHMENTS[1]!, attachmentType: 'cloud' };
    const input = { ...INPUT, marks: [INPUT.marks[0]!, { ...INPUT.marks[1]!, mark: 'DontSave' as const }] };
    const result = await submitInvoiceFromEmail(input, {
      config,
      email: emailSource({ attachments: [ATTACHMENTS[0]!, cloud] }),
      idempotencyKey: 'k',
    });
    expect(result.kind).toBe('accepted');
    expect((sentBody().marks as unknown[])[1]).toEqual({
      fileName: 'timesheet.csv',
      size: 40211,
      contentType: 'text/csv',
      mark: 'DontSave',
    });
  });
});

describe('submitInvoiceFromEmail — the answer', () => {
  const submit = () => submitInvoiceFromEmail(INPUT, { config, email: emailSource(), idempotencyKey: 'k' });

  it('202 → accepted with the §3 fields; a cached replay is flagged', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED, { 'X-Idempotency-Status': 'cached' }));
    expect(await submit()).toEqual({ kind: 'accepted', accepted: ACCEPTED, replayed: true });
  });

  it('code = errorCode ?? reasonCode, with the extras', async () => {
    mockFetch.mockResolvedValueOnce(res(403, { errorCode: 'OFFICE_009', reasonCode: 'insufficient_rights' }));
    expect(await submit()).toMatchObject({ kind: 'refused', status: 403, code: 'OFFICE_009' });

    mockFetch.mockResolvedValueOnce(res(403, { reasonCode: 'sdap.access.deny.insufficient_rights' }));
    expect(await submit()).toMatchObject({ kind: 'refused', code: 'sdap.access.deny.insufficient_rights' });

    mockFetch.mockResolvedValueOnce(
      res(400, { errorCode: 'ebilling.submission.marks_invalid', reason: 'invoice_count', fileName: 'a.pdf' })
    );
    expect(await submit()).toMatchObject({
      code: 'ebilling.submission.marks_invalid',
      reason: 'invoice_count',
      fileName: 'a.pdf',
    });

    mockFetch.mockResolvedValueOnce(
      res(409, {
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'invoice_status',
        invoiceStatus: 'Paid',
      })
    );
    expect(await submit()).toMatchObject({ reason: 'invoice_status', invoiceStatus: 'Paid' });

    mockFetch.mockResolvedValueOnce(
      res(422, { errorCode: 'ebilling.submission.marked_file_missing', fileName: 'x', mark: 'Invoice' })
    );
    expect(await submit()).toMatchObject({ fileName: 'x', mark: 'Invoice' });
  });

  it("retryable: 503 (Retry-After), 429 (retryAfterSeconds), and the access check's system failure", async () => {
    mockFetch.mockResolvedValueOnce(
      res(503, { errorCode: 'ebilling.submission.email_unavailable' }, { 'Retry-After': '7' })
    );
    expect(await submit()).toEqual({
      kind: 'retryable',
      status: 503,
      code: 'ebilling.submission.email_unavailable',
      retryAfterSeconds: 7,
    });

    mockFetch.mockResolvedValueOnce(res(429, { errorCode: 'OFFICE_015', retryAfterSeconds: 30, limit: 10 }));
    expect(await submit()).toMatchObject({ kind: 'retryable', code: 'OFFICE_015', retryAfterSeconds: 30 });

    mockFetch.mockResolvedValueOnce(res(403, { reasonCode: 'sdap.access.error.system_failure' }));
    expect(await submit()).toMatchObject({ kind: 'retryable', code: 'sdap.access.error.system_failure' });
  });

  it('must-not-fire: 503 ebilling.disabled is NOT retryable', async () => {
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.disabled' }));
    expect(await submit()).toMatchObject({ kind: 'refused', code: 'ebilling.disabled' });
  });

  it('no response at all → retryable (the same key makes the retry safe)', async () => {
    mockFetch.mockRejectedValueOnce(new TypeError('network'));
    expect(await submit()).toEqual({ kind: 'retryable', status: 0, code: undefined });
  });
});

describe('the replay key (§4)', () => {
  it('Retry of the same action reuses X-Idempotency-Key; a new action sends a new one', async () => {
    mockFetch.mockResolvedValue(res(503, { errorCode: 'ebilling.submission.failed' }));
    const handler = createInvoiceSubmitHandler({ config, email: emailSource() });

    await handler(INPUT, { actionId: 'action-1' });
    await handler(INPUT, { actionId: 'action-1' });
    await handler(INPUT, { actionId: 'action-2' });

    expect(sentHeaders(0)['X-Idempotency-Key']).toBe('action-1');
    expect(sentHeaders(1)['X-Idempotency-Key']).toBe('action-1');
    expect(sentHeaders(2)['X-Idempotency-Key']).toBe('action-2');
    expect(sentBody(1)).toEqual(sentBody(0));
  });
});

describe('pollSubmission (§3)', () => {
  const sleeps: number[] = [];
  const sleep = async (ms: number) => {
    sleeps.push(ms);
  };
  beforeEach(() => (sleeps.length = 0));

  it.each(['Accepted', 'Rejected', 'Failed'] as const)('stops at %s', async final => {
    mockFetch
      .mockResolvedValueOnce(res(200, { status: 'Received' }))
      .mockResolvedValueOnce(res(200, { status: 'Processing' }))
      .mockResolvedValueOnce(res(200, { status: final, outcomeReasonCode: final === 'Accepted' ? null : 'x.y' }))
      .mockResolvedValue(res(200, { status: 'Processing' }));

    const result = await pollSubmission(ACCEPTED.statusUrl, { config, sleep });

    expect(result).toEqual({ kind: 'final', status: final, outcomeReasonCode: final === 'Accepted' ? null : 'x.y' });
    expect(mockFetch).toHaveBeenCalledTimes(3);
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}${ACCEPTED.statusUrl}`);
  });

  it('backs off (doubling, capped) and gives up at the cap as pending', async () => {
    mockFetch.mockResolvedValue(res(200, { status: 'Processing' }));
    const result = await pollSubmission(ACCEPTED.statusUrl, {
      config,
      sleep,
      initialDelayMs: 1000,
      maxDelayMs: 4000,
      maxAttempts: 5,
    });
    expect(result).toEqual({ kind: 'pending', status: 'Processing' });
    expect(sleeps).toEqual([1000, 2000, 4000, 4000, 4000]);
  });

  it('a transient read failure is retried within the cap; a 404 ends it', async () => {
    mockFetch
      .mockResolvedValueOnce(res(503, { errorCode: 'ebilling.submission.record_unavailable' }))
      .mockResolvedValueOnce(res(200, { status: 'Accepted' }));
    expect(await pollSubmission('/s', { config, sleep })).toMatchObject({ kind: 'final', status: 'Accepted' });

    mockFetch.mockReset();
    mockFetch.mockResolvedValueOnce(res(404, { reasonCode: 'sdap.access.deny.record_unavailable' }));
    expect(await pollSubmission('/s', { config, sleep })).toEqual({
      kind: 'error',
      status: 404,
      code: 'sdap.access.deny.record_unavailable',
    });
  });
});

describe('loadInvoiceFirms (§8)', () => {
  const target = { entityType: 'WorkAssignment' as const, entityId: 'aaaa-1' };

  it('calls the §8 route and returns {firmId, name}', async () => {
    mockFetch.mockResolvedValueOnce(res(200, [{ firmId: 'f1', name: 'Firm F', extra: 1 }]));
    expect(await loadInvoiceFirms(target, config)).toEqual([{ firmId: 'f1', name: 'Firm F' }]);
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}/api/finance/core-records/WorkAssignment/aaaa-1/firms`);
  });

  it('must-not-fire: 200 [] is an empty list', async () => {
    mockFetch.mockResolvedValueOnce(res(200, []));
    expect(await loadInvoiceFirms(target, config)).toEqual([]);
  });

  it('must-fire: a 503 is an error, never []', async () => {
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.submission.record_unavailable' }));
    await expect(loadInvoiceFirms(target, config)).rejects.toThrow(/couldn't list the firms/);
  });
});

describe('createInvoiceSubmitHandler — submit, then follow the status', () => {
  const poll = { sleep: async () => undefined };

  it('accepted → processing (onAccepted) → Accepted is success with the tracking number', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED)).mockResolvedValueOnce(res(200, { status: 'Accepted' }));
    const onAccepted = jest.fn();
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, {
      actionId: 'a',
      onAccepted,
    });
    expect(onAccepted).toHaveBeenCalledWith({ trackingNumber: 'INV-4711.1' });
    expect(outcome).toEqual({ kind: 'success', trackingNumber: 'INV-4711.1' });
  });

  it('Failed invoice.read.no_invoice names the file the user marked Invoice', async () => {
    mockFetch
      .mockResolvedValueOnce(res(202, ACCEPTED))
      .mockResolvedValueOnce(res(200, { status: 'Failed', outcomeReasonCode: 'invoice.read.no_invoice' }));
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome.kind).toBe('refused');
    expect(outcome.kind !== 'success' && outcome.message).toMatch(/^"INV-4711\.pdf" isn't an invoice/);
  });

  it('email_unidentifiable is permanent (the view offers uploading in Spaarke)', async () => {
    mockFetch.mockResolvedValueOnce(
      res(422, { errorCode: 'ebilling.submission.email_unidentifiable', reason: 'email_not_found' })
    );
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome).toEqual({
      kind: 'permanent',
      message: "Spaarke couldn't find this email in your mailbox, so it can't be submitted from Outlook.",
    });
  });

  it('accepted but still processing at the cap is not a failure', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED)).mockResolvedValue(res(200, { status: 'Processing' }));
    const outcome = await createInvoiceSubmitHandler({
      config,
      email: emailSource(),
      poll: { ...poll, maxAttempts: 2 },
    })(INPUT, { actionId: 'a' });
    expect(outcome).toMatchObject({ kind: 'success', trackingNumber: 'INV-4711.1' });
  });
});
