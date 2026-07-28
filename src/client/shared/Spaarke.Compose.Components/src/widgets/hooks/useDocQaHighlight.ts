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

/**
 * Walk up from the editor's contenteditable to the nearest vertically-scrollable ancestor (the
 * Compose `editorSurface`, `overflow-y: auto`). Used for the S5 manual scroll — ProseMirror's own
 * scrollIntoView is unreliable inside that custom, scrollbar-hidden container. Returns null if none is
 * found (e.g. a test/headless mount), in which case the caller falls back to the native scrollIntoView.
 */
function findScrollableAncestor(node: HTMLElement): HTMLElement | null {
  let el: HTMLElement | null = node.parentElement;
  while (el) {
    const overflowY = getComputedStyle(el).overflowY;
    if ((overflowY === 'auto' || overflowY === 'scroll') && el.scrollHeight > el.clientHeight) {
      return el;
    }
    el = el.parentElement;
  }
  return null;
}

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

      // UAT round-3 S5 — robust scroll for a target BELOW the fold. ProseMirror's own scrollIntoView
      // under-scrolls inside the Compose editorSurface (a custom-overflow container that hides its
      // scrollbar — FIX #9), so a summary link to a section outside the viewport appeared to do
      // nothing. Explicitly bring the span into view by scrolling its scrollable ancestor via
      // coordsAtPos (the same live-measurement technique the comment gutter uses), centering it in the
      // upper third so it clears the sticky toolbar + review-summary panel.
      try {
        const scroller = findScrollableAncestor(editor.view.dom);
        if (scroller) {
          const coords = editor.view.coordsAtPos(span.from);
          const rect = scroller.getBoundingClientRect();
          const target = scroller.scrollTop + (coords.top - rect.top) - rect.height / 3;
          scroller.scrollTo({ top: Math.max(0, target), behavior: 'smooth' });
        }
      } catch {
        // coordsAtPos measures real DOM layout and can throw in a detached/not-yet-painted view; the
        // setTextSelection().scrollIntoView() above already ran as a best-effort fallback.
      }

      setActiveHighlight({ sectionLabel });
      clearTimer();
      timerRef.current = setTimeout(clear, HIGHLIGHT_TTL_MS);

      return 'highlighted';
    },
    [editor, clear, clearTimer]
  );

  // Auto-clear the timer on unmount so it never fires against a destroyed editor.
  React.useEffect(() => clearTimer, [clearTimer]);

  // Memoize the result so ComposeEditor's useImperativeHandle (which keys on `qaHighlight`) gets a
  // STABLE reference — paired with usePendingRedline's memoized return, the editor handle no longer
  // rebuilds on every render, only when a member's identity actually changes.
  return React.useMemo(() => ({ activeHighlight, highlight, clear }), [activeHighlight, highlight, clear]);
}

export default useDocQaHighlight;
