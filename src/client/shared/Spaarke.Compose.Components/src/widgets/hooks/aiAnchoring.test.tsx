/**
 * aiAnchoring.test.tsx — Success Criterion 3 automated proof (spaarkeai-compose-r4 task 042).
 *
 * "AI drift-proof — generation with concurrent edits lands at the rebased selection; bad anchors
 * refused. Verify: automated ProseMirror test + UAT on CIPO" (spec + design §10). This is the
 * automated half, exercising task 040's capture path (`useAiGenerateBookmark`) and task 041's
 * apply/validate path (`useAiApplyValidation`) END-TO-END over a REAL headless `@tiptap/core`
 * Editor — no mocked transport, no live network (ADR-038 / NFR-06).
 *
 * NOT duplicated from the sibling suites:
 *  - `useAiGenerateBookmark.test.tsx` (040) already proves the BOOKMARK rebases through a
 *    concurrent edit (`resolveBookmark`/`resolveOnReturn` position assertions) and the FR-07
 *    task-041 wiring (an 'operations' result reaches `aiApplyValidation.validateAndApply`) + a
 *    generic dark-mode render of a hand-built review item.
 *  - `useAiApplyValidation.test.ts` (041) already proves the pure validator/applier/fuzzy-fallback
 *    unit behavior in isolation.
 *  This file's job is the CROSS-CUTTING, END-TO-END proof spec Success Criterion 3 names
 *  explicitly: concurrent-edit generation → REBASED-selection landing (checked against the actual
 *  resulting DOCUMENT TEXT, not just a resolved position), and a bad/out-of-range anchor refused
 *  (checked end-to-end through the SAME two hooks together) — plus a dark-mode render of the
 *  review UI produced by THIS test's own real pipeline output (not a hand-built stub).
 *
 * File extension is `.tsx` (not the POML-listed `.ts`) because the ADR-021 dark-mode assertion
 * renders `ComposeAiToolbar` via RTL, which requires JSX — documented deviation, see
 * projects/spaarkeai-compose-r4/notes/ (task 042 completion notes).
 */
import * as React from 'react';
import { render, screen, renderHook, act } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import type { Editor as ReactEditor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { useAiGenerateBookmark } from './useAiGenerateBookmark';
import { useAiApplyValidation } from './useAiApplyValidation';
import { ComposeAiToolbar, __resetComposeAiToolbarActionsForTests } from '../ComposeAiToolbar';
import type { ComposeOperation } from '../../types/compose-operations';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets/events';

// Same mocking convention as the sibling suites — @spaarke/auth throws outside a real MSAL
// bootstrap; @spaarke/ui-components resolves to a dist/ this worktree hasn't rebuilt.
jest.mock('@spaarke/ui-components', () => ({
  createConsumerDispatcher: () => async () => ({ streamId: 'stub', status: 'complete' }),
}));
jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

const PARA_ID = '0000AAAA';
const SENTENCE = 'The quick brown fox jumps over the lazy dog.';

function makeEditor(): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID],
    content: `<p data-paraid="${PARA_ID}">${SENTENCE}</p>`,
  });
}

function posOf(editor: Editor, sub: string): { from: number; to: number } {
  const at = editor.state.doc.textContent.indexOf(sub);
  return { from: at + 1, to: at + 1 + sub.length };
}

function selectPhrase(editor: Editor, sub: string): void {
  const { from, to } = posOf(editor, sub);
  editor.commands.setTextSelection({ from, to });
}

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

// ---------------------------------------------------------------------------
// 1. Generation WITH concurrent edits lands at the REBASED selection (Success Criterion 3, half 1)
// ---------------------------------------------------------------------------
describe('Success Criterion 3 — generation with concurrent edits lands at the REBASED selection', () => {
  it('an AI-returned op anchored at the rebased (post-edit) bookmark point lands on the moved text, not the stale one', async () => {
    const editor = makeEditor();
    selectPhrase(editor, 'brown');

    const { result: bookmark } = renderHook(() => useAiGenerateBookmark(editor));
    act(() => {
      bookmark.current.beginGenerate({ requestId: 'gen-1' });
    });

    // The user keeps typing in the SAME paragraph while the request is "in flight" — the R4
    // generate-window state-drift scenario (design §4 F5).
    act(() => {
      editor.chain().insertContentAt(1, 'PREFIX ').run();
    });

    // The client resolves the bookmark to its CURRENT (rebased) position — NOT the stale
    // drop-time offset — before constructing the operation the apply path will validate.
    const resolved = bookmark.current.resolveBookmark('gen-1');
    expect(resolved).not.toBeNull();
    expect(resolved!.paraId).toBe(PARA_ID);
    // Sanity: the rebased point really does land on "brown" in the CURRENT document.
    expect(editor.state.doc.textBetween(resolved!.pos, resolved!.pos + 'brown'.length)).toBe('brown');

    const op: ComposeOperation = {
      type: 'replaceRange',
      paraId: resolved!.paraId,
      range: {
        start: resolved!.point,
        end: { runIndex: resolved!.point.runIndex, offset: resolved!.point.offset + 'brown'.length },
      },
      text: 'swift',
    };

    const { result: applyValidation } = renderHook(() => useAiApplyValidation(editor));
    let outcome!: Awaited<ReturnType<typeof applyValidation.current.validateAndApply>>;
    await act(async () => {
      outcome = await applyValidation.current.validateAndApply({
        status: 'operations',
        requestId: 'gen-1',
        operations: [op],
        resolved: null,
        review: null,
      });
    });

    // Landed at the REBASED selection: the applied text reflects the edit at the MOVED position.
    expect(outcome.applied).toHaveLength(1);
    expect(outcome.surfaced).toHaveLength(0);
    expect(editor.state.doc.textContent).toBe('PREFIX The quick swift fox jumps over the lazy dog.');
    editor.destroy();
  });

  it('CONTRAST — applying at the STALE (un-rebased) drop-time offset would have landed on the wrong text (the drift defect this fixes)', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'brown');
    const staleOffset = 10; // "brown" starts at char index 10 in the ORIGINAL "The quick brown..." text

    act(() => {
      editor.chain().insertContentAt(1, 'PREFIX ').run();
    });

    // Resolve what the STALE offset now addresses in the post-edit paragraph — it is no longer "brown".
    const paragraph = editor.state.doc.firstChild!;
    const staleText = paragraph.textContent.slice(staleOffset, staleOffset + 'brown'.length);
    expect(staleText).not.toBe('brown');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. A bad/out-of-range anchor is REFUSED — surfaced, never placed (Success Criterion 3, half 2)
// ---------------------------------------------------------------------------
describe('Success Criterion 3 — a bad/out-of-range anchor is REFUSED (surfaced, not placed)', () => {
  it('an unknown paraId is refused end-to-end: not applied, surfaced for review, document unchanged', async () => {
    const editor = makeEditor();
    const before = editor.state.doc.textContent;
    const badOp: ComposeOperation = {
      type: 'replaceRange',
      paraId: 'FFFFFFFF', // does not exist in the live document
      range: { start: { runIndex: 0, offset: 0 }, end: { runIndex: 0, offset: 3 } },
      text: 'The',
    };

    const { result: applyValidation } = renderHook(() => useAiApplyValidation(editor));
    let outcome!: Awaited<ReturnType<typeof applyValidation.current.validateAndApply>>;
    await act(async () => {
      outcome = await applyValidation.current.validateAndApply({
        status: 'operations',
        requestId: 'gen-2',
        operations: [badOp],
        resolved: null,
        review: null,
      });
    });

    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].reason).toBe('unknown-paraId');
    // NEGATIVE — no silent placement occurred ANYWHERE in the document.
    expect(editor.state.doc.textContent).toBe(before);
    editor.destroy();
  });

  it('an out-of-range offset on a KNOWN paraId is refused end-to-end: not applied, surfaced, document unchanged', async () => {
    const editor = makeEditor();
    const before = editor.state.doc.textContent;
    const badOp: ComposeOperation = {
      type: 'insertText',
      paraId: PARA_ID,
      at: { runIndex: 0, offset: 9999 }, // far beyond the paragraph's single run length
      text: 'x',
    };

    const { result: applyValidation } = renderHook(() => useAiApplyValidation(editor));
    let outcome!: Awaited<ReturnType<typeof applyValidation.current.validateAndApply>>;
    await act(async () => {
      outcome = await applyValidation.current.validateAndApply({
        status: 'operations',
        requestId: 'gen-3',
        operations: [badOp],
        resolved: null,
        review: null,
      });
    });

    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].reason).toBe('out-of-range');
    expect(editor.state.doc.textContent).toBe(before);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. Dark-mode render (ADR-021) of the review UI produced by THIS test's real pipeline output
// ---------------------------------------------------------------------------
describe('Success Criterion 3 — the surfaced review UI renders correctly in dark mode (ADR-021)', () => {
  /** A thin host component: wires a REAL `useAiApplyValidation` controller into `ComposeAiToolbar`,
   * runs `validateAndApply` against a bad op on mount, and renders whatever review state results —
   * proving the ACTUAL pipeline's output (not a hand-built stub) is dark-mode-correct. */
  function RefusedAnchorHost(): React.JSX.Element {
    const editorRef = React.useRef<Editor>(makeEditor());
    const applyValidation = useAiApplyValidation(editorRef.current);
    const ranRef = React.useRef(false);
    React.useEffect(() => {
      if (ranRef.current) return;
      ranRef.current = true;
      void applyValidation.validateAndApply({
        status: 'operations',
        requestId: 'gen-4',
        operations: [
          {
            type: 'replaceRange',
            paraId: 'FFFFFFFF',
            range: { start: { runIndex: 0, offset: 0 }, end: { runIndex: 0, offset: 3 } },
            text: 'The',
          },
        ],
        resolved: null,
        review: null,
      });
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    return (
      <ComposeAiToolbar
        editor={editorRef.current as unknown as ReactEditor}
        sessionId="session-042"
        dispatch={jest.fn() as unknown as DispatchPaneEvent}
        actions={[]}
        aiApplyValidation={applyValidation}
      />
    );
  }

  it('renders the surfaced review item under the dark theme with theme tokens only (no hard-coded hex)', async () => {
    const { container, findByTestId } = render(
      <FluentProvider theme={webDarkTheme}>
        <RefusedAnchorHost />
      </FluentProvider>
    );

    const banner = await findByTestId('compose-ai-review-banner');
    expect(banner).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(screen.getByText(/needs review/i)).toBeInTheDocument();
  });
});
