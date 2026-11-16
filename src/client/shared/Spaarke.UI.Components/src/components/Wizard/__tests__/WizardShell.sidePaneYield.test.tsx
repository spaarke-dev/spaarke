/**
 * WizardShell.sidePaneYield.test.tsx - spaarke-ontology-platform-r1 task 194.
 *
 * The opt-in `nonBlocking` + `yieldToSidePane` props reach SprkModal in modal mode (the SendEmailDialog contract), so a
 * host lookup pane in the same document opens above the wizard. Without them the wizard is the blocking modal it was;
 * embedded mode ignores them.
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import type { IWizardShellProps } from '../wizardShellTypes';

function props(overrides: Partial<IWizardShellProps> = {}): IWizardShellProps {
  return {
    open: true,
    title: 'Email documents',
    steps: [{ id: 'a', label: 'Step a', renderContent: () => <div>content-a</div>, canAdvance: () => true }],
    onClose: jest.fn(),
    onFinish: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

describe('WizardShell — opt-in yield to the host lookup side pane (task 194)', () => {
  it('without the opt-in the wizard is a blocking modal that never yields', () => {
    renderWithProviders(<WizardShell {...props()} />);
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).not.toHaveAttribute('inert');
    expect(dialog.style.filter).toBe('');
  });

  it('nonBlocking renders a non-modal wizard; yieldToSidePane dims it and makes it inert until cleared', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ nonBlocking: true })} />);
    const dialog = screen.getByRole('dialog');
    expect(dialog).not.toHaveAttribute('aria-modal', 'true');
    expect(dialog).not.toHaveAttribute('inert');

    rerender(<WizardShell {...props({ nonBlocking: true, yieldToSidePane: true })} />);
    expect(screen.getByRole('dialog')).toHaveAttribute('inert');
    expect(screen.getByRole('dialog').style.filter).toBe('brightness(0.75)');
    expect(screen.getByText('content-a')).toBeInTheDocument();

    rerender(<WizardShell {...props({ nonBlocking: true, yieldToSidePane: false })} />);
    expect(screen.getByRole('dialog')).not.toHaveAttribute('inert');
    expect(screen.getByRole('dialog').style.filter).toBe('');
  });

  it('embedded mode ignores the opt-in', () => {
    renderWithProviders(<WizardShell {...props({ embedded: true, nonBlocking: true, yieldToSidePane: true })} />);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.querySelector('[inert]')).toBeNull();
  });
});
