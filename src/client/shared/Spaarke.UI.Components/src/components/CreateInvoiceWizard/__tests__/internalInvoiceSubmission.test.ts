/**
 * internalInvoiceSubmission tests (ebilling-r1 task 052): the adapter between the shared InvoiceSubmission wizard and the BFF's
 * internal routes (tasks 044, 105). Every assertion is about what goes on the wire and what the caller of the adapter gets back.
 *
 * The injected fetch is a fake that answers by route. It either RETURNS a response (`reply`) or THROWS an ApiError (`refuse`), the
 * two ways a host's fetch fails, and both are covered.
 */
import * as fs from 'fs';
import * as path from 'path';
import type { IDataService } from '../../../types/serviceInterfaces';
import { FIRM_REQUIRED_ERROR_CODE, InvoiceSubmissionError } from '../../InvoiceSubmission';
import type { InvoiceCoreRecordOption } from '../../InvoiceSubmission';
import {
  BLOCKED_REASON_CODE,
  NO_FIRM_LINKED_ERROR_CODE,
  RECORD_PAGE_SIZE,
  blockedDecisionErrorMessage,
  createInternalInvoiceSubmission,
  isBlockedStatus,
  resolveInitialInvoiceRecord,
  toInvoiceSubmissionError,
} from '../internalInvoiceSubmission';

const BASE = 'https://bff.example.test';
const MATTER_ID = '11111111-1111-4111-8111-111111111111';
const PROJECT_ID = '22222222-2222-4222-8222-222222222222';
const WA_ID = '44444444-4444-4444-8444-444444444444';
const FIRM_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const FIRM_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: MATTER_ID,
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};

type Handler = (url: string, init?: RequestInit) => unknown;

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

function fakeFetch(handler: Handler) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fn = jest.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, init });
    const out = handler(url, init);
    if (out instanceof Error) throw out;
    return out as Response;
  });
  return { fn, calls };
}

function fakeData(rowsByEntity: Record<string, Record<string, unknown>[] | Error>) {
  const retrieveMultipleRecords = jest.fn(async (entity: string) => {
    const rows = rowsByEntity[entity];
    if (rows instanceof Error) throw rows;
    return { entities: rows ?? [] };
  });
  const data = {
    retrieveMultipleRecords,
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
    retrieveRecord: jest.fn(),
  } as unknown as IDataService;
  return { data, retrieveMultipleRecords };
}

const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });

function make(handler: Handler, opts: Partial<Parameters<typeof createInternalInvoiceSubmission>[0]> = {}, rows = {}) {
  const f = fakeFetch(handler);
  const d = fakeData(rows);
  const client = createInternalInvoiceSubmission({
    fetch: f.fn,
    baseUrl: `${BASE}/`,
    dataService: d.data,
    ...opts,
  });
  return { client, ...f, ...d };
}

describe('resolveInitialInvoiceRecord', () => {
  it('maps the three core-record entities and nothing else', () => {
    expect(
      resolveInitialInvoiceRecord({
        entityType: 'sprk_matter',
        recordId: `{${MATTER_ID.toUpperCase()}}`,
        recordName: 'M',
      })
    ).toEqual({ kind: 'matter', recordId: MATTER_ID, name: 'M' });
    expect(
      resolveInitialInvoiceRecord({ entityType: 'sprk_project', recordId: PROJECT_ID, recordName: 'P' })?.kind
    ).toBe('project');
    expect(
      resolveInitialInvoiceRecord({ entityType: 'sprk_workassignment', recordId: WA_ID, recordName: 'W' })?.kind
    ).toBe('workassignment');
    // Negative: another entity, or nothing.
    expect(resolveInitialInvoiceRecord({ entityType: 'sprk_event', recordId: WA_ID, recordName: 'E' })).toBeUndefined();
    expect(resolveInitialInvoiceRecord(undefined)).toBeUndefined();
  });
});

describe('isBlockedStatus', () => {
  it('is true only for Rejected with the blocked code', () => {
    expect(isBlockedStatus({ status: 'Rejected', outcomeReasonCode: BLOCKED_REASON_CODE })).toBe(true);
    expect(isBlockedStatus({ status: 'Rejected', outcomeReasonCode: 'ebilling.submission.returned' })).toBe(false);
    expect(isBlockedStatus({ status: 'Accepted', outcomeReasonCode: BLOCKED_REASON_CODE })).toBe(false);
    expect(isBlockedStatus({ status: 'Rejected' })).toBe(false);
  });
});

describe('loadRecords', () => {
  const rows = {
    sprk_matter: [{ sprk_matterid: MATTER_ID, sprk_mattername: 'Acme v. Roadrunner', sprk_matternumber: 'M-1001' }],
    sprk_project: [{ sprk_projectid: PROJECT_ID, sprk_name: 'Patent portfolio', sprk_referencenumber: 'P-77' }],
    sprk_workassignment: [{ sprk_workassignmentid: WA_ID, sprk_name: 'Review', sprk_workassignmentnumber: 'WA-3' }],
  };

  it('reads matters, projects and work assignments (the whole core-record set) and writes nothing', async () => {
    const t = make(() => reply(200, []), {}, rows);
    const list = await t.client.loadRecords({});
    expect(list.records.map(r => [r.kind, r.name, r.number])).toEqual([
      ['matter', 'Acme v. Roadrunner', 'M-1001'],
      ['project', 'Patent portfolio', 'P-77'],
      ['workassignment', 'Review', 'WA-3'],
    ]);
    expect(list.partial).toBe(false);
    expect(list.truncated).toBe(false);
    expect(t.retrieveMultipleRecords.mock.calls.map(c => c[0]).sort()).toEqual([
      'sprk_matter',
      'sprk_project',
      'sprk_workassignment',
    ]);
    expect(t.calls).toHaveLength(0); // no BFF call to list records
    // Active rows only, and a bounded page.
    expect(String(t.retrieveMultipleRecords.mock.calls[0][1])).toContain('statecode eq 0');
    expect(String(t.retrieveMultipleRecords.mock.calls[0][1])).toContain(`$top=${RECORD_PAGE_SIZE}`);
  });

  it('a source that fails is flagged partial and the others are still offered; all failing is an error', async () => {
    const t = make(() => reply(200, []), {}, { ...rows, sprk_project: new Error('boom') });
    const list = await t.client.loadRecords({});
    expect(list.partial).toBe(true);
    expect(list.records.map(r => r.kind)).toEqual(['matter', 'workassignment']);

    const none = make(
      () => reply(200, []),
      {},
      {
        sprk_matter: new Error('a'),
        sprk_project: new Error('b'),
        sprk_workassignment: new Error('c'),
      }
    );
    await expect(none.client.loadRecords({})).rejects.toBeInstanceOf(InvoiceSubmissionError);
  });

  it('a full page is flagged truncated', async () => {
    const many = Array.from({ length: RECORD_PAGE_SIZE }, (_, i) => ({
      sprk_matterid: `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`,
      sprk_mattername: `M${i}`,
      sprk_matternumber: `N${i}`,
    }));
    const t = make(() => reply(200, []), {}, { sprk_matter: many });
    expect((await t.client.loadRecords({})).truncated).toBe(true);
  });

  it('the record the wizard was launched from is offered even when it is not in the first page', async () => {
    const t = make(() => reply(200, []), { initialRecord: MATTER }, {});
    expect((await t.client.loadRecords({})).records).toEqual([MATTER]);
  });

  it('a locked launch offers only its record and reads nothing', async () => {
    const t = make(() => reply(200, []), { initialRecord: MATTER, lockRecord: true }, rows);
    expect(await t.client.loadRecords({})).toEqual({ records: [MATTER] });
    expect(t.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('lockRecord without a record locks nothing (control)', async () => {
    const t = make(() => reply(200, []), { lockRecord: true }, rows);
    expect((await t.client.loadRecords({})).records).toHaveLength(3);
  });
});

describe('loadFirms', () => {
  const firmsRoute = `${BASE}/api/finance/core-records/Matter/${MATTER_ID}/firms`;

  it('is unknown (null) until a record is known, and asks nothing', async () => {
    const t = make(() => reply(200, []));
    expect(await t.client.loadFirms()).toBeNull();
    expect(t.calls).toHaveLength(0);
  });

  it('a locked launch reads the firms linked to its record (D-106) through the internal route', async () => {
    const t = make(
      () =>
        reply(200, [
          { firmId: FIRM_A, name: 'Alpha LLP' },
          { firmId: FIRM_B, name: 'Beta LLP' },
        ]),
      {
        initialRecord: MATTER,
        lockRecord: true,
      }
    );
    expect(await t.client.loadFirms()).toEqual([
      { firmId: FIRM_A, name: 'Alpha LLP' },
      { firmId: FIRM_B, name: 'Beta LLP' },
    ]);
    expect(t.calls.map(c => c.url)).toEqual([firmsRoute]);
  });

  it('a failed read rejects (never an empty list that would read as "no firm")', async () => {
    const t = make(() => refuse(503, { errorCode: 'x' }), { initialRecord: MATTER, lockRecord: true });
    await expect(t.client.loadFirms()).rejects.toMatchObject({ statusCode: 503 });
  });
});

describe('submit', () => {
  const accepted = { submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.1', statusUrl: '/x' };
  const input = (over = {}) => ({ record: MATTER, file: pdf(), firmInvoiceNumber: 'INV-9', ...over });

  it('posts the PDF and the details to the internal matter route with the chosen firm as a query parameter', async () => {
    const t = make(() => reply(202, accepted));
    const result = await t.client.submit(input({ firmId: FIRM_B, poNumber: 'PO-5' }));
    expect(result).toEqual(accepted);
    expect(t.calls).toHaveLength(1); // a chosen firm needs no firm lookup
    const call = t.calls[0];
    expect(call.url).toBe(`${BASE}/api/finance/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM_B}`);
    expect(call.init?.method).toBe('POST');
    const form = call.init?.body as FormData;
    expect((form.get('file') as File).name).toBe('invoice.pdf');
    expect(form.get('firmInvoiceNumber')).toBe('INV-9');
    expect(form.get('poNumber')).toBe('PO-5');
    // The content type is the browser's (a multipart boundary): never set by hand.
    expect(JSON.stringify(call.init?.headers)).not.toMatch(/content-type/i);
  });

  it.each([
    ['project', PROJECT_ID, 'projects'],
    ['workassignment', WA_ID, 'workassignments'],
  ] as const)('uses the %s route', async (kind, id, segment) => {
    const t = make(() => reply(202, accepted));
    await t.client.submit(input({ record: { kind, recordId: id, name: 'X' }, firmId: FIRM_A }));
    expect(t.calls[0].url).toBe(`${BASE}/api/finance/${segment}/${id}/invoice-submissions?firmId=${FIRM_A}`);
  });

  it('sends no poNumber when there is none', async () => {
    const t = make(() => reply(202, accepted));
    await t.client.submit(input({ firmId: FIRM_A }));
    expect((t.calls[0].init?.body as FormData).has('poNumber')).toBe(false);
  });

  it('names the only firm of the record itself (the server never picks one)', async () => {
    const t = make(url =>
      url.endsWith('/firms') ? reply(200, [{ firmId: FIRM_A, name: 'Alpha LLP' }]) : reply(202, accepted)
    );
    await t.client.submit(input());
    expect(t.calls.map(c => c.url)).toEqual([
      `${BASE}/api/finance/core-records/Matter/${MATTER_ID}/firms`,
      `${BASE}/api/finance/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM_A}`,
    ]);
  });

  it('a record with no linked firm is refused before any upload', async () => {
    const t = make(url => (url.endsWith('/firms') ? reply(200, []) : reply(202, accepted)));
    await expect(t.client.submit(input())).rejects.toMatchObject({ errorCode: NO_FIRM_LINKED_ERROR_CODE });
    expect(t.calls.every(c => c.init?.method !== 'POST')).toBe(true);
  });

  it('several linked firms ask the wizard to let the user choose (firm_required), then loadFirms offers exactly those firms', async () => {
    const firms = [
      { firmId: FIRM_A, name: 'Alpha LLP' },
      { firmId: FIRM_B, name: 'Beta LLP' },
    ];
    const t = make(url => (url.endsWith('/firms') ? reply(200, firms) : reply(202, accepted)));
    await expect(t.client.submit(input())).rejects.toMatchObject({ errorCode: FIRM_REQUIRED_ERROR_CODE });
    expect(t.calls.every(c => c.init?.method !== 'POST')).toBe(true);
    expect(await t.client.loadFirms()).toEqual(firms);
    // The firms of the record were read once, not again for the second question.
    expect(t.calls.filter(c => c.url.endsWith('/firms'))).toHaveLength(1);
  });

  it('a failed firm read is an error, never "no firm" and never a submit without the firm', async () => {
    const t = make(url =>
      url.endsWith('/firms')
        ? refuse(503, { errorCode: 'ebilling.submission.record_unavailable' })
        : reply(202, accepted)
    );
    await expect(t.client.submit(input())).rejects.toMatchObject({ statusCode: 503 });
    expect(t.calls.every(c => c.init?.method !== 'POST')).toBe(true);
  });

  it('shows the server refusal by its code when the fetch THROWS (authenticatedFetch)', async () => {
    const t = make(() =>
      refuse(403, { errorCode: 'ebilling.submission.firm_not_linked', detail: 'The firm you chose is not linked.' })
    );
    await expect(t.client.submit(input({ firmId: FIRM_A }))).rejects.toMatchObject({
      statusCode: 403,
      errorCode: 'ebilling.submission.firm_not_linked',
      detail: 'The firm you chose is not linked.',
    });
  });

  it('shows the server refusal by its code when the fetch RETURNS a non-2xx response', async () => {
    const t = make(() => reply(413, { errorCode: 'ebilling.submission.too_large' }));
    await expect(t.client.submit(input({ firmId: FIRM_A }))).rejects.toMatchObject({
      statusCode: 413,
      errorCode: 'ebilling.submission.too_large',
    });
  });

  it('a network failure carries no status (the wizard says it is unconfirmed)', async () => {
    const t = make(() => new Error('Failed to fetch'));
    const err = await t.client.submit(input({ firmId: FIRM_A })).catch(e => e);
    expect(err).toBeInstanceOf(InvoiceSubmissionError);
    expect(err.statusCode).toBeUndefined();
  });
});

describe('getStatus, getBlocked and decide', () => {
  it('reads the status of a submission from the internal route', async () => {
    const view = { submissionId: SUBMISSION_ID, status: 'Rejected', outcomeReasonCode: BLOCKED_REASON_CODE };
    const t = make(() => reply(200, view));
    expect(await t.client.getStatus(SUBMISSION_ID)).toEqual(view);
    expect(t.calls[0].url).toBe(`${BASE}/api/finance/invoice-submissions/${SUBMISSION_ID}`);
  });

  it('reads the held submission (the server says canOverride / canReturn)', async () => {
    const view = { submissionId: SUBMISSION_ID, state: 'Blocked', canOverride: false, canReturn: true, findings: [] };
    const t = make(() => reply(200, view));
    expect(await t.client.getBlocked(SUBMISSION_ID)).toEqual(view);
    expect(t.calls[0].url).toBe(`${BASE}/api/finance/submissions/${SUBMISSION_ID}/blocked`);
  });

  it('posts only the reason to the override and return routes', async () => {
    const t = make(() => reply(200, { submissionId: SUBMISSION_ID, outcome: 'Overridden', alreadyDecided: false }));
    await t.client.decide('override', SUBMISSION_ID, 'Negotiated rate');
    await t.client.decide('return', SUBMISSION_ID, 'Wrong matter');
    expect(t.calls.map(c => c.url)).toEqual([
      `${BASE}/api/finance/submissions/${SUBMISSION_ID}/override`,
      `${BASE}/api/finance/submissions/${SUBMISSION_ID}/return`,
    ]);
    expect(t.calls.map(c => JSON.parse(String(c.init?.body)))).toEqual([
      { reason: 'Negotiated rate' },
      { reason: 'Wrong matter' },
    ]);
  });

  it('a 403 from the override call is an error carrying the server code (FR-19), thrown or returned', async () => {
    const problem = { errorCode: 'ebilling.override.not_permitted', reasonCode: 'ebilling.override.not_permitted' };
    for (const handler of [() => refuse(403, problem), () => reply(403, problem)]) {
      const t = make(handler);
      const err = await t.client.decide('override', SUBMISSION_ID, 'r').catch(e => e);
      expect(err).toBeInstanceOf(InvoiceSubmissionError);
      expect(err).toMatchObject({ statusCode: 403, errorCode: 'ebilling.override.not_permitted' });
      expect(blockedDecisionErrorMessage(err)).toMatch(/do not hold the eBilling Override privilege/i);
    }
  });
});

describe('blockedDecisionErrorMessage', () => {
  it('prefers the server detail, then the code, then the status; an unknown failure says it is unconfirmed', () => {
    expect(blockedDecisionErrorMessage(new InvoiceSubmissionError({ statusCode: 409, detail: 'Server words.' }))).toBe(
      'Server words.'
    );
    expect(
      blockedDecisionErrorMessage(
        new InvoiceSubmissionError({ statusCode: 409, errorCode: 'ebilling.blocked.already_returned' })
      )
    ).toMatch(/already returned/i);
    expect(blockedDecisionErrorMessage(new InvoiceSubmissionError({ statusCode: 503 }))).toMatch(/unavailable/i);
    expect(blockedDecisionErrorMessage(new Error('x'))).toMatch(/not known whether the decision was recorded/i);
  });
});

describe('toInvoiceSubmissionError', () => {
  it('keeps an InvoiceSubmissionError as it is and reads an ApiError by status and problem details', () => {
    const own = new InvoiceSubmissionError({ statusCode: 400 });
    expect(toInvoiceSubmissionError(own)).toBe(own);
    expect(toInvoiceSubmissionError(refuse(409, { errorCode: 'c', detail: 'd' }))).toMatchObject({
      statusCode: 409,
      errorCode: 'c',
      detail: 'd',
    });
    expect(toInvoiceSubmissionError(refuse(500, null))).toMatchObject({ statusCode: 500 });
  });
});

describe('ADR-002 / FR-17: no client path creates an invoice', () => {
  /** Every source file of the client tree (not node_modules, not tests). */
  function sourceFiles(dir: string, out: string[] = []): string[] {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (
        entry.name === 'node_modules' ||
        entry.name === 'dist' ||
        entry.name === '__tests__' ||
        entry.name === 'coverage'
      )
        continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) sourceFiles(full, out);
      else if (/\.(ts|tsx)$/.test(entry.name) && !/\.(test|spec)\./.test(entry.name)) out.push(full);
    }
    return out;
  }

  it('finds no createRecord of sprk_invoice or sprk_invoicesubmission and no invoiceService.createInvoice in src/client', () => {
    const clientRoot = path.resolve(__dirname, '../../../../../..'); // .../src/client
    expect(path.basename(clientRoot)).toBe('client');
    const files = sourceFiles(clientRoot);
    expect(files.length).toBeGreaterThan(100);
    const offenders: string[] = [];
    for (const file of files) {
      const text = fs.readFileSync(file, 'utf8');
      if (/createRecord\(\s*['"`]sprk_invoice(submission)?['"`]/.test(text))
        offenders.push(`${file}: createRecord of sprk_invoice`);
      if (/invoiceService\.createInvoice|\.createInvoice\(/.test(text)) offenders.push(`${file}: createInvoice`);
    }
    expect(offenders).toEqual([]);
  });

  it('the wizard folder holds no Dataverse write call at all', () => {
    const dir = path.resolve(__dirname, '..');
    for (const f of fs.readdirSync(dir).filter(n => /\.tsx?$/.test(n))) {
      const text = fs.readFileSync(path.join(dir, f), 'utf8');
      expect(text).not.toMatch(/\.(createRecord|updateRecord|deleteRecord)\(/);
      const imports = text.match(/^\s*(?:import|export)[^;]*?from\s+['"][^'"]+['"]/gm) ?? [];
      for (const line of imports) expect(line).not.toMatch(/@spaarke\/auth|msal|xrm/i);
      expect(text).not.toMatch(/window\.Xrm/);
    }
  });
});
