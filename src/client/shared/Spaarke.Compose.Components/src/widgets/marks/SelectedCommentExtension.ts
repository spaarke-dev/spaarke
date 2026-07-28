/**
 * SelectedCommentExtension — TRANSIENT "this advisory comment is selected" highlight decoration
 * (ai-advanced-capabilities-nda-r1, UAT round-4 #8).
 *
 * Bidirectional linked highlighting between the in-document comment anchor and its right-rail gutter
 * card: exactly ONE advisory thread can be "selected" at a time, and its anchored clause is painted
 * the SELECTED colour (yellow) while every other anchor keeps the BASE colour (light blue, applied by
 * the `compose-mark-comment-anchor` mark class in ComposeEditor's `useStyles`). Selecting is driven
 * imperatively by ComposeEditor — a click on a gutter card OR on the highlighted clause sets the
 * selected thread id.
 *
 * Like {@link ../marks/QaHighlightExtension} (and for the SAME load-bearing reason), this is a
 * ProseMirror VIEW DECORATION, NOT a TipTap Mark: selection is transient UI state that must NEVER
 * appear in `editor.getHTML()`/`getJSON()` and must NEVER serialize into the saved DOCX. Clearing or
 * changing the selection replaces the plugin's decoration state — no document mutation, no undo entry.
 *
 * LIVE POSITION (ADR-049): the decorated range is resolved from the selected thread's CURRENT
 * `commentAnchor` mark span via {@link findCommentAnchorRange} on every render — the SAME live-position
 * primitive the gutter + save-export paths use — never a stored, creation-time offset. A selected
 * thread whose anchor was since deleted simply renders no decoration (never a guessed position).
 *
 * @see ./QaHighlightExtension.ts — the transient-view-decoration precedent this mirrors
 * @see ./CommentAnchorMark.ts — the base anchor mark (light-blue base colour)
 * @see ../ComposeCommentThread.types.ts — `findCommentAnchorRange` (live span, reused not duplicated)
 * @see ../ComposeEditor.tsx — ComposeEditorHandle wiring: `selectedThreadId` state → this plugin
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import { findCommentAnchorRange } from '../ComposeCommentThread.types';

/** Stable ProseMirror plugin key — ComposeEditor dispatches select/clear transactions keyed to this. */
export const selectedCommentPluginKey = new PluginKey('composeSelectedCommentAnchor');

/**
 * CSS class applied to the SELECTED advisory comment's anchor span (styled via ADR-021 semantic tokens
 * in ComposeEditor's useStyles as yellow — the base anchor stays light blue).
 */
export const SELECTED_COMMENT_ANCHOR_CLASS = 'compose-mark-comment-anchor-selected';

type SelectedCommentMeta = { type: 'select'; commentId: string } | { type: 'clear' };

/**
 * The TipTap extension. Registers ONE ProseMirror plugin whose state is the selected thread id (or
 * null). ComposeEditor sets/clears it via `tr.setMeta(selectedCommentPluginKey, meta)`; the
 * decoration is derived LIVE from that id on each render, so it tracks the anchor through edits with
 * no remap bookkeeping.
 */
export const SelectedCommentExtension = Extension.create({
  name: 'composeSelectedCommentAnchor',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: selectedCommentPluginKey,
        state: {
          init: (): string | null => null,
          apply(tr, old: string | null): string | null {
            const meta = tr.getMeta(selectedCommentPluginKey) as SelectedCommentMeta | undefined;
            if (meta?.type === 'select') return meta.commentId;
            if (meta?.type === 'clear') return null;
            return old;
          },
        },
        props: {
          decorations(state): DecorationSet | null {
            const commentId = selectedCommentPluginKey.getState(state) as string | null;
            if (!commentId) return null;
            const range = findCommentAnchorRange(state.doc, commentId);
            if (!range) return null; // anchor deleted by a later edit — decorate nothing (never guess)
            return DecorationSet.create(state.doc, [
              Decoration.inline(range.from, range.to, { class: SELECTED_COMMENT_ANCHOR_CLASS }),
            ]);
          },
        },
      }),
    ];
  },
});

export default SelectedCommentExtension;
