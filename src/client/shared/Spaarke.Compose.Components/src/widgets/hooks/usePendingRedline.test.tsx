/**
 * usePendingRedline.test.tsx — FR-16 pending track-change materialization (task 033).
 *
 * Two layers:
 *  1. HOOK LOGIC — driven through `renderHook` over a REAL headless TipTap `@tiptap/core` Editor
 *     (same schema-registration path ComposeEditor uses: StarterKit + the three FR-15 marks).
 *     Covers materialize-from-ledger (insertion/deletion pair + `{bindingId}@t{n}` provenance),
 *     the FR-19 "do not guess" ambiguity/not-found rule, match modes, accept/reject, supersession,
 *     idempotency, and the refresh-durability property.
 *  2. UI — a full `ComposeEditor` render (BubbleMenu passthrough-mocked so tippy never loads)
 *     verifies the accept/reject affordances + unresolved-target banner render and wire to the
 *     hook, in dark theme (ADR-021).
 */
import * as React from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderHook } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from '../marks/InsertionMark';
import { DeletionMark } from '../marks/DeletionMark';
import { CommentAnchorMark } from '../marks/CommentAnchorMark';
import { usePendingRedline, resolveTargetSpans } from './usePendingRedline';

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
// `virtual` because the `/events` subpath is not resolvable under jest's node resolution (the
// sibling ComposeAiToolbar.test only ever imports it type-only, so it never hit this).
jest.mock(
  '@spaarke/ai-widgets/events',
  () => ({
    useDispatchPaneEvent: () => jest.fn(),
  }),
  { virtual: true }
);

// BubbleMenu wraps tippy.js (ESM, not in transformIgnorePatterns) and needs a real DOM range —
// passthrough-render its children so the AI toolbar mounts without tippy. useEditor/EditorContent
// stay REAL (requireActual).
jest.mock('@tiptap/react', () => {
  const actual = jest.requireActual('@tiptap/react');
  return { ...actual, BubbleMenu: ({ children }: { children: React.ReactNode }) => <div>{children}</div> };
});

function makeEditor(content = '<p>The quick brown fox jumps.</p>'): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark],
    content,
  });
}

const PROV = { ledgerRef: 'b1@t1', bindingId: 'b1', turn: 1 };

// ---------------------------------------------------------------------------
// resolveTargetSpans — the FR-19 match-mode contract (pure)
// ---------------------------------------------------------------------------
describe('resolveTargetSpans (match_mode contract)', () => {
  it('strict: resolves a unique occurrence', () => {
    const editor = makeEditor('<p>alpha beta gamma</p>');
    const r = resolveTargetSpans(editor, 'beta', 'strict');
    expect(r.ok).toBe(true);
    editor.destroy();
  });

  it('strict: AMBIGUOUS when the target occurs more than once (does not guess)', () => {
    const editor = makeEditor('<p>repeat and repeat again</p>');
    const r = resolveTargetSpans(editor, 'repeat', 'strict');
    expect(r).toMatchObject({ ok: false, kind: 'ambiguous', matchCount: 2 });
    editor.destroy();
  });

  it('not_found when the target is absent', () => {
    const editor = makeEditor('<p>nothing here</p>');
    const r = resolveTargetSpans(editor, 'absent', 'strict');
    expect(r).toMatchObject({ ok: false, kind: 'not_found' });
    editor.destroy();
  });

  it('first: takes the first of several matches', () => {
    const editor = makeEditor('<p>x here and x there</p>');
    const r = resolveTargetSpans(editor, 'x', 'first');
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.spans).toHaveLength(1);
    editor.destroy();
  });

  it('all: returns every match', () => {
    const editor = makeEditor('<p>x here and x there</p>');
    const r = resolveTargetSpans(editor, 'x', 'all');
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.spans).toHaveLength(2);
    editor.destroy();
  });

  it('does not match across a paragraph boundary (block sentinel)', () => {
    const editor = makeEditor('<p>foo</p><p>bar</p>');
    // "foobar" would only match if blocks were concatenated without a separator.
    const r = resolveTargetSpans(editor, 'foobar', 'first');
    expect(r.ok).toBe(false);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// usePendingRedline — hook logic
// ---------------------------------------------------------------------------
describe('usePendingRedline (materialize from ledger)', () => {
  it('materializes a pending insertion/deletion pair tagged {bindingId}@t{n}', () => {
    const editor = makeEditor('<p>The quick brown fox.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'quick', new_text: 'nimble', match_mode: 'strict' }, PROV);
    });

    expect(status).toBe('applied');
    const html = editor.getHTML();
    // deletion half over the target, insertion half carrying the alternative — both with provenance.
    expect(html).toContain('data-compose-mark="deletion"');
    expect(html).toContain('data-compose-mark="insertion"');
    expect(html).toContain('data-ledger-ref="b1@t1"');
    expect(html).toContain('data-binding="b1"');
    expect(html).toContain('nimble');
    expect(result.current.pending).toHaveLength(1);
    expect(result.current.pending[0]).toMatchObject({ ledgerRef: 'b1@t1', hasDeletion: true });
    editor.destroy();
  });

  it('all mode: replaces EVERY occurrence (accept leaves the alternative at each site)', () => {
    const editor = makeEditor('<p>fee then fee again</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ target_text: 'fee', new_text: 'fie', match_mode: 'all' }, PROV);
    });
    act(() => {
      result.current.accept('b1@t1');
    });

    const text = editor.getText();
    expect(text).not.toContain('fee');
    // Both occurrences replaced by the alternative.
    expect(text.match(/fie/g)).toHaveLength(2);
    editor.destroy();
  });

  it('insertion-style draft (no target_text) renders a pending insertion, no deletion', () => {
    const editor = makeEditor('<p>Intro.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ new_text: 'Appended clause.' }, PROV);
    });

    const html = editor.getHTML();
    expect(html).toContain('data-compose-mark="insertion"');
    expect(html).not.toContain('data-compose-mark="deletion"');
    expect(result.current.pending[0]).toMatchObject({ hasDeletion: false });
    editor.destroy();
  });

  it('AMBIGUOUS target surfaces an error and renders NOTHING (FR-19 do-not-guess)', () => {
    const editor = makeEditor('<p>term and term</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'term', new_text: 'x', match_mode: 'strict' }, PROV);
    });

    expect(status).toBe('ambiguous');
    expect(result.current.error).toMatchObject({ kind: 'ambiguous', matchCount: 2 });
    expect(result.current.pending).toHaveLength(0);
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    editor.destroy();
  });

  it('accept commits: keeps the alternative, removes the struck original', () => {
    const editor = makeEditor('<p>The quick fox.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });
    act(() => {
      result.current.accept('b1@t1');
    });

    const text = editor.getText();
    expect(text).toContain('nimble');
    expect(text).not.toContain('quick');
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('reject reverts: removes the alternative, restores the original', () => {
    const editor = makeEditor('<p>The quick fox.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });
    act(() => {
      result.current.reject('b1@t1');
    });

    const text = editor.getText();
    expect(text).toContain('quick');
    expect(text).not.toContain('nimble');
    expect(editor.getHTML()).not.toContain('data-compose-mark');
    expect(result.current.pending).toHaveLength(0);
    editor.destroy();
  });

  it('supersession: a newer output for the same binding removes the prior pending redline', () => {
    const editor = makeEditor('<p>The quick fox.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });
    act(() => {
      result.current.materialize(
        { target_text: 'quick', new_text: 'swift' },
        { ledgerRef: 'b1@t2', bindingId: 'b1', turn: 2 }
      );
    });

    const html = editor.getHTML();
    expect(html).toContain('data-ledger-ref="b1@t2"');
    expect(html).not.toContain('data-ledger-ref="b1@t1"'); // superseded — not left rendered
    expect(html).toContain('swift');
    expect(html).not.toContain('nimble');
    expect(result.current.pending).toHaveLength(1);
    expect(result.current.pending[0].ledgerRef).toBe('b1@t2');
    editor.destroy();
  });

  it('idempotent: re-materializing the same output already present is a no-op', () => {
    const editor = makeEditor('<p>The quick fox.</p>');
    const { result } = renderHook(() => usePendingRedline(editor));

    act(() => {
      result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });
    const afterFirst = editor.getHTML();

    let status: string | undefined;
    act(() => {
      status = result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });

    expect(status).toBe('already_present');
    expect(editor.getHTML()).toBe(afterFirst); // not double-applied
    expect(result.current.pending).toHaveLength(1);
    editor.destroy();
  });

  it('refresh-durability: re-materializing from the ledger into a freshly reloaded doc re-applies the redline', () => {
    // Simulates a page refresh: the document is reloaded CLEAN from SPE (no marks), then
    // ComposeWorkspace re-materializes the CURRENT compose output from the durable ledger.
    const reloaded = makeEditor('<p>The quick fox.</p>');
    const { result } = renderHook(() => usePendingRedline(reloaded));

    act(() => {
      result.current.materialize({ target_text: 'quick', new_text: 'nimble' }, PROV);
    });

    const html = reloaded.getHTML();
    expect(html).toContain('data-compose-mark="insertion"');
    expect(html).toContain('data-compose-mark="deletion"');
    expect(html).toContain('data-ledger-ref="b1@t1"');
    expect(result.current.pending).toHaveLength(1);
    reloaded.destroy();
  });
});

// ---------------------------------------------------------------------------
// ComposeEditor pending-redline UI (dark mode; controls render + wire)
// ---------------------------------------------------------------------------
describe('ComposeEditor pending-redline affordances (ADR-021 dark mode)', () => {
  // Imported lazily so the module mocks above are installed first.
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const { ComposeEditor } = require('../ComposeEditor');

  function renderEditor() {
    const ref = React.createRef<import('../ComposeEditor').ComposeEditorHandle>();
    render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeEditor ref={ref} docxBytes={null} />
      </FluentProvider>
    );
    return ref;
  }

  it('renders accept/reject controls after a draft is materialized, and reject clears them', async () => {
    const ref = renderEditor();
    await screen.findByRole('region'); // editor mounted (loading branch gone)

    act(() => {
      ref.current!.materializePendingRedline({ new_text: 'A suggested paragraph.' }, PROV);
    });

    const controls = await screen.findByTestId('compose-redline-controls');
    expect(controls).toBeInTheDocument();
    const acceptBtn = screen.getByTestId('compose-redline-accept-b1@t1');
    expect(acceptBtn).toBeInTheDocument();

    await userEvent.click(screen.getByTestId('compose-redline-reject-b1@t1'));
    await waitFor(() => expect(screen.queryByTestId('compose-redline-controls')).not.toBeInTheDocument());
  });

  it('surfaces the unresolved-target banner (do-not-guess) when target_text is absent from the doc', async () => {
    const ref = renderEditor();
    await screen.findByRole('region');

    act(() => {
      ref.current!.materializePendingRedline(
        { target_text: 'a phrase that is not in this document', new_text: 'x', match_mode: 'strict' },
        PROV
      );
    });

    const banner = await screen.findByTestId('compose-redline-error');
    expect(banner).toHaveTextContent(/not found/i);
    // Nothing was rendered as a pending suggestion.
    expect(screen.queryByTestId('compose-redline-controls')).not.toBeInTheDocument();
  });
});
