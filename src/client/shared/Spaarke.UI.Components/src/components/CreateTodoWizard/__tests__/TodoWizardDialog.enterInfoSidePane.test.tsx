/**
 * TodoWizardDialog — the Enter Info step gets the host navigationService (ontology task 180, F-R4-2, D-219).
 *
 * The generic shell is a stub that hands over its config; the test renders the real Enter Info step from
 * `config.infoStep.renderContent` and uses its real Select button.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { TodoWizardDialog } from '../TodoWizardDialog';

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
    <TodoWizardDialog
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example.test"
      navigationService={navigationService}
    />
  );
  renderWithProviders(<>{mockShell.config!.infoStep.renderContent([])}</>);
}

describe('TodoWizardDialog — Enter Info side pane wiring', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('passes its navigationService to the Enter Info step: Assigned To opens the active-contact pane', async () => {
    const nav = createMockNavigationService();
    renderInfoStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('keeps the inline search field when the host has no navigationService', () => {
    renderInfoStep();

    expect(screen.queryByRole('button', { name: 'Select Assigned To' })).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
  });
});
