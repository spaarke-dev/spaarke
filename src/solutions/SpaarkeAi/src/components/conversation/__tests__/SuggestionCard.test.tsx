/**
 * SuggestionCard + useSuggestionCards tests — spaarke-notification-spine-r1 task 051 / FR-16.
 *
 * Covers the acceptance criteria:
 *   - renders-from-envelope: a valid kind=suggestion envelope renders a bordered card.
 *   - expired-suggestion-does-not-render: an expired envelope is filtered PRE-mount (absence).
 *   - click-re-fetches-before-dispatch: the BFF re-ground fires BEFORE the shared dispatch (call-order).
 *   - stale-suggestion-fails-gracefully: a row gone from /pending → no dispatch + a stable local line.
 *   - dark-mode-token-correctness (ADR-021): renders token-driven (no inline color) under both themes.
 *
 * ADR-021 note (mirrors ConsumerChips.test.tsx): token-driven styling is asserted STRUCTURALLY
 * here (no inline hardcoded colors); the pixel-level dark-mode check runs in the browser.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { Theme } from '@fluentui/react-components';

import { SuggestionCard } from '../SuggestionCard';
import {
  useSuggestionCards,
  type SuggestionCardsDeps,
  type SuggestionEnvelopeLite,
  type PendingSuggestionItem,
} from '../useSuggestionCards';

// A fixed clock so expiry is deterministic regardless of wall time.
const NOW_MS = Date.parse('2026-07-22T12:00:00.000Z');
const FUTURE = '2026-07-22T13:00:00.000Z'; // after NOW → valid
const PAST = '2026-07-22T11:00:00.000Z'; // before NOW → expired

function envelope(overrides: Partial<SuggestionEnvelopeLite> = {}): SuggestionEnvelopeLite {
  return {
    kind: 'suggestion',
    suggestionId: 'sugg-1',
    source: 'daily-briefing',
    regardingRecordId: 'rec-1',
    title: 'Review Acme v. Beta',
    actionHint: 'review',
    expiresAt: FUTURE,
    ...overrides,
  };
}

function pendingItem(env: SuggestionEnvelopeLite, outboxRowId = 'row-1'): PendingSuggestionItem {
  return { outboxRowId, kind: 'suggestion', envelope: env, expiresAt: env.expiresAt };
}

function renderWithTheme(ui: React.ReactElement, theme: Theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

/** Test harness that renders only the hook's suggestion slot. */
function Harness(props: { deps: SuggestionCardsDeps }): React.JSX.Element {
  const { suggestionSlot } = useSuggestionCards(props.deps);
  return <>{suggestionSlot}</>;
}

/** Captures the `suggestion` handler so a test can simulate a spine signal (live or poll). */
function captureSubscribe(): {
  subscribe: SuggestionCardsDeps['subscribe'];
  fire: () => void;
} {
  let handler: ((event: unknown) => void) | null = null;
  const subscribe: SuggestionCardsDeps['subscribe'] = (_kind, cb) => {
    handler = cb as (event: unknown) => void;
    return () => {
      handler = null;
    };
  };
  return {
    subscribe,
    fire: () => handler?.({ outboxRowId: 'row-1', kind: 'suggestion', source: 'live' }),
  };
}

function baseDeps(overrides: Partial<SuggestionCardsDeps> = {}): SuggestionCardsDeps {
  return {
    subscribe: jest.fn(() => () => {}),
    fetchPending: jest.fn(async () => [] as ReadonlyArray<PendingSuggestionItem>),
    onSuggestionAction: jest.fn(),
    inject: jest.fn(),
    now: () => NOW_MS,
    ...overrides,
  };
}

describe('SuggestionCard (presentational)', () => {
  it('renders the card from a suggestion model with its title and a stable test id', () => {
    const onAction = jest.fn();
    renderWithTheme(
      <SuggestionCard
        suggestion={{ suggestionId: 'sugg-1', title: 'Review Acme v. Beta', actionHint: 'review' }}
        onAction={onAction}
      />
    );

    const card = screen.getByTestId('suggestion-card-sugg-1');
    expect(card).toBeInTheDocument();
    expect(card).toHaveTextContent('Review Acme v. Beta');
    expect(card).toHaveAttribute('data-suggestion-id', 'sugg-1');

    fireEvent.click(card);
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  // ADR-021: renders token-driven (no inline color) under BOTH themes.
  it.each([
    ['light', webLightTheme],
    ['dark', webDarkTheme],
  ])('renders under the %s theme with no inline hardcoded color', (_name, theme) => {
    const { unmount } = renderWithTheme(
      <SuggestionCard
        suggestion={{ suggestionId: 'sugg-x', title: 'Follow up on filing', actionHint: 'review' }}
        onAction={jest.fn()}
      />,
      theme as Theme
    );

    const card = screen.getByTestId('suggestion-card-sugg-x');
    expect(card).toBeInTheDocument();
    // No hardcoded hex/rgb color literal leaked onto the element's inline style (tokens only).
    const inlineStyle = card.getAttribute('style') ?? '';
    expect(inlineStyle).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(inlineStyle).not.toMatch(/rgb\(/);
    unmount();
  });
});

describe('useSuggestionCards (lifecycle)', () => {
  it('renders a card once a suggestion signal re-grounds a valid, non-expired row', async () => {
    const { subscribe, fire } = captureSubscribe();
    const deps = baseDeps({
      subscribe,
      fetchPending: jest.fn(async () => [pendingItem(envelope())]),
    });
    renderWithTheme(<Harness deps={deps} />);

    fire(); // a live spine signal → re-ground from /pending
    expect(await screen.findByTestId('suggestion-card-sugg-1')).toBeInTheDocument();
    expect(screen.getByTestId('suggestion-cards')).toBeInTheDocument();
  });

  it('does NOT render an expired suggestion (filtered pre-mount, verified by absence)', async () => {
    const { subscribe, fire } = captureSubscribe();
    const deps = baseDeps({
      subscribe,
      fetchPending: jest.fn(async () => [pendingItem(envelope({ expiresAt: PAST }))]),
    });
    renderWithTheme(<Harness deps={deps} />);

    fire();
    // Let the signal-driven refresh resolve, then assert the card never mounted.
    await waitFor(() => expect(deps.fetchPending).toHaveBeenCalled());
    expect(screen.queryByTestId('suggestion-card-sugg-1')).not.toBeInTheDocument();
    expect(screen.queryByTestId('suggestion-cards')).not.toBeInTheDocument();
  });

  it('re-fetches/re-grounds via the BFF BEFORE dispatching the action (call-order)', async () => {
    const { subscribe, fire } = captureSubscribe();
    const fetchPending = jest.fn(async () => [pendingItem(envelope())]); // still-present on both calls
    const onSuggestionAction = jest.fn();
    const deps = baseDeps({ subscribe, fetchPending, onSuggestionAction });
    renderWithTheme(<Harness deps={deps} />);

    fire();
    const card = await screen.findByTestId('suggestion-card-sugg-1');
    fireEvent.click(card);

    await waitFor(() => expect(onSuggestionAction).toHaveBeenCalledTimes(1));
    // The click-time re-fetch (the last fetchPending call) must precede the dispatch.
    const fetchOrders = fetchPending.mock.invocationCallOrder;
    const lastFetchOrder = fetchOrders[fetchOrders.length - 1];
    const dispatchOrder = onSuggestionAction.mock.invocationCallOrder[0];
    expect(lastFetchOrder).toBeLessThan(dispatchOrder);
    // The fresh envelope (not a stale token) is handed to the host.
    expect(onSuggestionAction).toHaveBeenCalledWith(expect.objectContaining({ actionHint: 'review' }));
  });

  it('fails gracefully when the re-fetch shows the suggestion is stale/revoked (no dispatch, stable line)', async () => {
    const { subscribe, fire } = captureSubscribe();
    // Present when the signal re-grounds, GONE at click time (expired/revoked server-side).
    const fetchPending = jest
      .fn<Promise<ReadonlyArray<PendingSuggestionItem>>, []>()
      .mockResolvedValueOnce([pendingItem(envelope())])
      .mockResolvedValueOnce([]);
    const onSuggestionAction = jest.fn();
    const inject = jest.fn();
    const deps = baseDeps({ subscribe, fetchPending, onSuggestionAction, inject });
    renderWithTheme(<Harness deps={deps} />);

    fire();
    const card = await screen.findByTestId('suggestion-card-sugg-1');
    fireEvent.click(card);

    await waitFor(() => expect(inject).toHaveBeenCalledTimes(1));
    expect(onSuggestionAction).not.toHaveBeenCalled();
    // The injected line is a stable local message (ADR-019) — not a raw server string.
    const injected = inject.mock.calls[0][0] as { content?: string };
    expect(typeof injected.content).toBe('string');
    expect(injected.content).toMatch(/no longer available/i);
  });
});
