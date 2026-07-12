/**
 * QaHighlightExtension — TRANSIENT ephemeral highlight decoration for the
 * Document Q&A stretch feature (spaarkeai-compose-r2 task 072, FR-35).
 *
 * Unlike the FR-15 marks in this directory (InsertionMark / DeletionMark /
 * CommentAnchorMark), this is deliberately NOT a TipTap Mark. A Mark is part
 * of the ProseMirror document — it round-trips through `getHTML()` /
 * `tipTapToDocxBytes()` and would leak into the saved DOCX unless explicitly
 * stripped before every save path. The Doc Q&A highlight is UI-only ("found
 * in §7.3" — CLAUDE.md project constraint: "the ephemeral highlight is
 * TRANSIENT UI state"), so it is implemented as a ProseMirror VIEW DECORATION
 * (`DecorationSet`), which:
 *   - never appears in `editor.getHTML()` / `editor.getJSON()`,
 *   - never serializes to DOCX,
 *   - is cleared by replacing the plugin's decoration state (no doc mutation,
 *     no undo-stack entry).
 *
 * Owned + driven imperatively by {@link useDocQaHighlight} — this extension
 * only registers the ProseMirror plugin that stores/renders the decoration;
 * it has no React state of its own.
 *
 * @see ./useDocQaHighlight.ts (../hooks) — imperative highlight/clear API
 * @see ../ComposeEditor.tsx — ComposeEditorHandle.highlightCitedSpan
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';

/** Stable ProseMirror plugin key — {@link useDocQaHighlight} dispatches transactions keyed to this. */
export const qaHighlightPluginKey = new PluginKey('composeQaEphemeralHighlight');

/** CSS class applied to the ephemeral highlight decoration (styled via ADR-021 semantic tokens in ComposeEditor's useStyles). */
export const QA_HIGHLIGHT_CLASS = 'compose-qa-highlight';

type QaHighlightMeta = { type: 'set'; from: number; to: number } | { type: 'clear' };

/**
 * The TipTap extension. Registers ONE ProseMirror plugin whose state is a
 * `DecorationSet` (empty by default). {@link useDocQaHighlight} sets/clears the
 * decoration via `tr.setMeta(qaHighlightPluginKey, meta)`.
 */
export const QaHighlightExtension = Extension.create({
  name: 'composeQaEphemeralHighlight',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: qaHighlightPluginKey,
        state: {
          init: (): DecorationSet => DecorationSet.empty,
          apply(tr, old): DecorationSet {
            const meta = tr.getMeta(qaHighlightPluginKey) as QaHighlightMeta | undefined;
            if (meta?.type === 'set') {
              const deco = Decoration.inline(meta.from, meta.to, { class: QA_HIGHLIGHT_CLASS });
              return DecorationSet.create(tr.doc, [deco]);
            }
            if (meta?.type === 'clear') {
              return DecorationSet.empty;
            }
            // No relevant meta on this transaction — map the existing decorations
            // across the document change (keeps the highlight anchored through
            // unrelated edits until explicitly cleared).
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

export default QaHighlightExtension;
