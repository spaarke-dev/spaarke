/**
 * docxBridge — DOCX ↔ TipTap conversion helpers (R1 LOCKED behaviour).
 *
 * Project:     spaarkeai-compose-r1, task 045 (Phase 4 W4).
 * Locked spec: `projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md`
 *
 * Direction split:
 *  - IMPORT (DOCX → HTML → TipTap):  mammoth ^1.8.0 (BSD-2-Clause)
 *  - EXPORT (TipTap JSON → DOCX):    docx     ^9.0.3 (MIT)
 *
 * Both libraries are **lazy-loaded** via dynamic `import()` so the editor's
 * cold-load cost stays small (per CHAT-ATTACHMENT-POLICY.md lazy-load
 * precedent in `useChatFileAttachment.ts`). Mammoth + docx together add
 * ~150-200 KB minified-gzipped; they are only fetched when the user actually
 * loads or saves a DOCX.
 *
 * Round-trip fidelity is governed by the **LOCKED Spike #1 OOB subset**
 * (§3.2 of the spike artifact). Features classified "Preserved" survive;
 * "Degraded" survive with documented loss; "Dropped" are silently removed
 * on import (R1; R2 adds import-time warnings). Multi-level numbering is
 * the most consequential R1 limitation — `Open in Word` (FR-12) is the
 * documented escape hatch.
 *
 * Privacy (ADR-015 Tier 3): document text payloads pass through these
 * helpers in-memory only. NO logging of document content. The
 * `MammothConversionResult` exposes mammoth's per-conversion `messages`
 * array (warnings about unsupported styles, numbering refs lost, etc.) —
 * these are SAFE to log (configuration metadata, not user content). The
 * `html`/`docxBytes` payloads are NOT safe to log.
 *
 * This module is import-side and export-side ONLY. It does NOT speak to
 * the BFF, does NOT speak to SPE, does NOT speak to Microsoft Graph.
 * Document bytes arrive from the host (via `ComposeEditor` props) and
 * return to the host. SPE plumbing lives in `ComposeDocumentService` /
 * Compose BFF endpoints.
 *
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md §3 (extension inventory) + §4 (library choice) + §4.5 (client-side conversion strategy)
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-prototype/src/Editor.tsx (mammoth wiring reference)
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-prototype/src/exportDocx.ts (docx wiring reference)
 */

import type { Editor } from '@tiptap/core';
import type {
  ParaIdMapEntry,
  ComposeEditedParagraph,
  ComposeContentModel,
  ComposeContentBlock,
  ComposeInlineRun,
  ComposeTable,
  ComposeTableRow,
  ComposeTableCell,
  ComposeAlignment,
} from '../types/compose-contracts';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

/**
 * Result of a DOCX → HTML conversion via mammoth.
 *
 * `html` is the TipTap-compatible HTML markup (set via `editor.commands.setContent`).
 * `messages` is mammoth's per-conversion warning array — surfaces unsupported
 * style references, unmapped numbering, dropped features. R1 captures these
 * but does NOT yet present them to the user (deferred to R2 per spike §5.4).
 */
export interface MammothConversionResult {
  /** TipTap-compatible HTML markup. Tier 3 (carries user document content). */
  html: string;
  /**
   * Per-conversion warnings (e.g. "unrecognized style: Heading 9",
   * "unsupported numbering: 1.1.1"). Each entry is `{ type, message }`.
   * Tier 1 safe (configuration metadata; no document content).
   */
  messages: Array<{ type: string; message: string }>;
}

// ---------------------------------------------------------------------------
// Import path: DOCX bytes → TipTap HTML (via mammoth)
// ---------------------------------------------------------------------------

/**
 * Convert DOCX bytes to TipTap-compatible HTML.
 *
 * Lazy-loads mammoth on first call. Subsequent calls reuse the loaded module.
 *
 * Behaviour notes:
 *  - Mammoth maps `<w:b>`, `<w:i>`, `<w:u>`, `<w:strike>` → HTML inline marks
 *  - Headings 1-6 → `<h1>`-`<h6>`
 *  - BulletList / OrderedList → `<ul>` / `<ol>` (single-level OOB-preserved;
 *    multi-level OOB-degraded per spike §3.2 row 8)
 *  - Tables → `<table><thead><tr><th>` / `<tbody><tr><td>`
 *  - Images → inline base64 data URIs (Image extension `allowBase64: true`)
 *  - Field codes (DATE, AUTHOR, REF) → resolved to current value or dropped
 *  - Headers/footers, page breaks, comments → dropped silently (Open-in-Word
 *    is the FR-12 escape hatch)
 *
 * @param docxBytes  Raw DOCX bytes (typically from SPE drive-item content)
 * @returns          HTML markup + conversion warnings
 * @throws           Error wrapping any mammoth failure (caller decides UX)
 */
export async function docxToTipTapHtml(docxBytes: ArrayBuffer): Promise<MammothConversionResult> {
  // Lazy-load mammoth (BSD-2-Clause). First call pays the bundle cost.
  // Subsequent calls reuse the module from the module-graph cache.
  //
  // mammoth's @types/mammoth declares the module exports as a namespace with
  // `convertToHtml`, `extractRawText`, etc. as top-level functions; bundlers
  // sometimes wrap this in a `.default` interop shim. Handle both shapes via
  // `unknown`-cast probing — cleaner than fighting the type system on a
  // dynamic-import boundary that runs once.
  const mammothModule = await import('mammoth');
  const mammothCandidate = mammothModule as unknown as {
    default?: { convertToHtml: typeof mammothModule.convertToHtml };
    convertToHtml?: typeof mammothModule.convertToHtml;
  };
  const convertToHtml = mammothCandidate.default?.convertToHtml ?? mammothCandidate.convertToHtml;
  if (!convertToHtml) {
    throw new Error('docxBridge: mammoth.convertToHtml export not found');
  }

  const result = await convertToHtml({ arrayBuffer: docxBytes });

  return {
    html: result.value,
    // mammoth Result.messages is `Array<{ type: 'warning' | 'error'; message: string }>`
    messages: result.messages.map(m => ({ type: m.type, message: m.message })),
  };
}

// ---------------------------------------------------------------------------
// R3 FR-08/FR-09/FR-10 — `w14:paraId` identity carry (design §5)
// ---------------------------------------------------------------------------
//
// mammoth flattens `.docx` to HTML and DISCARDS `w14:paraId` (docxToTipTapHtml
// above), so the paragraph ids arrive from the SERVER pre-parse map (task 010),
// not from the imported HTML. `stampParaIds` owns the client-side carry: an
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
 * Call immediately after {@link stampParaIds} on a load (or after a born-in-editor seed's setContent):
 * {@link collectEditedParagraphs} diffs the current doc against this to find the dirty paragraphs.
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
 * The paragraphs the user CHANGED since load (FR-01), each keyed by its `w14:paraId` with its new
 * reject-state settled text. Diffs the current doc against the load-time {@link captureParaIdSnapshot}
 * snapshot. Emits ONLY paragraphs that EXISTED at load (present in the snapshot) whose settled text
 * changed — the server synthesizer edits matched paragraphs in place and fails fast on a paraId the
 * retained original lacks, so a brand-new paraId (a split/insert) is intentionally NOT emitted here
 * (structural insert/split/delete is out of the E1 delta scope — a future synthesizer extension).
 * Unchanged paragraphs are omitted (sending them would needlessly rewrite their run structure).
 */
export function collectEditedParagraphs(
  editor: Editor,
  snapshot: ReadonlyMap<string, string>
): ComposeEditedParagraph[] {
  const edited: ComposeEditedParagraph[] = [];
  forEachBlock(editor.getJSON() as TipTapNode, block => {
    const paraId = block.attrs?.paraId as string | undefined;
    if (!paraId) return;
    const original = snapshot.get(paraId);
    if (original === undefined) return; // new paraId (split/insert) — out of E1 delta scope
    const current = rejectStateText(block);
    if (current !== original) {
      edited.push({ paraId, text: current });
    }
  });
  return edited;
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
