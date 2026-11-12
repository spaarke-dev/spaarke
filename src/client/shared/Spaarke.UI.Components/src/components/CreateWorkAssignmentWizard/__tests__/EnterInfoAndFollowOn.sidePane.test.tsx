/**
 * EnterInfoStep and CreateFollowOnEventStep — side-pane lookups (ontology task 181, owner D-219).
 *
 * - With a navigationService, Matter Type / Practice Area (reference tables) and the contact Assigned To fields
 *   open the pane for the table their inline search queried; contact fields pass the active-contacts filter.
 * - Without a navigationService the steps render the inline search, as before.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the pane contract of the two steps.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { INavigationService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';

jest.mock('../workAssignmentService', () => ({
  searchMatterTypes: jest.fn(async () => []),
  searchPracticeAreas: jest.fn(async () => []),
  searchContactsAsLookup: jest.fn(async () => []),
}));

import { EnterInfoStep } from '../EnterInfoStep';
import { CreateFollowOnEventStep } from '../CreateFollowOnEventStep';

const nav = (openLookup: jest.Mock): INavigationService => ({ openLookup }) as unknown as INavigationService;
const selectButton = (label: string): HTMLElement => screen.getByRole('button', { name: `Select ${label}` });

function mountEnterInfo(navigationService?: INavigationService) {
  const onFormValues = jest.fn();
  renderWithProviders(
    <EnterInfoStep
      dataService={{} as never}
      onValidChange={jest.fn()}
      onFormValues={onFormValues}
      initialValues={{ name: 'WA-1', priority: 100000001, responseDueDate: '2026-12-01' }}
      hasInitialValues
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
      navigationService={navigationService}
    />
  );
  return { last: () => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0] as Record<string, unknown> };
}

describe('EnterInfoStep — side pane lookups (task 181)', () => {
  it('Matter Type, Practice Area and Assigned To open the pane for their tables with no inline input', async () => {
    const openLookup = jest.fn(async () => []);
    mountEnterInfo(nav(openLookup));

    for (const label of ['Matter Type', 'Practice Area', 'Assigned To']) {
      expect(screen.queryByRole('textbox', { name: label })).not.toBeInTheDocument();
    }

    fireEvent.click(selectButton('Matter Type'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'sprk_mattertype_ref' });

    fireEvent.click(selectButton('Practice Area'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'sprk_practicearea_ref' });

    fireEvent.click(selectButton('Assigned To'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(3));
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('a picked Matter Type, Practice Area and contact reach the form values', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([{ id: '{AAAA0000-0000-0000-0000-000000000001}', name: 'Litigation', entityType: 'x' }])
      .mockResolvedValueOnce([{ id: 'bbbb0000-0000-0000-0000-000000000002', name: 'Tax', entityType: 'x' }])
      .mockResolvedValueOnce([{ id: 'cccc0000-0000-0000-0000-000000000003', name: 'Chosen Contact', entityType: 'x' }]);
    const { last } = mountEnterInfo(nav(openLookup));

    fireEvent.click(selectButton('Matter Type'));
    await waitFor(() => expect(last()).toMatchObject({ matterTypeName: 'Litigation' }));
    expect(last().matterTypeId).toBe('aaaa0000-0000-0000-0000-000000000001');

    fireEvent.click(selectButton('Practice Area'));
    await waitFor(() => expect(last()).toMatchObject({ practiceAreaName: 'Tax' }));

    fireEvent.click(selectButton('Assigned To'));
    await waitFor(() =>
      expect(last()).toMatchObject({
        assignedToId: 'cccc0000-0000-0000-0000-000000000003',
        assignedToName: 'Chosen Contact',
      })
    );
  });

  it('without a navigationService the fields are the inline search, as before', () => {
    mountEnterInfo(undefined);

    for (const label of ['Matter Type', 'Practice Area', 'Assigned To']) {
      expect(screen.getByRole('textbox', { name: label })).toBeInTheDocument();
    }
    expect(screen.queryByRole('button', { name: /^Select / })).not.toBeInTheDocument();
  });
});

describe('CreateFollowOnEventStep — side pane lookups (task 181)', () => {
  function mountEvent(navigationService?: INavigationService) {
    const onFormValues = jest.fn();
    renderWithProviders(
      <CreateFollowOnEventStep
        dataService={{} as never}
        onValidChange={jest.fn()}
        onFormValues={onFormValues}
        navigationService={navigationService}
      />
    );
    return { last: () => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0] as Record<string, unknown> };
  }

  it('Assigned To opens the contact pane with the active-contacts filter and reports the pick', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([
        { id: 'cccc0000-0000-0000-0000-000000000003', name: 'Event Owner', entityType: 'contact' },
      ]);
    const { last } = mountEvent(nav(openLookup));

    expect(screen.queryByRole('textbox', { name: 'Assigned To' })).not.toBeInTheDocument();
    fireEvent.click(selectButton('Assigned To'));
    await waitFor(() => expect(last()).toMatchObject({ assignedToName: 'Event Owner' }));
    expect(openLookup).toHaveBeenCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });
  });

  it('without a navigationService Assigned To is the inline search, as before', () => {
    mountEvent(undefined);
    expect(screen.getByRole('textbox', { name: 'Assigned To' })).toBeInTheDocument();
  });
});
