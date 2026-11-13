/**
 * CreateReportCardStep — the eight resource lookups open the side pane (ontology task 180, F-R4-2, owner D-219).
 *
 * With a `navigationService` each of Attorney 1-2, Paralegal 1-2, Law Firm 1-2, External and Internal opens the pane for
 * its own table (the six contact fields limited to active contacts), renders no inline search input, and writes the
 * pick to the same id/name fields the inline search wrote. Without a `navigationService` the step is unchanged.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { CreateReportCardStep } from '../CreateReportCardStep';
import type { ICreateReportCardFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';

const FIELDS: ReadonlyArray<{
  label: string;
  entityType: 'contact' | 'sprk_organization';
  id: keyof ICreateReportCardFormState;
  name: keyof ICreateReportCardFormState;
}> = [
  { label: 'Assigned Attorney 1', entityType: 'contact', id: 'assignedAttorney1Id', name: 'assignedAttorney1Name' },
  { label: 'Assigned Attorney 2', entityType: 'contact', id: 'assignedAttorney2Id', name: 'assignedAttorney2Name' },
  { label: 'Assigned Paralegal 1', entityType: 'contact', id: 'assignedParalegal1Id', name: 'assignedParalegal1Name' },
  { label: 'Assigned Paralegal 2', entityType: 'contact', id: 'assignedParalegal2Id', name: 'assignedParalegal2Name' },
  {
    label: 'Assigned Law Firm 1',
    entityType: 'sprk_organization',
    id: 'assignedToLawFirm1Id',
    name: 'assignedToLawFirm1Name',
  },
  {
    label: 'Assigned Law Firm 2',
    entityType: 'sprk_organization',
    id: 'assignedLawFirm2Id',
    name: 'assignedLawFirm2Name',
  },
  { label: 'Assigned External', entityType: 'contact', id: 'assignedToExternalId', name: 'assignedToExternalName' },
  { label: 'Assigned Internal', entityType: 'contact', id: 'assignedToInternalId', name: 'assignedToInternalName' },
];

const PICKED_ID = 'a1b2c3d4-0000-4000-8000-000000000001';

function renderStep(navigationService?: ReturnType<typeof createMockNavigationService>) {
  const onFormValues = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <CreateReportCardStep
        dataService={{} as IDataService}
        navigationService={navigationService}
        onValidChange={jest.fn()}
        onFormValues={onFormValues}
      />
    </FluentProvider>
  );
  const lastValues = (): ICreateReportCardFormState => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
  return { lastValues };
}

describe('CreateReportCardStep — side-pane lookups (with a navigationService)', () => {
  it('renders a Select button for each of the eight fields and no inline search input', () => {
    renderStep(createMockNavigationService());

    for (const { label } of FIELDS) {
      expect(screen.getByRole('button', { name: `Select ${label}` })).toBeInTheDocument();
    }
    expect(screen.queryByPlaceholderText(/^Search /)).not.toBeInTheDocument();
  });

  it.each(FIELDS)('$label opens the $entityType pane and writes the pick unchanged', async field => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValue([{ id: PICKED_ID, name: `Picked ${field.label}`, entityType: field.entityType }]);
    const { lastValues } = renderStep(nav);

    fireEvent.click(screen.getByRole('button', { name: `Select ${field.label}` }));

    await waitFor(() => expect(lastValues()[field.id]).toBe(PICKED_ID));
    expect(lastValues()[field.name]).toBe(`Picked ${field.label}`);
    expect(nav.openLookup).toHaveBeenCalledTimes(1);
    const options = nav.openLookup.mock.calls[0][0];
    expect(options.entityType).toBe(field.entityType);
    if (field.entityType === 'contact') {
      expect(options.filters).toEqual(ACTIVE_CONTACT_LOOKUP_FILTERS);
    } else {
      expect(options.filters).toBeUndefined();
    }
    // Only the clicked field changed.
    for (const other of FIELDS.filter(f => f.id !== field.id)) {
      expect(lastValues()[other.id]).toBe('');
    }
  });

  it('cancelling a pane keeps the name typed and the other picks', async () => {
    const nav = createMockNavigationService();
    nav.openLookup.mockResolvedValueOnce([{ id: PICKED_ID, name: 'Alex Contact', entityType: 'contact' }]);
    const { lastValues } = renderStep(nav);
    fireEvent.change(screen.getByPlaceholderText('Enter report card name'), { target: { value: 'Q3 report' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Attorney 1' }));
    await waitFor(() => expect(lastValues().assignedAttorney1Id).toBe(PICKED_ID));

    nav.openLookup.mockResolvedValueOnce([]); // cancel
    fireEvent.click(screen.getByRole('button', { name: 'Select Assigned Law Firm 1' }));
    await waitFor(() => expect(nav.openLookup).toHaveBeenCalledTimes(2));

    expect(lastValues()).toMatchObject({
      name: 'Q3 report',
      assignedAttorney1Id: PICKED_ID,
      assignedToLawFirm1Id: '',
    });
  });
});

describe('CreateReportCardStep — no navigationService (unchanged inline fields)', () => {
  it('renders the inline search fields with their original placeholders and no Select button', () => {
    renderStep();

    expect(screen.getAllByPlaceholderText('Search contacts...')).toHaveLength(6);
    expect(screen.getAllByPlaceholderText('Search organizations...')).toHaveLength(2);
    for (const { label } of FIELDS) {
      expect(screen.queryByRole('button', { name: `Select ${label}` })).not.toBeInTheDocument();
    }
  });
});
