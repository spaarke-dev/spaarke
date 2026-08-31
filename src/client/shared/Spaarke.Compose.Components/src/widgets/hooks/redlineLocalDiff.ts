/**
 * redlineLocalDiff.ts — FR-C05 outcome 1: a sub-paragraph edit diffs LOCALLY, inside the paragraph
 * the anchor already named (spaarkeai-compose-r8 task 052).
 *
 * WHAT THIS FIXES. `resolveAnchoredSpans` resolves a `target_para_id` to that paragraph's whole
 * content range, so a three-word change struck and replaced all forty lines of a clause. The anchor
 * had already answered "WHICH paragraph"; the only open question was "WHERE INSIDE IT", and nothing
 * answered it.
 *
 * WHAT THIS IS NOT. It is NOT a search. Every index this module produces comes from a character
 * index built over ONE known paragraph's own text ({@link buildRangeCharIndex} walks exactly the
 * span the anchor resolved to), so the returned span is bounded by that paragraph BY CONSTRUCTION —
 * it cannot address another paragraph even if the model's `new_text` describes one. That is the
 * distinction ADR-049 I-7 draws: locating a paragraph by scanning document prose is forbidden;
 * comparing a known paragraph against its proposed replacement is arithmetic on two strings.
 *
 * THE THREE FALL-BACKS TO WHOLE-PARAGRAPH REPLACEMENT are each a DEFINED outcome, never a widened
 * search (see {@link computeLocalEditRange} / {@link narrowAnchoredSpan}):
 *   1. `new_text` carries the FR-15 inline-markup subset (`<strong>`/`<em>`/`<u>`) — slicing markup
 *      by plain-text offsets would emit malformed fragments, so the whole paragraph is replaced and
 *      the markup rides through {@link sanitizeInlineMarkup} intact, exactly as before this task.
 *   2. `new_text` spans paragraphs (contains a line break) — an edit addresses ONE paragraph, so a
 *      multi-block replacement is applied to the anchored paragraph as a unit rather than spliced
 *      into the middle of it. This is the escalation case the task POML names, resolved by keeping
 *      the pre-existing behaviour rather than by widening anything.
 *   3. The texts are equal — a formatting-only or no-op proposal has no changed region to mark;
 *      replacing the paragraph preserves the pre-task behaviour instead of silently rendering
 *      nothing.
 *
 * @see ./usePendingRedline.ts — `resolveAnchoredSpans` (WHICH paragraph) then this (WHERE inside it)
 * @see ./redlineTextSearch.ts — the document-scoped search this module deliberately is NOT
 */
import type { Editor } from '@tiptap/core';

/** A resolved document range in ProseMirror positions (structurally identical to `RedlineSpan`). */
export interface LocalDiffSpan {
  from: number;
  to: number;
}

/** A character index over ONE document range: `text[i]` sits at ProseMirror position `positions[i]`. */
export interface RangeCharIndex {
  text: string;
  positions: number[];
}

/**
 * The FR-15 inline-markup tags {@link sanitizeInlineMarkup} understands. A `new_text` carrying any of
 * them is replaced whole-paragraph (fall-back 1) — plain-text offsets cannot slice markup safely.
 */
const INLINE_MARKUP_RE = /<\/?(?:strong|b|em|i|u)\s*\/?>/i;

/**
 * Build a character index over `[from, to)` — the anchored paragraph's CONTENT range. Only real text
 * characters are collected, each keeping its ORIGINAL ProseMirror position, so a span built from two
 * of these positions is always a valid, contiguous document range even when the paragraph contains
 * non-text inline leaves (hard breaks, images, opaque atoms) that carry no characters.
 *
 * Deliberately range-scoped, not document-scoped: it is structurally incapable of returning a
 * position outside the paragraph it was given.
 */
export function buildRangeCharIndex(editor: Editor, from: number, to: number): RangeCharIndex {
  const chars: string[] = [];
  const positions: number[] = [];
  editor.state.doc.nodesBetween(from, to, (node, pos) => {
    if (node.isText && typeof node.text === 'string') {
      for (let i = 0; i < node.text.length; i++) {
        const at = pos + i;
        if (at < from || at >= to) continue;
        chars.push(node.text[i]);
        positions.push(at);
      }
    }
    return true;
  });
  return { text: chars.join(''), positions };
}

/** True for a character the word-boundary snap treats as a word separator. */
function isWordSeparator(ch: string): boolean {
  return /\s/.test(ch);
}

/**
 * The CHANGED REGION between a paragraph's current text and its proposed replacement, as character
 * offsets into each. `null` when the two are identical (nothing changed to mark).
 *
 * Common prefix + common suffix, then snapped OUTWARD to the nearest word boundary on each side so a
 * legal redline strikes whole words ("twelve" → "twenty-four") instead of the raw character overlap
 * ("elve" → "enty-four"). The snap only ever GROWS the changed region and only ever SHRINKS the
 * retained prefix/suffix, so `start + suffix` can never overrun either string.
 *
 * Exported for direct unit testing — it is pure string arithmetic, no editor involved.
 */
export function computeLocalEditRange(
  currentText: string,
  replacementText: string
): { start: number; endCurrent: number; endReplacement: number } | null {
  const cLen = currentText.length;
  const rLen = replacementText.length;

  let prefix = 0;
  const maxPrefix = Math.min(cLen, rLen);
  while (prefix < maxPrefix && currentText[prefix] === replacementText[prefix]) prefix++;

  let suffix = 0;
  const maxSuffix = Math.min(cLen - prefix, rLen - prefix);
  while (suffix < maxSuffix && currentText[cLen - 1 - suffix] === replacementText[rLen - 1 - suffix]) suffix++;

  // Identical strings — no changed region exists.
  if (prefix === cLen && prefix === rLen) return null;

  // Snap the region's START back to a word boundary (the char before it must be whitespace, or 0).
  while (prefix > 0 && !isWordSeparator(currentText[prefix - 1])) prefix--;
  // Snap the region's END forward to a word boundary (the retained suffix must START with whitespace).
  while (suffix > 0 && !isWordSeparator(currentText[cLen - suffix])) suffix--;

  return { start: prefix, endCurrent: cLen - suffix, endReplacement: rLen - suffix };
}

/** What {@link narrowAnchoredSpan} decided for one anchored edit. */
export interface LocalAnchoredEdit {
  /** The range to strike — possibly EMPTY (`from === to`) for a pure in-paragraph insertion. */
  readonly span: LocalDiffSpan;
  /** The replacement text for exactly that range (a slice of `new_text`, never the whole of it). */
  readonly replacement: string;
  /** The paragraph's current text — the stale-target comparison reads it, so it is computed once here. */
  readonly currentText: string;
}

/**
 * Narrow an anchored edit from "replace this whole paragraph" to "replace the words that actually
 * changed inside it". Returns `null` when the edit must stay a whole-paragraph replacement — one of
 * the three defined fall-backs in this module's header, never a widened search.
 *
 * `paragraphSpan` MUST be the paragraph CONTENT range `resolveAnchoredSpans` produced. Every position
 * in the result is drawn from a character index over that range, so the result is bounded by it.
 */
export function narrowAnchoredSpan(
  editor: Editor,
  paragraphSpan: LocalDiffSpan,
  newText: string
): LocalAnchoredEdit | null {
  const index = buildRangeCharIndex(editor, paragraphSpan.from, paragraphSpan.to);
  const currentText = index.text;

  // Fall-back 1 — inline markup cannot be sliced by plain-text offsets.
  if (INLINE_MARKUP_RE.test(newText)) return null;
  // Fall-back 2 — a replacement that spans paragraphs is applied to the anchored paragraph as a unit.
  if (/\r|\n/.test(newText)) return null;

  const range = computeLocalEditRange(currentText, newText);
  // Fall-back 3 — nothing textual changed (formatting-only / no-op proposal).
  if (range === null) return null;

  // An entirely-rewritten paragraph is already "local": the changed region IS the paragraph, and the
  // span below reproduces `paragraphSpan` exactly. No special case needed.
  const from = range.start < index.positions.length ? index.positions[range.start] : paragraphSpan.to;
  const to = range.endCurrent > range.start ? index.positions[range.endCurrent - 1] + 1 : from;

  return {
    span: { from, to },
    replacement: newText.slice(range.start, range.endReplacement),
    currentText,
  };
}

/** The anchored paragraph's current text, for the stale-target comparison. */
export function readRangeText(editor: Editor, span: LocalDiffSpan): string {
  return buildRangeCharIndex(editor, span.from, span.to).text;
}
