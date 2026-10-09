/**
 * WizardShell.characterization.test.tsx — locks WizardShell's existing behaviour BEFORE its
 * non-embedded branch is re-based onto SprkModal (ontology-platform-r1 task 056, P1; D-26).
 *
 * Every behaviour test runs in BOTH modes (`embedded` and the modal envelope), so the same
 * unmodified tests prove the envelope swap changes nothing a consumer relies on. The embedded
 * snapshot proves the embedded markup does not move. Intended changes (Escape / backdrop no
 * longer close, the title's element) are deliberately NOT asserted here.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import type { IWizardShellHandle, IWizardShellProps, IWizardStepConfig } from '../wizardShellTypes';

function step(id: string, overrides: Partial<IWizardStepConfig> = {}): IWizardStepConfig {
  return {
    id,
    label: `Step ${id}`,
    renderContent: () => <div>content-{id}</div>,
    canAdvance: () => true,
    ...overrides,
  };
}

function props(overrides: Partial<IWizardShellProps> = {}): IWizardShellProps {
  return {
    open: true,
    title: 'Test Wizard',
    steps: [step('a'), step('b'), step('c')],
    onClose: jest.fn(),
    onFinish: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

const button = (name: string | RegExp) =>
  screen.getByRole('button', { name: typeof name === 'string' ? new RegExp(`^${name}$`, 'i') : name });
const queryButton = (name: string) => screen.queryByRole('button', { name: new RegExp(`^${name}$`, 'i') });

describe.each([
  ['embedded', true],
  ['modal', false],
])('WizardShell characterization (%s)', (_mode, embedded) => {
  const renderShell = (p: Partial<IWizardShellProps> = {}) =>
    renderWithProviders(<WizardShell {...props({ embedded, ...p })} />);

  it('renders the title, every step label in the stepper, and the first step content', () => {
    renderShell();
    expect(screen.getByText('Test Wizard')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Wizard steps' })).toBeInTheDocument();
    ['Step a', 'Step b', 'Step c'].forEach(l => expect(screen.getByText(l)).toBeInTheDocument());
    expect(screen.getByText('content-a')).toBeInTheDocument();
    expect(screen.getByText('Step a').closest('li')).toHaveAttribute('aria-current', 'step');
  });

  it('hides Back on the first step and shows Cancel + Next', () => {
    renderShell();
    expect(queryButton('Back')).toBeNull();
    expect(button('Cancel')).toBeInTheDocument();
    expect(button('Next')).toBeInTheDocument();
  });

  it('gates Next on canAdvance()', () => {
    renderShell({ steps: [step('a', { canAdvance: () => false }), step('b')] });
    expect(button('Next')).toBeDisabled();
  });

  it('Next advances, Back returns, and the stepper ticks the left step', () => {
    renderShell();
    fireEvent.click(button('Next'));
    expect(screen.getByText('content-b')).toBeInTheDocument();
    expect(screen.getByLabelText('Step a, completed')).toBeInTheDocument();
    expect(screen.getByLabelText('Step b, active')).toBeInTheDocument();
    fireEvent.click(button('Back'));
    expect(screen.getByText('content-a')).toBeInTheDocument();
    expect(screen.getByLabelText('Step b, pending')).toBeInTheDocument();
  });

  it('shows Skip only on a skippable, non-last step; Skip advances even when canAdvance() is false', () => {
    renderShell({
      steps: [step('a', { isSkippable: true, canAdvance: () => false }), step('b', { isSkippable: true })],
    });
    expect(button('Next')).toBeDisabled();
    fireEvent.click(button('Skip'));
    expect(screen.getByText('content-b')).toBeInTheDocument();
    // On the last step Skip is hidden even though the step is skippable.
    expect(queryButton('Skip')).toBeNull();
  });

  it('renders per-step footerActions and footerLeftExtra', () => {
    renderShell({
      steps: [step('a', { footerActions: <button>Preview</button> }), step('b')],
      footerLeftExtra: <button>Delete</button>,
    });
    expect(button('Preview')).toBeInTheDocument();
    expect(button('Delete')).toBeInTheDocument();
  });

  it('the primary button reads finishLabel on the last step', () => {
    renderShell({ steps: [step('a')], finishLabel: 'Create' });
    expect(button('Create')).toBeInTheDocument();
    expect(queryButton('Next')).toBeNull();
  });

  it('early finish: isEarlyFinish() turns Next into Finish and calls onFinish', async () => {
    const onFinish = jest.fn().mockResolvedValue(undefined);
    renderShell({ steps: [step('a', { isEarlyFinish: () => true }), step('b')], onFinish });
    fireEvent.click(button('Finish'));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));
  });

  it('finish → close: onFinish resolving nothing calls onClose', async () => {
    const onClose = jest.fn();
    renderShell({ steps: [step('a')], onClose });
    fireEvent.click(button('Finish'));
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('finish → busy: shows finishingLabel and disables Cancel while onFinish is pending', async () => {
    let resolve!: () => void;
    const onFinish = jest.fn(() => new Promise<void>(r => (resolve = r)));
    renderShell({ steps: [step('a')], onFinish, finishingLabel: 'Working' });
    fireEvent.click(button('Finish'));
    expect(await screen.findAllByText('Working')).not.toHaveLength(0);
    expect(button('Cancel')).toBeDisabled();
    await act(async () => resolve());
  });

  it('finish → error: a rejected onFinish shows the message and keeps the wizard open', async () => {
    const onClose = jest.fn();
    const onFinish = jest.fn().mockRejectedValue(new Error('Save failed'));
    renderShell({ steps: [step('a')], onFinish, onClose });
    fireEvent.click(button('Finish'));
    expect(await screen.findByRole('alert')).toHaveTextContent('Save failed');
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText('content-a')).toBeInTheDocument();
  });

  it('finish → success: a returned config replaces the content and the footer shows only its actions', async () => {
    const onFinish = jest.fn().mockResolvedValue({
      icon: <span>icon</span>,
      title: 'All done',
      body: 'Created 1 record',
      actions: <button>Done</button>,
    });
    renderShell({ steps: [step('a')], onFinish });
    fireEvent.click(button('Finish'));
    expect(await screen.findByText('All done')).toBeInTheDocument();
    expect(screen.getByText('Created 1 record')).toBeInTheDocument();
    expect(button('Done')).toBeInTheDocument();
    expect(screen.queryByText('content-a')).toBeNull();
    expect(queryButton('Cancel')).toBeNull();
  });

  it('Cancel and the close (×) button call onClose', () => {
    const onClose = jest.fn();
    renderShell({ onClose });
    fireEvent.click(button('Cancel'));
    fireEvent.click(button('Close'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('initialStepId opens on that step', () => {
    renderShell({ initialStepId: 'c' });
    expect(screen.getByText('content-c')).toBeInTheDocument();
    expect(screen.getByLabelText('Step b, completed')).toBeInTheDocument();
  });

  it('the imperative handle adds and removes dynamic steps (canonicalOrder honoured)', () => {
    const ref = React.createRef<IWizardShellHandle>();
    renderWithProviders(
      <WizardShell ref={ref} {...props({ embedded, steps: [step('a'), step('confirm')] })} />
    );
    act(() => ref.current!.addDynamicStep(step('y'), ['x', 'y', 'confirm']));
    act(() => ref.current!.addDynamicStep(step('x'), ['x', 'y', 'confirm']));
    expect(ref.current!.state.steps.map(s => s.id)).toEqual(['a', 'x', 'y', 'confirm']);
    expect(screen.getByText('Step x')).toBeInTheDocument();
    act(() => ref.current!.removeDynamicStep('x'));
    expect(screen.queryByText('Step x')).toBeNull();
  });

  it('re-opening resets to the first step', () => {
    const { rerender } = renderShell();
    fireEvent.click(button('Next'));
    expect(screen.getByText('content-b')).toBeInTheDocument();
    rerender(<WizardShell {...props({ embedded, open: false })} />);
    rerender(<WizardShell {...props({ embedded, open: true })} />);
    expect(screen.getByText('content-a')).toBeInTheDocument();
  });

  it('renders nothing when closed', () => {
    renderShell({ open: false });
    expect(screen.queryByText('content-a')).toBeNull();
  });
});

describe('WizardShell characterization — modal envelope', () => {
  it('renders inside a dialog named by the title', () => {
    renderWithProviders(<WizardShell {...props()} />);
    expect(screen.getByRole('dialog', { name: 'Test Wizard' })).toBeInTheDocument();
  });

  it('offers maximize; embedded mode does not', () => {
    const { unmount } = renderWithProviders(<WizardShell {...props()} />);
    expect(screen.getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
    unmount();
    renderWithProviders(<WizardShell {...props({ embedded: true })} />);
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
  });
});

describe('WizardShell characterization — embedded markup', () => {
  // Plain render (no FluentProvider) so no provider-generated ids enter the snapshot.
  it('embedded markup with the title bar is unchanged', () => {
    const { container } = render(
      <WizardShell
        {...props({ embedded: true, initialStepId: 'b', footerLeftExtra: <span>extra</span> })}
        steps={[step('a'), step('b', { isSkippable: true, footerActions: <span>step-action</span> }), step('c')]}
      />
    );
    expect(container.firstChild).toMatchSnapshot();
  });

  it('embedded markup under platform chrome (hideTitle) is unchanged', () => {
    const { container } = render(<WizardShell {...props({ embedded: true, hideTitle: true, ariaLabel: 'Aria' })} />);
    expect(container.firstChild).toMatchSnapshot();
  });
});
