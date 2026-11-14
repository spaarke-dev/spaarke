/**
 * AssignWorkStep — side-pane lookups (ontology task 181, owner D-219).
 *
 * - With a navigationService, Assigned Attorney / Paralegal / Law Firm / Law Firm Attorney open the Dataverse
 *   lookup side pane for their table and render no inline search input.
 * - Contact fields pass the active-contacts filter; Law Firm Attorney (firm chosen) passes ONE filter element
 *   combining active + the firm; Law Firm passes none.
 * - No firm chosen: Law Firm Attorney behaves as before (inline field, search returns nothing, no pane).
 * - Changing the Law Firm clears the Law Firm Attorney.
 * - Without a navigationService every field renders the inline search, as before.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the pane contract of the step.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { INavigationService } from '../../../types/serviceInterfaces';
import { ACTIVE_CONTACT_CONDITION_XML, ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';

const mockSearchByOrg = jest.fn(async () => []);
jest.mock('../workAssignmentService', () => ({
  searchContactsAsLookup: jest.fn(async () => []),
  searchOrganizationsAsLookup: jest.fn(async () => []),
  WorkAssignmentService: class {
    searchContactsByOrganization = (...args: unknown[]) => (mockSearchByOrg as (...a: unknown[]) => unknown)(...args);
  },
}));

import { AssignWorkStep, buildLawFirmAttorneyFilters } from '../AssignWorkStep';
import type { IAssignWorkState } from '../formTypes';

const FIRM_GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const FIRM_B_GUID = '11111111-2222-3333-4444-555555555555';
const ATTORNEY_GUID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

function mountStep(openLookup: jest.Mock | null) {
  const onFormValues = jest.fn();
  const navigationService = openLookup ? ({ openLookup } as unknown as INavigationService) : undefined;
  renderWithProviders(
    <AssignWorkStep
      dataService={{} as never}
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
      onFormValues={onFormValues}
      navigationService={navigationService}
    />
  );
  const last = (): IAssignWorkState => onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
  return { onFormValues, last };
}

const selectButton = (label: string): HTMLElement => screen.getByRole('button', { name: `Select ${label}` });
const pick = (id: string, name: string, entityType: string) => ({ id, name, entityType });

describe('AssignWorkStep — side pane lookups (task 181)', () => {
  beforeEach(() => mockSearchByOrg.mockClear());

  it('renders no inline search input and opens the pane for each field table', async () => {
    const openLookup = jest.fn(async () => []);
    mountStep(openLookup);

    expect(screen.queryAllByRole('textbox').length).toBe(1); // only Law Firm Attorney (no firm yet)
    expect(screen.getByRole('textbox', { name: 'Law Firm Attorney' })).toBeInTheDocument();

    fireEvent.click(selectButton('Assigned Attorney'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });

    fireEvent.click(selectButton('Assigned Paralegal'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'contact', filters: ACTIVE_CONTACT_LOOKUP_FILTERS });

    fireEvent.click(selectButton('Law Firm'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(3));
    // Organizations are not limited to active contacts: no filters key at all.
    expect(openLookup).toHaveBeenLastCalledWith({ entityType: 'sprk_organization' });
  });

  it('a picked attorney and paralegal reach the form values', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([pick('{AAAA1111-0000-0000-0000-000000000001}', 'Anna Attorney', 'contact')])
      .mockResolvedValueOnce([pick('bbbb2222-0000-0000-0000-000000000002', 'Paul Paralegal', 'contact')]);
    const { last } = mountStep(openLookup);

    fireEvent.click(selectButton('Assigned Attorney'));
    await waitFor(() => expect(last().assignedAttorneyName).toBe('Anna Attorney'));
    expect(last().assignedAttorneyId).toBe('aaaa1111-0000-0000-0000-000000000001');

    fireEvent.click(selectButton('Assigned Paralegal'));
    await waitFor(() => expect(last().assignedParalegalName).toBe('Paul Paralegal'));
  });

  it('with no firm chosen the Law Firm Attorney is the inline field: nothing to pick, no pane, no search', async () => {
    const openLookup = jest.fn(async () => []);
    mountStep(openLookup);

    const input = screen.getByRole('textbox', { name: 'Law Firm Attorney' });
    expect(input).toHaveAttribute('placeholder', 'Select a law firm first');
    expect(screen.queryByRole('button', { name: 'Select Law Firm Attorney' })).not.toBeInTheDocument();

    fireEvent.change(input, { target: { value: 'smi' } });
    await new Promise(r => setTimeout(r, 400));
    expect(mockSearchByOrg).not.toHaveBeenCalled();
    expect(openLookup).not.toHaveBeenCalled();
  });

  it('with a firm chosen the Law Firm Attorney pane gets ONE filter: active contacts of that firm', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([pick(`{${FIRM_GUID.toUpperCase()}}`, 'Firm A', 'sprk_organization')])
      .mockResolvedValueOnce([pick(ATTORNEY_GUID, 'Fiona Firmattorney', 'contact')]);
    const { last } = mountStep(openLookup);

    fireEvent.click(selectButton('Law Firm'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Select Law Firm Attorney' })).toBeInTheDocument());
    expect(screen.queryByRole('textbox', { name: 'Law Firm Attorney' })).not.toBeInTheDocument();

    fireEvent.click(selectButton('Law Firm Attorney'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));

    const options = openLookup.mock.calls[1][0];
    expect(options.entityType).toBe('contact');
    expect(options.filters).toHaveLength(1);
    expect(options.filters[0].entityLogicalName).toBe('contact');
    // Cleaned GUID (no braces, lower case), active condition and firm condition in the same filter element.
    expect(options.filters[0].filterXml).toBe(
      `<filter type="and">${ACTIVE_CONTACT_CONDITION_XML}` +
        `<condition attribute="parentcustomerid" operator="eq" value="${FIRM_GUID}"/></filter>`
    );

    await waitFor(() => expect(last().assignedLawFirmAttorneyName).toBe('Fiona Firmattorney'));
    expect(last().assignedLawFirmAttorneyId).toBe(ATTORNEY_GUID);
  });

  it('changing the Law Firm clears a Law Firm Attorney who no longer matches', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([pick(FIRM_GUID, 'Firm A', 'sprk_organization')])
      .mockResolvedValueOnce([pick(ATTORNEY_GUID, 'Fiona Firmattorney', 'contact')])
      .mockResolvedValueOnce([pick(FIRM_B_GUID, 'Firm B', 'sprk_organization')]);
    const { last } = mountStep(openLookup);

    fireEvent.click(selectButton('Law Firm'));
    await waitFor(() => screen.getByRole('button', { name: 'Select Law Firm Attorney' }));
    fireEvent.click(selectButton('Law Firm Attorney'));
    await waitFor(() => expect(last().assignedLawFirmAttorneyId).toBe(ATTORNEY_GUID));

    // Clear the firm, then choose another one.
    fireEvent.click(screen.getByRole('button', { name: 'Clear Law Firm' }));
    await waitFor(() => expect(last().assignedLawFirmId).toBe(''));
    expect(last().assignedLawFirmAttorneyId).toBe('');
    expect(last().assignedLawFirmAttorneyName).toBe('');

    fireEvent.click(selectButton('Law Firm'));
    await waitFor(() => expect(last().assignedLawFirmId).toBe(FIRM_B_GUID));
    expect(last().assignedLawFirmAttorneyId).toBe('');
  });

  it('a cancelled pane keeps the prior value', async () => {
    const openLookup = jest
      .fn()
      .mockResolvedValueOnce([pick(FIRM_GUID, 'Firm A', 'sprk_organization')])
      .mockResolvedValueOnce([]);
    const { last } = mountStep(openLookup);

    fireEvent.click(selectButton('Law Firm'));
    await waitFor(() => expect(last().assignedLawFirmId).toBe(FIRM_GUID));
    fireEvent.click(selectButton('Law Firm Attorney'));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));
    expect(last().assignedLawFirmId).toBe(FIRM_GUID);
    expect(last().assignedLawFirmAttorneyId).toBe('');
  });

  it('without a navigationService every field renders the inline search, as before', () => {
    mountStep(null);

    for (const label of ['Assigned Attorney', 'Assigned Paralegal', 'Law Firm', 'Law Firm Attorney']) {
      expect(screen.getByRole('textbox', { name: label })).toBeInTheDocument();
    }
    expect(screen.queryByRole('button', { name: /^Select / })).not.toBeInTheDocument();
  });
});

describe('buildLawFirmAttorneyFilters', () => {
  it('escapes the firm id for an XML attribute and cleans braces/case', () => {
    const [filter] = buildLawFirmAttorneyFilters('{ABC"><x a=\'&}');
    expect(filter.filterXml).toContain('value="abc&quot;&gt;&lt;x a=&apos;&amp;"');
    expect(filter.filterXml).not.toContain('"><x');
  });
});
