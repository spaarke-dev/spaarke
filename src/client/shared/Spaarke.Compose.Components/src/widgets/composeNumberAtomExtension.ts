/**
 * composeNumberAtomExtension.ts — FR-13/FR-14 (spaarkeai-compose-fidelity-r4.5 task 032, WS-3 render).
 *
 * Renders the 031-computed numbering label (`ComposeDocxProjectionBuilder.AppendNumberingAttrs` —
 * `data-computed-number` / `data-numbering-level` on the projected `<p>`/`<h#>` tag) as an EXPLICIT,
 * NON-EDITABLE number-atom prefix — the owner-locked decision (spec FR-13, design §4 WS-3 "Rendering",
 * project CLAUDE.md "Locked decisions"). The editor NEVER relies on the browser `<ol>` CSS auto-count
 * for a legal number: `ComposeEditor.tsx`'s `useStyles().editorSurface` suppresses the native `<ol>`
 * marker (`list-style: none`) unconditionally, and THIS module's decoration is the sole source of the
 * displayed number.
 *
 * TWO PIECES, mirroring the two established Compose client patterns:
 *
 * 1. **Hidden/re-emitted node attributes** (`composeIndentExtension.ts`'s pattern) — `addGlobalAttributes`
 *    on `paragraph`+`heading` so `data-computed-number`/`data-numbering-level` survive the
 *    `setContent(projection.html)` parse (TipTap's base Paragraph/Heading nodes have no attribute
 *    extraction of their own — an un-registered `data-*` is silently stripped on mount) and round-trip
 *    back out through `editor.getHTML()`.
 *
 * 2. **A ProseMirror VIEW DECORATION** (`TrackChangesExtension.ts` / `QaHighlightExtension.ts`'s pattern),
 *    NOT a doc node/atom — this is the load-bearing design choice for FR-14 ("read-time only… does not
 *    participate in the tracked-edit stream"). A decoration is NOT part of the ProseMirror document: it
 *    cannot be selected into, cannot be typed into, cannot shift the paragraph's own text offsets (the
 *    offset-addressing table the step interceptor/annotation-reanchor system relies on — task 011 —
 *    indexes RUN text only; an inline atom NODE inserted as literal paragraph content would have desynced
 *    that table). A widget decoration renders purely at the view layer: it never appears in
 *    `editor.getJSON()`, is structurally impossible to place a cursor inside, and vanishes/rebuilds on
 *    every doc change with zero doc-mutation risk. If a future change needs the number to participate in
 *    editing (live renumber-on-insert/delete, reflected in redline) — that is R5 G3, explicitly OUT of
 *    R4.5 scope; escalate rather than converting this to a doc node.
 *
 * NFR-03 (licensing): built entirely on `@tiptap/core`'s Extension + `@tiptap/pm`'s Plugin/Decoration
 * (MIT). No `@tiptap-pro/*`, no AGPL — same technique `TrackChangesExtension.ts` already uses.
 *
 * ADR-021 (dark mode): this module emits ONE class (`compose-number-atom`) — never inline style/hex.
 * Token-based rules live in `ComposeEditor.tsx`'s `useStyles()`.
 *
 * @see ./composeIndentExtension.ts — the attribute-preservation pattern mirrored here
 * @see ./marks/TrackChangesExtension.ts — the widget-decoration pattern mirrored here
 * @see ../../../../server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs —
 *      `AppendNumberingAttrs` (the FR-13 server emit this reads)
 * @see projects/spaarkeai-compose-fidelity-r4.5/spec.md FR-13, FR-14
 * @see projects/spaarkeai-compose-fidelity-r4.5/notes/task-031-numbering-computation-engine-notes.md
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Node as PMNode } from '@tiptap/pm/model';

/** Same two block types every sibling additive attribute extension targets (paraId, indent, styles). */
const NUMBER_ATOM_NODE_TYPES = ['paragraph', 'heading'] as const;

/**
 * UAT round 2 (r8, 2026-09-02) — the LIST-level types carrying `data-projected-list`, the discriminator
 * `ComposeDocxProjectionBuilder.EnsureList` stamps on a list that came from the SOURCE DOCUMENT.
 *
 * Why it exists: `ComposeEditor`'s `useStyles().editorSurface` used to suppress the native `<ol>` marker
 * UNCONDITIONALLY, so a list the USER created — which has no server-computed label — rendered with no
 * number at all. The unconditional form was not arbitrary: an unresolvable `numId` must stay bare rather
 * than take a browser count that would silently disagree with the real legal number (F-3). Both cases
 * "have no computed number", so they cannot be told apart by the number attribute — only by PROVENANCE.
 * Registering it here keeps it alive across the `setContent(projection.html)` parse and `getHTML()`
 * round trip, exactly as `computedNumber` is kept for blocks.
 */
const PROJECTED_LIST_NODE_TYPES = ['orderedList', 'bulletList'] as const;

/** CSS class applied to the rendered number-atom widget (styled via ADR-021 semantic tokens in ComposeEditor's useStyles). */
export const COMPOSE_NUMBER_ATOM_CLASS = 'compose-number-atom';

/** Stable ProseMirror plugin key (no shared mutable state — the plugin has none; exported for test introspection parity with sibling extensions). */
export const composeNumberAtomPluginKey = new PluginKey('composeNumberAtom');

/**
 * Builds the widget-decoration set for every numbered `paragraph`/`heading` node carrying a
 * `computedNumber` attribute. Pure function of `doc` — no plugin state, mirroring
 * `QaHighlightExtension`'s "no state needed" shape rather than `TrackChangesExtension`'s toggle-driven
 * one (this feature has no on/off switch; it is always on per FR-13).
 */
export function buildNumberAtomDecorations(doc: PMNode): DecorationSet {
  const decorations: Decoration[] = [];

  doc.descendants((node, pos) => {
    if (!NUMBER_ATOM_NODE_TYPES.includes(node.type.name as (typeof NUMBER_ATOM_NODE_TYPES)[number])) {
      return true;
    }
    const computedNumber = node.attrs?.computedNumber as string | null | undefined;
    if (!computedNumber) return true; // unnumbered paragraph — nothing to prefix.

    const paraId = node.attrs?.paraId as string | undefined;
    const contentStart = pos + 1; // inside the block, mirrors TrackChangesExtension's contentStart convention.

    decorations.push(
      Decoration.widget(
        contentStart,
        () => {
          const span = document.createElement('span');
          span.className = COMPOSE_NUMBER_ATOM_CLASS;
          span.setAttribute('contenteditable', 'false');
          span.setAttribute('data-compose-number-atom', 'true');
          span.textContent = computedNumber;
          return span;
        },
        // side: -1 places the widget "before" the position without offsetting a caret placed at the
        // true start of the paragraph's own text; ignoreSelection keeps it out of selection math
        // entirely (belt-and-suspenders — it is already outside the doc model).
        { side: -1, ignoreSelection: true, key: `num-${paraId ?? pos}` }
      )
    );

    // A text block has no nested numbered blocks to recurse into.
    return false;
  });

  return DecorationSet.create(doc, decorations);
}

/**
 * `composeNumberAtom` — FR-13's explicit non-editable number-atom. ADDITIVE to the LOCKED Spike #1
 * extension list (registered as its own array in `ComposeEditor.tsx`, never mutating it — same
 * convention as `COMPOSE_INDENT`/`COMPOSE_R3_PARAID`/`COMPOSE_R4_OPAQUE_ATOMS`).
 */
export const ComposeNumberAtomExtension = Extension.create({
  name: 'composeNumberAtom',

  addGlobalAttributes() {
    return [
      {
        types: [...NUMBER_ATOM_NODE_TYPES],
        attributes: {
          computedNumber: {
            default: null,
            parseHTML: (element: HTMLElement) => element.getAttribute('data-computed-number') || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.computedNumber as string | null;
              return value ? { 'data-computed-number': value } : {};
            },
          },
          numberingLevel: {
            default: null,
            parseHTML: (element: HTMLElement) => element.getAttribute('data-numbering-level') || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.numberingLevel as string | null;
              return value !== null && value !== undefined ? { 'data-numbering-level': value } : {};
            },
          },
        },
      },
      {
        types: [...PROJECTED_LIST_NODE_TYPES],
        attributes: {
          projectedList: {
            default: false,
            parseHTML: (element: HTMLElement) => element.hasAttribute('data-projected-list'),
            // Re-emitted so the CSS selector keeps matching after any getHTML()/setContent() cycle.
            renderHTML: (attributes: Record<string, unknown>) =>
              attributes.projectedList ? { 'data-projected-list': '1' } : {},
          },
        },
      },
    ];
  },

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: composeNumberAtomPluginKey,
        props: {
          decorations(state) {
            return buildNumberAtomDecorations(state.doc);
          },
        },
      }),
    ];
  },
});

/** Mount additively in `ComposeEditor.tsx`'s `useEditor` extensions list, alongside `COMPOSE_INDENT`. */
export const COMPOSE_NUMBER_ATOM = [ComposeNumberAtomExtension];

export default ComposeNumberAtomExtension;
