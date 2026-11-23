/**
 * CreateRecordWizard.existingRecordPicker.focus.test.tsx - task 200.
 *
 * After the "Select Existing" lookup settles, keyboard focus goes back to the one button the row renders (Select
 * Existing after a cancel, Clear after a pick): the platform pane leaves focus on <body> or on the link behind the
 * wizard, and the Select button is disabled while the pane is open.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps } from '../types';
import type { INavigationService } from '../../../types/serviceInterfaces';

const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

function buildConfig(navigationService: INavigationService): ICreateRecordWizardConfig {
  return {
    title: 'Create New Analysis',
    entityLabel: 'analysis',
    existingRecordPicker: { navigationService, entityType: 'sprk_document', entityLabel: 'Document' },
    infoStep: {
      id: 'create-record',
      label: 'Analysis Details',
      canAdvance: () => false,
      renderContent: () => <div>analysis info</div>,
    },
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

describe('CreateRecordWizard existingRecordPicker: focus returns to the row button (task 200)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
  });
  afterEach(() => outside.remove());

  function renderWizard(result: unknown[]) {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      return result;
    });
    const navigationService = { openLookup } as unknown as INavigationService;
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(navigationService)} />
    );
    return openLookup;
  }

  it('pick: focus ends on the Clear button that replaced the Select button', async () => {
    const openLookup = renderWizard([{ id: 'abc-123', name: 'NDA Draft', entityType: 'sprk_document' }]);

    fireEvent.click(screen.getByTestId('select-existing-record-button'));

    await waitFor(() => expect(openLookup).toHaveBeenCalled());
    const clear = await screen.findByRole('button', { name: 'Clear selection' });
    await waitFor(() => expect(document.activeElement).toBe(clear));
  });

  it('cancel: focus ends on the Select Existing button', async () => {
    const openLookup = renderWizard([]);

    fireEvent.click(screen.getByTestId('select-existing-record-button'));

    await waitFor(() => expect(openLookup).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(screen.getByTestId('select-existing-record-button')));
  });
});
