/**
 * CreateEventStep — "Assign to me" + Assigned To field tests
 * (spaarkeai-assistant-enhancements-r1 task 014 / FR-A4)
 *
 * Scope:
 *   - "Assign to me" resolves the current user (via the shared
 *     `resolveCurrentUserAsContactAssignee` helper) and sets the Assigned To
 *     field — end-to-end through the component's local state / onFormValues.
 *   - The form is valid (and the create-flow can proceed) with NO assignee
 *     set — grounding-optional / P6 companion check at the field level.
 *   - Dark-mode render smoke test (ADR-021).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { CreateEventStep } from '../CreateEventStep';
import type { IDataService } from '../../../types/serviceInterfaces';

jest.mock('../../../services/userLookup', () => ({
  searchContactsAsLookup: jest.fn(async () => []),
  resolveCurrentUserAsContactAssignee: jest.fn(),
}));

import { resolveCurrentUserAsContactAssignee } from '../../../services/userLookup';

const mockDataService = {} as IDataService;

function renderStep(theme = webLightTheme) {
  const onValidChange = jest.fn();
  const onFormValues = jest.fn();
  render(
    <FluentProvider theme={theme}>
      <CreateEventStep dataService={mockDataService} onValidChange={onValidChange} onFormValues={onFormValues} />
    </FluentProvider>
  );
  return { onValidChange, onFormValues };
}

describe('CreateEventStep — Assigned To / Assign to me', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('renders the Assigned To field and the Assign to me button', () => {
    renderStep();
    expect(screen.getByLabelText('Assigned To')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Assign to me' })).toBeInTheDocument();
  });

  it('"Assign to me" resolves the current user and sets assignedToId/assignedToName', async () => {
    (resolveCurrentUserAsContactAssignee as jest.Mock).mockResolvedValue({
      id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      name: 'Jane Attorney',
    });

    const { onFormValues } = renderStep();

    fireEvent.click(screen.getByRole('button', { name: 'Assign to me' }));

    await waitFor(() => {
      const lastCall = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
      expect(lastCall.assignedToId).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
      expect(lastCall.assignedToName).toBe('Jane Attorney');
    });

    // The resolved contact chip is now displayed.
    expect(screen.getByText('Jane Attorney')).toBeInTheDocument();
  });

  it('"Assign to me" is a graceful no-op when the current user cannot be resolved (no Xrm host / no matching contact)', async () => {
    (resolveCurrentUserAsContactAssignee as jest.Mock).mockResolvedValue(null);

    const { onFormValues } = renderStep();
    const callsBefore = onFormValues.mock.calls.length;

    fireEvent.click(screen.getByRole('button', { name: 'Assign to me' }));

    await waitFor(() => {
      expect(resolveCurrentUserAsContactAssignee).toHaveBeenCalled();
    });

    // No new form-value emission carrying an assignee — field stays empty,
    // available for manual search. (onFormValues may still fire from the
    // effect but never with a populated assignedToId.)
    const laterCalls = onFormValues.mock.calls.slice(callsBefore);
    expect(laterCalls.every(([values]) => values.assignedToId === '')).toBe(true);
  });

  it('P6 grounding-optional companion: reports the step as valid with no assignee and no other optional fields set', () => {
    const { onValidChange } = renderStep();
    fireEvent.change(screen.getByPlaceholderText('Enter event name'), { target: { value: 'Call the client' } });
    expect(onValidChange).toHaveBeenLastCalledWith(true);
  });

  it('renders without error in dark theme (ADR-021)', () => {
    renderStep(webDarkTheme);
    expect(screen.getByLabelText('Assigned To')).toBeInTheDocument();
  });
});
