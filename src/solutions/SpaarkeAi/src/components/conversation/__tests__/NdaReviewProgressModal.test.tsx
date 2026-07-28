/**
 * NdaReviewProgressModal tests — UAT round-5 #9 center-screen progress popup.
 *
 * Covers: renders nothing when idle; renders the stepper (portaled) while running; auto-dismisses on a
 * terminal state after its linger delay; shows the error title on failure.
 */
import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { NdaReviewProgressModal, NDA_REVIEW_PROGRESS_STEPS, NDA_REVIEW_WORKING_PHRASES } from '../NdaReviewProgressModal';

function renderModal(status: 'idle' | 'running' | 'complete' | 'error', onClose = jest.fn()) {
  return {
    onClose,
    ...render(
      <FluentProvider theme={webLightTheme}>
        <NdaReviewProgressModal status={status} onClose={onClose} />
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
    expect(screen.getByText('Reviewing your NDA…')).toBeInTheDocument();
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
