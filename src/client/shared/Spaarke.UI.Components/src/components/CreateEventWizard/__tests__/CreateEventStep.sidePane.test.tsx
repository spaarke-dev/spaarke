/**
 * CreateEventStep — side-pane lookups (ontology task 180, UAT round 4 F-R4-2, owner D-219).
 *
 * With a `navigationService`, Event Type (sprk_eventtype_ref) and Assigned To (contact, active only) open the Dataverse
 * side pane and render no inline search input; the picked id and name reach `onFormValues` in the same fields as the
 * inline search wrote (eventTypeId/Name, assignedToId/Name). Without a `navigationService` the step renders as before.
 * The tests drive the real step and the real DataverseLookupField; only the host services are doubles.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { CreateEventStep } from '../CreateEventStep';
import type { ICreateEventFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';

jest.mock('../../../services/userLookup', () => ({
  searchContactsAsLookup: jest.fn(async () => []),
  resolveCurrentUserAsContactAssignee: jest.fn(),
}));
import { resolveCurrentUserAsContactAssignee } from '../../../services/userLookup';

const dataService = {} as IDataService;
const PICKED_ID = 'a1b2c3d4-0000-4000-8000-000000000001';

function renderStep(navigationService?: ReturnType<typeof createMockNavigationService>) {
  const onValidChange = jest.fn();
  const onFormValues = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <CreateEventStep
        dataService={dataService}
        navigationService={navigationService}
        onValidChange={onValidChange}
        onFormValues={onFormValues}
      />
    </FluentProvider>
  );
  const lastValues = (): ICreateEventFormState => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
  return { onFormValues, lastValues };
}

describe('CreateEventStep — side-pane lookups (with a navigationService)', () => {
  afterEach(() => jest.clearAllMocks());

  it('renders a Select button per lookup and no inline search input', () => {
    renderStep(createMockNavigationService());

    expect(screen.getByRole('button', { name: 'Select Event Type' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Select Assigned To' })).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('Search event types...')).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText('Search contacts...')).not.toBeInTheDocument();
  });

  it('Event Type opens the sprk_eventtype_ref pane with no filter and writes the pick unchanged', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Hearing', entityType: 'sprk_eventtype_ref' }]);
    const { lastValues } = renderStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));

    await waitFor(() => expect(lastValues().eventTypeId).toBe(PICKED_ID));
    expect(lastValues().eventTypeName).toBe('Hearing');
    expect(nav.openLookup).toHaveBeenCalledTimes(1);
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'sprk_eventtype_ref' });
  });

  it('Assigned To opens the contact pane limited to active contacts and writes the pick unchanged', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Alex Contact', entityType: 'contact' }]);
    const { lastValues } = renderStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(lastValues().assignedToId).toBe(PICKED_ID));
    expect(lastValues().assignedToName).toBe('Alex Contact');
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('cancelling a pane keeps the value already chosen and the other fields', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValueOnce([{ id: PICKED_ID, name: 'Hearing', entityType: 'sprk_eventtype_ref' }]);
    const { lastValues } = renderStep(nav);
    fireEvent.change(screen.getByPlaceholderText('Enter event name'), { target: { value: 'Filing deadline' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));
    await waitFor(() => expect(lastValues().eventTypeId).toBe(PICKED_ID));

    nav.openLookup.mockResolvedValueOnce([]); // cancel
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(2));

    expect(lastValues()).toMatchObject({ eventName: 'Filing deadline', eventTypeId: PICKED_ID, assignedToId: '' });
  });

  it('a failed pane is a cancel: no change and no unhandled rejection', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockRejectedValue(new Error('no Xrm'));
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const { lastValues } = renderStep(nav);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(lastValues().assignedToId).toBe('');
    warn.mockRestore();
  });

  it('clearing a chip writes empty ids', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Hearing', entityType: 'sprk_eventtype_ref' }]);
    const { lastValues } = renderStep(nav);
    fireEvent.click(screen.getByRole('button', { name: 'Select Event Type' }));
    await waitFor(() => expect(lastValues().eventTypeId).toBe(PICKED_ID));

    fireEvent.click(screen.getByRole('button', { name: 'Clear Event Type' }));

    await waitFor(() => expect(lastValues().eventTypeId).toBe(''));
    expect(lastValues().eventTypeName).toBe('');
  });

  it('"Assign to me" still fills Assigned To', async () => {
    (resolveCurrentUserAsContactAssignee as jest.Mock).mockResolvedValue({ id: PICKED_ID, name: 'Jane Attorney' });
    const { lastValues } = renderStep(createMockNavigationService());

    fireEvent.click(screen.getByRole('button', { name: 'Assign to me' }));

    await waitFor(() => expect(lastValues().assignedToId).toBe(PICKED_ID));
    expect(screen.getByRole('button', { name: 'Clear Assigned To' })).toBeInTheDocument();
  });
});

describe('CreateEventStep — no navigationService (unchanged inline fields)', () => {
  it('renders the inline search fields and never offers a pane', () => {
    renderStep();

    expect(screen.getByPlaceholderText('Search event types...')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Event Type' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Assigned To' })).not.toBeInTheDocument();
  });
});
