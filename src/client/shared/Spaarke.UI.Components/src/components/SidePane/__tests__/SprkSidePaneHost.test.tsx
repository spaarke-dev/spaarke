/**
 * SprkSidePaneHost Component Tests
 *
 * Verifies task 011 acceptance criteria (spaarke-side-pane-navigation-history-r1
 * spec FR-01):
 *   - Renders the active contributor selected from the registry
 *   - Registry add/remove maps to exactly one rail-icon add/remove
 *   - `createPane` is called with `canClose:false` + `alwaysRender:true` (MUST rule / NFR-05)
 *   - Renders in light AND dark themes (ADR-021 — Fluent v9 tokens, no hardcoded colors)
 *   - Portal FluentProvider re-wrap is present on the rail-icon Tooltip (ADR-021 portal gotcha)
 *   - An unknown/unregistered contributor id degrades to a benign empty state, never throws
 *
 * @see ../SprkSidePaneHost.tsx
 * @see ../sidePaneRegistry.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, portal re-wrap, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { SprkSidePaneHost } from '../SprkSidePaneHost';
import { registerSidePaneContributor, clearSidePaneRegistry, type SidePaneContributorProps } from '../sidePaneRegistry';
import { THEME_STORAGE_KEY } from '../../../utils/themeStorage';

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Installs a minimal `window.Xrm.App.sidePanes` mock and returns its spies. */
function installMockXrm() {
  const pane = {
    paneId: 'sprk-sidepane-host',
    navigate: jest.fn().mockResolvedValue(undefined),
    close: jest.fn(),
    select: jest.fn(),
  };
  const createPane = jest.fn().mockResolvedValue(pane);
  const getPane = jest.fn().mockReturnValue(undefined);

  (window as unknown as { Xrm: unknown }).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    App: {
      sidePanes: {
        createPane,
        getPane,
        getAllPanes: jest.fn().mockReturnValue([]),
        getSelectedPane: jest.fn().mockReturnValue(undefined),
      },
    },
  };

  return { createPane, getPane, pane };
}

function StubIcon(label: string): React.ReactElement {
  return <span data-testid={`icon-${label}`}>{label}</span>;
}

function makeContributor(id: string, text: string): React.ComponentType<SidePaneContributorProps> {
  const Contributor: React.FC<SidePaneContributorProps> = ({ paneId }) => (
    <div data-testid={`contributor-${id}`}>
      {text} (pane: {paneId})
    </div>
  );
  Contributor.displayName = `Contributor_${id}`;
  return Contributor;
}

function registerContributor(id: string, title: string, order: number, text = title): void {
  registerSidePaneContributor({
    id,
    title,
    order,
    icon: StubIcon(id),
    component: async () => ({ default: makeContributor(id, text) }),
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('SprkSidePaneHost', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  beforeEach(() => {
    clearSidePaneRegistry();
    localStorage.clear();
    installMockXrm();
  });

  afterEach(() => {
    clearSidePaneRegistry();
    localStorage.clear();
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
    }
  });

  // ───────────────────────────────────────────────────────────────────────
  // Renders active contributor from the registry
  // ───────────────────────────────────────────────────────────────────────

  it('render_PopulatedRegistry_RendersLowestOrderContributorByDefault', async () => {
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);

    render(<SprkSidePaneHost />);

    expect(await screen.findByTestId('contributor-recent')).toBeInTheDocument();
    expect(screen.queryByTestId('contributor-pinned')).not.toBeInTheDocument();
  });

  it('render_RailIconClick_SwitchesActiveContributor', async () => {
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);
    const user = userEvent.setup();

    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');

    await user.click(screen.getByTestId('sprk-sidepane-rail-icon-pinned'));

    expect(await screen.findByTestId('contributor-pinned')).toBeInTheDocument();
    expect(screen.queryByTestId('contributor-recent')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // One registry entry = one rail icon; add/remove = +/-1
  // ───────────────────────────────────────────────────────────────────────

  it('render_TwoEntries_RendersExactlyTwoRailIcons', async () => {
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);

    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');

    const rail = screen.getByTestId('sprk-sidepane-host-rail');
    expect(rail.querySelectorAll('button')).toHaveLength(2);
  });

  it('registry_AddOneEntry_RailIconCountIncreasesByExactlyOne', async () => {
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);

    const { unmount } = render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const before = screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button').length;
    unmount();

    registerContributor('views', 'Views', 3);
    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const after = screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button').length;

    expect(after).toBe(before + 1);
  });

  it('registry_RemoveOneEntry_RailIconCountDecreasesByExactlyOne', async () => {
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);
    registerContributor('views', 'Views', 3);

    const { unmount } = render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const before = screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button').length;
    unmount();

    // Simulate removing one contributor: clear + re-register the remaining N-1
    // (the registry intentionally exposes no per-id unregister — a code-owned
    // static table per WorkspaceWidgetRegistry/surfaceLaunchRegistry precedent).
    clearSidePaneRegistry();
    registerContributor('recent', 'Recent', 1);
    registerContributor('pinned', 'Pinned', 2);

    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const after = screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button').length;

    expect(after).toBe(before - 1);
  });

  it('render_EmptyRegistry_RendersNoRailAndBenignEmptyState', async () => {
    render(<SprkSidePaneHost />);

    expect(screen.queryByTestId('sprk-sidepane-host-rail')).not.toBeInTheDocument();
    expect(await screen.findByTestId('sprk-sidepane-host-empty-state')).toHaveTextContent(
      'No side-pane sections are registered.'
    );
  });

  // ───────────────────────────────────────────────────────────────────────
  // createPane MUST rule (canClose:false + alwaysRender:true, NFR-05)
  // ───────────────────────────────────────────────────────────────────────

  it('mount_CreatesAppPane_WithCanCloseFalseAndAlwaysRenderTrue', async () => {
    registerContributor('recent', 'Recent', 1);
    const { createPane } = installMockXrm();

    render(<SprkSidePaneHost paneId="test-pane" paneTitle="Test Pane" />);
    await screen.findByTestId('contributor-recent');

    await waitFor(() => expect(createPane).toHaveBeenCalled());
    expect(createPane).toHaveBeenCalledWith(
      expect.objectContaining({
        paneId: 'test-pane',
        title: 'Test Pane',
        canClose: false,
        alwaysRender: true,
      })
    );
  });

  it('mount_ManagePaneFalse_DoesNotCreateAPane_ButStillRendersContributor', async () => {
    // Embedded mode (task 086 loop fix): when the pane was already created by
    // the Path B bootstrap and this host is merely its CONTENT, the host MUST
    // NOT create a second pane (that rival pane caused the load/unload loop).
    // It must still render the full UI (rail + active contributor).
    registerContributor('navigator', 'Navigator', 1);
    const { createPane } = installMockXrm();

    render(<SprkSidePaneHost managePane={false} paneId="sprk-navigator" paneTitle="Navigator" />);

    // Contributor renders with the passed-through pane id...
    expect(await screen.findByTestId('contributor-navigator')).toBeInTheDocument();
    expect(screen.getByTestId('contributor-navigator')).toHaveTextContent('pane: sprk-navigator');
    // ...but NO Xrm pane was created by the host.
    await waitFor(() => expect(screen.getByTestId('sprk-sidepane-host')).toBeInTheDocument());
    expect(createPane).not.toHaveBeenCalled();
  });

  it('mount_NoXrmAvailable_DoesNotThrow', async () => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    registerContributor('recent', 'Recent', 1);

    expect(() => render(<SprkSidePaneHost />)).not.toThrow();
    await screen.findByTestId('contributor-recent');
  });

  // ───────────────────────────────────────────────────────────────────────
  // Light + dark theme rendering (ADR-021)
  // ───────────────────────────────────────────────────────────────────────

  it('render_LightAndDarkThemePreference_RendersWithoutErrorAndAppliesDistinctThemeClasses', async () => {
    registerContributor('recent', 'Recent', 1);

    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    const { container: lightContainer, unmount } = render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const lightClassName = lightContainer.querySelector('.fui-FluentProvider')?.className;
    expect(lightClassName).toBeTruthy();
    unmount();

    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    const { container: darkContainer } = render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const darkClassName = darkContainer.querySelector('.fui-FluentProvider')?.className;
    expect(darkClassName).toBeTruthy();

    // Light and dark resolve to different Fluent themes -> different generated
    // theme class names on the root FluentProvider (honors both, ADR-021).
    expect(darkClassName).not.toBe(lightClassName);
  });

  // ───────────────────────────────────────────────────────────────────────
  // ADR-021 portal re-wrap on the rail-icon Tooltip
  // ───────────────────────────────────────────────────────────────────────

  it('render_RailIconTooltipOpen_PortalContentIsReWrappedInFluentProvider', async () => {
    registerContributor('recent', 'Recent', 1);
    const user = userEvent.setup();

    render(<SprkSidePaneHost />);
    const icon = await screen.findByTestId('sprk-sidepane-rail-icon-recent');

    await user.hover(icon);

    // The tooltip content ("Recent") mounts via a Portal; once visible, there
    // must be a SECOND `.fui-FluentProvider` (the explicit re-wrap) in the
    // document beyond the host's own root provider.
    await waitFor(() => {
      const providers = document.querySelectorAll('.fui-FluentProvider');
      expect(providers.length).toBeGreaterThanOrEqual(2);
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Unknown registry id degrades gracefully (negative acceptance criterion)
  // ───────────────────────────────────────────────────────────────────────

  it('render_UnknownDefaultActiveId_RendersBenignEmptyStateAndDoesNotThrow', async () => {
    registerContributor('recent', 'Recent', 1);

    expect(() => render(<SprkSidePaneHost defaultActiveId="does-not-exist" />)).not.toThrow();

    expect(await screen.findByTestId('sprk-sidepane-host-empty-state')).toHaveTextContent(
      'This section is unavailable.'
    );
    // The rail still reflects the real registry (one entry) — the unknown id
    // only affects which content pane renders, not the rail itself.
    expect(screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button')).toHaveLength(1);
  });
});
