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
import { NdaReviewProgressModal, NDA_REVIEW_PROGRESS_STEPS } from '../NdaReviewProgressModal';

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
});
