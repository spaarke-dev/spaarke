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
import { stampParaIds } from '../utils/docxBridge';
import { applyImportedRevisions, IMPORTED_LEDGER_PREFIX } from './importedRevisions';
import type { ImportedRevision } from '../types/compose-contracts';

const DATE = '2026-07-19T09:30:00.000Z';

function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, ...COMPOSE_R3_PARAID],
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

    expect(result).toEqual({ applied: 1, unresolved: 0 });
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

    expect(result).toEqual({ applied: 1, unresolved: 0 });
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

    expect(result).toEqual({ applied: 1, unresolved: 0 });
    expect(editor.getHTML()).toContain('compose-mark-insertion');
    editor.destroy();
  });

  it('counts a revision that cannot be anchored as unresolved and renders nothing', () => {
    const editor = makeEditor('<p>Entirely unrelated content.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const result = applyImportedRevisions(editor, [
      revision({
        kind: 'insertion',
        id: 'r4',
        text: 'nonexistent text',
        anchorText: 'A different paragraph that is nowhere.',
        paragraphHint: 9,
        paraId: 'ZZZZ9999',
      }),
    ]);

    expect(result).toEqual({ applied: 0, unresolved: 1 });
    expect(editor.getHTML()).not.toContain('compose-mark-insertion');
    editor.destroy();
  });

  it('is a no-op for an empty/undefined revision list', () => {
    const editor = makeEditor('<p>Untouched.</p>');
    expect(applyImportedRevisions(editor, [])).toEqual({ applied: 0, unresolved: 0 });
    expect(applyImportedRevisions(editor, undefined)).toEqual({ applied: 0, unresolved: 0 });
    expect(editor.getHTML()).toContain('Untouched.');
    expect(editor.getHTML()).not.toContain('compose-mark-');
    editor.destroy();
  });
});
