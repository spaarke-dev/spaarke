/**
 * CreateInvoiceWizard tests (ebilling-r1 task 052; FR-19, D-19, D-24, D-91, ADR-002).
 *
 * The real wizard runs end to end: the shared InvoiceSubmission flow, the adapter, the held-submission panel and the canonical
 * FormModal. Only the transport is faked: one `authenticatedFetch` stand-in that answers the internal BFF routes by URL, and an
 * `IDataService` that serves the record picker's READS. The assertions are about what the user sees and what goes on the wire.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateInvoiceWizard } from '../CreateInvoiceWizard';

// The status poll of the held screen runs on its 2 s default.
jest.setTimeout(30000);
const SLOW = { timeout: 8000 };

const BASE = 'https://bff.example.test';
const MATTER_ID = '11111111-1111-4111-8111-111111111111';
const FIRM_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const FIRM_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';
const ASSOCIATION = { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'Acme v. Roadrunner' };

const reply = (status: number, body: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body,
});

interface Server {
  firms: { firmId: string; name: string }[];
  /** The statuses the status route answers, in order; the last one repeats. */
  statuses: Record<string, unknown>[];
  blocked: Record<string, unknown>;
  decide: (verb: string, body: { reason: string }) => unknown;
}

const heldStatus = {
  submissionId: SUBMISSION_ID,
  status: 'Rejected',
  outcomeReasonCode: 'ebilling.submission.blocked',
  trackingNumber: 'INV-9.1',
  firmInvoiceNumber: 'INV-9',
};
const heldView = (over: Record<string, unknown> = {}) => ({
  submissionId: SUBMISSION_ID,
  state: 'Blocked',
  canOverride: true,
  canReturn: true,
  findings: [{ index: 0, clause: 'Partner rate may not exceed 650 per hour', ruleType: 'RateCap', blocking: true }],
  ...over,
});

function makeServer(over: Partial<Server> = {}) {
  const s: Server = {
    firms: [{ firmId: FIRM_A, name: 'Alpha LLP' }],
    statuses: [
      { submissionId: SUBMISSION_ID, status: 'Accepted', trackingNumber: 'INV-9.1', firmInvoiceNumber: 'INV-9' },
    ],
    blocked: heldView(),
    decide: () => reply(200, { submissionId: SUBMISSION_ID, outcome: 'Overridden', alreadyDecided: false }),
    ...over,
  };
  let statusReads = 0;
  const calls: { url: string; method: string; body?: unknown }[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    calls.push({ url, method, body: init?.body });
    const path = url.replace(BASE, '');
    if (/\/core-records\/\w+\/[\w-]+\/firms$/.test(path)) return reply(200, s.firms);
    if (/\/invoice-submissions\?firmId=/.test(path) && method === 'POST') {
      return reply(202, {
        submissionId: SUBMISSION_ID,
        trackingNumber: 'INV-9.1',
        statusUrl: `/api/finance/invoice-submissions/${SUBMISSION_ID}`,
      });
    }
    if (path === `/api/finance/invoice-submissions/${SUBMISSION_ID}`) {
      const i = Math.min(statusReads++, s.statuses.length - 1);
      return reply(200, s.statuses[i]);
    }
    if (path === `/api/finance/submissions/${SUBMISSION_ID}/blocked`) return reply(200, s.blocked);
    const decision = /\/submissions\/[\w-]+\/(override|return)$/.exec(path);
    if (decision && method === 'POST') {
      const out = s.decide(decision[1], JSON.parse(String(init?.body)));
      if (out instanceof Error) throw out;
      return out as Response;
    }
    return reply(404, { errorCode: 'test.unrouted' });
  });
  return { s, fetchFn, calls };
}

function makeData(rows: Record<string, Record<string, unknown>[]> = {}) {
  const retrieveMultipleRecords = jest.fn(async (entity: string) => ({ entities: rows[entity] ?? [] }));
  const createRecord = jest.fn();
  const updateRecord = jest.fn();
  const data = {
    retrieveMultipleRecords,
    createRecord,
    updateRecord,
    deleteRecord: jest.fn(),
    retrieveRecord: jest.fn(),
  } as unknown as IDataService;
  return { data, retrieveMultipleRecords, createRecord, updateRecord };
}

function mount(
  server: ReturnType<typeof makeServer>,
  props: Partial<React.ComponentProps<typeof CreateInvoiceWizard>> = {},
  data = makeData()
) {
  const onClose = jest.fn();
  const utils = renderWithProviders(
    <CreateInvoiceWizard
      open
      embedded
      onClose={onClose}
      dataService={data.data}
      authenticatedFetch={server.fetchFn as unknown as typeof fetch}
      bffBaseUrl={BASE}
      initialAssociation={ASSOCIATION}
      lockAssociation
      {...props}
    />
  );
  return { ...utils, onClose, data };
}

const next = () => screen.getByRole('button', { name: /^next$/i });
const submitButton = () => screen.getByRole('button', { name: /^submit invoice$/i });
const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });

/** From the Record step (the launch record already chosen) to the submitted invoice. */
async function fillAndSubmit(container: HTMLElement) {
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  const input = container.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [pdf()] } });
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
  fireEvent.click(submitButton());
}

const dialogs = () => screen.queryAllByRole('dialog');
const formModal = () => dialogs()[dialogs().length - 1];

describe('CreateInvoiceWizard — the internal submission', () => {
  it('submits through the internal route and shows the server result; the client creates nothing', async () => {
    const server = makeServer();
    const t = mount(server);
    // The launch record is already chosen and the only one offered (locked).
    expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeChecked();
    await fillAndSubmit(t.container);

    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.getByTestId('invoice-tracking-number')).toHaveTextContent('INV-9.1');

    const post = server.calls.find(c => c.method === 'POST')!;
    expect(post.url).toBe(`${BASE}/api/finance/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM_A}`);
    const form = post.body as FormData;
    expect(form.get('firmInvoiceNumber')).toBe('INV-9');
    expect((form.get('file') as File).name).toBe('invoice.pdf');

    // ADR-002: nothing was written to Dataverse by the client, and nothing was read to build a list (locked).
    expect(t.data.createRecord).not.toHaveBeenCalled();
    expect(t.data.updateRecord).not.toHaveBeenCalled();
    expect(t.data.retrieveMultipleRecords).not.toHaveBeenCalled();
    // An accepted invoice shows no Override, no Return and no held panel.
    expect(screen.queryByTestId('blocked-submission-panel')).not.toBeInTheDocument();
  });

  it('the firm invoice number is required before the submit button works (D-91)', async () => {
    const server = makeServer();
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    fireEvent.click(next());
    fireEvent.change(t.container.querySelector('input[type="file"]') as HTMLInputElement, {
      target: { files: [pdf()] },
    });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await screen.findByLabelText(/Firm invoice number/i);
    expect(submitButton()).toBeDisabled();
    expect(server.calls.some(c => c.method === 'POST')).toBe(false);
  });

  it('a record with several linked firms starts with the Choose firm step, listing exactly the firms the server names, and sends the chosen one', async () => {
    const server = makeServer({
      firms: [
        { firmId: FIRM_A, name: 'Alpha LLP' },
        { firmId: FIRM_B, name: 'Beta LLP' },
      ],
    });
    const t = mount(server);
    expect(await screen.findByText(/Choose the firm this invoice is from/)).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(2);
    expect(screen.getByRole('radio', { name: /Alpha LLP/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Beta LLP/ })).toBeInTheDocument();
    expect(next()).toBeDisabled();
    fireEvent.click(screen.getByRole('radio', { name: /Beta LLP/ }));
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await fillAndSubmit(t.container);
    await screen.findByText('Invoice received');
    expect(server.calls.find(c => c.method === 'POST')!.url).toContain(`firmId=${FIRM_B}`);
  });

  it('with no locked record the user chooses it from the records read from Dataverse; several firms are then asked for before the upload goes', async () => {
    const server = makeServer({
      firms: [
        { firmId: FIRM_A, name: 'Alpha LLP' },
        { firmId: FIRM_B, name: 'Beta LLP' },
      ],
    });
    const data = makeData({
      sprk_matter: [{ sprk_matterid: MATTER_ID, sprk_mattername: 'Acme v. Roadrunner', sprk_matternumber: 'M-1001' }],
      sprk_project: [
        {
          sprk_projectid: '22222222-2222-4222-8222-222222222222',
          sprk_name: 'Patent portfolio',
          sprk_referencenumber: 'P-77',
        },
      ],
    });
    const t = mount(server, { initialAssociation: undefined, lockAssociation: false }, data);
    fireEvent.click(await screen.findByRole('radio', { name: /Patent portfolio/ }));
    expect(screen.getByRole('radio', { name: /Acme v\. Roadrunner/ })).not.toBeChecked();
    fireEvent.click(next());
    fireEvent.change(t.container.querySelector('input[type="file"]') as HTMLInputElement, {
      target: { files: [pdf()] },
    });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());

    // The record has two firms: nothing is uploaded; the wizard asks the user to choose.
    expect(await screen.findByRole('radiogroup', { name: /Firms/ })).toBeInTheDocument();
    expect(server.calls.some(c => c.method === 'POST')).toBe(false);
    expect(server.calls.some(c => c.url.includes('/core-records/Project/'))).toBe(true);
  });

  it('says it is unavailable, and renders no flow, when the connection is not configured', () => {
    const server = makeServer();
    mount(server, { authenticatedFetch: undefined });
    expect(screen.getByRole('alert')).toHaveTextContent(/not available/i);
    expect(screen.queryByRole('button', { name: /^next$/i })).not.toBeInTheDocument();
  });

  it('a refusal by the server is shown and the wizard stays on Details', async () => {
    const server = makeServer();
    const original = server.fetchFn.getMockImplementation()!;
    server.fetchFn.mockImplementation(async (url: string, init?: RequestInit) =>
      init?.method === 'POST' && url.includes('invoice-submissions?')
        ? (reply(403, { errorCode: 'ebilling.submission.firm_not_linked' }) as unknown as Response)
        : original(url, init)
    );
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);
    expect(await screen.findByText(/firm you chose is not linked to this record/i)).toBeInTheDocument();
    expect(screen.queryByText('Invoice received')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Firm invoice number/i)).toBeInTheDocument();
  });
});

describe('CreateInvoiceWizard — a held submission (FR-19, D-24)', () => {
  const held = [heldStatus];

  it('shows the findings and Override + Return to firm when the server says the caller may override', async () => {
    const server = makeServer({ statuses: held });
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);
    expect(await screen.findByText(/Reason: ebilling\.submission\.blocked/)).toBeInTheDocument();
    expect(await screen.findByText('Partner rate may not exceed 650 per hour')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^override$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /return to firm/i })).toBeInTheDocument();
  });

  it('without the server signal there is no Override option at all; Return to firm remains', async () => {
    const server = makeServer({ statuses: held, blocked: heldView({ canOverride: false }) });
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);
    await screen.findByText('Partner rate may not exceed 650 per hour');
    expect(screen.queryByRole('button', { name: /override/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /return to firm/i })).toBeInTheDocument();
  });

  it('Override: an empty reason is refused in the form (no call), a reason is sent, and the screen follows the server to Accepted (overridden)', async () => {
    const server = makeServer({
      statuses: [
        heldStatus,
        { ...heldStatus, status: 'Accepted', outcomeReasonCode: 'ebilling.submission.overridden' },
      ],
    });
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);

    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    const submit = within(formModal()).getByRole('button', { name: /override and accept/i });
    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(server.calls.some(c => c.url.endsWith('/override'))).toBe(false);

    fireEvent.change(within(formModal()).getByLabelText(/reason/i), { target: { value: 'Negotiated rate' } });
    fireEvent.click(submit);
    await waitFor(() => expect(server.calls.filter(c => c.url.endsWith('/override'))).toHaveLength(1));
    expect(JSON.parse(String(server.calls.find(c => c.url.endsWith('/override'))!.body))).toEqual({
      reason: 'Negotiated rate',
    });

    // The held answer was followed to Accepted: the stale Rejected message is gone and the override is noted.
    expect(await screen.findByText(/The invoice was accepted/i, undefined, SLOW)).toBeInTheDocument();
    expect(await screen.findByTestId('invoice-accepted-overridden')).toHaveTextContent(/Accepted \(overridden\)/);
    expect(screen.queryByText(/Reason: ebilling\.submission\.blocked/)).not.toBeInTheDocument();
  });

  it('a 403 from the override call is shown and nothing changes: still held, form still open', async () => {
    const server = makeServer({
      statuses: held,
      decide: () => reply(403, { errorCode: 'ebilling.override.not_permitted' }),
    });
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);
    fireEvent.click(await screen.findByRole('button', { name: /^override$/i }));
    fireEvent.change(within(formModal()).getByLabelText(/reason/i), { target: { value: 'because' } });
    fireEvent.click(within(formModal()).getByRole('button', { name: /override and accept/i }));
    expect(await screen.findByTestId('blocked-decision-error')).toHaveTextContent(
      /do not hold the eBilling Override privilege/i
    );
    expect(screen.getByLabelText(/reason/i)).toHaveValue('because');
    expect(screen.queryByText(/The invoice was accepted/i)).not.toBeInTheDocument();
    expect(screen.getByText(/Reason: ebilling\.submission\.blocked/)).toBeInTheDocument();
  });

  it('Return to firm sends the reason to the return route and the screen shows it returned', async () => {
    const returned = { ...heldStatus, outcomeReasonCode: 'ebilling.submission.returned' };
    const server = makeServer({
      statuses: [heldStatus, returned],
      decide: () => {
        // From now on the server reads the submission as returned.
        server.s.blocked = heldView({
          state: 'Returned',
          canOverride: false,
          canReturn: false,
          return: { recordId: 'r', bySystemUserId: 'u', on: '2026-11-03T10:00:00Z', reason: 'Wrong matter' },
        });
        return reply(200, { submissionId: SUBMISSION_ID, outcome: 'Returned', alreadyDecided: false });
      },
    });
    const t = mount(server);
    await screen.findByRole('radio', { name: /Acme/ });
    await fillAndSubmit(t.container);
    fireEvent.click(await screen.findByRole('button', { name: /return to firm/i }));
    fireEvent.change(within(formModal()).getByLabelText(/reason/i), { target: { value: 'Wrong matter' } });
    fireEvent.click(within(formModal()).getByRole('button', { name: /^return to firm$/i }));
    await waitFor(() => expect(server.calls.filter(c => c.url.endsWith('/return'))).toHaveLength(1));
    expect(JSON.parse(String(server.calls.find(c => c.url.endsWith('/return'))!.body))).toEqual({
      reason: 'Wrong matter',
    });
    expect(await screen.findByTestId('blocked-state-returned')).toHaveTextContent('Reason: Wrong matter');
    // A Return is final: no more Override or Return buttons.
    await act(async () => undefined);
    expect(screen.queryByRole('button', { name: /^override$/i })).not.toBeInTheDocument();
  });
});
