// InvoiceSubmitPage (ebilling-r1 task 051, FR-18 / FR-19 / FR-45): the real page, the shared InvoiceSubmissionWizard,
// WizardShell, FileUploadZone, web-api-client, bff-client and backend selection run. fetch and the token acquirer (the
// seam each plane's bootstrap points at its own strategy) are fakes: a URL-routed BFF that records every request.
// Every assertion is about what goes over the wire and what the user sees.
import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';

// teams-js fetches its valid-domains list when first loaded; a fake keeps the recorded traffic to the SPA's own calls.
vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: vi.fn(),
    getContext: vi.fn(),
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(),
    notifyFailure: vi.fn(),
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => false) },
  authentication: { getAuthToken: vi.fn() },
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect: vi.fn(async () => undefined) }, accounts: [], inProgress: 'none' }),
}));

// The Invoices widget test needs the widget, not the shared grid behind it.
vi.mock('../src/widgets/GridWidgetBody', () => ({
  createGridWidgetBody: () => () => React.createElement('div', { 'data-testid': 'invoices-grid' }, 'grid'),
}));

const SLOW = { timeout: 60000 };
const MATTER_ID = '3f2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5b';
const PROJECT_ID = '4a2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5c';
const SUBMISSION_ID = '5b2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5d';
const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};

interface Call {
  url: string;
  method: string;
  authorization: string | undefined;
  contentType: string | undefined;
  /** FormData entries (file as its name). */
  form: Record<string, string> | null;
  /** A JSON body, verbatim. */
  json: string | null;
}

/** What the fake BFF answers; per test. */
interface Script {
  /** GET /api/v1/external/invoice-submissions (the probe). */
  probe: () => Response;
  /** POST .../invoice-submissions. */
  submit: () => Response;
  /** GET .../invoice-submissions/{id}, one answer per read (the last repeats). */
  status: Array<() => Response>;
  /** GET /api/v1/external/me (the caller's firms are `firms`). */
  me: () => Response;
  /** GET .../invoice-submissions/firm-records?firmId= (D-106): the caller's readable records the firm is linked to. */
  firmRecords: (firmId: string) => Response;
}

const FIRM_ALPHA = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const FIRM_BETA = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta & Co' };
const meWith = (firms: unknown) => () =>
  json(200, { contactId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', email: 'ann@firm.example', projects: [], firms });

let calls: Call[];
let script: Script;

const json = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/** The firm-records body: the ids (among the caller's readable records) the firm is linked to. */
const linkedTo = (
  firmId: string,
  matters: string[],
  projects: string[],
  partial = false,
  truncated = false
): Response => json(200, { firmId, matters, projects, workAssignments: [], partial, truncated });

const problem = (status: number, detail: string | undefined, errorCode?: string): Response =>
  json(status, { status, title: 'refused', ...(detail ? { detail } : {}), ...(errorCode ? { errorCode } : {}) });

const statusView = (over: Record<string, unknown> = {}) => ({
  submissionId: SUBMISSION_ID,
  trackingNumber: 'TRK-0001',
  firmInvoiceNumber: 'INV-9',
  status: 'Processing',
  ...over,
});

const ACCEPTED_202 = () =>
  json(202, {
    submissionId: SUBMISSION_ID,
    jobId: 'job-1',
    trackingNumber: 'TRK-0001',
    statusUrl: `/api/v1/external/invoice-submissions/${SUBMISSION_ID}`,
  });

beforeEach(() => {
  calls = [];
  script = {
    probe: () => json(200, []),
    submit: ACCEPTED_202,
    status: [() => json(200, statusView({ status: 'Accepted' }))],
    me: meWith([FIRM_ALPHA]),
    // Every firm is linked to the matter and the project unless a test says otherwise.
    firmRecords: firmId => linkedTo(firmId, [MATTER_ID], [PROJECT_ID]),
  };
  localStorage.clear();
  sessionStorage.clear();
  window.history.replaceState(null, '', '/');
  let statusReads = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      const method = init?.method ?? 'GET';
      const body = init?.body;
      let form: Record<string, string> | null = null;
      if (body instanceof FormData) {
        form = {};
        body.forEach((value, key) => {
          form![key] = value instanceof File ? value.name : String(value);
        });
      }
      calls.push({
        url,
        method,
        authorization: headers.Authorization,
        contentType: headers['Content-Type'],
        form,
        json: typeof body === 'string' ? body : null,
      });

      const p = new URL(url).pathname;
      if (method === 'GET' && p === '/api/v1/external/invoice-submissions') return script.probe();
      if (method === 'GET' && p === '/api/v1/external/me') return script.me();
      if (method === 'GET' && p === '/api/v1/external/invoice-submissions/firm-records') {
        return script.firmRecords(new URL(url).searchParams.get('firmId') ?? '');
      }
      if (method === 'GET' && p === '/api/v1/external/projects') {
        return json(200, {
          value: [{ sprk_projectid: PROJECT_ID, sprk_name: 'Patent portfolio', sprk_referencenumber: 'P-77' }],
        });
      }
      if (method === 'POST' && p === '/api/v1/external/api/dataverse/fetch') {
        const entity = (JSON.parse(String(body)) as { entityName: string }).entityName;
        if (entity === 'sprk_matter') {
          return json(200, {
            entities: [
              { sprk_matterid: MATTER_ID, sprk_mattername: 'Acme v. Roadrunner', sprk_matternumber: 'M-1001' },
            ],
            moreRecords: false,
          });
        }
        return json(200, { entities: [], moreRecords: false });
      }
      if (
        method === 'POST' &&
        /^\/api\/v1\/external\/(matters|projects|workassignments)\/[^/]+\/invoice-submissions$/.test(p)
      ) {
        return script.submit();
      }
      if (method === 'GET' && p.startsWith('/api/v1/external/invoice-submissions/')) {
        const answer = script.status[Math.min(statusReads, script.status.length - 1)];
        statusReads += 1;
        return answer();
      }
      return problem(404, 'unexpected route in test');
    })
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/** Fresh module graph; the plane's backend is activated and its acquirer installed, as the plane bootstrap does. */
async function load() {
  vi.resetModules();
  const selection = await import('../src/auth/backend-selection');
  const msalAuth = await import('../src/auth/msal-auth');
  msalAuth.setActiveBffTokenAcquirer(vi.fn(async () => 'ciam-token'));
  selection.activateBackend('ciam', { ...CONTOSO });
  const { InvoiceSubmitPage, INVOICE_SUBMIT_PATH } = await import('../src/pages/InvoiceSubmitPage');
  const { default: InvoicesWidgetBody } = await import('../src/widgets/InvoicesWidget');
  return { InvoiceSubmitPage, INVOICE_SUBMIT_PATH, InvoicesWidgetBody };
}

function renderPage(Page: React.FC, entry: string) {
  return render(
    React.createElement(
      MemoryRouter,
      { initialEntries: [entry] },
      React.createElement(
        Routes,
        null,
        React.createElement(Route, { path: '/invoices/submit', element: React.createElement(Page) }),
        React.createElement(Route, { path: '*', element: React.createElement('div', null, 'left the page') })
      )
    )
  );
}

const next = () => screen.findByRole('button', { name: /^next$/i });
/** The suite does not load jest-dom: a button is enabled when its `disabled` flag is clear. */
const isEnabled = (el: HTMLElement) => !(el as HTMLButtonElement).disabled;

/** Choose the matter, upload a PDF and reach the Details step. */
async function toDetails() {
  fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW));
  fireEvent.click(await next());
  const input = await waitFor(() => {
    const el = document.querySelector('input[type="file"]');
    if (!el) throw new Error('no file input yet');
    return el as HTMLInputElement;
  });
  fireEvent.change(input, {
    target: { files: [new File(['%PDF-1.4 test'], 'invoice.pdf', { type: 'application/pdf' })] },
  });
  await waitFor(() => expect(isEnabled(screen.getByRole('button', { name: /^next$/i }))).toBe(true));
  fireEvent.click(await next());
  return screen.findByLabelText(/Firm invoice number/i);
}

const submitCalls = () => calls.filter(c => c.method === 'POST' && c.url.includes('/invoice-submissions'));

describe('InvoiceSubmitPage — submit', () => {
  it(
    'a firm user picks an accessible matter, uploads a PDF with the required firm invoice number, sees Processing then Accepted with the tracking number',
    async () => {
      script.status = [
        () => json(200, statusView({ status: 'Processing' })),
        () => json(200, statusView({ status: 'Accepted' })),
      ];
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      // The picker offers the caller's projects, matters and work assignments (three reads), narrowed to the records
      // linked to the firm. The availability probe is /me (its `firms`), not a list read; /me is read again for the firms.
      const firmInvoiceNumber = await toDetails();
      expect(
        calls
          .filter(c => c.method === 'GET')
          .map(c => new URL(c.url).pathname)
          .sort()
      ).toEqual([
        '/api/v1/external/invoice-submissions/firm-records', // the records linked to the firm (D-106)
        '/api/v1/external/me', // the availability probe
        '/api/v1/external/me', // the caller's firms (one here: no firm step)
        '/api/v1/external/projects',
      ]);
      expect(calls.find(c => c.url.includes('/firm-records'))?.url).toContain(`firmId=${FIRM_ALPHA.firmId}`);
      // The probe made no list read: that route is only the fallback when `firms` is absent.
      expect(calls.some(c => new URL(c.url).pathname === '/api/v1/external/invoice-submissions')).toBe(false);
      // Matters and work assignments: one Tier-2-scoped module read each.
      expect(calls.filter(c => c.url.includes('/dataverse/fetch'))).toHaveLength(2);

      // D-91: cannot submit with the firm invoice number blank.
      const submit = screen.getByRole('button', { name: 'Submit invoice' });
      expect(isEnabled(submit)).toBe(false);
      fireEvent.change(firmInvoiceNumber, { target: { value: 'INV-9' } });
      fireEvent.change(screen.getByLabelText(/PO number/i), { target: { value: 'PO-5' } });
      expect(isEnabled(submit)).toBe(true);
      fireEvent.click(submit);

      expect(await screen.findByText('Invoice received', {}, SLOW)).toBeTruthy();
      const posts = submitCalls();
      expect(posts).toHaveLength(1);
      expect(posts[0]).toMatchObject({
        url: `https://contoso-bff.test/api/v1/external/matters/${MATTER_ID}/invoice-submissions`,
        method: 'POST',
        authorization: 'Bearer ciam-token',
        contentType: undefined, // multipart boundary is the browser's
        form: { file: 'invoice.pdf', firmInvoiceNumber: 'INV-9', poNumber: 'PO-5' },
      });
      // The server picks the firm: the client names none.
      expect(posts[0].url).not.toContain('firmId');
      expect(Object.keys(posts[0].form ?? {})).not.toContain('firmId');

      expect((await screen.findAllByText('Processing', {}, SLOW)).length).toBeGreaterThan(0);
      expect(screen.getByTestId('invoice-tracking-number').textContent).toBe('TRK-0001');
      expect(await screen.findByText(/The invoice was accepted/i, {}, { timeout: 10000 })).toBeTruthy();

      // Leaving and returning: the address now carries the submission, without a router navigation.
      expect(window.location.pathname + window.location.search).toBe(`/invoices/submit?submissionId=${SUBMISSION_ID}`);
    },
    SLOW
  );

  it(
    'a Rejected result shows the reason code and the report placeholder',
    async () => {
      script.status = [() => json(200, statusView({ status: 'Rejected', outcomeReasonCode: 'duplicate_invoice' }))];
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      fireEvent.change(await toDetails(), { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText(/Reason: duplicate_invoice/, {}, SLOW)).toBeTruthy();
      expect(screen.getByTestId('invoice-report-placeholder')).toBeTruthy();
    },
    SLOW
  );

  const REFUSALS: Array<[string, () => Response, RegExp]> = [
    [
      '400 not_pdf (the server detail)',
      () => problem(400, 'Only PDF invoices are accepted.', 'ebilling.submission.not_pdf'),
      /Only PDF invoices are accepted\./,
    ],
    [
      '400 firm_invoice_number_required (code text, no detail)',
      () => problem(400, undefined, 'ebilling.submission.firm_invoice_number_required'),
      /Enter the firm invoice number/,
    ],
    [
      '403 record_forbidden',
      () => problem(403, undefined, 'ebilling.submission.record_forbidden'),
      /do not have access to submit an invoice against this record/i,
    ],
    [
      '413 too_large (the server detail)',
      () => problem(413, 'The file is larger than the 25 MB limit.', 'ebilling.submission.too_large'),
      /larger than the 25 MB limit/,
    ],
    [
      '503 ebilling.disabled',
      () => problem(503, undefined, 'ebilling.disabled'),
      /Invoice submission is not available/,
    ],
  ];

  it.each(REFUSALS)(
    'server %s: the message shows by its code and the flow does not advance',
    async (_name, refusal, message) => {
      script.submit = refusal;
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      fireEvent.change(await toDetails(), { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText(message, {}, SLOW)).toBeTruthy();
      expect(screen.queryByText('Invoice received')).toBeNull();
      expect(submitCalls()).toHaveLength(1);
      // No status read, no address rewrite: nothing was submitted.
      expect(calls.some(c => c.method === 'GET' && c.url.includes(`/invoice-submissions/${SUBMISSION_ID}`))).toBe(
        false
      );
      expect(window.location.search).toBe('');
      expect(screen.getByLabelText(/Firm invoice number/i)).toBeTruthy();
    },
    SLOW
  );

  it(
    'the only requests are the probe, /me (the firms), the record reads and the invoice-submissions routes; nothing targets an invoice line',
    async () => {
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      fireEvent.change(await toDetails(), { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));
      await screen.findByText('Invoice received', {}, SLOW);

      for (const c of calls) {
        const p = new URL(c.url).pathname;
        expect(p).toMatch(
          /^\/api\/v1\/external\/(invoice-submissions(\/[^/]+)?|me|projects|api\/dataverse\/fetch|matters\/[^/]+\/invoice-submissions)$/
        );
        expect(c.url).not.toMatch(/billingevent|invoiceline/i);
        expect(c.json ?? '').not.toMatch(/sprk_billingevent|sprk_invoice\b|sprk_invoicesubmission/i);
      }
      // The matter read asks for the admitted columns only (ExternalAccessModule ReadableColumns).
      const fetches = calls
        .filter(c => c.url.includes('/dataverse/fetch'))
        .map(c => JSON.parse(c.json ?? '{}') as { entityName: string; fetchXml: string });
      const matterRead = fetches.find(f => f.entityName === 'sprk_matter');
      expect([...(matterRead?.fetchXml.matchAll(/attribute name="([^"]+)"/g) ?? [])].map(m => m[1])).toEqual([
        'sprk_matterid',
        'sprk_mattername',
        'sprk_matternumber',
      ]);
    },
    SLOW
  );
});

describe('InvoiceSubmitPage — Choose firm (owner, 2026-10-17: add a firm step)', () => {
  const firmRadio = (name: string) => screen.findByRole('radio', { name }, SLOW);

  it(
    'a caller with several firms must choose one first, and the choice goes over the wire as the firmId query parameter',
    async () => {
      script.me = meWith([FIRM_ALPHA, FIRM_BETA]);
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      // The Firm step is first, lists exactly the firms /me returned, and blocks Next until one is picked.
      await firmRadio('Alpha LLP');
      expect(isEnabled(await next())).toBe(false);
      fireEvent.click(screen.getByRole('radio', { name: 'Beta & Co' }));
      expect(isEnabled(await next())).toBe(true);
      fireEvent.click(await next());

      const firmInvoiceNumber = await toDetails();
      fireEvent.change(firmInvoiceNumber, { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText('Invoice received', {}, SLOW)).toBeTruthy();
      const posts = submitCalls();
      expect(posts).toHaveLength(1);
      expect(posts[0].url).toBe(
        `https://contoso-bff.test/api/v1/external/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM_BETA.firmId}`
      );
      // firmId is a QUERY parameter (task 044 reads it there); it is not also a form field.
      expect(Object.keys(posts[0].form ?? {})).not.toContain('firmId');
    },
    SLOW
  );

  it(
    'cannot leave the Firm step without a choice',
    async () => {
      script.me = meWith([FIRM_ALPHA, FIRM_BETA]);
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      await firmRadio('Alpha LLP');
      expect(isEnabled(await next())).toBe(false);
      expect(screen.queryByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeNull();
      expect(submitCalls()).toHaveLength(0);
    },
    SLOW
  );

  it.each([
    ['one firm', meWith([FIRM_ALPHA])],
    ['no firm', meWith([])],
    ['firms unknown (null: eBilling off or a failed read)', meWith(null)],
    ['a /me that fails', () => problem(500, 'boom')],
  ])(
    'with %s there is no firm step and no firmId is sent',
    async (_name, me) => {
      script.me = me;
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      const firmInvoiceNumber = await toDetails();
      expect(screen.queryByText('Choose the firm this invoice is from')).toBeNull();
      fireEvent.change(firmInvoiceNumber, { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText('Invoice received', {}, SLOW)).toBeTruthy();
      const posts = submitCalls();
      expect(posts).toHaveLength(1);
      expect(posts[0].url).not.toContain('firmId');
    },
    SLOW
  );

  it(
    'lists only the records linked to the chosen firm (D-106), and asks again when another firm is chosen',
    async () => {
      script.me = meWith([FIRM_ALPHA, FIRM_BETA]);
      // Alpha is linked to the matter only; Beta to the project only.
      script.firmRecords = firmId =>
        firmId === FIRM_ALPHA.firmId ? linkedTo(firmId, [MATTER_ID], []) : linkedTo(firmId, [], [PROJECT_ID]);
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      // Nothing about records is asked before a firm is chosen.
      await firmRadio('Alpha LLP');
      expect(calls.some(c => c.url.includes('/firm-records'))).toBe(false);
      expect(calls.some(c => new URL(c.url).pathname === '/api/v1/external/projects')).toBe(false);

      fireEvent.click(screen.getByRole('radio', { name: 'Alpha LLP' }));
      fireEvent.click(await next());
      expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW)).toBeTruthy();
      expect(screen.queryByRole('radio', { name: /Patent portfolio/ })).toBeNull();

      fireEvent.click(screen.getByRole('button', { name: /^back$/i }));
      fireEvent.click(await firmRadio('Beta & Co'));
      fireEvent.click(await next());
      expect(await screen.findByRole('radio', { name: /Patent portfolio/ }, SLOW)).toBeTruthy();
      expect(screen.queryByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeNull();
      expect(
        calls.filter(c => c.url.includes('/firm-records')).map(c => new URL(c.url).searchParams.get('firmId'))
      ).toEqual([FIRM_ALPHA.firmId, FIRM_BETA.firmId]);
    },
    SLOW
  );

  it(
    'says so when no record is linked to the firm, and when the link read failed (partial) it does not read as "none"',
    async () => {
      script.firmRecords = firmId => linkedTo(firmId, [], [], false);
      const { InvoiceSubmitPage } = await load();
      const view = renderPage(InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByText(/is linked to this firm/i, {}, SLOW)).toBeTruthy();
      expect(isEnabled(await next())).toBe(false);
      view.unmount();

      script.firmRecords = firmId => linkedTo(firmId, [MATTER_ID], [], true);
      renderPage((await load()).InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW)).toBeTruthy();
      expect(screen.getByTestId('records-partial')).toBeTruthy();
    },
    SLOW
  );

  it(
    'says the list is cut short when the firm-records read reports it was truncated at the BFF bound (a linked record may be missing)',
    async () => {
      script.firmRecords = firmId => linkedTo(firmId, [MATTER_ID], [], false, true);
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW)).toBeTruthy();
      expect(screen.getByTestId('records-truncated')).toBeTruthy();
      expect(screen.queryByTestId('records-partial')).toBeNull();
    },
    SLOW
  );

  it(
    'a refused or failed firm-records read shows the failure and never an unfiltered list',
    async () => {
      script.firmRecords = () => problem(403, undefined, 'ebilling.submission.use_internal_route');
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByText(/submitted from the internal workspace/i, {}, SLOW)).toBeTruthy();
      expect(screen.queryByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeNull();
      expect(screen.queryByRole('radio', { name: /Patent portfolio/ })).toBeNull();
    },
    SLOW
  );

  it(
    'with firms unknown (null) the records are not narrowed (no firm-records read): the server checks the link on submit',
    async () => {
      script.me = meWith(null);
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW)).toBeTruthy();
      expect(screen.getByRole('radio', { name: /Patent portfolio/ })).toBeTruthy();
      expect(calls.some(c => c.url.includes('/firm-records'))).toBe(false);
    },
    SLOW
  );

  it(
    'a 400 firm_required on submit brings the Choose firm step in, then the chosen firm goes as firmId',
    async () => {
      // /me says null the first time (unknown), several firms after the refusal.
      let meReads = 0;
      script.me = () => (++meReads <= 2 ? meWith(null)() : meWith([FIRM_ALPHA, FIRM_BETA])());
      let posts = 0;
      script.submit = () =>
        ++posts === 1 ? problem(400, undefined, 'ebilling.submission.firm_required') : ACCEPTED_202();
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      fireEvent.change(await toDetails(), { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText('Choose the firm this invoice is from', {}, SLOW)).toBeTruthy();
      fireEvent.click(screen.getByRole('radio', { name: 'Beta & Co' }));
      fireEvent.click(await next());
      // The record is still chosen (offered under the firm), the file and the number are kept.
      await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW);
      fireEvent.click(await next());
      await waitFor(() => expect(isEnabled(screen.getByRole('button', { name: /^next$/i }))).toBe(true));
      fireEvent.click(await next());
      expect(((await screen.findByLabelText(/Firm invoice number/i)) as HTMLInputElement).value).toBe('INV-9');
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText('Invoice received', {}, SLOW)).toBeTruthy();
      const sent = submitCalls();
      expect(sent).toHaveLength(2);
      expect(sent[0].url).not.toContain('firmId');
      expect(sent[1].url).toContain(`?firmId=${FIRM_BETA.firmId}`);
    },
    SLOW
  );

  it(
    'shows the server refusal when the chosen firm is refused, and does not advance',
    async () => {
      script.me = meWith([FIRM_ALPHA, FIRM_BETA]);
      script.submit = () => problem(403, undefined, 'ebilling.submission.firm_forbidden');
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      fireEvent.click(await firmRadio('Alpha LLP'));
      fireEvent.click(await next());
      const firmInvoiceNumber = await toDetails();
      fireEvent.change(firmInvoiceNumber, { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));

      expect(await screen.findByText(/not allowed to submit invoices for this firm/i, {}, SLOW)).toBeTruthy();
      expect(screen.queryByText('Invoice received')).toBeNull();
    },
    SLOW
  );
});

describe('InvoiceSubmitPage — resume (FR-18)', () => {
  it(
    'returning to the address with a submission id shows the stored result read from the server; no wizard, no upload',
    async () => {
      script.status = [() => json(200, statusView({ status: 'Rejected', outcomeReasonCode: 'po_mismatch' }))];
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, `/invoices/submit?submissionId=${SUBMISSION_ID}`);

      expect(await screen.findByText(/Reason: po_mismatch/, {}, SLOW)).toBeTruthy();
      expect(screen.getByTestId('invoice-tracking-number').textContent).toBe('TRK-0001');
      expect(screen.queryByRole('radio')).toBeNull();
      expect(submitCalls()).toHaveLength(0);
      expect(calls.some(c => c.url.endsWith(`/api/v1/external/invoice-submissions/${SUBMISSION_ID}`))).toBe(true);
      expect(calls.some(c => c.url.includes('/projects'))).toBe(false);
    },
    SLOW
  );

  it(
    'a submission that is still processing shows Processing',
    async () => {
      script.status = [() => json(200, statusView({ status: 'Received' }))];
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, `/invoices/submit?submissionId=${SUBMISSION_ID}`);
      expect((await screen.findAllByText('Processing', {}, SLOW)).length).toBeGreaterThan(0);
      expect(screen.getByText(/You can leave this page/)).toBeTruthy();
    },
    SLOW
  );

  it(
    'a submission that is not the caller’s (uniform 404) says so',
    async () => {
      script.status = [() => problem(404, undefined)];
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, `/invoices/submit?submissionId=${SUBMISSION_ID}`);
      expect(await screen.findByText(/not found, or you do not have access/i, {}, SLOW)).toBeTruthy();
    },
    SLOW
  );

  it(
    'a malformed submission id in the link is refused before any status read',
    async () => {
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit?submissionId=not-a-guid');
      expect(await screen.findByText(/submission id in the link is not valid/i, {}, SLOW)).toBeTruthy();
      expect(calls.some(c => c.url.includes('/invoice-submissions/'))).toBe(false);
    },
    SLOW
  );
});

describe('InvoiceSubmitPage — eBilling off (FR-45)', () => {
  it(
    '503 ebilling.disabled from the probe replaces the page with a not-available state; no record is read and nothing can be submitted',
    async () => {
      // eBilling off: /me carries no `firms`, so the probe falls back to the list read, which answers 503 ebilling.disabled.
      script.me = meWith(null);
      script.probe = () => problem(503, 'eBilling is not enabled in this environment.', 'ebilling.disabled');
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      expect(await screen.findByTestId('invoice-submit-unavailable', {}, SLOW)).toBeTruthy();
      expect(screen.queryByRole('radio')).toBeNull();
      expect(screen.queryByRole('button', { name: /^next$/i })).toBeNull();
      expect(calls.map(c => new URL(c.url).pathname)).toEqual([
        '/api/v1/external/me',
        '/api/v1/external/invoice-submissions',
      ]);
    },
    SLOW
  );

  it(
    'a 503 that is an outage (another error code) does not read as disabled: the wizard is shown',
    async () => {
      script.me = meWith(null);
      script.probe = () => problem(503, 'Submissions could not be read.', 'ebilling.submission.record_unavailable');
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }, SLOW)).toBeTruthy();
      expect(screen.queryByTestId('invoice-submit-unavailable')).toBeNull();
    },
    SLOW
  );
});

describe('Invoices widget — Submit invoice entry (FR-18, FR-45)', () => {
  type BodyProps = { title: string; onNavigate?: (path: string) => void };
  /** The router-owning host (App.tsx -> WorkspaceHomePage) hands the body `navigate`; `withNavigate: false` is a host without one. */
  function renderWidget(Body: React.FC<BodyProps>, withNavigate = true) {
    const Host: React.FC = () => {
      const navigate = useNavigate();
      return React.createElement(Body, { title: 'Invoices', onNavigate: withNavigate ? navigate : undefined });
    };
    return render(
      React.createElement(
        MemoryRouter,
        { initialEntries: ['/'] },
        React.createElement(
          Routes,
          null,
          React.createElement(Route, { path: '/', element: React.createElement(Host) }),
          React.createElement(Route, {
            path: '/invoices/submit',
            element: React.createElement('div', null, 'submit page'),
          })
        )
      )
    );
  }

  it(
    'shows the action when eBilling answers, and the action opens the submission page',
    async () => {
      const { InvoicesWidgetBody } = await load();
      renderWidget(InvoicesWidgetBody);
      const action = await screen.findByRole('button', { name: /submit invoice/i }, SLOW);
      expect(screen.getByTestId('invoices-grid')).toBeTruthy();
      fireEvent.click(action);
      expect(await screen.findByText('submit page')).toBeTruthy();
    },
    SLOW
  );

  it(
    'reads only /me on mount: no list probe, no record read',
    async () => {
      const { InvoicesWidgetBody } = await load();
      renderWidget(InvoicesWidgetBody);
      await screen.findByRole('button', { name: /submit invoice/i }, SLOW);
      expect(calls.map(c => new URL(c.url).pathname)).toEqual(['/api/v1/external/me']);
    },
    SLOW
  );

  it.each([
    ['eBilling off or an older BFF (firms null)', meWith(null)],
    [
      'firms absent',
      () => json(200, { contactId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', email: 'a@b.example', projects: [] }),
    ],
    ['no firm (empty: a systemuser, a deactivated contact)', meWith([])],
    ['a /me that fails', () => problem(500, 'boom')],
  ])(
    'hides the action for %s (the grid stays)',
    async (_name, me) => {
      script.me = me;
      const { InvoicesWidgetBody } = await load();
      renderWidget(InvoicesWidgetBody);
      await waitFor(() => expect(calls.length).toBeGreaterThan(0));
      await new Promise(r => setTimeout(r, 100));
      expect(screen.queryByRole('button', { name: /submit invoice/i })).toBeNull();
      expect(screen.getByTestId('invoices-grid')).toBeTruthy();
      expect(calls.some(c => new URL(c.url).pathname === '/api/v1/external/invoice-submissions')).toBe(false);
    },
    SLOW
  );

  it(
    'without a navigate handed by the host (no router) it renders the grid alone and reads nothing',
    async () => {
      const { InvoicesWidgetBody } = await load();
      renderWidget(InvoicesWidgetBody, false);
      await new Promise(r => setTimeout(r, 100));
      expect(screen.queryByRole('button', { name: /submit invoice/i })).toBeNull();
      expect(screen.getByTestId('invoices-grid')).toBeTruthy();
      expect(calls).toHaveLength(0);
    },
    SLOW
  );

  it(
    'renders with no router at all (the body imports no router API)',
    async () => {
      const { InvoicesWidgetBody } = await load();
      render(React.createElement(InvoicesWidgetBody, { title: 'Invoices' }));
      expect(screen.getByTestId('invoices-grid')).toBeTruthy();
    },
    SLOW
  );
});

describe('InvoiceSubmission files — ADR-021', () => {
  it('use theme tokens only: no hex or rgb colour literal in the new UI files', () => {
    const files = [
      '../src/pages/InvoiceSubmitPage.tsx',
      '../src/widgets/InvoicesWidget.tsx',
      '../../shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionWizard.tsx',
      '../../shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionOutcome.tsx',
    ];
    for (const rel of files) {
      const text = fs.readFileSync(path.resolve(__dirname, rel), 'utf8');
      expect(text, rel).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
      expect(text, rel).not.toMatch(/\brgba?\(|\bhsla?\(/);
    }
  });
});
