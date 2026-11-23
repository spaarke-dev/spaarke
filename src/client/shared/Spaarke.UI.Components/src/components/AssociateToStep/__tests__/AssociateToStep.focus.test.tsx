/**
 * AssociateToStep.focus.test.tsx - task 200.
 *
 * After the side-pane lookup behind "Select Record" settles (pick, cancel or failure), keyboard focus goes back to the
 * Select Record button: the platform pane leaves focus on <body> or on the link behind the dialog, and the button is
 * disabled while the pane is open. Focus placed inside the host dialog on purpose is left alone.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import { AssociateToStep } from '../AssociateToStep';

const TARGETS = [{ label: 'Matter', entityType: 'sprk_matter' }];
const selectRecord = (): HTMLElement => screen.getByTestId('associate-to-step-select-record-button');

function renderStep(openLookup: jest.Mock) {
  const navigationService = createMockNavigationService();
  navigationService.openLookup = openLookup as unknown as typeof navigationService.openLookup;
  return renderWithProviders(
    <div role="dialog" aria-label="Associate">
      <input aria-label="Other field in the dialog" />
      <AssociateToStep entityTypes={TARGETS} navigationService={navigationService} value={null} onChange={jest.fn()} />
    </div>
  );
}

describe('AssociateToStep: focus returns to Select Record (task 200)', () => {
  let outside: HTMLButtonElement;
  let errorSpy: jest.SpyInstance;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
    errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });
  afterEach(() => {
    outside.remove();
    errorSpy.mockRestore();
  });

  it('pick: focus ends on the Select Record button', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [{ id: 'abc', name: 'MAT-1', entityType: 'sprk_matter' }];
    });
    renderStep(openLookup);

    fireEvent.click(selectRecord());

    await waitFor(() => expect(openLookup).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(selectRecord()));
  });

  it('cancel and failure: focus ends on the Select Record button', async () => {
    const openLookup = jest
      .fn()
      .mockImplementationOnce(async () => {
        outside.focus();
        return [];
      })
      .mockImplementationOnce(async () => {
        outside.focus();
        throw new Error('no Xrm host');
      });
    renderStep(openLookup);

    fireEvent.click(selectRecord());
    await waitFor(() => expect(document.activeElement).toBe(selectRecord()));

    outside.focus();
    fireEvent.click(selectRecord());
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(document.activeElement).toBe(selectRecord()));
  });

  it('does not move focus that is inside the host dialog on another control', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      screen.getByRole('textbox', { name: 'Other field in the dialog' }).focus();
      return [];
    });
    renderStep(openLookup);

    fireEvent.click(selectRecord());

    await waitFor(() => expect(openLookup).toHaveBeenCalled());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Other field in the dialog' }));
  });
});
