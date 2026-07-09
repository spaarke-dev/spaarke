/**
 * DeletionMark — pending-deletion (track-change "removed") mark for the Compose editor
 * (spaarkeai-compose-r2 task 031, FR-15).
 *
 * The "removed text" half of an insertion/deletion redline pair (see {@link InsertionMark}):
 * text carrying it renders struck-through in the removed color defined by ComposeEditor's
 * token-based styles. Marks existing document text a suggestion proposes to remove — the text
 * stays in the document until the user accepts (FR-16/17 consume this mark), so a deletion
 * renders as strikethrough rather than actually removing content.
 *
 * SCOPE (binding — identical to {@link InsertionMark}): rendering primitive only; carries
 * provenance, does NOT dispatch/materialize/buffer (FR-16/task 033 core-gated). Additive to the
 * LOCKED Spike #1 extension list. Styling via the `compose-mark-deletion` class using semantic
 * `tokens.*` in ComposeEditor's `makeStyles` (ADR-021: no hex; dark-mode-correct) — emits the
 * class only, never inline colors.
 *
 * Provenance: `data-binding` + `data-ledger-ref` (`{bindingId}@t{n}`). Boundary:
 * `inclusive: false` — a deletion marks a fixed existing span; typing at its edge must NOT
 * extend the strikethrough into newly-typed text.
 *
 * @see ./InsertionMark.ts · ./CommentAnchorMark.ts
 * @see projects/spaarkeai-compose-r2/design.md §5 (Custom marks)
 */
import { Mark, mergeAttributes } from '@tiptap/core';

export interface DeletionMarkOptions {
  /** Extra HTML attributes merged onto the rendered `<span>` element. */
  HTMLAttributes: Record<string, unknown>;
}

/** Provenance attributes carried by a pending-deletion mark (all optional; ids only). */
export interface DeletionMarkAttributes {
  /** `sprk_playbookconsumer` Binding id that produced the suggestion (`data-binding`). */
  binding?: string;
  /** Addressable ledger key `{bindingId}@t{n}` of the stored compose output (`data-ledger-ref`). */
  ledgerRef?: string;
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    deletion: {
      /** Apply the pending-deletion mark to the current selection. */
      setDeletion: (attributes?: DeletionMarkAttributes) => ReturnType;
      /** Toggle the pending-deletion mark on the current selection. */
      toggleDeletion: (attributes?: DeletionMarkAttributes) => ReturnType;
      /** Remove the pending-deletion mark from the current selection. */
      unsetDeletion: () => ReturnType;
    };
  }
}

export const DeletionMark = Mark.create<DeletionMarkOptions>({
  name: 'deletion',

  // A deletion marks a fixed existing span — never extend into newly-typed text at the edge.
  inclusive: false,

  addOptions() {
    return { HTMLAttributes: {} };
  },

  addAttributes() {
    return {
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
    // A `<span>` tagged with our data attribute — deliberately NOT `<del>`/`<s>`. Those belong to
    // StarterKit's `Strike` mark (which parses `s`/`del`/line-through and would win the parse and
    // re-serialize as `<s>`, dropping this mark + its provenance). Rendering the removed-style look
    // via the `compose-mark-deletion` token class on a `<span>` keeps a plain imported strikethrough
    // a Strike, and a Compose deletion a Compose deletion.
    return [{ tag: 'span[data-compose-mark="deletion"]' }];
  },

  renderHTML({ HTMLAttributes }) {
    return [
      'span',
      mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
        'data-compose-mark': 'deletion',
        class: 'compose-mark-deletion',
      }),
      0,
    ];
  },

  addCommands() {
    return {
      setDeletion:
        attributes =>
        ({ commands }) =>
          commands.setMark(this.name, attributes),
      toggleDeletion:
        attributes =>
        ({ commands }) =>
          commands.toggleMark(this.name, attributes),
      unsetDeletion:
        () =>
        ({ commands }) =>
          commands.unsetMark(this.name),
    };
  },
});

export default DeletionMark;
