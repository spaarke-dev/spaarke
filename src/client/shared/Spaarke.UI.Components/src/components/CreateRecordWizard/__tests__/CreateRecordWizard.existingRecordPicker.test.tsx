/**
 * CreateRecordWizard — existingRecordPicker behavior (task 040)
 *
 * Verifies the opt-in `ICreateRecordWizardConfig.existingRecordPicker` affordance
 * rendered inside the built-in "Add file(s)" step:
 *   - Omitted (default) → no "Select Existing" button (unchanged behavior for
 *     every other consumer).
 *   - Configured → a "Select Existing {label}" button opens
 *     `navigationService.openLookup`; a picked record renders as a selected
 *     chip and is mutually exclusive with the upload dropzone.
 *
 * Added for ai-advanced-capabilities-analysis-hub-r1 task 040 (Analysis
 * wizard's Step 1 "upload OR select existing Document"). DOM-level assertions
 * only — no mock-internals or DI assertions (ADR-038).
 *
 * @see src/.../CreateRecordWizard/CreateRecordWizard.tsx (existingRecordPicker branch)
 * @see ./CreateRecordWizard.hideFilesStep.test.tsx (sibling reference shape)
 */

import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps } from '../types';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';

const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

function buildNavigationService(lookupResult: LookupResult[]): INavigationService {
  return {
    openRecord: jest.fn().mockResolvedValue(undefined),
    openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue(lookupResult),
  };
}

function buildConfig(existingRecordPicker?: ICreateRecordWizardConfig['existingRecordPicker']): ICreateRecordWizardConfig {
  return {
    title: 'Create New Analysis',
    entityLabel: 'analysis',
    existingRecordPicker,
    infoStep: {
      id: 'create-record',
      label: 'Analysis Details',
      canAdvance: () => false,
      renderContent: () => <div>analysis info</div>,
    },
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

describe('CreateRecordWizard — existingRecordPicker', () => {
  it('does not render a "Select Existing" button when existingRecordPicker is omitted', () => {
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(undefined)} />
    );

    expect(screen.queryByTestId('select-existing-record-button')).toBeNull();
  });

  it('opens the lookup and renders the selected record when picked', async () => {
    const navigationService = buildNavigationService([
      { id: '{abc-123}', name: 'MSA — Acme Corp', entityType: 'sprk_document' },
    ]);
    const config = buildConfig({
      navigationService,
      entityType: 'sprk_document',
      entityLabel: 'Document',
    });

    renderWithProviders(<CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={config} />);

    const button = screen.getByTestId('select-existing-record-button');
    expect(button).toHaveTextContent('Select Existing Document');

    await act(async () => {
      fireEvent.click(button);
      await Promise.resolve();
    });

    expect(navigationService.openLookup).toHaveBeenCalledWith(
      expect.objectContaining({ entityType: 'sprk_document', allowMultiSelect: false })
    );
    expect(screen.getByTestId('selected-existing-record')).toHaveTextContent('MSA — Acme Corp');
    // Button is replaced by the selected chip.
    expect(screen.queryByTestId('select-existing-record-button')).toBeNull();
  });

  it('clears the selection via the Clear button, restoring the picker button', async () => {
    const navigationService = buildNavigationService([{ id: 'abc-123', name: 'NDA Draft', entityType: 'sprk_document' }]);
    const config = buildConfig({ navigationService, entityType: 'sprk_document', entityLabel: 'Document' });

    renderWithProviders(<CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={config} />);

    await act(async () => {
      fireEvent.click(screen.getByTestId('select-existing-record-button'));
      await Promise.resolve();
    });
    expect(screen.getByTestId('selected-existing-record')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }));

    expect(screen.queryByTestId('selected-existing-record')).toBeNull();
    expect(screen.getByTestId('select-existing-record-button')).toBeInTheDocument();
  });
});
