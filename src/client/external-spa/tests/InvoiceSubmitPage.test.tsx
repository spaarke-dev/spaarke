// InvoiceSubmitPage (ebilling-r1 task 051, FR-18 / FR-19 / FR-45): the real page, the shared InvoiceSubmissionWizard,
// WizardShell, FileUploadZone, web-api-client, bff-client and backend selection run. fetch and the token acquirer (the
// seam each plane's bootstrap points at its own strategy) are fakes: a URL-routed BFF that records every request.
// Every assertion is about what goes over the wire and what the user sees.
import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

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
}

let calls: Call[];
let script: Script;

const json = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

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

      // The picker offers the caller's projects, matters and work assignments (three reads), nothing else.
      const firmInvoiceNumber = await toDetails();
      expect(calls.filter(c => c.method === 'GET').map(c => new URL(c.url).pathname)).toEqual([
        '/api/v1/external/invoice-submissions', // the availability probe
        '/api/v1/external/projects',
      ]);
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
    'the only requests are the probe, the record reads and the invoice-submissions routes; nothing targets an invoice line',
    async () => {
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');
      fireEvent.change(await toDetails(), { target: { value: 'INV-9' } });
      fireEvent.click(screen.getByRole('button', { name: 'Submit invoice' }));
      await screen.findByText('Invoice received', {}, SLOW);

      for (const c of calls) {
        const p = new URL(c.url).pathname;
        expect(p).toMatch(
          /^\/api\/v1\/external\/(invoice-submissions(\/[^/]+)?|projects|api\/dataverse\/fetch|matters\/[^/]+\/invoice-submissions)$/
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
      script.probe = () => problem(503, 'eBilling is not enabled in this environment.', 'ebilling.disabled');
      const { InvoiceSubmitPage } = await load();
      renderPage(InvoiceSubmitPage, '/invoices/submit');

      expect(await screen.findByTestId('invoice-submit-unavailable', {}, SLOW)).toBeTruthy();
      expect(screen.queryByRole('radio')).toBeNull();
      expect(screen.queryByRole('button', { name: /^next$/i })).toBeNull();
      expect(calls.map(c => new URL(c.url).pathname)).toEqual(['/api/v1/external/invoice-submissions']);
    },
    SLOW
  );

  it(
    'a 503 that is an outage (another error code) does not read as disabled: the wizard is shown',
    async () => {
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
  function renderWidget(Body: React.FC<{ title: string }>) {
    return render(
      React.createElement(
        MemoryRouter,
        { initialEntries: ['/'] },
        React.createElement(
          Routes,
          null,
          React.createElement(Route, { path: '/', element: React.createElement(Body, { title: 'Invoices' }) }),
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
    'hides the action on 503 ebilling.disabled (the grid stays)',
    async () => {
      script.probe = () => problem(503, undefined, 'ebilling.disabled');
      const { InvoicesWidgetBody } = await load();
      renderWidget(InvoicesWidgetBody);
      await waitFor(() => expect(calls.length).toBeGreaterThan(0));
      await new Promise(r => setTimeout(r, 50));
      expect(screen.queryByRole('button', { name: /submit invoice/i })).toBeNull();
      expect(screen.getByTestId('invoices-grid')).toBeTruthy();
    },
    SLOW
  );

  it(
    'keeps the action through an outage 503 or a refusal: only ebilling.disabled hides it',
    async () => {
      script.probe = () => problem(503, undefined, 'ebilling.submission.record_unavailable');
      const { InvoicesWidgetBody } = await load();
      const view = renderWidget(InvoicesWidgetBody);
      expect(await screen.findByRole('button', { name: /submit invoice/i }, SLOW)).toBeTruthy();
      view.unmount();

      script.probe = () => problem(403, undefined);
      renderWidget(InvoicesWidgetBody);
      expect(await screen.findByRole('button', { name: /submit invoice/i }, SLOW)).toBeTruthy();
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
