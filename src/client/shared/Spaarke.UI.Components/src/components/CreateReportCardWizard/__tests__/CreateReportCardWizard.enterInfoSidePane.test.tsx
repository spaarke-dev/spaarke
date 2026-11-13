/**
 * CreateReportCardWizard — the Enter Info step and the Add To Do follow-on get the host navigationService
 * (ontology task 180, F-R4-2, D-219).
 *
 * The generic shell is a stub that hands over its config; the test renders the real Enter Info step and the real
 * Add To Do card step from that config and uses their real Select buttons.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { IWizardShellHandle } from '../../Wizard/wizardShellTypes';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { CreateReportCardWizard } from '../CreateReportCardWizard';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const dataService = {
  createRecord: jest.fn(),
  retrieveRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  updateRecord: jest.fn(),
  deleteRecord: jest.fn(),
} as unknown as IDataService;

function renderWizard(navigationService?: ReturnType<typeof createMockNavigationService>) {
  renderWithProviders(
    <CreateReportCardWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={jest.fn() as never}
      navigationService={navigationService}
    />
  );
}

describe('CreateReportCardWizard — Enter Info and Add To Do side pane wiring', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('passes its navigationService to the Enter Info step: Assigned Attorney 1 and Law Firm 1 open their panes', async () => {
    const nav = createMockNavigationService();
    renderWizard(nav);
    renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Attorney 1' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Law Firm 1' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(2));

    expect(nav.openLookup.mock.calls[0][0]).toEqual({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
    expect(nav.openLookup.mock.calls[1][0]).toEqual({ entityType: 'sprk_organization' });
  });

  it('passes its navigationService to the Add To Do follow-on: Assigned To opens the active-contact pane', async () => {
    const nav = createMockNavigationService();
    renderWizard(nav);
    const card = mockShell.config!.followOnCards!.find(c => c.id === 'add-todo')!;
    expect(card).toBeDefined();
    renderWithProviders(<>{card.renderStep({} as IWizardShellHandle)}</>);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('keeps the inline search fields in both steps when the host has no navigationService', () => {
    renderWizard();
    renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);
    expect(screen.queryByRole('button', { name: 'Select Assigned Attorney 1' })).not.toBeInTheDocument();
    expect(screen.getAllByPlaceholderText('Search contacts...')).toHaveLength(6);

    const card = mockShell.config!.followOnCards!.find(c => c.id === 'add-todo')!;
    renderWithProviders(<>{card.renderStep({} as IWizardShellHandle)}</>);
    expect(screen.queryByRole('button', { name: 'Select Assigned To' })).not.toBeInTheDocument();
  });
});
