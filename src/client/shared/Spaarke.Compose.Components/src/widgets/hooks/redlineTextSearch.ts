/**
 * redlineTextSearch.ts — THE PROSE-MATCHING COLLABORATOR (spaarkeai-compose-r8 task 055).
 *
 * Extracted VERBATIM from `usePendingRedline.ts` (nothing changed, nothing retired — task 052 owns
 * retirement). It is a MOVE, and it exists for one reason: to make the text search a REPLACEABLE
 * collaborator instead of an inlined private function.
 *
 * WHY THAT MATTERS. FR-C01/C02/C03's contract is not "an anchored edit lands in the right place" —
 * it is "an anchored edit NEVER REACHES THE TEXT SEARCH". Those are different claims, and only the
 * second one rules out the wrong-occurrence failure this whole track removes: an edit could land
 * correctly by luck (the prose happened to be unique) while still taking the search route, and an
 * output-inspecting test would call that a pass. The server proves the real claim structurally, by
 * passing a throwing `IComposeEditValidator` into `ComposeEditAnchorPass.Validate` (see
 * `ThrowIfTextSearched` in `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs`). The
 * client had no equivalent seam: ts-jest compiles this package to CommonJS, where a same-module call
 * to `resolveTargetSpans` is a direct local reference that no spy or module mock can intercept. A
 * module boundary is the only thing that gives the client the same tripwire — hence this file.
 *
 * CONTRACT: pure. No React, no editor mutation, no I/O — it reads the live document and returns
 * ProseMirror spans. `usePendingRedline` re-exports {@link resolveTargetSpans} unchanged, so every
 * existing importer (`ComposeEditor`, `useDocQaHighlight`, `hooks/index.ts`) is untouched.
 *
 * @see ./usePendingRedline.ts — the consumer, and the re-export that preserves the public surface.
 * @see ../composeAnchorResolution.ts — the DETERMINISTIC sibling this leg is the fallback for.
 * @see ./usePendingRedline.wholeDocument.test.tsx — the tripwire this seam exists to make possible.
 */
import type { Editor } from '@tiptap/core';

/** How the client resolves `target_text` in the document (adeu `match_mode`, Spike 2). */
export type RedlineMatchMode = 'strict' | 'first' | 'all';

/** A resolved document range in ProseMirror positions. */
export interface RedlineSpan {
  from: number;
  to: number;
}

/** Outcome of resolving a `target_text` against the current document. */
export type ResolveResult =
  | { ok: true; spans: RedlineSpan[] }
  | { ok: false; kind: 'not_found' | 'ambiguous'; matchCount: number };

/**
 * STRICTLY 1:1 character fold for tolerant redline anchoring (round-3 UAT Test #4). Word/DOCX text
 * carries typographic characters — curly quotes, non-breaking / thin / figure spaces, en/em/figure/
 * non-breaking dashes — that the model routinely straightens when it echoes a clause back as
 * `target_text`. A raw substring match then misses. Folding BOTH the document index and the target
 * through the SAME per-character map closes that gap. It is deliberately 1:1 (one code unit in → one
 * out) so `buildCharIndex`'s char→ProseMirror-position map stays exactly aligned; non-1:1 folds
 * (whitespace collapse, ligature expansion, zero-width stripping) are NOT done here — they would
 * desynchronize positions and are Phase-2.
 */
const MATCH_FOLD: Readonly<Record<string, string>> = {
  // single quotes / apostrophes / prime → straight apostrophe
  '‘': "'",
  '’': "'",
  '‚': "'",
  '‛': "'",
  '′': "'",
  ʼ: "'",
  '`': "'",
  '´': "'",
  // double quotes / double prime → straight quote
  '“': '"',
  '”': '"',
  '„': '"',
  '‟': '"',
  '″': '"',
  // non-breaking / thin / figure / narrow-no-break spaces → regular space
  ' ': ' ',
  ' ': ' ',
  ' ': ' ',
  ' ': ' ',
  // hyphen / en / em / figure / horizontal-bar / non-breaking hyphen / minus → hyphen-minus
  '‐': '-',
  '‑': '-',
  '‒': '-',
  '–': '-',
  '—': '-',
  '―': '-',
  '−': '-',
};

/**
 * Item 1 (UAT round-4): invisible / zero-width characters that carry NO visible glyph and are NEVER
 * present in a model-authored `target_text`, but DO leak into mammoth-flattened editor text (and into
 * some source docs — e.g. copy-pasted patent boilerplate). Stripped in the TOLERANT fallback pass only
 * (dropping them is non-1:1, so it stays out of the precise 1:1 pass). Zero-width space (U+200B),
 * zero-width no-break space / BOM (U+FEFF), soft hyphen (U+00AD), word joiner (U+2060), zero-width
 * non-joiner/joiner (U+200C/U+200D). A single such char on one side otherwise defeats an exact match.
 */
const INVISIBLE_STRIP = /[\u200B\u200C\u200D\u2060\uFEFF\u00AD]/g;

/** Fold one character to its match-normal form (1:1). */
function normalizeChar(ch: string): string {
  return MATCH_FOLD[ch] ?? ch;
}

/** True for an invisible/zero-width char {@link INVISIBLE_STRIP} drops in the tolerant pass. */
function isInvisibleStrip(ch: string): boolean {
  const c = ch.charCodeAt(0);
  return c === 0x200b || c === 0x200c || c === 0x200d || c === 0x2060 || c === 0xfeff || c === 0x00ad;
}

/** Fold a string per-code-unit with {@link normalizeChar} — same iteration granularity as buildCharIndex. */
function normalizeForMatch(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i++) out += normalizeChar(text[i]);
  return out;
}

/**
 * Build a flat character index of the document's text, mapping each character to its ProseMirror
 * position. A NUL sentinel (`\u0000`, position -1) separates text blocks so a `target_text` snippet
 * never false-matches across a paragraph boundary. Targets never contain NUL, so a match can never
 * include a sentinel — every matched position is a real, contiguous text position.
 */
function buildCharIndex(editor: Editor): { text: string; positions: number[] } {
  const chars: string[] = [];
  const positions: number[] = [];
  editor.state.doc.descendants((node, pos) => {
    if (node.isTextblock) {
      if (chars.length > 0) {
        chars.push('\u0000');
        positions.push(-1);
      }
      return true;
    }
    if (node.isText && typeof node.text === 'string') {
      for (let i = 0; i < node.text.length; i++) {
        // 1:1 fold (curly quotes / NBSP / typographic dashes → straight forms) so the target — which
        // the model straightens — still matches. positions[] keeps the ORIGINAL PM position per char.
        chars.push(normalizeChar(node.text[i]));
        positions.push(pos + i);
      }
    }
    return true;
  });
  return { text: chars.join(''), positions };
}

/**
 * Fix #4 (UAT 2026-07-20): derive a WHITESPACE-COLLAPSED view of a {@link buildCharIndex} result — each
 * run of whitespace becomes a single space mapped to the PM position of the run's FIRST char; the NUL
 * block sentinel is preserved verbatim (and breaks any run, so no cross-paragraph match). The model
 * authors `target_text` against the Document-Intelligence extraction while we match against mammoth's
 * flattened text; the two normalize spacing/tabs/line-breaks differently, so a byte-verbatim target
 * often differs from the editor text ONLY in whitespace. Every retained char keeps a real PM position,
 * so the matched span endpoints stay exact even though the collapse is non-1:1.
 */
function collapseWhitespaceIndex(raw: { text: string; positions: number[] }): { text: string; positions: number[] } {
  const chars: string[] = [];
  const positions: number[] = [];
  let inRun = false;
  for (let i = 0; i < raw.text.length; i++) {
    const c = raw.text[i];
    if (c === '\u0000') {
      // Block sentinel — preserved; it terminates any whitespace run so a needle can't span blocks.
      chars.push(c);
      positions.push(raw.positions[i]);
      inRun = false;
    } else if (isInvisibleStrip(c)) {
      // Item 1 (UAT round-4): drop zero-width / soft-hyphen chars entirely (non-1:1, tolerant-pass
      // only). They carry no glyph and are absent from model targets, but leak into flattened editor
      // text — a single one otherwise defeats an exact match. Dropping keeps every RETAINED char's
      // real PM position, so matched span endpoints stay exact. Does NOT touch `inRun` (an invisible
      // between two spaces must not un-collapse the surrounding whitespace run).
      continue;
    } else if (/\s/.test(c)) {
      if (inRun) continue;
      chars.push(' ');
      positions.push(raw.positions[i]);
      inRun = true;
    } else {
      chars.push(c);
      positions.push(raw.positions[i]);
      inRun = false;
    }
  }
  return { text: chars.join(''), positions };
}

/** Find every span of `targetText` in the doc — 1:1-folded (precise) or additionally whitespace-collapsed
 * (tolerant). Returns real, contiguous PM spans (see {@link buildCharIndex}). */
function findTargetMatches(editor: Editor, targetText: string, collapseWhitespace: boolean): RedlineSpan[] {
  const raw = buildCharIndex(editor);
  const { text, positions } = collapseWhitespace ? collapseWhitespaceIndex(raw) : raw;
  const needle = collapseWhitespace
    ? // Item 1: mirror collapseWhitespaceIndex — strip invisibles, THEN collapse whitespace, so a
      // target carrying (or missing) a zero-width/soft-hyphen char still matches the collapsed view.
      normalizeForMatch(targetText).replace(INVISIBLE_STRIP, '').replace(/\s+/g, ' ').trim()
    : normalizeForMatch(targetText);
  if (!needle) return [];

  const matches: RedlineSpan[] = [];
  let idx = text.indexOf(needle);
  while (idx !== -1) {
    const from = positions[idx];
    const to = positions[idx + needle.length - 1] + 1;
    matches.push({ from, to });
    idx = text.indexOf(needle, idx + needle.length);
  }
  return matches;
}

/**
 * Resolve a `target_text` snippet to document span(s) under the adeu `match_mode` contract:
 *  - `strict` — exactly one match required; 0 → not_found, >1 → ambiguous (do NOT guess);
 *  - `first`  — the first match (≥1 required);
 *  - `all`    — every match.
 * Exported for direct unit testing.
 */
export function resolveTargetSpans(editor: Editor, targetText: string, matchMode: RedlineMatchMode): ResolveResult {
  if (!targetText) return { ok: false, kind: 'not_found', matchCount: 0 };

  // Pass 1 — PRECISE: the 1:1 fold (smart-quote / NBSP / typographic-dash), length-preserving.
  let matches = findTargetMatches(editor, targetText, false);
  // Pass 2 — TOLERANT FALLBACK (Fix #4): only when the precise pass found NOTHING, retry with whitespace
  // collapsed so a spacing/tab/line-break divergence between the model's target_text and the
  // mammoth-flattened editor text no longer defeats an otherwise-exact match. Because it runs only on a
  // precise-miss, it never loosens an already-unambiguous match. A genuine paraphrase (a changed/dropped
  // word) still finds nothing → the FR-19 "do not guess" refusal is preserved.
  if (matches.length === 0) {
    matches = findTargetMatches(editor, targetText, true);
  }

  if (matches.length === 0) return { ok: false, kind: 'not_found', matchCount: 0 };
  if (matchMode === 'strict' && matches.length > 1) {
    return { ok: false, kind: 'ambiguous', matchCount: matches.length };
  }
  if (matchMode === 'all') return { ok: true, spans: matches };
  // strict-unique or first → the first (and, for strict, only) match.
  return { ok: true, spans: [matches[0]] };
}
