/**
 * EnterInfoStep — optional Assigned To contact picker (field-mapping-server-write-path-r1 task 038, FR-37).
 *
 * - The field is OPTIONAL: Enter Info validity keeps its rule (name, priority, response due date), so a blank
 *   Assigned To neither blocks nor changes it, and a chosen contact does not either.
 * - Blank reports `assignedToId: ''` / `assignedToName: ''` (the service then sends NO sprk_assignedto key).
 * - A picked contact reports its id and name.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the validity contract of the step.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const CONTACT = { id: '51a5ddb0-40bf-f111-aaaf-0022482913fc', name: 'Chosen Contact' };

const mockSearchContacts = jest.fn(async (_ds: unknown, _q: string) => [CONTACT]);
jest.mock('../workAssignmentService', () => ({
  searchMatterTypes: jest.fn(async () => []),
  searchPracticeAreas: jest.fn(async () => []),
  searchContactsAsLookup: (ds: unknown, q: string) => mockSearchContacts(ds, q),
}));

import { EnterInfoStep } from '../EnterInfoStep';

function mountStep(initial: Record<string, unknown> = {}) {
  const onValidChange = jest.fn();
  const onFormValues = jest.fn();
  renderWithProviders(
    <EnterInfoStep
      dataService={{} as never}
      onValidChange={onValidChange}
      onFormValues={onFormValues}
      initialValues={{ name: 'WA-1', priority: 100000001, responseDueDate: '2026-12-01', ...initial }}
      hasInitialValues
      authenticatedFetch={jest.fn() as never}
      bffBaseUrl="https://bff.example"
    />
  );
  const last = <T,>(fn: jest.Mock): T => fn.mock.calls[fn.mock.calls.length - 1][0] as T;
  return { onValidChange, onFormValues, last };
}

describe('EnterInfoStep — Assigned To (task 038, FR-37)', () => {
  it('shows the Assigned To picker with the hint, and a blank value keeps Enter Info valid and reports no contact', () => {
    const { onValidChange, onFormValues, last } = mountStep();

    expect(screen.getByRole('button', { name: 'Browse Assigned To' })).toBeInTheDocument();
    expect(screen.getByText('Leave blank to use the Assigned To Internal value')).toBeInTheDocument();
    expect(last<boolean>(onValidChange)).toBe(true);
    expect(last<Record<string, unknown>>(onFormValues)).toMatchObject({ assignedToId: '', assignedToName: '' });
  });

  it('a chosen contact is reported with its id and name, and validity is unchanged', async () => {
    const { onValidChange, onFormValues, last } = mountStep();

    fireEvent.click(screen.getByRole('button', { name: 'Browse Assigned To' }));
    await waitFor(() => expect(screen.getByText(CONTACT.name)).toBeInTheDocument());
    fireEvent.click(screen.getByText(CONTACT.name));

    await waitFor(() =>
      expect(last<Record<string, unknown>>(onFormValues)).toMatchObject({
        assignedToId: CONTACT.id,
        assignedToName: CONTACT.name,
      })
    );
    expect(last<boolean>(onValidChange)).toBe(true);
  });

  it('a missing required field still invalidates the step with Assigned To blank (rule unchanged)', () => {
    const { onValidChange } = mountStep({ responseDueDate: '' });
    expect(onValidChange.mock.calls[onValidChange.mock.calls.length - 1][0]).toBe(false);
  });
});
