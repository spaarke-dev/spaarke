import {
  createInvoiceReviewApi,
  firmsFailureMessage,
  isIncomplete,
  loadFailureMessage,
  routes,
  setFirmFailureMessage,
  toFailure,
  writeFailureMessage,
} from '../services/invoiceReviewApi';
import { parseLaunch } from '../services/launch';
// The real decoder, through the jest seam for the library barrel.
import { parseDataParams } from '@spaarke/ui-components';
import { AuthError, INVOICE_ID, PDF_ID, lineId, problem, review } from './testKit';

const BASE = 'https://bff.example.test/';

function okResponse(body: unknown) {
  return {
    ok: true,
    status: 200,
    json: async () => body,
    arrayBuffer: async () => new ArrayBuffer(4),
  };
}

function fetchReturning(body: unknown) {
  return jest.fn(async (_url: string, _init?: RequestInit) => okResponse(body) as never);
}

describe('routes (tasks 100, 101, 092)', () => {
  it('builds every route the page calls, ids lower-cased and without a doubled slash', () => {
    const inv = INVOICE_ID.toUpperCase();
    expect(routes.review(BASE, inv)).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/review`);
    expect(routes.decideLine(BASE, INVOICE_ID, lineId(3))).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/lines/${lineId(3)}/decision`);
    expect(routes.decideLines(BASE, INVOICE_ID)).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/lines/decisions`);
    expect(routes.acceptClean(BASE, INVOICE_ID)).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/lines/accept-clean`);
    expect(routes.correctLine(BASE, INVOICE_ID, lineId(3))).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/lines/${lineId(3)}/corrections`);
    expect(routes.correctHeader(BASE, INVOICE_ID)).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/header/corrections`);
    expect(routes.corrections(BASE, INVOICE_ID)).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/corrections`);
    expect(routes.fileContent(BASE, PDF_ID)).toBe(`https://bff.example.test/api/finance/files/${PDF_ID}/content`);
  });
});

describe('createInvoiceReviewApi', () => {
  it('reads the review and refuses a body that is not the read model', async () => {
    const good = createInvoiceReviewApi(fetchReturning(review([])), BASE);
    expect(await good.getReview(INVOICE_ID)).toEqual({ ok: true, value: review([]) });

    const bad = createInvoiceReviewApi(fetchReturning({ lines: 'nope' }), BASE);
    const r = await bad.getReview(INVOICE_ID);
    expect(r.ok).toBe(false);
  });

  it('posts each decision with the client decision id and nothing the server owns', async () => {
    const fetchFn = fetchReturning({ decisionId: 'd', lines: [], totals: null });
    const api = createInvoiceReviewApi(fetchFn, BASE);

    await api.decideLine(INVOICE_ID, lineId(1), { decisionId: 'd1', action: 'reduce', reason: 'rate', approvedAmount: 1012.5 });
    await api.decideLines(INVOICE_ID, { decisionId: 'd2', action: 'disallow', reason: 'dup', lines: [{ lineId: lineId(1), approvedAmount: null }] });
    await api.acceptClean(INVOICE_ID, 'd3');

    const bodies = fetchFn.mock.calls.map(c => [c[0], c[1]?.method, JSON.parse(String(c[1]?.body))]);
    expect(bodies).toEqual([
      [routes.decideLine(BASE, INVOICE_ID, lineId(1)), 'POST', { decisionId: 'd1', action: 'reduce', reason: 'rate', approvedAmount: 1012.5 }],
      [routes.decideLines(BASE, INVOICE_ID), 'POST', { decisionId: 'd2', action: 'disallow', reason: 'dup', lines: [{ lineId: lineId(1), approvedAmount: null }] }],
      [routes.acceptClean(BASE, INVOICE_ID), 'POST', { decisionId: 'd3' }],
    ]);
  });

  it('posts a correction (value or confirm) to the line or header route and reads the corrections list', async () => {
    const fetchFn = jest.fn(async (url: string, _init?: RequestInit) =>
      (url.endsWith('/corrections') && !url.includes('/lines/') && !url.includes('/header/')
        ? okResponse({ corrections: [{ id: 'c', field: 'Hours' }] })
        : okResponse({ correction: { id: 'c' }, alreadyApplied: false })) as never
    );
    const api = createInvoiceReviewApi(fetchFn, BASE);

    await api.correctLine(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false });
    await api.correctLine(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true });
    await api.correctHeader(INVOICE_ID, { field: 'InvoiceDate', value: '2025-04-02', confirm: false });
    const list = await api.listCorrections(INVOICE_ID);

    expect(JSON.parse(String(fetchFn.mock.calls[0][1]?.body))).toEqual({ field: 'Hours', value: '2.5', confirm: false });
    expect(JSON.parse(String(fetchFn.mock.calls[1][1]?.body))).toEqual({ field: 'Hours', value: null, confirm: true });
    expect(fetchFn.mock.calls[2][0]).toBe(routes.correctHeader(BASE, INVOICE_ID));
    expect(list).toEqual({ ok: true, value: [{ id: 'c', field: 'Hours' }] });
  });

  it('turns a thrown ProblemDetails into a failure that keeps the stable code, the detail and the failed step', async () => {
    const fetchFn = jest.fn(async () => {
      throw problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals' });
    });
    const api = createInvoiceReviewApi(fetchFn as never, BASE);
    const r = await api.decideLine(INVOICE_ID, lineId(1), { decisionId: 'd', action: 'accept', reason: null, approvedAmount: null });
    expect(r).toEqual({
      ok: false,
      failure: { status: 503, errorCode: 'ebilling.decision.incomplete', detail: 'The lines were saved; the totals were not.', signInFailed: false, failedStep: 'totals', committedLineIds: [] },
    });
    expect(!r.ok && isIncomplete(r.failure)).toBe(true);
  });

  it('keeps the committed line ids of a 503 incomplete decision (the accept-clean repeat needs them)', async () => {
    const fetchFn = jest.fn(async () => {
      throw problem(503, 'ebilling.decision.incomplete', { committedLineIds: [lineId(1), lineId(2), 7, ''], failedStep: 'totals' });
    });
    const api = createInvoiceReviewApi(fetchFn as never, BASE);
    const r = await api.acceptClean(INVOICE_ID, 'd');
    expect(!r.ok && r.failure.committedLineIds).toEqual([lineId(1), lineId(2)]);
  });

  it('does not show the browser text of a request that got no server answer: a write and the PDF both read as status 0', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const fetchFn = jest.fn(async () => {
        throw new TypeError('Failed to fetch');
      });
      const api = createInvoiceReviewApi(fetchFn as never, BASE);

      const write = await api.decideLine(INVOICE_ID, lineId(1), { decisionId: 'd', action: 'accept', reason: null, approvedAmount: null });
      expect(!write.ok && write.failure).toMatchObject({ status: 0, errorCode: null, detail: null });
      expect(!write.ok && writeFailureMessage(write.failure)).toBe('The change could not be sent. Check your connection and retry.');

      const pdf = await api.fetchFile(PDF_ID);
      expect(!pdf.ok && pdf.failure).toMatchObject({ status: 0, detail: null });
      expect(!pdf.ok && writeFailureMessage(pdf.failure)).not.toMatch(/Failed to fetch/);

      // The raw text goes to the console, not to the user.
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('InvoiceReview'), 'Failed to fetch');
    } finally {
      warn.mockRestore();
    }
  });

  it('fetches the invoice PDF bytes through the content route', async () => {
    const fetchFn = fetchReturning(null);
    const api = createInvoiceReviewApi(fetchFn, BASE);
    const r = await api.fetchFile(PDF_ID);
    expect(r.ok).toBe(true);
    expect(fetchFn.mock.calls[0][0]).toBe(routes.fileContent(BASE, PDF_ID));
  });
});

describe('Set firm and the linked firms (095d-3)', () => {
  const FIRM_ID = 'a1a1a1a1-0000-4000-8000-0000000000a1';
  const MATTER = 'aaaaaaaa-0000-4000-8000-0000000000a1';

  it('builds the two routes: the section 8 firms of a core record, and the invoice firm', () => {
    expect(routes.coreRecordFirms(BASE, 'workAssignment', MATTER.toUpperCase())).toBe(`https://bff.example.test/api/finance/core-records/workAssignment/${MATTER}/firms`);
    expect(routes.assignFirm(BASE, INVOICE_ID.toUpperCase())).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/firm`);
  });

  it('lists the firms of the core record with a GET, reading each row as the add-in does (a row without a string id and name is dropped)', async () => {
    const fetchFn = fetchReturning([{ firmId: FIRM_ID, name: 'Alpha Law LLP', extra: 1 }, { firmId: 5, name: 'x' }, { name: 'no id' }, null]);
    const api = createInvoiceReviewApi(fetchFn, BASE);
    const r = await api.listFirms('matter', MATTER);
    expect(r).toEqual({ ok: true, value: [{ firmId: FIRM_ID, name: 'Alpha Law LLP' }] });
    expect(fetchFn).toHaveBeenCalledTimes(1);
    expect(fetchFn.mock.calls[0][0]).toBe(routes.coreRecordFirms(BASE, 'matter', MATTER));
    expect(fetchFn.mock.calls[0][1]?.method).toBe('GET');
    expect(fetchFn.mock.calls[0][1]?.body).toBeUndefined();
  });

  it('an empty 200 is an empty list, and a body that is not a list is a failure (never an empty list)', async () => {
    expect(await createInvoiceReviewApi(fetchReturning([]), BASE).listFirms('project', MATTER)).toEqual({ ok: true, value: [] });
    const bad = await createInvoiceReviewApi(fetchReturning({ firms: [] }), BASE).listFirms('project', MATTER);
    expect(bad.ok).toBe(false);
  });

  it('maps a refused or unreachable read to a failure with the status', async () => {
    const refused = await createInvoiceReviewApi(jest.fn(async () => { throw problem(404, null); }) as never, BASE).listFirms('matter', MATTER);
    expect(refused.ok).toBe(false);
    if (!refused.ok) {
      expect(refused.failure.status).toBe(404);
      expect(firmsFailureMessage(refused.failure)).toMatch(/isn't available to you/);
    }
    const unavailable = await createInvoiceReviewApi(jest.fn(async () => { throw problem(503, null); }) as never, BASE).listFirms('matter', MATTER);
    expect(unavailable.ok === false && firmsFailureMessage(unavailable.failure)).toMatch(/couldn't be listed/);
    expect(unavailable.ok === false && firmsFailureMessage(unavailable.failure)).not.toMatch(/No firm/);
    expect(firmsFailureMessage(toFailure(new AuthError('no token')))).toMatch(/Sign-in/);
  });

  it('Set firm posts exactly { firmId, note: null } to the invoice firm route and returns the answer as is', async () => {
    const answer = { outcome: 'assigned', decisionId: 'd1', trackingNumber: 'ALPHA-000042', previousTrackingNumber: null, rulesRerun: 'pending' };
    const fetchFn = fetchReturning(answer);
    const api = createInvoiceReviewApi(fetchFn, BASE);
    const r = await api.setFirm(INVOICE_ID, FIRM_ID);
    expect(r).toEqual({ ok: true, value: answer });
    expect(fetchFn).toHaveBeenCalledTimes(1);
    expect(fetchFn.mock.calls[0][0]).toBe(routes.assignFirm(BASE, INVOICE_ID));
    expect(fetchFn.mock.calls[0][1]?.method).toBe('POST');
    expect(JSON.parse(String(fetchFn.mock.calls[0][1]?.body))).toEqual({ firmId: FIRM_ID, note: null });
    expect(String(fetchFn.mock.calls[0][1]?.body)).toBe(JSON.stringify({ firmId: FIRM_ID, note: null }));
  });

  it('Set firm maps a refusal to a failure carrying the server detail and code, and no answer to status 0', async () => {
    const refused = await createInvoiceReviewApi(
      jest.fn(async () => { throw problem(409, 'ebilling.assign_firm.firm_already_set', { detail: 'This invoice already has a firm.' }); }) as never,
      BASE
    ).setFirm(INVOICE_ID, FIRM_ID);
    expect(refused.ok).toBe(false);
    if (!refused.ok) {
      expect(refused.failure).toMatchObject({ status: 409, errorCode: 'ebilling.assign_firm.firm_already_set', detail: 'This invoice already has a firm.' });
      expect(setFirmFailureMessage(refused.failure)).toBe('This invoice already has a firm.');
    }

    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const down = await createInvoiceReviewApi(jest.fn(async () => { throw new TypeError('Failed to fetch'); }) as never, BASE).setFirm(INVOICE_ID, FIRM_ID);
    warn.mockRestore();
    expect(down.ok).toBe(false);
    if (!down.ok) {
      expect(down.failure.status).toBe(0);
      expect(setFirmFailureMessage(down.failure)).toBe('The firm could not be confirmed. Check the invoice header; if no firm is shown, try again.');
      expect(setFirmFailureMessage(down.failure)).not.toMatch(/Nothing was changed/);
    }
    // A sign-in failure keeps the sign-in sentence, not the nothing-was-changed one.
    expect(setFirmFailureMessage(toFailure(new AuthError('no token')))).toMatch(/Sign-in/);
    // A refusal without a detail falls back to the shared write sentence.
    expect(setFirmFailureMessage(toFailure(problem(500, null)))).toBe(writeFailureMessage(toFailure(problem(500, null))));
  });
});

describe('failure messages', () => {
  it('shows the server detail of a refused write, and a fixed sentence without one', () => {
    expect(writeFailureMessage(toFailure(problem(400, 'ebilling.line.reason_required', { detail: 'A reason is required to reduce a line.' })))).toBe(
      'A reason is required to reduce a line.'
    );
    expect(writeFailureMessage(toFailure(problem(409, 'ebilling.invoice.busy')))).toMatch(/changed or cannot be changed/);
    expect(writeFailureMessage(toFailure(problem(503, 'ebilling.correction.incomplete', { detail: 'Saved; findings not updated.' })))).toBe(
      'Saved; findings not updated. Repeat it to finish.'
    );
  });

  it('names the kill switch, no access and sign-in on a failed read', () => {
    expect(loadFailureMessage(toFailure(problem(503, 'ebilling.disabled')))).toBe('eBilling is not enabled in this environment.');
    expect(loadFailureMessage(toFailure(problem(403, null)))).toBe('You do not have access to this invoice.');
    expect(loadFailureMessage(toFailure(new AuthError('no token')))).toMatch(/Sign-in/);
  });
});

describe('parseLaunch', () => {
  it('takes the invoice id from the data envelope, the URL or the form id, and refuses anything else', () => {
    expect(parseLaunch({ invoiceId: `{${INVOICE_ID.toUpperCase()}}` })).toEqual({ ok: true, invoiceId: INVOICE_ID });
    expect(parseLaunch({ id: INVOICE_ID, typename: 'sprk_invoice' })).toEqual({ ok: true, invoiceId: INVOICE_ID });
    expect(parseLaunch({ id: INVOICE_ID, typename: 'sprk_matter' }).ok).toBe(false);
    expect(parseLaunch({}).ok).toBe(false);
    expect(parseLaunch({ invoiceId: 'not-a-guid' }).ok).toBe(false);
  });

  // EB-D-194: the optional lineId is a hint and never blocks the invoice.
  const LINE = lineId(2);

  it('reads a lineId from the URL or from the Xrm data envelope', () => {
    expect(parseLaunch(parseDataParams(`?invoiceId=${INVOICE_ID}&lineId=${LINE}`))).toEqual({ ok: true, invoiceId: INVOICE_ID, lineId: LINE });
    expect(parseLaunch(parseDataParams(`?data=${encodeURIComponent(`invoiceId=${INVOICE_ID}&lineId=${LINE}`)}`))).toEqual({
      ok: true,
      invoiceId: INVOICE_ID,
      lineId: LINE,
    });
    expect(parseLaunch({ invoiceId: INVOICE_ID, lineid: LINE })).toEqual({ ok: true, invoiceId: INVOICE_ID, lineId: LINE });
  });

  it('leaves lineId undefined when it is missing, or present but not a GUID, and still opens the invoice', () => {
    for (const params of [{ invoiceId: INVOICE_ID }, { invoiceId: INVOICE_ID, lineId: 'not-a-guid' }, { invoiceId: INVOICE_ID, lineId: '' }]) {
      const result = parseLaunch(params);
      expect(result).toMatchObject({ ok: true, invoiceId: INVOICE_ID });
      expect(result.ok && result.lineId).toBeUndefined();
    }
  });

  it('normalises a lineId with braces and upper case', () => {
    expect(parseLaunch({ invoiceId: INVOICE_ID, lineId: `{${LINE.toUpperCase()}}` })).toEqual({ ok: true, invoiceId: INVOICE_ID, lineId: LINE });
  });
});
