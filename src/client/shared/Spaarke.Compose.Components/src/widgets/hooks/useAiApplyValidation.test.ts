/**
 * useAiApplyValidation.test.ts — FR-07 APPLY half (spaarkeai-compose-r4 task 041).
 *
 * Three layers, all over REAL editor state (ADR-038 / NFR-06 — no Mock<HttpMessageHandler>, no
 * DI-registration, no ctor-null tests; the fuzzy-reanchor network call is injected via a plain
 * function, the SAME `fetchOverride`-style pattern `useComposeReanchor.ts` already establishes):
 *  1. `validateComposeOperationAnchor` — the pure structural validator (paraId existence, run/
 *     offset bounds, atom-interior) against a REAL headless `@tiptap/core` Editor carrying the R3
 *     paraId extension + the R4 opaque-atom nodes.
 *  2. `applyValidatedComposeOperation` — applying a structurally-valid inline op mutates the LIVE
 *     document; a structural op type is never applied by this task's scope (always `false`).
 *  3. `useAiApplyValidation` — the hook: valid ops apply, unvalidatable ops surface (never silently
 *     placed), and — critically — even an "AUTO"-band fuzzy hint never auto-applies (the escalation
 *     guard this task is bound by).
 */
import { renderHook, act } from '@testing-library/react';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { COMPOSE_R4_OPAQUE_ATOMS } from '../opaqueAtomNode';
import {
  validateComposeOperationAnchor,
  applyValidatedComposeOperation,
  useAiApplyValidation,
  type AiApplyReanchorFn,
} from './useAiApplyValidation';
import type { AiGenerateOperationsResult } from './useAiGenerateBookmark';
import type { ComposeOperation } from '../../types/compose-operations';
import type { ReanchorSummary } from '../ComposeReanchor.types';

const PARA_ID = '0000AAAA';
const SENTENCE = 'Hello world';

function makeEditor(html = `<p data-paraid="${PARA_ID}">${SENTENCE}</p>`): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID, ...COMPOSE_R4_OPAQUE_ATOMS],
    content: html,
  });
}

/** Wrap operations in the minimal `AiGenerateOperationsResult` shape `validateAndApply` reads. */
function opsResult(operations: ComposeOperation[]): AiGenerateOperationsResult {
  return { status: 'operations', requestId: 'req-1', operations, resolved: null, review: null };
}

const replaceOp = (paraId = PARA_ID): ComposeOperation => ({
  type: 'replaceRange',
  paraId,
  range: { start: { runIndex: 0, offset: 0 }, end: { runIndex: 0, offset: 5 } },
  text: 'Howdy',
});

// ---------------------------------------------------------------------------
// 1. validateComposeOperationAnchor — pure structural validation (I-7: zero text-search)
// ---------------------------------------------------------------------------
describe('validateComposeOperationAnchor', () => {
  it('a well-formed anchor on an existing paraId, within run bounds, is valid', () => {
    const editor = makeEditor();
    const result = validateComposeOperationAnchor(editor.state.doc, replaceOp());
    expect(result.valid).toBe(true);
    editor.destroy();
  });

  it('an unknown paraId is invalid (unknown-paraId)', () => {
    const editor = makeEditor();
    const result = validateComposeOperationAnchor(editor.state.doc, replaceOp('FFFFFFFF'));
    expect(result).toEqual({ valid: false, reason: 'unknown-paraId' });
    editor.destroy();
  });

  it('an out-of-range offset on a valid paraId is invalid (out-of-range)', () => {
    const editor = makeEditor();
    const op: ComposeOperation = {
      type: 'insertText',
      paraId: PARA_ID,
      at: { runIndex: 0, offset: 999 },
      text: 'x',
    };
    const result = validateComposeOperationAnchor(editor.state.doc, op);
    expect(result).toEqual({ valid: false, reason: 'out-of-range' });
    editor.destroy();
  });

  it('an unknown runIndex on a valid paraId is invalid (out-of-range)', () => {
    const editor = makeEditor();
    const op: ComposeOperation = {
      type: 'insertText',
      paraId: PARA_ID,
      at: { runIndex: 7, offset: 0 },
      text: 'x',
    };
    const result = validateComposeOperationAnchor(editor.state.doc, op);
    expect(result).toEqual({ valid: false, reason: 'out-of-range' });
    editor.destroy();
  });

  it('a target run that is an opaque atom is invalid (atom-interior, FR-02 parity)', () => {
    const editor = makeEditor(
      `<p data-paraid="${PARA_ID}">Before <span class="compose-atom" data-atom-kind="field">1</span> After</p>`
    );
    // Run 0 = "Before ", run 1 = the inline atom leaf, run 2 = " After".
    const op: ComposeOperation = {
      type: 'insertText',
      paraId: PARA_ID,
      at: { runIndex: 1, offset: 0 },
      text: 'x',
    };
    const result = validateComposeOperationAnchor(editor.state.doc, op);
    expect(result).toEqual({ valid: false, reason: 'atom-interior' });
    editor.destroy();
  });

  it('a mergeParagraph whose targetParaId no longer exists is invalid (unknown-target-paraId)', () => {
    const editor = makeEditor();
    const op: ComposeOperation = { type: 'mergeParagraph', paraId: PARA_ID, targetParaId: 'FFFFFFFF' };
    const result = validateComposeOperationAnchor(editor.state.doc, op);
    expect(result).toEqual({ valid: false, reason: 'unknown-target-paraId' });
    editor.destroy();
  });

  it('a paragraph-scoped op (deleteParagraph) on an existing paraId is valid (no run offset to check)', () => {
    const editor = makeEditor();
    const op: ComposeOperation = { type: 'deleteParagraph', paraId: PARA_ID };
    expect(validateComposeOperationAnchor(editor.state.doc, op)).toEqual({ valid: true });
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 2. applyValidatedComposeOperation — mutates the LIVE document for inline ops only
// ---------------------------------------------------------------------------
describe('applyValidatedComposeOperation', () => {
  it('applies a valid replaceRange cleanly — the document text reflects the change', () => {
    const editor = makeEditor();
    const applied = applyValidatedComposeOperation(editor, replaceOp());
    expect(applied).toBe(true);
    expect(editor.state.doc.textContent).toBe('Howdy world');
    editor.destroy();
  });

  it('applies a valid insertText cleanly', () => {
    const editor = makeEditor();
    const op: ComposeOperation = { type: 'insertText', paraId: PARA_ID, at: { runIndex: 0, offset: 5 }, text: ',' };
    expect(applyValidatedComposeOperation(editor, op)).toBe(true);
    expect(editor.state.doc.textContent).toBe('Hello, world');
    editor.destroy();
  });

  it('applies a valid deleteRange cleanly', () => {
    const editor = makeEditor();
    const op: ComposeOperation = {
      type: 'deleteRange',
      paraId: PARA_ID,
      range: { start: { runIndex: 0, offset: 5 }, end: { runIndex: 0, offset: 11 } },
    };
    expect(applyValidatedComposeOperation(editor, op)).toBe(true);
    expect(editor.state.doc.textContent).toBe('Hello');
    editor.destroy();
  });

  it('a paragraph-structural op type is NEVER applied by this task (scope decision) — returns false, doc unchanged', () => {
    const editor = makeEditor();
    const before = editor.state.doc.textContent;
    const op: ComposeOperation = { type: 'deleteParagraph', paraId: PARA_ID };
    expect(applyValidatedComposeOperation(editor, op)).toBe(false);
    expect(editor.state.doc.textContent).toBe(before);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// 3. useAiApplyValidation — the hook: validate → apply | surface (never silently place/drop)
// ---------------------------------------------------------------------------
describe('useAiApplyValidation — valid op applies cleanly, no review surfaced', () => {
  it('a valid anchor applies and does not surface a review item', async () => {
    const editor = makeEditor();
    const onApplied = jest.fn();
    const { result } = renderHook(() => useAiApplyValidation(editor, { onApplied }));

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp()]));
    });

    expect(outcome.applied).toHaveLength(1);
    expect(outcome.surfaced).toHaveLength(0);
    expect(onApplied).toHaveBeenCalledTimes(1);
    expect(editor.state.doc.textContent).toBe('Howdy world');
    expect(result.current.reviewQueue).toHaveLength(0);
    editor.destroy();
  });
});

describe('useAiApplyValidation — an out-of-range/unknown anchor is REFUSED and surfaced, never mis-placed', () => {
  it('an unknown paraId surfaces as a review item; the document is unchanged', async () => {
    const editor = makeEditor();
    const onReviewRequired = jest.fn();
    const { result } = renderHook(() => useAiApplyValidation(editor, { onReviewRequired }));
    const before = editor.state.doc.textContent;

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp('FFFFFFFF')]));
    });

    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].reason).toBe('unknown-paraId');
    expect(editor.state.doc.textContent).toBe(before); // NOT mis-placed
    expect(result.current.reviewQueue).toHaveLength(1);
    expect(onReviewRequired).toHaveBeenCalledTimes(1);
    editor.destroy();
  });

  it('an out-of-range offset surfaces as a review item with no fuzzy hint when no reanchor is wired', async () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useAiApplyValidation(editor)); // no `reanchor` — fuzzy pass skipped
    const op: ComposeOperation = { type: 'insertText', paraId: PARA_ID, at: { runIndex: 0, offset: 999 }, text: 'x' };

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([op]));
    });

    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].reason).toBe('out-of-range');
    expect(outcome.surfaced[0].fuzzy).toBeNull();
    editor.destroy();
  });

  it('dismissReview removes a surfaced item from the queue', async () => {
    const editor = makeEditor();
    const { result } = renderHook(() => useAiApplyValidation(editor));

    await act(async () => {
      await result.current.validateAndApply(opsResult([replaceOp('FFFFFFFF')]));
    });
    expect(result.current.reviewQueue).toHaveLength(1);
    const id = result.current.reviewQueue[0].id;

    act(() => {
      result.current.dismissReview(id);
    });
    expect(result.current.reviewQueue).toHaveLength(0);
    editor.destroy();
  });
});

describe('useAiApplyValidation — fuzzy last resort (REUSE AnnotationReanchorService via the injected route)', () => {
  const summaryWith = (over: Partial<ReanchorSummary['annotations'][number]>): ReanchorSummary => ({
    documentSpeId: 'spe-1',
    total: 1,
    autoCount: 0,
    reviewCount: 0,
    orphanCount: 0,
    computedAtUtc: new Date().toISOString(),
    annotations: [
      {
        id: 'ai-op-0',
        type: 'ai-operation',
        preview: null,
        band: 'orphan',
        confidence: 0,
        matchedParagraphIndex: -1,
        contentSimilarity: 0,
        structuralProximity: 0,
        ambiguous: false,
        matchedParagraphPreview: null,
        ...over,
      },
    ],
  });

  it('an AUTO-band fuzzy match NEVER auto-applies — it still surfaces (the escalation guard)', async () => {
    const editor = makeEditor();
    const reanchor: AiApplyReanchorFn = jest
      .fn()
      .mockResolvedValue(summaryWith({ band: 'auto', confidence: 1, matchedParagraphPreview: 'Hello world' }));
    const { result } = renderHook(() =>
      useAiApplyValidation(editor, { reanchor, documentSpeId: 'spe-1', driveId: 'drive-1', tenantId: 'tenant-1' })
    );
    const before = editor.state.doc.textContent;

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp('FFFFFFFF')]));
    });

    // NEVER silently placed, even at AUTO confidence — surfaced with the band as a hint only.
    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].fuzzy).toMatchObject({ band: 'auto', matchedParagraphPreview: 'Hello world' });
    expect(editor.state.doc.textContent).toBe(before);
    editor.destroy();
  });

  it('an ambiguous REVIEW-band match hits the ambiguity guard and surfaces rather than auto-placing', async () => {
    const editor = makeEditor();
    const reanchor: AiApplyReanchorFn = jest.fn().mockResolvedValue(summaryWith({ band: 'review', ambiguous: true }));
    const { result } = renderHook(() =>
      useAiApplyValidation(editor, { reanchor, documentSpeId: 'spe-1', driveId: 'drive-1', tenantId: 'tenant-1' })
    );

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp('FFFFFFFF')]));
    });

    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced[0].fuzzy).toMatchObject({ band: 'review', ambiguous: true });
    editor.destroy();
  });

  it('a reanchor lookup failure still surfaces (never silently dropped) with fuzzy=null', async () => {
    const editor = makeEditor();
    const reanchor: AiApplyReanchorFn = jest.fn().mockRejectedValue(new Error('network down'));
    const { result } = renderHook(() =>
      useAiApplyValidation(editor, { reanchor, documentSpeId: 'spe-1', driveId: 'drive-1', tenantId: 'tenant-1' })
    );

    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp('FFFFFFFF')]));
    });

    expect(outcome.surfaced).toHaveLength(1);
    expect(outcome.surfaced[0].fuzzy).toBeNull();
    editor.destroy();
  });
});

describe('useAiApplyValidation — no editor mounted', () => {
  it('surfaces every operation for review rather than throwing', async () => {
    const { result } = renderHook(() => useAiApplyValidation(null));
    let outcome!: Awaited<ReturnType<typeof result.current.validateAndApply>>;
    await act(async () => {
      outcome = await result.current.validateAndApply(opsResult([replaceOp()]));
    });
    expect(outcome.applied).toHaveLength(0);
    expect(outcome.surfaced).toHaveLength(1);
  });
});
