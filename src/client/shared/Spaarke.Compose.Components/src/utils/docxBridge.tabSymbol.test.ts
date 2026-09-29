/**
 * docxBridge.tabSymbol.test.ts — task 048 (spaarkeai-compose-r8).
 *
 * Tabs and symbol-font glyphs used to be lost the moment a user edited the paragraph holding them. Both
 * reached the editor looking like ordinary text — a tab as an em space, a symbol as its resolved glyph — so
 * the mapper had no way to tell them apart from something typed, and rebuilt both as plain runs. A
 * definitions list lost its alignment; a Symbol-font § became a look-alike character.
 *
 * The fix gives each an IDENTITY without changing its APPEARANCE: the server now wraps both in the
 * `composeInlineAtom` node it already used for fields and content controls, and the mapper returns them as
 * `isTab` / `symbol` marker runs. This file asserts the round trip, and — just as importantly — asserts the
 * things that must NOT have changed:
 *
 *   * the text coordinate space (an atom contributes exactly the one character it did as plain text), and
 *   * `formattingUnchanged`'s two char streams staying aligned, so a paragraph containing a tab that the
 *     user never touched is still passed through by object identity rather than rebuilt.
 *
 * That second one is the regression this change could most plausibly have caused, which is why it is
 * measured here rather than assumed.
 *
 * @see ../widgets/opaqueAtomNode.ts (the node, and `atomRendersAsItself`)
 * @see ../../../../../src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs (AppendAtom)
 * @see docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md (the loss list these two rows left)
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Table from '@tiptap/extension-table';
import TableRow from '@tiptap/extension-table-row';
import TableHeader from '@tiptap/extension-table-header';
import TableCell from '@tiptap/extension-table-cell';
import { InsertionMark } from '../widgets/marks/InsertionMark';
import { DeletionMark } from '../widgets/marks/DeletionMark';
import { CommentAnchorMark } from '../widgets/marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from '../widgets/paraIdExtension';
import { COMPOSE_R4_OPAQUE_ATOMS } from '../widgets/opaqueAtomNode';
import { stampParaIds, captureParaIdSnapshot, buildImportedContentModel } from './docxBridge';
import type { ComposeContentModel, ParaIdMapEntry } from '../types/compose-contracts';

/**
 * Mirrors the REAL editor's extension list (`ComposeEditor.tsx`) including the opaque atoms — without them
 * the server's atom markup parses as a bare span and the test would measure a schema that does not ship.
 */
function makeEditor(content = '<p></p>'): Editor {
  return new Editor({
    extensions: [
      StarterKit,
      Underline,
      Link.configure({ openOnClick: false, autolink: false }),
      Table,
      TableRow,
      TableHeader,
      TableCell,
      InsertionMark,
      DeletionMark,
      CommentAnchorMark,
      ...COMPOSE_R3_PARAID,
      ...COMPOSE_R4_OPAQUE_ATOMS,
    ],
    content,
  });
}

function stamp(editor: Editor, ids: string[]): void {
  const map: ParaIdMapEntry[] = ids.map((paraId, index) => ({ index, paraId, isMinted: false }));
  stampParaIds(editor, map);
}

const NO_THREADS: never[] = [];

/** The em space the server's tab atom carries — U+2003, the non-collapsing representation (GPT §9.1). */
const EM_SPACE = ' ';

/** Exactly what `ComposeDocxProjectionBuilder.AppendAtom` emits for a `w:tab`. */
const TAB_ATOM = `<span class="compose-atom compose-tab" data-atom-kind="tab" contenteditable="false">${EM_SPACE}</span>`;

/** Exactly what it emits for a `w:sym` — here the corpus's real case, Symbol-font F0A7 → §. */
const SYMBOL_ATOM =
  '<span class="compose-atom" data-atom-kind="symbol" data-sym-font="Symbol" data-sym-char="F0A7" contenteditable="false">§</span>';

describe('task 048 — tabs and symbols survive an edit to their paragraph', () => {
  it('round-trips a tab as an isTab marker run, in the right place', () => {
    const editor = makeEditor();
    editor.commands.setContent(`<p>Term:${TAB_ATOM}definition</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    // The atom contributes exactly the one character it did as plain text — this is the invariant the whole
    // change rests on. If it ever contributes 0 or 2, every offset in the paragraph shifts.
    expect(snapshot.get('AAAA0001')).toBe(`Term:${EM_SPACE}definition`);

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [{ text: 'Term:' }, { text: '', isTab: true }, { text: 'definition' }],
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const runs = model.blocks[0].runs!;
    expect(runs.filter(r => r.isTab).length).toBe(1);

    // Position is the meaning of a tab: one in the wrong place is its own kind of damage, so presence alone
    // would be a test that passes while the document is wrong.
    const markerIndex = runs.findIndex(r => r.isTab);
    expect(
      runs
        .slice(0, markerIndex)
        .map(r => r.text)
        .join('')
    ).toBe('Term:');
    expect(
      runs
        .slice(markerIndex + 1)
        .map(r => r.text)
        .join('')
    ).toContain('definition');

    // The marker is a marker: the em space must NOT also survive as text, or the tab is written twice.
    expect(runs.every(r => !r.text.includes(EM_SPACE))).toBe(true);

    editor.destroy();
  });

  it('round-trips a symbol as its FONT and CODE POINT, not as the glyph the reader resolved', () => {
    const editor = makeEditor();
    editor.commands.setContent(`<p>See${SYMBOL_ATOM}4.2</p>`);
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);
    expect(snapshot.get('AAAA0001')).toBe('See§4.2');

    editor.commands.insertContentAt(editor.state.doc.content.size - 1, ' X');

    const loaded: ComposeContentModel = {
      blocks: [
        {
          kind: 'Paragraph',
          paraId: 'AAAA0001',
          runs: [{ text: 'See' }, { text: '', symbol: { font: 'Symbol', charCode: 'F0A7' } }, { text: '4.2' }],
        },
      ],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    const runs = model.blocks[0].runs!;
    const symbolRuns = runs.filter(r => r.symbol !== undefined);
    expect(symbolRuns.length).toBe(1);

    // The whole point. § in a legal document is usually Symbol-font F0A7, NOT U+00A7 — re-authoring the
    // resolved look-alike would quietly change the character the document contains.
    expect(symbolRuns[0].symbol).toEqual({ font: 'Symbol', charCode: 'F0A7' });

    // And the glyph must not ALSO survive as text, which would duplicate it.
    expect(runs.every(r => !r.text.includes('§'))).toBe(true);

    editor.destroy();
  });

  it('an UNTOUCHED paragraph containing a tab is still passed through by object identity', () => {
    // The regression this change could most plausibly have caused. `formattingUnchanged` walks two char
    // streams in parallel — the editor's segments and the loaded block's runs — and an atom counted on one
    // side but not the other desynchronizes them, so every paragraph with a tab would be reported changed
    // and rebuilt. Rebuilt is not catastrophic (the merge preserves content), but it forfeits the verbatim
    // clone that ADR-049's preserve-untouched invariant is built on.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Term:${TAB_ATOM}definition</p><p>Untouched too.</p>`);
    stamp(editor, ['AAAA0001', 'BBBB0002']);
    const snapshot = captureParaIdSnapshot(editor);

    const tabBlock = {
      kind: 'Paragraph' as const,
      paraId: 'AAAA0001',
      runs: [{ text: 'Term:' }, { text: '', isTab: true }, { text: 'definition' }],
    };
    const loaded: ComposeContentModel = {
      blocks: [tabBlock, { kind: 'Paragraph', paraId: 'BBBB0002', runs: [{ text: 'Untouched too.' }] }],
    };

    const { model } = buildImportedContentModel(editor, loaded, snapshot, {
      trackChanges: true,
      sessionThreads: NO_THREADS,
    });

    // Object identity, not deep equality: a rebuilt block that happens to look the same would pass a
    // structural comparison while having lost every server-set fact the original carried.
    expect(model.blocks[0]).toBe(tabBlock);

    editor.destroy();
  });

  it('renders a tab and a symbol as CONTENT, never as a labeled placeholder chip', () => {
    // Caught during implementation, so pinned here. `.compose-atom` styles an atom as a dashed,
    // background-filled, italic chip — right for "a content control was here", very wrong for a tab or a
    // section mark. Without the `compose-atom-renderable` reset the fidelity fix would have put a visible
    // dashed box around every tab in the document: a cosmetic regression shipped by a correctness change.
    const editor = makeEditor();
    editor.commands.setContent(`<p>Term:${TAB_ATOM}x${SYMBOL_ATOM}</p>`);
    const html = editor.getHTML();

    expect(html).toContain('compose-atom-renderable');
    expect(html).toContain('compose-tab');

    // The atom's own content, not a label. An opaque atom renders "Field: 3"; these must render the
    // character itself, or the user sees the word "Tab" written into their document.
    expect(html).not.toContain('Tab:');
    expect(html).not.toContain('Symbol:');
    expect(html).toContain('§');
    expect(html).toContain(EM_SPACE);

    editor.destroy();
  });

  it('leaves OPAQUE atoms contributing nothing to the coordinate space', () => {
    // The deliberate asymmetry, pinned so it cannot drift. A field's display text is a UI label — the editor
    // shows "Field: 3" — and injecting a label into the document's text coordinates would be worse than the
    // zero it contributes. Only atoms that render as their own content are counted.
    const editor = makeEditor();
    editor.commands.setContent(
      '<p>Section <span class="compose-atom" data-atom-kind="field" contenteditable="false">3</span> applies.</p>'
    );
    stamp(editor, ['AAAA0001']);
    const snapshot = captureParaIdSnapshot(editor);

    expect(snapshot.get('AAAA0001')).toBe('Section  applies.');
    expect(snapshot.get('AAAA0001')).not.toContain('Field');

    editor.destroy();
  });
});
