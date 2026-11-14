/**
 * CreateTodoStep — Assigned To side pane (ontology task 180, UAT round 4 F-R4-2, owner D-219).
 *
 * With a `navigationService` Assigned To opens the contact pane limited to active contacts, renders no inline search
 * input and writes the pick to assignedToId/assignedToName as the inline search did. Without one the step is unchanged
 * (the inline search keeps using `onSearchUsers`).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { CreateTodoStep } from '../CreateTodoStep';
import type { ICreateTodoFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';

const PICKED_ID = 'a1b2c3d4-0000-4000-8000-000000000001';

function renderStep(navigationService?: ReturnType<typeof createMockNavigationService>) {
  const onFormValues = jest.fn();
  const onSearchUsers = jest.fn(async () => []);
  render(
    <FluentProvider theme={webLightTheme}>
      <CreateTodoStep
        dataService={{} as IDataService}
        navigationService={navigationService}
        onSearchUsers={onSearchUsers}
        onValidChange={jest.fn()}
        onFormValues={onFormValues}
      />
    </FluentProvider>
  );
  const lastValues = (): ICreateTodoFormState => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
  return { lastValues, onSearchUsers };
}

describe('CreateTodoStep — Assigned To side pane', () => {
  it('opens the contact pane limited to active contacts and writes the pick unchanged', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: 'Alex Contact', entityType: 'contact' }]);
    const { lastValues } = renderStep(nav);
    expect(screen.queryByPlaceholderText('Search contacts...')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(lastValues().assignedToId).toBe(PICKED_ID));
    expect(lastValues().assignedToName).toBe('Alex Contact');
    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('cancelling the pane keeps the title already typed and leaves the assignee empty', async () => {
    const nav = createMockNavigationService(); // openLookup resolves [] = cancel
    const { lastValues } = renderStep(nav);
    fireEvent.change(screen.getByPlaceholderText('What needs to be done?'), { target: { value: 'Call the client' } });

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned To' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(lastValues()).toMatchObject({ title: 'Call the client', assignedToId: '', assignedToName: '' });
  });

  it('without a navigationService renders the inline search field and no Select button', () => {
    renderStep();

    expect(screen.getByPlaceholderText('Search contacts...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select Assigned To' })).not.toBeInTheDocument();
  });
});
