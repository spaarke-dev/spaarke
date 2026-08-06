/**
 * opaqueAtomNode.test.ts — coverage for the R4 opaque-atom node types (task 021, FR-02 client half):
 * `composeBlockAtom` / `composeInlineAtom`.
 *
 * Exercised through a headless TipTap `Editor` (StarterKit provides doc/paragraph/text; the atom
 * nodes register additively) — the same schema-registration path ComposeEditor uses, mirroring
 * `marks/marks.test.ts` and `ComposeEditor.paraId.test.tsx`. Protects the FR-02 acceptance criteria:
 *   - fields/SDTs render as placeholder atom nodes, in correct document order, without editor error;
 *   - each atom node is non-editable and carries its identity (paraId for inline; atomId for block —
 *     see task-021 decisions notes for why block atoms use atomId, not paraId);
 *   - typing "inside" an atom is structurally impossible (no interior cursor position) — negative case;
 *   - no inline hex/style leaks onto the rendered placeholder (ADR-021).
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { ComposeBlockAtomNode, ComposeInlineAtomNode, COMPOSE_R4_OPAQUE_ATOMS } from './opaqueAtomNode';
import { COMPOSE_R3_PARAID } from './paraIdExtension';

function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID, ...COMPOSE_R4_OPAQUE_ATOMS],
    content,
  });
}

describe('R4 opaque-atom nodes (task 021, FR-02)', () => {
  it('registers composeBlockAtom and composeInlineAtom in the editor schema', () => {
    const editor = makeEditor();
    const nodeNames = Object.keys(editor.schema.nodes);
    expect(nodeNames).toEqual(expect.arrayContaining(['composeBlockAtom', 'composeInlineAtom']));
    editor.destroy();
  });

  it('both node types are atom leaves with no content expression (structural non-editability)', () => {
    const editor = makeEditor();
    expect(editor.schema.nodes.composeBlockAtom.isAtom).toBe(true);
    expect(editor.schema.nodes.composeBlockAtom.isLeaf).toBe(true);
    expect(editor.schema.nodes.composeInlineAtom.isAtom).toBe(true);
    expect(editor.schema.nodes.composeInlineAtom.isLeaf).toBe(true);
    editor.destroy();
  });

  describe('block atom — EmitBlockAtom projection shape', () => {
    const html =
      '<p>Before</p>' +
      '<div class="compose-atom" data-atom-kind="sdt" data-atomid="A1B2C3D4" contenteditable="false"></div>' +
      '<p>After</p>';

    it('parses the server div into a composeBlockAtom, preserving document order between paragraphs', () => {
      const editor = makeEditor(html);
      const typeNames: string[] = [];
      editor.state.doc.forEach(node => typeNames.push(node.type.name));
      expect(typeNames).toEqual(['paragraph', 'composeBlockAtom', 'paragraph']);
      editor.destroy();
    });

    it('carries the server-minted atomId and kind as node attributes', () => {
      const editor = makeEditor(html);
      let atomNode: { attrs: Record<string, unknown> } | null = null;
      editor.state.doc.descendants(node => {
        if (node.type.name === 'composeBlockAtom') atomNode = node;
        return true;
      });
      expect(atomNode).not.toBeNull();
      expect((atomNode as unknown as { attrs: Record<string, unknown> }).attrs.atomId).toBe('A1B2C3D4');
      expect((atomNode as unknown as { attrs: Record<string, unknown> }).attrs.kind).toBe('sdt');
      editor.destroy();
    });

    it('renders a visible, labeled, non-editable placeholder', () => {
      const editor = makeEditor(html);
      const out = editor.getHTML();
      expect(out).toContain('class="compose-atom compose-atom-block"');
      expect(out).toContain('contenteditable="false"');
      expect(out).toContain('data-atom-kind="sdt"');
      expect(out).toContain('data-atomid="A1B2C3D4"');
      expect(out).toContain('Content control');
      editor.destroy();
    });

    it('does not error loading a doc containing a block atom (FR-02 acceptance)', () => {
      expect(() => makeEditor(html).destroy()).not.toThrow();
    });
  });

  describe('inline atom — AppendAtom projection shape', () => {
    const html =
      '<p data-paraid="FEED0001">Hello <span class="compose-atom" data-atom-kind="field" contenteditable="false">1</span> World</p>';

    it('parses the server span into a composeInlineAtom, preserving order within the paragraph', () => {
      const editor = makeEditor(html);
      const paragraph = editor.state.doc.firstChild!;
      const childTypeNames: string[] = [];
      paragraph.forEach(node => childTypeNames.push(node.type.name));
      expect(childTypeNames).toEqual(['text', 'composeInlineAtom', 'text']);
      editor.destroy();
    });

    it("carries the CONTAINING paragraph's paraId + its own kind + display text as node attributes", () => {
      const editor = makeEditor(html);
      let atomNode: { attrs: Record<string, unknown> } | null = null;
      editor.state.doc.descendants(node => {
        if (node.type.name === 'composeInlineAtom') atomNode = node;
        return true;
      });
      expect(atomNode).not.toBeNull();
      const attrs = (atomNode as unknown as { attrs: Record<string, unknown> }).attrs;
      expect(attrs.paraId).toBe('FEED0001');
      expect(attrs.kind).toBe('field');
      expect(attrs.displayText).toBe('1');
      editor.destroy();
    });

    it('keeps the inherited paraId OFF the rendered DOM — hidden attribute only (mirrors FR-09)', () => {
      const editor = makeEditor(html);
      const out = editor.getHTML();
      expect(out).not.toContain('data-paraid="FEED0001"'); // not re-emitted on the atom span itself
      editor.destroy();
    });

    it('renders a visible, labeled, non-editable placeholder carrying the field value', () => {
      const editor = makeEditor(html);
      const out = editor.getHTML();
      expect(out).toContain('class="compose-atom compose-atom-inline"');
      expect(out).toContain('contenteditable="false"');
      expect(out).toContain('data-atom-kind="field"');
      expect(out).toContain('Field: 1');
      editor.destroy();
    });

    it('does not error loading a doc containing an inline field/SDT atom (FR-02 acceptance)', () => {
      expect(() => makeEditor(html).destroy()).not.toThrow();
    });
  });

  describe('non-editability — typing inside an atom is a no-op (negative case)', () => {
    it('a block atom has no interior content position to type into', () => {
      const html =
        '<p>Before</p><div class="compose-atom" data-atom-kind="sdt" data-atomid="A1B2C3D4" contenteditable="false"></div><p>After</p>';
      const editor = makeEditor(html);

      // Locate the atom's document position, then attempt to place the cursor ONE position deeper
      // (i.e. "inside" it) and type. An atom leaf node has no valid interior position — ProseMirror's
      // TextSelection resolution snaps to the nearest valid boundary, never inside the atom.
      let atomPos = -1;
      editor.state.doc.descendants((node, pos) => {
        if (node.type.name === 'composeBlockAtom') atomPos = pos;
        return true;
      });
      expect(atomPos).toBeGreaterThanOrEqual(0);

      const before = editor.getHTML();
      // Attempt to select "inside" the atom (pos + 1, its content offset) and insert text there.
      editor
        .chain()
        .setTextSelection(atomPos + 1)
        .insertContent('X')
        .run();
      const after = editor.getHTML();

      // The atom's own placeholder markup (kind + atomId) is unchanged — the typed character never
      // landed INSIDE the atom (it landed adjacent to it, a normal document edit, not a mutation of
      // the atom's content — the atom has no content to mutate).
      expect(after).toContain('data-atomid="A1B2C3D4"');
      expect(after).toContain('data-atom-kind="sdt"');
      // The placeholder's label text is unchanged — no stray "X" fused into the placeholder's own
      // rendered content (attribute order is not asserted; only the rendered label text is).
      expect(after).toContain('>Content control</div>');
      expect(after).not.toContain('XContent');
      expect(after).not.toContain('ContentX');
      void before;
      editor.destroy();
    });

    it('an inline atom has no interior content position to type into', () => {
      const html =
        '<p data-paraid="FEED0001">Hello <span class="compose-atom" data-atom-kind="field" contenteditable="false">1</span> World</p>';
      const editor = makeEditor(html);

      let atomPos = -1;
      editor.state.doc.descendants((node, pos) => {
        if (node.type.name === 'composeInlineAtom') atomPos = pos;
        return true;
      });
      expect(atomPos).toBeGreaterThanOrEqual(0);

      editor
        .chain()
        .setTextSelection(atomPos + 1)
        .insertContent('X')
        .run();
      const after = editor.getHTML();

      // The atom's rendered label is unchanged — "Field: 1", not "Field: 1X" or "Field: X1".
      expect(after).toContain('Field: 1');
      expect(after).not.toContain('Field: 1X');
      expect(after).not.toContain('Field: X1');
      editor.destroy();
    });
  });

  describe('ADR-021 — no inline hex/style leaks onto the rendered placeholder', () => {
    it('the block atom placeholder emits classes only, never inline style/hex', () => {
      const editor = makeEditor(
        '<div class="compose-atom" data-atom-kind="sdt" data-atomid="A1B2C3D4" contenteditable="false"></div>'
      );
      const out = editor.getHTML();
      expect(out).not.toContain('style=');
      expect(out).not.toMatch(/#[0-9a-fA-F]{3,6}/);
      editor.destroy();
    });

    it('the inline atom placeholder emits classes only, never inline style/hex', () => {
      const editor = makeEditor(
        '<p data-paraid="FEED0001"><span class="compose-atom" data-atom-kind="field" contenteditable="false">1</span></p>'
      );
      const out = editor.getHTML();
      expect(out).not.toContain('style=');
      expect(out).not.toMatch(/#[0-9a-fA-F]{3,6}/);
      editor.destroy();
    });
  });

  describe('unknown / unhandled atom kinds degrade gracefully', () => {
    it('falls back to the "unknown" label when data-atom-kind is missing an expected token', () => {
      const editor = makeEditor(
        '<div class="compose-atom" data-atom-kind="drawing" data-atomid="Z9Z9Z9Z9" contenteditable="false"></div>'
      );
      const out = editor.getHTML();
      expect(out).toContain('Unsupported content');
      editor.destroy();
    });
  });

  describe('exported node identities', () => {
    it('COMPOSE_R4_OPAQUE_ATOMS is exactly [ComposeBlockAtomNode, ComposeInlineAtomNode]', () => {
      expect(COMPOSE_R4_OPAQUE_ATOMS).toEqual([ComposeBlockAtomNode, ComposeInlineAtomNode]);
    });
  });
});
