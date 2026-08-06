/**
 * importedRevisions.test.ts — render existing Word revisions as first-class marks (FR-24, task 050).
 *
 * Exercised through a headless TipTap `Editor` (StarterKit + the R2 insertion/deletion marks + the R3
 * paraId extension) — the same schema path ComposeEditor mounts. Protects the FR-24 render invariants:
 *  - an imported `w:ins` becomes a visible `compose-mark-insertion` over its inserted text, author/date-
 *    attributed, carrying an `imported:` ledgerRef so the save-path bridge skips it;
 *  - an imported `w:del` (which mammoth DROPS) is re-materialized as a visible `compose-mark-deletion`;
 *  - anchoring is paraId-FIRST, with the anchorText/paragraphHint fuzzy fallback for the cross-Word-session
 *    case (a stale/regenerated paraId);
 *  - an un-anchorable revision renders nothing (never guesses).
 *
 * A headless-editor DOM transform — no React render, no mammoth — so these are fast + deterministic.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { COMPOSE_R4_OPAQUE_ATOMS } from './opaqueAtomNode';
import { stampParaIds } from '../utils/docxBridge';
import {
  applyImportedRevisions,
  renderUnresolvedRevisionPlaceholders,
  IMPORTED_LEDGER_PREFIX,
} from './importedRevisions';
import type { ImportedRevision } from '../types/compose-contracts';

const DATE = '2026-07-19T09:30:00.000Z';

function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, ...COMPOSE_R3_PARAID, ...COMPOSE_R4_OPAQUE_ATOMS],
    content,
  });
}

function revision(overrides: Partial<ImportedRevision>): ImportedRevision {
  return {
    kind: 'insertion',
    id: '1',
    author: 'Jordan Ellis',
    date: DATE,
    text: '',
    anchorText: '',
    paragraphHint: 0,
    ...overrides,
  };
}

describe('applyImportedRevisions (FR-24, task 050)', () => {
  it('renders an imported insertion as a first-class, author-attributed, ledger-tagged mark (paraId anchor)', () => {
    // The inserted text is present as prose (mammoth kept a w:ins as text); the render marks it.
    const editor = makeEditor('<p>The quick brown fox (Vulpes vulpes).</p><p>The dog sleeps.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);

    const result = applyImportedRevisions(editor, [
      revision({
        kind: 'insertion',
        id: 'r1',
        text: ' (Vulpes vulpes)',
        anchorText: 'The quick brown fox.',
        paragraphHint: 0,
        paraId: 'AAAA0001',
      }),
    ]);

    expect(result).toEqual({ applied: 1, unresolved: 0, unresolvedItems: [] });
    const html = editor.getHTML();
    expect(html).toContain('compose-mark-insertion');
    expect(html).toContain('data-author="Jordan Ellis"');
    expect(html).toContain(`data-ledger-ref="${IMPORTED_LEDGER_PREFIX}r1"`);
    editor.destroy();
  });

  it('re-materializes an imported deletion (dropped by mammoth) as a visible, struck deletion mark', () => {
    const editor = makeEditor('<p>The dog sleeps.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'CCCC0003', isMinted: false }]);

    const result = applyImportedRevisions(editor, [
      revision({
        kind: 'deletion',
        id: 'r2',
        text: 'lazy ',
        anchorText: 'The dog sleeps.',
        paragraphHint: 0,
        paraId: 'CCCC0003',
      }),
    ]);

    expect(result).toEqual({ applied: 1, unresolved: 0, unresolvedItems: [] });
    const html = editor.getHTML();
    expect(html).toContain('compose-mark-deletion');
    // The deleted text is now VISIBLE (re-inserted + struck), not flattened away.
    expect(html).toContain('lazy');
    expect(html).toContain('data-author="Jordan Ellis"');
    expect(html).toContain(`data-ledger-ref="${IMPORTED_LEDGER_PREFIX}r2"`);
    editor.destroy();
  });

  it('falls back to fuzzy anchoring (anchorText + hint) when the paraId is stale (cross-Word-session)', () => {
    const editor = makeEditor('<p>The quick brown fox (Vulpes vulpes).</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    // Word regenerated the paraId on its external save — the revision's paraId no longer matches any node,
    // so it must re-anchor by anchorText + paragraphHint (the reader's settled anchorText excludes the
    // inserted run, so this exercises the word-overlap fuzzy path).
    const result = applyImportedRevisions(editor, [
      revision({
        kind: 'insertion',
        id: 'r3',
        text: ' (Vulpes vulpes)',
        anchorText: 'The quick brown fox.',
        paragraphHint: 0,
        paraId: 'DEADBEEF',
      }),
    ]);

    expect(result).toEqual({ applied: 1, unresolved: 0, unresolvedItems: [] });
    expect(editor.getHTML()).toContain('compose-mark-insertion');
    editor.destroy();
  });

  it('counts a revision that cannot be anchored as unresolved, renders nothing, and returns the item itself', () => {
    const editor = makeEditor('<p>Entirely unrelated content.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const unresolvable = revision({
      kind: 'insertion',
      id: 'r4',
      text: 'nonexistent text',
      anchorText: 'A different paragraph that is nowhere.',
      paragraphHint: 9,
      paraId: 'ZZZZ9999',
    });

    const result = applyImportedRevisions(editor, [unresolvable]);

    expect(result).toEqual({ applied: 0, unresolved: 1, unresolvedItems: [unresolvable] });
    expect(editor.getHTML()).not.toContain('compose-mark-insertion');
    editor.destroy();
  });

  // R5 task 012 (G12) — imported-deletion end-of-paragraph re-anchor for a paragraph-boundary-spanning
  // deletion. A deletion of a whole paragraph's content (across the para mark) carries EMPTY anchorText (all
  // its runs are inside w:del → the reader's settled-text anchor is ""). On our OWN round-trip the paraId
  // still matches and it re-anchors precisely; cross-Word-session (regenerated + stale paraId) the fully-
  // deleted paragraph is gone from the flattened editor, so the doc-order hint now lands on a DIFFERENT,
  // already-identified paragraph — which must NOT silently absorb the struck text (I-7 "never guess").
  describe('imported-deletion boundary-spanning re-anchor (G12 / task 012)', () => {
    it('re-anchors a whole-paragraph (empty-anchorText) deletion precisely by paraId on the own round-trip', () => {
      const editor = makeEditor('<p>Keeper one.</p><p></p><p>Keeper two.</p>');
      stampParaIds(editor, [
        { index: 0, paraId: 'AAAA0001', isMinted: false },
        { index: 1, paraId: 'CCCC0003', isMinted: false }, // the fully-deleted paragraph, now empty
        { index: 2, paraId: 'BBBB0002', isMinted: false },
      ]);

      const result = applyImportedRevisions(editor, [
        revision({
          kind: 'deletion',
          id: 'r7',
          text: 'entire deleted sentence',
          anchorText: '',
          paragraphHint: 1,
          paraId: 'CCCC0003',
        }),
      ]);

      expect(result).toEqual({ applied: 1, unresolved: 0, unresolvedItems: [] });
      const html = editor.getHTML();
      // The struck text lands in ITS OWN (empty) paragraph — the keepers are untouched.
      expect(html).toContain('Keeper one.</p>');
      expect(html).toContain('<p>Keeper two.');
      expect(html).toMatch(/<p><span[^>]*data-ledger-ref="imported:r7"[^>]*>entire deleted sentence<\/span><\/p>/);
      editor.destroy();
    });

    it('does NOT mis-anchor an empty-anchorText deletion onto a distinct identified paragraph when its paraId is stale', () => {
      // Cross-Word-session: the fully-deleted paragraph is gone; both survivors carry their own (regenerated)
      // ids, and the revision's paraId no longer matches any of them. The hint index now points at a REAL
      // keeper paragraph — the deletion must surface as unresolved, never append its struck text there.
      const editor = makeEditor('<p>Keeper one.</p><p>Keeper two.</p>');
      stampParaIds(editor, [
        { index: 0, paraId: 'AAAA0001', isMinted: false },
        { index: 1, paraId: 'BBBB0002', isMinted: false },
      ]);

      const boundarySpanning = revision({
        kind: 'deletion',
        id: 'r8',
        text: 'was a whole clause',
        anchorText: '',
        paragraphHint: 1,
        paraId: 'DEADDEAD',
      });

      const result = applyImportedRevisions(editor, [boundarySpanning]);

      expect(result).toEqual({ applied: 0, unresolved: 1, unresolvedItems: [boundarySpanning] });
      const html = editor.getHTML();
      // Neither keeper absorbed the struck text.
      expect(html).not.toContain('was a whole clause');
      expect(html).toContain('Keeper two.</p>');
      editor.destroy();
    });
  });

  it('is a no-op for an empty/undefined revision list', () => {
    const editor = makeEditor('<p>Untouched.</p>');
    expect(applyImportedRevisions(editor, [])).toEqual({ applied: 0, unresolved: 0, unresolvedItems: [] });
    expect(applyImportedRevisions(editor, undefined)).toEqual({ applied: 0, unresolved: 0, unresolvedItems: [] });
    expect(editor.getHTML()).toContain('Untouched.');
    expect(editor.getHTML()).not.toContain('compose-mark-');
    editor.destroy();
  });
});

describe('renderUnresolvedRevisionPlaceholders (task 053, FR-10 / invariant I-7)', () => {
  it('appends one composeInlineAtom review placeholder per unresolved revision, at the end of the document', () => {
    const editor = makeEditor('<p>The quick brown fox jumps.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const unresolvable = revision({
      kind: 'deletion',
      id: 'r9',
      author: 'Jordan Ellis',
      text: 'the missing clause',
      anchorText: 'A paragraph nowhere in this document.',
      paragraphHint: 9,
      paraId: 'ZZZZ9999',
    });

    const { unresolvedItems } = applyImportedRevisions(editor, [unresolvable]);
    renderUnresolvedRevisionPlaceholders(editor, unresolvedItems);

    const html = editor.getHTML();
    expect(html).toContain('compose-atom');
    expect(html).toContain('compose-atom-inline');
    expect(html).toContain('Jordan Ellis');
    expect(html).toContain('the missing clause');
    // The original paragraph is untouched — the placeholder is APPENDED, never text-searched into place.
    expect(html).toContain('The quick brown fox jumps.');
    editor.destroy();
  });

  it('distinguishes insertion vs deletion in the placeholder label', () => {
    const editor = makeEditor('<p>Untouched.</p>');
    const insertionRevision = revision({ kind: 'insertion', id: 'r10', text: 'new clause', author: 'Sam Rivera' });
    renderUnresolvedRevisionPlaceholders(editor, [insertionRevision]);
    expect(editor.getHTML()).toContain('Imported insertion could not be placed automatically');
    editor.destroy();
  });

  it('is a no-op for an empty/undefined unresolved list — no placeholder appended', () => {
    const editor = makeEditor('<p>Untouched.</p>');
    renderUnresolvedRevisionPlaceholders(editor, []);
    renderUnresolvedRevisionPlaceholders(editor, undefined);
    expect(editor.getHTML()).not.toContain('compose-atom');
    editor.destroy();
  });

  it('is a no-op when the editor has no opaque-atom node in its schema (defensive)', () => {
    const bareEditor = new Editor({
      extensions: [StarterKit, InsertionMark, DeletionMark, ...COMPOSE_R3_PARAID],
      content: '<p>Untouched.</p>',
    });
    expect(() => renderUnresolvedRevisionPlaceholders(bareEditor, [revision({ id: 'r11', text: 'x' })])).not.toThrow();
    bareEditor.destroy();
  });
});
