/**
 * docxBridge — DOCX ↔ TipTap conversion helpers.
 *
 * Project:     spaarkeai-compose-r1, task 045 (Phase 4 W4).
 * Locked spec: `projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md`
 *
 * Task 013 (spaarkeai-compose-fidelity-r4.5, F-2 "one reader"): the client-side IMPORT path
 * (`docxToTipTapHtml`, DOCX → HTML → TipTap via mammoth ^1.8.0 BSD-2-Clause) has been DELETED —
 * every Compose entry path (Load, Upload, Browse, open-in-Compose) now hydrates a server-side
 * `ComposeDocxProjection` (`ComposeDocxProjectionBuilder`, `Sprk.Bff.Api`) instead. This module is
 * now EXPORT-side only: TipTap JSON → structured content model (`buildContentModel`), consumed by
 * the server-side `.docx` renderer. It also still owns the load-time paraId carry helpers
 * (`stampParaIds`, `captureParaIdSnapshot`, `buildBaselineParaIdMap`), which are reader-agnostic —
 * they stamp/snapshot ids on whatever HTML the editor just mounted (formerly mammoth's HTML, now
 * the server projection's HTML) and are unchanged by the mammoth removal.
 *
 * `mammoth` remains a REPO dependency (SprkChat + Notepad, in `@spaarke/ui-components`, still use
 * it) — only the Compose call site was removed; the package itself is not uninstalled.
 *
 * Round-trip fidelity is governed by the **LOCKED Spike #1 OOB subset**
 * (§3.2 of the spike artifact). Features classified "Preserved" survive;
 * "Degraded" survive with documented loss; "Dropped" are silently removed
 * on import (R1; R2 adds import-time warnings). Multi-level numbering is
 * the most consequential R1 limitation — `Open in Word` (FR-12) is the
 * documented escape hatch.
 *
 * Privacy (ADR-015 Tier 3): document text payloads pass through these
 * helpers in-memory only. NO logging of document content.
 *
 * This module is export-side ONLY. It does NOT speak to
 * the BFF, does NOT speak to SPE, does NOT speak to Microsoft Graph.
 * Document bytes arrive from the host (via `ComposeEditor` props) and
 * return to the host. SPE plumbing lives in `ComposeDocumentService` /
 * Compose BFF endpoints.
 *
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md §3 (extension inventory) + §4 (library choice) + §4.5 (client-side conversion strategy)
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-prototype/src/exportDocx.ts (docx wiring reference)
 * @see projects/spaarkeai-compose-fidelity-r4.5/design.md §4 WS-1 (server projection / F-2 one reader)
 */

import type { Editor } from '@tiptap/core';
import type {
  ParaIdMapEntry,
  ComposeBaselineParaId,
  ComposeContentModel,
  ComposeContentBlock,
  ComposeInlineRun,
  ComposeTable,
  ComposeTableRow,
  ComposeTableCell,
  ComposeAlignment,
  ComposeComment,
  ComposeRevisionFact,
} from '../types/compose-contracts';
// R6 (task 012): the render-on-save imported-doc mapper redlines NON-marked user edits by diffing the
// current reject-state text against the load-time baseline — the SAME word-level LCS engine the live
// Track Changes decoration overlay uses, so what the save persists is what the overlay showed.
import { diffTokens, diffToRegions } from '../widgets/hooks/trackChangesDiff';

// ---------------------------------------------------------------------------
// R3 FR-08/FR-09/FR-10 — `w14:paraId` identity carry (design §5)
// ---------------------------------------------------------------------------
//
// Task 013: the client-side docx→HTML reader (formerly mammoth's `docxToTipTapHtml`, deleted per
// F-2 "one reader") is gone — the paragraph ids now ALWAYS arrive via the server pre-parse map
// (task 010), never from a client-side HTML conversion. `stampParaIds` owns the client-side carry: an
// EXPLICIT transaction stamping the server ids onto the paragraph nodes
// immediately after `setContent` (FR-09: load-time ids are server-owned, never
// left to the minting extension's auto-assign). The OOXML-shaped id generator for
// in-editor split minting lives with the extension in `../widgets/paraIdExtension`.

/**
 * Stamp the server pre-parse paraId map onto the editor's paraId-bearing nodes via
 * a single EXPLICIT transaction, in document order (FR-09). Call immediately after
 * `editor.commands.setContent(html)` on a docx Load: the Nth paraId-bearing node
 * (`paragraph` or `heading` — see `PARAID_NODE_TYPES`; in ProseMirror document
 * order, which — like the server's `body.Descendants<Paragraph>()` — descends into
 * table cells and counts headings, since an OOXML heading is a `<w:p>`) receives
 * `map[N].paraId`.
 *
 * Why explicit (not auto-assign): `@tiptap/extension-unique-id` DOES mint ids for
 * id-less nodes on content change, but those would be random client ids, not the
 * server's. Stamping explicitly AFTER setContent makes the server ids win, so the
 * editor's load-time identity is the same substrate the retained-original save
 * splices against (FR-12). Untouched paragraphs then keep these ids; a later split
 * re-mints via the extension's built-in dedup (FR-10).
 *
 * The stamp is marked `addToHistory:false` — a load-time identity stamp is not a
 * user edit and must not be undoable or flip the dirty flag.
 *
 * No-op when the map is empty (e.g. an AI-drafted seed with no server pre-parse —
 * there the extension's own minting assigns fresh OOXML-shaped ids).
 *
 * @param editor  Live TipTap Editor (paraId attribute must be in schema — the
 *                UniqueID extension is configured for `paragraph` in ComposeEditor)
 * @param map     Ordered server paraId map from the Load response (task 010)
 */
export function stampParaIds(editor: Editor, map: readonly ParaIdMapEntry[] | undefined): void {
  if (!map || map.length === 0) return;
  const { state } = editor;
  const tr = state.tr;
  let paraIndex = 0;
  let changed = false;
  state.doc.descendants((node, pos) => {
    // Match the SAME node set as the paraId extension's `PARAID_NODE_TYPES`
    // (paragraph + heading): the server map counts every OOXML `<w:p>`, and a
    // heading is a `<w:p>`, so headings must be counted here too or every id
    // after the first heading misaligns. Keep in sync with paraIdExtension.ts.
    if (node.type.name === 'paragraph' || node.type.name === 'heading') {
      const entry = map[paraIndex];
      if (entry && entry.paraId && node.attrs.paraId !== entry.paraId) {
        // Positions are stable across attribute-only edits, so the pos captured
        // during this walk stays valid for every setNodeMarkup on `tr`. Use
        // setNodeMarkup (the same idiom @tiptap/extension-unique-id uses) to set
        // the attribute — universally available and it preserves the other attrs.
        tr.setNodeMarkup(pos, undefined, { ...node.attrs, paraId: entry.paraId });
        changed = true;
      }
      paraIndex++;
    }
    return true; // descend into children (table cells hold paragraphs — server parity)
  });
  if (changed) {
    tr.setMeta('addToHistory', false);
    editor.view.dispatch(tr);
  }
}

// ---------------------------------------------------------------------------
// Export path (R3 FR-01/FR-01a, task 027): the client sends a STRUCTURED,
// paraId-keyed content model — it never authors `.docx` bytes. `docx.js` is
// removed; the SERVER owns all authoring (delta-onto-original via the
// synthesizer for loaded docs, full render via ComposeDocumentRenderer for
// born-in-editor docs).
// ---------------------------------------------------------------------------

/**
 * TipTap JSON node shape (subset — covers the OOB extensions in scope). Retained for the paraId
 * snapshot / edited-paragraph / content-model helpers below (all operate on `editor.getJSON()`).
 */
export type TipTapNode = {
  type?: string;
  content?: TipTapNode[];
  text?: string;
  attrs?: Record<string, unknown>;
  marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
};

/** The paraId-bearing block node types (mirror `PARAID_NODE_TYPES` / the server's `<w:p>` count). */
const BLOCK_NODE_TYPES = new Set(['paragraph', 'heading']);

/**
 * The REJECT-STATE settled text of a block node's inline content — the text as if every PENDING AI
 * redline were REJECTED: text carrying an `insertion` mark (a proposed insert) is DROPPED; all other
 * text (original, accepted edits — accept unsets the marks → normal text — and deletion-marked
 * originals) is KEPT. This is the same reduction the retired `buildRejectBaselineJson` did, applied
 * per-paragraph: it is what a dirty-save delta must diff against so a pending redline is NOT baked
 * into the synthesizer delta (it rides the `annotations` list instead — the server composes it, task 023).
 */
function rejectStateText(block: TipTapNode): string {
  let out = '';
  const walk = (n: TipTapNode): void => {
    if (n.type === 'text') {
      const isPendingInsert = n.marks?.some(m => m.type === 'insertion') ?? false;
      if (!isPendingInsert && n.text) out += n.text;
      return;
    }
    if (n.type === 'hardBreak') {
      out += '\n';
      return;
    }
    (n.content ?? []).forEach(walk);
  };
  (block.content ?? []).forEach(walk);
  return out;
}

/** Visit every paraId-bearing block (paragraph/heading), descending into lists + table cells (server parity). */
function forEachBlock(node: TipTapNode, fn: (block: TipTapNode) => void): void {
  if (node.type && BLOCK_NODE_TYPES.has(node.type)) {
    fn(node);
    return; // don't descend into a block's own inline content
  }
  (node.content ?? []).forEach(child => forEachBlock(child, fn));
}

/**
 * Capture the LOAD-TIME `{ paraId → reject-state text }` snapshot for the editor, in document order.
 * Call immediately after {@link stampParaIds} on a load (or after a born-in-editor seed's setContent).
 * R4 (task 023): the load-time snapshot now feeds {@link buildBaselineParaIdMap} for the C2 minted-id
 * stamp; the settled-text DIFF itself is superseded by the step interceptor's operation log
 * (`stepOperationInterceptor.ts`) — the retired paragraph-diff export (`collectEditedParagraphs`) that
 * used to diff against this snapshot was removed in task 023 (FR-06 client-half cleanup).
 */
export function captureParaIdSnapshot(editor: Editor): Map<string, string> {
  const snapshot = new Map<string, string>();
  forEachBlock(editor.getJSON() as TipTapNode, block => {
    const paraId = block.attrs?.paraId as string | undefined;
    if (paraId) snapshot.set(paraId, rejectStateText(block));
  });
  return snapshot;
}

/**
 * C2 fix (UAT 2026-07-20): build the ordered baseline paraId map the save sends so the SERVER can stamp
 * MINTED ids physically onto the retained-original baseline's id-less paragraphs before the synthesizer
 * resolves (see `ComposeBaselineParaIdStamper`). Sourced from the LOAD-TIME {@link captureParaIdSnapshot}
 * snapshot — document order, `paraId` → reject-state text — so each entry's `text` is exactly the baseline
 * paragraph's text (the server's verification key), NOT the post-edit text. One entry per snapshot
 * paragraph; `index` is its zero-based document-order position (the snapshot Map preserves insertion =
 * document order). Returns `[]` for an absent/empty snapshot (e.g. a born-in-editor doc — the server
 * renders its ids and the stamp is a no-op there anyway).
 *
 * Privacy (ADR-015 Tier 3): the text is document content — carried to the save request only, NEVER logged.
 */
export function buildBaselineParaIdMap(
  snapshot: ReadonlyMap<string, string> | null | undefined
): ComposeBaselineParaId[] {
  if (!snapshot || snapshot.size === 0) return [];
  const map: ComposeBaselineParaId[] = [];
  let index = 0;
  for (const [paraId, text] of snapshot) {
    if (paraId) map.push({ index, paraId, text });
    index++;
  }
  return map;
}

/**
 * Build the full paraId-keyed {@link ComposeContentModel} from the editor for a BORN-IN-EDITOR save
 * (FR-01a) — the server renders it into a high-fidelity `.docx`. Mirrors the server model: paragraphs /
 * headings (with level) / list items (flattened from bulletList/orderedList with nesting depth) /
 * native tables, each block carrying its `paraId` + inline runs (bold/italic/underline). Pending-insert
 * text is excluded (reject-state parity) — a born-in-editor draft normally has no redlines.
 */
export function buildContentModel(editor: Editor): ComposeContentModel {
  const blocks: ComposeContentBlock[] = [];
  for (const node of (editor.getJSON() as TipTapNode).content ?? []) {
    appendContentBlocks(node, blocks, 0);
  }
  return { blocks };
}

function appendContentBlocks(node: TipTapNode, out: ComposeContentBlock[], listDepth: number): void {
  switch (node.type) {
    case 'paragraph':
      out.push({ kind: 'Paragraph', paraId: paraIdOf(node), runs: runsOf(node), alignment: alignmentOf(node) });
      break;
    case 'heading':
      out.push({
        kind: 'Heading',
        level: (node.attrs?.level as number | undefined) ?? 1,
        paraId: paraIdOf(node),
        runs: runsOf(node),
        alignment: alignmentOf(node),
      });
      break;
    case 'bulletList':
    case 'orderedList': {
      const ordered = node.type === 'orderedList';
      let firstItem = true;
      for (const item of node.content ?? []) {
        if (item.type !== 'listItem') continue;
        for (const child of item.content ?? []) {
          if (child.type === 'paragraph' || child.type === 'heading') {
            out.push({
              kind: 'ListItem',
              level: listDepth,
              ordered,
              // A top-level ordered list restarts numbering at 1 on its first item.
              startsNewList: ordered && firstItem && listDepth === 0,
              paraId: paraIdOf(child),
              runs: runsOf(child),
            });
            firstItem = false;
          } else if (child.type === 'bulletList' || child.type === 'orderedList') {
            appendContentBlocks(child, out, listDepth + 1);
          }
        }
      }
      break;
    }
    case 'table':
      out.push({ kind: 'Table', table: tableOf(node) });
      break;
    default:
      // blockquote / other containers → flatten their block children (best-effort).
      for (const child of node.content ?? []) appendContentBlocks(child, out, listDepth);
  }
}

function runsOf(block: TipTapNode): ComposeInlineRun[] {
  const runs: ComposeInlineRun[] = [];
  for (const n of block.content ?? []) {
    if (n.type !== 'text' || !n.text) continue;
    const marks = new Set((n.marks ?? []).map(m => m.type));
    if (marks.has('insertion')) continue; // reject-state parity: pending insert excluded
    runs.push({
      text: n.text,
      bold: marks.has('bold') || undefined,
      italic: marks.has('italic') || undefined,
      underline: marks.has('underline') || undefined,
    });
  }
  return runs;
}

function tableOf(node: TipTapNode): ComposeTable {
  const rows: ComposeTableRow[] = [];
  for (const row of node.content ?? []) {
    if (row.type !== 'tableRow') continue;
    const cells: ComposeTableCell[] = [];
    for (const cell of row.content ?? []) {
      const isHeader = cell.type === 'tableHeader';
      const cellBlocks: ComposeContentBlock[] = [];
      for (const child of cell.content ?? []) appendContentBlocks(child, cellBlocks, 0);
      cells.push({ blocks: cellBlocks, isHeader: isHeader || undefined });
    }
    rows.push({ cells });
  }
  return { rows };
}

function paraIdOf(node: TipTapNode): string | undefined {
  const paraId = node.attrs?.paraId;
  return typeof paraId === 'string' && paraId.length > 0 ? paraId : undefined;
}

function alignmentOf(node: TipTapNode): ComposeAlignment | undefined {
  switch (node.attrs?.textAlign as string | undefined) {
    case 'left':
      return 'Left';
    case 'center':
      return 'Center';
    case 'right':
      return 'Right';
    case 'justify':
      return 'Justify';
    default:
      return undefined; // inherit the style default (server maps undefined → Default)
  }
}

// ---------------------------------------------------------------------------
// R6 (spaarkeai-compose-r6, task 012) — the render-on-save IMPORTED-document mapper
// ---------------------------------------------------------------------------
//
// The server now returns a canonical `ComposeContentModel` on load/upload/project, built from the
// SAME paraId-minted bytes as the HTML projection — so editor node `paraId` attrs and model block
// `paraId`s AGREE. On an imported dirty save the client posts `{ contentModel, … }` and the server
// renders the model into the retained carrier (RenderIntoCarrier), replacing the op-log/patch-engine
// path. `buildImportedContentModel` is that model builder: it merges editor state with the RETAINED
// loaded model, anchored by paraId —
//   - untouched blocks pass through VERBATIM (object identity — every server-set fact preserved:
//     numId, pageBreakBefore, markRevision, propertiesChange, isPageBreak/commentAnchor/revision/
//     formatChange runs, table structural facts);
//   - edited blocks are REBUILT from the editor, preserving the loaded block's server-set facts and
//     redlining non-marked user edits via `diffTokens` (word-level LCS over the load-time baseline
//     snapshot) as `revision` facts — the server attributes author-less facts to the saving user;
//   - insertion/deletion MARKS (imported Word revisions + pending AI redlines) translate to revision
//     facts with author/date fidelity ('Spaarke Assistant' for binding-carrying AI marks);
//   - session + advisory comment threads fold in as Start/End `commentAnchor` runs + appended
//     `model.comments` entries with freshly-allocated non-colliding integer ids (root + each reply
//     get their OWN id and pair — mirrors the retired server bake); imported anchors keep their ids.
//
// `buildContentModelWithComments` is the BORN-IN-EDITOR sibling (scope amendment, task 012): the
// server also removed the engine-based comment bake for a0 ContentModel saves, so the born-in-editor
// build must fold session/advisory threads the same way — delegating to the untouched
// `buildContentModel` when no threads exist (exact legacy parity).
//
// Verified server behavior notes (ComposeDocumentRenderer.cs):
//   - body `BuildRun` renders run text into ONE `w:t` — a literal '\n' is NOT a line break (only the
//     COMMENT author path splits on '\n'), so an edited paragraph's hardBreak is dropped from emitted
//     runs and counted as `edited-paragraph-line-break-dropped` (never silently);
//   - `ComposeInlineRun.Href` wraps the run in `w:hyperlink` (G5, task 033) — the mapper sets `href`
//     from the TipTap `link` mark.
//
// Privacy (ADR-015 Tier 3): all text below is document content — in-memory only, never logged.

/** One aggregated mapper fidelity warning (code + occurrence count). */
export interface ImportedModelWarning {
  code: string;
  count: number;
}

/** The mapper result: the merged model + aggregated fidelity warnings + the build-time baseline. */
export interface ImportedModelResult {
  model: ComposeContentModel;
  warnings: ImportedModelWarning[];
  /**
   * F4 (step-9.5 review): the `{ paraId → reject-state text }` snapshot captured from the SAME
   * document JSON this model was built from. After the save CONFIRMS (200) the host hands it back
   * via `adoptBaselineSnapshot` so the next diff baselines against exactly what was persisted. A
   * live re-capture at 200-time instead would silently absorb any edit typed while the save was in
   * flight — that edit would then equal the recaptured baseline and pass the STALE loaded block
   * through verbatim on every later save.
   */
  snapshot: ReadonlyMap<string, string>;
}

/** A session/advisory comment thread to fold into the model (imported threads EXCLUDED by caller). */
export interface ImportedModelThreadInput {
  /** Thread id — matches the anchoring `commentAnchor` mark's `commentId`. */
  id: string;
  author: string;
  /** ISO-8601 timestamp. */
  timestamp: string;
  /** Root comment body (already export-composed by the caller for advisory threads). */
  text: string;
  replies: Array<{ text: string; author: string; timestamp: string }>;
}

// ----- internal: inline segments -------------------------------------------

/** Facts carried by an insertion/deletion mark (attrs normalized null → undefined). */
interface RevisionMarkFacts {
  author?: string;
  date?: string;
  binding?: string;
  ledgerRef?: string;
}

/** One inline segment of a block: a text node with its resolved mark set, or a hardBreak ('\n'). */
interface InlineSegment {
  text: string;
  isHardBreak: boolean;
  bold: boolean;
  italic: boolean;
  underline: boolean;
  href?: string;
  insertion?: RevisionMarkFacts;
  deletion?: RevisionMarkFacts;
  commentIds: string[];
}

function toMarkFacts(attrs: Record<string, unknown>): RevisionMarkFacts {
  const facts: RevisionMarkFacts = {};
  if (typeof attrs.author === 'string' && attrs.author) facts.author = attrs.author;
  if (typeof attrs.date === 'string' && attrs.date) facts.date = attrs.date;
  if (typeof attrs.binding === 'string' && attrs.binding) facts.binding = attrs.binding;
  if (typeof attrs.ledgerRef === 'string' && attrs.ledgerRef) facts.ledgerRef = attrs.ledgerRef;
  return facts;
}

/** An imported Word revision mark (applyImportedRevisions) — ledgerRef starts with 'imported:'. */
function isImportedMark(facts: RevisionMarkFacts): boolean {
  return facts.ledgerRef?.startsWith('imported:') ?? false;
}

/**
 * Flatten a block node's inline content into segments. MUST mirror {@link rejectStateText}'s walk
 * exactly (text → segment, hardBreak → '\n', other nodes → descend) so the concatenation of
 * non-insertion segments equals the reject-state text — the diff coordinate space depends on it.
 */
function collectSegments(block: TipTapNode): InlineSegment[] {
  const out: InlineSegment[] = [];
  const walk = (n: TipTapNode): void => {
    if (n.type === 'text') {
      if (!n.text) return;
      const seg: InlineSegment = {
        text: n.text,
        isHardBreak: false,
        bold: false,
        italic: false,
        underline: false,
        commentIds: [],
      };
      for (const m of n.marks ?? []) {
        const attrs = (m.attrs ?? {}) as Record<string, unknown>;
        switch (m.type) {
          case 'bold':
            seg.bold = true;
            break;
          case 'italic':
            seg.italic = true;
            break;
          case 'underline':
            seg.underline = true;
            break;
          case 'link':
            if (typeof attrs.href === 'string' && attrs.href) seg.href = attrs.href;
            break;
          case 'insertion':
            seg.insertion = toMarkFacts(attrs);
            break;
          case 'deletion':
            seg.deletion = toMarkFacts(attrs);
            break;
          case 'commentAnchor':
            if (typeof attrs.commentId === 'string' && attrs.commentId) seg.commentIds.push(attrs.commentId);
            break;
          default:
            break;
        }
      }
      out.push(seg);
      return;
    }
    if (n.type === 'hardBreak') {
      out.push({ text: '\n', isHardBreak: true, bold: false, italic: false, underline: false, commentIds: [] });
      return;
    }
    (n.content ?? []).forEach(walk);
  };
  (block.content ?? []).forEach(walk);
  return out;
}

// ----- internal: mapper context --------------------------------------------

interface CommentFoldState {
  threadsById: Map<string, ImportedModelThreadInput>;
  /** loadedModel.comments ids — imported anchors carrying these are preserved as-is. */
  preservedIds: Set<number>;
  /** Next free allocated id (> max of preserved + allocated so far). */
  nextId: number;
  /** mark commentId → allocated ids (root first, then one per reply). Memoized per thread. */
  allocated: Map<string, number[]>;
  /** mark commentIds whose Start/End pair has been emitted (one pair per doc — first block wins). */
  emitted: Set<string>;
  /** ComposeComment entries appended to the output model. */
  appended: ComposeComment[];
}

interface MapperContext {
  mode: 'imported' | 'parity';
  trackChanges: boolean;
  snapshot: ReadonlyMap<string, string>;
  editorParaIds: ReadonlySet<string>;
  comment: CommentFoldState;
  warnings: Map<string, number>;
}

function warn(ctx: MapperContext, code: string, count = 1): void {
  ctx.warnings.set(code, (ctx.warnings.get(code) ?? 0) + count);
}

/**
 * F2 (step-9.5 review): mirror of `IMPORTED_COMMENT_THREAD_PREFIX` in `../widgets/importedComments.ts`
 * — the RUNTIME id shape of an imported comment thread's anchor mark is `'imported-thread:<n>'`, not
 * the bare numeric `<n>`. NOT imported from that module: it transitively pulls `@spaarke/auth` (via
 * `ComposeCommentThread.types` → `useComposeWordShuttle`), which would break this module's pure/leaf
 * status (and its jest suites). Keep in sync with `importedComments.ts`.
 */
const IMPORTED_THREAD_ID_PREFIX = 'imported-thread:';

/** Parse a `commentAnchor` mark's commentId into the imported comment id it preserves, or null.
 * Accepts BOTH runtime shapes: `'imported-thread:<n>'` (applyImportedCommentAnchors, the live mount
 * shape) and bare `'<n>'`; the id must exist in loadedModel.comments to count as preserved. */
function parsePreservedCommentId(ctx: MapperContext, commentId: string): number | null {
  const raw = commentId.startsWith(IMPORTED_THREAD_ID_PREFIX)
    ? commentId.slice(IMPORTED_THREAD_ID_PREFIX.length)
    : commentId;
  if (!/^\d+$/.test(raw)) return null;
  const id = Number(raw);
  return ctx.comment.preservedIds.has(id) ? id : null;
}

/** True when `commentId` is an imported anchor id preserved from the loaded model's comments. */
function isPreservedCommentId(ctx: MapperContext, commentId: string): boolean {
  return parsePreservedCommentId(ctx, commentId) !== null;
}

/**
 * Resolve a `commentAnchor` mark's commentId to the integer id(s) its Start/End runs carry:
 * imported id (exists in loadedModel.comments — bare or `imported-thread:`-prefixed) → that single
 * id, preserved; session/advisory thread → freshly-allocated ids (root + one per reply, memoized)
 * with the thread's comments appended to the output list; unknown → null + `comment-anchor-unresolved`.
 */
function resolveCommentAnchorIds(ctx: MapperContext, commentId: string): number[] | null {
  const preserved = parsePreservedCommentId(ctx, commentId);
  if (preserved !== null) return [preserved];
  const prior = ctx.comment.allocated.get(commentId);
  if (prior) return prior;
  const thread = ctx.comment.threadsById.get(commentId);
  if (!thread) {
    warn(ctx, 'comment-anchor-unresolved');
    return null;
  }
  const ids: number[] = [];
  const rootId = ctx.comment.nextId++;
  ids.push(rootId);
  ctx.comment.appended.push({ id: rootId, author: thread.author, date: thread.timestamp, text: thread.text });
  for (const reply of thread.replies) {
    const replyId = ctx.comment.nextId++;
    ids.push(replyId);
    ctx.comment.appended.push({ id: replyId, author: reply.author, date: reply.timestamp, text: reply.text });
  }
  ctx.comment.allocated.set(commentId, ids);
  return ids;
}

// ----- internal: run building ----------------------------------------------

/** Revision fact from an insertion/deletion mark: imported author/date preserved; a binding-carrying
 * (AI) mark with no author attributes to 'Spaarke Assistant'; otherwise author-less (the server
 * attributes the authenticated saving user — deliberate, never invented client-side). */
function revisionFromMark(kind: ComposeRevisionFact['kind'], facts: RevisionMarkFacts): ComposeRevisionFact {
  const fact: ComposeRevisionFact = { kind };
  const author = facts.author ?? (facts.binding ? 'Spaarke Assistant' : undefined);
  if (author) fact.author = author;
  if (facts.date) fact.date = facts.date;
  return fact;
}

function baseRun(seg: InlineSegment, text: string, ctx: MapperContext): ComposeInlineRun {
  const run: ComposeInlineRun = { text };
  if (seg.bold) run.bold = true;
  if (seg.italic) run.italic = true;
  if (seg.underline) run.underline = true;
  // href is emitted on the imported mapper only — the born-in-editor parity path mirrors
  // buildContentModel's legacy run shape exactly (comment folding is its ONLY additive change).
  if (ctx.mode === 'imported' && seg.href) run.href = seg.href;
  return run;
}

/**
 * Build a rebuilt/new block's runs from the editor node's inline content.
 *
 * `imported` mode: insertion/deletion marks → revision facts; non-marked text is diffed against
 * `baselineText` when trackChanges (insert regions → Inserted runs; delete regions → author-less
 * Deleted runs at their offsets — original formatting of deleted text is not recoverable
 * client-side, accepted fidelity posture); hardBreak dropped + warned; comment anchors emitted as
 * Start/End pairs around the covered segments.
 *
 * `parity` mode (born-in-editor): exact {@link runsOf} legacy behavior (insertion-marked excluded,
 * deletion-marked kept plain, hardBreak silently dropped, no href, no revisions) PLUS the comment
 * anchor folding.
 */
function buildRunsFromNode(block: TipTapNode, ctx: MapperContext, baselineText: string): ComposeInlineRun[] {
  const imported = ctx.mode === 'imported';
  const segments = collectSegments(block);

  // Comment-anchor plan: per distinct commentId (skipping ids already emitted in an earlier block —
  // one Start/End pair per doc, mirroring the anchored-comments clamp-to-start-paragraph posture),
  // the first/last covering segment indices + resolved ids.
  const anchorPlans: Array<{ first: number; last: number; ids: number[] }> = [];
  {
    const firstIdx = new Map<string, number>();
    const lastIdx = new Map<string, number>();
    segments.forEach((seg, idx) => {
      for (const id of seg.commentIds) {
        if (!firstIdx.has(id)) firstIdx.set(id, idx);
        lastIdx.set(id, idx);
      }
    });
    for (const [id, first] of firstIdx) {
      if (ctx.comment.emitted.has(id)) continue;
      const ids = resolveCommentAnchorIds(ctx, id);
      if (!ids) continue;
      ctx.comment.emitted.add(id);
      anchorPlans.push({ first, last: lastIdx.get(id)!, ids });
    }
  }

  // Diff prep (imported + trackChanges): regions over the CURRENT reject-state text.
  const useDiff = imported && ctx.trackChanges;
  let insertRanges: Array<{ from: number; to: number }> = [];
  let deletes: Array<{ offset: number; text: string }> = [];
  if (useDiff) {
    const rejectText = segments
      .filter(s => !s.insertion)
      .map(s => s.text)
      .join('');
    const regions = diffToRegions(diffTokens(baselineText, rejectText));
    insertRanges = regions
      .filter(r => r.insertLength > 0)
      .map(r => ({ from: r.offset, to: r.offset + r.insertLength }));
    deletes = regions.filter(r => r.deleteText.length > 0).map(r => ({ offset: r.offset, text: r.deleteText }));
  }

  const runs: ComposeInlineRun[] = [];
  let cursor = 0; // reject-coordinate cursor (insertion-marked segments do NOT advance it)
  let di = 0;
  const flushDeletes = (upto: number): void => {
    while (di < deletes.length && deletes[di].offset <= upto) {
      runs.push({ text: deletes[di].text, revision: { kind: 'Deleted' } });
      di++;
    }
  };
  const insideInsert = (pos: number): boolean => insertRanges.some(r => pos >= r.from && pos < r.to);
  const nextBoundary = (pos: number, end: number): number => {
    let boundary = end;
    for (const r of insertRanges) {
      if (r.from > pos && r.from < boundary) boundary = r.from;
      if (r.to > pos && r.to < boundary) boundary = r.to;
    }
    for (let k = di; k < deletes.length; k++) {
      if (deletes[k].offset > pos && deletes[k].offset < boundary) boundary = deletes[k].offset;
    }
    return boundary;
  };

  segments.forEach((seg, idx) => {
    flushDeletes(cursor);
    for (const plan of anchorPlans) {
      if (plan.first === idx) {
        for (const id of plan.ids) runs.push({ text: '', commentAnchor: { kind: 'Start', id } });
      }
    }

    if (seg.isHardBreak) {
      // Not representable in a rendered body run (server BuildRun: one w:t, no '\n' split) — dropped,
      // never silently, on the imported path; silently on parity (legacy buildContentModel behavior).
      if (imported) warn(ctx, 'edited-paragraph-line-break-dropped');
      cursor += seg.text.length;
    } else if (seg.insertion) {
      // Pending insert: parity excludes it (reject-state); imported translates it to an Inserted fact.
      if (imported) {
        const run = baseRun(seg, seg.text, ctx);
        run.revision = revisionFromMark('Inserted', seg.insertion);
        runs.push(run);
      }
      // insertion-marked text is not in reject coordinates — cursor unchanged.
    } else if (seg.deletion) {
      // Physically-present struck text: parity keeps it plain; imported translates to a Deleted fact.
      const run = baseRun(seg, seg.text, ctx);
      if (imported) run.revision = revisionFromMark('Deleted', seg.deletion);
      runs.push(run);
      cursor += seg.text.length;
    } else {
      // Plain text — split at diff-region boundaries (no-op when not diffing).
      const end = cursor + seg.text.length;
      let pos = cursor;
      while (pos < end) {
        flushDeletes(pos);
        const boundary = useDiff ? nextBoundary(pos, end) : end;
        const chunk = seg.text.slice(pos - cursor, boundary - cursor);
        if (chunk) {
          const run = baseRun(seg, chunk, ctx);
          if (useDiff && insideInsert(pos)) run.revision = { kind: 'Inserted' };
          runs.push(run);
        }
        pos = boundary;
      }
      cursor = end;
    }

    for (const plan of anchorPlans) {
      if (plan.last === idx) {
        for (const id of plan.ids) runs.push({ text: '', commentAnchor: { kind: 'End', id } });
      }
    }
  });
  flushDeletes(Number.POSITIVE_INFINITY);
  return runs;
}

// ----- internal: editor block-unit flattening ------------------------------

interface EditorLeafUnit {
  unit: 'leaf';
  blockKind: 'Paragraph' | 'Heading' | 'ListItem';
  node: TipTapNode;
  level?: number;
  ordered?: boolean;
  startsNewList?: boolean;
}

interface EditorTableUnit {
  unit: 'table';
  node: TipTapNode;
}

type EditorUnit = EditorLeafUnit | EditorTableUnit;

/** Flatten editor nodes into the SAME block-unit sequence {@link appendContentBlocks} produces
 * (lists flattened to ListItem units with depth/ordered/startsNewList; tables kept whole). */
function flattenEditorUnits(nodes: readonly TipTapNode[], listDepth: number, out: EditorUnit[]): void {
  for (const node of nodes) {
    switch (node.type) {
      case 'paragraph':
        out.push({ unit: 'leaf', blockKind: 'Paragraph', node });
        break;
      case 'heading':
        out.push({ unit: 'leaf', blockKind: 'Heading', node, level: (node.attrs?.level as number | undefined) ?? 1 });
        break;
      case 'bulletList':
      case 'orderedList': {
        const ordered = node.type === 'orderedList';
        let firstItem = true;
        for (const item of node.content ?? []) {
          if (item.type !== 'listItem') continue;
          for (const child of item.content ?? []) {
            if (child.type === 'paragraph' || child.type === 'heading') {
              out.push({
                unit: 'leaf',
                blockKind: 'ListItem',
                node: child,
                level: listDepth,
                ordered,
                startsNewList: ordered && firstItem && listDepth === 0,
              });
              firstItem = false;
            } else if (child.type === 'bulletList' || child.type === 'orderedList') {
              flattenEditorUnits([child], listDepth + 1, out);
            }
          }
        }
        break;
      }
      case 'table':
        out.push({ unit: 'table', node });
        break;
      default:
        flattenEditorUnits(node.content ?? [], listDepth, out);
    }
  }
}

/** First paraId of a node subtree in document order (an editor table's anchor — the op-log
 * convention: the first cell's first paragraph paraId). */
function firstParaIdIn(node: TipTapNode): string | undefined {
  if (node.type === 'paragraph' || node.type === 'heading') return paraIdOf(node);
  for (const child of node.content ?? []) {
    const found = firstParaIdIn(child);
    if (found) return found;
  }
  return undefined;
}

function unitAnchorParaId(u: EditorUnit): string | undefined {
  return u.unit === 'leaf' ? paraIdOf(u.node) : firstParaIdIn(u.node);
}

/** First paraId of a loaded block (a Table's anchor is its first cell's first paragraph paraId). */
function loadedAnchorParaId(block: ComposeContentBlock): string | undefined {
  if (block.kind !== 'Table') return block.paraId;
  for (const row of block.table?.rows ?? []) {
    for (const cell of row.cells) {
      for (const inner of cell.blocks) {
        const found = loadedAnchorParaId(inner);
        if (found) return found;
      }
    }
  }
  return undefined;
}

// ----- internal: block merge -----------------------------------------------

/** Force a loaded block (and, for tables, every cell block) into a fully user-DELETED state:
 * every run's revision becomes Deleted (existing Deleted runs untouched — innermost-wins baseline),
 * markRevision Deleted; every other fact preserved. Never mutates the input. */
function forceDeletedBlock(block: ComposeContentBlock): ComposeContentBlock {
  if (block.kind === 'Table' && block.table) {
    return {
      ...block,
      table: {
        ...block.table,
        rows: block.table.rows.map(row => ({
          ...row,
          cells: row.cells.map(cell => ({ ...cell, blocks: cell.blocks.map(forceDeletedBlock) })),
        })),
      },
    };
  }
  return {
    ...block,
    markRevision: { kind: 'Deleted' },
    runs: (block.runs ?? []).map(r => (r.revision?.kind === 'Deleted' ? r : { ...r, revision: { kind: 'Deleted' } })),
  };
}

/** Reject-state text of a LOADED block's runs (fallback baseline when the snapshot lacks the
 * paraId): Inserted runs excluded (they render as insertion-marked in the editor), Deleted runs
 * kept (physically present), marker runs skipped. */
function loadedRejectText(block: ComposeContentBlock): string {
  return (block.runs ?? [])
    .filter(r => !r.isPageBreak && !r.commentAnchor && r.revision?.kind !== 'Inserted')
    .map(r => r.text)
    .join('');
}

/** One character's inline-formatting signature (F1 — step-9.5 review). */
interface CharFormat {
  bold: boolean;
  italic: boolean;
  underline: boolean;
  href: string | undefined;
}

/**
 * F1 (step-9.5 review): per-character (bold, italic, underline, href) signature comparison between
 * the editor's reject-state content and the loaded block's runs. A formatting-ONLY edit (setBold on
 * otherwise-unchanged text) leaves the reject TEXT equal to the baseline, so without this check the
 * loaded block passed through verbatim and the edit was silently lost.
 *
 * Alignment of the two char streams: the editor side skips insertion-marked segments (excluded from
 * reject state) and hardBreak segments (formatting-less '\n' nodes); the loaded side skips marker
 * runs (isPageBreak / commentAnchor), Inserted-revision runs (they render as insertion-marked in
 * the editor — the same exclusion), and strips any literal '\n'. Both texts should then be equal
 * for a genuinely untouched block (both derive from the same paraId-minted bytes); if they are NOT
 * equal the streams cannot be positionally compared, and we conservatively report "changed" — the
 * rebuild tier preserves the user's content and its fact drops are warned, whereas a wrong verbatim
 * pass-through is silent loss.
 */
function formattingUnchanged(segments: readonly InlineSegment[], loaded: ComposeContentBlock): boolean {
  const editorText: string[] = [];
  const editorFormats: CharFormat[] = [];
  for (const s of segments) {
    if (s.insertion !== undefined || s.isHardBreak) continue;
    for (let i = 0; i < s.text.length; i++) {
      editorText.push(s.text[i]);
      editorFormats.push({ bold: s.bold, italic: s.italic, underline: s.underline, href: s.href });
    }
  }
  const loadedText: string[] = [];
  const loadedFormats: CharFormat[] = [];
  for (const r of loaded.runs ?? []) {
    if (r.isPageBreak || r.commentAnchor !== undefined || r.revision?.kind === 'Inserted') continue;
    for (let i = 0; i < r.text.length; i++) {
      if (r.text[i] === '\n') continue;
      loadedText.push(r.text[i]);
      loadedFormats.push({ bold: r.bold ?? false, italic: r.italic ?? false, underline: r.underline ?? false, href: r.href });
    }
  }
  if (editorText.length !== loadedFormats.length || editorText.join('') !== loadedText.join('')) {
    return false; // cannot positionally align — treat as changed (rebuild; never a silent pass-through)
  }
  return editorFormats.every((f, i) => {
    const l = loadedFormats[i];
    return f.bold === l.bold && f.italic === l.italic && f.underline === l.underline && f.href === l.href;
  });
}

function editablePropsMatch(u: EditorLeafUnit, loaded: ComposeContentBlock, alignment: ComposeAlignment | undefined): boolean {
  if (loaded.kind !== u.blockKind) return false;
  if (u.blockKind === 'Heading' && (loaded.level ?? 1) !== (u.level ?? 1)) return false;
  if (u.blockKind === 'ListItem') {
    if ((loaded.level ?? 0) !== (u.level ?? 0)) return false;
    if ((loaded.ordered ?? false) !== (u.ordered ?? false)) return false;
    return true; // ListItem carries no alignment (mirror buildContentModel)
  }
  return (alignment ?? 'Default') === (loaded.alignment ?? 'Default');
}

/** Merge one matched leaf: verbatim pass-through / props-only override / full run rebuild. */
function mergeLeafBlock(u: EditorLeafUnit, loaded: ComposeContentBlock, ctx: MapperContext): ComposeContentBlock {
  const paraId = paraIdOf(u.node);
  const alignment = u.blockKind === 'ListItem' ? undefined : alignmentOf(u.node);
  const segments = collectSegments(u.node);
  const rejectText = segments
    .filter(s => !s.insertion)
    .map(s => s.text)
    .join('');
  const baseline = paraId !== undefined ? ctx.snapshot.get(paraId) : undefined;
  const hasNonImportedRevisionMarks = segments.some(
    s => (s.insertion !== undefined && !isImportedMark(s.insertion)) || (s.deletion !== undefined && !isImportedMark(s.deletion))
  );
  const hasSessionAnchors = segments.some(s => s.commentIds.some(id => !isPreservedCommentId(ctx, id)));
  // F1 (step-9.5 review): a formatting-only edit leaves the reject TEXT untouched — the per-char
  // formatting signature must ALSO match the loaded runs or the block falls to the rebuild tier.
  const textUntouched =
    baseline !== undefined &&
    rejectText === baseline &&
    !hasNonImportedRevisionMarks &&
    !hasSessionAnchors &&
    formattingUnchanged(segments, loaded);

  if (textUntouched) {
    if (editablePropsMatch(u, loaded, alignment)) {
      return loaded; // VERBATIM — object identity preserves every server-set field exactly.
    }
    // Props-only change: editable props from the editor; runs + server-set facts untouched.
    const b: ComposeContentBlock = { ...loaded, kind: u.blockKind };
    if (u.blockKind === 'Heading' || u.blockKind === 'ListItem') b.level = u.level;
    else delete b.level;
    if (u.blockKind === 'ListItem') b.ordered = u.ordered;
    else {
      delete b.ordered;
      delete b.startsNewList;
      delete b.numId;
    }
    if (u.blockKind !== 'ListItem' && alignment !== undefined) b.alignment = alignment;
    else delete b.alignment;
    return b;
  }

  // Content changed — rebuild runs from the editor, preserving the loaded block's server-set facts.
  const lostPageBreaks = (loaded.runs ?? []).filter(r => r.isPageBreak).length;
  if (lostPageBreaks > 0) warn(ctx, 'edited-paragraph-page-break-dropped', lostPageBreaks);
  const runs = buildRunsFromNode(u.node, ctx, baseline ?? loadedRejectText(loaded));

  const block: ComposeContentBlock = { kind: u.blockKind, runs };
  if (paraId !== undefined) block.paraId = paraId;
  if (u.blockKind === 'Heading' || u.blockKind === 'ListItem') block.level = u.level;
  if (u.blockKind === 'ListItem') {
    block.ordered = u.ordered;
    // numId is the imported list identity — preserved when the loaded block was a ListItem;
    // startsNewList: loaded value wins (absent for imported items keyed by numId), editor's
    // first-ordered-item flag only when the block BECAME a list item in this session.
    if (loaded.kind === 'ListItem') {
      if (loaded.numId !== undefined) block.numId = loaded.numId;
      if (loaded.startsNewList !== undefined) block.startsNewList = loaded.startsNewList;
    } else if (u.startsNewList !== undefined) {
      block.startsNewList = u.startsNewList;
    }
  }
  if (u.blockKind !== 'ListItem' && alignment !== undefined) block.alignment = alignment;
  if (loaded.pageBreakBefore !== undefined) block.pageBreakBefore = loaded.pageBreakBefore;
  if (loaded.markRevision !== undefined) block.markRevision = loaded.markRevision;
  if (loaded.propertiesChange !== undefined) block.propertiesChange = loaded.propertiesChange;
  return block;
}

/** Build a fresh block for an editor unit with NO loaded counterpart (new content). In imported
 * mode with trackChanges, all non-marked text redlines Inserted (empty-baseline diff) and the block
 * mark carries `markRevision: Inserted`. */
function buildFreshBlockFromUnit(u: EditorUnit, ctx: MapperContext): ComposeContentBlock {
  if (u.unit === 'table') {
    return { kind: 'Table', table: buildFreshTable(u.node, ctx) };
  }
  const runs = buildRunsFromNode(u.node, ctx, '');
  const block: ComposeContentBlock = { kind: u.blockKind, runs };
  const paraId = paraIdOf(u.node);
  if (paraId !== undefined) block.paraId = paraId;
  if (u.blockKind === 'Heading' || u.blockKind === 'ListItem') block.level = u.level;
  if (u.blockKind === 'ListItem') {
    block.ordered = u.ordered;
    block.startsNewList = u.startsNewList;
  }
  if (u.blockKind !== 'ListItem') {
    const alignment = alignmentOf(u.node);
    if (alignment !== undefined) block.alignment = alignment;
  }
  if (ctx.mode === 'imported' && ctx.trackChanges) block.markRevision = { kind: 'Inserted' };
  return block;
}

function cellNodesOf(row: TipTapNode): TipTapNode[] {
  return (row.content ?? []).filter(c => c.type === 'tableCell' || c.type === 'tableHeader');
}

function rowNodesOf(table: TipTapNode): TipTapNode[] {
  return (table.content ?? []).filter(r => r.type === 'tableRow');
}

function mergeCellBlocks(cellNode: TipTapNode, loadedBlocks: readonly ComposeContentBlock[], ctx: MapperContext): ComposeContentBlock[] {
  const units: EditorUnit[] = [];
  flattenEditorUnits(cellNode.content ?? [], 0, units);
  return mergeBlockLists(units, loadedBlocks, ctx);
}

function buildFreshTable(tableNode: TipTapNode, ctx: MapperContext): ComposeTable {
  const rows: ComposeTableRow[] = rowNodesOf(tableNode).map(rowNode => ({
    cells: cellNodesOf(rowNode).map(cellNode => {
      const cell: ComposeTableCell = { blocks: mergeCellBlocks(cellNode, [], ctx) };
      if (cellNode.type === 'tableHeader') cell.isHeader = true;
      return cell;
    }),
  }));
  return { rows };
}

/** Merge a matched editor table against its loaded Table block. Shape match (same row×cell counts)
 * → per-cell recursive merge preserving ALL table/row/cell structural facts (verbatim identity when
 * nothing changed). Shape mismatch → rebuild from the editor preserving table-level facts +
 * positionally-paired cell/row facts, counted once as `edited-table-structure-rebuilt`. */
function mergeTableBlock(u: EditorTableUnit, loaded: ComposeContentBlock, ctx: MapperContext): ComposeContentBlock {
  const loadedTable = loaded.table!;
  const editorRows = rowNodesOf(u.node);
  const shapeMatches =
    editorRows.length === loadedTable.rows.length &&
    editorRows.every((r, i) => cellNodesOf(r).length === loadedTable.rows[i].cells.length);

  if (shapeMatches) {
    let allIdentical = true;
    const rows = editorRows.map((rowNode, i) => {
      const loadedRow = loadedTable.rows[i];
      const cells = cellNodesOf(rowNode).map((cellNode, k) => {
        const loadedCell = loadedRow.cells[k];
        const blocks = mergeCellBlocks(cellNode, loadedCell.blocks, ctx);
        const identical =
          blocks.length === loadedCell.blocks.length && blocks.every((b, x) => b === loadedCell.blocks[x]);
        if (identical) return loadedCell;
        allIdentical = false;
        return { ...loadedCell, blocks };
      });
      return cells.every((c, k) => c === loadedRow.cells[k]) ? loadedRow : { ...loadedRow, cells };
    });
    if (allIdentical) return loaded; // VERBATIM — the whole table is untouched.
    return { ...loaded, table: { ...loadedTable, rows } };
  }

  warn(ctx, 'edited-table-structure-rebuilt');
  const rows: ComposeTableRow[] = editorRows.map((rowNode, i) => {
    const loadedRow = loadedTable.rows[i];
    const cells: ComposeTableCell[] = cellNodesOf(rowNode).map((cellNode, k) => {
      const loadedCell = loadedRow?.cells[k];
      const cell: ComposeTableCell = { blocks: mergeCellBlocks(cellNode, loadedCell?.blocks ?? [], ctx) };
      if (loadedCell) {
        if (loadedCell.isHeader !== undefined) cell.isHeader = loadedCell.isHeader;
        else if (cellNode.type === 'tableHeader') cell.isHeader = true;
        if (loadedCell.gridSpan !== undefined) cell.gridSpan = loadedCell.gridSpan;
        if (loadedCell.vMerge !== undefined) cell.vMerge = loadedCell.vMerge;
        if (loadedCell.width !== undefined) cell.width = loadedCell.width;
        if (loadedCell.verticalAlignment !== undefined) cell.verticalAlignment = loadedCell.verticalAlignment;
      } else if (cellNode.type === 'tableHeader') {
        cell.isHeader = true;
      }
      return cell;
    });
    const row: ComposeTableRow = { cells };
    if (loadedRow?.repeatAsHeaderRow !== undefined) row.repeatAsHeaderRow = loadedRow.repeatAsHeaderRow;
    return row;
  });
  const table: ComposeTable = { rows };
  if (loadedTable.styleId !== undefined) table.styleId = loadedTable.styleId;
  if (loadedTable.width !== undefined) table.width = loadedTable.width;
  if (loadedTable.borders !== undefined) table.borders = loadedTable.borders;
  if (loadedTable.gridColumnWidthsTwips !== undefined) table.gridColumnWidthsTwips = loadedTable.gridColumnWidthsTwips;
  if (loadedTable.lookHex !== undefined) table.lookHex = loadedTable.lookHex;
  return { kind: 'Table', table };
}

/**
 * The paraId-anchored merge over one block list (the document body, or one table cell's blocks).
 * Walks editor units in order, pairing to loaded blocks by anchor paraId; loaded blocks passed
 * between matches whose paraId no longer appears ANYWHERE in the editor flush as USER-DELETED
 * (all-Deleted redline when trackChanges, omitted when clean); loaded blocks whose paraId appears
 * elsewhere in the editor (moved) are left for their editor-position match. Remaining unmatched
 * loaded blocks flush at the end the same way.
 */
function mergeBlockLists(units: readonly EditorUnit[], loadedBlocks: readonly ComposeContentBlock[], ctx: MapperContext): ComposeContentBlock[] {
  const out: ComposeContentBlock[] = [];
  const anchors = loadedBlocks.map(loadedAnchorParaId);
  const anchorToIndex = new Map<string, number>();
  anchors.forEach((a, i) => {
    if (a !== undefined && !anchorToIndex.has(a)) anchorToIndex.set(a, i);
  });
  const consumed: boolean[] = new Array(loadedBlocks.length).fill(false);
  let cursor = 0;

  const emitUserDeleted = (block: ComposeContentBlock): void => {
    if (ctx.mode === 'imported' && ctx.trackChanges) out.push(forceDeletedBlock(block));
    // clean mode: omitted entirely.
  };
  const flushRange = (from: number, to: number): void => {
    for (let k = from; k < to; k++) {
      if (consumed[k]) continue;
      const a = anchors[k];
      if (a !== undefined && ctx.editorParaIds.has(a)) continue; // moved — matched at its editor position
      consumed[k] = true;
      emitUserDeleted(loadedBlocks[k]);
    }
  };

  for (const u of units) {
    const anchor = unitAnchorParaId(u);
    const j = anchor !== undefined ? anchorToIndex.get(anchor) : undefined;
    if (j === undefined || consumed[j]) {
      out.push(buildFreshBlockFromUnit(u, ctx)); // NEW block (no/unknown/already-consumed paraId)
      continue;
    }
    if (j >= cursor) {
      flushRange(cursor, j);
      cursor = j + 1;
    }
    consumed[j] = true;
    const loaded = loadedBlocks[j];
    const loadedIsTable = loaded.kind === 'Table' && loaded.table !== undefined;
    if (u.unit === 'table' && loadedIsTable) {
      out.push(mergeTableBlock(u, loaded, ctx));
    } else if (u.unit === 'leaf' && !loadedIsTable) {
      out.push(mergeLeafBlock(u, loaded, ctx));
    } else {
      // Kind flip (paragraph ↔ table around the same anchor — rare): the loaded block's content was
      // effectively removed; redline it deleted (trackChanges) and build the editor's shape fresh.
      emitUserDeleted(loaded);
      out.push(buildFreshBlockFromUnit(u, ctx));
    }
  }
  for (let k = 0; k < loadedBlocks.length; k++) {
    if (consumed[k]) continue;
    consumed[k] = true;
    const a = anchors[k];
    // A paraId still present in the editor (e.g. now inside a table the cell-merge represented) is
    // NOT user-deleted — its content was emitted at its editor position; flushing would duplicate.
    if (a !== undefined && ctx.editorParaIds.has(a)) continue;
    emitUserDeleted(loadedBlocks[k]);
  }
  return out;
}

// ----- public entry points -------------------------------------------------

function makeMapperContext(
  mode: 'imported' | 'parity',
  trackChanges: boolean,
  snapshot: ReadonlyMap<string, string>,
  editorParaIds: ReadonlySet<string>,
  sessionThreads: readonly ImportedModelThreadInput[],
  loadedComments: readonly ComposeComment[] | undefined
): MapperContext {
  const preservedIds = new Set((loadedComments ?? []).map(c => c.id));
  const nextId = (loadedComments ?? []).reduce((max, c) => Math.max(max, c.id), 0) + 1;
  return {
    mode,
    trackChanges,
    snapshot,
    editorParaIds,
    comment: {
      threadsById: new Map(sessionThreads.map(t => [t.id, t])),
      preservedIds,
      nextId,
      allocated: new Map(),
      emitted: new Set(),
      appended: [],
    },
    warnings: new Map(),
  };
}

function toWarningList(ctx: MapperContext): ImportedModelWarning[] {
  return Array.from(ctx.warnings, ([code, count]) => ({ code, count }));
}

/**
 * Build the {@link ComposeContentModel} an IMPORTED document's dirty save posts (R6 task 012 —
 * render-on-save cutover; see the section header above for the full merge contract). Pure over the
 * editor's current JSON + the retained `loadedModel` + the load-time `baselineSnapshot`
 * ({@link captureParaIdSnapshot}). Never mutates `loadedModel` — the output's `blocks`/`comments`
 * are NEW arrays (verbatim blocks are shared by REFERENCE, never modified).
 *
 * @param editor            Live TipTap editor.
 * @param loadedModel       The server's canonical content model from load/upload/project.
 * @param baselineSnapshot  Load-time `{ paraId → reject-state text }` map — the redline diff baseline.
 * @param opts.trackChanges false for reopened AUTHORED docs (clean edits, REQ-1) — user edits save
 *                          plain; deleted blocks are omitted rather than redlined.
 * @param opts.sessionThreads Session + advisory comment threads to fold in (imported threads
 *                          EXCLUDED by the caller — they ride `loadedModel.comments`).
 */
export function buildImportedContentModel(
  editor: Editor,
  loadedModel: ComposeContentModel,
  baselineSnapshot: ReadonlyMap<string, string>,
  opts: {
    trackChanges: boolean;
    sessionThreads: ImportedModelThreadInput[];
  }
): ImportedModelResult {
  const doc = editor.getJSON() as TipTapNode;
  const editorParaIds = new Set<string>();
  // F4 (step-9.5 review): capture the NEXT baseline from the SAME doc JSON the model is built from,
  // returned on the result for the host to adopt only after the save CONFIRMS (never re-captured
  // from the live doc at 200-time — a mid-flight edit would silently vanish into the baseline).
  const buildSnapshot = new Map<string, string>();
  forEachBlock(doc, b => {
    const p = paraIdOf(b);
    if (p !== undefined) {
      editorParaIds.add(p);
      buildSnapshot.set(p, rejectStateText(b));
    }
  });
  const ctx = makeMapperContext(
    'imported',
    opts.trackChanges,
    baselineSnapshot,
    editorParaIds,
    opts.sessionThreads,
    loadedModel.comments
  );
  const units: EditorUnit[] = [];
  flattenEditorUnits(doc.content ?? [], 0, units);
  const blocks = mergeBlockLists(units, loadedModel.blocks, ctx);
  const model: ComposeContentModel = { blocks };
  if (loadedModel.comments !== undefined || ctx.comment.appended.length > 0) {
    model.comments = [...(loadedModel.comments ?? []), ...ctx.comment.appended];
  }
  return { model, warnings: toWarningList(ctx), snapshot: buildSnapshot };
}

/**
 * BORN-IN-EDITOR sibling (task 012 scope amendment): the server removed the engine-based comment
 * bake for ALL ContentModel saves, so the a0 build must fold session + advisory comment threads
 * into the model itself. Delegates to the untouched {@link buildContentModel} when there are no
 * threads (exact legacy output); otherwise re-walks the editor with the SAME reject-state text
 * semantics (insertion-marked excluded, deletion-marked kept plain, no revision facts) plus
 * Start/End `commentAnchor` runs + appended `model.comments` with ids allocated from 1.
 */
export function buildContentModelWithComments(
  editor: Editor,
  sessionThreads: ImportedModelThreadInput[]
): ImportedModelResult {
  if (sessionThreads.length === 0) {
    return { model: buildContentModel(editor), warnings: [], snapshot: captureParaIdSnapshot(editor) };
  }
  const ctx = makeMapperContext('parity', false, new Map(), new Set(), sessionThreads, undefined);
  const doc = editor.getJSON() as TipTapNode;
  // F4 parity: the build-time snapshot rides the result here too (same doc JSON as the model).
  const buildSnapshot = new Map<string, string>();
  forEachBlock(doc, b => {
    const p = paraIdOf(b);
    if (p !== undefined) buildSnapshot.set(p, rejectStateText(b));
  });
  const units: EditorUnit[] = [];
  flattenEditorUnits(doc.content ?? [], 0, units);
  const blocks = mergeBlockLists(units, [], ctx);
  const model: ComposeContentModel = { blocks };
  if (ctx.comment.appended.length > 0) model.comments = [...ctx.comment.appended];
  return { model, warnings: toWarningList(ctx), snapshot: buildSnapshot };
}
