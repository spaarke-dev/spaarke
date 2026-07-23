/**
 * stepOperationInterceptor.ts — R4 FR-03 (spaarkeai-compose-r4 task 020).
 *
 * THE CLIENT HALF OF THE R4 BRIDGE (design.md §5 "Capture (frontend)"). A
 * ProseMirror plugin (registered as a headless TipTap extension) that intercepts
 * editor transaction STEPS and maps EACH step to a task-003 {@link ComposeOperation}
 * anchored `(paraId, runIndex, run-local-offset)` — invariant I-6 (the client is a
 * view + controller that EMITS operations, it authors no bytes) and D2 (anchor by
 * `(paraId, runIndex, run-local-offset)`, never an absolute editor position).
 *
 * WHAT THIS IS NOT (binding):
 *  - It does NOT diff the document and does NOT emit `{paraId,text}` payloads — that
 *    is the retired `collectEditedParagraphs` paragraph-diff path (docxBridge.ts,
 *    deleted in task 023). This is STEP-LEVEL operational capture (D1).
 *  - It does NOT fetch or save (ADR-028: no client fetch here — capture only).
 *  - It does NOT re-mint paraIds — the enclosing node's `w14:paraId` is read via the
 *    `paraIdExtension` convention (PARAID_NODE_TYPES); ids come from there.
 *
 * LICENSING (NFR-03): MIT ProseMirror base only. `Step`/`StepMap`/`Mapping` and the
 * concrete step classes come from `@tiptap/pm/transform` — the MIT re-export surface
 * TipTap already bundles (same import family the sibling QaHighlightExtension uses:
 * `@tiptap/pm/state`, `@tiptap/pm/view`). NO `@tiptap-pro/*`, no AGPL, no new package.
 *
 * SCOPE (task 020 framework + task 031 structural synthesis): task 020 built the
 * interceptor framework + the run-local anchor resolver + the INLINE op set (insertText /
 * deleteRange / replaceRange / setMark / clearMark) + the `setBlockAttr` alignment case +
 * the opaque-atom refusal guard. Task 031 adds paragraph-level STRUCTURAL synthesis
 * (`classifyStructuralStep`): a block-boundary ReplaceStep/ReplaceAroundStep is diffed
 * before-vs-after to EMIT `splitParagraph` / `mergeParagraph` / `insertParagraph` /
 * `deleteParagraph` — so a whole-paragraph delete/merge is CAPTURED into the rebased op-log
 * (closes the task-023 coverage gap; see `notes/task-023-coverage-gap.md`). A structural
 * shape the four ops cannot cleanly carry (a forward-merge, a multi-paragraph rewrite, a
 * list wrap/unwrap with no block-count change) is still routed to the `onStructuralStep`
 * seam (recognized, NEVER silently dropped, NEVER mis-mapped). A step whose SHAPE the op
 * schema genuinely cannot carry (e.g. a formatting mark outside the closed `ComposeMarkType`
 * set) is surfaced via `onUnrepresentableStep` (the POML `<escalation>` seam — root §6/§6.5).
 *
 * @see projects/spaarkeai-compose-r4/notes/bridge-prior-art.md §1 (Step/StepMap/Mapping), §4 (anchor drift)
 * @see src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.ts (task 003 op contract)
 * @see src/client/shared/Spaarke.Compose.Components/src/widgets/paraIdExtension.ts (PARAID_NODE_TYPES / w14:paraId)
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import type { Node as PMNode, Mark, Fragment } from '@tiptap/pm/model';
import type { Transaction } from '@tiptap/pm/state';
import {
  ReplaceStep,
  ReplaceAroundStep,
  AddMarkStep,
  RemoveMarkStep,
  AttrStep,
  type Step,
  type Mapping,
} from '@tiptap/pm/transform';

import { PARAID_NODE_TYPES } from './paraIdExtension';
import type {
  ComposeOperation,
  ComposeRunPoint,
  ComposeMarkType,
  ComposeBlockAttr,
} from '../types/compose-operations';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * A resolved RUN-LOCAL anchor: the durable `paraId` coarse anchor plus the fine
 * `(runIndex, run-local-offset)` anchor (D2). Re-derivable at apply time — NEVER an
 * absolute editor position. Structurally a {@link ComposeRunPoint} extended with its
 * paraId (the op-schema's per-op `paraId` + `at`/`range` decompose to exactly this).
 */
export interface ComposeAnchor extends ComposeRunPoint {
  /** The enclosing paragraph's durable `w14:paraId`. */
  paraId: string;
}

/** How a single {@link Step} was classified against the closed task-003 op set. */
export type StepClassification =
  /** The step maps cleanly to zero-or-more task-003 operations. */
  | { kind: 'ops'; ops: ComposeOperation[] }
  /** The step's range enters/replaces an opaque atom node — REFUSED (atoms are non-editable, FR-02). */
  | { kind: 'refused-atom'; step: Step }
  /** A paragraph-level structural step — deferred to task 031's `onStructuralStep` seam (not this task). */
  | { kind: 'defer-structural'; step: Step; reason: string }
  /** A step the op schema genuinely cannot represent — surfaced (escalation seam), never dropped. */
  | { kind: 'unrepresentable'; step: Step; reason: string };

/** Context handed to {@link StepOperationInterceptorOptions.onOperations}. */
export interface OperationEmitContext {
  /** The transaction the operations were captured from (in-session only; do NOT persist positions from it). */
  transaction: Transaction;
}

/** Configuration for the interceptor. Every callback is optional — a bare registration captures nothing observable. */
export interface StepOperationInterceptorOptions {
  /**
   * Called once per doc-changing transaction with the ORDERED operations captured
   * from that transaction's steps. Empty transactions and refused/deferred steps
   * produce no call. The save/rebase wiring (task 022+) supplies this; task 020
   * registers the extension bare (no handler) — wire-in only, no save-path change.
   */
  onOperations?: (operations: ComposeOperation[], context: OperationEmitContext) => void;
  /** Called when a step is refused because its range enters an opaque atom node (FR-02). */
  onRefusedAtomEdit?: (step: Step) => void;
  /** Called for a paragraph-level structural step (task 031 seam). Recognized, not dropped, not mis-mapped. */
  onStructuralStep?: (step: Step, reason: string) => void;
  /** Called for a step the closed op set cannot represent (escalation seam — root §6/§6.5). */
  onUnrepresentableStep?: (step: Step, reason: string) => void;
  /**
   * Predicate identifying an opaque-atom node (task 021's non-editable leaf). Default:
   * any node whose schema spec is `atom: true` — schema-driven so it stays DECOUPLED
   * from task 021's exact node name (no shared constant, no file collision). A host may
   * override to narrow it to the specific opaque-atom node type.
   */
  isOpaqueAtom?: (node: PMNode) => boolean;
}

// ---------------------------------------------------------------------------
// Mark / block-attr mapping tables (closed sets — mirror the server enums)
// ---------------------------------------------------------------------------

/** TipTap mark name → closed-set {@link ComposeMarkType}. Marks outside this set are NOT char-format run props. */
const TIPTAP_MARK_TO_COMPOSE: Readonly<Record<string, ComposeMarkType>> = {
  bold: 'Bold',
  italic: 'Italic',
  underline: 'Underline',
};

/** TextAlign attr value → the `Alignment` {@link ComposeBlockAttr} value (mirrors the server `ComposeAlignment`). */
const TEXTALIGN_TO_COMPOSE: Readonly<Record<string, string>> = {
  left: 'Left',
  center: 'Center',
  right: 'Right',
  justify: 'Justify',
};

// ---------------------------------------------------------------------------
// Anchor resolution (D2 — the run-local fine anchor)
// ---------------------------------------------------------------------------

/** True when `node` is a paraId-bearing editable textblock (paragraph/heading carrying a non-empty `w14:paraId`). */
function isParaIdBlock(node: PMNode | null | undefined): node is PMNode {
  if (!node) return false;
  if (!(PARAID_NODE_TYPES as readonly string[]).includes(node.type.name)) return false;
  const paraId = (node.attrs as { paraId?: unknown } | undefined)?.paraId;
  return typeof paraId === 'string' && paraId.length > 0;
}

/** A single mark-boundary run within a paragraph: its char length + a comparable key of its mark-set. */
interface RunSpan {
  length: number;
  markKey: string;
}

/** Stable comparable key for a mark-set — run boundaries occur at any rPr (mark-set) change, mirroring OOXML `<w:r>`. */
function markSetKey(marks: readonly Mark[]): string {
  if (marks.length === 0) return '';
  return marks
    .map((m) => m.type.name)
    .sort()
    .join(',');
}

/**
 * Decompose a paragraph's inline content into mark-boundary RUNS (mirrors OOXML `<w:r>`
 * splitting): adjacent text nodes sharing an identical mark-set merge into one run; a
 * mark-set change starts a new run; each inline leaf (image, inline atom) is its own run.
 * Re-derivable from the node alone — the basis for the run-local anchor (D2).
 */
export function runsOfBlock(block: PMNode): RunSpan[] {
  const runs: RunSpan[] = [];
  let current: RunSpan | null = null;
  block.forEach((child) => {
    if (child.isText) {
      const key = markSetKey(child.marks);
      const len = child.text?.length ?? 0;
      if (current && current.markKey === key) {
        current.length += len;
      } else {
        current = { length: len, markKey: key };
        runs.push(current);
      }
    } else {
      // Inline leaf (e.g. image) — its own run; breaks the merge chain.
      runs.push({ length: child.nodeSize, markKey: ` leaf:${child.type.name}` });
      current = null;
    }
  });
  return runs;
}

/**
 * Map a paragraph-local character offset `k` to a run-local `(runIndex, offset)`.
 * Boundary convention (deterministic + documented): an offset that falls exactly on a
 * run boundary resolves to the TRAILING edge of the EARLIER run (`offset === run length`)
 * — the first run whose cumulative end is `>= k`. An empty paragraph yields `(0, 0)`.
 */
export function runLocalPoint(block: PMNode, k: number): ComposeRunPoint {
  const runs = runsOfBlock(block);
  if (runs.length === 0) return { runIndex: 0, offset: 0 };
  let acc = 0;
  for (let i = 0; i < runs.length; i++) {
    const len = runs[i].length;
    if (k <= acc + len) return { runIndex: i, offset: k - acc };
    acc += len;
  }
  // k beyond the paragraph's content (defensive) — clamp to the trailing edge of the last run.
  const last = runs.length - 1;
  return { runIndex: last, offset: runs[last].length };
}

/**
 * Resolve an absolute editor position to its run-local anchor `(paraId, runIndex,
 * run-local-offset)` (D2). Returns `null` when the position's immediate parent is NOT a
 * paraId-bearing textblock (e.g. a caret at a block boundary or inside a non-paragraph
 * container) — the caller treats a null anchor as "not an inline paragraph edit".
 */
export function resolveRunAnchor(
  doc: PMNode,
  pos: number,
): ComposeAnchor | null {
  if (pos < 0 || pos > doc.content.size) return null;
  const $pos = doc.resolve(pos);
  const parent = $pos.parent;
  if (!isParaIdBlock(parent) || !parent.isTextblock) return null;
  const paraId = (parent.attrs as { paraId: string }).paraId;
  // parentOffset is the char offset within the textblock's inline content — exactly the
  // paragraph-local `k` the run walk expects (NOT an absolute position).
  const point = runLocalPoint(parent, $pos.parentOffset);
  return { paraId, runIndex: point.runIndex, offset: point.offset };
}

// ---------------------------------------------------------------------------
// Step helpers
// ---------------------------------------------------------------------------

/** True when any node fully inside the range `[from, to)` is an opaque atom — the refusal guard (FR-02). */
function rangeContainsOpaqueAtom(
  doc: PMNode,
  from: number,
  to: number,
  isOpaqueAtom: (node: PMNode) => boolean,
): boolean {
  if (to <= from) return false; // a zero-width insertion cannot land INSIDE a leaf atom
  let found = false;
  doc.nodesBetween(from, to, (node) => {
    if (found) return false;
    if (isOpaqueAtom(node)) {
      found = true;
      return false;
    }
    return true;
  });
  return found;
}

/** True when every top-level child of the slice fragment is inline (no block boundary — i.e. a same-paragraph edit). */
function fragmentIsInline(fragment: Fragment): boolean {
  if (fragment.childCount === 0) return true;
  let inline = true;
  fragment.forEach((child) => {
    if (!child.isInline) inline = false;
  });
  return inline;
}

/** The first inline text node's marks within a fragment (the marks the inserted text carries), or `[]`. */
function firstInlineMarks(fragment: Fragment): readonly Mark[] {
  let marks: readonly Mark[] = [];
  fragment.descendants((node) => {
    if (node.isText) {
      marks = node.marks;
      return false;
    }
    return true;
  });
  return marks;
}

/** Map a ProseMirror mark-set to the closed {@link ComposeMarkType} set (drops non-char-format marks, e.g. link). */
function marksToComposeMarks(marks: readonly Mark[]): ComposeMarkType[] {
  const out: ComposeMarkType[] = [];
  for (const m of marks) {
    const mapped = TIPTAP_MARK_TO_COMPOSE[m.type.name];
    if (mapped && !out.includes(mapped)) out.push(mapped);
  }
  return out;
}

/** The plain text a slice inserts (inline content only). */
function sliceText(fragment: Fragment): string {
  return fragment.textBetween(0, fragment.size, '\n');
}

// ---------------------------------------------------------------------------
// Structural step → operation synthesis (task 031 — the four paragraph ops)
// ---------------------------------------------------------------------------

/** A paraId-addressable block in a doc snapshot: its node, durable paraId (or null if unminted), and text. */
interface StructuralBlock {
  node: PMNode;
  paraId: string | null;
  text: string;
}

/** Ordered list of paraId-bearing textblock paragraphs, each with its paraId (or null) + text content. */
function structuralBlocks(doc: PMNode): StructuralBlock[] {
  const out: StructuralBlock[] = [];
  doc.descendants((node) => {
    if ((PARAID_NODE_TYPES as readonly string[]).includes(node.type.name) && node.isTextblock) {
      const pid = (node.attrs as { paraId?: unknown } | undefined)?.paraId;
      out.push({
        node,
        paraId: typeof pid === 'string' && pid.length > 0 ? pid : null,
        text: node.textContent,
      });
      return false; // a textblock's inline children are not themselves blocks
    }
    return true;
  });
  return out;
}

/** Mint a fresh 8-hex `w14:paraId` (ST_LongHexNumber) for the NEW paragraph a split/insert op creates. */
function mintParaId(): string {
  let s = '';
  for (let i = 0; i < 8; i++) s += Math.floor(Math.random() * 16).toString(16);
  return s.toUpperCase();
}

/**
 * Synthesize the task-003 STRUCTURAL operation a block-boundary ProseMirror step performs, by diffing the
 * paraId-bearing blocks BEFORE vs AFTER the step (the durable-id equivalent of the retired
 * `collectEditedParagraphs` load-vs-current diff — see `notes/task-023-coverage-gap.md`):
 *
 *   - one paraId removed, its content absorbed by its predecessor  → `mergeParagraph`
 *   - one paraId removed, content NOT absorbed forward             → `deleteParagraph` (closes task 023's gap)
 *   - one block gained, a source's tail became the new block       → `splitParagraph`
 *   - one empty block gained, all existing blocks unchanged        → `insertParagraph`
 *
 * A shape the four ops cannot cleanly carry (a genuine forward-merge, a multi-paragraph rewrite, a list
 * wrap/unwrap with no block-count change) is DEFERRED — recognized via `onStructuralStep`, never mis-mapped to a
 * wrong op. This is the same never-mis-map discipline the inline classifier applies at its refusal seams.
 */
function classifyStructuralStep(step: Step, docBefore: PMNode, reason: string): StepClassification {
  const applied = step.apply(docBefore);
  if (applied.failed || !applied.doc) {
    return { kind: 'defer-structural', step, reason };
  }

  const before = structuralBlocks(docBefore);
  const after = structuralBlocks(applied.doc);
  const afterById = new Map<string, StructuralBlock>();
  for (const b of after) if (b.paraId) afterById.set(b.paraId, b);
  const removed = before.filter((b) => b.paraId !== null && !afterById.has(b.paraId));

  // ---- one paragraph removed → merge (backward) or whole-paragraph delete ----
  if (removed.length === 1 && after.length === before.length - 1) {
    const gone = removed[0];
    const goneIdx = before.indexOf(gone);
    const predecessor = before[goneIdx - 1];
    if (predecessor?.paraId) {
      const predAfter = afterById.get(predecessor.paraId);
      if (predAfter && predAfter.text === predecessor.text + gone.text) {
        return {
          kind: 'ops',
          ops: [{ type: 'mergeParagraph', paraId: gone.paraId as string, targetParaId: predecessor.paraId }],
        };
      }
    }
    // Not a backward-merge. Classify as a whole-paragraph DELETE only when `gone`'s content was NOT absorbed
    // forward into its successor (a forward-merge is not expressible as "append onto predecessor" — defer it
    // rather than mis-strike content as a delete).
    const successor = before[goneIdx + 1];
    const succAfter = successor?.paraId ? afterById.get(successor.paraId) : undefined;
    const absorbedForward = !!succAfter && succAfter.text === gone.text + (successor?.text ?? '');
    if (!absorbedForward) {
      return { kind: 'ops', ops: [{ type: 'deleteParagraph', paraId: gone.paraId as string }] };
    }
    return { kind: 'defer-structural', step, reason };
  }

  // ---- one paragraph gained → split (a source's tail) or insert (a brand-new block) ----
  if (removed.length === 0 && after.length === before.length + 1) {
    // SPLIT: everything before the split index is unchanged, so before[i] aligns positionally with after[i].
    // At the split index the source's prefix stays in after[i] (same paraId) and the moved tail is after[i+1].
    // (Positional, NOT by-id: ProseMirror's `split` copies the source paraId onto the new block, so an id map
    //  would collide — the new block's own id is irrelevant, the server assigns `newParaId`.)
    for (let i = 0; i < before.length; i++) {
      const s = before[i];
      const prefix = after[i];
      if (!s.paraId || !prefix || prefix.paraId !== s.paraId) continue;
      if (prefix.text.length < s.text.length && s.text.startsWith(prefix.text)) {
        const suffix = s.text.slice(prefix.text.length);
        const moved = after[i + 1];
        if (moved && moved.text === suffix) {
          return {
            kind: 'ops',
            ops: [{ type: 'splitParagraph', paraId: s.paraId, at: runLocalPoint(s.node, prefix.text.length), newParaId: mintParaId() }],
          };
        }
      }
    }

    const existingUnchanged = before.every((b) => !b.paraId || afterById.get(b.paraId)?.text === b.text);
    if (existingUnchanged) {
      const newIdx = after.findIndex((b) => b.paraId === null);
      if (newIdx >= 0) {
        const refBefore = after[newIdx - 1];
        const refAfter = after[newIdx + 1];
        if (refBefore?.paraId) {
          return { kind: 'ops', ops: [{ type: 'insertParagraph', paraId: refBefore.paraId, newParaId: mintParaId(), position: 'After' }] };
        }
        if (refAfter?.paraId) {
          return { kind: 'ops', ops: [{ type: 'insertParagraph', paraId: refAfter.paraId, newParaId: mintParaId(), position: 'Before' }] };
        }
      }
    }
    return { kind: 'defer-structural', step, reason };
  }

  // Multi-paragraph rewrite / list wrap-unwrap without a block-count change — recognized, not mis-mapped.
  return { kind: 'defer-structural', step, reason };
}

// ---------------------------------------------------------------------------
// Step → operation classification
// ---------------------------------------------------------------------------

/**
 * Classify ONE ProseMirror {@link Step} (given the doc BEFORE it applied) into the closed
 * task-003 op set. This is the heart of the bridge (design §5.0 "offset→run mapping").
 * Pure + synchronous; the plugin calls it per step with the correct pre-step doc.
 */
export function classifyStep(
  step: Step,
  docBefore: PMNode,
  options: StepOperationInterceptorOptions,
): StepClassification {
  const isOpaqueAtom = options.isOpaqueAtom ?? defaultIsOpaqueAtom;

  // --- ReplaceStep: the workhorse (insert / delete / replace / structural) ---------
  if (step instanceof ReplaceStep) {
    const from = step.from;
    const to = step.to;
    const slice = step.slice;

    // FR-02 guard: an edit whose range enters an opaque atom is REFUSED (never captured).
    if (rangeContainsOpaqueAtom(docBefore, from, to, isOpaqueAtom)) {
      return { kind: 'refused-atom', step };
    }

    const $from = docBefore.resolve(from);
    const $to = docBefore.resolve(to);
    const sameBlock = $from.sameParent($to);

    // Inline, same-paragraph edit → an inline op. Anything else is structural (task 031).
    if (
      sameBlock &&
      isParaIdBlock($from.parent) &&
      $from.parent.isTextblock &&
      fragmentIsInline(slice.content)
    ) {
      const anchorFrom = resolveRunAnchor(docBefore, from);
      const anchorTo = resolveRunAnchor(docBefore, to);
      if (!anchorFrom || !anchorTo || anchorFrom.paraId !== anchorTo.paraId) {
        return { kind: 'defer-structural', step, reason: 'replace-step-anchor-unresolved' };
      }
      const paraId = anchorFrom.paraId;
      const text = sliceText(slice.content);
      const marks = marksToComposeMarks(firstInlineMarks(slice.content));

      // Pure insertion (collapsed range, non-empty inserted text).
      if (from === to && slice.content.size > 0) {
        const op: ComposeOperation = {
          type: 'insertText',
          paraId,
          at: pointOf(anchorFrom),
          text,
          ...(marks.length ? { marks } : {}),
        };
        return { kind: 'ops', ops: [op] };
      }

      // Pure deletion (non-empty range, empty slice).
      if (to > from && slice.content.size === 0) {
        const op: ComposeOperation = {
          type: 'deleteRange',
          paraId,
          range: { start: pointOf(anchorFrom), end: pointOf(anchorTo) },
        };
        return { kind: 'ops', ops: [op] };
      }

      // Replace (non-empty range, non-empty slice).
      if (to > from && slice.content.size > 0) {
        const op: ComposeOperation = {
          type: 'replaceRange',
          paraId,
          range: { start: pointOf(anchorFrom), end: pointOf(anchorTo) },
          text,
          ...(marks.length ? { marks } : {}),
        };
        return { kind: 'ops', ops: [op] };
      }

      // Collapsed range + empty slice = a no-op replace (nothing to capture).
      return { kind: 'ops', ops: [] };
    }

    // Cross-paragraph or block-boundary ReplaceStep (split/merge/para insert/delete) → synthesize the
    // structural op (task 031). Whole-paragraph delete/merge is captured here (closes task 023's gap).
    return classifyStructuralStep(step, docBefore, 'replace-step-structural');
  }

  // --- AddMarkStep → setMark -------------------------------------------------------
  if (step instanceof AddMarkStep) {
    return classifyMarkStep(step, docBefore, 'setMark');
  }

  // --- RemoveMarkStep → clearMark --------------------------------------------------
  if (step instanceof RemoveMarkStep) {
    return classifyMarkStep(step, docBefore, 'clearMark');
  }

  // --- AttrStep → setBlockAttr (alignment case; other attrs deferred to task 031) --
  if (step instanceof AttrStep) {
    const node = docBefore.nodeAt(step.pos);
    if (isParaIdBlock(node)) {
      const paraId = (node.attrs as { paraId: string }).paraId;
      if (step.attr === 'textAlign') {
        const value = step.value == null ? null : (TEXTALIGN_TO_COMPOSE[String(step.value)] ?? null);
        const op: ComposeOperation = {
          type: 'setBlockAttr',
          paraId,
          attr: 'Alignment' as ComposeBlockAttr,
          value,
        };
        return { kind: 'ops', ops: [op] };
      }
    }
    return { kind: 'defer-structural', step, reason: `attr-step:${step.attr}` };
  }

  // --- ReplaceAroundStep (list wrap/unwrap, blockquote, para split/merge) → structural (task 031) ----
  if (step instanceof ReplaceAroundStep) {
    return classifyStructuralStep(step, docBefore, 'replace-around-step');
  }

  // --- Any other core step (AddNodeMark / RemoveNodeMark / DocAttr / unknown) ------
  // These correspond to block/structural changes handled in task 031, not a schema gap.
  return { kind: 'defer-structural', step, reason: `unhandled-step:${step.constructor?.name ?? 'unknown'}` };
}

/** Narrow a {@link ComposeAnchor} to its {@link ComposeRunPoint} (drops the paraId, which lives on the op base). */
function pointOf(anchor: ComposeAnchor): ComposeRunPoint {
  return { runIndex: anchor.runIndex, offset: anchor.offset };
}

/** Shared classifier for Add/Remove mark steps → setMark / clearMark. */
function classifyMarkStep(
  step: AddMarkStep | RemoveMarkStep,
  docBefore: PMNode,
  type: 'setMark' | 'clearMark',
): StepClassification {
  const composeMark = TIPTAP_MARK_TO_COMPOSE[step.mark.type.name];
  if (!composeMark) {
    // A formatting mark the closed ComposeMarkType set cannot carry — a genuine schema
    // gap. Surface it (escalation seam), never silently drop or mis-map.
    return { kind: 'unrepresentable', step, reason: `mark-outside-closed-set:${step.mark.type.name}` };
  }
  const $from = docBefore.resolve(step.from);
  const $to = docBefore.resolve(step.to);
  if (!$from.sameParent($to) || !isParaIdBlock($from.parent) || !$from.parent.isTextblock) {
    // Multi-paragraph mark span decomposes into per-paragraph ops — task 031.
    return { kind: 'defer-structural', step, reason: 'mark-step-cross-paragraph' };
  }
  const anchorFrom = resolveRunAnchor(docBefore, step.from);
  const anchorTo = resolveRunAnchor(docBefore, step.to);
  if (!anchorFrom || !anchorTo || anchorFrom.paraId !== anchorTo.paraId) {
    return { kind: 'defer-structural', step, reason: 'mark-step-anchor-unresolved' };
  }
  const op: ComposeOperation = {
    type,
    paraId: anchorFrom.paraId,
    range: { start: pointOf(anchorFrom), end: pointOf(anchorTo) },
    mark: composeMark,
  };
  return { kind: 'ops', ops: [op] };
}

/** Default opaque-atom predicate — schema-driven (`atom: true`), decoupled from task 021's node name. */
function defaultIsOpaqueAtom(node: PMNode): boolean {
  return node.type.spec.atom === true;
}

// ---------------------------------------------------------------------------
// The TipTap extension (headless — registers ONE ProseMirror plugin)
// ---------------------------------------------------------------------------

/** Stable plugin key. Transactions may `tr.setMeta(STEP_INTERCEPTOR_IGNORE_META, true)` to opt out of capture. */
export const stepOperationInterceptorPluginKey = new PluginKey('composeStepOperationInterceptor');

/** Meta flag a transaction can carry to be SKIPPED by the interceptor (e.g. programmatic non-user mutations). */
export const STEP_INTERCEPTOR_IGNORE_META = 'composeStepInterceptorIgnore';

/**
 * Build the read-only ProseMirror plugin. Via `appendTransaction` it walks each applied
 * doc-changing transaction's steps against the correct pre-step document (`tr.docs[i]`)
 * and emits task-003 operations — WITHOUT ever appending a transaction of its own
 * (capture only; it never mutates the doc). Exported so the capture path is unit-testable
 * against a plain `EditorState` (no TipTap Editor / DOM mount required).
 */
export function createStepInterceptorPlugin(options: StepOperationInterceptorOptions): Plugin {
  return new Plugin({
    key: stepOperationInterceptorPluginKey,
    appendTransaction(transactions, _oldState, _newState) {
      for (const tr of transactions) {
        if (!tr.docChanged) continue;
        if (tr.getMeta(STEP_INTERCEPTOR_IGNORE_META)) continue;

        const collected: ComposeOperation[] = [];
        const steps = tr.steps;
        for (let i = 0; i < steps.length; i++) {
          const step = steps[i];
          const docBefore = tr.docs[i] ?? _oldState.doc;
          const cls = classifyStep(step, docBefore, options);
          switch (cls.kind) {
            case 'ops':
              collected.push(...cls.ops);
              break;
            case 'refused-atom':
              options.onRefusedAtomEdit?.(cls.step);
              break;
            case 'defer-structural':
              options.onStructuralStep?.(cls.step, cls.reason);
              break;
            case 'unrepresentable':
              options.onUnrepresentableStep?.(cls.step, cls.reason);
              break;
          }
        }
        if (collected.length > 0) {
          options.onOperations?.(collected, { transaction: tr });
        }
      }
      // Read-only interceptor — never append a transaction.
      return undefined;
    },
  });
}

/**
 * The interceptor extension. Registers ONE read-only ProseMirror plugin (see
 * {@link createStepInterceptorPlugin}) that captures transaction steps as task-003
 * operations without mutating the doc.
 *
 * Registered bare (default options) by ComposeEditor for task 020 — wire-in only. The
 * save/rebase path (task 022+) supplies `onOperations` to consume the captured log.
 */
export const StepOperationInterceptor = Extension.create<StepOperationInterceptorOptions>({
  name: 'composeStepOperationInterceptor',

  addOptions() {
    return {
      onOperations: undefined,
      onRefusedAtomEdit: undefined,
      onStructuralStep: undefined,
      onUnrepresentableStep: undefined,
      isOpaqueAtom: undefined,
    };
  },

  addProseMirrorPlugins() {
    return [createStepInterceptorPlugin(this.options)];
  },
});

/**
 * Additive registration array (matches the ComposeEditor convention of one array per
 * additive extension group, e.g. COMPOSE_R2_QA_HIGHLIGHT). Spread into the editor's
 * extension list alongside the LOCKED Spike #1 set — never mutates that list.
 */
export const COMPOSE_R4_STEP_INTERCEPTOR = [StepOperationInterceptor];

export default StepOperationInterceptor;

// ===========================================================================
// Rebased operation log (spaarkeai-compose-r4 task 022, FR-03) — extends the
// task-020 interceptor above; NOT a new surface.
// ===========================================================================
//
// Maintains an ORDERED, REBASED operation log per dirty editing session. As the
// user keeps editing, each subsequent transaction's ProseMirror `Mapping`
// (`notes/bridge-prior-art.md` §1 — `Mapping.map(pos,assoc)`/`mapResult`) rebases
// the tracked anchor position(s) of every ALREADY-LOGGED operation forward, so
// the log stays internally consistent with the live document. On save, the
// client sends the ORDERED rebased log + the base version (the Phase 5 save
// path, task 050, supplies the actual fetch — this module only produces the
// `{orderedOps, baseVersion}` shape it will transmit).
//
// NEVER-SILENTLY-DROP (mirrors `AnnotationReanchorService`'s AUTO/REVIEW/ORPHAN
// discipline, `src/server/api/Sprk.Bff.Api/Services/Compose/AnnotationReanchorService.cs`):
// when a later edit deletes the content an earlier op's anchor points into,
// `MapResult.deleted` flags the op — it stays in the log (`deletedContentFlag:
// true`) for review, never removed.
//
// REBASING IS POSITION-MAPPING ONLY (I-7, D2): every rebase call is
// `Mapping.mapResult(pos, assoc)` against a raw ProseMirror position — never a
// text-search / content-match. The (paraId, runIndex, run-local-offset) anchor
// on each logged op is RE-DERIVED (via `resolveRunAnchor`, the same D2 resolver
// task 020 defines above) from the tracked position at serialize time, so it is
// always re-derivable against the CURRENT document, never a stale absolute
// editor position.

/** A single tracked ProseMirror position + the `assoc` side ProseMirror mapping uses at a boundary. */
interface TrackedPoint {
  /** Absolute ProseMirror position, valid in the doc as of the last rebase (or capture). */
  pos: number;
  /**
   * Boundary association used by `Mapping.mapResult` (ProseMirror convention): `-1` sticks to
   * content BEFORE an exact-boundary insertion (used for an insertion point and a range's `start`
   * — the point should not be pushed forward by content inserted exactly there); `1` sticks to
   * content AFTER (used for a range's `end`, so the range naturally grows to include content
   * inserted exactly at its trailing edge).
   */
  assoc: -1 | 1;
}

/** Which tracked point(s) a logged op's anchor is rebased through — shape depends on the op's own anchor kind. */
type ComposeOpAnchor =
  /** `insertText`'s single `at` insertion point. */
  | { kind: 'point'; point: TrackedPoint }
  /** An intra-paragraph run-local RANGE (`deleteRange` / `replaceRange` / `setMark` / `clearMark`). */
  | { kind: 'range'; start: TrackedPoint; end: TrackedPoint }
  /** `setBlockAttr`'s paragraph-NODE position (no run offset — the op is paragraph-scoped). */
  | { kind: 'block'; point: TrackedPoint };

/**
 * One entry in the rebased operation log: the task-003 {@link ComposeOperation} (kept current —
 * re-derived from the tracked anchor at serialize time) plus the never-silently-drop flag.
 */
export interface ComposeLoggedOperation {
  /** The operation, with its `paraId`/anchor fields re-derived from the CURRENT document. */
  operation: ComposeOperation;
  /**
   * `true` once this op's tracked anchor has landed inside content a LATER edit in this session
   * deleted (`MapResult.deleted`) — surfaced for review, per the never-silently-drop discipline.
   * The op remains in `orderedOps`; the save/apply path (Phase 5+) MUST NOT apply a flagged op
   * without review.
   */
  deletedContentFlag: boolean;
}

/** The `{orderedOps, baseVersion}` shape the save path (Phase 5, task 050) will transmit. */
export interface ComposeOperationLogSnapshot {
  /**
   * Captured operations in DOCUMENT order (ascending resolved position; an op's original capture
   * order breaks ties at the same position — see the `onAmbiguousOrder` escalation seam below).
   */
  orderedOps: ComposeLoggedOperation[];
  /** The opaque base-version handle carried from load (SPE eTag + projection schema version), or `null` before {@link RebasedOperationLog.setBaseVersion} is called. */
  baseVersion: string | null;
}

interface ComposeOpLogEntry {
  /** Capture (chronological) order — the document-order sort's stable tie-breaker. */
  seq: number;
  anchor: ComposeOpAnchor;
  op: ComposeOperation;
  deletedContentFlag: boolean;
}

export interface RebasedOperationLogOptions extends StepOperationInterceptorOptions {
  /**
   * Escalation seam (root §6/§6.5): called when two DIFFERENT logged ops resolve to the exact
   * same document position at serialize time — a genuine ordering ambiguity rebasing cannot
   * disambiguate. The log still serializes (falling back to capture order, a deterministic but
   * UNVERIFIED tie-break) — this callback is how the ambiguity is surfaced, never silently guessed.
   */
  onAmbiguousOrder?: (a: ComposeOperation, b: ComposeOperation) => void;
}

/** Map one {@link TrackedPoint} through a `Mapping`, returning the rebased point + whether it now lands in deleted content. */
function remapPoint(point: TrackedPoint, mapping: Mapping): { point: TrackedPoint; deleted: boolean } {
  const result = mapping.mapResult(point.pos, point.assoc);
  return { point: { pos: result.pos, assoc: point.assoc }, deleted: result.deleted };
}

/** Map a full {@link ComposeOpAnchor} through a `Mapping` (all its tracked points), OR-ing the deleted signal. */
function remapAnchor(anchor: ComposeOpAnchor, mapping: Mapping): { anchor: ComposeOpAnchor; deleted: boolean } {
  if (anchor.kind === 'range') {
    const start = remapPoint(anchor.start, mapping);
    const end = remapPoint(anchor.end, mapping);
    return { anchor: { kind: 'range', start: start.point, end: end.point }, deleted: start.deleted || end.deleted };
  }
  const mapped = remapPoint(anchor.point, mapping);
  return { anchor: { kind: anchor.kind, point: mapped.point }, deleted: mapped.deleted };
}

/** The absolute position used to order a logged entry in the serialized (document-order) log. */
function primaryPositionOf(anchor: ComposeOpAnchor): number {
  return anchor.kind === 'range' ? anchor.start.pos : anchor.point.pos;
}

/**
 * Derive the RAW (pre-step, `docBefore`-space) anchor for a just-classified op from the `Step`
 * that produced it, mapped through the REST of the owning transaction (`mapping.slice(stepIndex)`
 * — the step's own map plus every later step's map in the same transaction) so the tracked
 * point(s) land in `tr.doc` (the transaction's FINAL document) — the same coordinate space every
 * subsequent transaction's rebase (`remapAnchor`) continues from.
 */
function buildAnchor(op: ComposeOperation, step: Step, mapping: Mapping, stepIndex: number): ComposeOpAnchor | null {
  const toFinal = (pos: number, assoc: -1 | 1): TrackedPoint => ({
    pos: mapping.slice(stepIndex).map(pos, assoc),
    assoc,
  });

  if (
    op.type === 'splitParagraph' ||
    op.type === 'mergeParagraph' ||
    op.type === 'insertParagraph' ||
    op.type === 'deleteParagraph'
  ) {
    // A structural op is paragraph-scoped (no run offset) — anchor it at the step's block-boundary position so
    // the rebasing pass still flags it if a later edit deletes that position (never-silently-drop). Its durable
    // payload paraIds (paraId / targetParaId / newParaId) are Word-native and carried as-captured.
    if (step instanceof ReplaceStep || step instanceof ReplaceAroundStep) {
      return { kind: 'block', point: toFinal(step.from, -1) };
    }
    return null;
  }

  if (step instanceof ReplaceStep) {
    if (op.type === 'insertText') {
      // The insertion point sticks to content BEFORE it (assoc -1) — a caret-position anchor that
      // is NOT pushed forward by its own inserted text, so it continues to mean "insert here".
      return { kind: 'point', point: toFinal(step.from, -1) };
    }
    if (op.type === 'deleteRange' || op.type === 'replaceRange') {
      return { kind: 'range', start: toFinal(step.from, -1), end: toFinal(step.to, 1) };
    }
    return null;
  }
  if ((step instanceof AddMarkStep || step instanceof RemoveMarkStep) && (op.type === 'setMark' || op.type === 'clearMark')) {
    return { kind: 'range', start: toFinal(step.from, -1), end: toFinal(step.to, 1) };
  }
  if (step instanceof AttrStep && op.type === 'setBlockAttr') {
    // A NODE position (the paragraph itself), not a text offset — assoc -1 keeps it anchored to
    // the node's own start rather than drifting past it.
    return { kind: 'block', point: toFinal(step.pos, -1) };
  }
  return null;
}

/**
 * Re-derive a logged op's `paraId`/anchor fields from the CURRENT document at its tracked
 * position(s), via the SAME `(paraId, runIndex, run-local-offset)` resolver task 020 defines
 * (`resolveRunAnchor`) — never a text-search (I-7). Returns `null` when re-derivation fails (the
 * tracked position no longer resolves to a paraId-bearing block, e.g. its paragraph was removed
 * entirely) — the caller treats a `null` re-derivation as an additional never-silently-drop signal
 * (flagged, not dropped), same as an explicit `MapResult.deleted`.
 */
function deriveOperation(op: ComposeOperation, anchor: ComposeOpAnchor, doc: PMNode): ComposeOperation | null {
  switch (op.type) {
    case 'insertText': {
      if (anchor.kind !== 'point') return null;
      const a = resolveRunAnchor(doc, anchor.point.pos);
      if (!a) return null;
      return { ...op, paraId: a.paraId, at: pointOf(a) };
    }
    case 'deleteRange':
    case 'replaceRange':
    case 'setMark':
    case 'clearMark': {
      if (anchor.kind !== 'range') return null;
      const s = resolveRunAnchor(doc, anchor.start.pos);
      const e = resolveRunAnchor(doc, anchor.end.pos);
      if (!s || !e || s.paraId !== e.paraId) return null;
      return { ...op, paraId: s.paraId, range: { start: pointOf(s), end: pointOf(e) } } as ComposeOperation;
    }
    case 'setBlockAttr': {
      if (anchor.kind !== 'block') return null;
      const node = doc.nodeAt(anchor.point.pos);
      if (!isParaIdBlock(node)) return null;
      return { ...op, paraId: (node.attrs as { paraId: string }).paraId };
    }
    case 'splitParagraph':
    case 'mergeParagraph':
    case 'insertParagraph':
    case 'deleteParagraph':
      // Structural ops carry durable `w14:paraId`s in their payload (paraId / targetParaId / newParaId),
      // captured from the pre-step doc — Word-native ids, NOT re-derivable from a live document position. Return
      // the op as-captured; the rebasing `deletedContentFlag` still guards it (never-silently-drop).
      return op;
    default:
      return null;
  }
}

/**
 * The per-dirty-session ORDERED, REBASED operation log (FR-03, task 022). Wraps task-020's
 * step→operation capture (`classifyStep`): every doc-changing transaction (1) rebases every
 * ALREADY-LOGGED entry's tracked anchor through that transaction's `Mapping` — flagging (never
 * dropping) any entry whose anchor now falls inside content the transaction deleted — then
 * (2) classifies the transaction's OWN steps and appends their operations, tracked from their
 * post-transaction position onward. {@link serialize} re-derives every entry's `(paraId,
 * runIndex, run-local-offset)` from the CURRENT document and returns the ordered
 * `{orderedOps, baseVersion}` shape the Phase 5 save path will transmit.
 *
 * This class does NOT fetch or save (ADR-028 — no client fetch here); it does NOT wire into
 * `ComposeEditor.tsx` (that remains task 020's bare, handler-less registration) — a future
 * save-path task instantiates and drives it (e.g. from a `Plugin`'s `appendTransaction`, mirroring
 * {@link createStepInterceptorPlugin}, or directly from a host's transaction dispatch hook).
 */
export class RebasedOperationLog {
  private entries: ComposeOpLogEntry[] = [];
  private seqCounter = 0;
  private baseVersion: string | null = null;
  private readonly classifierOptions: StepOperationInterceptorOptions;
  private readonly onAmbiguousOrder?: RebasedOperationLogOptions['onAmbiguousOrder'];

  constructor(options: RebasedOperationLogOptions = {}) {
    const { onAmbiguousOrder, ...classifierOptions } = options;
    this.onAmbiguousOrder = onAmbiguousOrder;
    this.classifierOptions = classifierOptions;
  }

  /**
   * Set the opaque base-version handle carried from load (SPE eTag + projection schema version).
   * Idempotent — the FIRST call wins (load happens once per dirty session); later calls are no-ops
   * so an accidental re-invocation mid-session cannot silently swap the version a save will assert
   * against.
   */
  setBaseVersion(version: string): void {
    if (this.baseVersion === null) this.baseVersion = version;
  }

  /** The current entry count (session-scoped; includes flagged entries — they are never dropped). */
  get size(): number {
    return this.entries.length;
  }

  /**
   * Clear the accumulated log (spaarkeai-compose-r4 task 032). Called (1) after a fresh document loads
   * into the editor — the load `setContent`/import transactions are NOT user edits, so any ops they
   * produced must be dropped so the log stays aligned to the load-time reject-state baseline — and (2)
   * after a save serializes + persists the log, so the next dirty session starts empty and already-applied
   * ops are never re-sent onto the new baseline (double-apply). `baseVersion` is preserved (it is carried
   * from the one load per dirty session; the save path re-sends it as `baselineVersionId`).
   */
  reset(): void {
    this.entries = [];
    this.seqCounter = 0;
  }

  /**
   * The seq the NEXT appended entry will carry — a stable high-water mark captured at serialize time so
   * {@link commitSaved} can later drop exactly the serialized batch while preserving edits appended
   * AFTER the serialize (spaarkeai-compose-r4 task 038: concurrent edits made during an in-flight save
   * must not be discarded when that save confirms).
   */
  get nextSeq(): number {
    return this.seqCounter;
  }

  /**
   * spaarkeai-compose-r4 task 038 (zero-error guardrails): after a serialized batch has PERSISTED (the
   * save POST returned 200), drop the entries that were part of it — those captured before the
   * `throughSeq` high-water mark ({@link nextSeq} read at serialize time) — while PRESERVING any entries
   * appended AFTER the serialize (concurrent edits made during the in-flight save). This replaces the
   * former "serialize + full {@link reset} BEFORE the POST" sequencing, which emptied the log up-front so
   * a rejected (422) save left nothing to retry: the retry re-sent an empty log and every valid text edit
   * in that batch was lost on reload. A failed save simply never calls this, so the batch stays intact for
   * the retry. `baseVersion` + `seqCounter` are preserved (the dirty session continues).
   */
  commitSaved(throughSeq: number): void {
    this.entries = this.entries.filter(entry => entry.seq >= throughSeq);
  }

  /**
   * Process ONE doc-changing transaction: rebase every already-logged entry through its
   * `Mapping`, then classify + append the transaction's own operations. Returns the operations
   * newly appended by THIS transaction (empty for a transaction that only rebases, e.g. deferred
   * structural / refused-atom / unrepresentable steps, or a no-op transaction).
   */
  recordTransaction(tr: Transaction): ComposeOperation[] {
    if (!tr.docChanged) return [];
    if (tr.getMeta(STEP_INTERCEPTOR_IGNORE_META)) return [];

    // (1) Rebase every already-logged entry through this transaction's Mapping — BEFORE this
    // transaction's own new ops are appended (a transaction's own steps never rebase themselves).
    for (const entry of this.entries) {
      const remapped = remapAnchor(entry.anchor, tr.mapping);
      entry.anchor = remapped.anchor;
      if (remapped.deleted) entry.deletedContentFlag = true;
    }

    // (2) Classify + append this transaction's own steps.
    const appended: ComposeOperation[] = [];
    const steps = tr.steps;
    for (let i = 0; i < steps.length; i++) {
      const step = steps[i];
      const docBefore = tr.docs[i] ?? tr.before;
      const cls = classifyStep(step, docBefore, this.classifierOptions);
      switch (cls.kind) {
        case 'ops':
          for (const op of cls.ops) {
            const anchor = buildAnchor(op, step, tr.mapping, i);
            if (!anchor) continue; // defensive — every op type classifyStep emits has an anchor shape above
            this.entries.push({ seq: this.seqCounter++, anchor, op, deletedContentFlag: false });
            appended.push(op);
          }
          break;
        case 'refused-atom':
          this.classifierOptions.onRefusedAtomEdit?.(cls.step);
          break;
        case 'defer-structural':
          this.classifierOptions.onStructuralStep?.(cls.step, cls.reason);
          break;
        case 'unrepresentable':
          this.classifierOptions.onUnrepresentableStep?.(cls.step, cls.reason);
          break;
      }
    }
    return appended;
  }

  /**
   * Serialize the log against `doc` (the current document): re-derive every entry's `paraId`/
   * anchor fields, sort into DOCUMENT order (ascending resolved position; capture order breaks
   * ties), and return the `{orderedOps, baseVersion}` shape the save path will transmit. An entry
   * whose re-derivation fails (paragraph removed entirely) is ALSO flagged — never dropped from
   * `orderedOps`.
   */
  serialize(doc: PMNode): ComposeOperationLogSnapshot {
    const rows = this.entries.map(entry => {
      const derived = deriveOperation(entry.op, entry.anchor, doc);
      const loggedOp: ComposeLoggedOperation = {
        operation: derived ?? entry.op,
        deletedContentFlag: entry.deletedContentFlag || derived === null,
      };
      return { seq: entry.seq, primaryPos: primaryPositionOf(entry.anchor), loggedOp };
    });

    rows.sort((a, b) => a.primaryPos - b.primaryPos || a.seq - b.seq);

    // Escalation seam: two DIFFERENT ops resolving to the identical position is a genuine ordering
    // ambiguity rebasing cannot disambiguate — surface it (never silently guess); the deterministic
    // capture-order tie-break above still lets the log serialize.
    for (let i = 1; i < rows.length; i++) {
      if (rows[i].primaryPos === rows[i - 1].primaryPos) {
        this.onAmbiguousOrder?.(rows[i - 1].loggedOp.operation, rows[i].loggedOp.operation);
      }
    }

    return { orderedOps: rows.map(r => r.loggedOp), baseVersion: this.baseVersion };
  }
}

/**
 * Convenience plugin factory mirroring {@link createStepInterceptorPlugin}: wires a
 * {@link RebasedOperationLog} into `appendTransaction` so it accumulates automatically as the
 * user edits. NOT registered by task 020's bare `COMPOSE_R4_STEP_INTERCEPTOR` array — a future
 * save-path task registers this (or drives `RebasedOperationLog` directly) once the save wiring
 * lands (Phase 5, task 050+).
 */
export function createRebasedOperationLogPlugin(log: RebasedOperationLog): Plugin {
  return new Plugin({
    key: new PluginKey('composeRebasedOperationLog'),
    appendTransaction(transactions) {
      for (const tr of transactions) {
        log.recordTransaction(tr);
      }
      return undefined; // read-only — never appends a transaction of its own
    },
  });
}
