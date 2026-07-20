/**
 * ComposeToolbarPolish.test.tsx — task 042 (FR-19/FR-20/FR-21, R3 UAT round-3 carry-in) coverage:
 *
 *   1. FR-19 (DEF-16): the format toolbar stays pinned/visible while the editor document scrolls.
 *   2. FR-20 (DEF-17): the selection AI bubble menu lays out on a single row at default widths.
 *   3. FR-21 (DEF-15): the "opened with N simplification(s)" banner's × dismiss persists for the
 *      SESSION (sessionStorage) — it does not reappear on re-render/remount within the session.
 *   4. ADR-021 dark-mode render check across all three surfaces.
 *
 * FR-19/FR-20's underlying CSS (`position: sticky` on ComposeFormatToolbar; `flexWrap: 'nowrap'` on
 * the AI bubble Toolbar) already shipped in the R2 UAT-round-3 fix (DEF-16/DEF-17) — this file is the
 * carried-in R3 test coverage the task calls for, exercised against the CURRENT component structure
 * (ComposeEditor mounts the toolbar as a flex-column sibling ABOVE the scrollable editor surface, so
 * scrolling the surface structurally cannot move the toolbar — the sticky declaration is the
 * additional embedded-host safety net per the DEF-16 commit note).
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import type { Editor } from '@tiptap/react';
import { ComposeEditor } from './ComposeEditor';
import { ComposeAiToolbar, type ComposeAiToolbarAction } from './ComposeAiToolbar';
import { ComposeBannerStack, type ComposeBannerStackProps } from './ComposeBannerStack';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets/events';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

function renderComposeEditor(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} sessionId="session-toolbar-polish" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Define read-only scroll geometry on an element (jsdom leaves these at 0) — mirrors ComposeEditor.scroll.test.tsx. */
function setScrollGeometry(
  el: HTMLElement,
  geo: { scrollHeight: number; clientHeight: number; scrollTop: number }
): void {
  Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => geo.scrollHeight });
  Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => geo.clientHeight });
  Object.defineProperty(el, 'scrollTop', { configurable: true, writable: true, value: geo.scrollTop });
}

// ---------------------------------------------------------------------------
// 1. FR-19 (DEF-16) — sticky toolbar stays visible during scroll
// ---------------------------------------------------------------------------

describe('FR-19 (DEF-16) — the format toolbar stays pinned/visible while the editor scrolls', () => {
  it('the toolbar is position:sticky/top:0 and is NOT nested inside the scrolling surface (structurally un-scrollable-away)', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');

    const toolbar = screen.getByTestId('compose-format-toolbar');
    const surface = screen.getByTestId('compose-editor-surface');

    // The toolbar renders as a flex-column SIBLING above the scroll region (`editorScrollWrap`),
    // not a descendant of the scrollable surface — scrolling `surface` structurally cannot move it.
    expect(surface.contains(toolbar)).toBe(false);

    const style = getComputedStyle(toolbar);
    expect(style.position).toBe('sticky');
    expect(style.top).toBe('0px');
  });

  it('stays present, visible, and unchanged after the editor surface scrolls (long-document simulation)', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');

    const toolbar = screen.getByTestId('compose-format-toolbar');
    const surface = screen.getByTestId('compose-editor-surface');

    expect(toolbar).toBeVisible();

    // Simulate a long document scrolled several screens down (mirrors ComposeEditor.scroll.test.tsx).
    setScrollGeometry(surface, { scrollHeight: 5000, clientHeight: 400, scrollTop: 3200 });
    act(() => {
      fireEvent.scroll(surface);
    });

    // The toolbar is untouched by the scroll: still mounted, still visible, sticky CSS unchanged.
    expect(screen.getByTestId('compose-format-toolbar')).toBe(toolbar);
    expect(toolbar).toBeVisible();
    const style = getComputedStyle(toolbar);
    expect(style.position).toBe('sticky');
    expect(style.top).toBe('0px');
  });
});

// ---------------------------------------------------------------------------
// 2. FR-20 (DEF-17) — one-line selection bubble menu
// ---------------------------------------------------------------------------

/** Minimal TipTap-`Editor`-shaped mock with a NON-COLLAPSED selection (renders the AI bubble). */
function createSelectionEditor(): Editor {
  const listeners = new Map<string, Set<() => void>>();
  const mock = {
    state: {
      selection: { from: 0, to: 20 },
      doc: { textBetween: () => 'A selected clause of text' },
    },
    on: (event: string, handler: () => void) => {
      const set = listeners.get(event) ?? new Set<() => void>();
      set.add(handler);
      listeners.set(event, set);
      return mock;
    },
    off: (event: string, handler: () => void) => {
      listeners.get(event)?.delete(handler);
      return mock;
    },
  };
  return mock as unknown as Editor;
}

function noopDispatch(): DispatchPaneEvent {
  return jest.fn() as unknown as DispatchPaneEvent;
}

// A representative FULL action set (primary + overflow + email split menu are all always rendered)
// so the one-row assertion covers the busiest realistic layout, not just the stubbed default.
const FULL_TEST_ACTIONS: ComposeAiToolbarAction[] = [
  { id: 'compose-explain-clause', label: 'Explain', tooltip: 'Explain.', bindingId: 'b1', placement: 'primary' },
  {
    id: 'compose-compare-to-playbook',
    label: 'Compare to playbook',
    tooltip: 'Compare.',
    bindingId: 'b2',
    placement: 'primary',
  },
  {
    id: 'compose-draft-alternative',
    label: 'Draft alternative',
    tooltip: 'Draft.',
    bindingId: 'b3',
    placement: 'primary',
    materializesInEditor: true,
  },
  { id: 'compose-defined-terms', label: 'Defined terms', tooltip: 'Scan.', bindingId: 'b4', placement: 'overflow' },
];

function renderAiToolbar(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeAiToolbar
        editor={createSelectionEditor()}
        documentRef={{ speDriveItemId: 'spe-123' }}
        sessionId="session-789"
        bffBaseUrl="https://bff.example.test"
        dispatch={noopDispatch()}
        actions={FULL_TEST_ACTIONS}
      />
    </FluentProvider>
  );
}

describe('FR-20 (DEF-17) — the selection AI bubble menu renders on a single row', () => {
  it('lays out with flex-wrap: nowrap / white-space: nowrap at default widths (no two-row wrap)', () => {
    renderAiToolbar();
    const toolbar = screen.getByTestId('compose-ai-toolbar');
    const style = getComputedStyle(toolbar);
    expect(style.flexWrap).toBe('nowrap');
    expect(style.whiteSpace).toBe('nowrap');
  });

  it('every action (primary + email + overflow trigger) renders inside the SAME single toolbar row', () => {
    renderAiToolbar();
    const toolbar = screen.getByTestId('compose-ai-toolbar');

    // All controls are descendants of the one nowrap Toolbar — nothing renders in a second,
    // sibling row.
    expect(toolbar).toContainElement(screen.getByTestId('compose-ai-toolbar-compose-explain-clause'));
    expect(toolbar).toContainElement(screen.getByTestId('compose-ai-toolbar-compose-compare-to-playbook'));
    expect(toolbar).toContainElement(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));
    expect(toolbar).toContainElement(screen.getByTestId('compose-ai-toolbar-email'));
    expect(toolbar).toContainElement(screen.getByTestId('compose-ai-toolbar-more'));
    expect(screen.getAllByRole('toolbar')).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// 3. FR-21 (DEF-15) — dismiss × persists for the SESSION (sessionStorage)
// ---------------------------------------------------------------------------

function renderBannerStack(overrides: Partial<ComposeBannerStackProps> = {}) {
  const props: ComposeBannerStackProps = {
    errorMessage: null,
    checkoutStatus: 'idle',
    checkoutLockedBy: null,
    checkoutFailureMessage: null,
    importWarnings: [],
    pendingAssistantInsert: null,
    ...overrides,
  };
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeBannerStack {...props} />
    </FluentProvider>
  );
}

const SIMPLIFICATION_WARNINGS = [
  { type: 'formatting', message: 'A complex table was simplified.' },
  { type: 'formatting', message: 'A nested list was flattened.' },
];

describe('FR-21 (DEF-15) — dismissing the simplification banner persists for the session', () => {
  beforeEach(() => {
    // Isolate each test's sessionStorage sentinel — the dismissal key is content-signature-keyed,
    // and this suite reuses `SIMPLIFICATION_WARNINGS` across `it`s.
    window.sessionStorage.clear();
  });

  it('the × closes the banner and it does NOT reappear on a fresh re-render within the same session', async () => {
    const user = userEvent.setup();
    const { unmount } = renderBannerStack({ importWarnings: SIMPLIFICATION_WARNINGS });

    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));
    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();

    // Simulate "re-render the editor within the same session" — unmount and remount an ENTIRELY
    // new component tree with the SAME import warnings (e.g. navigating away and back, or the host
    // re-mounting ComposeWorkspace). Only sessionStorage (not React state) can survive this.
    unmount();
    renderBannerStack({ importWarnings: SIMPLIFICATION_WARNINGS });

    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();
  });

  it('a genuinely different set of import warnings is NOT suppressed by a prior dismissal', async () => {
    const user = userEvent.setup();
    const { unmount } = renderBannerStack({ importWarnings: SIMPLIFICATION_WARNINGS });

    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));
    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();
    unmount();

    const DIFFERENT_WARNINGS = [{ type: 'formatting', message: 'A different document had a footnote dropped.' }];
    renderBannerStack({ importWarnings: DIFFERENT_WARNINGS });

    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(screen.getByText('Document opened with 1 simplification(s)')).toBeInTheDocument();
  });

  it('sessionStorage carries a dismissal sentinel keyed to the warnings content after ×', async () => {
    const user = userEvent.setup();
    renderBannerStack({ importWarnings: SIMPLIFICATION_WARNINGS });

    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));

    const keys = Object.keys(window.sessionStorage).filter(k =>
      k.startsWith('spaarke-compose:import-warnings-dismissed:')
    );
    expect(keys.length).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// 4. ADR-021 dark-mode render check — all three surfaces
// ---------------------------------------------------------------------------

describe('ADR-021 dark mode — toolbar, bubble menu, and simplification banner render with theme tokens', () => {
  it('the format toolbar renders under webDarkTheme with no hardcoded hex color', async () => {
    const { container } = renderComposeEditor(webDarkTheme);
    await screen.findByRole('textbox');
    expect(screen.getByTestId('compose-format-toolbar')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('the AI selection bubble menu renders under webDarkTheme with no hardcoded hex color', () => {
    const { container } = renderAiToolbar(webDarkTheme);
    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('the simplification banner renders under webDarkTheme with no hardcoded hex color', () => {
    window.sessionStorage.clear();
    const { container } = render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={SIMPLIFICATION_WARNINGS}
          pendingAssistantInsert={null}
        />
      </FluentProvider>
    );
    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-import-warning-dismiss')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
