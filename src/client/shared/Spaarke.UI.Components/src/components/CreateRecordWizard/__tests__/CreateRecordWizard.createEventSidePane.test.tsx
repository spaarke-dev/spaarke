/**
 * CreateRecordWizard — the Create Event follow-on uses the side pane when `config.navigationService` is set
 * (ontology task 180, UAT round 4 F-R4-2, owner D-219).
 *
 * Drives the real wizard through Info -> Next Steps -> Create Event. Event Type opens the sprk_eventtype_ref pane and
 * Assigned To the active-contact pane; the picked Event Type reaches `onFinish` in the same `followOn` fields
 * (createEventTypeId / createEventTypeName) as with the inline search. Without `config.navigationService` the step
 * keeps the inline search fields.
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

const EVENT_TYPE = { id: '55555555-0000-4000-8000-000000000005', name: 'Hearing' };

function buildConfig(onFinish: ICreateRecordWizardConfig['onFinish'], nav?: unknown) {
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
    eventDataService: noopWebApi as never,
    onFinish,
    ...(nav ? { navigationService: nav as never } : {}),
  };
  return config;
}

async function goToCreateEvent(): Promise<void> {
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  const card = await screen.findByText('Create Event');
  fireEvent.click(card);
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  await screen.findByText(/Enter details for the event/);
}

describe('CreateRecordWizard — Create Event follow-on side pane (config.navigationService)', () => {
  it('opens each pane for its table and delivers the Event Type pick to onFinish unchanged', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockImplementation(async opts =>
      opts.entityType === 'sprk_eventtype_ref' ? [{ ...EVENT_TYPE, entityType: opts.entityType }] : []
    );
    const onFinish = jest.fn(async (_ctx: IFinishContext) => ({
      title: 'done',
      body: <div>ok</div>,
      actions: <div />,
    }));
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(onFinish, nav)} />
    );
    await goToCreateEvent();

    expect(screen.queryByPlaceholderText(/^Search /)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Clear Event Type' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(2));
    fireEvent.change(screen.getByPlaceholderText('Enter event name'), { target: { value: 'Filing deadline' } });

    expect(nav.openLookup.mock.calls.map(c => c[0].entityType)).toEqual(['sprk_eventtype_ref', 'contact']);
    expect(nav.openLookup.mock.calls.map(c => c[0].filters)).toEqual([undefined, ACTIVE_CONTACT_LOOKUP_FILTERS]);

    fireEvent.click(screen.getByRole('button', { name: 'Finish' }));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));
    expect(onFinish.mock.calls[0][0].followOn).toMatchObject({
      createEventName: 'Filing deadline',
      createEventTypeId: EVENT_TYPE.id,
      createEventTypeName: 'Hearing',
    });
  });

  it('without config.navigationService the step shows the inline search fields and no Select buttons', async () => {
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(jest.fn())} />
    );
    await goToCreateEvent();

    expect(screen.getByPlaceholderText('Search event types...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Event Type' })).not.toBeInTheDocument();
  });
});
