/**
 * useAiGenerateBookmark.test.tsx — FR-07 CAPTURE half (spaarkeai-compose-r4 task 040).
 *
 * Three layers, all over REAL editor state (ADR-038 / NFR-06 — no Mock<HttpMessageHandler>,
 * no DI-registration, no ctor-null tests):
 *  1. `extractComposeOperations` — the pure free-text-vs-operations classifier (I-7).
 *  2. HOOK LOGIC — driven through `renderHook` over a REAL headless `@tiptap/core` Editor
 *     carrying the R3 paraId extension, so `resolveRunAnchor` resolves durable paraIds:
 *     bookmark-drop + paraId context, REBASE-through-concurrent-edit (the drift-proof proof),
 *     JSON-operations-referencing-paraId return, free-text refusal, and the deleted-content
 *     surface-for-review seam (task 041).
 *  3. UI — the existing `ComposeAiToolbar` generate affordance wired to the controller, in
 *     dark theme (ADR-021), asserting the bookmark drops + targetParaId reaches the dispatch
 *     slots + resolveOnReturn fires on the result — additively (no controller ⇒ unchanged).
 */
import * as React from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderHook } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import type { Editor as ReactEditor } from '@tiptap/react';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { useAiGenerateBookmark, extractComposeOperations, type AiGenerateBookmarkController } from './useAiGenerateBookmark';
import type { AiApplyValidationController, AiApplyReviewItem } from './useAiApplyValidation';
import {
  ComposeAiToolbar,
  __resetComposeAiToolbarActionsForTests,
  type ComposeAiToolbarAction,
} from '../ComposeAiToolbar';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets/events';
import type { DispatchConsumer } from '@spaarke/ui-components';

// `@spaarke/ui-components` resolves to its built `dist/` (jest.config comment) — which, in a
// worktree without a fresh SharedLibs build, transitively fails to resolve `@fluentui/react-icons`
// from that dist (the SAME environmental blocker the sibling `ComposeAiToolbar.test.tsx` hits). The
// toolbar only needs `createConsumerDispatcher` from this package for the layer-3 wiring test (which
// is bypassed by `dispatchConsumerOverride` anyway), so mock it to keep the suite runnable here AND
// in CI (types are erased). The pure hook tests below need none of this.
jest.mock('@spaarke/ui-components', () => ({
  createConsumerDispatcher: () => async () => ({ streamId: 'stub', status: 'complete' }),
}));

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

const PARA_ID = '0000AAAA';
const SENTENCE = 'The quick brown fox jumps over the lazy dog.';

function makeEditor(text = SENTENCE): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID],
    content: `<p data-paraid="${PARA_ID}">${text}</p>`,
  });
}

/** Doc position of a substring in a single-paragraph doc (textOffset + 1 for the opening <p>). */
function posOf(editor: Editor, sub: string): { from: number; to: number } {
  const at = editor.state.doc.textContent.indexOf(sub);
  return { from: at + 1, to: at + 1 + sub.length };
}

function selectPhrase(editor: Editor, sub: string): void {
  const { from, to } = posOf(editor, sub);
  editor.commands.setTextSelection({ from, to });
}

/** A valid JSON-operations return payload referencing the target paraId. */
function opsPayload(paraId = PARA_ID) {
  return {
    schemaVersion: 'compose-ops-v1',
    operations: [
      {
        type: 'replaceRange',
        paraId,
        range: { start: { runIndex: 0, offset: 4 }, end: { runIndex: 0, offset: 9 } },
        text: 'swift',
      },
    ],
  };
}

// ---------------------------------------------------------------------------
// 1. extractComposeOperations — free text vs JSON operations (I-7)
// ---------------------------------------------------------------------------
describe('extractComposeOperations (I-7 free-text refusal classifier)', () => {
  it('a plain string (free text) is refused → null', () => {
    expect(extractComposeOperations('Here is a suggested rewrite of the clause.')).toBeNull();
  });

  it('a well-formed operation-log envelope returns its operations', () => {
    const ops = extractComposeOperations(opsPayload());
    expect(ops).not.toBeNull();
    expect(ops).toHaveLength(1);
    expect(ops![0]).toMatchObject({ type: 'replaceRange', paraId: PARA_ID });
  });

  it('a bare array of valid operations is accepted', () => {
    const ops = extractComposeOperations([{ type: 'deleteParagraph', paraId: PARA_ID }]);
    expect(ops).toHaveLength(1);
  });

  it('an empty operations envelope is a valid no-op ([]), not a refusal', () => {
    expect(extractComposeOperations({ schemaVersion: 'compose-ops-v1', operations: [] })).toEqual([]);
  });

  it('a mixed array (an op + free-text garbage) is refused → null', () => {
    expect(extractComposeOperations([{ type: 'insertText', paraId: PARA_ID, at: { runIndex: 0, offset: 0 }, text: 'x' }, 'noise'])).toBeNull();
  });

  it('an object with no operations key is refused → null', () => {
    expect(extractComposeOperations({ message: 'I could not do that' })).toBeNull();
  });

  it('an op missing paraId is not a valid operation → refused', () => {
    expect(extractComposeOperations({ operations: [{ type: 'insertText', at: { runIndex: 0, offset: 0 }, text: 'x' }] })).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// 2. Hook — bookmark drop + paraId context (acceptance criterion 1)
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — bookmark on Generate + paraId context', () => {
  it('drops a bookmark at the selection tied to a request id and resolves the paragraph paraId as context', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'quick');
    const { result } = renderHook(() => useAiGenerateBookmark(editor));

    let ctx!: ReturnType<AiGenerateBookmarkController['beginGenerate']>;
    act(() => {
      ctx = result.current.beginGenerate({ requestId: 'req-1' });
    });

    expect(ctx.requestId).toBe('req-1');
    expect(ctx.paraId).toBe(PARA_ID); // the durable target paraId sent to the model as context
    expect(ctx.anchor).not.toBeNull();
    expect(result.current.activeCount).toBe(1);
    editor.destroy();
  });

  it('mints a request id when none is supplied', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'quick');
    const { result } = renderHook(() => useAiGenerateBookmark(editor));

    let ctx!: ReturnType<AiGenerateBookmarkController['beginGenerate']>;
    act(() => {
      ctx = result.current.beginGenerate();
    });
    expect(ctx.requestId).toMatch(/^ai-generate#\d+$/);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2b. Hook — REBASE through concurrent edits (acceptance criterion 2 — the drift fix)
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — rebase through concurrent edits (drift-proof)', () => {
  it('the bookmark tracks the selection through a concurrent insertion BEFORE it — lands at the rebased position, not the stale offset', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'brown');
    const staleStart = posOf(editor, 'brown').from;
    const { result } = renderHook(() => useAiGenerateBookmark(editor));

    act(() => {
      result.current.beginGenerate({ requestId: 'req-2' });
    });

    // The user keeps typing at the START of the paragraph while the request is "in flight".
    act(() => {
      editor.chain().insertContentAt(1, 'PREFIX ').run(); // shifts everything after pos 1 by 7
    });

    const resolved = result.current.resolveBookmark('req-2');
    expect(resolved).not.toBeNull();
    // REBASED: the bookmark now points at the MOVED "brown", not the original (stale) offset.
    expect(resolved!.pos).toBe(staleStart + 'PREFIX '.length);
    expect(resolved!.pos).not.toBe(staleStart);
    expect(editor.state.doc.textBetween(resolved!.pos, resolved!.pos + 'brown'.length)).toBe('brown');
    expect(resolved!.paraId).toBe(PARA_ID);
    editor.destroy();
  });

  it('resolveOnReturn lands the returned operations at the rebased selection after a concurrent edit', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'brown');
    const staleStart = posOf(editor, 'brown').from;
    const { result } = renderHook(() => useAiGenerateBookmark(editor));

    act(() => {
      result.current.beginGenerate({ requestId: 'req-3' });
    });
    act(() => {
      editor.chain().insertContentAt(1, 'XX ').run(); // +3
    });

    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('req-3', opsPayload());
    });

    expect(ret.status).toBe('operations');
    if (ret.status === 'operations') {
      expect(ret.review).toBeNull();
      expect(ret.resolved).not.toBeNull();
      expect(ret.resolved!.pos).toBe(staleStart + 3); // rebased, not stale
      expect(ret.resolved!.paraId).toBe(PARA_ID);
    }
    // Bookmark consumed.
    expect(result.current.activeCount).toBe(0);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2c. Hook — return payload IS JSON operations referencing paraId (acceptance criterion 3)
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — returned payload is JSON operations referencing paraId', () => {
  it('hands the apply path (041) the operations, each referencing a non-empty paraId', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'quick');
    const { result } = renderHook(() => useAiGenerateBookmark(editor));

    act(() => {
      result.current.beginGenerate({ requestId: 'req-4' });
    });

    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('req-4', opsPayload());
    });

    expect(ret.status).toBe('operations');
    if (ret.status === 'operations') {
      expect(ret.operations).toHaveLength(1);
      expect(ret.operations.every(op => typeof op.paraId === 'string' && op.paraId.length > 0)).toBe(true);
      expect(ret.operations[0]).toMatchObject({ type: 'replaceRange', paraId: PARA_ID });
    }
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2d. Hook — NEGATIVE: free text is refused + surfaced (acceptance criterion 4)
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — free-text return is REFUSED (I-7 / escalation seam)', () => {
  it('a free-text return refuses and fires the surface callback — no silent placement, no text-search', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'quick');
    const onFreeTextRefused = jest.fn();
    const { result } = renderHook(() => useAiGenerateBookmark(editor, { onFreeTextRefused }));

    act(() => {
      result.current.beginGenerate({ requestId: 'req-5' });
    });

    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('req-5', 'Sure — here is a rewritten version of the clause.');
    });

    expect(ret.status).toBe('refused');
    if (ret.status === 'refused') expect(ret.reason).toBe('free-text');
    expect(onFreeTextRefused).toHaveBeenCalledWith('req-5', 'free-text');
    expect(result.current.activeCount).toBe(0);
    editor.destroy();
  });

  it('a non-operations object is refused as not-operations', () => {
    const editor = makeEditor();
    selectPhrase(editor, 'quick');
    const { result } = renderHook(() => useAiGenerateBookmark(editor));
    act(() => {
      result.current.beginGenerate({ requestId: 'req-5b' });
    });
    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('req-5b', { note: 'no ops here' });
    });
    expect(ret.status).toBe('refused');
    if (ret.status === 'refused') expect(ret.reason).toBe('not-operations');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2e. Hook — bookmark inside deleted content SURFACES for review (acceptance criterion / 041 seam)
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — deleted-content bookmark surfaces for review (never silent placement)', () => {
  it('flags review + fires onReviewRequired when a concurrent edit deletes the bookmarked content', () => {
    const editor = makeEditor();
    const brown = posOf(editor, 'brown');
    selectPhrase(editor, 'brown');
    const onReviewRequired = jest.fn();
    const { result } = renderHook(() => useAiGenerateBookmark(editor, { onReviewRequired }));

    act(() => {
      result.current.beginGenerate({ requestId: 'req-6' });
    });
    // Delete a range that strictly CONTAINS the bookmark's tracked start (assoc -1) → MapResult.deleted.
    act(() => {
      editor.chain().deleteRange({ from: brown.from - 2, to: brown.to + 2 }).run();
    });

    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('req-6', opsPayload());
    });

    expect(ret.status).toBe('operations'); // ops still returned...
    if (ret.status === 'operations') {
      expect(ret.review).toBe('bookmark-in-deleted-content'); // ...but flagged for review, not silently placed
    }
    expect(onReviewRequired).toHaveBeenCalledWith('req-6', 'bookmark-in-deleted-content');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2f. Hook — unknown / consumed request id
// ---------------------------------------------------------------------------
describe('useAiGenerateBookmark — unknown request id', () => {
  it('returns unknown-request for an id with no live bookmark (e.g. double return)', () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useAiGenerateBookmark(editor));
    let ret!: ReturnType<AiGenerateBookmarkController['resolveOnReturn']>;
    act(() => {
      ret = result.current.resolveOnReturn('never-started', opsPayload());
    });
    expect(ret.status).toBe('unknown-request');
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. UI — ComposeAiToolbar generate affordance wired to the controller (ADR-021 dark mode)
// ---------------------------------------------------------------------------

/** Minimal TipTap-`Editor`-shaped mock — only the members ComposeAiToolbar reads. */
function createMockEditor(params: { from: number; to: number; text: string }): ReactEditor {
  const mock = {
    state: {
      selection: { from: params.from, to: params.to },
      doc: { textBetween: () => params.text },
    },
    on: () => mock,
    off: () => mock,
  };
  return mock as unknown as ReactEditor;
}

function stubController(): AiGenerateBookmarkController {
  return {
    beginGenerate: jest.fn(() => ({ requestId: 'ctx-req', paraId: PARA_ID, anchor: { runIndex: 0, offset: 0 } })),
    resolveBookmark: jest.fn(() => null),
    resolveOnReturn: jest.fn(),
    clearBookmark: jest.fn(),
    activeCount: 0,
  };
}

const GENERATE_ACTION: ComposeAiToolbarAction = {
  id: 'compose-draft-alternative',
  label: 'Draft alternative',
  tooltip: 'Draft an alternative (Generate).',
  bindingId: 'b-draft',
  placement: 'primary',
  materializesInEditor: true,
};

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

function renderToolbar(
  ui: {
    controller?: AiGenerateBookmarkController;
    dispatchConsumerOverride?: DispatchConsumer;
    applyValidation?: AiApplyValidationController;
  },
  theme: typeof webLightTheme = webLightTheme
) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeAiToolbar
        editor={createMockEditor({ from: 0, to: 11, text: 'Hello world' })}
        documentRef={{ speDriveItemId: 'spe-123', sprkDocumentId: 'doc-456' }}
        sessionId="session-789"
        bffBaseUrl="https://bff.example.test"
        dispatch={jest.fn() as unknown as DispatchPaneEvent}
        actions={[GENERATE_ACTION]}
        dispatchConsumerOverride={ui.dispatchConsumerOverride}
        aiGenerateBookmark={ui.controller}
        aiApplyValidation={ui.applyValidation}
      />
    </FluentProvider>
  );
}

/** A fixed-shape `AiApplyValidationController` stub (task 041 wiring tests below). */
function stubApplyValidation(reviewQueue: AiApplyReviewItem[] = []): AiApplyValidationController {
  return {
    validateAndApply: jest.fn().mockResolvedValue({ applied: [], surfaced: reviewQueue }),
    reviewQueue,
    dismissReview: jest.fn(),
  };
}

describe('ComposeAiToolbar — FR-07 generate affordance + bookmark wiring (ADR-021 dark mode)', () => {
  it('renders the generate affordance under the dark theme with no hard-coded hex color (ADR-021)', () => {
    const { container } = renderToolbar({ controller: stubController() }, webDarkTheme);
    expect(screen.getByTestId('compose-ai-toolbar')).toBeInTheDocument();
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('clicking Generate drops a bookmark, adds targetParaId to the dispatch slots, and resolves on the result', async () => {
    const user = userEvent.setup();
    const controller = stubController();
    const result = { streamId: 's1', status: 'complete', result: opsPayload() };
    const dispatchConsumerOverride = jest.fn().mockResolvedValue(result);

    renderToolbar({ controller, dispatchConsumerOverride });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));

    // Bookmark dropped for the Generate action.
    expect(controller.beginGenerate).toHaveBeenCalledTimes(1);
    // Dispatch (envelope-only, existing PublicContracts route) carries the target paraId as context.
    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'b-draft',
      expect.objectContaining({ slots: expect.objectContaining({ targetParaId: PARA_ID }) })
    );
    // On the return, the bookmark is resolved and the payload handed to the apply path (041).
    await waitFor(() => expect(controller.resolveOnReturn).toHaveBeenCalledTimes(1));
    expect(controller.resolveOnReturn).toHaveBeenCalledWith(
      expect.stringContaining('compose-draft-alternative#bm'),
      result.result
    );
  });

  it('ADDITIVE: without a controller, the same Generate action dispatches with NO targetParaId slot (unchanged behavior)', async () => {
    const user = userEvent.setup();
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });

    renderToolbar({ dispatchConsumerOverride }); // no controller

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));

    expect(dispatchConsumerOverride).toHaveBeenCalledTimes(1);
    const [, args] = dispatchConsumerOverride.mock.calls[0];
    expect((args as { slots: Record<string, unknown> }).slots).not.toHaveProperty('targetParaId');
  });
});

// ---------------------------------------------------------------------------
// 4. UI — task 041 wiring: an 'operations' resolution is handed to the apply-validation
//    controller; a surfaced review item renders (ADR-021 dark mode).
// ---------------------------------------------------------------------------
describe('ComposeAiToolbar — FR-07 task-041 apply-validation wiring (ADR-021 dark mode)', () => {
  it('an "operations" resolveOnReturn result is handed to aiApplyValidation.validateAndApply', async () => {
    const user = userEvent.setup();
    const controller = stubController();
    const opsOutcome = { status: 'operations' as const, requestId: 'ctx-req', operations: [], resolved: null, review: null };
    (controller.resolveOnReturn as jest.Mock).mockReturnValue(opsOutcome);
    const applyValidation = stubApplyValidation();
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete', result: opsPayload() });

    renderToolbar({ controller, dispatchConsumerOverride, applyValidation });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));

    await waitFor(() => expect(applyValidation.validateAndApply).toHaveBeenCalledTimes(1));
    expect(applyValidation.validateAndApply).toHaveBeenCalledWith(opsOutcome);
  });

  it('ADDITIVE: a "refused" resolveOnReturn result does NOT reach aiApplyValidation (no operations to validate)', async () => {
    const user = userEvent.setup();
    const controller = stubController();
    (controller.resolveOnReturn as jest.Mock).mockReturnValue({ status: 'refused', requestId: 'ctx-req', reason: 'free-text' });
    const applyValidation = stubApplyValidation();
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete', result: 'free text' });

    renderToolbar({ controller, dispatchConsumerOverride, applyValidation });

    await user.click(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative'));

    await waitFor(() => expect(controller.resolveOnReturn).toHaveBeenCalledTimes(1));
    expect(applyValidation.validateAndApply).not.toHaveBeenCalled();
  });

  it('renders a surfaced review item under the dark theme with no hard-coded hex color (ADR-021)', () => {
    const reviewItem: AiApplyReviewItem = {
      id: 'ai-review-1',
      operation: { type: 'replaceRange', paraId: 'ZZZZ0001', range: { start: { runIndex: 0, offset: 0 }, end: { runIndex: 0, offset: 1 } }, text: 'x' },
      reason: 'unknown-paraId',
      fuzzy: null,
    };
    const applyValidation = stubApplyValidation([reviewItem]);

    const { container } = renderToolbar({ controller: stubController(), applyValidation }, webDarkTheme);

    expect(screen.getByTestId('compose-ai-review-banner')).toBeInTheDocument();
    expect(screen.getByTestId(`compose-ai-review-item-${reviewItem.id}`)).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('dismissing a surfaced review item calls aiApplyValidation.dismissReview with its id', async () => {
    const user = userEvent.setup();
    const reviewItem: AiApplyReviewItem = {
      id: 'ai-review-2',
      operation: { type: 'deleteParagraph', paraId: 'ZZZZ0002' },
      reason: 'not-applied',
      fuzzy: null,
    };
    const applyValidation = stubApplyValidation([reviewItem]);

    renderToolbar({ controller: stubController(), applyValidation });

    await user.click(screen.getByTestId(`compose-ai-review-dismiss-${reviewItem.id}`));
    expect(applyValidation.dismissReview).toHaveBeenCalledWith(reviewItem.id);
  });
});
