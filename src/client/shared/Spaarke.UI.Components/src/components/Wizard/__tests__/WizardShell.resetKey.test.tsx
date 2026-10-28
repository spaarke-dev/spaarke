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
    const marked = (s: string) =>
      step(s, { isSkippable: true, renderContent: () => <input data-testid={`field-${s}`} /> });
    const shell = (resetKey: number) =>
      props({ resetKey, showSkippedSteps: true, steps: [marked('a'), marked('b'), marked('c')] });
    // The stepper labels each step with its status ("Step a, skipped").
    const skippedSteps = () =>
      within(screen.getByRole('navigation', { name: 'Wizard steps' })).queryAllByLabelText(/, skipped$/);
    const { rerender } = renderWithProviders(<WizardShell {...shell(1)} />);
    fireEvent.change(screen.getByTestId('field-a'), { target: { value: 'typed' } });
    fireEvent.click(button('Skip'));
    expect(screen.getByTestId('field-b')).toBeInTheDocument();
    // The Skip landed: the stepper shows step a as skipped (a plain Next would show it completed).
    expect(skippedSteps()).toHaveLength(1);

    rerender(<WizardShell {...shell(2)} />);
    expect(skippedSteps()).toHaveLength(0);
    // Same step id, same content type: without the reset the typed value would still be there.
    expect((screen.getByTestId('field-a') as HTMLInputElement).value).toBe('');
  });

  it('a NaN resetKey does not reset on every render (NaN equals itself)', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ resetKey: NaN })} />);
    fireEvent.click(button('Next'));
    rerender(<WizardShell {...props({ resetKey: NaN, title: 'Renamed' })} />);
    expect(screen.getByTestId('content-b')).toBeInTheDocument();
  });

  it('after a reset, focus that was on removed content moves into the dialog, not <body>', () => {
    const withButton = (id: string) =>
      step(id, { renderContent: () => <button data-testid={`inner-${id}`}>inner {id}</button> });
    const { rerender } = renderWithProviders(
      <WizardShell {...props({ resetKey: 'one', steps: [withButton('a'), withButton('b')] })} />
    );
    screen.getByTestId('inner-a').focus();
    expect(surface()).toContainElement(document.activeElement as HTMLElement);
    rerender(<WizardShell {...props({ resetKey: 'two', steps: [withButton('x'), withButton('y')] })} />);
    expect(document.activeElement).not.toBe(document.body);
    expect(surface()).toContainElement(document.activeElement as HTMLElement);
  });

  it('after a reset, focus already inside the dialog is left where it is', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props({ resetKey: 'one' })} />);
    const next = button('Next');
    next.focus();
    rerender(<WizardShell {...props({ resetKey: 'two' })} />);
    expect(button('Next')).toHaveFocus();
  });

  it('after a reset, focus held OUTSIDE the dialog (a confirm or side pane opened above it) is left where it is', () => {
    // The decision wizard's discard confirm is a separate dialog: an item that finishes loading while it is open resets the shell.
    const Host = ({ resetKey }: { resetKey: string }) => (
      <>
        <WizardShell {...props({ resetKey })} />
        <div role="alertdialog" aria-label="Discard unrecorded choices?">
          <button data-testid="confirm-keep">Keep editing</button>
        </div>
      </>
    );
    const { rerender } = renderWithProviders(<Host resetKey="one" />);
    const keep = screen.getByTestId('confirm-keep');
    keep.focus();
    rerender(<Host resetKey="two" />);
    expect(keep).toHaveFocus();
  });

  it('must-not-fire: without resetKey the content area has no tabIndex, and nothing takes focus on a re-render', () => {
    const { rerender } = renderWithProviders(<WizardShell {...props()} />);
    const content = screen.getByTestId('content-a').parentElement as HTMLElement;
    expect(content).not.toHaveAttribute('tabindex');
    rerender(<WizardShell {...props({ title: 'Renamed' })} />);
    expect(content).not.toHaveAttribute('tabindex');
    expect(content).not.toHaveFocus();
  });

  it('with resetKey the content area is a programmatic focus target only (tabIndex -1)', () => {
    renderWithProviders(<WizardShell {...props({ resetKey: 'k' })} />);
    expect(screen.getByTestId('content-a').parentElement).toHaveAttribute('tabindex', '-1');
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
