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

/** The atom-kind tokens the server projection emits (`AtomKindToken` in ComposeDocxProjectionBuilder.cs). */
export type ComposeAtomKind = 'sdt' | 'field' | 'object' | 'tab' | 'symbol' | 'unknown';

/**
 * Task 048: the kinds that render as THEMSELVES rather than as a labeled placeholder.
 *
 * `sdt` / `field` / `object` are OPAQUE — the server could not render them, so the editor shows a label
 * saying what was there. `tab` and `symbol` are the opposite: they render exactly as they always did (an
 * em space, the resolved glyph), and are atoms only in the caret sense — a leaf with no interior position,
 * so the user can select or delete one but never type inside it and never split it in half.
 *
 * They are here at all because that identity is the ONLY thing that was missing. A tab reached the editor
 * as an em space and a symbol as a glyph, so an edit anywhere in the paragraph rebuilt both as ordinary
 * text. Nothing about their appearance needed to change — only whether the mapper can recognize them.
 */
const RENDERS_AS_ITSELF = new Set<ComposeAtomKind>(['tab', 'symbol']);

/**
 * Whether an atom of this kind renders as its own content rather than as a labeled placeholder.
 *
 * Exported because the MAPPER needs exactly this distinction (`docxBridge.ts`): a renderable atom's display
 * text is the document's own character and belongs in the editor's text coordinate space, whereas an opaque
 * atom's is a UI LABEL ("Field: 3") that must never be mistaken for content. One definition, two consumers.
 */
export function atomRendersAsItself(kind: string | null | undefined): boolean {
  return RENDERS_AS_ITSELF.has(kind as ComposeAtomKind);
}

/** Human-readable label per OPAQUE atom kind — the placeholder's visible "labeled with its kind" text. */
const ATOM_KIND_LABELS: Record<ComposeAtomKind, string> = {
  sdt: 'Content control',
  field: 'Field',
  object: 'Object',
  // Never shown (RENDERS_AS_ITSELF), but present so the record stays total over the kind union.
  tab: 'Tab',
  symbol: 'Symbol',
  unknown: 'Unsupported content',
};

function atomKindLabel(kind: string | null | undefined): string {
  return ATOM_KIND_LABELS[kind as ComposeAtomKind] ?? ATOM_KIND_LABELS.unknown;
}

const KNOWN_ATOM_KINDS: readonly string[] = ['sdt', 'field', 'object', 'tab', 'symbol'];

function readAtomKind(element: HTMLElement): ComposeAtomKind {
  const raw = element.getAttribute('data-atom-kind');
  return KNOWN_ATOM_KINDS.includes(raw ?? '') ? (raw as ComposeAtomKind) : 'unknown';
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
        renderHTML: attributes => (attributes.atomId ? { 'data-atomid': attributes.atomId as string } : {}),
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
  /**
   * Task 048, `symbol` kind only: the symbol FONT (`w:sym/@w:font`, e.g. `Symbol`). Round-tripped so the
   * save re-emits the original `w:sym` instead of the glyph the reader resolved for display.
   */
  symFont?: string | null;
  /** Task 048, `symbol` kind only: the code point within that font (`w:sym/@w:char`, e.g. `F0A7`). */
  symChar?: string | null;
  /**
   * Task 057, `field` kind only: the field INSTRUCTION verbatim (`data-field-instr` —
   * `w:fldSimple/@w:instr`, or the concatenated `w:instrText` of the `w:fldChar` code phase), including
   * the leading/trailing spaces Word writes.
   *
   * Its PRESENCE is the carryability gate. `ComposeDocxProjectionBuilder.FieldAtomDataAttributes` emits
   * this attribute only for a field the server can re-emit exactly; a NESTED or instruction-less field
   * gets no payload at all, so a client structurally cannot hand back a construct the server would have
   * to refuse. The rule lives in one place (the server's `TryCarryField`), mirrored here as data rather
   * than restated as client policy.
   *
   * Declared here because ProseMirror keeps only the attributes a node's schema NAMES: without this
   * declaration the server's payload is dropped at `setContent` and task 049's carry is unreachable from
   * a keystroke edit. Same mechanism task 048 added for `w:sym`'s font + code point — and, like those,
   * re-emitted by `renderHTML` so the payload also survives the `getHTML()` round trip the local draft
   * store persists (`ComposeEditor.getDraftHtml`).
   *
   * DISPLAY-ONLY still holds: this module never parses or authors the instruction, and never renders it.
   * The atom shows the same "Field: <cached result>" label it always did.
   */
  fieldInstruction?: string | null;
  /**
   * Task 057, `field` kind only: `true` when the source authored the field as the `w:fldChar`
   * begin/instrText/separate/result/end RUN sequence (`data-field-complex`); `false` for the compact
   * `w:fldSimple` element. The renderer reproduces the FORM the document used rather than normalising —
   * Word treats the two as equivalent, but a save is not licensed to rewrite what the file contains.
   */
  fieldComplex?: boolean;
  /**
   * Task 057, `field` kind only: `w:fldLock` (`data-field-locked`) — the author froze this field so it
   * never updates. Dropping it is the one way the carry could be WORSE than flattening: it would convert
   * a deliberately frozen field into a live one.
   */
  fieldLocked?: boolean;
  /** Task 057, `field` kind only: `w:dirty` (`data-field-dirty`) — the author asked Word to re-evaluate
   * this field on next open. The document's own instruction about when the field may change. */
  fieldDirty?: boolean;
}

/** Read a server `data-field-*` boolean flag — emitted as `"1"` when true, omitted entirely when false. */
function readFieldFlag(element: HTMLElement, attribute: string): boolean {
  return element.getAttribute(attribute) === '1';
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
        // Task 048: NOT trimmed any more. A tab's display text is a single em space — trimming it to the
        // empty string would erase the one character the atom contributes to the editor's text coordinate
        // space, which is exactly the space the server's offset table already counts for a `w:tab`. The
        // opaque kinds are unaffected: their display text never has meaningful edge whitespace, and an
        // empty string still falls back to the bare label below.
        //
        // Task 057: prefer an explicit `data-atom-display` over the element's text. The server never
        // emits that attribute (its atom span contains exactly the display text), so the load path is
        // unchanged — but THIS node's own `renderHTML` does, and without it the round trip corrupts the
        // value. An OPAQUE atom renders as "<label>: <displayText>", so re-parsing `getHTML()` output
        // read `Field: 4` back as the display text, and a second pass read `Field: Field: 4`. That was
        // cosmetic while the display text was only ever a UI label; it stopped being cosmetic once task
        // 057 made it the field's `cachedResult`, i.e. a string written into the saved document. The
        // round trip is real and reachable: `ComposeEditor.getDraftHtml` persists `getHTML()` to the
        // local draft store on the dirty-autosave tick, and the FR-03 recovery path re-mounts it.
        parseHTML: (element: HTMLElement) =>
          element.getAttribute('data-atom-display') ?? (element.textContent || null),
        renderHTML: attributes =>
          attributes.displayText ? { 'data-atom-display': attributes.displayText as string } : {},
      },
      symFont: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-sym-font'),
        renderHTML: attributes => (attributes.symFont ? { 'data-sym-font': attributes.symFont as string } : {}),
      },
      symChar: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-sym-char'),
        renderHTML: attributes => (attributes.symChar ? { 'data-sym-char': attributes.symChar as string } : {}),
      },
      // Task 057 — the field payload. Parsed AND re-emitted: parsing is what makes the carry reachable
      // from a keystroke edit at all, re-emitting is what keeps it alive across the `getHTML()` round trip
      // the local draft store persists. Absent `data-field-instr` => `null` => the mapper emits no field
      // run, which is exactly the server's own carryability refusal.
      fieldInstruction: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-field-instr'),
        renderHTML: attributes =>
          attributes.fieldInstruction ? { 'data-field-instr': attributes.fieldInstruction as string } : {},
      },
      fieldComplex: {
        default: false,
        parseHTML: (element: HTMLElement) => readFieldFlag(element, 'data-field-complex'),
        renderHTML: attributes => (attributes.fieldComplex ? { 'data-field-complex': '1' } : {}),
      },
      fieldLocked: {
        default: false,
        parseHTML: (element: HTMLElement) => readFieldFlag(element, 'data-field-locked'),
        renderHTML: attributes => (attributes.fieldLocked ? { 'data-field-locked': '1' } : {}),
      },
      fieldDirty: {
        default: false,
        parseHTML: (element: HTMLElement) => readFieldFlag(element, 'data-field-dirty'),
        renderHTML: attributes => (attributes.fieldDirty ? { 'data-field-dirty': '1' } : {}),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'span.compose-atom[data-atom-kind]' }];
  },

  renderHTML({ node, HTMLAttributes }) {
    const kind = (node.attrs.kind as ComposeAtomKind) ?? 'unknown';
    const displayText = node.attrs.displayText as string | null;

    // Task 048: a renderable atom IS its content — no label, and it keeps the class its appearance already
    // depended on (`compose-tab`). Anything else is opaque and shows a labeled placeholder.
    //
    // `compose-atom-renderable` is what stops it LOOKING like a placeholder. `.compose-atom` styles an atom
    // as a dashed, background-filled, italic chip — right for "a content control was here", very wrong for a
    // tab or a section mark, which are ordinary document content and must render indistinguishably from the
    // plain text they were before this change. The modifier class resets that chrome in ComposeEditor's
    // `useStyles()`. The `compose-atom` class itself has to stay: it is half the parse selector.
    if (RENDERS_AS_ITSELF.has(kind)) {
      const classes = ['compose-atom', 'compose-atom-inline', 'compose-atom-renderable'];
      if (kind === 'tab') classes.push('compose-tab');
      return [
        'span',
        mergeAttributes(this.options.HTMLAttributes, HTMLAttributes, {
          class: classes.join(' '),
          contenteditable: 'false',
        }),
        displayText ?? '',
      ];
    }

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
