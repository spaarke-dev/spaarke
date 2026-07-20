/**
 * useComposeDocumentStyles — FR-22 styles pane, apply-existing-only (spaarkeai-compose-r3 task 043).
 *
 * SCOPE GUARD (binding, spec FR-22 + Assumptions): apply EXISTING paragraph styles to the current
 * selection ONLY. This module MUST NOT expose any create/rename/delete/manage-style capability —
 * full style management is Word-parity, explicitly deferred to Open-in-Word (design §7.5).
 *
 * SUBSTRATE NOTE (read before extending): design §7.5's capability table lists "Headings + paragraph
 * styles | StarterKit + style mapping (shipped/extend)" as the source, and the task brief pointed at
 * `docxBridge.ts` as "where OOXML style names / `pStyle` are surfaced". On inspection, `docxBridge.ts`
 * does NOT currently carry a named-style catalog — mammoth's default conversion maps Word headings to
 * `<h1>`-`<h6>` and everything else to `<p>`; no custom Word style NAME (e.g. a firm's "Recital") is
 * captured anywhere in the load pipeline today, client or server. Per the binding "no invented list"
 * constraint, this hook does NOT fabricate a fixed style catalog. Instead it derives the catalog LIVE
 * from the loaded document's actual ProseMirror structure — the `heading` node's `level` attribute
 * (StarterKit, levels 1-6) and the `paragraph` node type — mapped to Word's own canonical built-in
 * style ids (`Heading1`..`Heading6`, `Normal`). This is genuinely non-invented (it lists only styles
 * ACTUALLY PRESENT in the current document, recomputed as the document changes) while staying
 * client-only (no BFF change, ADR-028). Surfacing a full custom-named-style catalog (e.g. "Recital")
 * would require a server-side OOXML style-name pre-parse — out of this client-only task's scope, and a
 * natural fast-follow once the server pre-parser is extended (see `docxBridge.ts` FR-08/09 pattern for
 * the precedent: server pre-parse + client stamp).
 *
 * The `pStyle` node attribute this hook introduces mirrors the existing `paraId` hidden-attribute
 * pattern (`paraIdExtension.ts`): additive to the LOCKED Spike #1 extension list (never mutates it),
 * hidden from the rendered DOM (`renderHTML: () => ({})`), and set via an explicit `tr.setNodeMarkup`
 * transaction — the same idiom `stampParaIds` (docxBridge.ts) uses. Applying a style is therefore an
 * ATTRIBUTE change (+ a node-type change for headings), never a formatting-MARK change.
 *
 * @see ../ComposeStylesPane.tsx — the Fluent v9 pane consuming this hook
 * @see ../../utils/docxBridge.ts — `stampParaIds`'s `tr.setNodeMarkup` idiom, mirrored here
 * @see ../paraIdExtension.ts — the sibling hidden-node-attribute extension this pattern mirrors
 * @see projects/spaarkeai-compose-r3/design.md §7.5 · spec.md FR-22 + Assumptions
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import { Extension } from '@tiptap/core';

// ---------------------------------------------------------------------------
// Schema extension — hidden `pStyle` node attribute on paragraph + heading.
// ---------------------------------------------------------------------------

/**
 * Additive TipTap extension registering a hidden `pStyle` node attribute on the `paragraph` and
 * `heading` node types (the same two paraId-bearing block types — `PARAID_NODE_TYPES` in
 * `paraIdExtension.ts`). Default `null`; never rendered to the DOM. Mount this ADDITIVELY alongside
 * the LOCKED Spike #1 extension list, `COMPOSE_R3_PARAID`, etc. — never mutate the locked list.
 */
export const ComposePStyleExtension = Extension.create({
  name: 'composePStyle',
  addGlobalAttributes() {
    return [
      {
        types: ['paragraph', 'heading'],
        attributes: {
          pStyle: {
            default: null,
            // No natural HTML representation on mammoth-imported markup today (see the substrate
            // note above) — never pick up an unrelated attribute from arbitrary source HTML.
            parseHTML: () => null,
            // Hidden node attribute (mirrors paraId's FR-09 convention) — never emitted to the DOM.
            renderHTML: () => ({}),
          },
        },
      },
    ];
  },
});

/** Mount alongside the other additive extension arrays in `ComposeEditor.tsx`'s `useEditor` call. */
export const COMPOSE_R3_STYLES = [ComposePStyleExtension];

// ---------------------------------------------------------------------------
// Style catalog — a Word-canonical OOXML style id + a human display name.
// ---------------------------------------------------------------------------

export interface ComposeDocumentStyle {
  /** Word's canonical built-in style id (e.g. `Normal`, `Heading1`). */
  styleId: string;
  /** Human-readable label for the pane (e.g. `Body Text`, `Heading 1`). */
  displayName: string;
}

/** Doc-structure-order catalog key — Body Text first, then headings low-to-high. */
const STYLE_ORDER = ['Normal', 'Heading1', 'Heading2', 'Heading3', 'Heading4', 'Heading5', 'Heading6'];

function styleIdForHeadingLevel(level: number): string {
  return `Heading${level}`;
}

function displayNameFor(styleId: string): string {
  if (styleId === 'Normal') return 'Body Text';
  const headingMatch = /^Heading(\d)$/.exec(styleId);
  if (headingMatch) return `Heading ${headingMatch[1]}`;
  return styleId;
}

/** Parse a heading level (1-6) back out of a `HeadingN` style id, or `null` for `Normal`/anything else. */
function headingLevelOf(styleId: string): number | null {
  const match = /^Heading(\d)$/.exec(styleId);
  if (!match) return null;
  const level = Number(match[1]);
  return level >= 1 && level <= 6 ? level : null;
}

/**
 * The set of existing paragraph styles actually present in `editor`'s CURRENT document, derived from
 * the live ProseMirror node types (`heading.level` / `paragraph`) — NOT an invented fixed list. Doc
 * order is normalized (Body Text, then Heading 1 → 6) so the pane's listing is stable across renders.
 * Exported for direct unit testing.
 */
export function deriveDocumentStyles(editor: Editor): ComposeDocumentStyle[] {
  const present = new Set<string>();
  editor.state.doc.descendants(node => {
    if (node.type.name === 'heading') {
      const level = (node.attrs.level as number | undefined) ?? 1;
      present.add(styleIdForHeadingLevel(level));
    } else if (node.type.name === 'paragraph') {
      present.add('Normal');
    }
    return true;
  });
  return STYLE_ORDER.filter(id => present.has(id)).map(styleId => ({ styleId, displayName: displayNameFor(styleId) }));
}

/**
 * The existing style id of the paragraph/heading block at the selection anchor, or `undefined` when
 * the selection is not inside a paraId-bearing block (should not normally happen — paragraph/heading
 * are the editor's only top-level textblocks).
 */
function activeStyleIdAt(editor: Editor): string | undefined {
  const { from } = editor.state.selection;
  let found: string | undefined;
  editor.state.doc.nodesBetween(from, from, node => {
    if (node.type.name === 'heading') {
      found = styleIdForHeadingLevel((node.attrs.level as number | undefined) ?? 1);
    } else if (node.type.name === 'paragraph') {
      found = 'Normal';
    }
    return true;
  });
  return found;
}

// ---------------------------------------------------------------------------
// Apply — a selection-scoped node ATTRIBUTE (+ type, for headings) change.
// ---------------------------------------------------------------------------

/**
 * Apply `style` (an entry already present in {@link deriveDocumentStyles}'s catalog — the SCOPE GUARD
 * boundary: this function does not validate `style.styleId` against Word's full built-in style
 * universe, only the caller, {@link useComposeDocumentStyles.applyStyle}, does that) to every
 * paragraph/heading block touching the current selection.
 *
 * This is an ATTRIBUTE change, not a formatting-mark change: each affected block's node type
 * (paragraph vs. heading) and `level`/`pStyle` attrs are rewritten in place via `tr.setNodeMarkup` —
 * the SAME idiom `stampParaIds` (docxBridge.ts) uses for the paraId stamp, chosen deliberately so the
 * block's `paraId` (FR-09 identity) and `textAlign` (if present) survive a style change unmolested;
 * `setNodeMarkup` does not resize the document, so positions captured during the `descendants` walk
 * stay valid across multiple calls on the same transaction (mirrors the same guarantee `stampParaIds`
 * relies on). Returns `true` when at least one block was restyled.
 *
 * Exported for direct unit testing.
 */
export function applyComposeDocumentStyle(editor: Editor, style: ComposeDocumentStyle): boolean {
  if (editor.isDestroyed) return false;
  const level = headingLevelOf(style.styleId);
  const { state } = editor;
  const paragraphType = state.schema.nodes.paragraph;
  const headingType = state.schema.nodes.heading;
  if (!paragraphType || (level !== null && !headingType)) return false;

  const { from, to } = state.selection;
  const tr = state.tr;
  let applied = false;
  state.doc.nodesBetween(from, to, (node, pos) => {
    if (node.type === paragraphType || node.type === headingType) {
      const targetType = level !== null ? headingType : paragraphType;
      const targetAttrs: Record<string, unknown> = {
        paraId: node.attrs.paraId ?? null,
        pStyle: style.styleId,
      };
      if (level !== null) targetAttrs.level = level;
      if ('textAlign' in node.attrs) targetAttrs.textAlign = node.attrs.textAlign;
      tr.setNodeMarkup(pos, targetType, targetAttrs, node.marks);
      applied = true;
    }
    return true;
  });
  if (!applied) return false;
  editor.view.dispatch(tr);
  editor.commands.focus();
  return true;
}

// ---------------------------------------------------------------------------
// The hook
// ---------------------------------------------------------------------------

export interface UseComposeDocumentStylesResult {
  /** Existing paragraph styles found in the CURRENT document — recomputed live as it changes. */
  styles: ComposeDocumentStyle[];
  /** The existing style id of the block under the selection anchor, or `undefined`. */
  activeStyleId: string | undefined;
  /**
   * Apply an EXISTING style (identified by `styleId`, which MUST be present in `styles` — the scope
   * guard boundary) to the current selection. Returns `false` (no-op) for any `styleId` not already
   * in the catalog — this hook never creates a new style.
   */
  applyStyle: (styleId: string) => boolean;
}

/**
 * FR-22 styles-pane state: the live existing-style catalog for `editor`'s document plus an
 * apply-to-selection action. `null` editor yields an empty, inert result (mirrors
 * `useComposeFindReplace`'s null-editor handling).
 */
export function useComposeDocumentStyles(editor: Editor | null): UseComposeDocumentStylesResult {
  const [tick, setTick] = React.useState(0);

  React.useEffect(() => {
    if (!editor) return;
    const handler = (): void => setTick(t => t + 1);
    editor.on('transaction', handler);
    editor.on('selectionUpdate', handler);
    return () => {
      editor.off('transaction', handler);
      editor.off('selectionUpdate', handler);
    };
  }, [editor]);

  const styles = React.useMemo(
    () => (editor && !editor.isDestroyed ? deriveDocumentStyles(editor) : []),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `tick` is the live-recompute trigger.
    [editor, tick]
  );

  const activeStyleId = React.useMemo(
    () => (editor && !editor.isDestroyed ? activeStyleIdAt(editor) : undefined),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `tick` is the live-recompute trigger.
    [editor, tick]
  );

  const applyStyle = React.useCallback(
    (styleId: string): boolean => {
      if (!editor || editor.isDestroyed) return false;
      // SCOPE GUARD: only a style already present in the catalog is applicable — no ad hoc style id
      // reaches the ProseMirror layer, so this hook can never author a NEW style.
      const style = styles.find(s => s.styleId === styleId);
      if (!style) return false;
      return applyComposeDocumentStyle(editor, style);
    },
    [editor, styles]
  );

  return React.useMemo(() => ({ styles, activeStyleId, applyStyle }), [styles, activeStyleId, applyStyle]);
}

export default useComposeDocumentStyles;
