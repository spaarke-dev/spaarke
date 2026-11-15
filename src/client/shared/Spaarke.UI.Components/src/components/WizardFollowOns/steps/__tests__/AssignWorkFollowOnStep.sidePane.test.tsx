/**
 * AssignWorkFollowOnStep — side-pane lookups (ontology task 179, UAT round 4 F-R4-2, owner D-219).
 *
 * With a `navigationService` every lookup on the step (Matter Type, Practice Area, Assigned Attorney, Assigned
 * Paralegal, Assigned Outside Counsel) opens the Dataverse side pane for its own table and renders no inline search
 * input; the two person fields limit the pane to active contacts. Without a `navigationService` the step keeps the
 * inline search fields. The tests drive the real step and the real DataverseLookupField; only the host service is a double.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { AssignWorkFollowOnStep, WORK_ASSIGNMENT_PRIORITY } from '../AssignWorkFollowOnStep';
import type { IAssignWorkFollowOnStepProps } from '../AssignWorkFollowOnStep';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../../LookupField/lookupFilters';
import { createMockNavigationService } from '../../../../__mocks__/mockNavigationService';

function baseProps(overrides?: Partial<IAssignWorkFollowOnStepProps>): IAssignWorkFollowOnStepProps {
  return {
    nameValue: '',
    onNameChange: jest.fn(),
    descriptionValue: '',
    onDescriptionChange: jest.fn(),
    matterTypeValue: null,
    onMatterTypeChange: jest.fn(),
    onSearchMatterTypes: jest.fn(async () => []),
    practiceAreaValue: null,
    onPracticeAreaChange: jest.fn(),
    onSearchPracticeAreas: jest.fn(async () => []),
    priorityValue: WORK_ASSIGNMENT_PRIORITY.Normal,
    onPriorityChange: jest.fn(),
    responseDueDateValue: '',
    onResponseDueDateChange: jest.fn(),
    attorneyValue: null,
    onAttorneyChange: jest.fn(),
    onSearchAttorneys: jest.fn(async () => []),
    paralegalValue: null,
    onParalegalChange: jest.fn(),
    onSearchParalegals: jest.fn(async () => []),
    outsideCounselValue: null,
    onOutsideCounselChange: jest.fn(),
    onSearchOutsideCounsel: jest.fn(async () => []),
    ...overrides,
  };
}

function renderStep(props: IAssignWorkFollowOnStepProps) {
  render(
    <FluentProvider theme={webLightTheme}>
      <AssignWorkFollowOnStep {...props} />
    </FluentProvider>
  );
}

const LOOKUP_FIELDS = [
  { label: 'Matter Type', entityType: 'sprk_mattertype_ref', person: false, change: 'onMatterTypeChange' },
  { label: 'Practice Area', entityType: 'sprk_practicearea_ref', person: false, change: 'onPracticeAreaChange' },
  { label: 'Assigned Attorney', entityType: 'contact', person: true, change: 'onAttorneyChange' },
  { label: 'Assigned Paralegal', entityType: 'contact', person: true, change: 'onParalegalChange' },
  {
    label: 'Assigned Outside Counsel',
    entityType: 'sprk_organization',
    person: false,
    change: 'onOutsideCounselChange',
  },
] as const;

const PICKED_ID = 'a1b2c3d4-0000-4000-8000-000000000001';

describe('AssignWorkFollowOnStep — side-pane lookups (with a navigationService)', () => {
  it('renders no inline search input for any lookup, only a Select button each', () => {
    renderStep(baseProps({ navigationService: createMockNavigationService() }));

    for (const { label } of LOOKUP_FIELDS) {
      expect(screen.getByRole('button', { name: `Select ${label}` })).toBeInTheDocument();
    }
    expect(screen.queryByPlaceholderText(/^Search /)).not.toBeInTheDocument();
  });

  it.each(LOOKUP_FIELDS)(
    '$label opens the pane for $entityType and passes the picked id and name unchanged',
    async ({ label, entityType, person, change }) => {
      const nav = createMockNavigationService();
      nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: `Picked ${label}`, entityType }]);
      const props = baseProps({ navigationService: nav });
      renderStep(props);

      fireEvent.click(screen.getByRole('button', { name: `Select ${label}` }));

      await waitFor(() => expect(props[change]).toHaveBeenCalledTimes(1));
      expect(props[change]).toHaveBeenCalledWith({ id: PICKED_ID, name: `Picked ${label}` });
      expect(nav.openLookup).toHaveBeenCalledTimes(1);
      const options = nav.openLookup.mock.calls[0][0];
      expect(options.entityType).toBe(entityType);
      if (person) {
        expect(options.filters).toEqual(ACTIVE_CONTACT_LOOKUP_FILTERS);
      } else {
        expect(options.filters).toBeUndefined();
      }
    }
  );

  it('the active-contacts filter is the shared constant: contact, statecode 0', () => {
    expect(ACTIVE_CONTACT_LOOKUP_FILTERS).toEqual([
      {
        entityLogicalName: 'contact',
        filterXml: '<filter><condition attribute="statecode" operator="eq" value="0"/></filter>',
      },
    ]);
  });

  it('cancelling the pane keeps the current value (no change call)', async () => {
    const nav = createMockNavigationService(); // openLookup resolves []
    const props = baseProps({ navigationService: nav });
    renderStep(props);

    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Attorney' }));

    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(1));
    expect(props.onAttorneyChange).not.toHaveBeenCalled();
  });

  it('shows a picked value as a chip and clearing it reports null', () => {
    const props = baseProps({
      navigationService: createMockNavigationService(),
      outsideCounselValue: { id: 'org-1', name: 'Jones & Smith PC' },
    });
    renderStep(props);

    expect(screen.getByText('Jones & Smith PC')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Clear Assigned Outside Counsel' }));
    expect(props.onOutsideCounselChange).toHaveBeenCalledWith(null);
  });

  it('"Assign to me" still fills Assigned Attorney', async () => {
    const onAttorneyChange = jest.fn();
    const onAssignAttorneyToMe = jest.fn().mockResolvedValue({ id: 'contact-guid-1', name: 'Jane Attorney' });
    renderStep(baseProps({ navigationService: createMockNavigationService(), onAttorneyChange, onAssignAttorneyToMe }));

    fireEvent.click(screen.getByRole('button', { name: 'Assign attorney to me' }));

    await waitFor(() => expect(onAttorneyChange).toHaveBeenCalledWith({ id: 'contact-guid-1', name: 'Jane Attorney' }));
  });
});

describe('AssignWorkFollowOnStep — no navigationService (unchanged inline fields)', () => {
  it('renders the inline search fields with their original placeholders and never offers a pane', () => {
    renderStep(baseProps());

    expect(screen.getByPlaceholderText('Search matter types...')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search practice areas...')).toBeInTheDocument();
    expect(screen.getAllByPlaceholderText('Search contacts...')).toHaveLength(2);
    expect(screen.getByPlaceholderText('Search organizations...')).toBeInTheDocument();
    for (const { label } of LOOKUP_FIELDS) {
      expect(screen.queryByRole('button', { name: `Select ${label}` })).not.toBeInTheDocument();
    }
  });
});
