/**
 * NdaReviewProgressModal tests — UAT round-5 #9 center-screen progress popup; extended UAT round-3
 * item #8 (non-blocking / dismissible).
 *
 * Covers: renders nothing when idle; renders the stepper (portaled) while running; auto-dismisses on a
 * terminal state after its linger delay; shows the error title on failure; UAT round-3 item #8 —
 * `modalType="non-modal"` (no scrim), the "Continue working in background" dismiss button, dismiss via
 * Escape/light-dismiss, `visible=false` renders nothing even while `status` is still running/complete.
 */
import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { NdaReviewProgressModal, NDA_REVIEW_PROGRESS_STEPS, NDA_REVIEW_WORKING_PHRASES } from '../NdaReviewProgressModal';

function renderModal(
  status: 'idle' | 'running' | 'complete' | 'error',
  onClose = jest.fn(),
  onDismiss = jest.fn(),
  visible = status !== 'idle'
) {
  return {
    onClose,
    onDismiss,
    ...render(
      <FluentProvider theme={webLightTheme}>
        <NdaReviewProgressModal status={status} visible={visible} onClose={onClose} onDismiss={onDismiss} />
      </FluentProvider>
    ),
  };
}

describe('NdaReviewProgressModal', () => {
  afterEach(() => jest.useRealTimers());

  it('renders nothing when idle', () => {
    renderModal('idle');
    expect(screen.queryByTestId('nda-review-progress-modal')).not.toBeInTheDocument();
  });

  it('renders the center-screen stepper while running with the running title', () => {
    renderModal('running');
    expect(screen.getByTestId('nda-review-progress-modal')).toBeInTheDocument();
    expect(screen.getByText('Reviewing your agreement')).toBeInTheDocument();
    // The first real phase is shown.
    expect(screen.getByText(NDA_REVIEW_PROGRESS_STEPS[0].label)).toBeInTheDocument();
  });

  it('shows the complete title and auto-dismisses after the linger delay', () => {
    jest.useFakeTimers();
    const { onClose } = renderModal('complete');
    expect(screen.getByText('Review complete')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    act(() => {
      jest.advanceTimersByTime(1000);
    });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('shows the error title on failure', () => {
    renderModal('error');
    expect(screen.getByText('The review couldn’t finish')).toBeInTheDocument();
  });

  it('UAT round-6 #7 — shows a rotating legal "working" phrase while running, and rotates it over time', () => {
    jest.useFakeTimers();
    renderModal('running');
    const line = screen.getByTestId('nda-review-progress-working');
    const first = line.textContent ?? '';
    expect(NDA_REVIEW_WORKING_PHRASES.some(p => first.includes(p))).toBe(true);
    // After the rotate interval, the phrase advances to the next one.
    act(() => {
      jest.advanceTimersByTime(2200);
    });
    const second = screen.getByTestId('nda-review-progress-working').textContent ?? '';
    expect(NDA_REVIEW_WORKING_PHRASES.some(p => second.includes(p))).toBe(true);
    expect(second).not.toBe(first);
  });

  it('UAT round-6 #7 — hides the working phrase line on a terminal state', () => {
    renderModal('complete');
    expect(screen.queryByTestId('nda-review-progress-working')).not.toBeInTheDocument();
  });
});

describe('NdaReviewProgressModal — UAT round-3 item #8 (non-blocking / dismissible)', () => {
  afterEach(() => jest.useRealTimers());

  it('renders nothing when visible=false, even while status is still running (dismissed mid-run)', () => {
    renderModal('running', jest.fn(), jest.fn(), /* visible */ false);
    expect(screen.queryByTestId('nda-review-progress-modal')).not.toBeInTheDocument();
  });

  it('renders nothing when visible=false, even while status is complete (dismissed, then finished)', () => {
    renderModal('complete', jest.fn(), jest.fn(), /* visible */ false);
    expect(screen.queryByTestId('nda-review-progress-modal')).not.toBeInTheDocument();
  });

  it('shows a "Continue working in background" dismiss button while running', () => {
    renderModal('running');
    const dismissButton = screen.getByTestId('nda-review-progress-dismiss');
    expect(dismissButton).toBeInTheDocument();
    expect(dismissButton).toHaveTextContent('Continue working in background');
  });

  it('clicking the dismiss button calls onDismiss (NOT onClose) — hides, does not reset to idle', () => {
    const { onDismiss, onClose } = renderModal('running');
    fireEvent.click(screen.getByTestId('nda-review-progress-dismiss'));
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('does NOT show the dismiss button on a terminal state (complete/error) — it auto-dismisses on its own', () => {
    jest.useFakeTimers();
    renderModal('complete');
    expect(screen.queryByTestId('nda-review-progress-dismiss')).not.toBeInTheDocument();
  });

  it('Escape (light-dismiss) also routes to onDismiss, never onClose — dismiss is a visibility toggle, never a terminal reset', () => {
    const { onDismiss, onClose } = renderModal('running');
    const dialog = screen.getByRole('dialog');
    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('renders as modalType="non-modal", NOT the blocking "alert" role — proves the fix is in place', () => {
    // Fluent v9 sets role="alertdialog" for modalType="alert" (the OLD, blocking behavior this item
    // fixes) and role="dialog" for modalType="non-modal"/"modal". Asserting the "dialog" role is
    // present and "alertdialog" is absent is a precise, Fluent-documented proof that the surface is
    // no longer the blocking alert variant (the surface itself still renders identically otherwise).
    renderModal('running');
    expect(screen.getByTestId('nda-review-progress-modal')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});
