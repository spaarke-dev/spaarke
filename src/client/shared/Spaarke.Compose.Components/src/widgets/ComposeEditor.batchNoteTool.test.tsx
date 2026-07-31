/**
 * ComposeEditor.batchNoteTool.test.tsx — ai-advanced-capabilities-agreements-r1 task 041 (spec
 * FR-11) render-level DoD coverage for the sequential batch note-tool loop against the REAL
 * `ComposeEditor` (real TipTap editor, real `placeAdvisoryComments` → real gutter cards) — mirrors
 * `ComposeEditor.advisoryComments.test.tsx`'s mount convention (only that file's composition
 * — `resolveTargetSpans('strict')` + `useComposeCommentThreads.createThread` — reaches real
 * multi-note gutter cards) and `ComposeEditor.aiToolbarTriggers.test.tsx`'s `getEditorInstance`
 * pattern for reaching the REAL TipTap `Editor` so `coordsAtPos` can be stubbed (jsdom has no real
 * layout engine — `ComposeCommentGutter` positions/renders a card only once `coordsAtPos` resolves;
 * see `ComposeEditor.bidirectionalHighlight.test.tsx`'s "not reliably reachable" note for why most
 * task-040/041-adjacent suites avoid asserting on gutter-card DOM — this suite instead follows
 * `ComposeCommentGutter.test.tsx`'s own convention of spying on the real editor instance directly).
 *
 * NOTE: `placeAdvisoryComments` mints each thread's `id` internally (`useComposeCommentThreads.
 * createThread` — not caller-supplied), so this suite reads the REAL ids back via
 * `ComposeEditorHandle.getAdvisoryCommentThreads()` rather than assuming literal "thread-1" etc.
 * (that literal-id convention is only valid in `ComposeCommentGutter.test.tsx`'s isolated harness,
 * which constructs `ComposeCommentThreadModel` fixtures directly).
 *
 * Proves, against the REAL wiring (`ComposeCommentGutter.onRunBatchNoteTool` →
 * `ComposeEditor.runBatchNoteTool` → `batchNoteToolRunner.runBatchNoteTool` →
 * `dispatchNoteToolRequest` → the injected `enqueueComposeAction` prop):
 *  - selecting N notes + Run calls `enqueueComposeAction` exactly N times, STRICTLY sequentially
 *    (never more than one call unsettled at once) — the ADR-016 assertion at the full-component level;
 *  - each call's request is byte-shape-identical to what a SINGLE-note run builds for the same
 *    thread + tool (the spec 041 "outcomes byte-equivalent to a single run" criterion — proven here
 *    by construction: both paths call the SAME `dispatchNoteToolRequest`);
 *  - a mid-batch rejection does not abort the remaining notes (failure isolation);
 *  - the end-of-batch progress modal shows the real per-note progress, then the pass/fail summary.
 */
import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorHandle, type ComposeEditorDocumentRef } from './ComposeEditor';
import { registerComposeAiToolbarAction, __resetComposeAiToolbarActionsForTests } from './ComposeAiToolbar';
import type { ComposeServerProjection } from '../types/compose-contracts';
import type { DispatchConsumerResult } from '@spaarke/ui-components';
import type { Editor } from '@tiptap/react';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

const BATCH_PROJECTION: ComposeServerProjection = {
  status: 'success',
  canEdit: true,
  html:
    '<p data-paraid="AB12CD34">The receiving party shall retain confidential information indefinitely. ' +
    'Some unrelated boilerplate text separates the clauses in this fixture document nicely. ' +
    'The disclosing party may audit compliance with reasonable prior notice at any time. ' +
    'A third distinct clause about indemnification obligations rounds out this fixture.</p>',
  warnings: [],
  schemaVersion: 'compose-html-v1',
};

function docxBytesFixture(): ArrayBuffer {
  const buf = new Uint8Array(8);
  buf.set([0x50, 0x4b, 0x03, 0x04], 0);
  return buf.buffer;
}

const REAL_BINDING_ID = 'binding-041-draft-alternative';

function registerRealDraftAlternativeTool(): void {
  // Overrides the default `bindingId: ''` stub (Phase-4 gate) with a real id — mirrors
  // ComposeAiToolbar.tsx's own documented registration pattern (see ComposeEditor.activeWorkType.test.tsx).
  registerComposeAiToolbarAction({
    id: 'compose-draft-alternative',
    label: 'Draft compliant alternative',
    tooltip: 'Task 041 fixture.',
    bindingId: REAL_BINDING_ID,
    placement: 'primary',
    materializesInEditor: true,
    surfaces: ['selection', 'review-note'],
  });
}

/** Locates the actual ProseMirror contenteditable node TipTap mounts (mirrors aiToolbarTriggers.test.tsx). */
function getEditorDom(container: HTMLElement): HTMLElement {
  const dom = container.querySelector('.ProseMirror') as HTMLElement | null;
  if (!dom) throw new Error('ProseMirror editor DOM not found — editor did not mount');
  return dom;
}

/** The real TipTap `Editor` instance, attached by the PM view to its contenteditable DOM node. */
function getEditorInstance(container: HTMLElement): Editor {
  const dom = getEditorDom(container) as unknown as { editor?: Editor };
  if (!dom.editor) throw new Error('TipTap editor instance not found on ProseMirror DOM node');
  return dom.editor;
}

function renderEditor(
  ref: React.Ref<ComposeEditorHandle>,
  documentRef: ComposeEditorDocumentRef,
  enqueueComposeAction: (request: {
    id: string;
    bindingId: string;
    args?: unknown;
    documentSessionId?: string;
  }) => Promise<DispatchConsumerResult>
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor
          ref={ref}
          docxBytes={docxBytesFixture()}
          projection={BATCH_PROJECTION}
          documentRef={documentRef}
          sessionId="session-041-batch"
          activeWorkType="agreement-analysis"
          enqueueComposeAction={enqueueComposeAction as never}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Mounts, stubs `coordsAtPos` (deterministic, non-overlapping tops) so gutter cards actually render
 * in jsdom, seeds 3 advisory-comment threads, and returns their REAL (server/hook-minted) ids in
 * seed order — `placeAdvisoryComments` never lets the caller choose a thread id. */
async function mountAndSeedThreeThreads(
  ref: React.RefObject<ComposeEditorHandle | null>,
  documentRef: ComposeEditorDocumentRef,
  enqueueComposeAction: (request: {
    id: string;
    bindingId: string;
    args?: unknown;
    documentSessionId?: string;
  }) => Promise<DispatchConsumerResult>
): Promise<readonly string[]> {
  const { container } = renderEditor(ref, documentRef, enqueueComposeAction);
  await screen.findByRole('textbox');

  const editor = getEditorInstance(container);
  let nextTop = 0;
  jest.spyOn(editor.view, 'coordsAtPos').mockImplementation(() => {
    nextTop += 100;
    return { top: nextTop, bottom: nextTop + 20, left: 0, right: 0 };
  });

  const result = ref.current!.placeAdvisoryComments([
    {
      targetText: 'The receiving party shall retain confidential information indefinitely.',
      explanation: 'Finding 1.',
    },
    {
      targetText: 'The disclosing party may audit compliance with reasonable prior notice at any time.',
      explanation: 'Finding 2.',
    },
    {
      targetText: 'A third distinct clause about indemnification obligations rounds out this fixture.',
      explanation: 'Finding 3.',
    },
  ]);
  expect(result.placed).toBe(3);

  // `createThread`'s `setThreads` is an async React state update — wait for it to flush + the gutter
  // to re-render with all 3 cards before reading their (hook-minted) ids back out of the DOM.
  const checkboxes = await waitFor(() => {
    const els = Array.from(
      container.querySelectorAll<HTMLElement>('[data-testid^="compose-comment-gutter-checkbox-"]')
    );
    if (els.length !== 3) throw new Error(`expected 3 checkboxes, found ${els.length}`);
    return els;
  });
  const ids = checkboxes.map(el => el.getAttribute('data-testid')!.replace('compose-comment-gutter-checkbox-', ''));
  return ids;
}

async function selectAllAndOpenBatchToolbar(ids: readonly string[]): Promise<void> {
  for (const id of ids) {
    fireEvent.click(screen.getByTestId(`compose-comment-gutter-checkbox-${id}`));
  }
  fireEvent.click(screen.getByTestId('compose-comment-gutter-batch-tool-dropdown'));
  fireEvent.click(await screen.findByTestId('compose-comment-gutter-batch-tool-compose-draft-alternative'));
}

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
});

describe('ComposeEditor — task 041 sequential batch note-tool loop (ADR-016)', () => {
  it('dispatches N sequential enqueueComposeAction calls (never >1 in flight) for N selected notes', async () => {
    registerRealDraftAlternativeTool();
    let inFlight = 0;
    let maxInFlight = 0;
    const calls: unknown[] = [];
    const enqueueComposeAction = jest.fn(async (request: { id: string; bindingId: string; args?: unknown }) => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      calls.push(request);
      await new Promise(res => setTimeout(res, 1));
      inFlight -= 1;
      return {} as DispatchConsumerResult;
    });

    const ref = React.createRef<ComposeEditorHandle>();
    const ids = await mountAndSeedThreeThreads(
      ref,
      { speDriveItemId: 'drive-041', fileName: 'Agreement.docx' },
      enqueueComposeAction
    );

    await selectAllAndOpenBatchToolbar(ids);
    fireEvent.click(screen.getByTestId('compose-comment-gutter-batch-run'));

    await waitFor(() => expect(enqueueComposeAction).toHaveBeenCalledTimes(3));
    expect(maxInFlight).toBe(1); // the ADR-016 assertion, proven at the full-component level

    // Every dispatched request routed to the DOCUMENT session (DEF-09) with the real bindingId.
    for (const call of calls as { bindingId: string; documentSessionId?: string }[]) {
      expect(call.bindingId).toBe(REAL_BINDING_ID);
      expect(call.documentSessionId).toBe('session-041-batch');
    }

    // Progress modal reaches its terminal "complete" summary.
    await waitFor(() =>
      expect(screen.getByTestId('compose-batch-note-tool-progress-summary')).toHaveTextContent('3 succeeded')
    );
  });

  it("each batch request's shape is identical to what a single-note run builds for the same thread", async () => {
    registerRealDraftAlternativeTool();
    const enqueueComposeAction = jest.fn(async () => ({}) as DispatchConsumerResult);

    const ref = React.createRef<ComposeEditorHandle>();
    const ids = await mountAndSeedThreeThreads(
      ref,
      { speDriveItemId: 'drive-041b', fileName: 'Agreement.docx' },
      enqueueComposeAction as never
    );
    const [id1] = ids;

    // Single-note run via the ⋮ menu for the first thread.
    fireEvent.click(screen.getByTestId(`compose-comment-gutter-tools-${id1}`));
    fireEvent.click(await screen.findByTestId(`compose-comment-gutter-tool-${id1}-compose-draft-alternative`));
    await waitFor(() => expect(enqueueComposeAction).toHaveBeenCalledTimes(1));
    const singleReq = enqueueComposeAction.mock.calls[0][0] as {
      bindingId: string;
      args: { slots: Record<string, unknown> };
      documentSessionId?: string;
    };
    enqueueComposeAction.mockClear();

    // Batch run over all 3 (including the same thread again) — compare its request shape.
    await selectAllAndOpenBatchToolbar(ids);
    fireEvent.click(screen.getByTestId('compose-comment-gutter-batch-run'));
    await waitFor(() => expect(enqueueComposeAction).toHaveBeenCalledTimes(3));
    const batchCalls = enqueueComposeAction.mock.calls.map(c => c[0]) as {
      bindingId: string;
      args: { slots: Record<string, unknown> };
      documentSessionId?: string;
    }[];
    const batchReqForId1 = batchCalls.find(
      c => c.args.slots.selectionAnchorStart === singleReq.args.slots.selectionAnchorStart
    );

    expect(batchReqForId1).toBeDefined();
    // Byte-shape-identical request keys/values (id excluded — it carries a per-call sequence suffix
    // by design, same as any two single-note runs of the SAME tool would also differ on).
    expect(batchReqForId1!.bindingId).toBe(singleReq.bindingId);
    expect(batchReqForId1!.documentSessionId).toBe(singleReq.documentSessionId);
    expect(batchReqForId1!.args.slots).toEqual(singleReq.args.slots);
  });

  it('a mid-batch rejection is isolated — the remaining notes still dispatch, and the summary reports success/failure per note', async () => {
    registerRealDraftAlternativeTool();
    let callIndex = 0;
    const enqueueComposeAction = jest.fn(async () => {
      callIndex += 1;
      if (callIndex === 2) throw new Error('simulated dispatch failure');
      return {} as DispatchConsumerResult;
    });

    const ref = React.createRef<ComposeEditorHandle>();
    const ids = await mountAndSeedThreeThreads(
      ref,
      { speDriveItemId: 'drive-041c', fileName: 'Agreement.docx' },
      enqueueComposeAction as never
    );

    await selectAllAndOpenBatchToolbar(ids);
    fireEvent.click(screen.getByTestId('compose-comment-gutter-batch-run'));

    // All 3 still dispatch despite the 2nd failing.
    await waitFor(() => expect(enqueueComposeAction).toHaveBeenCalledTimes(3));

    await waitFor(() =>
      expect(screen.getByTestId('compose-batch-note-tool-progress-summary')).toHaveTextContent('2 succeeded')
    );
    expect(screen.getByTestId('compose-batch-note-tool-progress-summary')).toHaveTextContent('1 failed');
    // The modal stays open (does not auto-dismiss) when there is a failure — a Close button appears.
    expect(screen.getByTestId('compose-batch-note-tool-progress-close')).toBeInTheDocument();
  });

  it('zero notes selected renders no sub-toolbar (nothing to run)', async () => {
    registerRealDraftAlternativeTool();
    const enqueueComposeAction = jest.fn(async () => ({}) as DispatchConsumerResult);
    const ref = React.createRef<ComposeEditorHandle>();
    await mountAndSeedThreeThreads(
      ref,
      { speDriveItemId: 'drive-041d', fileName: 'Agreement.docx' },
      enqueueComposeAction as never
    );

    expect(screen.queryByTestId('compose-comment-gutter-batch-toolbar')).not.toBeInTheDocument();
    expect(enqueueComposeAction).not.toHaveBeenCalled();
  });
});
