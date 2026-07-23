/**
 * CreateRecordWizard — grounding-optional "Add file(s)" step
 * (spaarkeai-assistant-enhancements-r1 task 014 / P6 / FR-A5 / ADR-039)
 *
 * A create flow MUST NOT require an attached document or session content.
 * Previously `addFilesStep.canAdvance` required `uploadedFiles.length > 0`,
 * disabling "Next" with no file even though the step is `isSkippable: true`
 * (the file header comment already documented the step as "always skip-able
 * (canAdvance: true)" — this was a drift bug). This suite asserts the fix:
 * "Next" is enabled with zero files, and the user can advance all the way to
 * the entity-info step without ever uploading anything.
 *
 * @see ../CreateRecordWizard.tsx (addFilesStep.canAdvance)
 * @see ./CreateRecordWizard.hideFilesStep.test.tsx (sibling reference shape)
 */
import * as React from 'react';
import { screen, fireEvent } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps } from '../types';

const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

function buildConfig(): ICreateRecordWizardConfig {
  return {
    title: 'Create New To Do',
    entityLabel: 'to do',
    infoStep: {
      id: 'create-record',
      label: 'To Do Details',
      canAdvance: () => true,
      renderContent: () => <div>to do info</div>,
    },
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

describe('CreateRecordWizard — grounding-optional Add file(s) step', () => {
  it('enables "Next" with zero files uploaded (no document/session content required)', () => {
    renderWithProviders(<CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig()} />);

    expect(screen.queryAllByText('Add file(s)').length).toBeGreaterThan(0);
    const nextButton = screen.getByRole('button', { name: 'Next' });
    expect(nextButton).not.toBeDisabled();
  });

  it('advances past Add file(s) into the entity-info step with no file uploaded', () => {
    renderWithProviders(<CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.getByText('to do info')).toBeInTheDocument();
  });
});
