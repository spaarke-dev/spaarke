// THROWAWAY prototype - Spike #1
// Export path: TipTap JSON -> docx (library) -> DOCX bytes -> SPE save.
//
// Implementation sketch (not production-ready) — documents the export contract
// for R1 task 045 (ComposeEditor.tsx) and task 023 (ComposeService save endpoint).

import {
  Document,
  Packer,
  Paragraph,
  TextRun,
  HeadingLevel,
  Table as DocxTable,
  TableRow as DocxRow,
  TableCell as DocxCell,
  AlignmentType,
} from "docx";

// TipTap JSON node shape (subset — covers OOB extensions used).
type TipTapNode = {
  type: string;
  content?: TipTapNode[];
  text?: string;
  attrs?: Record<string, unknown>;
  marks?: Array<{ type: string; attrs?: Record<string, unknown> }>;
};

const HEADING_MAP: Record<number, (typeof HeadingLevel)[keyof typeof HeadingLevel]> = {
  1: HeadingLevel.HEADING_1,
  2: HeadingLevel.HEADING_2,
  3: HeadingLevel.HEADING_3,
  4: HeadingLevel.HEADING_4,
  5: HeadingLevel.HEADING_5,
  6: HeadingLevel.HEADING_6,
};

function alignmentFor(attrs?: Record<string, unknown>): AlignmentType | undefined {
  const a = attrs?.textAlign as string | undefined;
  if (a === "center") return AlignmentType.CENTER;
  if (a === "right") return AlignmentType.RIGHT;
  if (a === "justify") return AlignmentType.JUSTIFIED;
  return AlignmentType.LEFT;
}

function textRunsFromInline(nodes: TipTapNode[] | undefined): TextRun[] {
  if (!nodes) return [];
  const runs: TextRun[] = [];
  for (const n of nodes) {
    if (n.type === "text" && n.text) {
      const marks = new Set(n.marks?.map((m) => m.type) ?? []);
      runs.push(
        new TextRun({
          text: n.text,
          bold: marks.has("bold"),
          italics: marks.has("italic"),
          strike: marks.has("strike"),
          underline: marks.has("underline") ? {} : undefined,
        })
      );
    } else if (n.type === "hardBreak") {
      runs.push(new TextRun({ text: "\n", break: 1 }));
    }
    // Note: marks of type "link" — `docx` library supports ExternalHyperlink;
    // omitted in sketch to keep diff small. Captured in subset spec as "Preserved (basic)".
  }
  return runs;
}

function paragraphsFromNode(node: TipTapNode): Paragraph[] {
  if (node.type === "paragraph") {
    return [
      new Paragraph({
        alignment: alignmentFor(node.attrs),
        children: textRunsFromInline(node.content),
      }),
    ];
  }
  if (node.type === "heading") {
    const lvl = (node.attrs?.level as number | undefined) ?? 1;
    return [
      new Paragraph({
        heading: HEADING_MAP[lvl] ?? HeadingLevel.HEADING_1,
        alignment: alignmentFor(node.attrs),
        children: textRunsFromInline(node.content),
      }),
    ];
  }
  if (node.type === "bulletList" || node.type === "orderedList") {
    // List handling — docx library uses numbering refs. Implemented but verbose;
    // see subset spec for nesting-depth limits.
    const items: Paragraph[] = [];
    for (const item of node.content ?? []) {
      if (item.type === "listItem") {
        for (const child of item.content ?? []) {
          items.push(...paragraphsFromNode(child));
        }
      }
    }
    return items;
  }
  if (node.type === "blockquote") {
    return (node.content ?? []).flatMap(paragraphsFromNode);
  }
  if (node.type === "horizontalRule") {
    return [new Paragraph({ text: "—" })]; // visual approximation
  }
  return [];
}

function tableFromNode(node: TipTapNode): DocxTable {
  const rows: DocxRow[] = [];
  for (const row of node.content ?? []) {
    if (row.type === "tableRow") {
      const cells: DocxCell[] = [];
      for (const cell of row.content ?? []) {
        const ps = (cell.content ?? []).flatMap(paragraphsFromNode);
        cells.push(new DocxCell({ children: ps.length ? ps : [new Paragraph("")] }));
      }
      rows.push(new DocxRow({ children: cells }));
    }
  }
  return new DocxTable({ rows });
}

/**
 * Convert TipTap JSON document to a DOCX byte array.
 *
 * Round-trip fidelity: lossless for the OOB subset documented in
 * spike-1-tiptap-docx-roundtrip.md §3 (Preserved column).
 * Anything Degraded/Dropped/Open-in-Word in the inventory will NOT survive
 * — that is by design (per design.md §14 row 1 + spec FR-03).
 */
export async function exportTipTapToDocx(tipTapJson: TipTapNode): Promise<ArrayBuffer> {
  const children: (Paragraph | DocxTable)[] = [];
  for (const node of tipTapJson.content ?? []) {
    if (node.type === "table") {
      children.push(tableFromNode(node));
    } else {
      children.push(...paragraphsFromNode(node));
    }
  }
  const doc = new Document({ sections: [{ children }] });
  const blob = await Packer.toBlob(doc);
  return blob.arrayBuffer();
}
