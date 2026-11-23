/**
 * TodoWizardDialog — the host navigationService reaches the wizard config (ontology task 179, F-R4-2, D-219).
 *
 * The Assign Work follow-on opens the Dataverse side pane only when `ICreateRecordWizardConfig.navigationService`
 * is set. The generic shell is a stub that hands over its config.
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
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

function renderWizard(navigationService?: ReturnType<typeof createMockNavigationService>) {
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
}

describe('TodoWizardDialog — Assign Work side pane wiring', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('passes its navigationService into the wizard config', () => {
    const nav = createMockNavigationService();
    renderWizard(nav);
    expect(mockShell.config!.navigationService).toBe(nav);
  });

  it('leaves the config without a navigationService when the host has none', () => {
    renderWizard();
    expect(mockShell.config!.navigationService).toBeUndefined();
  });
});
