/**
 * WizardShell.sprkModal.test.tsx — the SprkModal re-base and the additive props the decision wizard
 * needs (ontology-platform-r1 task 056, P2; D-26; ADR-050 as amended 2026-10-07; modal note §7):
 * one envelope, dismiss (default explicit), size + deprecated maxWidth/height, uiScale, nav +
 * onBeforeNavigate, statusBar, footer override, stayOpenOnFinish, and the 'skipped' step status.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import { buildInitialShellState, wizardShellReducer } from '../wizardShellReducer';
import type { IWizardShellProps, IWizardStepConfig } from '../wizardShellTypes';

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
    title: 'Decide',
    steps: [step('a'), step('b'), step('c')],
    onClose: jest.fn(),
    onFinish: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

const button = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}$`, 'i') });
const surface = () => screen.getByRole('dialog');

describe('WizardShell — rendered inside SprkModal (one envelope)', () => {
  it('the only Dialog/DialogSurface in the tree is SprkModal’s, named by its header title span', () => {
    renderWithProviders(<WizardShell {...props()} />);
    expect(document.querySelectorAll('.fui-DialogSurface')).toHaveLength(1);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    const labelledBy = surface().getAttribute('aria-labelledby');
    expect(labelledBy).toMatch(/^sprk-modal-title-/);
    expect(document.getElementById(labelledBy!)).toHaveTextContent('Decide');
    // Cancel left (footerStart), Skip · Back · Next right (footer) — rendered by SprkModal's footer.
    expect(within(surface()).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
    expect(within(surface()).getByRole('button', { name: /^next$/i })).toBeInTheDocument();
  });

  it('embedded mode renders no dialog at all', () => {
    renderWithProviders(<WizardShell {...props({ embedded: true })} />);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.querySelectorAll('.fui-DialogSurface')).toHaveLength(0);
  });

  it('follows a dark theme (renders under webDarkTheme)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <WizardShell {...props()} />
      </FluentProvider>
    );
    expect(screen.getByText('content-a')).toBeInTheDocument();
  });
});

describe('WizardShell — dismiss', () => {
  it('defaults to explicit: Escape does not close; × and Cancel do', () => {
    const onClose = jest.fn();
    renderWithProviders(<WizardShell {...props({ onClose })} />);
    fireEvent.keyDown(surface(), { key: 'Escape', code: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.click(button('Close'));
    fireEvent.click(button('Cancel'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("dismiss='light' lets Escape close", () => {
    const onClose = jest.fn();
    renderWithProviders(<WizardShell {...props({ onClose, dismiss: 'light' })} />);
    fireEvent.keyDown(surface(), { key: 'Escape', code: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('WizardShell — size, deprecated maxWidth/height, uiScale', () => {
  it("defaults to the named 'wizard' size", () => {
    renderWithProviders(<WizardShell {...props()} />);
    expect(surface().style.width).toBe('62vw');
    expect(surface().style.height).toBe('min(74vh, 760px)');
  });

  it('honours a named size and scales it by uiScale', () => {
    renderWithProviders(<WizardShell {...props({ size: 'lg', uiScale: 1.5 })} />);
    expect(surface().style.width).toBe('min(1920px, 94vw)');
    expect(surface().style.height).toBe('min(85vh, 1320px)');
  });

  it('still honours the deprecated maxWidth/height strings', () => {
    renderWithProviders(<WizardShell {...props({ maxWidth: '1280px', height: '85vh' })} />);
    expect(surface().style.width).toBe('1280px');
    expect(surface().style.height).toBe('85vh');
    expect(surface().style.minHeight).toBe('85vh');
  });

  it('maximize ignores the deprecated strings and goes full', () => {
    renderWithProviders(<WizardShell {...props({ maxWidth: '1280px', height: '85vh' })} />);
    fireEvent.click(screen.getByRole('button', { name: /maximize dialog/i }));
    expect(surface().style.width).toBe('100vw');
    expect(surface().style.height).toBe('100vh');
  });
});

describe('WizardShell — nav (‹ N of M ›) + onBeforeNavigate', () => {
  it('renders the counter in the header and navigates', () => {
    const onNavigate = jest.fn();
    renderWithProviders(<WizardShell {...props({ nav: { index: 1, total: 5, onNavigate } })} />);
    expect(within(surface()).getByText('2 of 5')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /next record/i }));
    expect(onNavigate).toHaveBeenCalledWith('next');
  });

  it('a guard returning false blocks the move; the wizard keeps its step', async () => {
    const onNavigate = jest.fn();
    const onBeforeNavigate = jest.fn().mockResolvedValue(false);
    renderWithProviders(<WizardShell {...props({ nav: { index: 1, total: 5, onNavigate, onBeforeNavigate } })} />);
    fireEvent.click(button('Next'));
    fireEvent.click(screen.getByRole('button', { name: /previous record/i }));
    await waitFor(() => expect(onBeforeNavigate).toHaveBeenCalledWith('prev'));
    await act(async () => Promise.resolve());
    expect(onNavigate).not.toHaveBeenCalled();
    expect(screen.getByText('content-b')).toBeInTheDocument();
  });

  it('a guard resolving true allows the move', async () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <WizardShell
        {...props({ nav: { index: 1, total: 5, onNavigate, onBeforeNavigate: () => Promise.resolve(true) } })}
      />
    );
    fireEvent.click(screen.getByRole('button', { name: /previous record/i }));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('prev'));
  });
});

describe.each([
  ['embedded', true],
  ['modal', false],
])('WizardShell — statusBar, footer override, stayOpenOnFinish (%s)', (_mode, embedded) => {
  it('statusBar renders above the step content on every step', () => {
    renderWithProviders(<WizardShell {...props({ embedded, statusBar: <div role="status">Decided</div> })} />);
    const bar = screen.getByRole('status');
    expect(bar).toHaveTextContent('Decided');
    expect(bar.compareDocumentPosition(screen.getByText('content-a')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    fireEvent.click(button('Next'));
    expect(screen.getByRole('status')).toHaveTextContent('Decided');
  });

  it('a footer override replaces the side it supplies and keeps the other; its callbacks drive the shell', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <WizardShell
        {...props({
          embedded,
          onClose,
          initialStepId: 'b',
          footer: ctx => ({
            end: (
              <>
                <button onClick={ctx.goBack} disabled={ctx.isFirstStep}>
                  Prev step
                </button>
                <button onClick={ctx.goNext}>Next open item</button>
                <button onClick={ctx.close}>Done</button>
              </>
            ),
          }),
        })}
      />
    );
    // Standard right side gone; standard left side (Cancel) kept.
    expect(screen.queryByRole('button', { name: /^next$/i })).toBeNull();
    expect(button('Cancel')).toBeInTheDocument();
    fireEvent.click(button('Prev step'));
    expect(screen.getByText('content-a')).toBeInTheDocument();
    fireEvent.click(button('Next open item'));
    expect(screen.getByText('content-b')).toBeInTheDocument();
    fireEvent.click(button('Done'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('a footer override returning null keeps the standard footer', () => {
    renderWithProviders(<WizardShell {...props({ embedded, footer: () => null })} />);
    expect(button('Next')).toBeInTheDocument();
    expect(button('Cancel')).toBeInTheDocument();
  });

  it('the footer override can replace the left side too', () => {
    renderWithProviders(
      <WizardShell {...props({ embedded, footer: ctx => ({ start: <button onClick={ctx.close}>Close wizard</button> }) })} />
    );
    expect(screen.queryByRole('button', { name: /^cancel$/i })).toBeNull();
    expect(button('Close wizard')).toBeInTheDocument();
    expect(button('Next')).toBeInTheDocument();
  });

  it('stayOpenOnFinish: onFinish resolving nothing does NOT close; the step stays shown', async () => {
    const onClose = jest.fn();
    const onFinish = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <WizardShell {...props({ embedded, steps: [step('a')], onClose, onFinish, stayOpenOnFinish: true })} />
    );
    fireEvent.click(button('Finish'));
    await waitFor(() => expect(onFinish).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(button('Finish')).not.toBeDisabled());
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText('content-a')).toBeInTheDocument();
  });

  it('stayOpenOnFinish still shows a returned success screen', async () => {
    const onFinish = jest.fn().mockResolvedValue({
      icon: null,
      title: 'Recorded',
      body: '3 open items left',
      actions: <button>Next open item</button>,
    });
    renderWithProviders(<WizardShell {...props({ embedded, steps: [step('a')], onFinish, stayOpenOnFinish: true })} />);
    fireEvent.click(button('Finish'));
    expect(await screen.findByText('Recorded')).toBeInTheDocument();
  });

  it("Skip marks the step 'skipped' (no tick); Next later turns it completed", () => {
    renderWithProviders(
      <WizardShell {...props({ embedded, steps: [step('a', { isSkippable: true }), step('b'), step('c')] })} />
    );
    fireEvent.click(button('Skip'));
    const skipped = screen.getByLabelText('Step a, skipped');
    expect(skipped).toBeInTheDocument();
    // No checkmark icon on the skipped row (a completed row renders one).
    expect(skipped.closest('li')!.querySelector('svg')).toBeNull();
    fireEvent.click(button('Next'));
    expect(screen.getByLabelText('Step a, skipped')).toBeInTheDocument();
    expect(screen.getByLabelText('Step b, completed').closest('li')!.querySelector('svg')).not.toBeNull();
    // Back to the skipped step, then leave it with Next: now completed.
    fireEvent.click(button('Back'));
    fireEvent.click(button('Back'));
    expect(screen.getByLabelText('Step a, active')).toBeInTheDocument();
    fireEvent.click(button('Next'));
    expect(screen.getByLabelText('Step a, completed')).toBeInTheDocument();
  });

  it('re-opening forgets skipped marks', () => {
    const steps = [step('a', { isSkippable: true }), step('b')];
    const { rerender } = renderWithProviders(<WizardShell {...props({ embedded, steps })} />);
    fireEvent.click(button('Skip'));
    expect(screen.getByLabelText('Step a, skipped')).toBeInTheDocument();
    rerender(<WizardShell {...props({ embedded, steps, open: false })} />);
    rerender(<WizardShell {...props({ embedded, steps, open: true })} />);
    expect(screen.getByLabelText('Step a, active')).toBeInTheDocument();
    expect(screen.getByLabelText('Step b, pending')).toBeInTheDocument();
  });
});

describe("wizardShellReducer — 'skipped'", () => {
  const cfg = (id: string): IWizardStepConfig => ({ id, label: id, renderContent: () => null, canAdvance: () => true });
  const base = () => buildInitialShellState([cfg('a'), cfg('b'), cfg('c')]);
  const statuses = (s: ReturnType<typeof base>) => s.steps.map(x => x.status);

  it('SKIP_STEP advances and marks the left step skipped', () => {
    const s = wizardShellReducer(base(), { type: 'SKIP_STEP' });
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['skipped', 'active', 'pending']);
  });

  it('SKIP_STEP on the last step is a no-op (same reference)', () => {
    const last = buildInitialShellState([cfg('a'), cfg('b')], 'b');
    expect(wizardShellReducer(last, { type: 'SKIP_STEP' })).toBe(last);
  });

  it('a skipped mark survives moving back past it and dynamic insertions', () => {
    let s = wizardShellReducer(wizardShellReducer(base(), { type: 'NEXT_STEP' }), { type: 'SKIP_STEP' });
    expect(statuses(s)).toEqual(['completed', 'skipped', 'active']);
    s = wizardShellReducer(s, { type: 'GO_TO_STEP', stepIndex: 0 });
    expect(statuses(s)).toEqual(['active', 'skipped', 'pending']);
    s = wizardShellReducer(s, { type: 'ADD_DYNAMIC_STEP', config: cfg('x') });
    expect(statuses(s)).toEqual(['active', 'skipped', 'pending', 'pending']);
  });

  it('GO_TO_STEP with clearSkipped forgets the marks', () => {
    const s0 = wizardShellReducer(base(), { type: 'SKIP_STEP' });
    const s = wizardShellReducer(s0, { type: 'GO_TO_STEP', stepIndex: 2, clearSkipped: true });
    expect(statuses(s)).toEqual(['completed', 'completed', 'active']);
  });
});
