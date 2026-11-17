/**
 * internalInvoiceSubmission — the Correction half (ebilling-r1 task 095f-2e2b; EB-D-245).
 *
 * What goes on the wire for a corrected invoice, against the server contract of 095f-2e2a (the "Form-field contract" table of
 * `projects/ebilling-r1/notes/095f-2e2a-internal-route-correction.md`): multipart `mode=Correction`, `invoiceId` and `correctionNote`,
 * each once; nothing for a new invoice. And the prefill read and each refusal the route answers, thrown (`authenticatedFetch`) or returned.
 */
import type { IDataService } from '../../../types/serviceInterfaces';
import { InvoiceSubmissionError, invoiceSubmissionErrorMessage } from '../../InvoiceSubmission';
import type { InvoiceCoreRecordOption } from '../../InvoiceSubmission';
import {
  correctionPrefillErrorMessage,
  correctionTargetFromPrefill,
  createInternalInvoiceSubmission,
  readCorrectionPrefill,
} from '../internalInvoiceSubmission';
import type { CorrectionPrefill } from '../internalInvoiceSubmission';

const BASE = 'https://bff.example.test';
const MATTER_ID = '11111111-1111-4111-8111-111111111111';
const FIRM = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const INVOICE_ID = '44444444-4444-4444-8444-444444444444';
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';
const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: MATTER_ID,
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};

const reply = (status: number, body: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body,
});

/** What `@spaarke/auth`'s `authenticatedFetch` throws for a non-2xx answer. */
function refuse(status: number, problem: Record<string, unknown> | null) {
  const err = new Error(`HTTP ${status}`) as Error & { status: number; problemDetails: unknown };
  err.name = 'ApiError';
  err.status = status;
  err.problemDetails = problem;
  return err;
}

function make(handler: (url: string, init?: RequestInit) => unknown) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fn = jest.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, init });
    const out = handler(url, init);
    if (out instanceof Error) throw out;
    return out as Response;
  });
  const dataService = { retrieveMultipleRecords: jest.fn() } as unknown as IDataService;
  return { client: createInternalInvoiceSubmission({ fetch: fn, baseUrl: `${BASE}/`, dataService }), calls, fn };
}

const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });
const accepted = { submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2', statusUrl: '/x' };

const PREFILL: CorrectionPrefill = {
  invoiceId: INVOICE_ID,
  firm: { id: FIRM, name: 'Alpha LLP' },
  coreRecord: { entityType: 'Matter', entityId: MATTER_ID, name: 'Acme v. Roadrunner' },
  invoiceStatus: 'InReview',
  trackingNumber: 'INV-9.1',
  firmInvoiceNumber: 'INV-9',
  canCorrect: true,
  reason: null,
};

describe('submit — a Correction (the 095f-2e2a form-field contract)', () => {
  const correction = { invoiceId: INVOICE_ID, note: 'The rates were wrong.' };
  const input = (over = {}) => ({
    record: MATTER,
    file: pdf(),
    firmInvoiceNumber: 'INV-9',
    firmId: FIRM,
    correction,
    ...over,
  });

  it('posts mode=Correction, invoiceId and correctionNote, each exactly once, with the file, the number and the firm', async () => {
    const t = make(() => reply(202, accepted));
    const result = await t.client.submit(input({ poNumber: 'PO-5' }));
    expect(result).toEqual(accepted);
    expect(t.calls).toHaveLength(1); // the firm is named: no firm lookup
    expect(t.calls[0].url).toBe(`${BASE}/api/finance/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM}`);
    expect(t.calls[0].init?.method).toBe('POST');
    const form = t.calls[0].init?.body as FormData;
    expect(form.getAll('mode')).toEqual(['Correction']);
    expect(form.getAll('invoiceId')).toEqual([INVOICE_ID]);
    expect(form.getAll('correctionNote')).toEqual(['The rates were wrong.']);
    expect(form.getAll('firmInvoiceNumber')).toEqual(['INV-9']);
    expect(form.getAll('poNumber')).toEqual(['PO-5']);
    expect((form.get('file') as File).name).toBe('invoice.pdf');
    // The content type is the browser's (a multipart boundary): never set by hand.
    expect(JSON.stringify(t.calls[0].init?.headers)).not.toMatch(/content-type/i);
  });

  it('must-not-fire: a new invoice sends none of the three fields (the server refuses a stray one)', async () => {
    const t = make(() => reply(202, accepted));
    await t.client.submit({ record: MATTER, file: pdf(), firmInvoiceNumber: 'INV-9', firmId: FIRM });
    const form = t.calls[0].init?.body as FormData;
    expect(form.has('mode')).toBe(false);
    expect(form.has('invoiceId')).toBe(false);
    expect(form.has('correctionNote')).toBe(false);
  });

  describe('refusals of the route, thrown (authenticatedFetch) and returned alike', () => {
    const cases: Array<[string, number, Record<string, unknown> | null, RegExp]> = [
      [
        '400 correction_note_required (the server detail wins)',
        400,
        {
          errorCode: 'ebilling.submission.correction_note_required',
          detail: 'A correction needs a note saying why the invoice is corrected.',
        },
        /^A correction needs a note saying why the invoice is corrected\.$/,
      ],
      [
        '400 correction_note_required with no detail',
        400,
        { errorCode: 'ebilling.submission.correction_note_required' },
        /A correction needs a note\. Nothing was saved\./,
      ],
      [
        '400 field error (a ValidationProblem: errors.correctionNote)',
        400,
        {
          title: 'One or more validation errors occurred.',
          errors: { correctionNote: ['correctionNote may be at most 2000 characters.'] },
        },
        /^correctionNote may be at most 2000 characters\.$/,
      ],
      [
        '404 uniform (no errorCode, a reasonCode, the server generic detail: the correction sentence is shown anyway)',
        404,
        { reasonCode: 'sdap.access.deny.record_unavailable', detail: 'The requested record was not found.' },
        /not available to you, or no longer exists, so it cannot be corrected\. Nothing was saved\./,
      ],
      [
        '409 correction_not_allowed / invoice_status',
        409,
        {
          errorCode: 'ebilling.submission.correction_not_allowed',
          reason: 'invoice_status',
          invoiceStatus: 'Approved',
        },
        /Only an invoice in review can be corrected; this one has moved on, so nothing was saved\./,
      ],
      [
        '409 correction_not_allowed / different_core_record',
        409,
        { errorCode: 'ebilling.submission.correction_not_allowed', reason: 'different_core_record' },
        /filed under another record/,
      ],
      [
        '409 correction_not_allowed with the server sentence',
        409,
        {
          errorCode: 'ebilling.submission.correction_not_allowed',
          reason: 'different_firm',
          detail: 'The server wrote this one.',
        },
        /^The server wrote this one\.$/,
      ],
      [
        '503 record_unavailable',
        503,
        { errorCode: 'ebilling.submission.record_unavailable' },
        /temporarily unavailable\. Try again shortly\./,
      ],
    ];

    it.each(cases)('%s', async (_name, status, problem, words) => {
      for (const failure of [() => refuse(status, problem), () => reply(status, problem)]) {
        const t = make(() => failure());
        const err = await t.client.submit(input()).catch((e: unknown) => e);
        expect(err).toBeInstanceOf(InvoiceSubmissionError);
        expect((err as InvoiceSubmissionError).statusCode).toBe(status);
        expect(invoiceSubmissionErrorMessage(err)).toMatch(words);
      }
    });

    it('keeps the `reason` of a 409 on the error', async () => {
      const t = make(() =>
        reply(409, { errorCode: 'ebilling.submission.correction_not_allowed', reason: 'no_submission' })
      );
      const err = (await t.client.submit(input()).catch((e: unknown) => e)) as InvoiceSubmissionError;
      expect(err.reason).toBe('no_submission');
      expect(err.errorCode).toBe('ebilling.submission.correction_not_allowed');
    });
  });

  it('must-not-fire: a 404 or a field error of a NEW invoice keeps the words it always had', async () => {
    const t404 = make(() => reply(404, { reasonCode: 'sdap.access.deny.record_unavailable' }));
    const e404 = await t404.client
      .submit({ record: MATTER, file: pdf(), firmInvoiceNumber: 'INV-9', firmId: FIRM })
      .catch((e: unknown) => e);
    expect(invoiceSubmissionErrorMessage(e404)).toBe('That submission was not found, or you do not have access to it.');
    const e400 = await make(() => reply(400, { errors: { mode: ['mode must be New or Correction.'] } }))
      .client.submit({ record: MATTER, file: pdf(), firmInvoiceNumber: 'INV-9', firmId: FIRM })
      .catch((e: unknown) => e);
    expect(invoiceSubmissionErrorMessage(e400)).toBe(
      'The submission was not accepted. Check the file and the details and try again.'
    );
  });
});

describe('loadCorrectionPrefill', () => {
  it('reads GET /api/finance/invoices/{id}/correction-prefill', async () => {
    const t = make(() => reply(200, PREFILL));
    await expect(t.client.loadCorrectionPrefill(INVOICE_ID)).resolves.toEqual(PREFILL);
    expect(t.calls[0].url).toBe(`${BASE}/api/finance/invoices/${INVOICE_ID}/correction-prefill`);
    expect(t.calls[0].init?.method).toBe('GET');
  });

  it('a body that is not a prefill is an error, never a partial answer', async () => {
    for (const body of [
      null,
      {},
      { invoiceId: INVOICE_ID },
      { ...PREFILL, canCorrect: 'yes' },
      { ...PREFILL, coreRecord: null },
      { ...PREFILL, firmInvoiceNumber: undefined },
      { ...PREFILL, firmInvoiceNumber: null },
    ]) {
      const t = make(() => reply(200, body));
      await expect(t.client.loadCorrectionPrefill(INVOICE_ID)).rejects.toBeInstanceOf(InvoiceSubmissionError);
    }
  });

  it('a 404 is the correction words, a 503 is "could not be read", thrown or returned', async () => {
    for (const failure of [
      () =>
        refuse(404, {
          reasonCode: 'sdap.access.deny.record_unavailable',
          detail: 'The requested record was not found.',
        }),
      () => reply(404, { detail: 'The requested record was not found.' }),
      () => reply(404, {}),
    ]) {
      const err = await make(failure)
        .client.loadCorrectionPrefill(INVOICE_ID)
        .catch((e: unknown) => e);
      expect(correctionPrefillErrorMessage(err)).toMatch(/not available to you, or no longer exists/);
    }
    for (const failure of [
      () => refuse(503, { errorCode: 'ebilling.submission.record_unavailable' }),
      () => reply(503, null),
    ]) {
      const err = await make(failure)
        .client.loadCorrectionPrefill(INVOICE_ID)
        .catch((e: unknown) => e);
      expect(correctionPrefillErrorMessage(err)).toMatch(
        /could not be read right now\. Try again shortly\. Nothing was saved\./
      );
    }
    const network = await make(() => new Error('Failed to fetch'))
      .client.loadCorrectionPrefill(INVOICE_ID)
      .catch((e: unknown) => e);
    expect(correctionPrefillErrorMessage(network)).toMatch(/could not be read, so it cannot be corrected/);
  });
});

describe('readCorrectionPrefill and correctionTargetFromPrefill', () => {
  it('reads a null firm, status and tracking number', () => {
    expect(readCorrectionPrefill({ ...PREFILL, firm: null, trackingNumber: null, invoiceStatus: null })).toEqual({
      ...PREFILL,
      firm: null,
      trackingNumber: null,
      invoiceStatus: null,
    });
  });

  it('an eligible invoice becomes the target: its record kind, id (lower-case, no braces), firm and number', () => {
    expect(
      correctionTargetFromPrefill({
        ...PREFILL,
        coreRecord: { ...PREFILL.coreRecord, entityId: `{${MATTER_ID.toUpperCase()}}` },
      })
    ).toEqual({
      ok: true,
      target: {
        invoiceId: INVOICE_ID,
        firmInvoiceNumber: 'INV-9',
        record: { kind: 'matter', recordId: MATTER_ID, name: 'Acme v. Roadrunner' },
        firmId: FIRM,
        firmName: 'Alpha LLP',
        trackingNumber: 'INV-9.1',
      },
    });
    expect(
      correctionTargetFromPrefill({ ...PREFILL, coreRecord: { ...PREFILL.coreRecord, entityType: 'WorkAssignment' } })
    ).toMatchObject({
      ok: true,
      target: { record: { kind: 'workassignment' } },
    });
  });

  it.each([
    ['invoice_status', /Only an invoice in review can be corrected/],
    ['no_submission', /no finished submission to correct/],
    ['submission_in_progress', /still being processed/],
  ])('canCorrect false (%s) is the plain reason', (reason, words) => {
    const launch = correctionTargetFromPrefill({ ...PREFILL, canCorrect: false, reason });
    expect(launch.ok).toBe(false);
    expect(launch.ok === false && launch.message).toMatch(words);
  });

  it.each([
    ['no firm', { firm: null }],
    ['no number', { firmInvoiceNumber: '   ' }],
    ['an unknown record kind', { coreRecord: { entityType: 'Thing', entityId: MATTER_ID, name: 'X' } }],
  ])('%s cannot be offered to the wizard', (_name, over) => {
    const launch = correctionTargetFromPrefill({ ...PREFILL, ...over });
    expect(launch.ok).toBe(false);
    expect(launch.ok === false && launch.message).toMatch(/cannot be corrected from here/);
  });
});
