/**
 * NavigatorBody Component Tests
 *
 * Verifies task 040 acceptance criteria (spaarke-side-pane-navigation-history-r1
 * spec FR-01 + FR-11), UPDATED for the UAT-driven redesign (Recent / Bookmarks /
 * Monitored / Views, 4 tabs — formerly Recent / Pinned / Views with Monitored
 * nested inside Pinned) and the capture-wiring bug fix:
 *   - Recent/Bookmarks/Monitored/Views tab scaffold + persistent search-bar
 *     placeholder render
 *   - Renders in light AND dark themes (ADR-021 — Fluent v9 tokens, no hardcoded colors)
 *   - Tab selection switches the active panel
 *   - Portal FluentProvider re-wrap is present on the search-bar info Tooltip
 *     (ADR-021 portal gotcha), and the re-wrapped theme differs between light/dark
 *   - A non-default `--sprk-ui-scale` (Display-size setting) produces a scaled
 *     Fluent theme via the SAME `scaleTheme`/`useUiScale` composition NavigatorBody
 *     wires up (NFR-07)
 *   - No-Xrm surfaces degrade to a safe empty state and never throw (negative
 *     acceptance criterion)
 *   - BUG FIX: `startNavigatorCapture` (previously defined but never invoked
 *     anywhere) is now invoked exactly once on mount
 *
 * @see ../src/NavigatorBody.tsx
 * @see ADR-021 Fluent UI v9 design system (tokens, portal re-wrap, light/dark, --sprk-ui-scale)
 * @see ADR-022 React 16/17-safe shared-lib code (theme/scale hooks NavigatorBody consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  DISPLAY_SIZE_STORAGE_KEY,
  THEME_STORAGE_KEY,
  getEffectiveUiScale,
  resolveCodePageTheme,
  scaleTheme,
} from '@spaarke/ui-components';

// Mocked so the capture-start assertion below doesn't depend on the real
// ~1.5s poll loop actually ticking — this test only verifies NavigatorBody's
// OWN wiring (the bug fix), not navigatorCaptureService.ts's poll behavior
// itself (covered by that module's own test suite in the shared lib).
jest.mock('@spaarke/ui-components/services/navigator/navigatorCaptureService', () => ({
  startNavigatorCapture: jest.fn(() => jest.fn()),
}));

import { startNavigatorCapture } from '@spaarke/ui-components/services/navigator/navigatorCaptureService';
import { NavigatorBody } from '../src/NavigatorBody';

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Installs a minimal `window.Xrm` mock (WebApi only — enough for `getXrm()`). */
function installMockXrm(): void {
  (window as unknown as { Xrm: unknown }).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
  };
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

/** NavigatorBody normally renders under an ambient FluentProvider (SprkSidePaneHost's root). */
function renderNavigatorBody(paneId = 'test-pane') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <NavigatorBody paneId={paneId} />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('NavigatorBody', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  beforeEach(() => {
    localStorage.clear();
    installMockXrm();
    (startNavigatorCapture as jest.Mock).mockClear();
  });

  afterEach(() => {
    localStorage.clear();
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
  });

  // ───────────────────────────────────────────────────────────────────────
  // Tab scaffold + search-bar placeholder
  // ───────────────────────────────────────────────────────────────────────

  it('render_WithXrm_ShowsRecentBookmarksMonitoredViewsTabsAndQuickSwitcherSearchBox', async () => {
    renderNavigatorBody();

    expect(screen.getByTestId('navigator-tab-recent')).toHaveTextContent('Recent');
    expect(screen.getByTestId('navigator-tab-bookmarks')).toHaveTextContent('Bookmarks');
    expect(screen.getByTestId('navigator-tab-monitored')).toHaveTextContent('Monitored');
    expect(screen.getByTestId('navigator-tab-views')).toHaveTextContent('Views');
    // task 070 — the search-bar placeholder is replaced by the real QuickSwitcher.
    expect(screen.getByTestId('navigator-quickswitcher-input')).toBeInTheDocument();

    // Default active tab is Recent — <RecentTab> renders only captured Viewed
    // history now (UAT redesign removed the Viewed/Edited toggle).
    // installMockXrm() here has no `Utility`, so RecentTab's load short-circuits
    // to its empty state rather than querying history rows.
    expect(await screen.findByTestId('recent-tab-empty')).toHaveTextContent(
      'Recently viewed records will appear here.'
    );
  });

  it('render_TabClick_SwitchesActivePanel', async () => {
    renderNavigatorBody();
    const user = userEvent.setup();

    await user.click(screen.getByTestId('navigator-tab-bookmarks'));

    // <BookmarksTab/> (formerly <PinnedTab/>). installMockXrm() here has no
    // `Utility`, so BookmarksTab's load short-circuits to its empty state
    // (mirrors the `recent`-tab assertion above).
    expect(await screen.findByTestId('bookmarks-tab-empty')).toHaveTextContent(
      'Pinned records, views, and links will appear here.'
    );
    expect(screen.queryByTestId('navigator-tab-panel-recent')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('navigator-tab-monitored'));

    expect(await screen.findByTestId('monitored-tab-empty')).toHaveTextContent(
      "Records you're monitoring will appear here."
    );
    expect(screen.queryByTestId('navigator-tab-panel-bookmarks')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('navigator-tab-views'));

    expect(await screen.findByTestId('navigator-tab-panel-views')).toHaveTextContent(
      'Your saved views will appear here.'
    );
  });

  // ───────────────────────────────────────────────────────────────────────
  // Light + dark theme rendering (ADR-021) + portal re-wrap distinctness
  // ───────────────────────────────────────────────────────────────────────

  it('render_LightAndDarkThemePreference_RendersScaffoldAndReWrapsPortalWithDistinctThemes', async () => {
    const user = userEvent.setup();

    // Light
    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    const { unmount: unmountLight } = renderNavigatorBody();
    expect(screen.getByTestId('navigator-tab-recent')).toBeInTheDocument();
    await user.hover(screen.getByTestId('navigator-search-info-icon'));
    let lightProviderClass: string | null = null;
    await waitFor(() => {
      const providers = document.querySelectorAll('.fui-FluentProvider');
      // The test's own outer wrapper is one; the Tooltip portal re-wrap is a second.
      expect(providers.length).toBeGreaterThanOrEqual(2);
      lightProviderClass = providers[providers.length - 1].className;
    });
    unmountLight();

    // Dark
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    renderNavigatorBody();
    expect(screen.getByTestId('navigator-tab-recent')).toBeInTheDocument();
    await user.hover(screen.getByTestId('navigator-search-info-icon'));
    let darkProviderClass: string | null = null;
    await waitFor(() => {
      const providers = document.querySelectorAll('.fui-FluentProvider');
      expect(providers.length).toBeGreaterThanOrEqual(2);
      darkProviderClass = providers[providers.length - 1].className;
    });

    expect(lightProviderClass).toBeTruthy();
    expect(darkProviderClass).toBeTruthy();
    // Light and dark resolve to different Fluent themes -> different generated
    // theme class names on the re-wrapped portal provider (honors both, ADR-021).
    expect(darkProviderClass).not.toBe(lightProviderClass);
  });

  // ───────────────────────────────────────────────────────────────────────
  // --sprk-ui-scale (NFR-07) — same composition NavigatorBody wires up
  // ───────────────────────────────────────────────────────────────────────

  it('render_NonDefaultDisplaySize_RendersWithoutErrorAndScaledThemeDiffersFromBase', () => {
    localStorage.setItem(DISPLAY_SIZE_STORAGE_KEY, 'large');

    expect(() => renderNavigatorBody()).not.toThrow();
    expect(screen.getByTestId('navigator-tab-recent')).toBeInTheDocument();

    // Direct check of the SAME composition NavigatorBody wires up internally
    // (resolveCodePageTheme + scaleTheme + getEffectiveUiScale) — proves the
    // scaled theme genuinely respects the non-default Display-size setting.
    const baseTheme = resolveCodePageTheme();
    const scaledTheme = scaleTheme(baseTheme, getEffectiveUiScale('large'));
    expect(getEffectiveUiScale('large')).toBeGreaterThan(1);
    expect(scaledTheme.fontSizeBase300).not.toBe(baseTheme.fontSizeBase300);
  });

  // ───────────────────────────────────────────────────────────────────────
  // No-Xrm surface degrades gracefully (negative acceptance criterion)
  // ───────────────────────────────────────────────────────────────────────

  it('mount_NoXrmAvailable_RendersSafeEmptyStateAndDoesNotThrow', () => {
    removeMockXrm();

    expect(() => renderNavigatorBody()).not.toThrow();

    expect(screen.getByTestId('navigator-body-no-xrm')).toHaveTextContent(
      'Navigator is unavailable outside a Dataverse session.'
    );
    expect(screen.queryByTestId('navigator-body')).not.toBeInTheDocument();
    expect(screen.queryByTestId('navigator-tabs')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // BUG FIX — startNavigatorCapture() was defined but never called anywhere,
  // so no page-visit history was ever written and the Recent tab was
  // permanently empty. NavigatorBody now starts the poll on mount.
  // ───────────────────────────────────────────────────────────────────────

  it('mount_NavigatorBody_StartsNavigatorCaptureOnce', () => {
    const { unmount } = renderNavigatorBody();

    expect(startNavigatorCapture).toHaveBeenCalledTimes(1);
    expect(startNavigatorCapture).toHaveBeenCalledWith(
      expect.objectContaining({ onError: expect.any(Function) })
    );

    // Unmounting calls the returned stop function — the capture guard resets
    // so a genuine remount (not asserted further here) can start its own poll.
    const stopFn = (startNavigatorCapture as jest.Mock).mock.results[0].value;
    unmount();
    expect(stopFn).toHaveBeenCalledTimes(1);
  });
});
