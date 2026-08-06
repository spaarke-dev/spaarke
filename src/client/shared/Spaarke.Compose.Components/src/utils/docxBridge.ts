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
} from '../types/compose-contracts';

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
