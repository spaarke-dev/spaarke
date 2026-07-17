/**
 * ComposeEditor.paraId.test.tsx — R3 FR-09/FR-10 (spaarkeai-compose-r3 task 011).
 *
 * Exercises the REAL `COMPOSE_R3_PARAID` extension (the `@tiptap/extension-unique-id`
 * config exported from ComposeEditor) through a HEADLESS TipTap `Editor` — the same
 * schema-registration path ComposeEditor mounts, minus the React surface (mirrors
 * `marks/marks.test.ts`). Protects the FR-09/FR-10 invariants:
 *   - server ids stamp onto paragraph nodes in document order after setContent (FR-09);
 *   - the paraId is a HIDDEN node attribute — never rendered to the DOM (FR-09);
 *   - a paragraph split keeps one id + re-mints the other OOXML-valid, via the
 *     extension's built-in dedup with NO custom plugin (FR-10);
 *   - untouched paragraphs keep their ids across unrelated edits.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
// The paraId extension is factored into its own pure schema module (paraIdExtension.ts)
// so this headless suite imports it WITHOUT dragging in ComposeEditor's React/auth graph.
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { stampParaIds } from '../utils/docxBridge';
import type { ParaIdMapEntry } from '../types/compose-contracts';

/** Build a headless editor with the production paraId extension + table support. */
function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [StarterKit, Table, TableRow, TableHeader, TableCell, ...COMPOSE_R3_PARAID],
    content,
  });
}

/** Collect each paraId-bearing node's `paraId` attribute in document order (paragraph + heading). */
function paragraphIds(editor: Editor): Array<string | null> {
  const ids: Array<string | null> = [];
  editor.state.doc.descendants(node => {
    if (node.type.name === 'paragraph' || node.type.name === 'heading') {
      ids.push((node.attrs.paraId as string | null) ?? null);
    }
    return true;
  });
  return ids;
}

const OOXML_ID = /^[0-9A-F]{8}$/;

describe('COMPOSE_R3_PARAID — server-id carry + split minting (FR-09/FR-10)', () => {
  it('registers the paraId global attribute on the paragraph node schema', () => {
    const editor = makeEditor('<p>hello</p>');
    expect(editor.schema.nodes.paragraph.spec.attrs).toHaveProperty('paraId');
    editor.destroy();
  });

  it('stamps the server paraId map in document order — headings + table cells count as paragraphs (FR-09)', () => {
    const editor = makeEditor();
    // A HEADING (an OOXML `<w:p>` → counted by the server map), a paragraph, two
    // table-cell paragraphs, and a trailing paragraph. ProseMirror document order
    // (like the server body.Descendants<Paragraph>()) is H,A,B,C,D — the heading
    // MUST occupy index 0 or every subsequent id misaligns.
    editor.commands.setContent(
      '<h2>H</h2><p>A</p><table><tbody><tr><td><p>B</p></td><td><p>C</p></td></tr></tbody></table><p>D</p>'
    );
    const map: ParaIdMapEntry[] = [
      { index: 0, paraId: 'AAAA0001', isMinted: false }, // heading H
      { index: 1, paraId: 'BBBB0002', isMinted: false }, // paragraph A
      { index: 2, paraId: 'CCCC0003', isMinted: true }, // cell paragraph B
      { index: 3, paraId: 'DDDD0004', isMinted: false }, // cell paragraph C
      { index: 4, paraId: 'EEEE0005', isMinted: false }, // paragraph D
    ];
    stampParaIds(editor, map);

    expect(paragraphIds(editor)).toEqual(['AAAA0001', 'BBBB0002', 'CCCC0003', 'DDDD0004', 'EEEE0005']);
    editor.destroy();
  });

  it('keeps the paraId OFF the rendered DOM — hidden node attribute only (FR-09)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>alpha</p><p>beta</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'DEADBEEF', isMinted: false },
      { index: 1, paraId: 'FEED0001', isMinted: false },
    ]);

    const html = editor.getHTML();
    // The ids are carried on the ProseMirror nodes...
    expect(paragraphIds(editor)).toEqual(['DEADBEEF', 'FEED0001']);
    // ...but never emitted to the DOM (no attribute, no id value in the markup).
    expect(html).not.toContain('paraId');
    expect(html).not.toContain('data-para');
    expect(html).not.toContain('DEADBEEF');
    expect(html).not.toContain('FEED0001');
    editor.destroy();
  });

  it('splitting a paragraph keeps one id and re-mints the other OOXML-valid (FR-10, built-in dedup)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>hello world</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);
    expect(paragraphIds(editor)).toEqual(['AAAA0001']);

    // Place the caret after "hello" (pos 6) and split — no custom plugin, the
    // extension's appendTransaction dedup re-mints the duplicated half.
    editor.chain().setTextSelection(6).splitBlock().run();

    const ids = paragraphIds(editor);
    expect(ids).toHaveLength(2);
    const [first, second] = ids as [string, string];
    // Two DISTINCT ids; exactly one equals the original.
    expect(first).not.toEqual(second);
    expect([first, second]).toContain('AAAA0001');
    // The re-minted half is a valid OOXML w14:paraId (8-hex, < 0x80000000).
    const minted = first === 'AAAA0001' ? second : first;
    expect(minted).toMatch(OOXML_ID);
    expect(parseInt(minted, 16)).toBeLessThan(0x80000000);
    expect(parseInt(minted, 16)).toBeGreaterThan(0);
    editor.destroy();
  });

  it('untouched paragraphs keep their paraIds across an unrelated edit', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>alpha</p><p>beta</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);
    expect(paragraphIds(editor)).toEqual(['AAAA0001', 'BBBB0002']);

    // Type into the FIRST paragraph (unrelated to the second). Neither paragraph
    // is split, so both keep their ids — the second is entirely untouched.
    editor.chain().setTextSelection(3).insertContent('X').run();

    expect(paragraphIds(editor)).toEqual(['AAAA0001', 'BBBB0002']);
    editor.destroy();
  });

  it('mints OOXML-valid ids for paragraphs the server map does not cover (new/seed content)', () => {
    // No stamp: the extension's own appendTransaction minting supplies ids (the
    // AI-drafted-seed path, where there is no server pre-parse map). Mount via
    // setContent — exactly how ComposeEditor mounts an `initialHtml` seed.
    const editor = makeEditor();
    editor.commands.setContent('<p>seed one</p><p>seed two</p>');
    const ids = paragraphIds(editor);
    expect(ids).toHaveLength(2);
    for (const id of ids) {
      expect(id).toMatch(OOXML_ID);
      expect(parseInt(id as string, 16)).toBeLessThan(0x80000000);
    }
    // Independently minted → distinct.
    expect(ids[0]).not.toEqual(ids[1]);
    editor.destroy();
  });
});
