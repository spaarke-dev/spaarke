/**
 * Create Event and Add To Do follow-on steps — side-pane lookups (ontology task 180, UAT round 4 F-R4-2, owner D-219).
 *
 * Both follow-ons wrap the same Enter Info forms as their wizards (CreateEventStep, CreateTodoStep), so with a
 * `navigationService` they render the same side-pane fields; without one they keep the inline search fields.
 * The tests drive the real follow-on steps; only the host services are doubles.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { CreateEventFollowOnStep } from '../CreateEventFollowOnStep';
import { AddTodoFollowOnStep } from '../AddTodoFollowOnStep';
import { EMPTY_EVENT_FORM } from '../../../CreateEventWizard/formTypes';
import { EMPTY_TODO_FORM } from '../../../CreateTodoWizard/formTypes';
import type { IDataService } from '../../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../../LookupField/lookupFilters';
import { createMockNavigationService } from '../../../../__mocks__/mockNavigationService';

jest.mock('../../../../services/userLookup', () => ({
  searchContactsAsLookup: jest.fn(async () => []),
  resolveCurrentUserAsContactAssignee: jest.fn(),
}));

const dataService = {} as IDataService;
const PICKED_ID = 'a1b2c3d4-0000-4000-8000-000000000001';

function renderEventFollowOn(navigationService?: ReturnType<typeof createMockNavigationService>) {
  const onFormValues = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <CreateEventFollowOnStep
        dataService={dataService}
        navigationService={navigationService}
        formValues={EMPTY_EVENT_FORM}
        onFormValues={onFormValues}
        onValidChange={jest.fn()}
      />
    </FluentProvider>
  );
  return { onFormValues };
}

function renderTodoFollowOn(navigationService?: ReturnType<typeof createMockNavigationService>) {
  const onFormValuesChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <AddTodoFollowOnStep
        dataService={dataService}
        navigationService={navigationService}
        formValues={EMPTY_TODO_FORM}
        onFormValuesChange={onFormValuesChange}
        onSearchAssignees={jest.fn(async () => [])}
      />
    </FluentProvider>
  );
  return { onFormValuesChange };
}

describe('CreateEventFollowOnStep — side-pane lookups', () => {
  it('with a navigationService renders Event Type and Assigned To as side-pane fields', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Hearing', entityType: 'sprk_eventtype_ref' }]);
    const { onFormValues } = renderEventFollowOn(nav);
    expect(screen.queryByPlaceholderText(/^Search /)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));

    await waitFor(() => {
      const last = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
      expect(last.eventTypeId).toBe(PICKED_ID);
      expect(last.eventTypeName).toBe('Hearing');
    });
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'sprk_eventtype_ref' });

    nav.openLookup.mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('without a navigationService keeps the inline search fields', () => {
    renderEventFollowOn();

    expect(screen.getByPlaceholderText('Search event types...')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Event Type' })).not.toBeInTheDocument();
  });
});

describe('AddTodoFollowOnStep — side-pane lookups', () => {
  it('with a navigationService renders Assigned To as a side-pane field over active contacts', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Alex Contact', entityType: 'contact' }]);
    const { onFormValuesChange } = renderTodoFollowOn(nav);
    expect(screen.queryByPlaceholderText('Search contacts...')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => {
      const last = onFormValuesChange.mock.calls[onFormValuesChange.mock.calls.length - 1][0];
      expect(last.assignedToId).toBe(PICKED_ID);
      expect(last.assignedToName).toBe('Alex Contact');
    });
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('without a navigationService keeps the inline search field', () => {
    renderTodoFollowOn();

    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Assigned To' })).not.toBeInTheDocument();
  });
});
