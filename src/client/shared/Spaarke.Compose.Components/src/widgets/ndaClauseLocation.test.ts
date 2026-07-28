/**
 * ndaClauseLocation.test.ts — doc-derived clause location label (UAT round-5 #1/#3/#6).
 *
 * Drives `findGoverningHeading` + `deriveClauseLocationLabel` against a REAL headless TipTap editor
 * with heading nodes (the same substrate ComposeEditor mounts), proving the label carries the section
 * heading + ordinal the model's `sectionRef` lacks, and falls back cleanly when no heading governs.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { findGoverningHeading, deriveClauseLocationLabel } from './ndaClauseLocation';

function makeEditor(content: string): Editor {
  return new Editor({ extensions: [StarterKit], content });
}

/** Find the document position of the first text matching `needle`. */
function posOf(editor: Editor, needle: string): number {
  let found = -1;
  editor.state.doc.descendants((node, pos) => {
    if (found === -1 && node.isText && node.text?.includes(needle)) found = pos;
    return found === -1;
  });
  return found;
}

const DOC =
  '<h2>Agreement Not To Disclose Confidential Information</h2>' +
  '<p>The parties agree to keep information confidential and use best efforts.</p>' +
  '<h2>Confidential Information</h2>' +
  '<p>Confidential information refers to any non-public proprietary information.</p>';

describe('findGoverningHeading', () => {
  it('returns the nearest preceding heading + its 1-based ordinal', () => {
    const editor = makeEditor(DOC);
    const pos = posOf(editor, 'best efforts'); // inside the FIRST section
    expect(findGoverningHeading(editor.state.doc, pos)).toEqual({
      heading: 'Agreement Not To Disclose Confidential Information',
      ordinal: 1,
    });
    editor.destroy();
  });

  it('advances the ordinal for a clause under a later heading', () => {
    const editor = makeEditor(DOC);
    const pos = posOf(editor, 'non-public proprietary'); // inside the SECOND section
    expect(findGoverningHeading(editor.state.doc, pos)).toEqual({ heading: 'Confidential Information', ordinal: 2 });
    editor.destroy();
  });

  it('returns null when no heading precedes the position (flat document)', () => {
    const editor = makeEditor('<p>No headings here at all.</p>');
    const pos = posOf(editor, 'No headings');
    expect(findGoverningHeading(editor.state.doc, pos)).toBeNull();
    editor.destroy();
  });
});

describe('deriveClauseLocationLabel', () => {
  it('builds "Pg N · Sec N · Para N · <heading>" from the doc heading + the model page/para', () => {
    const editor = makeEditor(DOC);
    const pos = posOf(editor, 'best efforts');
    expect(deriveClauseLocationLabel(editor.state.doc, pos, 'Paragraph 1 (p. 1)')).toBe(
      'Pg 1 · Sec 1 · Para 1 · Agreement Not To Disclose Confidential Information'
    );
    editor.destroy();
  });

  it('omits page/para parts the model did not provide', () => {
    const editor = makeEditor(DOC);
    const pos = posOf(editor, 'non-public proprietary');
    expect(deriveClauseLocationLabel(editor.state.doc, pos, undefined)).toBe('Sec 2 · Confidential Information');
    editor.destroy();
  });

  it('falls back to the model-only formatter when no heading governs', () => {
    const editor = makeEditor('<p>A flat clause with no heading above it.</p>');
    const pos = posOf(editor, 'flat clause');
    expect(deriveClauseLocationLabel(editor.state.doc, pos, 'Section 4.2, para 2 (p. 3)')).toBe(
      'Pg 3 · Sec 4.2 · Para 2'
    );
    editor.destroy();
  });

  it('falls back to the model-only formatter when the position is null (unresolved anchor)', () => {
    const editor = makeEditor(DOC);
    expect(deriveClauseLocationLabel(editor.state.doc, null, 'Paragraph 5 (p. 1)')).toBe('Pg 1 · Para 5');
    editor.destroy();
  });
});
