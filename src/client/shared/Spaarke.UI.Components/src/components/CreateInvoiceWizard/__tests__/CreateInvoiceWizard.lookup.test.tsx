/**
 * CreateInvoiceWizard — the Record step's Dataverse lookup (ebilling-r1 def071, GitHub #3335).
 *
 * Launched with no record (the Console's Create Invoice card) and a host navigation service, the Record step offers "Choose a record":
 * the host's lookup over the three core tables. Without a navigation service the step is the list only (the in-app host passes one only
 * in a frame that has `Xrm.Utility.lookupObjects`: see InAppWizardHost.invoice.test.tsx); a launch from a record (locked) has no Record step at all.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IDataService, INavigationService } from '../../../types/serviceInterfaces';
import { CreateInvoiceWizard } from '../CreateInvoiceWizard';

const BASE = 'https://bff.example.test';
const MATTER_ID = '11111111-1111-4111-8111-111111111111';
const PROJECT_ID = '22222222-2222-4222-8222-222222222222';
const WORK_ID = '44444444-4444-4444-8444-444444444444';
const FIRM_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const FIRM_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

const fetchFn = jest.fn(async () => ({ ok: true, status: 200, json: async () => [] }));
const dataService = {
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  createRecord: jest.fn(),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
  retrieveRecord: jest.fn(),
} as unknown as IDataService;

function navigation(result: Array<{ id: string; name: string; entityType: string }>) {
  const openLookup = jest.fn().mockResolvedValue(result);
  const service = {
    openRecord: jest.fn(),
    openDialog: jest.fn(),
    closeDialog: jest.fn(),
    openLookup,
  } as INavigationService;
  return { service, openLookup };
}

/** A BFF stand-in: the firms of any record, the 202 of a submission, and an accepted status. */
function bff(firms: { firmId: string; name: string }[]) {
  const calls: { url: string; method: string; body?: unknown }[] = [];
  const fn = jest.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    calls.push({ url, method, body: init?.body });
    if (/\/core-records\/\w+\/[\w-]+\/firms$/.test(url)) return { ok: true, status: 200, json: async () => firms };
    if (method === 'POST' && /\/invoice-submissions\?firmId=/.test(url)) {
      return { ok: true, status: 202, json: async () => ({ submissionId: 's-1', trackingNumber: 'INV-9.1' }) };
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({
        submissionId: 's-1',
        status: 'Accepted',
        trackingNumber: 'INV-9.1',
        firmInvoiceNumber: 'INV-9',
      }),
    };
  });
  return { fn: fn as unknown as typeof fetch, calls };
}

const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });
const submitButton = () => screen.getByRole('button', { name: /^submit invoice$/i });

/** From the Record step (a record chosen) through the PDF and the invoice number to the Submit click. */
async function uploadAndSubmit(container: HTMLElement) {
  fireEvent.click(next());
  fireEvent.change(container.querySelector('input[type="file"]') as HTMLInputElement, { target: { files: [pdf()] } });
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
  fireEvent.click(submitButton());
}

function mount(props: Partial<React.ComponentProps<typeof CreateInvoiceWizard>> = {}) {
  return renderWithProviders(
    <CreateInvoiceWizard
      open
      embedded
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={fetchFn as unknown as typeof fetch}
      bffBaseUrl={BASE}
      {...props}
    />
  );
}

const next = () => screen.getByRole('button', { name: /^next$/i });

describe('CreateInvoiceWizard — record lookup', () => {
  it('must fire: with a navigation service, "Choose a record" looks up the three core tables and selects the pick', async () => {
    const { service, openLookup } = navigation([
      { id: `{${MATTER_ID.toUpperCase()}}`, name: 'Acme v. Roadrunner', entityType: 'sprk_matter' },
    ]);
    mount({ navigationService: service });

    const button = await screen.findByRole('button', { name: /choose a record/i });
    expect(next()).toBeDisabled();
    fireEvent.click(button);

    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Acme v. Roadrunner · Matter'));
    expect(openLookup).toHaveBeenCalledWith(
      expect.objectContaining({
        entityTypes: ['sprk_matter', 'sprk_project', 'sprk_workassignment'],
        allowMultiSelect: false,
      })
    );
    expect(next()).toBeEnabled();
  });

  it('maps a project and a work assignment pick to their kinds', async () => {
    const { service } = navigation([{ id: PROJECT_ID, name: 'Patent portfolio', entityType: 'sprk_project' }]);
    mount({ navigationService: service });
    fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));
    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Patent portfolio · Project'));
  });

  it.each([
    ['a cancelled lookup (no rows)', [] as Array<{ id: string; name: string; entityType: string }>],
    ['a row of another table', [{ id: PROJECT_ID, name: 'A contact', entityType: 'contact' }]],
    ['a row with no id', [{ id: '', name: 'Nameless', entityType: 'sprk_matter' }]],
  ])('%s selects nothing', async (_name, rows) => {
    const { service, openLookup } = navigation(rows);
    mount({ navigationService: service });
    fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: /choose a record/i })).toBeEnabled());
    expect(screen.queryByTestId('selected-record')).toBeNull();
    expect(next()).toBeDisabled();
  });

  it.each([
    ['matter', 'sprk_matter', 'matters', MATTER_ID],
    ['project', 'sprk_project', 'projects', PROJECT_ID],
    ['work assignment', 'sprk_workassignment', 'workassignments', WORK_ID],
  ])(
    'submits a looked-up %s to its own route, with the braces stripped and the id lower-cased',
    async (_label, entityType, segment, id) => {
      const { service } = navigation([{ id: `{${id.toUpperCase()}}`, name: 'Picked', entityType }]);
      const server = bff([{ firmId: FIRM_A, name: 'Alpha LLP' }]);
      const t = mount({ navigationService: service, authenticatedFetch: server.fn });
      fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));
      await waitFor(() => expect(next()).toBeEnabled());
      await uploadAndSubmit(t.container);

      await waitFor(() => expect(server.calls.some(c => c.method === 'POST')).toBe(true));
      const post = server.calls.find(c => c.method === 'POST')!;
      expect(post.url).toBe(`${BASE}/api/finance/${segment}/${id}/invoice-submissions?firmId=${FIRM_A}`);
      expect(post.url).not.toMatch(/[{}]/);
    }
  );

  it('must fire: a looked-up record survives the firm step that firm_required brings in, and the POST goes to it with the chosen firm', async () => {
    const { service } = navigation([
      { id: `{${PROJECT_ID.toUpperCase()}}`, name: 'Beyond the first page', entityType: 'sprk_project' },
    ]);
    const server = bff([
      { firmId: FIRM_A, name: 'Alpha LLP' },
      { firmId: FIRM_B, name: 'Beta LLP' },
    ]);
    const t = mount({ navigationService: service, authenticatedFetch: server.fn });
    fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));
    await waitFor(() => expect(next()).toBeEnabled());
    await uploadAndSubmit(t.container);

    // The record has two firms: nothing was uploaded; the Choose firm step is first.
    expect(await screen.findByRole('radiogroup', { name: /Firms/ })).toBeInTheDocument();
    expect(server.calls.some(c => c.method === 'POST')).toBe(false);
    fireEvent.click(screen.getByRole('radio', { name: /Beta LLP/ }));
    fireEvent.click(next());

    // The Record step: the looked-up record is still the selection and Next is on.
    await waitFor(() =>
      expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page · Project')
    );
    expect(next()).toBeEnabled();

    fireEvent.click(next());
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.click(await screen.findByRole('button', { name: /^submit invoice$/i }));

    await waitFor(() => expect(server.calls.some(c => c.method === 'POST')).toBe(true));
    expect(server.calls.find(c => c.method === 'POST')!.url).toBe(
      `${BASE}/api/finance/projects/${PROJECT_ID}/invoice-submissions?firmId=${FIRM_B}`
    );
  });

  it('must not fire: no navigation service means the list only', async () => {
    mount();
    await screen.findByText(/Choose the record this invoice is for/);
    expect(screen.queryByRole('button', { name: /choose a record/i })).toBeNull();
  });

  it('must not fire: a locked launch has no Record step and so no lookup', async () => {
    const { service } = navigation([]);
    mount({
      navigationService: service,
      initialAssociation: { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'Acme v. Roadrunner' },
      lockAssociation: true,
    });
    expect(await screen.findByText(/Upload the invoice PDF/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /choose a record/i })).toBeNull();
    expect(screen.queryByText(/Choose the record this invoice is for/)).toBeNull();
  });
});
