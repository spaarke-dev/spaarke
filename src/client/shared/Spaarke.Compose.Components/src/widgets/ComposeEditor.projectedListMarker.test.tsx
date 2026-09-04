/**
 * ComposeEditor.projectedListMarker.test.tsx — UAT round 2 (r8, 2026-09-02).
 *
 * "Numbered list" looked like a dead button: `& .ProseMirror ol { list-style-type: none }` was
 * UNCONDITIONAL and the only source of a displayed number was the server-computed
 * `data-computed-number`, so a list the USER created rendered with no number at all.
 *
 * The suppression itself is correct for DOCUMENT-SOURCED lists and must stay: a clause whose `numId`
 * could not be resolved is deliberately left bare (F-3, "never fabricate a number"), and a browser count
 * would silently disagree with the real legal number. Both that case and a brand-new list "have no
 * computed number", so they cannot be told apart by the number — only by PROVENANCE. Hence
 * `data-projected-list`, stamped by `ComposeDocxProjectionBuilder.EnsureList`.
 *
 * These tests pin BOTH directions, because a one-directional test would pass on the very regression the
 * rule guards against (re-suppressing everything, or suppressing nothing).
 */

import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { COMPOSE_NUMBER_ATOM } from './composeNumberAtomExtension';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { COMPOSE_INDENT } from './composeIndentExtension';

function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, ...COMPOSE_R3_PARAID, ...COMPOSE_NUMBER_ATOM, ...COMPOSE_INDENT],
    content,
  });
}

/** What `EnsureList` emits for a list that came from the source .docx. */
const PROJECTED_LIST =
  '<ol data-projected-list="1"><li><p data-paraid="AAAA1111" data-computed-number="4.2">Projected clause.</p></li></ol>';
/** What the editor produces when the user clicks "Numbered list". */
const EDITOR_LIST = '<ol><li><p>Freshly authored item.</p></li></ol>';

describe('projected-list provenance marker', () => {
  it('parses data-projected-list off a document-sourced list and re-emits it through getHTML()', () => {
    const editor = makeEditor(PROJECTED_LIST);
    expect(editor.state.doc.firstChild?.type.name).toBe('orderedList');
    expect(editor.state.doc.firstChild?.attrs.projectedList).toBe(true);
    // Must survive the round trip, or the CSS selector stops matching after any setContent cycle.
    expect(editor.getHTML()).toContain('data-projected-list="1"');
    editor.destroy();
  });

  it('does NOT mark an editor-created list, and emits no stray attribute for it', () => {
    const editor = makeEditor(EDITOR_LIST);
    expect(editor.state.doc.firstChild?.attrs.projectedList).toBe(false);
    expect(editor.getHTML()).not.toContain('data-projected-list');
    editor.destroy();
  });

  it('marks a list the user creates from a plain paragraph as NOT projected', () => {
    const editor = makeEditor('<p data-paraid="CCCC3333">Ordinary body text.</p>');
    editor.commands.setTextSelection(3);
    editor.chain().focus().toggleOrderedList().run();

    // This is the exact path behind "I select number from the Paragraph dropdown and it does not set a
    // number tag" — the list must be unmarked so the native marker is allowed to render.
    expect(editor.state.doc.firstChild?.type.name).toBe('orderedList');
    expect(editor.state.doc.firstChild?.attrs.projectedList).toBe(false);
    editor.destroy();
  });

  it('keeps the marker on a projected list even after the user edits inside it', () => {
    const editor = makeEditor(PROJECTED_LIST);
    editor.commands.setTextSelection(5);
    editor.commands.insertContent('edited ');

    // Provenance is a property of the SOURCE, not of whether the text has been touched. Losing it here
    // would hand a real legal clause a browser-invented number — the F-3 violation this guards.
    expect(editor.state.doc.firstChild?.attrs.projectedList).toBe(true);
    editor.destroy();
  });
});

// ---------------------------------------------------------------------------
// Document spacing round-trip (UAT round 2 — read path)
// ---------------------------------------------------------------------------

describe('document spacing survives the TipTap parse', () => {
  // The projection's AppendSpacingDeclarations writes w:spacing as inline CSS. TipTap's base
  // Paragraph/Heading nodes strip any style property they do not know about, so without the registered
  // attributes the editor would drop the document's real spacing on setContent and silently fall back to
  // the generic typographic defaults — looking correct while being wrong, the worst failure shape here.

  it('parses line-height / margin-top / margin-bottom and re-emits them through getHTML()', () => {
    const editor = makeEditor(
      '<p data-paraid="SP000001" style="line-height: 1.5; margin-top: 12pt; margin-bottom: 6pt">Spaced clause.</p>'
    );
    const attrs = editor.state.doc.firstChild?.attrs as Record<string, unknown>;
    expect(attrs.spacingLineHeight).toBe('1.5');
    expect(attrs.spacingMarginTop).toBe('12pt');
    expect(attrs.spacingMarginBottom).toBe('6pt');

    const html = editor.getHTML();
    expect(html).toContain('line-height: 1.5');
    expect(html).toContain('margin-top: 12pt');
    expect(html).toContain('margin-bottom: 6pt');
    editor.destroy();
  });

  it('keeps an EXACT (pt) line-height verbatim — the client never re-derives the unit', () => {
    // The server distinguishes w:lineRule auto (a multiple) from exact (points); the two differ by more
    // than an order of magnitude. The client carries the string opaquely so it cannot disagree.
    const editor = makeEditor('<p data-paraid="SP000002" style="line-height: 18pt">Exact leading.</p>');
    expect((editor.state.doc.firstChild?.attrs as Record<string, unknown>).spacingLineHeight).toBe('18pt');
    editor.destroy();
  });

  it('leaves an unspaced paragraph with null spacing attributes', () => {
    const editor = makeEditor('<p data-paraid="SP000003">Ordinary clause.</p>');
    const attrs = editor.state.doc.firstChild?.attrs as Record<string, unknown>;
    expect(attrs.spacingLineHeight).toBeNull();
    expect(attrs.spacingMarginTop).toBeNull();
    expect(attrs.spacingMarginBottom).toBeNull();
    editor.destroy();
  });
});
