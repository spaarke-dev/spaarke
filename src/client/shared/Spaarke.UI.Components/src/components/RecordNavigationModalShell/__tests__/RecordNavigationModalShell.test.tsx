/**
 * Tests for `RecordNavigationModalShell` — smart-todo-r4 task R4-010.
 *
 * Covers spec FR-12 (chrome + props), FR-14 (cross-frame dirty-check round
 * trip), and the disabled-boundary + counter-format invariants.
 *
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 * @see ADR-012 - Shared component library
 */

import * as React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  RecordNavigationModalShell,
  DIRTY_CHECK_REQUEST_TYPE,
  DIRTY_CHECK_RESULT_TYPE,
  type IRecordNavigationModalShellProps,
} from '../index';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface MockIframeWindow {
  postMessage: jest.Mock;
}

/**
 * Builds a minimal `Window`-shaped target for `dirtyCheckTargetWindow`. The
 * shell only invokes `.postMessage()` on this target, so we don't need a full
 * Window facade.
 */
function makeMockIframeWindow(): MockIframeWindow {
  return { postMessage: jest.fn() };
}

/**
 * Simulates the iframe-side listener responding with a `dirty-check-result`
 * message. Posts as a `MessageEvent` to the test's `window` so the shell's
 * `message` listener picks it up.
 *
 * Origin defaults to a value matching the default allow-list
 * (`https://contoso.crm.dynamics.com` — wildcard `https://*.dynamics.com`).
 */
function postDirtyCheckResponse(
  correlationId: string,
  dirty: boolean,
  origin: string = 'https://contoso.crm.dynamics.com'
): void {
  const event = new MessageEvent('message', {
    data: { type: DIRTY_CHECK_RESULT_TYPE, correlationId, dirty },
    origin,
  });
  window.dispatchEvent(event);
}

/**
 * Reads the `correlationId` from the most recent `postMessage` call on the
 * mock iframe target. Lets tests respond with a matching correlationId.
 */
function getLastCorrelationId(target: MockIframeWindow): string {
  const calls = target.postMessage.mock.calls;
  if (calls.length === 0) throw new Error('No postMessage call recorded');
  const last = calls[calls.length - 1][0];
  if (!last || typeof last !== 'object' || typeof last.correlationId !== 'string') {
    throw new Error('postMessage payload missing correlationId');
  }
  return last.correlationId as string;
}

function defaultProps(
  overrides?: Partial<IRecordNavigationModalShellProps>
): IRecordNavigationModalShellProps {
  return {
    currentIndex: 1,
    navigationTotal: 5,
    onNavigate: jest.fn().mockResolvedValue(undefined),
    title: 'Sample Record',
    children: <div data-testid="content-child">iframe-placeholder</div>,
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('RecordNavigationModalShell', () => {
  describe('chrome rendering (FR-12)', () => {
    it('renders title, prev/next buttons, counter, and children slot', () => {
      renderWithProviders(<RecordNavigationModalShell {...defaultProps()} />);

      // Title
      expect(screen.getByRole('heading', { name: 'Sample Record' })).toBeInTheDocument();
      // Prev + next buttons (Fluent v9 Button gets `role=button` automatically)
      expect(screen.getByRole('button', { name: 'Previous record' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next record' })).toBeInTheDocument();
      // Counter — "N of M" 1-based; currentIndex=1, total=5 → "2 of 5"
      expect(screen.getByText('2 of 5')).toBeInTheDocument();
      // Children slot
      expect(screen.getByTestId('content-child')).toBeInTheDocument();
    });

    it('renders the action-bar slot when provided', () => {
      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps()}
          actionBar={
            <button type="button" data-testid="action-close">
              Close
            </button>
          }
        />
      );
      expect(screen.getByTestId('action-close')).toBeInTheDocument();
    });

    it('omits the action-bar region (and divider) when actionBar is not supplied', () => {
      const { container } = renderWithProviders(
        <RecordNavigationModalShell {...defaultProps()} />
      );
      // Vertical Divider renders as a presentation-role element with the
      // `aria-orientation=vertical` attribute. Absence is the contract.
      const dividers = container.querySelectorAll('[aria-orientation="vertical"]');
      expect(dividers.length).toBe(0);
    });
  });

  describe('disabled-boundary semantics (FR-12)', () => {
    it('disables prev at index 0', () => {
      renderWithProviders(
        <RecordNavigationModalShell {...defaultProps({ currentIndex: 0 })} />
      );
      expect(screen.getByRole('button', { name: 'Previous record' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Next record' })).not.toBeDisabled();
    });

    it('disables next at the final index (navigationTotal - 1)', () => {
      renderWithProviders(
        <RecordNavigationModalShell {...defaultProps({ currentIndex: 4, navigationTotal: 5 })} />
      );
      expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Previous record' })).not.toBeDisabled();
    });
  });

  describe('counter format', () => {
    it('renders "1 of M" for index 0', () => {
      renderWithProviders(
        <RecordNavigationModalShell {...defaultProps({ currentIndex: 0, navigationTotal: 7 })} />
      );
      expect(screen.getByText('1 of 7')).toBeInTheDocument();
    });

    it('renders "0 of 0" fallback when navigationTotal is 0', () => {
      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps({ currentIndex: 0, navigationTotal: 0 })}
        />
      );
      expect(screen.getByText('0 of 0')).toBeInTheDocument();
    });
  });

  describe('clean-path navigation (no dirty-check target)', () => {
    it('invokes onNavigate("next") immediately when no dirtyCheckTargetWindow is supplied', async () => {
      const onNavigate = jest.fn().mockResolvedValue(undefined);
      const user = userEvent.setup();
      renderWithProviders(
        <RecordNavigationModalShell {...defaultProps({ onNavigate })} />
      );
      await user.click(screen.getByRole('button', { name: 'Next record' }));
      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledTimes(1);
        expect(onNavigate).toHaveBeenCalledWith('next');
      });
    });

    it('invokes onNavigate("prev") immediately when no dirtyCheckTargetWindow is supplied', async () => {
      const onNavigate = jest.fn().mockResolvedValue(undefined);
      const user = userEvent.setup();
      renderWithProviders(
        <RecordNavigationModalShell {...defaultProps({ onNavigate })} />
      );
      await user.click(screen.getByRole('button', { name: 'Previous record' }));
      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledTimes(1);
        expect(onNavigate).toHaveBeenCalledWith('prev');
      });
    });
  });

  describe('dirty-check protocol — clean response (FR-14)', () => {
    it('posts a request-dirty-check message and proceeds when iframe responds clean', async () => {
      const onNavigate = jest.fn().mockResolvedValue(undefined);
      const iframe = makeMockIframeWindow();
      const user = userEvent.setup();

      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps({
            onNavigate,
            dirtyCheckTargetWindow: iframe as unknown as Window,
          })}
        />
      );

      await user.click(screen.getByRole('button', { name: 'Next record' }));

      // Request was posted with the expected discriminator + a correlationId.
      await waitFor(() => {
        expect(iframe.postMessage).toHaveBeenCalledTimes(1);
      });
      const sent = iframe.postMessage.mock.calls[0][0];
      expect(sent.type).toBe(DIRTY_CHECK_REQUEST_TYPE);
      expect(typeof sent.correlationId).toBe('string');

      // Iframe responds clean.
      const correlationId = getLastCorrelationId(iframe);
      act(() => {
        postDirtyCheckResponse(correlationId, false);
      });

      await waitFor(() => {
        expect(onNavigate).toHaveBeenCalledWith('next');
      });
      expect(screen.queryByRole('heading', { name: 'Discard unsaved changes?' })).toBeNull();
    });
  });

  describe('dirty-check protocol — dirty response → discard dialog (FR-14)', () => {
    it('shows the discard dialog when iframe reports dirty=true', async () => {
      const iframe = makeMockIframeWindow();
      const user = userEvent.setup();

      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps({
            dirtyCheckTargetWindow: iframe as unknown as Window,
          })}
        />
      );

      await user.click(screen.getByRole('button', { name: 'Next record' }));
      await waitFor(() => {
        expect(iframe.postMessage).toHaveBeenCalledTimes(1);
      });

      const correlationId = getLastCorrelationId(iframe);
      act(() => {
        postDirtyCheckResponse(correlationId, true);
      });

      await waitFor(() => {
        expect(screen.getByText('Discard unsaved changes?')).toBeInTheDocument();
      });
    });

    it('aborts nav when user clicks Cancel in the discard dialog', async () => {
      const onNavigate = jest.fn().mockResolvedValue(undefined);
      const onDirtyDiscard = jest.fn();
      const iframe = makeMockIframeWindow();
      const user = userEvent.setup();

      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps({
            onNavigate,
            onDirtyDiscard,
            dirtyCheckTargetWindow: iframe as unknown as Window,
          })}
        />
      );

      await user.click(screen.getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(iframe.postMessage).toHaveBeenCalledTimes(1));
      const correlationId = getLastCorrelationId(iframe);
      act(() => {
        postDirtyCheckResponse(correlationId, true);
      });
      await waitFor(() =>
        expect(screen.getByText('Discard unsaved changes?')).toBeInTheDocument()
      );

      await user.click(screen.getByRole('button', { name: 'Cancel' }));

      await waitFor(() => {
        expect(screen.queryByText('Discard unsaved changes?')).toBeNull();
      });
      expect(onNavigate).not.toHaveBeenCalled();
      expect(onDirtyDiscard).not.toHaveBeenCalled();
    });

    it('proceeds and fires onDirtyDiscard when user clicks "Discard and continue"', async () => {
      const onNavigate = jest.fn().mockResolvedValue(undefined);
      const onDirtyDiscard = jest.fn();
      const iframe = makeMockIframeWindow();
      const user = userEvent.setup();

      renderWithProviders(
        <RecordNavigationModalShell
          {...defaultProps({
            onNavigate,
            onDirtyDiscard,
            dirtyCheckTargetWindow: iframe as unknown as Window,
          })}
        />
      );

      await user.click(screen.getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(iframe.postMessage).toHaveBeenCalledTimes(1));
      const correlationId = getLastCorrelationId(iframe);
      act(() => {
        postDirtyCheckResponse(correlationId, true);
      });
      await waitFor(() =>
        expect(screen.getByText('Discard unsaved changes?')).toBeInTheDocument()
      );

      await user.click(screen.getByRole('button', { name: 'Discard and continue' }));

      await waitFor(() => {
        expect(onDirtyDiscard).toHaveBeenCalledTimes(1);
        expect(onNavigate).toHaveBeenCalledWith('next');
      });
      // Discard must precede onNavigate so caller-side cleanup runs first.
      const discardOrder = onDirtyDiscard.mock.invocationCallOrder[0];
      const navigateOrder = onNavigate.mock.invocationCallOrder[0];
      expect(discardOrder).toBeLessThan(navigateOrder);
    });
  });

  describe('origin allow-list (FR-14)', () => {
    it('ignores responses from untrusted origins (falls back to timeout = clean)', async () => {
      jest.useFakeTimers();
      try {
        const onNavigate = jest.fn().mockResolvedValue(undefined);
        const iframe = makeMockIframeWindow();
        const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });

        renderWithProviders(
          <RecordNavigationModalShell
            {...defaultProps({
              onNavigate,
              dirtyCheckTargetWindow: iframe as unknown as Window,
              dirtyCheckTimeout: 500,
            })}
          />
        );

        await user.click(screen.getByRole('button', { name: 'Next record' }));
        await waitFor(() => expect(iframe.postMessage).toHaveBeenCalledTimes(1));
        const correlationId = getLastCorrelationId(iframe);

        // Untrusted origin — should be ignored. Even though the iframe
        // reports dirty=true, the shell must not surface the discard dialog
        // for this message.
        act(() => {
          postDirtyCheckResponse(correlationId, true, 'https://evil.example.com');
        });

        // Discard dialog must NOT appear from the untrusted message.
        expect(screen.queryByText('Discard unsaved changes?')).toBeNull();

        // Advance to the timeout boundary — shell now treats as clean.
        await act(async () => {
          jest.advanceTimersByTime(600);
        });

        await waitFor(() => {
          expect(onNavigate).toHaveBeenCalledWith('next');
        });
        expect(screen.queryByText('Discard unsaved changes?')).toBeNull();
      } finally {
        jest.useRealTimers();
      }
    });
  });

  describe('timeout fallback (FR-14)', () => {
    it('treats no-response as clean and invokes onNavigate after the timeout', async () => {
      jest.useFakeTimers();
      try {
        const onNavigate = jest.fn().mockResolvedValue(undefined);
        const iframe = makeMockIframeWindow();
        const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });

        renderWithProviders(
          <RecordNavigationModalShell
            {...defaultProps({
              onNavigate,
              dirtyCheckTargetWindow: iframe as unknown as Window,
              dirtyCheckTimeout: 250,
            })}
          />
        );

        await user.click(screen.getByRole('button', { name: 'Next record' }));
        await waitFor(() => expect(iframe.postMessage).toHaveBeenCalledTimes(1));

        expect(onNavigate).not.toHaveBeenCalled();

        await act(async () => {
          jest.advanceTimersByTime(300);
        });

        await waitFor(() => {
          expect(onNavigate).toHaveBeenCalledWith('next');
        });
      } finally {
        jest.useRealTimers();
      }
    });
  });
});
