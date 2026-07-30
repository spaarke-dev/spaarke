/**
 * ndaClauseLocation.ts — derive a clear clause LOCATION LABEL from the live editor document
 * (ai-advanced-capabilities-nda-r1, UAT round-5 #1/#3/#6).
 *
 * The NDA-REVIEW model's `sectionRef` is free text that carries, at best, a page + paragraph (e.g.
 * "Paragraph 5 (p. 1)") — it does NOT carry the section HEADING ("Agreement Not To Disclose
 * Confidential Information") or a section ordinal. Those live in the document, so once the Review
 * Summary is hosted INSIDE the editor (round-5 #1), we can walk the doc to the heading that governs a
 * flagged clause and produce the label the reviewer asked for: "Pg 1 · Sec 3 · Para 1 · Agreement Not
 * To Disclose Confidential Information".
 *
 * Heading detection is tolerant of how the DOCX→editor projection represents headings: a real
 * `heading` node OR a paragraph carrying a `pStyle` of `Heading1..Heading6` (the hidden style attribute
 * `useComposeDocumentStyles` introduces). If NO governing heading is found (e.g. a flat doc, or an
 * anchor that couldn't be resolved), it falls back to {@link formatClauseLocation} on the model's
 * `sectionRef` — so the label is never blank and never worse than round-5 batch A.
 *
 * @see ./NdaReviewSummaryPanel.tsx — `formatClauseLocation` (the model-only fallback formatter)
 * @see ./hooks/useComposeDocumentStyles.ts — the `pStyle` heading attribute
 * @see ./ComposeCommentGutter.tsx / ./ComposeEditor.tsx — callers (gutter note + summary row)
 */
import type { Node as PMNode } from '@tiptap/pm/model';
import { formatClauseLocation } from './NdaReviewSummaryPanel';

/** True when `node` is a document heading — a `heading` node OR a paragraph styled `Heading1..6`. */
function isHeadingNode(node: PMNode): boolean {
  if (node.type.name === 'heading') return true;
  const pStyle = (node.attrs as { pStyle?: unknown }).pStyle;
  return typeof pStyle === 'string' && /heading\s*[1-6]?/i.test(pStyle);
}

/**
 * The WS-3/WS-4 computed legal number of a node, if it carries one (e.g. `"2"` / `"4.2"`). This is the
 * `computedNumber` attribute `composeNumberAtomExtension.ts` (task 032) registers on `paragraph`/`heading`
 * nodes from the projection's `data-computed-number` — the exact number Word displays + the editor renders
 * as its non-editable number-atom. Deterministically produced server-side by
 * `ComposeDocxProjectionBuilder`'s `NumberingComputationEngine` (task 031).
 */
function computedNumberOf(node: PMNode): string | undefined {
  const cn = (node.attrs as { computedNumber?: unknown }).computedNumber;
  return typeof cn === 'string' && cn.trim() ? cn.trim() : undefined;
}

/**
 * The computed legal number of the block that CONTAINS `pos`, when that block is itself a numbered
 * clause/section (e.g. the NDA's "2. If the Confidential Information …" — a numbered PARAGRAPH, not a
 * heading). This is what lets the location label cite "Sec 2" for numbered-paragraph clauses that
 * `findGoverningHeading` cannot see (they are not heading nodes). Returns undefined for unnumbered blocks.
 */
export function computedNumberAt(doc: PMNode, pos: number): string | undefined {
  const clamped = Math.min(Math.max(pos, 0), doc.content.size);
  const $pos = doc.resolve(clamped);
  for (let depth = $pos.depth; depth >= 0; depth--) {
    const found = computedNumberOf($pos.node(depth));
    if (found) return found;
  }
  const at = doc.nodeAt(clamped);
  return at ? computedNumberOf(at) : undefined;
}

/** Parse the page number from the model's free-text `sectionRef` (e.g. "(p. 3)"). */
function parsePage(sectionRef: string): string | undefined {
  return /\(?\bp(?:g|age)?\.?\s*(\d+)\)?/i.exec(sectionRef)?.[1];
}

/** Parse the paragraph number from the model's free-text `sectionRef` (e.g. "para 2"). */
function parseParagraph(sectionRef: string): string | undefined {
  return /\bpara(?:graph)?\.?\s*(\d+)/i.exec(sectionRef)?.[1];
}

/**
 * The heading governing `pos` + its 1-based ordinal among the document's headings, or null. Also carries
 * the heading's own WS-3 `computedNumber` when it is a style-linked numbered heading (e.g. `"4.2"`), so the
 * label can prefer the real legal number over the positional ordinal. `computedNumber` is undefined for
 * plain (unnumbered) headings — Jest `toEqual` ignores undefined properties, so existing `{heading, ordinal}`
 * expectations are unaffected.
 */
export function findGoverningHeading(
  doc: PMNode,
  pos: number
): { heading: string; ordinal: number; computedNumber?: string } | null {
  let ordinal = 0;
  let governing: { heading: string; ordinal: number; computedNumber?: string } | null = null;
  doc.descendants((node, nodePos) => {
    if (!isHeadingNode(node)) return true;
    ordinal += 1;
    // The governing heading is the LAST heading that starts at or before the clause position.
    if (nodePos <= pos) {
      const heading = node.textContent.trim();
      if (heading) governing = { heading, ordinal, computedNumber: computedNumberOf(node) };
    }
    return true; // keep descending (headings can nest; textContent covers the whole heading)
  });
  return governing;
}

/**
 * Build the full location label for a flagged clause at document position `pos`. Page + paragraph come
 * from the model's `sectionRef` (what it reliably emits); section ordinal + heading come from the live
 * document. Falls back to {@link formatClauseLocation} when no governing heading is found.
 *
 * @param doc         the live ProseMirror document
 * @param pos         the resolved start position of the flagged clause (from the anchor / strict match)
 * @param sectionRef  the model's free-text section reference (may be undefined)
 */
export function deriveClauseLocationLabel(doc: PMNode, pos: number | null, sectionRef?: string): string {
  const ref = (sectionRef ?? '').trim();
  if (pos === null) return formatClauseLocation(ref);

  // The clause's OWN computed legal number (a numbered paragraph, e.g. the NDA's "2. …") is the most
  // precise citation and the one the reviewer sees rendered. Fall back to a style-linked governing
  // heading's computed number, then to the positional heading ordinal (round-5 behavior).
  const clauseNumber = computedNumberAt(doc, pos);
  const governing = findGoverningHeading(doc, pos);
  const sec = clauseNumber ?? governing?.computedNumber ?? (governing ? String(governing.ordinal) : undefined);

  // No section signal at all (no computed number, no heading) → model-only label (never blank).
  if (!sec) return formatClauseLocation(ref);

  const page = parsePage(ref);
  const paragraph = parseParagraph(ref);
  const parts: string[] = [];
  if (page) parts.push(`Pg ${page}`);
  parts.push(`Sec ${sec}`);
  if (paragraph) parts.push(`Para ${paragraph}`);
  if (governing) parts.push(governing.heading);
  return parts.join(' · ');
}
