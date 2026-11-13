/**
 * CreateRecordWizard — Assign Work follow-on uses the side pane and its picks reach onFinish (ontology task 179,
 * UAT round 4 F-R4-2, owner D-219).
 *
 * Drives the real wizard through Info -> Next Steps -> Assign Work with `config.navigationService` set. Each lookup
 * opens the pane for its own table (the two person fields limited to active contacts); the picked ids and names reach
 * `onFinish` in the same `followOn` fields as with the inline search (assignedAttorneyId/Name, assignedParalegalId/Name,
 * assignedOutsideCounselId/Name, plus the two classification pairs).
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { CreateRecordWizard } from '../CreateRecordWizard';
import type { ICreateRecordWizardConfig, ICreateRecordWizardProps, IFinishContext } from '../types';

const noopWebApi: ICreateRecordWizardProps['webApi'] = {
  retrieveMultipleRecords: async () => ({ entities: [] }),
  retrieveRecord: async () => ({}),
  createRecord: async () => ({ id: 'stub' }),
};

const PICKS: Record<string, { id: string; name: string }> = {
  sprk_mattertype_ref: { id: '11111111-0000-4000-8000-000000000001', name: 'Litigation' },
  sprk_practicearea_ref: { id: '22222222-0000-4000-8000-000000000002', name: 'Employment' },
  contact: { id: '33333333-0000-4000-8000-000000000003', name: 'Alex Contact' },
  sprk_organization: { id: '44444444-0000-4000-8000-000000000004', name: 'Jones & Smith PC' },
};

function buildConfig(onFinish: ICreateRecordWizardConfig['onFinish'], withNav: boolean, nav: unknown) {
  const config: ICreateRecordWizardConfig = {
    title: 'Create New Project',
    entityLabel: 'project',
    hideFilesStep: true,
    infoStep: {
      id: 'create-record',
      label: 'Project Details',
      canAdvance: () => true,
      renderContent: () => <div>project info</div>,
    },
    onFinish,
    ...(withNav ? { navigationService: nav as never } : {}),
  };
  return config;
}

async function goToAssignWork(): Promise<void> {
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  const card = await screen.findByText('Assign Work');
  fireEvent.click(card);
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  await screen.findByText(/Create a work assignment linked to this record/);
}

describe('CreateRecordWizard — Assign Work side pane (config.navigationService)', () => {
  it('opens each pane for its table and delivers the picks to onFinish unchanged', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockImplementation(async opts => [{ ...PICKS[opts.entityType], entityType: opts.entityType }]);
    const onFinish = jest.fn(async (_ctx: IFinishContext) => ({
      title: 'done',
      body: <div>ok</div>,
      actions: <div />,
    }));
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(onFinish, true, nav)} />
    );
    await goToAssignWork();

    expect(screen.queryByPlaceholderText(/^Search /)).not.toBeInTheDocument();
    const order = [
      'Matter Type',
      'Practice Area',
      'Assigned Attorney',
      'Assigned Paralegal',
      'Assigned Outside Counsel',
    ];
    for (const label of order) {
      fireEvent.click(screen.getByRole('button', { name: `Select ${label}` }));
      await waitFor(() => expect(screen.getByRole('button', { name: `Clear ${label}` })).toBeInTheDocument());
    }
    fireEvent.change(screen.getByPlaceholderText('Enter work assignment name...'), {
      target: { value: 'Review file' },
    });

    expect(nav.openLookup.mock.calls.map(c => c[0].entityType)).toEqual([
      'sprk_mattertype_ref',
      'sprk_practicearea_ref',
      'contact',
      'contact',
      'sprk_organization',
    ]);
    expect(nav.openLookup.mock.calls.map(c => c[0].filters)).toEqual([
      undefined,
      undefined,
      ACTIVE_CONTACT_LOOKUP_FILTERS,
      ACTIVE_CONTACT_LOOKUP_FILTERS,
      undefined,
    ]);

    fireEvent.click(screen.getByRole('button', { name: 'Finish' }));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));
    const followOn = onFinish.mock.calls[0][0].followOn;
    expect(followOn).toMatchObject({
      assignWorkName: 'Review file',
      assignWorkMatterTypeId: PICKS.sprk_mattertype_ref.id,
      assignWorkMatterTypeName: 'Litigation',
      assignWorkPracticeAreaId: PICKS.sprk_practicearea_ref.id,
      assignWorkPracticeAreaName: 'Employment',
      assignedAttorneyId: PICKS.contact.id,
      assignedAttorneyName: 'Alex Contact',
      assignedParalegalId: PICKS.contact.id,
      assignedParalegalName: 'Alex Contact',
      assignedOutsideCounselId: PICKS.sprk_organization.id,
      assignedOutsideCounselName: 'Jones & Smith PC',
    });
  });

  it('without config.navigationService the step shows the inline search fields and no Select buttons', async () => {
    const onFinish = jest.fn();
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(onFinish, false, null)} />
    );
    await goToAssignWork();

    expect(screen.getByPlaceholderText('Search matter types...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Assigned Attorney' })).not.toBeInTheDocument();
  });
});
