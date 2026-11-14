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
import { act, fireEvent, screen, waitFor } from '@testing-library/react';

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
const CONTACT = { id: '66666666-0000-4000-8000-000000000006', name: 'Alex Contact' };

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

  it('keeps Assigned To when the user goes Back and Next again (fix round 1: the chip no longer clears)', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockImplementation(async opts =>
      opts.entityType === 'contact' ? [{ ...CONTACT, entityType: 'contact' }] : []
    );
    renderWithProviders(
      <CreateRecordWizard open onClose={jest.fn()} webApi={noopWebApi} config={buildConfig(jest.fn(), nav)} />
    );
    await goToCreateEvent();
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await screen.findByRole('button', { name: 'Clear Assigned To' });

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    await screen.findByRole('checkbox', { name: /Create Event/i });
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await screen.findByText(/Enter details for the event/);

    expect(screen.getByRole('button', { name: 'Clear Assigned To' })).toBeInTheDocument();
    expect(screen.getByText(CONTACT.name)).toBeInTheDocument();
  });

  it('delivers the Assigned To pick to onFinish in followOn (createEventAssignedToId / Name)', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockImplementation(async opts =>
      opts.entityType === 'contact' ? [{ ...CONTACT, entityType: 'contact' }] : []
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
    fireEvent.change(screen.getByPlaceholderText('Enter event name'), { target: { value: 'Filing deadline' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await screen.findByRole('button', { name: 'Clear Assigned To' });
    // The pick reaches the wizard through the step's effect and a parent re-render; let both flush before Finish.
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 0));
    });

    fireEvent.click(screen.getByRole('button', { name: 'Finish' }));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));

    expect(onFinish.mock.calls[0][0].followOn).toMatchObject({
      createEventName: 'Filing deadline',
      createEventAssignedToId: CONTACT.id,
      createEventAssignedToName: CONTACT.name,
    });
  });

  it('with no assignee picked, followOn carries empty Assigned To values', async () => {
    const onFinish = jest.fn(async (_ctx: IFinishContext) => ({
      title: 'done',
      body: <div>ok</div>,
      actions: <div />,
    }));
    renderWithProviders(
      <CreateRecordWizard
        open
        onClose={jest.fn()}
        webApi={noopWebApi}
        config={buildConfig(onFinish, createMockNavigationService())}
      />
    );
    await goToCreateEvent();
    fireEvent.change(screen.getByPlaceholderText('Enter event name'), { target: { value: 'Filing deadline' } });

    fireEvent.click(screen.getByRole('button', { name: 'Finish' }));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));

    expect(onFinish.mock.calls[0][0].followOn).toMatchObject({
      createEventAssignedToId: '',
      createEventAssignedToName: '',
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
