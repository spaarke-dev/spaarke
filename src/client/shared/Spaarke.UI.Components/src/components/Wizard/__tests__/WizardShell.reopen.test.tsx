/**
 * WizardShell.reopen.test.tsx - a wizard reopened in the same session starts from its static steps (DI-155, #2814).
 * The follow-on steps picked in the last session are gone from the stepper; picking one again adds it once.
 */
import * as React from 'react';
import { act, fireEvent, screen, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import type { IWizardShellHandle, IWizardShellProps, IWizardStepConfig } from '../wizardShellTypes';

function step(id: string, overrides: Partial<IWizardStepConfig> = {}): IWizardStepConfig {
  return {
    id,
    label: `Step ${id}`,
    renderContent: () => <div data-testid={`content-${id}`}>content-{id}</div>,
    canAdvance: () => true,
    ...overrides,
  };
}

const staticSteps = [step('a'), step('b'), step('c')];

function props(overrides: Partial<IWizardShellProps> = {}): IWizardShellProps {
  return {
    open: true,
    title: 'Create',
    steps: staticSteps,
    onClose: jest.fn(),
    onFinish: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

const button = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}$`, 'i') });
const labels = () =>
  within(screen.getByRole('navigation', { name: 'Wizard steps' }))
    .getAllByRole('listitem')
    .map(li => (li.textContent ?? '').trim());

describe('WizardShell reopen', () => {
  it('a reopened wizard shows only its static steps; picking the follow-on again adds it once', () => {
    const ref = React.createRef<IWizardShellHandle>();
    const { rerender } = renderWithProviders(<WizardShell ref={ref} {...props()} />);

    act(() => ref.current!.addDynamicStep(step('follow', { label: 'Create Event' })));
    expect(labels()).toEqual(['Step a', 'Step b', 'Step c', 'Create Event']);
    fireEvent.click(button('Next'));

    rerender(<WizardShell ref={ref} {...props({ open: false })} />);
    rerender(<WizardShell ref={ref} {...props({ open: true })} />);

    expect(labels()).toEqual(['Step a', 'Step b', 'Step c']);
    expect(screen.getByTestId('content-a')).toBeInTheDocument();

    act(() => ref.current!.addDynamicStep(step('follow', { label: 'Create Event' })));
    act(() => ref.current!.addDynamicStep(step('follow', { label: 'Create Event' })));
    expect(labels()).toEqual(['Step a', 'Step b', 'Step c', 'Create Event']);
  });

  it('must-not-fire: a re-render while open keeps the follow-on step', () => {
    const ref = React.createRef<IWizardShellHandle>();
    const { rerender } = renderWithProviders(<WizardShell ref={ref} {...props()} />);
    act(() => ref.current!.addDynamicStep(step('follow', { label: 'Create Event' })));
    rerender(<WizardShell ref={ref} {...props({ title: 'Renamed' })} />);
    expect(labels()).toEqual(['Step a', 'Step b', 'Step c', 'Create Event']);
  });

  it('a reopened wizard still starts at its first step with skipped marks cleared (unchanged)', () => {
    const marked = (s: string) => step(s, { isSkippable: true });
    const steps = [marked('a'), marked('b'), marked('c')];
    const { rerender } = renderWithProviders(<WizardShell {...props({ steps, showSkippedSteps: true })} />);
    fireEvent.click(button('Skip'));
    rerender(<WizardShell {...props({ steps, showSkippedSteps: true, open: false })} />);
    rerender(<WizardShell {...props({ steps, showSkippedSteps: true, open: true })} />);
    expect(screen.getByTestId('content-a')).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Wizard steps' })).queryAllByLabelText(/, skipped$/)
    ).toHaveLength(0);
  });
});
