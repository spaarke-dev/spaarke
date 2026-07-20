/**
 * importedRevisions.ts — render existing Word revisions as first-class editor marks (FR-24, task 050).
 *
 * Project: spaarkeai-compose-r3, task 050 (import round-trip — the in-editor render half).
 *
 * WHY THIS EXISTS (design §7 — "the reader is not the gap, the editor mount is"): the load-time `.docx`
 * arrives in the editor via mammoth (`docxBridge.docxToTipTapHtml`), which FLATTENS native `w:ins`/`w:del`
 * tracked changes to prose — insertions become plain text, deletions vanish entirely — before ProseMirror
 * ever sees them. The BFF already recovers those revisions on Load (the EXISTING `DocxAnnotationReader`,
 * reused verbatim) and projects them onto the Load response as {@link ImportedRevision}s carrying the E2
 * `w14:paraId`. This module is the client render: it re-materializes each recovered revision as a
 * first-class R2 home-grown insertion/deletion mark (task 031's `InsertionMark`/`DeletionMark` — MIT base,
 * NO `@tiptap-pro/*`), author/date-attributed and accept/reject-able through the SAME per-change popover +
 * `usePendingRedline.accept/reject` path any redline uses (they address marks by `data-ledger-ref`).
 *
 * ANCHORING (design §5.2 / project MUST): `w14:paraId` is the PRIMARY anchor — the editor's paragraph
 * nodes carry the same server-stamped ids (`docxBridge.stampParaIds`), so an imported revision maps to its
 * paragraph by an exact paraId match within our own load→edit→save round-trip. The FUZZY FALLBACK
 * (`anchorText` + `paragraphHint`) covers the cross-Word-session case: Word regenerates every paraId on an
 * external save when tracked changes are present (Open-XML-SDK #925), so a revision recovered from a
 * Word-touched document carries a paraId the editor no longer has, and we re-anchor by paragraph text +
 * document-order hint (the client peer of the server-side `AnnotationReanchorService`, which is RETAINED
 * unchanged for the return-from-Word reanchor endpoint).
 *
 * RENDER MECHANICS:
 *  - INSERTION — mammoth KEEPS the inserted text as prose, so we locate `revision.text` inside the target
 *    paragraph's live text and apply the `insertion` mark over that span (green + underline).
 *  - DELETION — mammoth DROPS the deleted text, so we re-insert `revision.text` at the end of the target
 *    paragraph carrying the `deletion` mark (struck) so it is VISIBLE and reject/accept-able. The exact
 *    intra-paragraph offset does not survive the mammoth flatten, so end-of-paragraph is a bounded,
 *    honest placement (the paragraph anchor is exact; only the within-paragraph position is approximate).
 *
 * The stamped marks carry a distinguishing `imported:` `ledgerRef` prefix ({@link IMPORTED_LEDGER_PREFIX})
 * so the save-path bridge (`redlineMarksToDocxAnnotations`) does NOT re-emit them as new annotations — an
 * imported revision rides the RETAINED-ORIGINAL baseline on save (its survival across a dirty save is
 * proven by task 052), never a double-write.
 *
 * Marks are applied with `addToHistory: false` — a load-time render of pre-existing revisions is not a
 * user edit and must not be undoable (the caller resets the dirty flag right after; see ComposeEditor).
 *
 * Privacy (ADR-015 Tier 3): `revision.text` / `revision.anchorText` are document content — carried
 * in-memory to the render only, NEVER logged.
 *
 * @see ./marks/InsertionMark.ts · ./marks/DeletionMark.ts (the rendering primitives, author/date-extended)
 * @see ./hooks/usePendingRedline.ts (accept/reject scans marks by ledgerRef — imported marks ride it)
 * @see ../utils/docxBridge.ts (stampParaIds — the paraIds this module anchors against)
 * @see projects/spaarkeai-compose-r3/design.md §7 (import round-trip)
 */
import type { Editor } from '@tiptap/core';
import type { ImportedRevision } from '../types/compose-contracts';

const INSERTION_MARK = 'insertion';
const DELETION_MARK = 'deletion';

/** The paraId-bearing block node types (mirror `PARAID_NODE_TYPES` / the server's `<w:p>` count). */
const BLOCK_NODE_TYPES = new Set(['paragraph', 'heading']);

/**
 * The `ledgerRef` prefix stamped onto every IMPORTED Word revision's mark. Distinguishes an imported
 * existing revision (which rides the retained-original baseline on save) from an AI-suggested redline
 * (which the save-path `redlineMarksToDocxAnnotations` bridge DOES re-emit). Kept in sync with the guard
 * in `useComposeWordShuttle.redlineMarksToDocxAnnotations`.
 */
export const IMPORTED_LEDGER_PREFIX = 'imported:';

/** Outcome of an {@link applyImportedRevisions} pass — how many revisions rendered vs could not anchor. */
export interface ApplyImportedRevisionsResult {
  /** Revisions rendered as a first-class insertion/deletion mark. */
  applied: number;
  /** Revisions that could not be anchored (no paraId match + no fuzzy match) or whose text was empty. */
  unresolved: number;
}

/**
 * One paraId-bearing block (paragraph/heading) located in the live editor document. Exported (task 051)
 * so {@link ../widgets/importedComments.ts} reuses the SAME paraId-primary / anchorText-fuzzy resolution
 * this module built for revisions — no parallel anchor-resolution logic.
 */
export interface BlockInfo {
  /** ProseMirror position of the block node's opening boundary. */
  from: number;
  /** ProseMirror position just after the block node (its closing boundary + 1). */
  to: number;
  /** The block's `w14:paraId` hidden attribute, when stamped. */
  paraId?: string;
  /** The block's plain text content (for the fuzzy anchor fallback). */
  text: string;
}

/**
 * The paraId + anchorText + paragraphHint triple {@link resolveBlock} matches against. Both
 * {@link ImportedRevision} and the sibling `ImportedComment` (task 051) satisfy this shape structurally.
 */
export interface AnchorHint {
  paraId?: string;
  paragraphHint: number;
  anchorText: string;
}

/** Collect the editor's paraId-bearing blocks in document order (descends into table cells — server parity).
 * Exported (task 051) for reuse by {@link ../widgets/importedComments.ts}. */
export function collectBlocks(editor: Editor): BlockInfo[] {
  const blocks: BlockInfo[] = [];
  editor.state.doc.descendants((node, pos) => {
    if (BLOCK_NODE_TYPES.has(node.type.name)) {
      const rawParaId = node.attrs.paraId;
      const paraId = typeof rawParaId === 'string' && rawParaId.length > 0 ? rawParaId : undefined;
      blocks.push({ from: pos, to: pos + node.nodeSize, paraId, text: node.textContent });
    }
    return true; // descend into children (table cells hold paragraphs — server parity)
  });
  return blocks;
}

/** Whitespace-collapsed, lower-cased text for the fuzzy anchor comparison. */
function normalize(text: string): string {
  return text.replace(/\s+/g, ' ').trim().toLowerCase();
}

/**
 * Tolerant text match between a revision's `anchorText` (the reader's SETTLED paragraph text — excludes
 * the tracked runs) and an editor block's text (mammoth-flattened — INCLUDES the inserted text). A raw
 * substring compare fails on that asymmetry, so this uses substring OR a ≥60% word-overlap heuristic
 * (words of length >2, to skip stop-words / punctuation), which the peer server `AnnotationReanchorService`
 * fuzzy scorer models more richly (Levenshtein) — this client fallback only needs to pick the right
 * paragraph, not a character span.
 */
function fuzzyMatches(blockText: string, anchorText: string): boolean {
  const a = normalize(anchorText);
  if (!a) return false;
  const b = normalize(blockText);
  if (b.includes(a) || a.includes(b)) return true;

  const words = a.split(' ').filter(w => w.length > 2);
  if (words.length === 0) return false;
  const hits = words.filter(w => b.includes(w)).length;
  return hits / words.length >= 0.6;
}

/**
 * Resolve the target paragraph for a revision: `paraId` EXACT first (primary anchor, exact within our own
 * round-trip), then the fuzzy fallback — the `paragraphHint`-indexed block when its text loosely matches
 * `anchorText` ({@link fuzzyMatches}), else the first block that fuzzy-matches. Returns `null` when nothing
 * anchors (the caller counts it unresolved and renders nothing — never guesses).
 */
export function resolveBlock(blocks: readonly BlockInfo[], target: AnchorHint): BlockInfo | null {
  // Primary: exact w14:paraId match (the editor carries the same server-stamped ids).
  if (target.paraId) {
    const byParaId = blocks.find(b => b.paraId === target.paraId);
    if (byParaId) return byParaId;
  }

  // Fuzzy fallback (cross-Word-session): the document-order hint, verified loosely against anchorText.
  const anchor = target.anchorText ?? '';
  if (target.paragraphHint >= 0 && target.paragraphHint < blocks.length) {
    const atHint = blocks[target.paragraphHint];
    if (!normalize(anchor) || fuzzyMatches(atHint.text, anchor)) {
      return atHint;
    }
  }

  // Last resort: any block whose settled text fuzzy-matches the anchor text.
  if (normalize(anchor)) {
    const byAnchor = blocks.find(b => fuzzyMatches(b.text, anchor));
    if (byAnchor) return byAnchor;
  }

  return null;
}

/** Stable, distinguishing `imported:` ledgerRef for a revision (its OOXML id, else a paraId+index key). */
function ledgerRefFor(revision: ImportedRevision, index: number): string {
  const id = revision.id && revision.id.length > 0 ? revision.id : `${revision.paraId ?? 'p'}#${index}`;
  return `${IMPORTED_LEDGER_PREFIX}${id}`;
}

/** A flat char index of the text WITHIN one block, mapping each char to its ProseMirror position. Exported
 * (task 051) for reuse by {@link ../widgets/importedComments.ts}'s comment-anchor mark placement. */
export function blockCharIndex(editor: Editor, from: number, to: number): { text: string; positions: number[] } {
  const chars: string[] = [];
  const positions: number[] = [];
  editor.state.doc.nodesBetween(from, to, (node, pos) => {
    if (node.isText && typeof node.text === 'string') {
      for (let i = 0; i < node.text.length; i++) {
        chars.push(node.text[i]);
        positions.push(pos + i);
      }
    }
    return true;
  });
  return { text: chars.join(''), positions };
}

/** Locate `revision.text` in the block's live prose (mammoth kept it) and apply the insertion mark. */
function applyInsertion(editor: Editor, block: BlockInfo, revision: ImportedRevision, ledgerRef: string): boolean {
  if (revision.text.length === 0) return false;
  const markType = editor.state.schema.marks[INSERTION_MARK];
  if (!markType) return false;

  const { text, positions } = blockCharIndex(editor, block.from, block.to);
  const idx = text.indexOf(revision.text);
  if (idx === -1) return false;

  const from = positions[idx];
  const to = positions[idx + revision.text.length - 1] + 1;
  const mark = markType.create({
    ledgerRef,
    author: revision.author || null,
    date: revision.date || null,
  });
  const tr = editor.state.tr.addMark(from, to, mark);
  tr.setMeta('addToHistory', false);
  editor.view.dispatch(tr);
  return true;
}

/** Re-insert `revision.text` (dropped by mammoth) at the block's end carrying the deletion mark. */
function applyDeletion(editor: Editor, block: BlockInfo, revision: ImportedRevision, ledgerRef: string): boolean {
  if (revision.text.length === 0) return false;
  const markType = editor.state.schema.marks[DELETION_MARK];
  if (!markType) return false;

  const node = editor.state.schema.text(revision.text, [
    markType.create({
      ledgerRef,
      author: revision.author || null,
      date: revision.date || null,
    }),
  ]);
  // Insert just inside the block's closing boundary (end of its inline content).
  const at = block.to - 1;
  const tr = editor.state.tr.insert(at, node);
  tr.setMeta('addToHistory', false);
  editor.view.dispatch(tr);
  return true;
}

/**
 * Render each recovered Word revision as a first-class, accept/reject-able insertion/deletion mark in the
 * editor, anchored by `paraId` (primary) with an `anchorText`/`paragraphHint` fuzzy fallback. Call ONCE per
 * `.docx` mount, AFTER `stampParaIds` (so the editor's paragraphs carry their server paraIds) and BEFORE
 * `captureParaIdSnapshot` (so an imported revision's marks are folded into the load-time reject-state
 * baseline and are NOT mistaken for a user edit on the next save). No-op when `revisions` is empty.
 *
 * @param editor    the live TipTap editor (must have the insertion/deletion marks + paraId attr in schema)
 * @param revisions the imported revisions from the Load response (`ImportedRevision[]`), in document order
 * @returns how many revisions rendered vs could not anchor (diagnostic only — never throws)
 */
export function applyImportedRevisions(
  editor: Editor | null,
  revisions: readonly ImportedRevision[] | undefined
): ApplyImportedRevisionsResult {
  if (!editor || !revisions || revisions.length === 0) {
    return { applied: 0, unresolved: 0 };
  }

  let applied = 0;
  let unresolved = 0;

  revisions.forEach((revision, index) => {
    // Re-collect blocks each iteration: a prior deletion re-insert shifts later block positions, so
    // stale `from`/`to` would corrupt the next apply. Revision counts are small, so the re-walk is cheap.
    const blocks = collectBlocks(editor);
    const block = resolveBlock(blocks, revision);
    if (!block) {
      unresolved++;
      return;
    }

    const ledgerRef = ledgerRefFor(revision, index);
    const ok =
      revision.kind === 'deletion'
        ? applyDeletion(editor, block, revision, ledgerRef)
        : applyInsertion(editor, block, revision, ledgerRef);

    if (ok) {
      applied++;
    } else {
      unresolved++;
    }
  });

  return { applied, unresolved };
}

export default applyImportedRevisions;
