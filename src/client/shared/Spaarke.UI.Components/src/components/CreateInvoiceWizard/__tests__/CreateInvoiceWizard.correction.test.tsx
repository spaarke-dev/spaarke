/**
 * CreateInvoiceWizard — the corrected-invoice launch (ebilling-r1 task 095f-2e2b; EB-D-245).
 *
 * `correctionInvoiceId` (what Invoice Review's "Submit corrected invoice" opens the code page with) makes the wizard read the invoice's
 * correction prefill and then run the shared wizard as a Correction. The real wizard, adapter and shell run; only the transport is faked.
 * `CreateInvoiceWizard.test.tsx` runs unchanged beside this file and is the must-not-fire proof for the new-invoice launch.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateInvoiceWizard } from '../CreateInvoiceWizard';

jest.setTimeout(30000);

const BASE = 'https://bff.example.test';
const MATTER_ID = '11111111-1111-4111-8111-111111111111';
const FIRM = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const INVOICE_ID = '44444444-4444-4444-8444-444444444444';
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

const reply = (status: number, body: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body,
});

const PREFILL = {
  invoiceId: INVOICE_ID,
  firm: { id: FIRM, name: 'Alpha LLP' },
  coreRecord: { entityType: 'Matter', entityId: MATTER_ID, name: 'Acme v. Roadrunner' },
  invoiceStatus: 'InReview',
  trackingNumber: 'INV-9.1',
  firmInvoiceNumber: 'INV-9',
  canCorrect: true,
  reason: null,
};

interface Server {
  prefill: () => unknown;
  submit: () => unknown;
}

function makeServer(over: Partial<Server> = {}) {
  const s: Server = {
    prefill: () => reply(200, PREFILL),
    submit: () => reply(202, { submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2' }),
    ...over,
  };
  const calls: { url: string; method: string; body?: unknown }[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    calls.push({ url, method, body: init?.body });
    const path = url.replace(BASE, '');
    if (path === `/api/finance/invoices/${INVOICE_ID}/correction-prefill`) return s.prefill();
    if (/\/invoice-submissions\?firmId=/.test(path) && method === 'POST') return s.submit();
    if (path === `/api/finance/invoice-submissions/${SUBMISSION_ID}`) {
      return reply(200, {
        submissionId: SUBMISSION_ID,
        status: 'Accepted',
        trackingNumber: 'INV-9.2',
        firmInvoiceNumber: 'INV-9',
      });
    }
    return reply(404, { errorCode: 'test.unrouted' });
  });
  return { fetchFn, calls };
}

function mount(
  server: ReturnType<typeof makeServer>,
  props: Partial<React.ComponentProps<typeof CreateInvoiceWizard>> = {}
) {
  const onClose = jest.fn();
  const data = {
    retrieveMultipleRecords: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
    retrieveRecord: jest.fn(),
  } as unknown as IDataService;
  const utils = renderWithProviders(
    <CreateInvoiceWizard
      open
      embedded
      onClose={onClose}
      dataService={data}
      authenticatedFetch={server.fetchFn as unknown as typeof fetch}
      bffBaseUrl={BASE}
      correctionInvoiceId={INVOICE_ID}
      {...props}
    />
  );
  return { ...utils, onClose, data };
}

const next = () => screen.getByRole('button', { name: /^next$/i });
const submitButton = () => screen.getByRole('button', { name: /^submit invoice$/i });
const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });

async function toDetails(container: HTMLElement) {
  await waitFor(() => expect(container.querySelector('input[type="file"]')).not.toBeNull());
  fireEvent.change(container.querySelector('input[type="file"]') as HTMLInputElement, { target: { files: [pdf()] } });
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  await screen.findByLabelText(/Firm invoice number/i);
}

describe('CreateInvoiceWizard — correctionInvoiceId', () => {
  it('reads the prefill, prefills the number, locks the record and the firm, requires the note, and posts the Correction', async () => {
    const server = makeServer();
    const t = mount(server);
    expect(screen.getByText('Reading the invoice')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Submit corrected invoice' })).toBeInTheDocument();
    // No Record step and no firm step; the record and the firm are the invoice's own.
    expect(screen.queryByRole('radiogroup', { name: 'Records' })).not.toBeInTheDocument();
    await toDetails(t.container);
    expect((screen.getByLabelText(/Firm invoice number/i) as HTMLInputElement).value).toBe('INV-9');
    expect(submitButton()).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Correction note'), { target: { value: 'Wrong rate card' } });
    fireEvent.click(submitButton());

    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(screen.getByTestId('invoice-tracking-number')).toHaveTextContent('INV-9.2');

    const reads = server.calls.filter(c => c.method === 'GET' && c.url.includes('correction-prefill'));
    expect(reads).toHaveLength(1);
    const posts = server.calls.filter(c => c.method === 'POST');
    expect(posts).toHaveLength(1);
    expect(posts[0].url).toBe(`${BASE}/api/finance/matters/${MATTER_ID}/invoice-submissions?firmId=${FIRM}`);
    const form = posts[0].body as FormData;
    expect(form.getAll('mode')).toEqual(['Correction']);
    expect(form.getAll('invoiceId')).toEqual([INVOICE_ID]);
    expect(form.getAll('correctionNote')).toEqual(['Wrong rate card']);
    expect(form.getAll('firmInvoiceNumber')).toEqual(['INV-9']);
    // The firms and the record list are not read: the invoice's own firm and record are used. Nothing is written to Dataverse.
    expect(server.calls.some(c => c.url.includes('/firms'))).toBe(false);
    expect(t.data.retrieveMultipleRecords).not.toHaveBeenCalled();
    expect(t.data.createRecord).not.toHaveBeenCalled();
  });

  it('a double click of Submit posts exactly one Correction', async () => {
    let answer!: (v: unknown) => void;
    const server = makeServer({ submit: () => new Promise(resolve => (answer = resolve)) as never });
    const t = mount(server);
    await toDetails(t.container);
    fireEvent.change(screen.getByLabelText('Correction note'), { target: { value: 'Wrong rate card' } });
    const button = submitButton();
    await act(async () => {
      button.click();
      button.click();
    });
    expect(server.calls.filter(c => c.method === 'POST')).toHaveLength(1);
    await act(async () => answer(reply(202, { submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2' })));
    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(server.calls.filter(c => c.method === 'POST')).toHaveLength(1);
  });

  it('shows the server refusal of the Correction on the Details step (409) and keeps what was typed', async () => {
    const server = makeServer({
      submit: () =>
        reply(409, {
          errorCode: 'ebilling.submission.correction_not_allowed',
          reason: 'invoice_status',
          detail: 'Only an invoice in review can be corrected; this one has moved on, so nothing was saved.',
        }),
    });
    const t = mount(server);
    await toDetails(t.container);
    fireEvent.change(screen.getByLabelText('Correction note'), { target: { value: 'Wrong rate card' } });
    fireEvent.click(submitButton());
    expect(await screen.findByText(/this one has moved on/)).toBeInTheDocument();
    expect((screen.getByLabelText('Correction note') as HTMLTextAreaElement).value).toBe('Wrong rate card');
  });

  it('an invoice the intake will not correct shows the plain reason and Close, with no wizard', async () => {
    const server = makeServer({
      prefill: () => reply(200, { ...PREFILL, invoiceStatus: 'Approved', canCorrect: false, reason: 'invoice_status' }),
    });
    const t = mount(server);
    const refused = await screen.findByTestId('correction-refused');
    expect(refused).toHaveTextContent(
      'Only an invoice in review can be corrected; this one has moved on, so nothing was saved.'
    );
    expect(screen.queryByRole('button', { name: /^next$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /try again/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    expect(t.onClose).toHaveBeenCalledTimes(1);
    expect(server.calls.some(c => c.method === 'POST')).toBe(false);
  });

  it('an unavailable invoice (404) says so; a failed read (503) offers Try again, which reads again', async () => {
    const gone = makeServer({ prefill: () => reply(404, { reasonCode: 'sdap.access.deny.record_unavailable' }) });
    const first = mount(gone);
    expect(await screen.findByTestId('correction-refused')).toHaveTextContent(
      /not available to you, or no longer exists/
    );
    expect(screen.queryByRole('button', { name: /try again/i })).toBeInTheDocument();
    first.unmount();

    let fail = true;
    const flaky = makeServer({
      prefill: () => (fail ? reply(503, { errorCode: 'ebilling.submission.record_unavailable' }) : reply(200, PREFILL)),
    });
    mount(flaky);
    expect(await screen.findByTestId('correction-refused')).toHaveTextContent(
      /could not be read right now\. Try again shortly\./
    );
    fail = false;
    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    expect(await screen.findByRole('heading', { name: 'Submit corrected invoice' })).toBeInTheDocument();
    expect(flaky.calls.filter(c => c.url.includes('correction-prefill'))).toHaveLength(2);
  });

  it('an invoice with no firm cannot be offered to the wizard (the internal route needs the firm named)', async () => {
    const server = makeServer({ prefill: () => reply(200, { ...PREFILL, firm: null }) });
    mount(server);
    expect(await screen.findByTestId('correction-refused')).toHaveTextContent(/cannot be corrected from here/);
  });

  it('an id that is not a GUID reads nothing and says so', async () => {
    const server = makeServer();
    mount(server, { correctionInvoiceId: 'not-a-guid' });
    expect(await screen.findByTestId('correction-refused')).toHaveTextContent(/could not be identified/);
    expect(server.fetchFn).not.toHaveBeenCalled();
  });

  it('must-not-fire: without correctionInvoiceId nothing is read from the prefill route and the title is the new-invoice title', async () => {
    const server = makeServer();
    mount(server, { correctionInvoiceId: undefined });
    expect(await screen.findByRole('heading', { name: 'Create New Invoice' })).toBeInTheDocument();
    expect(server.calls.some(c => c.url.includes('correction-prefill'))).toBe(false);
  });
});
