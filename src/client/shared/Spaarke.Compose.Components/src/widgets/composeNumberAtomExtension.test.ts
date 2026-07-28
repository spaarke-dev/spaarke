/**
 * composeNumberAtomExtension.test.ts — FR-13/FR-14 (spaarkeai-compose-fidelity-r4.5 task 032, WS-3
 * render). Headless `@tiptap/core` `Editor` tests (same convention as `ComposeEditor.paraId.test.tsx` /
 * `opaqueAtomNode`-style schema suites) — exercises the REAL `COMPOSE_NUMBER_ATOM` extension through the
 * exact schema-registration path `ComposeEditor.tsx` mounts, minus the React/auth surface. ProseMirror's
 * `EditorView` renders real DOM (via jsdom) even headless, so `editor.view.dom` assertions below observe
 * genuine rendered output, not a mock.
 *
 * These are the `<ui-tests>` this task's POML specifies, adapted to a CI-shaped jest file (Chrome-driven
 * `ui-test` skill verification is impractical inside this repo's automated suite) — the same adaptation
 * task 021's `ComposeEditor.indentAndWhitespace.test.tsx` made for FR-07/FR-08. Covers: (1) numbers
 * render identical to what the server computed (decimal/letter/roman/multi-level/"Article I" — no
 * client-side reformatting), (2) an interrupted run's continuation renders verbatim (no client restart —
 * the server, task 031, already owns "no restart at 1"; this proves the CLIENT does not fight that), (3)
 * the atom is non-editable and does NOT participate in the document model / tracked-edit stream (FR-14).
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_NUMBER_ATOM, buildNumberAtomDecorations } from './composeNumberAtomExtension';
import { COMPOSE_R3_PARAID } from './paraIdExtension';

/** Build a headless editor with the production number-atom extension (+ paraId, for decoration-key coverage). */
function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID, ...COMPOSE_NUMBER_ATOM],
    content,
  });
}

describe('ComposeNumberAtomExtension — schema attributes (FR-13)', () => {
  it('registers computedNumber/numberingLevel global attributes on paragraph + heading', () => {
    const editor = makeEditor();
    expect(editor.schema.nodes.paragraph.spec.attrs).toHaveProperty('computedNumber');
    expect(editor.schema.nodes.paragraph.spec.attrs).toHaveProperty('numberingLevel');
    expect(editor.schema.nodes.heading.spec.attrs).toHaveProperty('computedNumber');
    editor.destroy();
  });

  it('parses data-computed-number/data-numbering-level from the projected HTML and re-emits them on getHTML() (mirrors composeIndentExtension\'s round-trip)', () => {
    const editor = makeEditor('<p data-computed-number="4.2." data-numbering-level="1">Clause text</p>');
    let found: string | null = null;
    let level: string | null = null;
    editor.state.doc.descendants(node => {
      if (node.type.name === 'paragraph') {
        found = node.attrs.computedNumber as string | null;
        level = node.attrs.numberingLevel as string | null;
      }
      return true;
    });
    expect(found).toBe('4.2.');
    expect(level).toBe('1');
    expect(editor.getHTML()).toContain('data-computed-number="4.2."');
    expect(editor.getHTML()).toContain('data-numbering-level="1"');
    editor.destroy();
  });

  it('an unnumbered paragraph carries no computedNumber attribute (the projection never emits the data attribute for it)', () => {
    const editor = makeEditor('<p>Plain paragraph, no legal number</p>');
    let found: unknown;
    editor.state.doc.descendants(node => {
      if (node.type.name === 'paragraph') found = node.attrs.computedNumber;
      return true;
    });
    expect(found).toBeNull();
    editor.destroy();
  });
});

describe('ComposeNumberAtomExtension — explicit non-editable render (FR-13)', () => {
  it('renders the 031-computed label as a non-editable prefix atom in the live DOM', () => {
    const editor = makeEditor('<p data-computed-number="4.2." data-numbering-level="0">Clause text</p>');
    const atom = editor.view.dom.querySelector('.compose-number-atom');
    expect(atom).not.toBeNull();
    expect(atom?.textContent).toBe('4.2.');
    expect(atom?.getAttribute('contenteditable')).toBe('false');
    editor.destroy();
  });

  it('renders letters / roman / multi-level / "Article I" style-linked labels exactly as the server computed them — no client-side reformatting', () => {
    const editor = makeEditor(
      '<p data-computed-number="a.">Letter item</p>' +
        '<p data-computed-number="iv.">Roman item</p>' +
        '<p data-computed-number="4.2.1">Multi-level sub-item</p>' +
        '<h1 data-computed-number="Article I">Style-linked heading</h1>'
    );
    const labels = Array.from(editor.view.dom.querySelectorAll('.compose-number-atom')).map(el => el.textContent);
    expect(labels).toEqual(['a.', 'iv.', '4.2.1', 'Article I']);
    editor.destroy();
  });

  it('renders an interrupted run\'s continuation verbatim — the client performs NO auto-count/restart of its own (F-3 invariant; server task 031 owns the "does not restart at 1" guarantee)', () => {
    const editor = makeEditor(
      '<p data-computed-number="3.">Third clause</p>' +
        '<h2>Unrelated heading (interruption)</h2>' +
        '<p data-computed-number="4.">Fourth clause — continues, does not restart at 1</p>'
    );
    const labels = Array.from(editor.view.dom.querySelectorAll('.compose-number-atom')).map(el => el.textContent);
    expect(labels).toEqual(['3.', '4.']);
    editor.destroy();
  });

  it('a bulleted (unnumbered) list item carries no atom — bullets are unaffected (no regression)', () => {
    const editor = makeEditor('<ul><li><p>Bullet item, no legal number</p></li></ul>');
    expect(editor.view.dom.querySelector('.compose-number-atom')).toBeNull();
    // The bullet's own text still renders — this task must not regress ordinary list rendering.
    expect(editor.getText()).toContain('Bullet item, no legal number');
    editor.destroy();
  });
});

describe('ComposeNumberAtomExtension — read-time-only boundary (FR-14, escalation-guard)', () => {
  it('the number label is NOT part of the ProseMirror document text — a decoration, never a doc node', () => {
    const editor = makeEditor('<p data-computed-number="4.2." data-numbering-level="0">Clause text</p>');
    // getText() reads the DOC MODEL only — decorations are a pure VIEW-layer overlay, invisible to it.
    // If this ever contained "4.2.", the atom would have become real editable content (the R5 G3
    // escalation boundary this task's POML calls out).
    expect(editor.getText()).toBe('Clause text');
    expect(editor.getText()).not.toContain('4.2.');
    editor.destroy();
  });

  it('getJSON() carries computedNumber only as an inert NODE ATTRIBUTE (metadata), never as a "text" node interleaved with real content — the doc has no separate atom node at all', () => {
    const editor = makeEditor('<p data-computed-number="4.2." data-numbering-level="0">Clause text</p>');
    const json = editor.getJSON();
    const paragraph = json.content?.[0];
    // The label rides as an attribute (exactly like paraId/indent do) — never as its own content-model
    // "text"/"node" entry that a track-changes word-diff (which walks `node.textContent`) could see.
    expect(paragraph?.attrs?.computedNumber).toBe('4.2.');
    expect(paragraph?.content).toEqual([{ type: 'text', text: 'Clause text' }]);
    editor.destroy();
  });

  it('a transaction against the document does not alter or remove the computed-number attribute the atom is sourced from (read-only, no auto-renumber on edit)', () => {
    const editor = makeEditor('<p data-computed-number="4.2." data-numbering-level="0">Clause text</p>');
    // Insert INSIDE the existing paragraph's text (not at the absolute doc end, which could create a
    // new, attribute-less paragraph and mask what this test is actually checking).
    editor.commands.insertContentAt(2, 'PREFIX ');
    let found: string | null = null;
    let paragraphCount = 0;
    editor.state.doc.descendants(node => {
      if (node.type.name === 'paragraph') {
        found = node.attrs.computedNumber as string | null;
        paragraphCount++;
      }
      return true;
    });
    expect(paragraphCount).toBe(1); // still one paragraph — proves the edit landed inline, not as a new node.
    expect(editor.getText()).toContain('PREFIX');
    // FR-14: read-time only — an ordinary edit to the paragraph's text does NOT trigger any
    // client-side recompute/mutation of the label the server supplied at load time.
    expect(found).toBe('4.2.');
    editor.destroy();
  });
});

describe('buildNumberAtomDecorations — pure decoration-set builder', () => {
  it('emits exactly one widget decoration per numbered paragraph, none for unnumbered ones', () => {
    const editor = makeEditor(
      '<p data-computed-number="1.">One</p><p>Unnumbered</p><p data-computed-number="2.">Two</p>'
    );
    const decorations = buildNumberAtomDecorations(editor.state.doc).find();
    expect(decorations).toHaveLength(2);
    editor.destroy();
  });

  it('returns an empty set for a document with no numbered paragraphs', () => {
    const editor = makeEditor('<p>Alpha</p><p>Beta</p>');
    const decorations = buildNumberAtomDecorations(editor.state.doc).find();
    expect(decorations).toHaveLength(0);
    editor.destroy();
  });
});
