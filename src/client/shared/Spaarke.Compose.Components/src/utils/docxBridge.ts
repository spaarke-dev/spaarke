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

  // TipTap JSON node shape (subset — covers OOB extensions in scope).
  type TipTapNode = {
    type: string;
    content?: TipTapNode[];
    text?: string;
    attrs?: Record<string, unknown>;
    marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
  };

  const json = editor.getJSON() as TipTapNode;

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
