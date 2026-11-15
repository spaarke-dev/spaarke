/**
 * CreateReportCardWizard — the host navigationService reaches the Assign Work step (ontology task 179, F-R4-2, D-219).
 *
 * This wizard owns its follow-on cards (`config.followOnCards`) and renders `AssignWorkFollowOnStep` itself, so the
 * service goes to the step directly. The generic shell is a stub that hands over its config; the test renders the
 * Assign Work card's step from that config and uses its real Select buttons.
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

function renderAssignWorkStep(navigationService?: ReturnType<typeof createMockNavigationService>) {
  renderWithProviders(
    <CreateReportCardWizard
      open
      onClose={jest.fn()}
      dataService={dataService}
      authenticatedFetch={jest.fn() as never}
      navigationService={navigationService}
    />
  );
  const card = mockShell.config!.followOnCards!.find(c => c.id === 'assign-work')!;
  expect(card).toBeDefined();
  renderWithProviders(<>{card.renderStep({} as IWizardShellHandle)}</>);
}

describe('CreateReportCardWizard — Assign Work side pane wiring', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('passes its navigationService to the Assign Work step: the attorney Select opens the contact pane (active only)', async () => {
    const nav = createMockNavigationService();
    renderAssignWorkStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Attorney' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('keeps the inline search fields when the host has no navigationService', () => {
    renderAssignWorkStep();

    expect(screen.queryByRole('button', { name: 'Select Assigned Attorney' })).not.toBeInTheDocument();
    expect(screen.getAllByPlaceholderText('Search contacts...')).toHaveLength(2);
  });
});
