/**
 * commentScrollSync.ts — G9 (FR-08, task 030/031) Comments-pane ⇄ document scroll-sync helpers.
 *
 * Position-links the Comments pane to in-document anchor positions instead of a flat list:
 *   • doc → pane: as the reader scrolls the document, {@link pickActiveThreadId} names the thread whose
 *     anchor is nearest at/above the viewport top so the pane can highlight + scroll that card into view.
 *   • pane → doc: {@link scrollEditorToThreadAnchor} scrolls the editor to a selected thread's anchor.
 *
 * Client-only (ADR-049 — the client is view+controller; nothing here authors bytes or touches the
 * OOXML model / persistence). Resolves anchors by the LIVE `commentAnchor` mark position via the
 * existing {@link findCommentAnchorRange} primitive (reused, not reimplemented) — never text-search,
 * never a guessed span (a thread whose anchored text was deleted simply drops out of tracking).
 *
 * The pure position-picking core ({@link pickActiveThreadId}) takes a plain position list so it is
 * unit-testable without a live editor; the doc-reading + editor-scrolling helpers are thin wrappers
 * over ProseMirror state.
 */

import type { Editor } from '@tiptap/react';
import type { ComposeCommentThreadModel } from './ComposeCommentThread.types';
// Import the pure primitive from the LEAF module (not ComposeCommentThread.types) so this helper does
// not transitively load the persistence-vocabulary chain (useComposeWordShuttle → @spaarke/auth).
import { findCommentAnchorRange } from './commentAnchorRange';

/** A thread's live in-document anchor start position (ProseMirror doc position). */
export interface ThreadAnchorPosition {
  threadId: string;
  /** ProseMirror position of the anchor mark's start. */
  pos: number;
}

/**
 * Resolve each thread's LIVE anchor start position (via {@link findCommentAnchorRange}), sorted
 * ascending by document position — so the pane can order/track threads by where they sit in the
 * document, not by creation order. Threads whose anchor mark is no longer present (the anchored text
 * was edited away) are OMITTED — never guessed a replacement (I-7 discipline). Pure read of doc state.
 */
export function resolveThreadAnchorPositions(
  editor: Editor | null,
  threads: readonly ComposeCommentThreadModel[]
): ThreadAnchorPosition[] {
  if (!editor) return [];
  const doc = editor.state.doc;
  const out: ThreadAnchorPosition[] = [];
  for (const thread of threads) {
    const range = findCommentAnchorRange(doc, thread.id);
    if (range) {
      out.push({ threadId: thread.id, pos: range.from });
    }
  }
  out.sort((a, b) => a.pos - b.pos);
  return out;
}

/**
 * The thread whose anchor is nearest at/above the viewport-top document position — the comment the
 * reader is currently looking at. Returns the greatest `pos <= viewportTopPos`; when nothing is above
 * (the reader is at the very top, before the first anchor) the topmost thread is active; `null` when
 * there are no anchored threads. `positions` MUST be sorted ascending (as {@link resolveThreadAnchorPositions}
 * returns). Pure — the testable core of doc→pane tracking.
 */
export function pickActiveThreadId(positions: readonly ThreadAnchorPosition[], viewportTopPos: number): string | null {
  if (positions.length === 0) return null;
  let active: ThreadAnchorPosition | null = null;
  for (const p of positions) {
    if (p.pos <= viewportTopPos) {
      active = p;
    } else {
      break; // ascending — no later position can be at/above the viewport top
    }
  }
  return (active ?? positions[0]).threadId;
}

/**
 * pane → doc: scroll the editor to a thread's anchor by selecting its live `commentAnchor` span and
 * calling TipTap's `scrollIntoView`. No-op (returns false) when the editor is absent or the anchor mark
 * is gone (the anchored text was deleted) — never scrolls to a guessed location. Returns whether it
 * scrolled.
 */
export function scrollEditorToThreadAnchor(editor: Editor | null, threadId: string): boolean {
  if (!editor) return false;
  const range = findCommentAnchorRange(editor.state.doc, threadId);
  if (!range) return false;
  editor.chain().setTextSelection({ from: range.from, to: range.to }).scrollIntoView().run();
  return true;
}

/**
 * doc → pane: resolve the viewport-top document position from the editor's scroll container, mapping
 * the container's top-left screen point to a ProseMirror position via `posAtCoords`. Returns `null`
 * when the editor/container is absent or the point maps to no position (e.g. jsdom, which has no
 * layout) — the caller then leaves the active thread unchanged.
 */
export function resolveViewportTopPos(editor: Editor | null, container: HTMLElement | null): number | null {
  if (!editor || !container) return null;
  const rect = container.getBoundingClientRect();
  const hit = editor.view.posAtCoords({ left: rect.left + 8, top: rect.top + 8 });
  return hit ? hit.pos : null;
}
