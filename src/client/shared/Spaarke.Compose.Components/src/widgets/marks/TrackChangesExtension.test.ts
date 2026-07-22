/**
 * TrackChangesExtension.test.ts — item 4 (UAT round-4) live Track Changes decoration overlay.
 *
 * Drives `buildTrackChangeDecorations` (the pure doc→DecorationSet core) against a REAL headless
 * TipTap editor whose paragraphs carry `paraId` attrs (the same identity extension ComposeEditor
 * mounts), plus the plugin's enabled/disabled behavior. The decoration is a VIEW layer — these tests
 * assert it surfaces the right ranges WITHOUT mutating document content (the property that lets user
 * edits persist via the existing collectEditedParagraphs → FR-02 path).
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_R3_PARAID } from '../paraIdExtension';
import { InsertionMark } from './InsertionMark';
import { DeletionMark } from './DeletionMark';
import { TrackChangesExtension, trackChangesPluginKey, buildTrackChangeDecorations } from './TrackChangesExtension';

function makeEditor(content: string): Editor {
  return new Editor({
    // Include the AI-redline marks (production ComposeEditor has them) so the "skip AI-marked block" path
    // is exercised with a real insertion/deletion mark in the schema.
    extensions: [StarterKit, InsertionMark, DeletionMark, ...COMPOSE_R3_PARAID, TrackChangesExtension],
    content,
  });
}

/** Force a known paraId onto the FIRST text block and return it. */
function stampFirstParaId(editor: Editor, paraId: string): void {
  let pos = -1;
  editor.state.doc.descendants((node, p) => {
    if ((node.type.name === 'paragraph' || node.type.name === 'heading') && pos === -1) pos = p;
    return true;
  });
  const node = editor.state.doc.nodeAt(pos)!;
  editor.view.dispatch(editor.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, paraId }));
}

function firstBlockText(editor: Editor): string {
  let text = '';
  editor.state.doc.descendants(node => {
    if ((node.type.name === 'paragraph' || node.type.name === 'heading') && text === '') text = node.textContent;
    return true;
  });
  return text;
}

describe('buildTrackChangeDecorations (doc vs baseline → DecorationSet)', () => {
  it('produces NO decorations when the baseline map is empty', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    const set = buildTrackChangeDecorations(editor.state.doc, new Map());
    expect(set.find()).toHaveLength(0);
    editor.destroy();
  });

  it('produces NO decorations when the paragraph is unchanged vs its baseline', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');
    const baseline = new Map([['AAAAAAA1', firstBlockText(editor)]]);
    const set = buildTrackChangeDecorations(editor.state.doc, baseline);
    expect(set.find()).toHaveLength(0);
    editor.destroy();
  });

  it('decorates an INSERTION over the added span (current text longer than baseline)', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');
    // Baseline lacked "quick " — the current doc added it.
    const baseline = new Map([['AAAAAAA1', 'the brown fox']]);
    const set = buildTrackChangeDecorations(editor.state.doc, baseline);
    const decos = set.find();
    // At least one decoration; an inline insertion span covering "quick " (offset 4, length 6).
    expect(decos.length).toBeGreaterThanOrEqual(1);
    const contentStart = 1; // paragraph at pos 0 → content starts at 1
    const insertion = decos.find(d => d.from === contentStart + 4 && d.to === contentStart + 4 + 'quick '.length);
    expect(insertion).toBeTruthy();
    editor.destroy();
  });

  it('decorates a DELETION as a widget (baseline had text the current doc lacks)', () => {
    const editor = makeEditor('<p>the brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');
    // Baseline HAD "quick " which the current doc no longer has → a widget decoration (from === to).
    const baseline = new Map([['AAAAAAA1', 'the quick brown fox']]);
    const set = buildTrackChangeDecorations(editor.state.doc, baseline);
    const decos = set.find();
    const widget = decos.find(d => d.from === d.to); // widget decorations are zero-width
    expect(widget).toBeTruthy();
    editor.destroy();
  });

  it('does NOT decorate a block whose paraId is absent from the baseline (a new/split paragraph)', () => {
    const editor = makeEditor('<p>a brand new paragraph</p>');
    stampFirstParaId(editor, 'NEWPARA1');
    // Baseline only knows a DIFFERENT paragraph id.
    const baseline = new Map([['OTHERID1', 'some other baseline text']]);
    const set = buildTrackChangeDecorations(editor.state.doc, baseline);
    expect(set.find()).toHaveLength(0);
    editor.destroy();
  });

  it('does NOT decorate a block that already carries an AI-suggestion redline mark (no double-draw; lightbulb stays visible)', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');
    // Apply an insertion mark (an AI suggestion) to part of the block.
    const insMark = editor.state.schema.marks.insertion;
    if (insMark) {
      editor.view.dispatch(editor.state.tr.addMark(5, 10, insMark.create({ ledgerRef: 'b1@t1' })));
    }
    // Even though the current text differs from this shorter baseline, the block is skipped because it
    // carries an AI redline mark (it renders via that mark + the rationale lightbulb).
    const set = buildTrackChangeDecorations(editor.state.doc, new Map([['AAAAAAA1', 'the brown fox']]));
    expect(set.find()).toHaveLength(0);
    editor.destroy();
  });

  it('is a pure VIEW layer — building decorations does not change document content', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');
    const before = editor.getHTML();
    buildTrackChangeDecorations(editor.state.doc, new Map([['AAAAAAA1', 'the brown fox']]));
    expect(editor.getHTML()).toBe(before);
    editor.destroy();
  });
});

describe('TrackChangesExtension plugin (enabled state via meta)', () => {
  it('starts disabled and flips enabled/off when the toggle dispatches its meta', () => {
    const editor = makeEditor('<p>the quick brown fox</p>');
    stampFirstParaId(editor, 'AAAAAAA1');

    // initialEnabled is false → the overlay is off until the toolbar toggle turns it on.
    expect(trackChangesPluginKey.getState(editor.state)?.enabled).toBe(false);

    editor.view.dispatch(editor.state.tr.setMeta(trackChangesPluginKey, { enabled: true }));
    expect(trackChangesPluginKey.getState(editor.state)?.enabled).toBe(true);

    editor.view.dispatch(editor.state.tr.setMeta(trackChangesPluginKey, { enabled: false }));
    expect(trackChangesPluginKey.getState(editor.state)?.enabled).toBe(false);
    editor.destroy();
  });
});
