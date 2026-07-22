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
import {
  stampParaIds,
  captureParaIdSnapshot,
  collectEditedParagraphs,
  buildContentModel,
  buildBaselineParaIdMap,
} from './docxBridge';
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

  it('emits a pending AI-insertion in ACCEPT-STATE so it persists via the position-based synthesizer (round-4 save fix)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The Supplier shall indemnify the Customer.</p>');
    const map: ParaIdMapEntry[] = [{ index: 0, paraId: 'AAAA0001', isMinted: false }];
    stampParaIds(editor, map);
    const snapshot = captureParaIdSnapshot(editor);

    // INSERT new proposed text carrying a pending-insertion mark (an AI suggestion, not yet accepted).
    // Round-4 save-robustness fix: the delta is now ACCEPT-STATE (insertion kept), so the paragraph IS
    // emitted with the suggested text → the server synthesizer diffs it against the retained original by
    // paraId and emits a `w:ins` tracked change. No server text-search, so no "target not found" 422.
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent({
      type: 'text',
      text: ' promptly',
      marks: [{ type: 'insertion', attrs: { ledgerRef: 'b1@t1', binding: 'b1' } }],
    });

    const edited = collectEditedParagraphs(editor, snapshot);
    expect(edited).toEqual([{ paraId: 'AAAA0001', text: 'The Supplier shall indemnify the Customer. promptly' }]);
    editor.destroy();
  });

  it('an IMPORTED native insertion is NOT emitted (it rides the retained-original bytes — no double-synthesis)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The Supplier shall indemnify the Customer.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);
    const snapshot = captureParaIdSnapshot(editor);

    // An IMPORTED revision (ledgerRef `imported:*`) is already a w:ins in the retained original. Accept-
    // state mirrors reject-state for imported marks, so the paragraph's accept-state == its baseline
    // snapshot → nothing emitted → the synthesizer does not re-create (double) it.
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent({
      type: 'text',
      text: ' promptly',
      marks: [{ type: 'insertion', attrs: { ledgerRef: 'imported:rev-7' } }],
    });

    const edited = collectEditedParagraphs(editor, snapshot);
    expect(edited).toEqual([]);
    editor.destroy();
  });

  it('emits a whole-paragraph DELETION (empty text) for a load-time paraId removed from the current doc', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Keep me.</p><p>Delete me.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);
    const snapshot = captureParaIdSnapshot(editor);

    // The second paragraph is gone from the current doc (a restructuring edit removed it).
    editor.commands.setContent('<p>Keep me.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const edited = collectEditedParagraphs(editor, snapshot);
    // The vanished load-time paraId is emitted with empty text → synthesizer strikes it (no leftover).
    expect(edited).toContainEqual({ paraId: 'BBBB0002', text: '' });
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

// C2 fix (UAT 2026-07-20) — the baseline paraId map the save sends so the server can stamp minted ids
// physically onto the retained-original baseline. Pure over a snapshot Map (no editor needed).
describe('buildBaselineParaIdMap — the C2 save-time paraId map (UAT 2026-07-20)', () => {
  it('projects the load-time snapshot to ordered {index, paraId, text} entries in document order', () => {
    const snapshot = new Map<string, string>([
      ['1A2B3C4D', 'First clause.'],
      ['1E5EC15C', 'Second clause the source left id-less.'],
      ['0FF1CE12', 'Third clause.'],
    ]);

    const map = buildBaselineParaIdMap(snapshot);

    expect(map).toEqual([
      { index: 0, paraId: '1A2B3C4D', text: 'First clause.' },
      { index: 1, paraId: '1E5EC15C', text: 'Second clause the source left id-less.' },
      { index: 2, paraId: '0FF1CE12', text: 'Third clause.' },
    ]);
  });

  it('returns [] for an absent or empty snapshot (born-in-editor — the server renders its ids)', () => {
    expect(buildBaselineParaIdMap(null)).toEqual([]);
    expect(buildBaselineParaIdMap(undefined)).toEqual([]);
    expect(buildBaselineParaIdMap(new Map())).toEqual([]);
  });
});
