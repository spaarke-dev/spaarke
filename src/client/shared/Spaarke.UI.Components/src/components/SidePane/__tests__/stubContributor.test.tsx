/**
 * stubContributor.test.tsx — FR-13 / Success Criterion 11 framework proof
 * (spaarke-side-pane-navigation-history-r1 task 085).
 *
 * Proves `SprkSidePaneHost` extends by REGISTRATION ONLY: a second, throwaway
 * `StubContributor` (see `../__stub__/StubContributor.tsx`) is registered
 * here — in this test file, NOT at module load — supplying only
 * { id, icon, title, component } (+ the mandatory `order` sort key every
 * registry entry carries). No host code (`SprkSidePaneHost.tsx`) is touched
 * to make this work; that absence IS the proof (see task notes for the
 * `git diff --stat` confirmation).
 *
 * Registration lives in `beforeEach`/cleanup in `afterEach` via
 * `clearSidePaneRegistry()` (mirroring `SprkSidePaneHost.test.tsx`) so the
 * global registry singleton never leaks the stub into another test file or,
 * more importantly, into the production `NavigatorPane` bundle — which never
 * imports `__stub__/StubContributor.tsx` in the first place.
 *
 * @see ../__stub__/StubContributor.tsx
 * @see ../SprkSidePaneHost.tsx (host — verified unmodified by this proof)
 * @see ../sidePaneRegistry.ts
 * @see ../__tests__/SprkSidePaneHost.test.tsx (sibling suite this mirrors)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { SprkSidePaneHost } from '../SprkSidePaneHost';
import { registerSidePaneContributor, clearSidePaneRegistry } from '../sidePaneRegistry';
import {
  STUB_CONTRIBUTOR_ID,
  STUB_CONTRIBUTOR_BODY_TEXT,
  STUB_SIDE_PANE_REGISTRY_ENTRY,
} from '../__stub__/StubContributor';
import { THEME_STORAGE_KEY } from '../../../utils/themeStorage';

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Installs a minimal `window.Xrm.App.sidePanes` mock (mirrors sibling suite). */
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

function makeContributor(id: string, text: string): React.ComponentType<{ paneId: string }> {
  const Contributor: React.FC<{ paneId: string }> = ({ paneId }) => (
    <div data-testid={`contributor-${id}`}>
      {text} (pane: {paneId})
    </div>
  );
  Contributor.displayName = `Contributor_${id}`;
  return Contributor;
}

/** Registers an ordinary (non-stub) contributor — a control alongside the stub. */
function registerOrdinaryContributor(id: string, title: string, order: number): void {
  registerSidePaneContributor({
    id,
    title,
    order,
    icon: StubIcon(id),
    component: async () => ({ default: makeContributor(id, title) }),
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('FR-13 framework proof — stub contributor (registration-only extension)', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  beforeEach(() => {
    clearSidePaneRegistry();
    localStorage.clear();
    installMockXrm();
  });

  afterEach(() => {
    // Leaves the global registry singleton clean — the stub must never
    // survive past this test file (production NavigatorPane bundle never
    // imports it either, so this is belt-and-suspenders).
    clearSidePaneRegistry();
    localStorage.clear();
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
    }
  });

  // ───────────────────────────────────────────────────────────────────────
  // (a) Stub gets its own rail icon and renders when registered
  // ───────────────────────────────────────────────────────────────────────

  it('register_StubEntry_GetsOwnRailIconAndRendersOwnComponent', async () => {
    registerOrdinaryContributor('recent', 'Recent', 1);
    registerSidePaneContributor(STUB_SIDE_PANE_REGISTRY_ENTRY);
    const user = userEvent.setup();

    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');

    // The stub has its own rail icon, distinct from the ordinary contributor's.
    const stubIcon = screen.getByTestId(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);
    expect(stubIcon).toBeInTheDocument();

    await user.click(stubIcon);

    expect(await screen.findByTestId('fr13-stub-contributor-body')).toHaveTextContent(STUB_CONTRIBUTOR_BODY_TEXT);
    expect(screen.queryByTestId('contributor-recent')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // The stub needs ONLY { id, icon, title, component } (+ mandatory `order`)
  // ───────────────────────────────────────────────────────────────────────

  it('register_StubDescriptor_HasOnlyIdIconTitleOrderComponentFields', () => {
    // Documents + enforces the closed field set at the type level: any extra
    // key here would be a TS excess-property error on a literal, and any
    // missing required key would fail to satisfy `SidePaneRegistryEntry`.
    const keys = Object.keys(STUB_SIDE_PANE_REGISTRY_ENTRY).sort();
    expect(keys).toEqual(['component', 'icon', 'id', 'order', 'title']);
  });

  // ───────────────────────────────────────────────────────────────────────
  // (b) Removing the stub removes exactly its rail icon (no others)
  // ───────────────────────────────────────────────────────────────────────

  it('unregister_StubEntry_RemovesExactlyStubRailIconNoOthers', async () => {
    registerOrdinaryContributor('recent', 'Recent', 1);
    registerOrdinaryContributor('pinned', 'Pinned', 2);
    registerSidePaneContributor(STUB_SIDE_PANE_REGISTRY_ENTRY);

    const { unmount } = render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const beforeIds = Array.from(
      screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button[data-testid]')
    ).map(el => el.getAttribute('data-testid'));
    expect(beforeIds).toHaveLength(3);
    expect(beforeIds).toContain(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);
    unmount();

    // Remove ONLY the stub — re-register the two ordinary contributors, leave
    // the stub out (the registry is a code-owned static table with no
    // per-id unregister; clear + re-register the survivors is the documented
    // pattern, mirroring SprkSidePaneHost.test.tsx's own remove-entry case).
    clearSidePaneRegistry();
    registerOrdinaryContributor('recent', 'Recent', 1);
    registerOrdinaryContributor('pinned', 'Pinned', 2);

    render(<SprkSidePaneHost />);
    await screen.findByTestId('contributor-recent');
    const afterIds = Array.from(
      screen.getByTestId('sprk-sidepane-host-rail').querySelectorAll('button[data-testid]')
    ).map(el => el.getAttribute('data-testid'));

    expect(afterIds).toHaveLength(2);
    expect(afterIds).not.toContain(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);
    // Exactly the stub's icon is gone — both ordinary icons remain.
    expect(afterIds).toEqual(
      expect.arrayContaining(['sprk-sidepane-rail-icon-recent', 'sprk-sidepane-rail-icon-pinned'])
    );
  });

  // ───────────────────────────────────────────────────────────────────────
  // (c) Light AND dark theme render, with portal FluentProvider re-wrap
  // ───────────────────────────────────────────────────────────────────────

  it('render_StubRegisteredLightAndDarkTheme_RendersWithoutErrorAndAppliesDistinctThemeClasses', async () => {
    registerSidePaneContributor(STUB_SIDE_PANE_REGISTRY_ENTRY);

    localStorage.setItem(THEME_STORAGE_KEY, 'light');
    const { container: lightContainer, unmount } = render(<SprkSidePaneHost />);
    await screen.findByTestId(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);
    const lightClassName = lightContainer.querySelector('.fui-FluentProvider')?.className;
    expect(lightClassName).toBeTruthy();
    unmount();

    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    const { container: darkContainer } = render(<SprkSidePaneHost />);
    await screen.findByTestId(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);
    const darkClassName = darkContainer.querySelector('.fui-FluentProvider')?.className;
    expect(darkClassName).toBeTruthy();

    expect(darkClassName).not.toBe(lightClassName);
  });

  it('render_StubRailIconTooltipOpen_PortalContentIsReWrappedInFluentProvider', async () => {
    registerSidePaneContributor(STUB_SIDE_PANE_REGISTRY_ENTRY);
    const user = userEvent.setup();

    render(<SprkSidePaneHost />);
    const icon = await screen.findByTestId(`sprk-sidepane-rail-icon-${STUB_CONTRIBUTOR_ID}`);

    await user.hover(icon);

    // The tooltip content ("FR-13 Stub") mounts via a Portal; once visible,
    // there must be a SECOND `.fui-FluentProvider` (the explicit re-wrap the
    // host already provides — unmodified by this proof) beyond the root.
    await waitFor(() => {
      const providers = document.querySelectorAll('.fui-FluentProvider');
      expect(providers.length).toBeGreaterThanOrEqual(2);
    });
  });
});
