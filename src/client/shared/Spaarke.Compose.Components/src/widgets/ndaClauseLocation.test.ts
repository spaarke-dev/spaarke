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
import { ComposeNumberAtomExtension } from './composeNumberAtomExtension';

function makeEditor(content: string): Editor {
  return new Editor({ extensions: [StarterKit], content });
}

/**
 * Editor that ALSO registers `ComposeNumberAtomExtension` — so `data-computed-number` on a `<p>`/`<h#>`
 * parses into the `computedNumber` node attribute, exactly as the real ComposeEditor mounts it. Used to
 * exercise the WS-3/WS-4 numbered-paragraph citation path.
 */
function makeNumberedEditor(content: string): Editor {
  return new Editor({ extensions: [StarterKit, ComposeNumberAtomExtension], content });
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

  // WS-3/WS-4 (spaarkeai-compose-fidelity-r4.5) — the NDA's sections are NUMBERED PARAGRAPHS, not
  // headings, so `findGoverningHeading` sees nothing. Before this, the label dropped to "Para 3"; now it
  // cites the clause's own computed legal number ("Sec 2"). Reproduces the 2026-07-28 dev-UAT gap.
  it('cites a numbered-paragraph clause by its computed legal number (no heading present)', () => {
    const editor = makeNumberedEditor(
      '<p>This Agreement governs the disclosure of information by and between the parties.</p>' +
        '<p data-computed-number="1">&ldquo;Confidential Information&rdquo; means any technical information.</p>' +
        '<p data-computed-number="2">If the Confidential Information is contained in a written communication, it will be marked Confidential.</p>'
    );
    const pos = posOf(editor, 'written communication'); // inside the clause the model calls "Paragraph 3"
    expect(deriveClauseLocationLabel(editor.state.doc, pos, 'Paragraph 3 (p. 1)')).toBe('Pg 1 · Sec 2 · Para 3');
    editor.destroy();
  });

  it("prefers the clause's own computed number over a governing heading's positional ordinal", () => {
    const editor = makeNumberedEditor(
      '<h2>Confidentiality</h2>' +
        '<p data-computed-number="4.2">Each party shall protect the other party&rsquo;s Confidential Information.</p>'
    );
    const pos = posOf(editor, 'shall protect');
    expect(deriveClauseLocationLabel(editor.state.doc, pos, 'para 1 (p. 3)')).toBe(
      'Pg 3 · Sec 4.2 · Para 1 · Confidentiality'
    );
    editor.destroy();
  });

  it('uses a style-linked heading computed number when the clause body has none', () => {
    const editor = makeNumberedEditor(
      '<h2 data-computed-number="4.2">Confidentiality</h2>' +
        '<p>Each party shall protect the other party&rsquo;s Confidential Information.</p>'
    );
    const pos = posOf(editor, 'shall protect');
    expect(deriveClauseLocationLabel(editor.state.doc, pos, undefined)).toBe('Sec 4.2 · Confidentiality');
    editor.destroy();
  });
});
