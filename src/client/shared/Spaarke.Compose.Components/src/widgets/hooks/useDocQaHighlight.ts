/**
 * useDocQaHighlight — FR-35 Document Q&A ephemeral highlight (spaarkeai-compose-r2
 * task 072, stretch).
 *
 * Drives the {@link QaHighlightExtension} ProseMirror plugin imperatively:
 * resolves a cited excerpt (`qaSourceText`) against the CURRENT document text
 * using the EXACT same strict-match, do-not-guess semantics FR-16's
 * `resolveTargetSpans` already implements (task 033 — reused verbatim, not
 * reimplemented), then sets/clears a TRANSIENT view decoration (never a doc
 * Mark — see QaHighlightExtension's file header for why that distinction is
 * load-bearing for DOCX save-path safety).
 *
 * "Do not guess" (FR-19 sister rule): a citation excerpt that matches ZERO or
 * MORE THAN ONE span in the document does not highlight anything — silently
 * for `not_found` (the citation may legitimately belong to a different
 * knowledge source than the open document; this hook has no opinion on that),
 * and via the returned status for `ambiguous` (caller may choose to surface
 * it, though the Doc Q&A UX does not currently render a distinct banner for
 * this case — matching the "no highlight" outcome).
 *
 * Auto-clears after {@link HIGHLIGHT_TTL_MS} so the ephemeral affordance does
 * not linger indefinitely (CLAUDE.md: "the ephemeral highlight is TRANSIENT
 * UI state").
 *
 * @see ../marks/QaHighlightExtension.ts — the ProseMirror decoration plugin
 * @see ./usePendingRedline.ts — `resolveTargetSpans` (reused, not duplicated)
 * @see ../ComposeEditor.tsx — ComposeEditorHandle.highlightCitedSpan / clearCitedHighlight
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import { resolveTargetSpans } from './usePendingRedline';
import { qaHighlightPluginKey } from '../marks/QaHighlightExtension';

/** Outcome of a {@link UseDocQaHighlightResult.highlight} call. */
export type QaHighlightStatus = 'highlighted' | 'not_found' | 'ambiguous' | 'noop';

/** The currently-active ephemeral highlight, or null when none is showing. */
export interface ActiveQaHighlight {
  /** Display label shown in the "Found in …" affordance (Tier-1 — see PaneEventTypes.ts). */
  sectionLabel?: string;
}

export interface UseDocQaHighlightResult {
  /** The active highlight (drives the "Found in …" banner), or null. */
  activeHighlight: ActiveQaHighlight | null;
  /**
   * Resolve `sourceText` against the current document and, on a unique match,
   * render the ephemeral highlight + scroll it into view. Returns the outcome
   * so callers can distinguish a genuine miss (different source) from a
   * successful highlight.
   */
  highlight: (sourceText: string, sectionLabel?: string) => QaHighlightStatus;
  /** Clear the active highlight immediately (no-op if none is active). */
  clear: () => void;
}

/** How long an ephemeral highlight stays visible before auto-clearing. */
const HIGHLIGHT_TTL_MS = 8000;

export function useDocQaHighlight(editor: Editor | null): UseDocQaHighlightResult {
  const [activeHighlight, setActiveHighlight] = React.useState<ActiveQaHighlight | null>(null);
  const timerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearTimer = React.useCallback((): void => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const clear = React.useCallback((): void => {
    clearTimer();
    setActiveHighlight(null);
    if (!editor) return;
    editor.view.dispatch(editor.state.tr.setMeta(qaHighlightPluginKey, { type: 'clear' }));
  }, [editor, clearTimer]);

  const highlight = React.useCallback(
    (sourceText: string, sectionLabel?: string): QaHighlightStatus => {
      if (!editor || !sourceText) return 'noop';

      // Strict match: 0 → not_found (different source, silently ignored by
      // design), >1 → ambiguous (do not guess which occurrence was cited).
      const resolved = resolveTargetSpans(editor, sourceText, 'strict');
      if (!resolved.ok) return resolved.kind;

      const span = resolved.spans[0];
      editor.view.dispatch(
        editor.state.tr.setMeta(qaHighlightPluginKey, { type: 'set', from: span.from, to: span.to })
      );
      editor.chain().setTextSelection(span).scrollIntoView().run();

      setActiveHighlight({ sectionLabel });
      clearTimer();
      timerRef.current = setTimeout(clear, HIGHLIGHT_TTL_MS);

      return 'highlighted';
    },
    [editor, clear, clearTimer]
  );

  // Auto-clear the timer on unmount so it never fires against a destroyed editor.
  React.useEffect(() => clearTimer, [clearTimer]);

  return { activeHighlight, highlight, clear };
}

export default useDocQaHighlight;
