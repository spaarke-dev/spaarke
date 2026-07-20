/**
 * docxBridge.contentModel.test.ts — R3 FR-01/FR-01a (spaarkeai-compose-r3 task 027).
 *
 * Exercises the CLIENT content-model export path through a HEADLESS TipTap `Editor` (the same schema
 * ComposeEditor mounts, minus React) — the replacement for the removed `docx.js` byte serializers:
 *   - `captureParaIdSnapshot` — load-time `{ paraId → reject-state text }` map;
 *   - `collectEditedParagraphs` — the dirty paragraphs (paraId-keyed), diffed against the snapshot, with
 *     REJECT-STATE semantics (a pending AI-insertion mark does NOT count as a settled-text change) and the
 *     existing-paraId-only rule (a new/split paraId is not emitted — out of E1 delta scope);
 *   - `buildContentModel` — the full paraId-keyed model (headings/paragraphs/lists/tables + b/i/u runs)
 *     the server renders for a born-in-editor save.
 * This is also where the reject-state reduction (formerly `buildRejectBaselineJson`) is now covered.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import { COMPOSE_R3_PARAID } from '../widgets/paraIdExtension';
import { InsertionMark } from '../widgets/marks/InsertionMark';
import { DeletionMark } from '../widgets/marks/DeletionMark';
import { stampParaIds, captureParaIdSnapshot, collectEditedParagraphs, buildContentModel } from './docxBridge';
import type { ParaIdMapEntry } from '../types/compose-contracts';

function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [
      StarterKit,
      Underline,
      Table,
      TableRow,
      TableHeader,
      TableCell,
      InsertionMark,
      DeletionMark,
      ...COMPOSE_R3_PARAID,
    ],
    content,
  });
}

describe('captureParaIdSnapshot + collectEditedParagraphs (FR-01)', () => {
  it('emits only paragraphs whose settled text CHANGED, keyed by paraId; unchanged ones are omitted', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>First clause.</p><p>Second clause.</p><p>Third clause.</p>');
    const map: ParaIdMapEntry[] = [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
      { index: 2, paraId: 'CCCC0003', isMinted: false },
    ];
    stampParaIds(editor, map);
    const snapshot = captureParaIdSnapshot(editor);

    // Edit ONLY the second paragraph's text.
    editor.commands.setContent('<p>First clause.</p><p>Second clause, amended.</p><p>Third clause.</p>');
    stampParaIds(editor, map); // re-stamp same ids (setContent re-created nodes)

    const edited = collectEditedParagraphs(editor, snapshot);
    expect(edited).toEqual([{ paraId: 'BBBB0002', text: 'Second clause, amended.' }]);
    editor.destroy();
  });

  it('does NOT treat a pending AI-insertion (NEW proposed text) as a settled-text change (reject-state parity)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The Supplier shall indemnify the Customer.</p>');
    const map: ParaIdMapEntry[] = [{ index: 0, paraId: 'AAAA0001', isMinted: false }];
    stampParaIds(editor, map);
    const snapshot = captureParaIdSnapshot(editor);

    // INSERT new proposed text carrying a pending-insertion mark (an AI suggestion, not yet accepted).
    // Reject-state drops it → the settled text is still the original → no edited-paragraph delta (the
    // suggestion rides the `annotations` list, composed server-side — task 023).
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent({
      type: 'text',
      text: ' promptly',
      marks: [{ type: 'insertion', attrs: { ledgerRef: 'b1@t1', binding: 'b1' } }],
    });

    const edited = collectEditedParagraphs(editor, snapshot);
    expect(edited).toEqual([]);
    editor.destroy();
  });

  it('does NOT emit a brand-new paraId (split/insert is out of E1 delta scope)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Original.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);
    const snapshot = captureParaIdSnapshot(editor);

    // A new paragraph with an id the snapshot never saw.
    editor.commands.setContent('<p>Original.</p><p>Inserted.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'FFFF9999', isMinted: true },
    ]);

    const edited = collectEditedParagraphs(editor, snapshot);
    // Original unchanged, inserted paraId not in snapshot → nothing emitted.
    expect(edited).toEqual([]);
    editor.destroy();
  });
});

describe('buildContentModel (FR-01a)', () => {
  it('maps headings/paragraphs with level + inline bold/italic/underline runs', () => {
    const editor = makeEditor();
    editor.commands.setContent('<h1>Definitions</h1><p>Plain <strong>bold</strong> <em>italic</em> <u>under</u>.</p>');
    const model = buildContentModel(editor);

    expect(model.blocks[0]).toMatchObject({ kind: 'Heading', level: 1 });
    expect(model.blocks[0].runs).toEqual([{ text: 'Definitions' }]);

    const para = model.blocks[1];
    expect(para.kind).toBe('Paragraph');
    expect(para.runs).toEqual([
      { text: 'Plain ' },
      { text: 'bold', bold: true },
      { text: ' ' },
      { text: 'italic', italic: true },
      { text: ' ' },
      { text: 'under', underline: true },
      { text: '.' },
    ]);
    editor.destroy();
  });

  it('flattens ordered/bullet lists into ListItem blocks with ordered + nesting level', () => {
    const editor = makeEditor();
    editor.commands.setContent('<ol><li><p>one</p></li><li><p>two</p></li></ol><ul><li><p>bullet</p></li></ul>');
    const model = buildContentModel(editor);

    const listItems = model.blocks.filter(b => b.kind === 'ListItem');
    expect(listItems).toHaveLength(3);
    expect(listItems[0]).toMatchObject({ kind: 'ListItem', ordered: true, level: 0, startsNewList: true });
    expect(listItems[1]).toMatchObject({ ordered: true, level: 0, startsNewList: false });
    expect(listItems[2]).toMatchObject({ ordered: false, level: 0 });
    editor.destroy();
  });

  it('maps a native table to a Table block with header cells + cell paragraphs', () => {
    const editor = makeEditor();
    editor.commands.setContent(
      '<table><tbody><tr><th><p>Term</p></th><th><p>Meaning</p></th></tr><tr><td><p>Affiliate</p></td><td><p>A controlled entity.</p></td></tr></tbody></table>'
    );
    const model = buildContentModel(editor);

    const table = model.blocks.find(b => b.kind === 'Table')!;
    expect(table.table!.rows).toHaveLength(2);
    expect(table.table!.rows[0].cells[0].isHeader).toBe(true);
    expect(table.table!.rows[1].cells[0].blocks[0].runs).toEqual([{ text: 'Affiliate' }]);
    editor.destroy();
  });

  it('excludes pending-insertion text from the content model (reject-state parity)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Keep this.</p>');
    editor.commands.setTextSelection({ from: 1, to: 5 }); // "Keep"
    editor.commands.setMark('insertion', { ledgerRef: 'b1@t1', binding: 'b1' });

    const model = buildContentModel(editor);
    // "Keep" carried a pending-insertion mark → dropped; only " this." survives.
    const text = (model.blocks[0].runs ?? []).map(r => r.text).join('');
    expect(text).toBe(' this.');
    editor.destroy();
  });
});
