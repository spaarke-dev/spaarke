/**
 * Task 133 A1: the from-email invoice submission client against ebilling's agreed contract
 * (projects/ebilling-r1/notes/095-from-email-contract.md @ fd70b8e1f6): the §2 body, the replay key, the §6 code rule
 * (`errorCode ?? reasonCode`) and retry classes, §3 polling, and the §8 firm list.
 */
import type { AttachmentInfo } from '@shared/adapters/types';
import {
  createInvoiceSubmitHandler,
  FROM_EMAIL_ROUTE,
  loadCorrectionPrefill,
  loadInvoiceFirms,
  pollSubmission,
  submitInvoiceFromEmail,
  type InvoiceEmailSource,
} from '../invoiceSubmissionService';
import { HELD_FOR_REVIEW_MESSAGE } from '../../utils/invoiceSubmissionMessages';
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

  it("task 094: step A's re-file 403 carries its code only under extensions (the problem middleware's body)", async () => {
    mockFetch.mockResolvedValueOnce(
      res(403, {
        type: 'https://spaarke.com/errors/COMMUNICATION_REFILE_FORBIDDEN',
        title: 'Forbidden',
        detail: 'The communication does not exist or is not visible to the caller, or the caller may not change it.',
        status: 403,
        extensions: { code: 'COMMUNICATION_REFILE_FORBIDDEN', correlationId: 'c-1' },
      })
    );
    expect(await submit()).toMatchObject({ kind: 'refused', status: 403, code: 'COMMUNICATION_REFILE_FORBIDDEN' });

    // must-not-fire: errorCode still wins over extensions.code
    mockFetch.mockResolvedValueOnce(
      res(403, { errorCode: 'ebilling.submission.source_forbidden', extensions: { code: 'OTHER' } })
    );
    expect(await submit()).toMatchObject({ code: 'ebilling.submission.source_forbidden' });

    // must-not-fire: reasonCode wins too
    mockFetch.mockResolvedValueOnce(
      res(403, { reasonCode: 'sdap.access.deny.insufficient_rights', extensions: { code: 'OTHER' } })
    );
    expect(await submit()).toMatchObject({ code: 'sdap.access.deny.insufficient_rights' });
  });

  it("must-not-fire: extensions.code is read on a 403 only (the middleware's 502 Graph error and 401 OBO failure)", async () => {
    mockFetch.mockResolvedValueOnce(res(502, { title: 'Graph', extensions: { code: 'graph_error' } }));
    expect(await submit()).toMatchObject({ kind: 'retryable', status: 502, code: undefined });

    // two 401s: authenticatedJsonFetch retries a 401 once with a fresh token
    const obo = res(401, { title: 'OBO', extensions: { code: 'obo_failed' } });
    mockFetch.mockResolvedValueOnce(obo).mockResolvedValueOnce(obo);
    expect(await submit()).toMatchObject({ kind: 'refused', status: 401, code: undefined });
  });

  it('task 094: a detail ending in the pinned "filing is kept" sentence flags filingKept; others do not', async () => {
    const kept =
      'This email was already submitted as an invoice for this record by someone else. Nothing was saved. ' +
      'The communication was filed under the record first; that filing is kept.';
    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_already_submitted', detail: kept })
    );
    expect(await submit()).toMatchObject({ kind: 'refused', filingKept: true });

    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.tracking.unavailable', detail: `Busy. ${kept}` }));
    expect(await submit()).toMatchObject({ kind: 'retryable', filingKept: true });

    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_already_submitted', detail: 'Nothing was saved.' })
    );
    expect(await submit()).not.toHaveProperty('filingKept');

    // must-not-fire: the sentence in the middle of the detail is not the pinned ending
    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_already_submitted', detail: `${kept} Then it was undone.` })
    );
    expect(await submit()).not.toHaveProperty('filingKept');
  });

  it("task 094: ebilling's top-level filingKept: true flags it without the sentence; false does not", async () => {
    mockFetch.mockResolvedValueOnce(
      res(409, {
        errorCode: 'ebilling.submission.document_already_submitted',
        detail: 'Nothing was saved.',
        filingKept: true,
      })
    );
    expect(await submit()).toMatchObject({ kind: 'refused', filingKept: true });

    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_already_submitted', filingKept: false })
    );
    expect(await submit()).not.toHaveProperty('filingKept');
  });

  it('ebilling task 192: a well-formed filedUnder on the 409 is read; a malformed one is ignored', async () => {
    const filedUnder = { entityType: 'Matter', entityId: 'b1a2c3d4-0000-0000-0000-000000000001', name: 'PAT-897705' };
    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_core_record_mismatch', filedUnder })
    );
    expect(await submit()).toMatchObject({ kind: 'refused', status: 409, filedUnder });

    // must-not-fire: absent, a non-core type, a missing name or id, or not an object
    for (const bad of [
      undefined,
      { ...filedUnder, entityType: 'Account' },
      { entityType: 'Matter', entityId: filedUnder.entityId },
      { entityType: 'Project', name: 'P-1' },
      'PAT-897705',
    ]) {
      mockFetch.mockResolvedValueOnce(
        res(409, { errorCode: 'ebilling.submission.document_core_record_mismatch', filedUnder: bad })
      );
      expect(await submit()).not.toHaveProperty('filedUnder');
    }
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

  it('a 404 record_unavailable (the read gate, 2026-10-28) says the record is not available, never []', async () => {
    mockFetch.mockResolvedValueOnce(res(404, { reasonCode: 'sdap.access.deny.record_unavailable' }));
    await expect(loadInvoiceFirms(target, config)).rejects.toThrow(/This record isn't available to you/);
  });
});

describe('loadCorrectionPrefill (§10, ebilling 097e)', () => {
  const INVOICE_ID = '3c9d2b1a-7e4f-4a6b-8c2d-1e0f9a8b7c65';
  const PREFILL = {
    invoiceId: INVOICE_ID,
    firm: { id: 'f1', name: 'Morrison Foerster' },
    coreRecord: { entityType: 'Matter', entityId: 'm1', name: 'PAT-792345' },
    invoiceStatus: 'InReview',
    trackingNumber: 'INV-012.2',
    firmInvoiceNumber: 'INV-012',
    canCorrect: true,
    reason: null,
    replacedBy: null,
  };

  it('calls the §10 route with the cleaned id and returns the body', async () => {
    mockFetch.mockResolvedValueOnce(res(200, PREFILL));
    expect(await loadCorrectionPrefill(`{${INVOICE_ID.toUpperCase()}}`, config)).toEqual(PREFILL);
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}/api/finance/invoices/${INVOICE_ID}/correction-prefill`);
  });

  it('a Superseded invoice carries its replacement; an unreadable replacement is null', async () => {
    const superseded = {
      ...PREFILL,
      invoiceStatus: 'Superseded',
      canCorrect: false,
      reason: 'invoice_status',
      replacedBy: { invoiceId: 'i2', name: 'INV-013' },
    };
    mockFetch.mockResolvedValueOnce(res(200, superseded));
    expect(await loadCorrectionPrefill(INVOICE_ID, config)).toMatchObject({
      canCorrect: false,
      reason: 'invoice_status',
      replacedBy: { invoiceId: 'i2', name: 'INV-013' },
    });

    mockFetch.mockResolvedValueOnce(res(200, { ...superseded, replacedBy: { invoiceId: 'i2' } }));
    expect(await loadCorrectionPrefill(INVOICE_ID, config)).toMatchObject({ replacedBy: null });
  });

  it('must-not-fire: a body missing what a correction keeps is not used', async () => {
    for (const bad of [
      { ...PREFILL, firm: null },
      { ...PREFILL, coreRecord: { ...PREFILL.coreRecord, entityType: 'Account' } },
      { ...PREFILL, firmInvoiceNumber: undefined },
      { ...PREFILL, canCorrect: 'yes' },
      null,
    ]) {
      mockFetch.mockResolvedValueOnce(res(200, bad));
      await expect(loadCorrectionPrefill(INVOICE_ID, config)).rejects.toThrow(/couldn't be read/);
    }
  });

  it('the uniform 404, ebilling.disabled, another 503 and no answer each have their sentence', async () => {
    mockFetch.mockResolvedValueOnce(res(404, { reasonCode: 'sdap.access.deny.record_unavailable' }));
    await expect(loadCorrectionPrefill(INVOICE_ID, config)).rejects.toThrow(/This invoice isn't available to you/);
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.disabled' }));
    await expect(loadCorrectionPrefill(INVOICE_ID, config)).rejects.toThrow(/turned off in Spaarke/);
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.submission.record_unavailable' }));
    await expect(loadCorrectionPrefill(INVOICE_ID, config)).rejects.toThrow(/couldn't read this invoice right now/);
    mockFetch.mockRejectedValueOnce(new TypeError('network'));
    await expect(loadCorrectionPrefill(INVOICE_ID, config)).rejects.toThrow(/Couldn't reach Spaarke/);
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
    // Failed AFTER the 202: an accepted submission that ended, with its tracking number (not "not submitted").
    expect(outcome).toMatchObject({ kind: 'ended', status: 'Failed', trackingNumber: 'INV-4711.1' });
    expect(outcome.kind === 'ended' && outcome.message).toMatch(/^"INV-4711\.pdf" isn't an invoice/);
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

  it('attachment_not_preserved (422, 2026-10-28) is permanent: nothing was saved, upload in Spaarke', async () => {
    mockFetch.mockResolvedValueOnce(res(422, { errorCode: 'ebilling.submission.attachment_not_preserved' }));
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome).toEqual({
      kind: 'permanent',
      message: "One of the marked files couldn't be saved exactly as attached. Nothing was submitted.",
    });
  });

  it('task 094: a synchronous 422 email_save_failed is permanent; storage_not_configured is a plain refusal', async () => {
    mockFetch.mockResolvedValueOnce(res(422, { errorCode: 'ebilling.submission.email_save_failed' }));
    const handler = createInvoiceSubmitHandler({ config, email: emailSource(), poll });
    expect(await handler(INPUT, { actionId: 'a' })).toMatchObject({
      kind: 'permanent',
      message: expect.stringMatching(/saved copy in Spaarke is missing or empty/),
    });

    mockFetch.mockResolvedValueOnce(res(422, { errorCode: 'ebilling.submission.storage_not_configured' }));
    expect(await handler(INPUT, { actionId: 'b' })).toMatchObject({
      kind: 'refused',
      message: expect.stringMatching(/Ask an administrator\.$/),
    });
  });

  it('task 094: COMMUNICATION_REFILE_FORBIDDEN is permanent (an upload in Spaarke does not file the email)', async () => {
    mockFetch.mockResolvedValueOnce(res(403, { extensions: { code: 'COMMUNICATION_REFILE_FORBIDDEN' } }));
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome).toMatchObject({ kind: 'permanent', message: expect.stringMatching(/permission to file it/) });
  });

  it('task 094: a refusal after step A tells the user the email stays filed', async () => {
    mockFetch.mockResolvedValueOnce(
      res(409, {
        errorCode: 'ebilling.submission.document_already_submitted',
        detail: 'Nothing was saved. The communication was filed under the record first; that filing is kept.',
      })
    );
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome).toMatchObject({ kind: 'refused', message: expect.stringMatching(/stays filed there\.$/) });
  });

  it('ebilling task 192: the mismatch refusal names the filed-under record and carries it for the view', async () => {
    const filedUnder = {
      entityType: 'Project',
      entityId: 'b1a2c3d4-0000-0000-0000-000000000002',
      name: 'Harbor Lease',
    };
    mockFetch.mockResolvedValueOnce(
      res(409, { errorCode: 'ebilling.submission.document_core_record_mismatch', filedUnder })
    );
    const handler = createInvoiceSubmitHandler({ config, email: emailSource(), poll });
    expect(await handler(INPUT, { actionId: 'a' })).toEqual({
      kind: 'refused',
      message:
        'This email is filed under Harbor Lease, so its invoice can only be submitted to that record. Nothing was submitted.',
      filedUnder,
    });

    // must-not-fire: without filedUnder the refusal names no record and offers none
    mockFetch.mockResolvedValueOnce(res(409, { errorCode: 'ebilling.submission.document_core_record_mismatch' }));
    const plain = await handler(INPUT, { actionId: 'b' });
    expect(plain).not.toHaveProperty('filedUnder');
    expect(plain).toMatchObject({
      kind: 'refused',
      message: expect.stringMatching(/can't be submitted to this record/),
    });
  });

  it('OFFICE_AUTH_ERROR (500, 2026-10-28) is retryable', async () => {
    mockFetch.mockResolvedValueOnce(res(500, { errorCode: 'OFFICE_AUTH_ERROR', correlationId: 'c-1' }));
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome).toMatchObject({ kind: 'retryable', message: expect.stringMatching(/couldn't check your access/) });
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

describe('review fixes (task 133) — reading the email', () => {
  it('must-fire: readContext failing is a retryable sentence for the user, not the adapter text, and nothing is sent', async () => {
    const email: InvoiceEmailSource = {
      ...emailSource(),
      readContext: async () => {
        throw { code: 'InternalError', message: 'Office.context.mailbox.item is null (code 5001)' };
      },
    };
    const result = await submitInvoiceFromEmail(INPUT, { config, email, idempotencyKey: 'k' });
    expect(result).toEqual({
      kind: 'retryable',
      status: 0,
      code: undefined,
      detail: "Couldn't read this email, so nothing was submitted. Try again.",
    });
    expect(mockFetch).not.toHaveBeenCalled();

    const outcome = await createInvoiceSubmitHandler({ config, email })(INPUT, { actionId: 'a' });
    expect(outcome).toEqual({
      kind: 'retryable',
      message: "Couldn't read this email, so nothing was submitted. Try again.",
    });
    expect(JSON.stringify(outcome)).not.toMatch(/5001|mailbox/);
  });

  it('a failing attachment list is the same sentence (no raw adapter text)', async () => {
    const email: InvoiceEmailSource = {
      ...emailSource(),
      listAttachments: async () => {
        throw new Error('getAttachmentsAsync failed: 5001');
      },
    };
    const result = await submitInvoiceFromEmail(INPUT, { config, email, idempotencyKey: 'k' });
    expect(result).toMatchObject({
      kind: 'retryable',
      detail: "Couldn't read this email, so nothing was submitted. Try again.",
    });
  });
});

describe('review fixes (task 133) — a capture error is worded for Submit Invoice, not for the Save view', () => {
  const run = (reader: InvoiceEmailSource['reader']) =>
    submitInvoiceFromEmail(INPUT, { config, email: { ...emailSource(), reader }, idempotencyKey: 'k' });

  it("must-fire: a marked attachment that can't be read → \"Couldn't read '<file>'. Mark it Don't save…\"", async () => {
    const result = await run({
      getBody: async () => '<p>x</p>',
      getAttachmentContent: async () => {
        throw new Error('Outlook said 5001');
      },
    });
    expect(result).toMatchObject({
      kind: 'retryable',
      detail: "Couldn't read 'INV-4711.pdf'. Mark it Don't save, or try again. Nothing was submitted.",
    });
    const detail = (result as { detail: string }).detail;
    expect(detail).not.toMatch(/untick|saved|5001/);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('a body too large says "submit", with the sizes', async () => {
    const result = await run({
      getBody: async () => 'a'.repeat(28_100_000),
      getAttachmentContent: async () => ({ content: '' }),
    });
    const detail = (result as { detail: string }).detail;
    expect(detail).toMatch(/text is too large to submit/);
    expect(detail).toMatch(/Nothing was submitted\.$/);
    expect(detail).not.toMatch(/saved/);
  });

  it('a body that cannot be read is the generic read sentence, without the adapter text', async () => {
    const result = await run({
      getBody: async () => {
        throw new Error('Office.js 5001');
      },
      getAttachmentContent: async () => ({ content: '' }),
    });
    expect(result).toMatchObject({
      kind: 'retryable',
      detail: "Couldn't read this email, so nothing was submitted. Try again.",
    });
  });

  it("must-not-fire: the Save view's own text is unchanged", async () => {
    const { captureEmailContent } = await import('../emailContentCapture');
    const failing = captureEmailContent(
      {
        getBody: async () => 'x',
        getAttachmentContent: async () => {
          throw new Error('boom');
        },
      },
      await emailSource().listAttachments(),
      new Set(['AAMkAD...1'])
    );
    await expect(failing).rejects.toThrow(/nothing was saved.*untick it/);
  });
});

describe('review fixes (task 133) — attachments the capture left out are carried, never dropped', () => {
  const withCloudLink = () => {
    const cloud: AttachmentInfo = {
      id: 'AAMkAD...9',
      name: 'shared-notes.docx',
      size: 10,
      contentType: 'application/octet-stream',
      isInline: false,
      attachmentType: 'cloud',
    };
    return emailSource({ attachments: [...ATTACHMENTS, cloud] });
  };

  it("must-fire: the capture's skipped list travels on the accepted result and into the handler outcome", async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    const result = await submitInvoiceFromEmail(INPUT, { config, email: withCloudLink(), idempotencyKey: 'k' });
    expect(result.kind).toBe('accepted');
    expect(result.skipped).toHaveLength(1);
    expect(result.skipped![0]).toMatchObject({ attachmentId: 'AAMkAD...9', reason: 'cloud-link' });

    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED)).mockResolvedValueOnce(res(200, { status: 'Accepted' }));
    const outcome = await createInvoiceSubmitHandler({
      config,
      email: withCloudLink(),
      poll: { sleep: async () => undefined },
    })(INPUT, { actionId: 'a' });
    expect(outcome).toMatchObject({ kind: 'success', trackingNumber: 'INV-4711.1' });
    expect(outcome.skipped?.map(k => k.attachmentId)).toEqual(['AAMkAD...9']);
    expect(outcome.skipped![0]!.message).toMatch(/shared-notes\.docx" was not saved with the email/);
  });

  it('a server refusal after the capture still carries the skipped list', async () => {
    mockFetch.mockResolvedValueOnce(res(409, { errorCode: 'ebilling.submission.document_already_submitted' }));
    const outcome = await createInvoiceSubmitHandler({ config, email: withCloudLink() })(INPUT, { actionId: 'a' });
    expect(outcome.kind).toBe('refused');
    expect(outcome.skipped).toHaveLength(1);
  });

  it('must-fire: a large body uses the budget, the capture skips the marked PDF as too large, and the local refusal carries that reason', async () => {
    const email: InvoiceEmailSource = {
      ...emailSource(),
      // ~27.9 MB of text: what is left of the 28 MB budget cannot hold the marked PDF.
      reader: {
        getBody: async () => 'a'.repeat(27_900_000),
        getAttachmentContent: async id => ({ content: `B64-${id}` }),
      },
    };
    const result = await submitInvoiceFromEmail(INPUT, { config, email, idempotencyKey: 'k' });
    expect(result).toMatchObject({
      kind: 'refused',
      code: 'ebilling.submission.marked_file_missing',
      fileName: 'INV-4711.pdf',
      local: true,
    });
    expect(mockFetch).not.toHaveBeenCalled();
    // Only the PDF is too big for what is left; the small csv still fits.
    expect(result.skipped?.map(k => k.attachmentId)).toEqual(['AAMkAD...1']);
    expect(result.skipped![0]).toMatchObject({ reason: 'too-large', ticked: true });

    const outcome = await createInvoiceSubmitHandler({ config, email })(INPUT, { actionId: 'a' });
    expect(outcome.kind).toBe('refused');
    expect(outcome.skipped?.map(k => k.attachmentId)).toEqual(['AAMkAD...1']);
  });

  it('a marked cloud link refused locally names itself in the skipped list too', async () => {
    const cloud: AttachmentInfo = { ...ATTACHMENTS[1]!, attachmentType: 'cloud' };
    const email = emailSource({ attachments: [ATTACHMENTS[0]!, cloud] });
    const result = await submitInvoiceFromEmail(INPUT, { config, email, idempotencyKey: 'k' });
    expect(result.skipped?.map(k => k.attachmentId)).toEqual(['AAMkAD...2']);
    expect(result.skipped![0]).toMatchObject({ reason: 'cloud-link' });
  });

  it('must-not-fire: nothing left out means no skipped key at all', async () => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED));
    const result = await submitInvoiceFromEmail(INPUT, { config, email: emailSource(), idempotencyKey: 'k' });
    expect(result).not.toHaveProperty('skipped');
  });
});

describe('review fixes (task 133) — what the handler says after the 202', () => {
  const poll = { sleep: async () => undefined };
  const run = (statusBody: Record<string, unknown>) => {
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED)).mockResolvedValueOnce(res(200, statusBody));
    return createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
  };

  it('must-fire: Accepted with a code the catalog does not know is "held for review", never the refusal text', async () => {
    const outcome = await run({ status: 'Accepted', outcomeReasonCode: 'ebilling.submission.held_for_review' });
    expect(outcome).toEqual({ kind: 'success', trackingNumber: 'INV-4711.1', message: HELD_FOR_REVIEW_MESSAGE });
    expect(JSON.stringify(outcome)).not.toMatch(/not accepted/);
  });

  it('Accepted with a catalog Failed code is still the neutral held sentence, not a failure sentence', async () => {
    const outcome = await run({ status: 'Accepted', outcomeReasonCode: 'ebilling.submission.processing_failed' });
    expect(outcome).toMatchObject({ kind: 'success', message: HELD_FOR_REVIEW_MESSAGE });
  });

  it('097f must-fire: Accepted and replaced by a correction is not "held"; it names the correcting tracking number', async () => {
    const outcome = await run({
      status: 'Accepted',
      outcomeReasonCode: 'ebilling.submission.replaced_by_correction',
      replacedBy: { submissionId: 'sub-2', trackingNumber: 'INV-4711.2' },
    });
    expect(outcome).toEqual({
      kind: 'success',
      trackingNumber: 'INV-4711.1',
      message: 'This invoice was replaced by a correction (tracking INV-4711.2).',
    });
    expect(JSON.stringify(outcome)).not.toContain(HELD_FOR_REVIEW_MESSAGE);
  });

  it('097f: replacedBy absent (unreadable, ambiguous or a lookup fault) still says replaced, without a number', async () => {
    const outcome = await run({ status: 'Accepted', outcomeReasonCode: 'ebilling.submission.replaced_by_correction' });
    expect(outcome).toMatchObject({ kind: 'success', message: 'This invoice was replaced by a correction.' });
  });

  it('097f must-not-fire: replacedBy on another code changes nothing (still held)', async () => {
    const outcome = await run({
      status: 'Accepted',
      outcomeReasonCode: 'ebilling.submission.held_for_review',
      replacedBy: { trackingNumber: 'INV-4711.2' },
    });
    expect(outcome).toMatchObject({ kind: 'success', message: HELD_FOR_REVIEW_MESSAGE });
  });

  it('must-not-fire: Accepted with no code has no message', async () => {
    expect(await run({ status: 'Accepted', outcomeReasonCode: null })).toEqual({
      kind: 'success',
      trackingNumber: 'INV-4711.1',
    });
  });

  it('must-fire: Rejected after the 202 is its own outcome, with the tracking number and the reason', async () => {
    expect(await run({ status: 'Rejected', outcomeReasonCode: 'ebilling.submission.blocked' })).toEqual({
      kind: 'ended',
      status: 'Rejected',
      message: 'The invoice was rejected by a billing rule.',
      trackingNumber: 'INV-4711.1',
    });
  });

  it('must-fire: Failed after the 202 is its own outcome; an unknown code does not say "not accepted"', async () => {
    expect(await run({ status: 'Failed', outcomeReasonCode: 'invoice.read.unreadable' })).toMatchObject({
      kind: 'ended',
      status: 'Failed',
      trackingNumber: 'INV-4711.1',
      message: '"INV-4711.pdf" couldn\'t be read: it may be damaged or password-protected.',
    });
    expect(await run({ status: 'Failed', outcomeReasonCode: 'something.new' })).toEqual({
      kind: 'ended',
      status: 'Failed',
      message: "Spaarke couldn't process the invoice.",
      trackingNumber: 'INV-4711.1',
    });
  });

  it('a refusal BEFORE the 202 stays "refused" (nothing was submitted)', async () => {
    mockFetch.mockResolvedValueOnce(res(409, { errorCode: 'ebilling.submission.document_already_submitted' }));
    const outcome = await createInvoiceSubmitHandler({ config, email: emailSource(), poll })(INPUT, { actionId: 'a' });
    expect(outcome.kind).toBe('refused');
  });
});

describe('review fixes (task 133) — leaving the view stops the work', () => {
  it('pollSubmission: an abort during the wait ends it as aborted, with no further read (injected sleep)', async () => {
    const controller = new AbortController();
    const result = await pollSubmission('/s', {
      config,
      signal: controller.signal,
      sleep: async () => controller.abort(),
    });
    expect(result).toEqual({ kind: 'aborted' });
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('pollSubmission: the default wait ends the moment the signal aborts (not after the delay)', async () => {
    const controller = new AbortController();
    setTimeout(() => controller.abort(), 20);
    const started = Date.now();
    const result = await pollSubmission('/s', { config, signal: controller.signal, initialDelayMs: 60_000 });
    expect(result).toEqual({ kind: 'aborted' });
    expect(Date.now() - started).toBeLessThan(5000);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('pollSubmission: an abort after the first read stops the next one', async () => {
    const controller = new AbortController();
    mockFetch.mockImplementation(async () => {
      controller.abort();
      return res(200, { status: 'Processing' });
    });
    const result = await pollSubmission('/s', { config, signal: controller.signal, sleep: async () => undefined });
    expect(result).toEqual({ kind: 'aborted' });
    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect((mockFetch.mock.calls[0]![1] as RequestInit).signal).toBe(controller.signal);
  });

  it('must-not-fire: without a signal, polling behaves as before', async () => {
    mockFetch.mockResolvedValueOnce(res(200, { status: 'Accepted' }));
    expect(await pollSubmission('/s', { config, sleep: async () => undefined })).toMatchObject({ kind: 'final' });
    expect((mockFetch.mock.calls[0]![1] as RequestInit).signal).toBeUndefined();
  });

  it("the handler passes the attempt's signal to the request and the polling, and an abort stops it", async () => {
    const controller = new AbortController();
    mockFetch.mockResolvedValueOnce(res(202, ACCEPTED)).mockResolvedValue(res(200, { status: 'Processing' }));
    const outcome = await createInvoiceSubmitHandler({
      config,
      email: emailSource(),
      poll: { sleep: async () => controller.abort() },
    })(INPUT, { actionId: 'a', signal: controller.signal });
    expect((mockFetch.mock.calls[0]![1] as RequestInit).signal).toBe(controller.signal);
    expect(mockFetch).toHaveBeenCalledTimes(1); // the POST only: the status was never read
    expect(outcome).toMatchObject({ kind: 'success', trackingNumber: 'INV-4711.1' });
    expect(outcome.kind === 'success' && outcome.message).toMatch(/left this view/);
  });
});
