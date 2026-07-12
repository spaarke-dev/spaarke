/**
 * ComposeEditor.aiToolbarTriggers.test.tsx — task 111 (UAT-R2 + scope-change,
 * 2026-07-10) render-level DoD coverage for the inline AI toolbar's TWO mount
 * paths, exercised against the REAL `ComposeEditor` (real TipTap editor, real
 * `<BubbleMenu>`, real `ComposeAiToolbar`) — not a mock/isolation harness.
 *
 * Covers:
 *  1. The persistent top toolbar (`ComposeFormatToolbar`) is untouched and
 *     still reachable (formatting is not lost, just relocated out of the
 *     popup).
 *  2. The right-click (context-menu) trigger: a native `contextmenu` event on
 *     the editor's contenteditable region is suppressed (`preventDefault`)
 *     and opens a SINGLE Fluent Toolbar containing all 5 AI actions + a
 *     Toolbar-contextualized divider — even with NO selection (a collapsed
 *     caret; this is the only selection state reachable without simulating
 *     real browser text-selection in jsdom, which is what the task requires
 *     it to prove anyway: "even when there is NO text selection").
 *  3. The popup contains ONLY AI actions — no bold/italic/underline/strike/
 *     link controls (those never existed in `ComposeFormatToolbar` either;
 *     see that file's own docstring — flagged as a known trade-off in the
 *     task 111 completion report, not silently dropped).
 *
 * The SELECTION-triggered `<BubbleMenu>` popup (mouse-drag-select → popup) is
 * NOT separately exercised here: TipTap's `BubbleMenu` extension shows/hides
 * via a tippy.js popper tied to `view.hasFocus()` + a non-empty ProseMirror
 * selection, neither of which jsdom can produce through real user gestures
 * (no layout engine, no native text-selection injection into a contenteditable
 * ProseMirror view). The right-click path below exercises the SAME rendered
 * composition (`ComposeAiToolbar` as the popup's sole content) through a
 * mechanism jsdom CAN drive end-to-end (a dispatched native `contextmenu`
 * event), which is why task 111 standardized on it for the "mounted +
 * reachable, not merely built in isolation" proof.
 */

import * as React from 'react';
import { render, screen, within, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor } from './ComposeEditor';
import type { Editor } from '@tiptap/react';

// ComposeAiToolbar's `useAuth()` throws outside a real `initAuth()` bootstrap
// (MSAL) — mirrors the identical mock in ComposeAiToolbar.test.tsx. This test
// never dispatches an action (no bindingId is ever wired), so a stub token is
// sufficient.
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

function renderComposeEditor(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} sessionId="session-111" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Locates the actual ProseMirror contenteditable node TipTap mounts. */
function getEditorDom(container: HTMLElement): HTMLElement {
  const dom = container.querySelector('.ProseMirror') as HTMLElement | null;
  if (!dom) throw new Error('ProseMirror editor DOM not found — editor did not mount');
  return dom;
}

/**
 * The real TipTap `Editor` instance is attached by the PM view to its
 * contenteditable DOM node as `.editor` (verified empirically). We use it ONLY
 * to drive a real, non-empty text selection for the dual-popup dedupe test —
 * the production code reads `view.state.selection`, so a genuine selection is
 * the honest way to exercise the dedupe branch (jsdom cannot produce one via
 * mouse gestures).
 */
function getEditorInstance(container: HTMLElement): Editor {
  const dom = getEditorDom(container) as unknown as { editor?: Editor };
  if (!dom.editor) throw new Error('TipTap editor instance not found on ProseMirror DOM node');
  return dom.editor;
}

describe('ComposeEditor — top formatting toolbar owns the relocated inline controls (task 111)', () => {
  it('renders the persistent ComposeFormatToolbar with block controls AND the relocated Bold/Italic/Underline/Strikethrough/Link', async () => {
    const { container } = renderComposeEditor();
    await screen.findByRole('textbox'); // editor mounted

    const formatToolbar = screen.getByTestId('compose-format-toolbar');
    expect(formatToolbar).toBeInTheDocument();
    // Existing block controls (no regression).
    expect(within(formatToolbar).getByTestId('compose-format-bullet-list')).toBeInTheDocument();
    expect(within(formatToolbar).getByTestId('compose-format-undo')).toBeInTheDocument();
    // Relocated inline character-format controls — now fully reachable from the
    // top toolbar (owner decision; task 111).
    expect(within(formatToolbar).getByTestId('compose-format-bold')).toBeInTheDocument();
    expect(within(formatToolbar).getByTestId('compose-format-italic')).toBeInTheDocument();
    expect(within(formatToolbar).getByTestId('compose-format-underline')).toBeInTheDocument();
    expect(within(formatToolbar).getByTestId('compose-format-strike')).toBeInTheDocument();
    expect(within(formatToolbar).getByTestId('compose-format-link')).toBeInTheDocument();
  });
});

describe('ComposeEditor — right-click AI-toolbar trigger (task 111 requirement 2)', () => {
  it('suppresses the native context menu and opens a single AI-actions-only Toolbar at the click point, with NO selection', async () => {
    const { container } = renderComposeEditor();
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    // No popup yet.
    expect(screen.queryByTestId('compose-ai-context-menu')).not.toBeInTheDocument();

    const contextMenuEvent = new MouseEvent('contextmenu', {
      bubbles: true,
      cancelable: true,
      clientX: 123,
      clientY: 45,
    });
    fireEvent(editorDom, contextMenuEvent);

    // The native browser context menu was suppressed.
    expect(contextMenuEvent.defaultPrevented).toBe(true);

    const popup = await screen.findByTestId('compose-ai-context-menu');
    expect(popup).toBeInTheDocument();
    expect(popup).toHaveStyle({ left: '123px', top: '45px' });

    // ---- ONE Toolbar, AI-actions-only, divider in-context, no overflow ----
    const toolbars = within(popup).getAllByRole('toolbar');
    expect(toolbars).toHaveLength(1);
    const toolbar = toolbars[0];
    expect(toolbar).toHaveAttribute('data-testid', 'compose-ai-toolbar');

    // All 5 AI actions present (3 primary buttons + overflow trigger; the 2
    // overflow actions are behind "More actions").
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-explain-clause')).toBeInTheDocument();
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-compare-to-playbook')).toBeInTheDocument();
    expect(within(toolbar).getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
    expect(within(toolbar).getByTestId('compose-ai-toolbar-more')).toBeInTheDocument();

    await fireEvent.click(within(toolbar).getByTestId('compose-ai-toolbar-more'));
    expect(await screen.findByTestId('compose-ai-toolbar-overflow-compose-summarize-word-changes')).toBeInTheDocument();
    expect(screen.getByTestId('compose-ai-toolbar-overflow-compose-defined-terms')).toBeInTheDocument();

    // Divider is a child of the (single) Toolbar — has Toolbar context, not orphaned.
    const divider = toolbar.querySelector('[role="separator"]');
    expect(divider).not.toBeNull();
    expect(toolbar.contains(divider)).toBe(true);

    // AI-actions ONLY — no formatting controls leaked into the popup.
    expect(within(popup).queryByLabelText('Bold')).not.toBeInTheDocument();
    expect(within(popup).queryByLabelText('Italic')).not.toBeInTheDocument();
    expect(within(popup).queryByLabelText('Underline')).not.toBeInTheDocument();
    expect(within(popup).queryByLabelText('Strikethrough')).not.toBeInTheDocument();
    expect(within(popup).queryByLabelText('Add link')).not.toBeInTheDocument();

    // DEF-17 (UAT-R3): the AI bubble renders on a SINGLE line (flex-wrap:
    // nowrap); overflow actions go to the ⋯ menu, not a second row (real
    // Griffel-injected CSS).
    expect(getComputedStyle(toolbar).flexWrap).toBe('nowrap');
  });

  it('dismisses on Escape and on an outside click', async () => {
    const { container } = renderComposeEditor();
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 10, clientY: 10 });
    expect(await screen.findByTestId('compose-ai-context-menu')).toBeInTheDocument();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByTestId('compose-ai-context-menu')).not.toBeInTheDocument();

    fireEvent.contextMenu(editorDom, { clientX: 10, clientY: 10 });
    expect(await screen.findByTestId('compose-ai-context-menu')).toBeInTheDocument();
    fireEvent.mouseDown(document.body);
    expect(screen.queryByTestId('compose-ai-context-menu')).not.toBeInTheDocument();
  });

  it('dark mode (ADR-021): the context-menu popup renders with no hardcoded hex color', async () => {
    const { container } = renderComposeEditor(webDarkTheme);
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);

    fireEvent.contextMenu(editorDom, { clientX: 5, clientY: 5 });
    const popup = await screen.findByTestId('compose-ai-context-menu');
    expect(popup).toBeInTheDocument();
    expect(popup.outerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

describe('ComposeEditor — dual-popup dedupe (task 111 requirement 3)', () => {
  it('does NOT open the right-click popup when a NON-EMPTY selection is active (the BubbleMenu is the sole popup)', async () => {
    const { container } = renderComposeEditor();
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);
    const editor = getEditorInstance(container);

    // Drive a real, non-empty text selection (the state the selection BubbleMenu
    // shows on). `act` flushes the selectionUpdate-driven re-renders.
    act(() => {
      editor.commands.setContent('<p>Hello world here</p>');
      editor.commands.setTextSelection({ from: 1, to: 6 });
    });
    // Sanity: the selection really is non-empty.
    expect(editor.state.selection.from).not.toBe(editor.state.selection.to);

    const contextMenuEvent = new MouseEvent('contextmenu', {
      bubbles: true,
      cancelable: true,
      clientX: 50,
      clientY: 50,
    });
    fireEvent(editorDom, contextMenuEvent);

    // Native menu still suppressed (we always preventDefault)...
    expect(contextMenuEvent.defaultPrevented).toBe(true);
    // ...but NO second (right-click) popup — dedupe: only one popup can show.
    expect(screen.queryByTestId('compose-ai-context-menu')).not.toBeInTheDocument();
  });

  it('opens the right-click popup when the selection is COLLAPSED (BubbleMenu not showing) — the non-dedupe path', async () => {
    const { container } = renderComposeEditor();
    await screen.findByRole('textbox');
    const editorDom = getEditorDom(container);
    const editor = getEditorInstance(container);

    // Collapse the selection to a caret.
    act(() => {
      editor.commands.setContent('<p>Hello world here</p>');
      editor.commands.setTextSelection({ from: 3, to: 3 });
    });
    expect(editor.state.selection.from).toBe(editor.state.selection.to);

    fireEvent.contextMenu(editorDom, { clientX: 20, clientY: 20 });
    expect(await screen.findByTestId('compose-ai-context-menu')).toBeInTheDocument();
  });
});
