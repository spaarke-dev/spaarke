/**
 * opaqueAtomNode.ts — opaque-atom node types for the Compose editor (spaarkeai-compose-r4 task 021,
 * FR-02 client half).
 *
 * Renders the server projection's opaque-atom placeholders (task 012,
 * `ComposeDocxProjectionBuilder.EmitBlockAtom` / `BuildContext.AppendAtom`) — non-renderable OOXML
 * constructs (SDT/content controls, fields, complex/floating objects) — as VISIBLE, NON-EDITABLE
 * ProseMirror `atom: true` leaf nodes. The server's HTML markup this parses:
 *
 *   BLOCK atom  (`EmitBlockAtom`, task 012): a whole-construct SDT standing between paragraphs —
 *     `<div class="compose-atom" data-atom-kind="sdt" data-atomid="A1B2C3D4" contenteditable="false"></div>`
 *     Carries its OWN minted id (`data-atomid`) — deliberately NOT `data-paraid` (task-012 decisions
 *     §3: block atoms are kept OUT of `ParaIdMap`, whose contract is "one entry per body paragraph").
 *
 *   INLINE atom (`AppendAtom`, task 012): a field / inline SDT / complex object living inside a
 *     paragraph's own run flow —
 *     `<span class="compose-atom" data-atom-kind="field" contenteditable="false">1</span>`
 *     Carries NO separate identity of its own; it sits inside its CONTAINING paragraph's
 *     `data-paraid` block (task-012 decisions §3). This module recovers that paraId at parse time
 *     via `element.closest('[data-paraid]')` and mirrors it as a HIDDEN node attribute — same
 *     convention as `paraIdExtension.ts` (never rendered to the DOM; FR-09 precedent).
 *
 * DOCUMENT ORDER (FR-02): preserved structurally — both node types parse at the exact DOM position
 * the server emitted them, which is document order by construction (the projection builder appends
 * HTML in a single top-to-bottom walk). No position/index attribute is needed to reconstruct order.
 *
 * NON-EDITABILITY (I-1/I-4, FR-02): both nodes declare NO `content` expression (ProseMirror leaf
 * nodes) and `atom: true` — there is no interior cursor position inside an atom, so "typing inside"
 * is structurally impossible (the caret is placed before/after the atom, never within it). This is
 * the SCHEMA-level half of non-editability. The step-interceptor PLUGIN (task 020, parallel task,
 * different file) is the TRANSACTION-level half — it refuses to route a captured edit whose anchor
 * falls inside an atom (signaled server-side via `RunBoundary.AtomKind` / `RunBoundary.IsAtom`) into
 * an operation. Per the task-020/021 coordination split, this file does NOT attempt to also guard
 * NodeSelection-replace-via-typing (a ProseMirror `appendTransaction`/`filterTransaction` concern) —
 * that is task 020's job; duplicating it here would cross the parallel-task file boundary.
 *
 * DISPLAY-ONLY (I-1/I-4): the atom's OOXML is NEVER opened here — this module renders a placeholder
 * LABEL only (the atom `kind`, plus the server's cached display text for inline atoms, e.g. a field's
 * resolved value). It never authors or mutates the atom's bytes; the server projection already
 * degraded the construct to a display string before this module ever sees it.
 *
 * NFR-03 (licensing): built entirely on `@tiptap/core`'s `Node.create` (MIT). No `@tiptap-pro/*`,
 * no AGPL. Same technique TipTap's own (MIT) `@tiptap/extension-mention` uses for an atomic inline
 * node (`inline: true, group: 'inline', atom: true`) — not imported (avoids an unused dependency),
 * just the well-known pattern, mirrored by hand.
 *
 * ADR-021 (dark mode): this module emits classes only (`compose-atom`, `compose-atom-block`,
 * `compose-atom-inline`) — never inline `style=`/hex. Token-based rules live in ComposeEditor's
 * `useStyles()` alongside the other `compose-mark-*` / `compose-track-*` classes.
 *
 * @see ../../../../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs (EmitBlockAtom, AppendAtom)
 * @see ./paraIdExtension.ts (the hidden-attribute convention this mirrors for inline atoms' paraId)
 * @see projects/spaarkeai-compose-r4/notes/task-012-opaque-atoms-decisions.md §3 (identity model)
 * @see projects/spaarkeai-compose-r4/notes/task-021-opaque-atom-node-decisions.md (this task's decisions)
 * @see projects/spaarkeai-compose-r4/spec.md FR-02, NFR-03
 */
import { Node, mergeAttributes } from '@tiptap/core';

/** The four atom-kind tokens the server projection emits (`AtomKindToken` in ComposeDocxProjectionBuilder.cs). */
export type ComposeAtomKind = 'sdt' | 'field' | 'object' | 'unknown';

/** Human-readable label per atom kind — the placeholder's visible "labeled with its kind" text. */
const ATOM_KIND_LABELS: Record<ComposeAtomKind, string> = {
  sdt: 'Content control',
  field: 'Field',
  object: 'Object',
  unknown: 'Unsupported content',
};

function atomKindLabel(kind: string | null | undefined): string {
  return ATOM_KIND_LABELS[kind as ComposeAtomKind] ?? ATOM_KIND_LABELS.unknown;
}

function readAtomKind(element: HTMLElement): ComposeAtomKind {
  const raw = element.getAttribute('data-atom-kind');
  return raw === 'sdt' || raw === 'field' || raw === 'object' ? raw : 'unknown';
}

// ---------------------------------------------------------------------------
// Block atom — `<div class="compose-atom" data-atom-kind="…" data-atomid="…">` (EmitBlockAtom)
// ---------------------------------------------------------------------------

export interface ComposeBlockAtomOptions {
  /** Extra HTML attributes merged onto the rendered `<div>` element. */
  HTMLAttributes: Record<string, unknown>;
}

/** Attributes carried by a block-level opaque atom (identifiers only). */
export interface ComposeBlockAtomAttributes {
  /** The server-minted atom id (`data-atomid`) — this atom's OWN identity (never a paraId). */
  atomId?: string | null;
  /** Atom classification (`data-atom-kind`) — currently always `'sdt'` for block atoms (task 012). */
  kind?: ComposeAtomKind;
}

/**
 * `composeBlockAtom` — a whole-construct opaque SDT standing between editable paragraphs. A
 * `group: 'block'` atom leaf (no `content` expression): document order is its ProseMirror sibling
 * position among the surrounding `paragraph`/`heading` nodes, exactly as the server projected it.
 */
export const ComposeBlockAtomNode = Node.create<ComposeBlockAtomOptions>({
  name: 'composeBlockAtom',

  group: 'block',
  atom: true,
  selectable: true,
  draggable: false,
  isolating: true,
  // No marks apply to a non-text placeholder (there is no text run to carry them).
  marks: '',

  addOptions() {
    return { HTMLAttributes: {} };
  },

  addAttributes() {
    return {
      atomId: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-atomid'),
        renderHTML: attributes =>
          attributes.atomId ? { 'data-atomid': attributes.atomId as string } : {},
      },
      kind: {
        default: 'unknown',
        parseHTML: (element: HTMLElement) => readAtomKind(element),
        renderHTML: attributes => ({ 'data-atom-kind': (attributes.kind as string) ?? 'unknown' }),
      },
    };
  },

  parseHTML() {
    // The server ONLY ever emits a block atom as a `data-atomid`-bearing div (EmitBlockAtom always
    // mints one). Requiring `data-atomid` in the selector keeps this rule from ever matching an
    // inline atom's `<span>` (different tag entirely, but explicit for clarity/defensiveness).
    return [{ tag: 'div.compose-atom[data-atomid]' }];
  },

  renderHTML({ node, HTMLAttributes }) {
    const kind = (node.attrs.kind as string) ?? 'unknown';
    return [
      'div',
      mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
        class: 'compose-atom compose-atom-block',
        // Belt-and-suspenders: the schema already makes this a cursor-free leaf; mirroring the
        // server's own `contenteditable="false"` keeps a raw-DOM inspection story consistent too.
        contenteditable: 'false',
      }),
      atomKindLabel(kind),
    ];
  },
});

// ---------------------------------------------------------------------------
// Inline atom — `<span class="compose-atom" data-atom-kind="…">…</span>` (AppendAtom)
// ---------------------------------------------------------------------------

export interface ComposeInlineAtomOptions {
  /** Extra HTML attributes merged onto the rendered `<span>` element. */
  HTMLAttributes: Record<string, unknown>;
}

/** Attributes carried by an inline opaque atom. */
export interface ComposeInlineAtomAttributes {
  /** Atom classification (`data-atom-kind`): `'field'` | `'sdt'` | `'object'`. */
  kind?: ComposeAtomKind;
  /**
   * The CONTAINING paragraph's `w14:paraId`, recovered at parse time via `closest('[data-paraid]')`
   * (task-012 decisions §3: an inline atom carries no identity of its own). HIDDEN — never rendered
   * to the DOM, mirroring `paraIdExtension.ts`'s convention for the paragraph-level `paraId` attr.
   * A client-side addressability convenience only: the authoritative "do not target inside me"
   * signal for the patch model is the server's `RunBoundary.AtomKind` in the offset-addressing
   * table, which this attribute is not a substitute for.
   */
  paraId?: string | null;
  /** The server's cached display text (a field's resolved value, an SDT's rendered content), if any. */
  displayText?: string | null;
}

/**
 * `composeInlineAtom` — a field / inline content-control / complex-object atom living inside a
 * paragraph's own run flow. A `group: 'inline'` atom leaf (no `content` expression), same pattern
 * TipTap's own MIT `@tiptap/extension-mention` uses for an atomic inline node.
 */
export const ComposeInlineAtomNode = Node.create<ComposeInlineAtomOptions>({
  name: 'composeInlineAtom',

  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  draggable: false,
  marks: '',

  addOptions() {
    return { HTMLAttributes: {} };
  },

  addAttributes() {
    return {
      kind: {
        default: 'unknown',
        parseHTML: (element: HTMLElement) => readAtomKind(element),
        renderHTML: attributes => ({ 'data-atom-kind': (attributes.kind as string) ?? 'unknown' }),
      },
      paraId: {
        default: null,
        parseHTML: (element: HTMLElement) => element.closest('[data-paraid]')?.getAttribute('data-paraid') ?? null,
        // Hidden — never emitted (FR-09 precedent; see paraIdExtension.ts).
        renderHTML: () => ({}),
      },
      displayText: {
        default: null,
        parseHTML: (element: HTMLElement) => element.textContent?.trim() || null,
        // Not re-emitted as an attribute — re-rendered as the placeholder's visible label instead
        // (see renderHTML below), so it stays testable via getHTML() without a redundant data-*.
        renderHTML: () => ({}),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'span.compose-atom[data-atom-kind]' }];
  },

  renderHTML({ node, HTMLAttributes }) {
    const kind = (node.attrs.kind as string) ?? 'unknown';
    const displayText = node.attrs.displayText as string | null;
    const label = atomKindLabel(kind);
    const content = displayText ? `${label}: ${displayText}` : label;
    return [
      'span',
      mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
        class: 'compose-atom compose-atom-inline',
        contenteditable: 'false',
      }),
      content,
    ];
  },
});

/**
 * Registration array — ADDITIVE to the LOCKED Spike #1 extension list + every other additive array
 * in ComposeEditor.tsx (never mutates them), same convention as `COMPOSE_R2_MARKS` / `COMPOSE_R3_PARAID`.
 */
export const COMPOSE_R4_OPAQUE_ATOMS = [ComposeBlockAtomNode, ComposeInlineAtomNode];
