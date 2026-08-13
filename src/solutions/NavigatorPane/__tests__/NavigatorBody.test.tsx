/**
 * NavigatorBody Component Tests
 *
 * Verifies task 040 acceptance criteria (spaarke-side-pane-navigation-history-r1
 * spec FR-01 + FR-11):
 *   - Recent/Pinned/Views tab scaffold + persistent search-bar placeholder render
 *   - Renders in light AND dark themes (ADR-021 — Fluent v9 tokens, no hardcoded colors)
 *   - Tab selection switches the active panel
 *   - Portal FluentProvider re-wrap is present on the search-bar info Tooltip
 *     (ADR-021 portal gotcha), and the re-wrapped theme differs between light/dark
 *   - A non-default `--sprk-ui-scale` (Display-size setting) produces a scaled
 *     Fluent theme via the SAME `scaleTheme`/`useUiScale` composition NavigatorBody
 *     wires up (NFR-07)
 *   - No-Xrm surfaces degrade to a safe empty state and never throw (negative
 *     acceptance criterion)
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

  it('render_WithXrm_ShowsRecentPinnedViewsTabsAndSearchBarPlaceholder', async () => {
    renderNavigatorBody();

    expect(screen.getByTestId('navigator-tab-recent')).toHaveTextContent('Recent');
    expect(screen.getByTestId('navigator-tab-pinned')).toHaveTextContent('Pinned');
    expect(screen.getByTestId('navigator-tab-views')).toHaveTextContent('Views');
    expect(screen.getByTestId('navigator-search-placeholder')).toBeInTheDocument();

    // Default active tab is Recent — task 041 wires this panel to <RecentTab>
    // (replacing the task-040 placeholder text for the `recent` tab only).
    // installMockXrm() here has no `Utility`, so RecentTab's load short-circuits
    // to its empty state rather than querying history rows.
    expect(await screen.findByTestId('recent-tab-empty')).toHaveTextContent(
      'Recently viewed records will appear here.'
    );
  });

  it('render_TabClick_SwitchesActivePanel', async () => {
    renderNavigatorBody();
    const user = userEvent.setup();

    await user.click(screen.getByTestId('navigator-tab-pinned'));

    // Task 050 wires the `pinned` panel to <PinnedTab/>. installMockXrm() here
    // has no `Utility`, so PinnedTab's load short-circuits to its empty state
    // (mirrors the `recent`-tab assertion above) rather than the pre-050
    // static placeholder text.
    expect(await screen.findByTestId('pinned-tab-empty')).toHaveTextContent(
      'Pinned records will appear here.'
    );
    expect(screen.queryByTestId('navigator-tab-panel-recent')).not.toBeInTheDocument();

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
});
