/**
 * EnterInfoStep — optional Assigned To contact picker (field-mapping-server-write-path-r1 task 038, FR-37).
 *
 * - The field is OPTIONAL: Enter Info validity keeps its rule (name, priority, response due date), so a blank
 *   Assigned To neither blocks nor changes it, and a chosen contact does not either.
 * - Blank reports `assignedToId: ''` / `assignedToName: ''` (the service then sends NO sprk_assignedto key).
 * - A picked contact reports its id and name. The search double is production-like: the real
 *   `searchContactsAsLookup` returns [] for fewer than 2 characters, so the contact is found by typing >= 2.
 * - The hint is wired to the input with aria-describedby (WCAG AA, via LookupField's `hint`).
 * - Seeded values (Back to Enter Info) show in the picker and the name field.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the validity contract of the step.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const CONTACT = { id: '51a5ddb0-40bf-f111-aaaf-0022482913fc', name: 'Chosen Contact' };

// Production-like: matterService.searchContacts returns [] for a filter shorter than 2 characters.
const mockSearchContacts = jest.fn(async (_ds: unknown, q: string) => (q.trim().length < 2 ? [] : [CONTACT]));
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

const assignedToInput = (): HTMLElement => screen.getByRole('textbox', { name: 'Assigned To' });

describe('EnterInfoStep — Assigned To (task 038, FR-37)', () => {
  beforeEach(() => mockSearchContacts.mockClear());

  it('shows the Assigned To picker, a blank value keeps Enter Info valid and reports no contact', () => {
    const { onValidChange, onFormValues, last } = mountStep();

    expect(assignedToInput()).toBeInTheDocument();
    expect(last<boolean>(onValidChange)).toBe(true);
    expect(last<Record<string, unknown>>(onFormValues)).toMatchObject({ assignedToId: '', assignedToName: '' });
  });

  it('links the hint to the input with aria-describedby', () => {
    mountStep();

    const hint = screen.getByText('Leave blank to use the Assigned To Internal value');
    expect(hint.id).not.toBe('');
    expect(assignedToInput().getAttribute('aria-describedby')).toContain(hint.id);
  });

  it('a contact found by typing >= 2 characters is reported with its id and name, validity unchanged', async () => {
    const { onValidChange, onFormValues, last } = mountStep();

    fireEvent.change(assignedToInput(), { target: { value: 'ch' } });
    await waitFor(() => expect(screen.getByText(CONTACT.name)).toBeInTheDocument());
    fireEvent.click(screen.getByText(CONTACT.name));

    await waitFor(() =>
      expect(last<Record<string, unknown>>(onFormValues)).toMatchObject({
        assignedToId: CONTACT.id,
        assignedToName: CONTACT.name,
      })
    );
    expect(last<boolean>(onValidChange)).toBe(true);
    // The picker only searches from 2 characters (the real search returns nothing below that).
    expect(mockSearchContacts.mock.calls.every(c => c[1].trim().length >= 2)).toBe(true);
  });

  it('typed-but-unpicked text reports no contact', async () => {
    const { onFormValues, last } = mountStep();

    fireEvent.change(assignedToInput(), { target: { value: 'ch' } });
    await waitFor(() => expect(screen.getByText(CONTACT.name)).toBeInTheDocument());

    expect(last<Record<string, unknown>>(onFormValues)).toMatchObject({ assignedToId: '', assignedToName: '' });
  });

  it('a seeded contact and name show in the step (the Back to Enter Info case)', () => {
    mountStep({ name: 'Typed name', assignedToId: CONTACT.id, assignedToName: CONTACT.name });

    expect(screen.getByText(CONTACT.name)).toBeInTheDocument();
    expect(screen.getByDisplayValue('Typed name')).toBeInTheDocument();
  });

  it('a missing required field still invalidates the step with Assigned To blank (rule unchanged)', () => {
    const { onValidChange } = mountStep({ responseDueDate: '' });
    expect(onValidChange.mock.calls[onValidChange.mock.calls.length - 1][0]).toBe(false);
  });
});
