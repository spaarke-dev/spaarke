/**
 * composeAnchoredDocumentText.test.ts — the whole-document CLOSED SET (task 054, FR-C03).
 *
 * The supply half of FR-C03. Task 051 gave the SELECTION-scoped Actions a deterministic anchor; the
 * whole-document pass has no selection, so it needs the document's paragraphs as an enumerated set the
 * model can choose an id from.
 *
 * What is proven here:
 *  - the set and the text are ONE artifact — each id sits beside the content it names, so naming a
 *    target is a COPY rather than a generation (the whole point: generation is what loses wording);
 *  - the set is COMPLETE — every id-bearing block appears, in document order, including empty
 *    paragraphs and paragraphs inside tables. An incomplete "closed" set is a contradiction: the model
 *    would be refused on ids that genuinely exist;
 *  - it reflects the LIVE document, so a paragraph typed after load is in the set (this is the reason
 *    the editor supplies it instead of the server's Load-time reference map);
 *  - it is the SAME set placement resolves against — the ids it emits all resolve through
 *    `collectBlocks`, so the model cannot be given an id the redline path cannot place (invariant 3);
 *  - un-stamped blocks are emitted but NOT claimed as set members, so the caller can tell "no closed
 *    set" from "a partial one".
 *
 * Same headless `@tiptap/core` Editor + `stampParaIds` convention as `usePendingRedline.anchor.test.tsx`,
 * so the paraIds are real rather than simulated.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { collectBlocks } from './importedRevisions';
import { stampParaIds } from '../utils/docxBridge';
import { buildAnchoredDocumentText } from './composeAnchoredDocumentText';
import type { ParaIdMapEntry } from '../types/compose-contracts';

function entry(index: number, paraId: string): ParaIdMapEntry {
  return { index, paraId, isMinted: false };
}

function makeEditor(content: string, ids: string[]): Editor {
  const editor = new Editor({ extensions: [StarterKit, ...COMPOSE_R3_PARAID], content });
  stampParaIds(
    editor,
    ids.map((id, i) => entry(i, id))
  );
  return editor;
}

const THREE_CLAUSES =
  '<p>1. Definitions</p>' +
  '<p>The receiving party shall indemnify the disclosing party.</p>' +
  '<p>The disclosing party shall indemnify the receiving party.</p>';

describe('buildAnchoredDocumentText — the closed set IS the document text', () => {
  it('prefixes every id-bearing paragraph with its own identifier, in document order', () => {
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0002', 'AAAA0003']);

    const { text, paraIds } = buildAnchoredDocumentText(editor);

    expect(paraIds).toEqual(['AAAA0001', 'AAAA0002', 'AAAA0003']);
    expect(text.split('\n')).toEqual([
      '[AAAA0001] 1. Definitions',
      '[AAAA0002] The receiving party shall indemnify the disclosing party.',
      '[AAAA0003] The disclosing party shall indemnify the receiving party.',
    ]);
    editor.destroy();
  });

  it('puts each id beside the content it names — so targeting is a copy, not a guess', () => {
    // The two indemnity clauses differ only by party order: a model working from a SIDE list of ids
    // would have to disambiguate them by quoting prose, which is the lossy step this removes.
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0002', 'AAAA0003']);

    const { text } = buildAnchoredDocumentText(editor);
    const line = text.split('\n').find(l => l.includes('receiving party shall indemnify'))!;

    expect(line.startsWith('[AAAA0002] ')).toBe(true);
    editor.destroy();
  });

  it('emits every id the redline path can place, and no others (one coordinate system)', () => {
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0002', 'AAAA0003']);

    const { paraIds } = buildAnchoredDocumentText(editor);
    const placeable = collectBlocks(editor)
      .map(b => b.paraId)
      .filter((id): id is string => !!id);

    // Set equality in BOTH directions. A model given an id outside the placeable set would be refused
    // (UAT-21); a placeable id missing from the set is a paragraph the model cannot target at all.
    expect([...paraIds].sort()).toEqual([...placeable].sort());
    editor.destroy();
  });
});

describe('buildAnchoredDocumentText — completeness is the invariant', () => {
  it('includes a paragraph typed AFTER load — the reason the editor supplies this, not the server', () => {
    // The server's ChatSession.ReferenceMap is a Load-time snapshot. A paragraph added since would be
    // absent from it, so a server-supplied set would omit an id that genuinely exists and the model
    // would be refused for naming it.
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0002', 'AAAA0003']);
    const before = buildAnchoredDocumentText(editor).paraIds.length;

    editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    editor.commands.insertContent('<p>4. Governing law is Delaware.</p>');
    stampParaIds(editor, [entry(3, 'AAAA0004')]);

    const after = buildAnchoredDocumentText(editor);

    expect(after.paraIds.length).toBeGreaterThan(before);
    expect(after.text).toContain('Governing law is Delaware.');
    editor.destroy();
  });

  it('keeps EMPTY paragraphs in the set — they are legitimate insertion targets', () => {
    const editor = makeEditor('<p>1. Definitions</p><p></p><p>3. Term</p>', ['BBBB0001', 'BBBB0002', 'BBBB0003']);

    const { paraIds, text } = buildAnchoredDocumentText(editor);

    expect(paraIds).toContain('BBBB0002');
    expect(text.split('\n')[1]).toBe('[BBBB0002] ');
    editor.destroy();
  });

  it('never drops content: every block walked is represented on a line', () => {
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0002', 'AAAA0003']);

    const { text, totalBlocks } = buildAnchoredDocumentText(editor);

    expect(totalBlocks).toBe(collectBlocks(editor).length);
    expect(text.split('\n')).toHaveLength(totalBlocks);
    editor.destroy();
  });
});

describe('buildAnchoredDocumentText — an absent set is distinguishable from a partial one', () => {
  it('reports an EMPTY set for an unstamped document, while still emitting its text', () => {
    // The caller omits documentText entirely on an empty set. Sending an id-free document while the
    // prompt claims it carries a closed set is the failure this distinction prevents.
    const editor = new Editor({
      extensions: [StarterKit, ...COMPOSE_R3_PARAID],
      content: THREE_CLAUSES,
    });

    const { paraIds, text, totalBlocks } = buildAnchoredDocumentText(editor);

    expect(paraIds).toEqual([]);
    expect(totalBlocks).toBe(3);
    expect(text).toContain('1. Definitions');
    expect(text).not.toContain('[');
    editor.destroy();
  });

  it('emits an unstamped block unprefixed rather than omitting it', () => {
    // Dropping it would hand the model a document that disagrees with the one on screen, and an edit
    // to a neighbouring paragraph would then be reasoned about against prose the model never saw.
    const editor = makeEditor(THREE_CLAUSES, ['AAAA0001', 'AAAA0003']);

    const { text, paraIds, totalBlocks } = buildAnchoredDocumentText(editor);

    expect(totalBlocks).toBe(3);
    expect(text.split('\n')).toHaveLength(3);
    expect(paraIds).toHaveLength(2);
    expect(text).toContain('The disclosing party shall indemnify the receiving party.');
    editor.destroy();
  });
});
