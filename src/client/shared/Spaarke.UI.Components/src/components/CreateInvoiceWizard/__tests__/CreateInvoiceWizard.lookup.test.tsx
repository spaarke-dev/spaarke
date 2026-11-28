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

  it('a cancelled lookup (no rows) or a row of another table selects nothing', async () => {
    const { service } = navigation([{ id: PROJECT_ID, name: 'A contact', entityType: 'contact' }]);
    mount({ navigationService: service });
    fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /choose a record/i })).toBeEnabled());
    expect(screen.queryByTestId('selected-record')).toBeNull();
    expect(next()).toBeDisabled();
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
