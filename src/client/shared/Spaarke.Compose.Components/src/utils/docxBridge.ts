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
import type { ParaIdMapEntry } from '../types/compose-contracts';

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
// Export path: TipTap state → DOCX bytes (via docx)
// ---------------------------------------------------------------------------

/**
 * Convert a TipTap editor's current state to DOCX bytes.
 *
 * Lazy-loads `docx` on first call. Round-trip fidelity is governed by the
 * Spike #1 OOB subset — anything in the OOB inventory's "Preserved" rows
 * survives; "Degraded" rows survive with documented loss.
 *
 * Implementation: pulls the TipTap JSON document via `editor.getJSON()` and
 * walks the node tree, mapping each ProseMirror node to its docx equivalent
 * (Paragraph, Heading, Table, TextRun with marks). This is intentionally
 * a focused converter for the OOB subset — NOT a general ProseMirror-to-docx
 * library.
 *
 * The conversion strategy mirrors the locked Spike #1 reference at
 * `notes/spikes/spike-1-prototype/src/exportDocx.ts` but is re-authored here
 * for production conventions (typed nodes, error handling, ArrayBuffer
 * return type, dynamic-import-friendly destructuring).
 *
 * @param editor  Live TipTap Editor instance (from `useEditor`)
 * @returns       DOCX bytes ready for upload to SPE / BFF save endpoint
 * @throws        Error if the editor JSON is malformed or docx Packer fails
 */
export async function tipTapToDocxBytes(editor: Editor): Promise<ArrayBuffer> {
  return tipTapJsonToDocxBytes(editor.getJSON() as TipTapNode);
}

/**
 * TipTap JSON node shape (subset — covers OOB extensions in scope). Exported so the redline→Word
 * save-fidelity path (UAT-R7 #2/#3/#4) can build a BASELINE document tree without a live editor.
 */
export type TipTapNode = {
  type?: string;
  content?: TipTapNode[];
  text?: string;
  attrs?: Record<string, unknown>;
  marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
};

/**
 * Build the REJECT-STATE BASELINE of a TipTap doc for redline→Word save fidelity (UAT-R7 #2/#3/#4).
 *
 * The Compose editor renders pending AI redlines as {@link ../widgets/marks/InsertionMark insertion}
 * / {@link ../widgets/marks/DeletionMark deletion} marks. A plain `tipTapToDocxBytes` flattens BOTH
 * halves to plain body text (it has no track-change branch), so the saved `.docx` ends up carrying
 * the original AND the AI text as ordinary text. Instead, Save sends a clean baseline (this) + a
 * structured annotation list, and the BFF re-applies the redlines as NATIVE `w:ins`/`w:del`/
 * `w:comment` via `DocxAnnotationWriter`.
 *
 * The baseline is the document as if every PENDING redline were REJECTED:
 *  - text carrying the `insertion` mark (the proposed new text) is DROPPED;
 *  - text carrying the `deletion` mark (the original) is KEPT, with the redline/comment marks
 *    stripped so it serializes as ordinary text;
 *  - ACCEPTED edits are already committed in the doc (accept unsets the marks → normal text), so
 *    they flow through untouched.
 *
 * Returns a new tree (the input is not mutated).
 */
export function buildRejectBaselineJson(node: TipTapNode): TipTapNode {
  const REDLINE_MARKS = new Set(['insertion', 'deletion', 'commentAnchor']);

  const transform = (n: TipTapNode): TipTapNode | null => {
    // Drop proposed-insertion text entirely (reject = it was never there).
    if (n.type === 'text' && (n.marks?.some(m => m.type === 'insertion') ?? false)) {
      return null;
    }
    const next: TipTapNode = { ...n };
    if (n.marks) {
      const kept = n.marks.filter(m => !REDLINE_MARKS.has(m.type));
      if (kept.length > 0) next.marks = kept;
      else delete next.marks;
    }
    if (n.content) {
      next.content = n.content.map(transform).filter((c): c is TipTapNode => c !== null);
    }
    return next;
  };

  return transform(node) ?? { type: 'doc', content: [] };
}

/**
 * Serialize a TipTap JSON document tree (not a live editor) to DOCX bytes. This is the JSON-in core
 * of {@link tipTapToDocxBytes}; the redline save path calls it with a {@link buildRejectBaselineJson}
 * baseline. Fidelity is governed by the same Spike #1 OOB subset.
 */
export async function tipTapJsonToDocxBytes(json: TipTapNode): Promise<ArrayBuffer> {
  // Lazy-load docx (MIT). Pure-JS pack; ~90 KB minified-gzipped.
  const docxModule = await import('docx');
  const {
    Document,
    Packer,
    Paragraph,
    TextRun,
    HeadingLevel,
    Table: DocxTable,
    TableRow: DocxRow,
    TableCell: DocxCell,
    AlignmentType,
  } = docxModule;

  const headingMap: Record<number, (typeof HeadingLevel)[keyof typeof HeadingLevel]> = {
    1: HeadingLevel.HEADING_1,
    2: HeadingLevel.HEADING_2,
    3: HeadingLevel.HEADING_3,
    4: HeadingLevel.HEADING_4,
    5: HeadingLevel.HEADING_5,
    6: HeadingLevel.HEADING_6,
  };

  function alignmentFor(attrs?: Record<string, unknown>) {
    const a = attrs?.textAlign as string | undefined;
    if (a === 'center') return AlignmentType.CENTER;
    if (a === 'right') return AlignmentType.RIGHT;
    if (a === 'justify') return AlignmentType.JUSTIFIED;
    return AlignmentType.LEFT;
  }

  function textRunsFromInline(nodes: TipTapNode[] | undefined): InstanceType<typeof TextRun>[] {
    if (!nodes) return [];
    const runs: InstanceType<typeof TextRun>[] = [];
    for (const n of nodes) {
      if (n.type === 'text' && n.text) {
        const marks = new Set(n.marks?.map(m => m.type) ?? []);
        runs.push(
          new TextRun({
            text: n.text,
            bold: marks.has('bold'),
            italics: marks.has('italic'),
            strike: marks.has('strike'),
            underline: marks.has('underline') ? {} : undefined,
          })
        );
      } else if (n.type === 'hardBreak') {
        runs.push(new TextRun({ text: '', break: 1 }));
      }
      // Link marks: `docx` exposes ExternalHyperlink; deliberately omitted for
      // R1 scope to keep the converter focused — preserved as plain text per
      // spike §3.2 row 14 "Preserved (basic)" carve-out.
    }
    return runs;
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  function paragraphsFromNode(node: TipTapNode): any[] {
    if (node.type === 'paragraph') {
      return [
        new Paragraph({
          alignment: alignmentFor(node.attrs),
          children: textRunsFromInline(node.content),
        }),
      ];
    }
    if (node.type === 'heading') {
      const lvl = (node.attrs?.level as number | undefined) ?? 1;
      return [
        new Paragraph({
          heading: headingMap[lvl] ?? HeadingLevel.HEADING_1,
          alignment: alignmentFor(node.attrs),
          children: textRunsFromInline(node.content),
        }),
      ];
    }
    if (node.type === 'bulletList' || node.type === 'orderedList') {
      // Visual nested-list preservation; semantic numbering refs lost
      // (spike §3.2 row 8 "Degraded" — most consequential R1 limitation).
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const items: any[] = [];
      for (const item of node.content ?? []) {
        if (item.type === 'listItem') {
          for (const child of item.content ?? []) {
            items.push(...paragraphsFromNode(child));
          }
        }
      }
      return items;
    }
    if (node.type === 'blockquote') {
      return (node.content ?? []).flatMap(paragraphsFromNode);
    }
    if (node.type === 'horizontalRule') {
      return [new Paragraph({ text: '—' })]; // em-dash visual approximation
    }
    if (node.type === 'taskList' || node.type === 'taskItem') {
      // Task lists: docx has no native checkbox content control in this lib;
      // convert items to paragraphs with a leading bullet character.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const items: any[] = [];
      for (const child of node.content ?? []) {
        items.push(...paragraphsFromNode(child));
      }
      return items;
    }
    return [];
  }

  function tableFromNode(node: TipTapNode) {
    const rows: InstanceType<typeof DocxRow>[] = [];
    for (const row of node.content ?? []) {
      if (row.type === 'tableRow') {
        const cells: InstanceType<typeof DocxCell>[] = [];
        for (const cell of row.content ?? []) {
          const ps = (cell.content ?? []).flatMap(paragraphsFromNode);
          cells.push(new DocxCell({ children: ps.length ? ps : [new Paragraph('')] }));
        }
        rows.push(new DocxRow({ children: cells }));
      }
    }
    return new DocxTable({ rows });
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const children: any[] = [];
  for (const node of json.content ?? []) {
    if (node.type === 'table') {
      children.push(tableFromNode(node));
    } else {
      children.push(...paragraphsFromNode(node));
    }
  }

  const doc = new Document({ sections: [{ children }] });
  const blob = await Packer.toBlob(doc);
  return blob.arrayBuffer();
}
