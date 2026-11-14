/**
 * CreateEventWizard — the Enter Info step gets the host navigationService (ontology task 180, F-R4-2, D-219).
 *
 * The generic shell is a stub that hands over its config; the test renders the real Enter Info step from
 * `config.infoStep.renderContent` and uses its real Select buttons.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { CreateEventWizard } from '../CreateEventWizard';

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

function renderInfoStep(navigationService?: ReturnType<typeof createMockNavigationService>) {
  renderWithProviders(
    <CreateEventWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={jest.fn() as never}
      navigationService={navigationService}
    />
  );
  renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);
}

describe('CreateEventWizard — Enter Info side pane wiring', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('passes its navigationService to the Enter Info step: Event Type and Assigned To open their panes', async () => {
    const nav = createMockNavigationService();
    renderInfoStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(2));

    expect(nav.openLookup.mock.calls[0][0]).toEqual({ entityType: 'sprk_eventtype_ref' });
    expect(nav.openLookup.mock.calls[1][0]).toEqual({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('keeps the inline search fields when the host has no navigationService', () => {
    renderInfoStep();

    expect(screen.queryByRole('button', { name: 'Select Event Type' })).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search event types...')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
  });
});
