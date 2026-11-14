/**
 * CreateRecordWizard — Send Email pre-fill runs once (spaarke-ontology-platform-r1 task 182 review finding).
 *
 * The Send Email step pre-fills the subject and body from the record name. Before the fix the pre-fill ran whenever the
 * subject was empty, so clearing the subject to retype it reset the subject AND the body (losing a template or AI draft).
 * Must-fire: the pre-fill happens when the step first opens. Must-not-fire: clearing the subject leaves it empty.
 */

import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';

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
    title: 'Create New Project',
    entityLabel: 'project',
    hideFilesStep: true,
    infoStep: {
      id: 'create-record',
      label: 'Project Details',
      canAdvance: () => true,
      renderContent: () => <div>project info</div>,
    },
    getEntityName: () => 'Acme',
    onFinish: async () => ({ title: 'done', body: <div>ok</div>, actions: <div /> }),
  };
}

const clickNext = () => {
  const next = screen.getAllByRole('button', { name: 'Next' }).find(b => !b.hasAttribute('disabled'));
  if (!next) throw new Error('no enabled Next button');
  fireEvent.click(next);
};

describe('CreateRecordWizard — Send Email pre-fill (task 182)', () => {
  it('pre-fills once; clearing the subject to retype it does not bring the pre-fill back', async () => {
    renderWithProviders(<CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig()} />);

    clickNext(); // Project Details -> Next Steps
    fireEvent.click(await screen.findByRole('checkbox', { name: /Send Notification Email/i }));
    clickNext(); // Next Steps -> Send Email

    const subject = await screen.findByRole('textbox', { name: 'Subject' });
    await waitFor(() => expect(subject).toHaveValue('New Record: Acme'));

    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: '' } });
    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByRole('textbox', { name: 'Subject' })).toHaveValue('');
  });
});
