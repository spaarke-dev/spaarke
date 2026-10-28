/**
 * WizardShell.resetKey.test.tsx - the additive `resetKey` prop (ontology task 165, UAT round 1 re-check).
 * A consumer that swaps what the wizard is about (the decision wizard moves between items) changes `resetKey` to rebuild the
 * step state IN PLACE: the dialog node stays mounted. Absent, nothing changes (the shipped wizards remount to start over).
 */
import * as React from 'react';
import { act, fireEvent, screen, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import { wizardShellReducer, buildInitialShellState } from '../wizardShellReducer';
import type { IWizardShellProps, IWizardStepConfig } from '../wizardShellTypes';

function step(id: string, overrides: Partial<IWizardStepConfig> = {}): IWizardStepConfig {
  return {
    id,
    label: `Step ${id}`,
    renderContent: () => <div data-testid={`content-${id}`}>content-{id}</div>,
    canAdvance: () => true,
    ...overrides,
  };
}

function props(overrides: Partial<IWizardShellProps> = {}): IWizardShellProps {
  return {
    open: true,
    title: 'Decide',
    steps: [step('a'), step('b'), step('c')],
    onClose: jest.fn(),
    onFinish: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

const button = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}$`, 'i') });
const surface = () => screen.getByRole('dialog');
const labels = () =>
  within(screen.getByRole('navigation', { name: 'Wizard steps' }))
    .getAllByRole('listitem')
    .map(li => (li.textContent ?? '').trim());

describe('WizardShell resetKey', () => {
  it('a changed resetKey rebuilds the step state from the current steps and initialStepId, on the SAME dialog node', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ resetKey: 'one' })} />);
    const dialog = surface();
    fireEvent.click(button('Next'));
    expect(screen.getByTestId('content-b')).toBeInTheDocument();

    rerender(<WizardShell {...props({ resetKey: 'two', steps: [step('x'), step('y')] })} />);
    expect(surface()).toBe(dialog);
    expect(screen.getByTestId('content-x')).toBeInTheDocument();
    expect(labels()).toEqual(['Step x', 'Step y']);

    // initialStepId is honoured on a reset (the decision wizard reopens a recorded item on its last step).
    rerender(<WizardShell {...props({ resetKey: 'three', steps: [step('p'), step('q')], initialStepId: 'q' })} />);
    expect(surface()).toBe(dialog);
    expect(screen.getByTestId('content-q')).toBeInTheDocument();
    expect(screen.queryByTestId('content-p')).toBeNull();
  });

  it('a reset also forgets skipped marks, and the old content does not survive it', () => {
    const marked = (s: string) => step(s, { renderContent: () => <input data-testid={`field-${s}`} /> });
    const { rerender } = renderWithProviders(
      <WizardShell {...props({ resetKey: 1, showSkippedSteps: true, steps: [marked('a'), marked('b')] })} />
    );
    fireEvent.change(screen.getByTestId('field-a'), { target: { value: 'typed' } });
    rerender(<WizardShell {...props({ resetKey: 2, showSkippedSteps: true, steps: [marked('a'), marked('b')] })} />);
    // Same step id, same content type: without the reset the typed value would still be there.
    expect((screen.getByTestId('field-a') as HTMLInputElement).value).toBe('');
  });

  it('absent resetKey: changing steps or initialStepId after mount still has no effect (unchanged behaviour)', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props()} />);
    fireEvent.click(button('Next'));
    rerender(<WizardShell {...props({ steps: [step('a'), step('b'), step('c')], initialStepId: 'c' })} />);
    expect(screen.getByTestId('content-b')).toBeInTheDocument();
  });

  it('an unchanged resetKey keeps the step the user is on across re-renders', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ resetKey: 'k' })} />);
    fireEvent.click(button('Next'));
    rerender(<WizardShell {...props({ resetKey: 'k', title: 'Renamed' })} />);
    expect(screen.getByTestId('content-b')).toBeInTheDocument();
  });

  it('closing (open=false) still removes the dialog, whatever resetKey is', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ resetKey: 'k' })} />);
    expect(screen.queryByRole('dialog')).not.toBeNull();
    rerender(<WizardShell {...props({ resetKey: 'k', open: false })} />);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('clears a finish error on reset', async () => {
    const onFinish = jest.fn().mockRejectedValue(new Error('Boom'));
    const { rerender } = renderWithProviders(
      <WizardShell {...props({ resetKey: 'a', steps: [step('only')], onFinish })} />
    );
    await act(async () => {
      fireEvent.click(button('Finish'));
    });
    expect(screen.getByText('Boom')).toBeInTheDocument();
    rerender(<WizardShell {...props({ resetKey: 'b', steps: [step('only')], onFinish })} />);
    expect(screen.queryByText('Boom')).toBeNull();
  });
});

describe('wizardShellReducer RESET', () => {
  it('rebuilds the state from the supplied steps and initialStepId', () => {
    const cfgs = [step('a'), step('b'), step('c')];
    const moved = wizardShellReducer(buildInitialShellState(cfgs), { type: 'NEXT_STEP' });
    const reset = wizardShellReducer(moved, { type: 'RESET', steps: [step('x'), step('y')], initialStepId: 'y' });
    expect(reset).toEqual(buildInitialShellState([step('x'), step('y')], 'y'));
    expect(reset.currentStepIndex).toBe(1);
  });
});
