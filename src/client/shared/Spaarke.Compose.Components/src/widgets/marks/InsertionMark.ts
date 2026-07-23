/**
 * InsertionMark — pending-insertion (track-change "added") mark for the Compose editor
 * (spaarkeai-compose-r2 task 031, FR-15).
 *
 * One of three ADDITIVE R2 schema marks (with {@link DeletionMark} + {@link CommentAnchorMark})
 * that render pending AI track-changes as redline. This mark is the "added text" half of an
 * insertion/deletion redline pair: text carrying it renders in the added style (underline +
 * added color) defined by ComposeEditor's token-based styles.
 *
 * SCOPE (binding — task 031 constraints):
 *  - RENDERING PRIMITIVE ONLY. This mark carries provenance attributes and renders; it does NOT
 *    dispatch, materialize from the ledger, or maintain a client-side pending-edit buffer.
 *    Materializing pending redlines FROM the session ledger is FR-16 / task 033 (core-gated),
 *    which CONSUMES these marks. The apply/accept/reject signal flows via PaneEventBus in FR-16,
 *    never from here (ADR-030: marks carry provenance only).
 *  - ADDITIVE to the LOCKED Spike #1 extension list (design §5) — registered alongside it in
 *    ComposeEditor.tsx WITHOUT modifying that locked list.
 *  - STYLING lives in ComposeEditor's `makeStyles` via the `compose-mark-insertion` class using
 *    semantic `tokens.*` (ADR-021: no hardcoded hex; dark-mode-correct). This mark emits the
 *    class only — never inline colors — so a single Griffel-scoped rule styles it in both themes.
 *
 * Provenance: `data-binding` + `data-ledger-ref` carry the `{bindingId}@t{n}` addressing FR-16
 * materializes and FR-17 supersedes. Boundary: `inclusive: true` — typing at the trailing edge
 * extends the insertion (continued drafting stays within the added span).
 *
 * @see ./DeletionMark.ts · ./CommentAnchorMark.ts
 * @see projects/spaarkeai-compose-r2/design.md §5 (Custom marks)
 * @see ../ComposeEditor.tsx ("LOCKED Spike #1 extension list" — marks are additive to it)
 */
import { Mark, mergeAttributes } from '@tiptap/core';

export interface InsertionMarkOptions {
  /** Extra HTML attributes merged onto the rendered `<span>` element. */
  HTMLAttributes: Record<string, unknown>;
}

/** Provenance attributes carried by a pending-insertion mark (all optional; ids only). */
export interface InsertionMarkAttributes {
  /** `sprk_playbookconsumer` Binding id that produced the suggestion (`data-binding`). */
  binding?: string;
  /** Addressable ledger key `{bindingId}@t{n}` of the stored compose output (`data-ledger-ref`). */
  ledgerRef?: string;
  /**
   * FR-24 (spaarkeai-compose-r3 task 050, import round-trip) — Word revision AUTHOR (`data-author`), set
   * ONLY on an IMPORTED existing `w:ins` recovered from the load-time `.docx` (the reader carries author/
   * date fidelity). Absent on AI-suggested redlines (task 031/033), which carry provenance via
   * `binding`/`ledgerRef` instead. Rendered as a hover `title` for visible attribution.
   */
  author?: string;
  /** FR-24 (task 050) — the imported revision's ISO-8601 UTC authoring date (`data-date`). See {@link author}. */
  date?: string;
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    insertion: {
      /** Apply the pending-insertion mark to the current selection. */
      setInsertion: (attributes?: InsertionMarkAttributes) => ReturnType;
      /** Toggle the pending-insertion mark on the current selection. */
      toggleInsertion: (attributes?: InsertionMarkAttributes) => ReturnType;
      /** Remove the pending-insertion mark from the current selection. */
      unsetInsertion: () => ReturnType;
    };
  }
}

export const InsertionMark = Mark.create<InsertionMarkOptions>({
  name: 'insertion',

  // Continued typing at the boundary stays inside the added span (track-change semantics).
  inclusive: true,

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
      // FR-24 (task 050) — imported-revision author/date attribution (data-author / data-date). Emitted
      // only when present, so AI-suggested redlines (no author/date) serialize exactly as before.
      author: {
        default: null,
        parseHTML: element => element.getAttribute('data-author'),
        renderHTML: attributes => (attributes.author ? { 'data-author': attributes.author as string } : {}),
      },
      date: {
        default: null,
        parseHTML: element => element.getAttribute('data-date'),
        renderHTML: attributes => (attributes.date ? { 'data-date': attributes.date as string } : {}),
      },
    };
  },

  parseHTML() {
    // A `<span>` tagged with our data attribute — NOT a bare tag. Rendering as `<span>` (with the
    // added-style look from the `compose-mark-insertion` token class) avoids the `<ins>`/`<del>`/`<s>`
    // schema collision with StarterKit's Strike/Underline marks (which claim those semantic tags).
    return [{ tag: 'span[data-compose-mark="insertion"]' }];
  },

  renderHTML({ HTMLAttributes }) {
    // FR-24 (task 050): when an imported revision carries author/date, expose a hover `title` for visible
    // attribution ("Inserted by {author} on {date}"). Token-class styling only — never inline color/hex
    // (ADR-021); a title is neither, so the marks' no-`style=`/no-hex invariant is preserved.
    const author = HTMLAttributes['data-author'] as string | undefined;
    const date = HTMLAttributes['data-date'] as string | undefined;
    const title = author ? `Inserted by ${author}${date ? ` on ${date}` : ''}` : undefined;
    return [
      'span',
      mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
        'data-compose-mark': 'insertion',
        class: 'compose-mark-insertion',
        ...(title ? { title } : {}),
      }),
      0,
    ];
  },

  addCommands() {
    return {
      setInsertion:
        attributes =>
        ({ commands }) =>
          commands.setMark(this.name, attributes),
      toggleInsertion:
        attributes =>
        ({ commands }) =>
          commands.toggleMark(this.name, attributes),
      unsetInsertion:
        () =>
        ({ commands }) =>
          commands.unsetMark(this.name),
    };
  },
});

export default InsertionMark;
