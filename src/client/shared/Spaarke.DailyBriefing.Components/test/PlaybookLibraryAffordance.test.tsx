/**
 * PlaybookLibraryAffordance.test.tsx — R7 task 095 / FR-18.
 *
 * Asserts the "Browse Playbooks" overflow menu affordance on the
 * `DigestHeader` (the shared `@spaarke/daily-briefing-components` component)
 * is correctly gated on the `onBrowsePlaybooks` prop and fires the host
 * callback on click. This is consumer surface 2 of 3 for FR-18 (chat surface
 * 1 done in task 094; ad-hoc surface 3 pending in task 096).
 *
 * Pattern parity with task 094's `PlaybookLibraryHardSlash.test.ts`:
 *   - Pure component test (no full DailyBriefingApp mount; no service mocks).
 *   - Asserts AFFORDANCE PRESENCE + CALLBACK INVOCATION + back-compat
 *     (omitted prop => no menu rendered).
 *   - Verifies the menu item label + icon (semantic structure for a11y).
 *
 * Test classification per ADR-038 §7 (KEEP): MAINTAIN-class component test —
 * pins the public contract (`onBrowsePlaybooks` callback semantics) that
 * downstream hosts (standalone DailyBriefing main.tsx + embedded section
 * registration factory) depend on. Removing this test would lose the
 * detection net for accidental contract regressions.
 *
 * NFR-03: jest-environment-jsdom; no Dataverse; no MSAL; no /narrate.
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { DigestHeader } from '../src/components/DigestHeader';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function renderHeader(props: React.ComponentProps<typeof DigestHeader>): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DigestHeader {...props} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('DigestHeader — Playbook Library affordance (R7 task 095 / FR-18)', () => {
  it('renders the "More actions" overflow trigger when onBrowsePlaybooks is provided', () => {
    const onBrowsePlaybooks = jest.fn();
    renderHeader({
      totalUnreadCount: 0,
      onBrowsePlaybooks,
    });

    // The Tooltip+Button trigger has aria-label="More actions" per the
    // DigestHeader implementation. Mirrors task 094 audit's preference for
    // discoverable accessible labels.
    expect(screen.getByRole('button', { name: /More actions/i })).toBeInTheDocument();
  });

  it('does NOT render the overflow trigger when onBrowsePlaybooks is omitted (back-compat)', () => {
    renderHeader({
      totalUnreadCount: 0,
      // onBrowsePlaybooks omitted on purpose.
    });

    // Back-compat invariant: hosts that don't wire the callback (e.g.,
    // non-Dataverse preview surfaces, future test harnesses) must not see the
    // overflow menu. This protects the shared-lib API contract: the menu
    // is opt-in, not opt-out.
    expect(screen.queryByRole('button', { name: /More actions/i })).not.toBeInTheDocument();
  });

  it('opens the menu and fires onBrowsePlaybooks when "Browse Playbooks" is clicked', () => {
    const onBrowsePlaybooks = jest.fn();
    renderHeader({
      totalUnreadCount: 5,
      onBrowsePlaybooks,
    });

    // Click the trigger to open the menu.
    const trigger = screen.getByRole('button', { name: /More actions/i });
    act(() => {
      fireEvent.click(trigger);
    });

    // Menu item with the canonical label is rendered inside the popover.
    const menuItem = screen.getByRole('menuitem', { name: /^Browse Playbooks$/i });
    expect(menuItem).toBeInTheDocument();

    // Click the menu item — host callback fires exactly once.
    act(() => {
      fireEvent.click(menuItem);
    });
    expect(onBrowsePlaybooks).toHaveBeenCalledTimes(1);
  });

  it('preserves existing refresh + preferencesSlot rendering when the affordance is added', () => {
    const onRefresh = jest.fn();
    const onBrowsePlaybooks = jest.fn();
    renderHeader({
      totalUnreadCount: 0,
      onRefresh,
      preferencesSlot: <div data-testid="prefs-slot" />,
      onBrowsePlaybooks,
    });

    // Pre-existing affordances unchanged. This is the regression guard for
    // task 095 — we extended the header, we did NOT replace its previous
    // semantics. If a future change accidentally drops the refresh button
    // or the preferences slot, this test fails.
    expect(screen.getByRole('button', { name: /Refresh notifications/i })).toBeInTheDocument();
    expect(screen.getByTestId('prefs-slot')).toBeInTheDocument();
    // New affordance also renders alongside.
    expect(screen.getByRole('button', { name: /More actions/i })).toBeInTheDocument();
  });
});
