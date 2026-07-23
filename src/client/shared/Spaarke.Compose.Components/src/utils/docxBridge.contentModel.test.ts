/**
 * docxBridge.contentModel.test.ts — R3 FR-01/FR-01a (spaarkeai-compose-r3 task 027).
 *
 * Exercises the CLIENT content-model export path through a HEADLESS TipTap `Editor` (the same schema
 * ComposeEditor mounts, minus React) — the replacement for the removed `docx.js` byte serializers:
 *   - `captureParaIdSnapshot` — load-time `{ paraId → reject-state text }` map (feeds the C2 minted-id
 *     stamp + the live Track Changes decoration baseline; R4 task 023 removed the paragraph-diff export
 *     `collectEditedParagraphs` that used to diff against it — dirty-save capture now routes only through
 *     the step interceptor's operation log, covered by `stepOperationInterceptor.test.ts`);
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

describe('captureParaIdSnapshot (FR-01) — reject-state text per paraId', () => {
  it('captures each paragraph\'s current settled text keyed by paraId, in document order', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>First clause.</p><p>Second clause.</p><p>Third clause.</p>');
    const map: ParaIdMapEntry[] = [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
      { index: 2, paraId: 'CCCC0003', isMinted: false },
    ];
    stampParaIds(editor, map);

    const snapshot = captureParaIdSnapshot(editor);
    expect(Array.from(snapshot.entries())).toEqual([
      ['AAAA0001', 'First clause.'],
      ['BBBB0002', 'Second clause.'],
      ['CCCC0003', 'Third clause.'],
    ]);
    editor.destroy();
  });

  it('excludes a PENDING AI-insertion mark from the reject-state text (round-4 baseline semantics)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>The Supplier shall indemnify the Customer.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    // INSERT new proposed text carrying a pending-insertion mark (an AI suggestion, not yet accepted).
    // Reject-state treats it as NOT YET settled — the snapshot the op-log/Track-Changes baseline diffs
    // against must not bake in an un-accepted suggestion.
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent({
      type: 'text',
      text: ' promptly',
      marks: [{ type: 'insertion', attrs: { ledgerRef: 'b1@t1', binding: 'b1' } }],
    });

    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe('The Supplier shall indemnify the Customer.');
    editor.destroy();
  });

  it('keeps deletion-marked original text in the reject-state snapshot (a pending deletion is not yet settled)', () => {
    const editor = makeEditor();
    editor.commands.setContent('<p>Keep this clause intact.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    editor.commands.setTextSelection({ from: 6, to: 10 }); // "this"
    editor.commands.setMark('deletion', { ledgerRef: 'b1@t1', binding: 'b1' });

    const snapshot = captureParaIdSnapshot(editor);
    // A pending (not-yet-accepted) deletion suggestion — reject-state keeps the original text.
    expect(snapshot.get('AAAA0001')).toBe('Keep this clause intact.');
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
