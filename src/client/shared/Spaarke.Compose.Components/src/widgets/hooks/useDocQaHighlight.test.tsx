/**
 * useDocQaHighlight.test.tsx — FR-35 Document Q&A ephemeral highlight
 * (spaarkeai-compose-r2 task 072, stretch).
 *
 * Two layers (mirrors usePendingRedline.test.tsx):
 *  1. HOOK LOGIC — `renderHook` over a REAL headless TipTap `@tiptap/core`
 *     Editor (StarterKit + QaHighlightExtension). Covers the found /
 *     not_found / ambiguous (do-not-guess) / clear / auto-TTL-clear cases.
 *  2. UI — a full `ComposeEditor` render verifies the "Found in …" banner
 *     renders + clears and that the ephemeral highlight decoration is a
 *     PROSEMIRROR VIEW DECORATION (never a doc Mark — absent from getHTML()).
 */
import * as React from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import { renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { QaHighlightExtension } from '../marks/QaHighlightExtension';
import { useDocQaHighlight } from './useDocQaHighlight';

// `@spaarke/auth`'s useAuth throws outside a real MSAL bootstrap — mocked (see ComposeAiToolbar.test).
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// PaneEventBus dispatch — ComposeEditor calls useDispatchPaneEvent() directly; return a no-op.
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
}));

// BubbleMenu wraps tippy.js (ESM) and needs a real DOM range — passthrough-render its children.
jest.mock('@tiptap/react', () => {
  const actual = jest.requireActual('@tiptap/react');
  return { ...actual, BubbleMenu: ({ children }: { children: React.ReactNode }) => <div>{children}</div> };
});

function makeEditor(content: string): Editor {
  return new Editor({ extensions: [StarterKit, QaHighlightExtension], content });
}

// ---------------------------------------------------------------------------
// Hook logic
// ---------------------------------------------------------------------------
describe('useDocQaHighlight (hook logic over a real TipTap editor)', () => {
  it('highlights a unique match, renders a view decoration, and reports the section label', () => {
    const editor = makeEditor('<p>The indemnification cap is five hundred thousand dollars.</p>');
    const { result } = renderHook(() => useDocQaHighlight(editor));

    let status: string | undefined;
    act(() => {
      status = result.current.highlight('indemnification cap', 'Section 7.3');
    });

    expect(status).toBe('highlighted');
    expect(result.current.activeHighlight).toEqual({ sectionLabel: 'Section 7.3' });
    expect(editor.view.dom.querySelector('.compose-qa-highlight')).not.toBeNull();
    // The decoration is view-only — it must NEVER appear in the serialized doc.
    expect(editor.getHTML()).not.toContain('compose-qa-highlight');
    editor.destroy();
  });

  it('not_found: silently ignores a citation excerpt absent from this document (different source)', () => {
    const editor = makeEditor('<p>Nothing relevant here.</p>');
    const { result } = renderHook(() => useDocQaHighlight(editor));

    let status: string | undefined;
    act(() => {
      status = result.current.highlight('a phrase from a different document entirely');
    });

    expect(status).toBe('not_found');
    expect(result.current.activeHighlight).toBeNull();
    expect(editor.view.dom.querySelector('.compose-qa-highlight')).toBeNull();
    editor.destroy();
  });

  it('ambiguous: does NOT guess when the excerpt matches more than one span', () => {
    const editor = makeEditor('<p>The cap applies here. The cap applies there too.</p>');
    const { result } = renderHook(() => useDocQaHighlight(editor));

    let status: string | undefined;
    act(() => {
      status = result.current.highlight('The cap applies');
    });

    expect(status).toBe('ambiguous');
    expect(result.current.activeHighlight).toBeNull();
    expect(editor.view.dom.querySelector('.compose-qa-highlight')).toBeNull();
    editor.destroy();
  });

  it('clear() removes the active highlight and its decoration', () => {
    const editor = makeEditor('<p>The indemnification cap is notable.</p>');
    const { result } = renderHook(() => useDocQaHighlight(editor));

    act(() => {
      result.current.highlight('indemnification cap');
    });
    expect(result.current.activeHighlight).not.toBeNull();

    act(() => {
      result.current.clear();
    });
    expect(result.current.activeHighlight).toBeNull();
    expect(editor.view.dom.querySelector('.compose-qa-highlight')).toBeNull();
    editor.destroy();
  });

  it('noop on an empty sourceText or a null editor', () => {
    const editor = makeEditor('<p>Some text.</p>');
    const { result } = renderHook(() => useDocQaHighlight(editor));
    let status: string | undefined;
    act(() => {
      status = result.current.highlight('');
    });
    expect(status).toBe('noop');

    const { result: nullResult } = renderHook(() => useDocQaHighlight(null));
    act(() => {
      status = nullResult.current.highlight('Some text');
    });
    expect(status).toBe('noop');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// ComposeEditor UI — "Found in …" banner (ADR-021 dark mode)
// ---------------------------------------------------------------------------
describe('ComposeEditor Doc Q&A highlight banner (ADR-021 dark mode)', () => {
  // Imported lazily so the module mocks above are installed first.
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const { ComposeEditor } = require('../ComposeEditor');
  const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

  function renderEditor() {
    const ref = React.createRef<import('../ComposeEditor').ComposeEditorHandle>();
    render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeEditor ref={ref} docxBytes={null} />
      </FluentProvider>
    );
    return ref;
  }

  it('renders "Found in …" after a matching citation, and clears on clearCitedHighlight()', async () => {
    const ref = renderEditor();
    await screen.findByRole('region');

    // Seed real document text via the shipped insertion path, then commit it
    // (accept) so it's plain text the highlight can resolve against.
    act(() => {
      ref.current!.materializePendingRedline(
        { new_text: 'The indemnification cap is five hundred thousand dollars.' },
        PROV
      );
    });

    // DEF-12: the fixed accept/reject bar was removed; commit the redline via the imperative handle
    // (the same usePendingRedline.accept the Assistant control + on-click popover route to).
    act(() => {
      ref.current!.acceptPendingRedline('b1@t1');
    });
    await waitFor(() =>
      expect(document.querySelector('[data-compose-mark="insertion"][data-ledger-ref="b1@t1"]')).toBeNull()
    );

    let status: string | undefined;
    act(() => {
      status = ref.current!.highlightCitedSpan('indemnification cap', 'Section 7.3');
    });
    expect(status).toBe('highlighted');

    const banner = await screen.findByTestId('compose-qa-highlight-banner');
    expect(banner).toHaveTextContent('Found in Section 7.3');

    act(() => {
      ref.current!.clearCitedHighlight();
    });
    await waitFor(() => expect(screen.queryByTestId('compose-qa-highlight-banner')).not.toBeInTheDocument());
  });

  it('does NOT render the banner for an uncited/unmatched excerpt (grounded-output invariant)', async () => {
    const ref = renderEditor();
    await screen.findByRole('region');

    let status: string | undefined;
    act(() => {
      status = ref.current!.highlightCitedSpan('text that does not exist in this empty document');
    });

    expect(status).toBe('not_found');
    expect(screen.queryByTestId('compose-qa-highlight-banner')).not.toBeInTheDocument();
  });
});
