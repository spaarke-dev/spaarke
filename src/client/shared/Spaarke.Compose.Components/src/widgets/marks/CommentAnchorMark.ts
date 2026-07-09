/**
 * CommentAnchorMark — comment-anchor mark for the Compose editor (spaarkeai-compose-r2 task 031,
 * FR-15).
 *
 * Anchors a comment to a span of document text: text carrying it renders as a highlighted anchor
 * (the styled `compose-mark-comment-anchor` span) that a comment thread attaches to. Distinct
 * from the insertion/deletion redline pair — a comment does not change document content, it
 * annotates a region.
 *
 * SCOPE (binding — same as the redline marks): rendering primitive only; carries provenance +
 * a `data-comment-id`, does NOT dispatch/materialize/buffer (comment threading + push-to-Word
 * `<w:comment>` are later, core-gated work). Additive to the LOCKED Spike #1 extension list.
 * Styling via the `compose-mark-comment-anchor` class using semantic `tokens.*` in
 * ComposeEditor's `makeStyles` (ADR-021: no hex; dark-mode-correct) — emits the class only.
 *
 * Provenance: `data-comment-id` (the thread id the anchor belongs to) + `data-binding` +
 * `data-ledger-ref` (`{bindingId}@t{n}`). Boundary: `inclusive: false` — the anchor covers a
 * fixed region; typing at its edge must not extend the anchored span.
 *
 * @see ./InsertionMark.ts · ./DeletionMark.ts
 * @see projects/spaarkeai-compose-r2/design.md §5 (Custom marks)
 */
import { Mark, mergeAttributes } from '@tiptap/core';

export interface CommentAnchorMarkOptions {
  /** Extra HTML attributes merged onto the rendered `<span>` element. */
  HTMLAttributes: Record<string, unknown>;
}

/** Attributes carried by a comment-anchor mark (ids only). */
export interface CommentAnchorMarkAttributes {
  /** Comment thread id the anchored span belongs to (`data-comment-id`). */
  commentId?: string;
  /** `sprk_playbookconsumer` Binding id, when the comment was AI-produced (`data-binding`). */
  binding?: string;
  /** Addressable ledger key `{bindingId}@t{n}`, when applicable (`data-ledger-ref`). */
  ledgerRef?: string;
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    commentAnchor: {
      /** Apply the comment-anchor mark to the current selection. */
      setCommentAnchor: (attributes?: CommentAnchorMarkAttributes) => ReturnType;
      /** Toggle the comment-anchor mark on the current selection. */
      toggleCommentAnchor: (attributes?: CommentAnchorMarkAttributes) => ReturnType;
      /** Remove the comment-anchor mark from the current selection. */
      unsetCommentAnchor: () => ReturnType;
    };
  }
}

export const CommentAnchorMark = Mark.create<CommentAnchorMarkOptions>({
  name: 'commentAnchor',

  // The anchor covers a fixed region — do not extend it into newly-typed text at the edge.
  inclusive: false,

  // Multiple overlapping comment anchors on the same span are allowed (distinct thread ids).
  excludes: '',

  addOptions() {
    return { HTMLAttributes: {} };
  },

  addAttributes() {
    return {
      commentId: {
        default: null,
        parseHTML: element => element.getAttribute('data-comment-id'),
        renderHTML: attributes => (attributes.commentId ? { 'data-comment-id': attributes.commentId as string } : {}),
      },
      binding: {
        default: null,
        parseHTML: element => element.getAttribute('data-binding'),
        renderHTML: attributes => (attributes.binding ? { 'data-binding': attributes.binding as string } : {}),
      },
      ledgerRef: {
        default: null,
        parseHTML: element => element.getAttribute('data-ledger-ref'),
        renderHTML: attributes => (attributes.ledgerRef ? { 'data-ledger-ref': attributes.ledgerRef as string } : {}),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-comment-id]' }, { tag: 'span[data-compose-mark="comment-anchor"]' }];
  },

  renderHTML({ HTMLAttributes }) {
    return [
      'span',
      mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
        'data-compose-mark': 'comment-anchor',
        class: 'compose-mark-comment-anchor',
      }),
      0,
    ];
  },

  addCommands() {
    return {
      setCommentAnchor:
        attributes =>
        ({ commands }) =>
          commands.setMark(this.name, attributes),
      toggleCommentAnchor:
        attributes =>
        ({ commands }) =>
          commands.toggleMark(this.name, attributes),
      unsetCommentAnchor:
        () =>
        ({ commands }) =>
          commands.unsetMark(this.name),
    };
  },
});

export default CommentAnchorMark;
