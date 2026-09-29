/**
 * composeIndentExtension.ts — FR-07 (spaarkeai-compose-fidelity-r4.5 task 021, WS-2).
 *
 * Preserves the server projection's `w:ind` emit (`ComposeDocxProjectionBuilder.AppendIndentDeclarations`
 * — `margin-left` / `text-indent`, in `pt`) through the ProseMirror parse/render round-trip.
 *
 * WHY THIS IS NEEDED (not just the CSS the BFF emits): TipTap's base `paragraph`/`heading` nodes
 * (`@tiptap/extension-paragraph`, `@tiptap/starter-kit`'s heading) have `parseHTML: () => [{ tag: 'p' }]`
 * with no attribute extraction — an arbitrary inline `style` on the source `<p>` is silently dropped when
 * `editor.commands.setContent(projection.html)` parses it, UNLESS some registered extension's
 * `addGlobalAttributes` declares an attribute with a `parseHTML` that reads it back out. This is exactly
 * the mechanism the LOCKED `@tiptap/extension-text-align` (`ComposeEditor.tsx` LOCKED_EXTENSIONS,
 * `TextAlign.configure({ types: ['heading', 'paragraph'] })`) already uses for `text-align` — this
 * extension mirrors that pattern for `margin-left`/`text-indent`. Without it, the server's FR-07 emit
 * would be present in `projection.html` as a string but silently stripped on mount, so indented legal
 * clauses would keep rendering flush-left in the actual editor — the exact defect FR-07 exists to fix
 * (design §1). The projection HTML round-trips through `editor.getHTML()` on save (F-1: no client-side
 * byte authoring, ADR-040/R4 two-author split — this extension only PRESERVES what the server computed,
 * it never computes an indent value itself).
 *
 * `mergeAttributes` (`@tiptap/core`) merges this extension's `style` output with TextAlign's own
 * `text-align` declaration (and any other extension's `style` contribution) into ONE combined `style`
 * attribute, semicolon-joined and keyed by CSS property — see `@tiptap/core`'s `mergeAttributes` (splits
 * on `;`, maps `property: value`, re-joins). No collision: `margin-left`/`text-indent` never overlap
 * `text-align`.
 *
 * @see ./ComposeEditor.tsx — LOCKED_EXTENSIONS TextAlign registration (the pattern mirrored here)
 * @see ./hooks/useComposeDocumentStyles.ts — ComposePStyleExtension (the same additive-attribute idiom)
 * @see ../../../server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs — AppendIndentDeclarations (FR-07 emit)
 */
import { Extension } from '@tiptap/core';

/** Same two paraId-/textAlign-bearing block types every sibling additive attribute extension targets. */
const INDENT_NODE_TYPES = ['paragraph', 'heading'] as const;

/**
 * Additive TipTap extension registering `indentMarginLeft`/`indentTextIndent` node attributes on
 * `paragraph` + `heading`, sourced from (and re-emitted as) the CSS `margin-left`/`text-indent` inline
 * style the server projection's `AppendIndentDeclarations` writes. Values are opaque CSS length strings
 * (e.g. `"36pt"`, `"-18pt"`) — this extension does not parse/recompute the twips→pt conversion; that
 * stays server-side (ADR-007/013: `Services/Compose/` is the sole owner of the OOXML read).
 */
export const ComposeIndentExtension = Extension.create({
  name: 'composeIndent',
  addGlobalAttributes() {
    return [
      {
        types: [...INDENT_NODE_TYPES],
        attributes: {
          indentMarginLeft: {
            default: null,
            parseHTML: (element: HTMLElement) => element.style.marginLeft || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.indentMarginLeft as string | null;
              return value ? { style: `margin-left: ${value}` } : {};
            },
          },
          // ── UAT round 2 (spaarkeai-compose-r8, 2026-09-02): document SPACING ──────────────────
          // Same pattern, same reason: the projection's `AppendSpacingDeclarations` writes the paragraph's
          // own `w:spacing` as inline CSS, and TipTap's base Paragraph/Heading nodes strip any style
          // property they do not know about — so without these three the editor would drop the document's
          // real spacing on the `setContent(projection.html)` parse and fall back to the generic
          // typographic defaults in ComposeEditor's `editorSurface`.
          //
          // Values stay OPAQUE CSS strings (`"1.5"`, `"18pt"`). The twips/240ths conversion and the
          // `w:lineRule` auto-vs-exact distinction are the SERVER's (ADR-007/013: `Services/Compose/` owns
          // the OOXML read) — re-deriving either here would be the two-engine drift this project exists to
          // prevent, on a value where the two readings differ by more than an order of magnitude.
          spacingLineHeight: {
            default: null,
            parseHTML: (element: HTMLElement) => element.style.lineHeight || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.spacingLineHeight as string | null;
              return value ? { style: `line-height: ${value}` } : {};
            },
          },
          spacingMarginTop: {
            default: null,
            parseHTML: (element: HTMLElement) => element.style.marginTop || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.spacingMarginTop as string | null;
              return value ? { style: `margin-top: ${value}` } : {};
            },
          },
          spacingMarginBottom: {
            default: null,
            parseHTML: (element: HTMLElement) => element.style.marginBottom || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.spacingMarginBottom as string | null;
              return value ? { style: `margin-bottom: ${value}` } : {};
            },
          },
          indentTextIndent: {
            default: null,
            parseHTML: (element: HTMLElement) => element.style.textIndent || null,
            renderHTML: (attributes: Record<string, unknown>) => {
              const value = attributes.indentTextIndent as string | null;
              return value ? { style: `text-indent: ${value}` } : {};
            },
          },
        },
      },
    ];
  },
});

/** Mount additively in `ComposeEditor.tsx`'s `useEditor` extensions list, alongside `COMPOSE_R3_STYLES`. */
export const COMPOSE_INDENT = [ComposeIndentExtension];
