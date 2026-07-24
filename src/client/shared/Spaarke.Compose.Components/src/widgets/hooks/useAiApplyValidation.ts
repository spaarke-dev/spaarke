/**
 * useAiApplyValidation.ts — FR-07 APPLY half: validate-before-apply + fuzzy-as-comment last
 * resort (spaarkeai-compose-r4 task 041).
 *
 * THE PROBLEM (design §5 AI paragraph): task 040 resolves the AI generate-window bookmark to its
 * CURRENT (rebased) position and hands the model's returned JSON operations to "the apply path".
 * A returned operation's `paraId`/offset is a CLAIM by the model — it must be VALIDATED against the
 * LIVE document (the current editor/projection state, post-concurrent-edit) before it is ever
 * applied. An operation whose anchor is unvalidatable — unknown `paraId`, an out-of-range offset, a
 * range that no longer exists, or a target that falls inside an opaque atom (FR-02) — MUST surface
 * as a review comment, NEVER be silently placed (FR-07 / NFR-02 / invariant I-7).
 *
 * THIS MODULE (the apply/validate gate):
 *  1. {@link validateComposeOperationAnchor} — PURE, structural, ZERO text-search validation of one
 *     {@link ComposeOperation}'s anchor against the live document: does `paraId` resolve to a
 *     paraId-bearing textblock right now, is the run-local offset/range within that paragraph's
 *     current bounds, and does the target avoid an opaque-atom run (mirrors the SAME opaque-atom
 *     refusal `stepOperationInterceptor.ts` applies to captured user edits — FR-02 parity).
 *  2. {@link applyValidatedComposeOperation} — applies a structurally-valid INLINE operation
 *     (`insertText` / `deleteRange` / `replaceRange` / `setMark` / `clearMark`) to the live editor
 *     via a normal TipTap `chain()` transaction. Because ComposeEditor already wires a
 *     `RebasedOperationLog` / step-interceptor plugin onto this SAME editor instance (task 020/022),
 *     that transaction is captured automatically into the SAME operation log the user's own edits
 *     flow through — no second capture path, no reimplementation (CLAUDE.md §11 reuse).
 *  3. The four paragraph-STRUCTURAL op types (`splitParagraph` / `mergeParagraph` /
 *     `insertParagraph` / `deleteParagraph`) are anchor-VALIDATED (paraId/targetParaId existence)
 *     but are DELIBERATELY NOT applied directly by this task — see "SCOPE DECISION" below. They
 *     always surface for review, same as an unvalidatable op. This is MORE conservative than the
 *     acceptance bar requires, never less.
 *  4. {@link useAiApplyValidation} — the hook. For every returned operation: valid → apply; invalid
 *     → run the FUZZY LAST RESORT via the shipped `AnnotationReanchorService` route (injected as
 *     `options.reanchor`, the SAME function `useComposeReanchor().reanchor` already exposes — REUSE,
 *     do not reimplement fuzzy matching client-side) purely to ATTACH a confidence hint (AUTO /
 *     REVIEW / ORPHAN + a matched-paragraph preview) to the surfaced item; then surface it.
 *
 * SCOPE DECISION — WHY EVERY UNVALIDATABLE OP SURFACES, EVEN AN "AUTO" FUZZY MATCH (binding for this
 * task; documented per CLAUDE.md §6.5 discipline even though it is NOT an ADR conflict — it is a
 * literal reading of the spec text, not a deviation from it):
 *   NFR-02 / invariant I-7 state fuzzy content-match "survives ONLY as the below-threshold
 *   surface-as-comment last resort" — i.e. fuzzy matching, at ANY confidence band, is a surfacing
 *   mechanism, never a placement mechanism. Consistently, `AnnotationReanchorService`'s response
 *   here returns a paragraph INDEX + preview (a summary datum), not a client-resolvable run-local
 *   anchor — there is no safe, non-guessing way to convert "paragraph 7" back into a
 *   `(paraId, runIndex, offset)` this module could apply without re-introducing the very
 *   content-matching the write path bans. So an AUTO-band hint is surfaced as a HIGH-CONFIDENCE
 *   review item (the comment can say so) rather than ever auto-placed. The escalation trigger this
 *   guards ("any code path that would silently place an unvalidatable operation") therefore has NO
 *   satisfying code path in this module by construction.
 *   A second, related limitation: the fuzzy engine's `textPattern` is meant to describe the PRIOR
 *   anchored content so it can be re-located by content similarity (the Word-round-trip use case,
 *   FR-27). A model-returned operation's own `text` field (when present) is the REPLACEMENT text,
 *   not a description of the target — using it as `textPattern` would fabricate a pattern the model
 *   never actually vouched for. This module passes an intentionally EMPTY `textPattern`, so the
 *   fuzzy scorer can only ever resolve via its exact-`paraId` short-circuit (a genuine, honest
 *   signal) — never a guessed content match.
 *
 * @see ./useAiGenerateBookmark.ts (task 040 — the CAPTURE half this consumes)
 * @see ../stepOperationInterceptor.ts (`runsOfBlock` — reused, not reimplemented; the opaque-atom
 *      refusal this mirrors)
 * @see ../paraIdExtension.ts (`PARAID_NODE_TYPES` — the paraId-bearing node types)
 * @see ../ComposeReanchor.types.ts / ../useComposeReanchor.ts (the shipped fuzzy-reanchor route —
 *      `AnnotationReanchorService.cs` via `POST /api/compose/document/{id}/reanchor-annotations`)
 * @see projects/spaarkeai-compose-r4/design.md §5 (AI paragraph)
 * @see projects/spaarkeai-compose-r4/spec.md FR-07, NFR-02
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import type { Node as PMNode } from '@tiptap/pm/model';
import { PARAID_NODE_TYPES } from '../paraIdExtension';
import { runsOfBlock } from '../stepOperationInterceptor';
import type { ComposeOperation, ComposeRunPoint, ComposeRunRange, ComposeMarkType } from '../../types/compose-operations';
import type { AiGenerateOperationsResult } from './useAiGenerateBookmark';
import type { PriorAnchorInput, ReanchorBand, ReanchorSummary } from '../ComposeReanchor.types';

// ---------------------------------------------------------------------------
// 1. Pure, structural anchor validation (I-7: zero text-search)
// ---------------------------------------------------------------------------

/** Why a returned operation's anchor failed structural validation against the live document. */
export type AnchorValidationReason =
  | 'unknown-paraId'
  | 'unknown-target-paraId'
  | 'out-of-range'
  | 'atom-interior';

export type AnchorValidationResult =
  | { readonly valid: true }
  | { readonly valid: false; readonly reason: AnchorValidationReason };

/** One run within a paragraph, as returned by {@link runsOfBlock} (its `RunSpan` type is module-private). */
type RunSpanLike = ReturnType<typeof runsOfBlock>[number];

/** Find the paraId-bearing textblock node + its ProseMirror position, or `null` if it no longer exists. */
function findParaIdNode(doc: PMNode, paraId: string): { node: PMNode; pos: number } | null {
  let found: { node: PMNode; pos: number } | null = null;
  doc.descendants((node, pos) => {
    if (found) return false;
    if ((PARAID_NODE_TYPES as readonly string[]).includes(node.type.name) && node.isTextblock) {
      const attrs = node.attrs as { paraId?: unknown } | undefined;
      if (typeof attrs?.paraId === 'string' && attrs.paraId === paraId) {
        found = { node, pos };
        return false;
      }
    }
    return true;
  });
  return found;
}

/**
 * True when `run` is an inline-atom leaf — mirrors `stepOperationInterceptor.ts`'s opaque-atom
 * refusal (FR-02). `runsOfBlock` tags a leaf run's `markKey` as `` `${sentinel}leaf:${nodeName}` ``
 * (a non-mark-name sentinel prefix, NOT necessarily a plain space — verified against the actual
 * runtime value rather than assumed); a real mark-set key is `marks.map(m=>m.type.name).sort().join(',')`
 * and can never contain the substring `'leaf:'`, so this substring check is robust without
 * depending on the exact sentinel byte.
 */
function isAtomRun(run: RunSpanLike): boolean {
  return run.markKey.includes('leaf:');
}

function validatePoint(node: PMNode, point: ComposeRunPoint): AnchorValidationResult {
  const runs = runsOfBlock(node);
  if (runs.length === 0) {
    // An empty paragraph has no runs; only the (0,0) point is meaningful.
    return point.runIndex === 0 && point.offset === 0 ? { valid: true } : { valid: false, reason: 'out-of-range' };
  }
  if (point.runIndex < 0 || point.runIndex >= runs.length) return { valid: false, reason: 'out-of-range' };
  const run = runs[point.runIndex];
  if (point.offset < 0 || point.offset > run.length) return { valid: false, reason: 'out-of-range' };
  if (isAtomRun(run)) return { valid: false, reason: 'atom-interior' };
  return { valid: true };
}

function validateRange(node: PMNode, range: ComposeRunRange): AnchorValidationResult {
  const start = validatePoint(node, range.start);
  if (!start.valid) return start;
  const end = validatePoint(node, range.end);
  if (!end.valid) return end;
  const runs = runsOfBlock(node);
  const lo = Math.min(range.start.runIndex, range.end.runIndex);
  const hi = Math.max(range.start.runIndex, range.end.runIndex);
  for (let i = lo; i <= hi; i++) {
    if (isAtomRun(runs[i])) return { valid: false, reason: 'atom-interior' };
  }
  return { valid: true };
}

/**
 * Validate ONE AI-returned {@link ComposeOperation}'s anchor against the LIVE editor document
 * (FR-07 apply gate). Pure and structural — every check is a paraId lookup plus a run/offset
 * bounds check; never a content match (I-7). Exported for direct unit testing.
 */
export function validateComposeOperationAnchor(doc: PMNode, op: ComposeOperation): AnchorValidationResult {
  const target = findParaIdNode(doc, op.paraId);
  if (!target) return { valid: false, reason: 'unknown-paraId' };

  switch (op.type) {
    case 'insertText':
      return validatePoint(target.node, op.at);
    case 'deleteRange':
    case 'replaceRange':
    case 'setMark':
    case 'clearMark':
      return validateRange(target.node, op.range);
    case 'splitParagraph':
      return validatePoint(target.node, op.at);
    case 'mergeParagraph':
      return findParaIdNode(doc, op.targetParaId)
        ? { valid: true }
        : { valid: false, reason: 'unknown-target-paraId' };
    case 'insertParagraph':
    case 'deleteParagraph':
    case 'setBlockAttr':
      // Paragraph-scoped (no run offset) — existence of `paraId` (already checked above) is sufficient.
      return { valid: true };
    default:
      return { valid: false, reason: 'out-of-range' };
  }
}

// ---------------------------------------------------------------------------
// 2. Apply a structurally-valid INLINE operation (structural ops are review-only — see file header)
// ---------------------------------------------------------------------------

const COMPOSE_MARK_TO_TIPTAP: Readonly<Record<ComposeMarkType, string>> = {
  Bold: 'bold',
  Italic: 'italic',
  Underline: 'underline',
};

/** Resolve a structurally-VALIDATED `(paraId, runIndex, offset)` point to an absolute ProseMirror position. */
function resolvePointPosition(doc: PMNode, paraId: string, point: ComposeRunPoint): number | null {
  const target = findParaIdNode(doc, paraId);
  if (!target) return null;
  const runs = runsOfBlock(target.node);
  let acc = 0;
  for (let i = 0; i < point.runIndex; i++) acc += runs[i]?.length ?? 0;
  return target.pos + 1 + acc + point.offset;
}

function tiptapMarksFor(marks: ComposeMarkType[] | undefined): Array<{ type: string }> | undefined {
  if (!marks || marks.length === 0) return undefined;
  const mapped = marks.map(m => ({ type: COMPOSE_MARK_TO_TIPTAP[m] })).filter(m => !!m.type);
  return mapped.length ? mapped : undefined;
}

/**
 * The closed set of op types this task applies DIRECTLY to the live editor. The four paragraph-
 * structural types are deliberately excluded — see file header "SCOPE DECISION".
 */
const DIRECTLY_APPLIABLE_TYPES: ReadonlySet<ComposeOperation['type']> = new Set([
  'insertText',
  'deleteRange',
  'replaceRange',
  'setMark',
  'clearMark',
]);

/**
 * Apply a STRUCTURALLY-VALIDATED (per {@link validateComposeOperationAnchor}) inline operation to
 * the live editor via a normal TipTap transaction. Returns `false` — never throws — for a
 * paragraph-structural op type (review-only in this task's scope) or when the chain command itself
 * is rejected; the caller treats `false` as "surface for review", never as "silently drop".
 */
export function applyValidatedComposeOperation(editor: Editor, op: ComposeOperation): boolean {
  if (!DIRECTLY_APPLIABLE_TYPES.has(op.type)) return false;
  const doc = editor.state.doc;

  switch (op.type) {
    case 'insertText': {
      if (op.text.length === 0) return true; // nothing to insert — a legitimate no-op
      const pos = resolvePointPosition(doc, op.paraId, op.at);
      if (pos === null) return false;
      return editor.chain().insertContentAt(pos, { type: 'text', text: op.text, marks: tiptapMarksFor(op.marks) }).run();
    }
    case 'deleteRange': {
      const from = resolvePointPosition(doc, op.paraId, op.range.start);
      const to = resolvePointPosition(doc, op.paraId, op.range.end);
      if (from === null || to === null || to < from) return false;
      if (to === from) return true; // zero-width range — nothing to delete
      return editor.chain().deleteRange({ from, to }).run();
    }
    case 'replaceRange': {
      const from = resolvePointPosition(doc, op.paraId, op.range.start);
      const to = resolvePointPosition(doc, op.paraId, op.range.end);
      if (from === null || to === null || to < from) return false;
      if (op.text.length === 0) {
        // Pure delete via replace — an empty text node is not a valid ProseMirror insertion.
        return to === from ? true : editor.chain().deleteRange({ from, to }).run();
      }
      return editor
        .chain()
        .insertContentAt({ from, to }, { type: 'text', text: op.text, marks: tiptapMarksFor(op.marks) })
        .run();
    }
    case 'setMark':
    case 'clearMark': {
      const from = resolvePointPosition(doc, op.paraId, op.range.start);
      const to = resolvePointPosition(doc, op.paraId, op.range.end);
      if (from === null || to === null || to <= from) return false;
      const tiptapMark = COMPOSE_MARK_TO_TIPTAP[op.mark];
      if (!tiptapMark) return false;
      const chain = editor.chain().setTextSelection({ from, to });
      return (op.type === 'setMark' ? chain.setMark(tiptapMark) : chain.unsetMark(tiptapMark)).run();
    }
    default:
      return false;
  }
}

// ---------------------------------------------------------------------------
// 3. Fuzzy last resort (REUSE AnnotationReanchorService via its shipped route) + the hook
// ---------------------------------------------------------------------------

/** Why a surfaced item was never applied — either the anchor failed structural validation, or the
 * anchor validated but this task's apply scope does not cover its op type (see file header). */
export type AiApplyReviewReason = AnchorValidationReason | 'not-applied';

/** The fuzzy last-resort lookup's result for one unvalidatable op — informational ONLY (never a placement target). */
export interface AiApplyFuzzyHint {
  readonly band: ReanchorBand;
  readonly confidence: number;
  readonly matchedParagraphPreview: string | null;
  readonly ambiguous: boolean;
}

/** One operation surfaced for human review — never silently placed, never silently dropped (FR-07 / NFR-02 / I-7). */
export interface AiApplyReviewItem {
  readonly id: string;
  readonly operation: ComposeOperation;
  readonly reason: AiApplyReviewReason;
  /** `null` when no fuzzy lookup was attempted (no `reanchor` wired) or it failed/returned nothing. */
  readonly fuzzy: AiApplyFuzzyHint | null;
}

/**
 * Injectable fuzzy lookup — the SAME shape `useComposeReanchor().reanchor` already exposes
 * (`../useComposeReanchor.ts`). Passing that hook's `reanchor` function IS the reuse contract this
 * module honors; omit to skip the fuzzy pass entirely (every unvalidatable op still surfaces, just
 * without a confidence hint).
 */
export type AiApplyReanchorFn = (args: {
  documentSpeId: string;
  driveId: string;
  tenantId: string;
  priorAnchors: PriorAnchorInput[];
}) => Promise<ReanchorSummary>;

export interface UseAiApplyValidationOptions {
  /** The fuzzy last-resort lookup (pass `useComposeReanchor().reanchor`). */
  reanchor?: AiApplyReanchorFn;
  documentSpeId?: string;
  driveId?: string;
  tenantId?: string;
  /** Called once per surfaced (unvalidatable, or valid-but-unapplied) operation. */
  onReviewRequired?: (item: AiApplyReviewItem) => void;
  /** Called once per operation successfully applied to the live document. */
  onApplied?: (op: ComposeOperation) => void;
}

export interface AiApplyOutcome {
  readonly applied: ComposeOperation[];
  readonly surfaced: AiApplyReviewItem[];
}

/** The stable controller returned by {@link useAiApplyValidation}. */
export interface AiApplyValidationController {
  /**
   * Validate + apply every operation in a task-040 {@link AiGenerateOperationsResult}. Every
   * operation either applies cleanly or is surfaced for review — never silently placed, never
   * silently dropped.
   */
  validateAndApply: (result: AiGenerateOperationsResult) => Promise<AiApplyOutcome>;
  /** Review items currently surfaced (session-scoped; cleared via `dismissReview`). */
  reviewQueue: AiApplyReviewItem[];
  /** Dismiss a surfaced review item (e.g. the user acknowledged it). No-op for an unknown id. */
  dismissReview: (id: string) => void;
}

/**
 * FR-07 apply-side controller (see file header). Consumes a task-040
 * {@link AiGenerateOperationsResult}: validates each operation's anchor against the live document,
 * applies the structurally-valid inline ones, and surfaces everything else for review via the
 * fuzzy-reanchor confidence hint (never a placement).
 */
export function useAiApplyValidation(
  editor: Editor | null,
  options: UseAiApplyValidationOptions = {}
): AiApplyValidationController {
  const { reanchor, documentSpeId, driveId, tenantId } = options;
  const [reviewQueue, setReviewQueue] = React.useState<AiApplyReviewItem[]>([]);
  const seqRef = React.useRef(0);

  // Stable callback ref (mirrors useAiGenerateBookmark's cbRef pattern) so a parent's fresh inline
  // callbacks don't force this hook's memoized surfaces to re-identity every render.
  const cbRef = React.useRef({ onReviewRequired: options.onReviewRequired, onApplied: options.onApplied });
  cbRef.current = { onReviewRequired: options.onReviewRequired, onApplied: options.onApplied };

  const canReanchor = !!reanchor && !!documentSpeId && !!driveId && !!tenantId;

  const surface = React.useCallback(
    async (op: ComposeOperation, reason: AiApplyReviewReason): Promise<AiApplyReviewItem> => {
      let fuzzy: AiApplyFuzzyHint | null = null;
      if (canReanchor) {
        try {
          const priorAnchor: PriorAnchorInput = {
            id: `ai-op-${seqRef.current}`,
            type: 'ai-operation',
            // Intentionally empty — see file header "SCOPE DECISION" (an op's own `text`, when
            // present, is the REPLACEMENT, not a description of the target; using it as a fuzzy
            // pattern would fabricate a match the model never vouched for). This means the fuzzy
            // scorer can ONLY resolve via its exact-paraId short-circuit — an honest signal.
            textPattern: '',
            paragraphHint: -1,
            paraId: op.paraId,
          };
          const summary = await reanchor!({ documentSpeId: documentSpeId!, driveId: driveId!, tenantId: tenantId!, priorAnchors: [priorAnchor] });
          const annotation = summary.annotations[0];
          if (annotation) {
            fuzzy = {
              band: annotation.band,
              confidence: annotation.confidence,
              matchedParagraphPreview: annotation.matchedParagraphPreview ?? null,
              ambiguous: annotation.ambiguous,
            };
          }
        } catch {
          // Network/lookup failure — fall through with fuzzy=null. The op STILL surfaces below
          // (never-silently-drop); a fuzzy-lookup failure is not itself a silent-drop path.
        }
      }
      const item: AiApplyReviewItem = { id: `ai-review-${(seqRef.current += 1)}`, operation: op, reason, fuzzy };
      setReviewQueue(prev => [...prev, item]);
      cbRef.current.onReviewRequired?.(item);
      return item;
    },
    [canReanchor, reanchor, documentSpeId, driveId, tenantId]
  );

  const validateAndApply = React.useCallback(
    async (result: AiGenerateOperationsResult): Promise<AiApplyOutcome> => {
      const applied: ComposeOperation[] = [];
      const surfaced: AiApplyReviewItem[] = [];

      if (!editor) {
        for (const op of result.operations) surfaced.push(await surface(op, 'unknown-paraId'));
        return { applied, surfaced };
      }

      for (const op of result.operations) {
        const validation = validateComposeOperationAnchor(editor.state.doc, op);
        if (!validation.valid) {
          surfaced.push(await surface(op, validation.reason));
          continue;
        }
        // The ONLY code path that ever mutates the document from an AI return. `applyValidatedComposeOperation`
        // returns `false` for a structural op type (scope decision, see file header) or a chain-rejected op;
        // BOTH branches surface — there is no third path that silently drops a validated-but-unapplied op
        // (root §6 escalation guard: no silent placement AND no silent drop).
        const didApply = applyValidatedComposeOperation(editor, op);
        if (didApply) {
          applied.push(op);
          cbRef.current.onApplied?.(op);
        } else {
          surfaced.push(await surface(op, 'not-applied'));
        }
      }

      return { applied, surfaced };
    },
    [editor, surface]
  );

  const dismissReview = React.useCallback((id: string) => {
    setReviewQueue(prev => prev.filter(item => item.id !== id));
  }, []);

  return React.useMemo(
    () => ({ validateAndApply, reviewQueue, dismissReview }),
    [validateAndApply, reviewQueue, dismissReview]
  );
}
