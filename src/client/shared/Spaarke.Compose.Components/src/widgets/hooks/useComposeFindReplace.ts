/**
 * useComposeFindReplace — FR-17 in-editor find/replace (spaarkeai-compose-r3 task 040).
 *
 * Design §7.5 "The Editing Surface" lists find/replace as a table-stakes credibility tool to BUILD
 * small, on the MIT TipTap base — NO `@tiptap-pro/*` module (no "Search & Replace Pro"). This hook
 * is the whole implementation: a plain character-index scan over the editor's doc text (mirroring
 * {@link resolveTargetSpans}'s `buildCharIndex` technique from `usePendingRedline.ts`, but WITHOUT
 * its typographic 1:1 fold — find/replace matches the LITERAL text the user typed, not a
 * model-echoed, typography-tolerant target) plus a ProseMirror {@link DecorationSet} for
 * highlighting and mark-preserving `replaceWith` transactions for the mutating half.
 *
 * MARK PRESERVATION (the load-bearing constraint, FR-17): when a match range carries
 * `InsertionMark` / `DeletionMark` / `CommentAnchorMark` (or any other mark — bold, italic, a
 * future addition), replacing it must not corrupt or orphan those marks. {@link marksForReplacement}
 * walks every inline text node touching `[from, to)` and unions their marks — the DIRECT answer to
 * "what marks does this matched range carry", applied verbatim to the replacement text.
 *
 * This is DELIBERATELY NOT `ResolvedPos.marksAcross()` (ProseMirror's boundary-typing primitive,
 * used by `Transaction.insertText` for an EMPTY selection / cursor position): `marksAcross` answers
 * "should a new character typed at this boundary continue a NON-INCLUSIVE mark", which — for the
 * exact-match-equals-marked-span case this feature exists for — DROPS `inclusive: false` marks
 * (DeletionMark, CommentAnchorMark) whenever the text immediately after the match does not also
 * carry them (the ordinary case, since the mark ends exactly where the match ends). That is the
 * wrong question for find/replace: we are not asking "what should new typing inherit", we are
 * asking "what did the replaced text carry", so every match's marks — inclusive or not — travel
 * onto the replacement.
 *
 * FIND is decoration-only (no doc mutation) — mirrors {@link QaHighlightExtension}'s file-header
 * rationale: a view decoration never appears in `editor.getHTML()`/`getJSON()`, never serializes to
 * DOCX, and clears without an undo-stack entry. REPLACE / REPLACE-ALL are the only doc-mutating
 * operations, and both preserve marks per the above.
 *
 * @see ../marks/QaHighlightExtension.ts — the sibling decoration-only highlight pattern this mirrors
 * @see ./usePendingRedline.ts (`buildCharIndex` / `resolveTargetSpans`) — the sibling text-search technique
 * @see ../ComposeEditor.tsx — mounts {@link ComposeFindReplaceExtension} + drives the panel
 * @see projects/spaarkeai-compose-r3/design.md §7.5 · spec.md FR-17
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Mark } from '@tiptap/pm/model';

/** A resolved document range in ProseMirror positions (from the search pass). */
export interface FindReplaceSpan {
  from: number;
  to: number;
}

// ---------------------------------------------------------------------------
// Decoration extension — mirrors QaHighlightExtension's plugin shape, extended
// to carry a LIST of ranges (every match) plus a "current" index (the
// prev/next-navigated match, styled distinctly).
// ---------------------------------------------------------------------------

/** Stable ProseMirror plugin key — {@link useComposeFindReplace} dispatches transactions keyed to this. */
export const composeFindReplacePluginKey = new PluginKey('composeFindReplaceHighlight');

/** CSS class applied to every non-current match decoration (ADR-021 semantic tokens in ComposeEditor's useStyles). */
export const FIND_MATCH_CLASS = 'compose-find-match';

/** CSS class applied to the current (prev/next-navigated) match decoration. */
export const FIND_MATCH_CURRENT_CLASS = 'compose-find-match-current';

type FindReplaceHighlightMeta =
  | { type: 'set'; ranges: readonly FindReplaceSpan[]; currentIndex: number }
  | { type: 'clear' };

/**
 * The TipTap extension. Registers ONE ProseMirror plugin whose state is a `DecorationSet` (empty by
 * default) — decoration-only, NEVER a doc Mark, so find highlighting never mutates the document or
 * serializes to DOCX. {@link useComposeFindReplace} sets/clears it via
 * `tr.setMeta(composeFindReplacePluginKey, meta)`.
 */
export const ComposeFindReplaceExtension = Extension.create({
  name: 'composeFindReplaceHighlight',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: composeFindReplacePluginKey,
        state: {
          init: (): DecorationSet => DecorationSet.empty,
          apply(tr, old): DecorationSet {
            const meta = tr.getMeta(composeFindReplacePluginKey) as FindReplaceHighlightMeta | undefined;
            if (meta?.type === 'set') {
              const decos = meta.ranges.map((r, i) =>
                Decoration.inline(r.from, r.to, {
                  class: i === meta.currentIndex ? FIND_MATCH_CURRENT_CLASS : FIND_MATCH_CLASS,
                })
              );
              return DecorationSet.create(tr.doc, decos);
            }
            if (meta?.type === 'clear') {
              return DecorationSet.empty;
            }
            // No relevant meta on this transaction — map the existing decorations across the
            // document change (keeps highlights anchored through unrelated edits elsewhere).
            return old.map(tr.mapping, tr.doc);
          },
        },
        props: {
          decorations(state) {
            return this.getState(state);
          },
        },
      }),
    ];
  },
});

// ---------------------------------------------------------------------------
// Search — a flat char index over the doc's text, matched literally (no
// typographic fold: find/replace matches what the user typed, verbatim).
// ---------------------------------------------------------------------------

interface CharIndex {
  text: string;
  positions: number[];
}

/**
 * A block-boundary FILLER character is pushed into `text` wherever two textblocks join, so a
 * search term never accidentally spans two different paragraphs. Detection below is POSITIONAL
 * (a `-1` entry in `positions`, mirroring `usePendingRedline.ts`'s `buildCharIndex` sentinel
 * convention) rather than content-based, so the filler's actual character value is irrelevant —
 * an ordinary space keeps this a plain, fully diffable text file.
 */
const BLOCK_BOUNDARY_FILLER = ' ';

function buildCharIndex(editor: Editor): CharIndex {
  const chars: string[] = [];
  const positions: number[] = [];
  editor.state.doc.descendants((node, pos) => {
    if (node.isTextblock) {
      if (chars.length > 0) {
        chars.push(BLOCK_BOUNDARY_FILLER);
        positions.push(-1);
      }
      return true;
    }
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

/**
 * Every non-overlapping literal occurrence of `term` in the document, honoring `caseSensitive`.
 * Exported for direct unit testing.
 */
export function findAllMatches(editor: Editor, term: string, caseSensitive: boolean): FindReplaceSpan[] {
  if (!term) return [];
  const { text, positions } = buildCharIndex(editor);
  const haystack = caseSensitive ? text : text.toLowerCase();
  const needle = caseSensitive ? term : term.toLowerCase();
  if (needle.length === 0) return [];

  const spans: FindReplaceSpan[] = [];
  let idx = haystack.indexOf(needle);
  while (idx !== -1) {
    // A match cannot legitimately straddle a block boundary (it would mean "the end of one
    // paragraph plus the start of the next" collapsed into one string) — reject it. Detected
    // POSITIONALLY (a `-1` sentinel position within the matched slice), never by character
    // content, since `BLOCK_BOUNDARY_FILLER` is an ordinary printable character.
    const straddlesBoundary = positions.slice(idx, idx + needle.length).some(p => p === -1);
    if (!straddlesBoundary) {
      const from = positions[idx];
      const to = positions[idx + needle.length - 1] + 1;
      spans.push({ from, to });
    }
    idx = haystack.indexOf(needle, idx + needle.length);
  }
  return spans;
}

// ---------------------------------------------------------------------------
// Mark-preserving replace
// ---------------------------------------------------------------------------

/**
 * The union of marks carried by every inline text node touching `[from, to)` — the set a
 * replacement of that range should carry, so a tracked-changes mark (or any other mark) present
 * anywhere in the matched text is preserved on the replacement rather than stripped or orphaned.
 * Exported for direct unit testing.
 */
export function marksForReplacement(editor: Editor, from: number, to: number): readonly Mark[] {
  let marks: readonly Mark[] = [];
  editor.state.doc.nodesBetween(from, to, node => {
    if (node.isText) {
      for (const mark of node.marks) {
        if (!mark.isInSet(marks)) marks = mark.addToSet(marks);
      }
    }
    return true;
  });
  return marks;
}

// ---------------------------------------------------------------------------
// The hook
// ---------------------------------------------------------------------------

export interface UseComposeFindReplaceResult {
  searchTerm: string;
  setSearchTerm: (value: string) => void;
  replaceTerm: string;
  setReplaceTerm: (value: string) => void;
  caseSensitive: boolean;
  setCaseSensitive: (value: boolean) => void;
  /** Total matches for the current `searchTerm` (0 when empty or no matches). */
  matchCount: number;
  /** 0-based index of the current (prev/next-navigated) match, or -1 when there are none. */
  currentMatchIndex: number;
  /** Navigate to the next match (wraps). No-op when there are no matches. */
  findNext: () => void;
  /** Navigate to the previous match (wraps). No-op when there are no matches. */
  findPrevious: () => void;
  /** Replace the current match with `replaceTerm`, preserving its marks. No-op when there is none. */
  replaceCurrent: () => void;
  /** Replace every match with `replaceTerm` in one transaction, preserving each match's marks. Returns the count replaced. */
  replaceAll: () => number;
  /** Clear the search (highlights + state) without touching the document. */
  clear: () => void;
}

export function useComposeFindReplace(editor: Editor | null): UseComposeFindReplaceResult {
  const [searchTerm, setSearchTerm] = React.useState('');
  const [replaceTerm, setReplaceTerm] = React.useState('');
  const [caseSensitive, setCaseSensitive] = React.useState(false);
  const [matches, setMatches] = React.useState<FindReplaceSpan[]>([]);
  const [currentMatchIndex, setCurrentMatchIndex] = React.useState(-1);

  const pushDecorations = React.useCallback(
    (ranges: readonly FindReplaceSpan[], currentIndex: number): void => {
      if (!editor || editor.isDestroyed) return;
      const meta: FindReplaceHighlightMeta =
        ranges.length > 0 ? { type: 'set', ranges, currentIndex } : { type: 'clear' };
      editor.view.dispatch(editor.state.tr.setMeta(composeFindReplacePluginKey, meta));
    },
    [editor]
  );

  /** Recompute matches for the current `searchTerm`/`caseSensitive` against the LIVE doc. */
  const recompute = React.useCallback((): void => {
    if (!editor || editor.isDestroyed) return;
    const found = findAllMatches(editor, searchTerm, caseSensitive);
    setMatches(found);
    setCurrentMatchIndex(found.length > 0 ? 0 : -1);
    pushDecorations(found, 0);
  }, [editor, searchTerm, caseSensitive, pushDecorations]);

  // Recompute whenever the term/case toggle changes.
  React.useEffect(() => {
    recompute();
  }, [recompute]);

  // Recompute whenever the DOCUMENT changes (an edit elsewhere, or our own replace/replace-all) so
  // highlighted positions never go stale. `editor.on('update')` fires only on doc-changing
  // transactions — our decoration-only `pushDecorations` dispatches do NOT change the doc, so this
  // does not loop back on itself.
  React.useEffect(() => {
    if (!editor) return;
    const handler = (): void => recompute();
    editor.on('update', handler);
    return () => {
      editor.off('update', handler);
    };
  }, [editor, recompute]);

  // Clear the decoration on unmount so a closed panel never leaves a stale highlight behind.
  React.useEffect(() => {
    return () => {
      if (editor && !editor.isDestroyed) {
        editor.view.dispatch(editor.state.tr.setMeta(composeFindReplacePluginKey, { type: 'clear' }));
      }
    };
  }, [editor]);

  const findNext = React.useCallback((): void => {
    if (matches.length === 0) return;
    const next = (currentMatchIndex + 1) % matches.length;
    setCurrentMatchIndex(next);
    pushDecorations(matches, next);
    if (editor && !editor.isDestroyed) {
      editor.chain().setTextSelection(matches[next]).scrollIntoView().run();
    }
  }, [editor, matches, currentMatchIndex, pushDecorations]);

  const findPrevious = React.useCallback((): void => {
    if (matches.length === 0) return;
    const prev = (currentMatchIndex - 1 + matches.length) % matches.length;
    setCurrentMatchIndex(prev);
    pushDecorations(matches, prev);
    if (editor && !editor.isDestroyed) {
      editor.chain().setTextSelection(matches[prev]).scrollIntoView().run();
    }
  }, [editor, matches, currentMatchIndex, pushDecorations]);

  const replaceCurrent = React.useCallback((): void => {
    if (!editor || editor.isDestroyed) return;
    if (currentMatchIndex < 0 || currentMatchIndex >= matches.length) return;
    const span = matches[currentMatchIndex];
    const marks = marksForReplacement(editor, span.from, span.to);
    const { schema } = editor.state;
    const tr = editor.state.tr;
    if (replaceTerm.length > 0) {
      tr.replaceWith(span.from, span.to, schema.text(replaceTerm, marks as Mark[]));
    } else {
      tr.delete(span.from, span.to);
    }
    editor.view.dispatch(tr);
    // `editor.on('update')` (registered above) re-runs `recompute()` against the post-replace doc.
  }, [editor, matches, currentMatchIndex, replaceTerm]);

  const replaceAll = React.useCallback((): number => {
    if (!editor || editor.isDestroyed || matches.length === 0) return 0;
    const { state } = editor;
    const { schema } = state;

    // Compute marks for every span against the ORIGINAL (pre-edit) doc, then apply the edits
    // high→low through `tr.mapping` (same idiom as `usePendingRedline`'s `stripRedlineMarks`) so
    // each span's ORIGINAL position is valid at the moment it's read, and mapping keeps every
    // not-yet-applied (lower) span's position correct as later (higher) spans are edited first.
    const withMarks = matches.map(span => ({
      span,
      marks: marksForReplacement(editor, span.from, span.to),
    }));
    const ordered = [...withMarks].sort((a, b) => b.span.from - a.span.from);

    let tr = state.tr;
    for (const { span, marks } of ordered) {
      const from = tr.mapping.map(span.from);
      const to = tr.mapping.map(span.to);
      if (replaceTerm.length > 0) {
        tr = tr.replaceWith(from, to, schema.text(replaceTerm, marks as Mark[]));
      } else {
        tr = tr.delete(from, to);
      }
    }
    editor.view.dispatch(tr);
    return matches.length;
  }, [editor, matches, replaceTerm]);

  const clear = React.useCallback((): void => {
    setSearchTerm('');
    setReplaceTerm('');
    setMatches([]);
    setCurrentMatchIndex(-1);
    pushDecorations([], -1);
  }, [pushDecorations]);

  return React.useMemo(
    () => ({
      searchTerm,
      setSearchTerm,
      replaceTerm,
      setReplaceTerm,
      caseSensitive,
      setCaseSensitive,
      matchCount: matches.length,
      currentMatchIndex,
      findNext,
      findPrevious,
      replaceCurrent,
      replaceAll,
      clear,
    }),
    [
      searchTerm,
      replaceTerm,
      caseSensitive,
      matches.length,
      currentMatchIndex,
      findNext,
      findPrevious,
      replaceCurrent,
      replaceAll,
      clear,
    ]
  );
}

export default useComposeFindReplace;
