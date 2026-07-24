/**
 * importRoundTrip.test.tsx — FR-10 import round-trip acceptance suite (spaarkeai-compose-r4 task 053).
 *
 * Pre-existing Word `w:ins`/`w:del`/`w:comment` in an imported document must render IN THE EDITOR as
 * first-class tracked changes + comment threads that are accept/reject-able, paraId-keyed so they
 * survive a save, and — for the negative case — an imported revision on an unresolvable paraId MUST
 * surface for review rather than being silently dropped (spec FR-10, invariant I-7).
 *
 * THE READER (`Services/Compose/DocxAnnotationReader.cs`) already recovers `w:ins`/`w:del`/`w:comment`
 * out of the OOXML and `ComposeService.LoadAsync` already projects the result onto the Load response
 * (`ImportedRevision[]` / `ImportedComment[]`, each carrying the E2 `w14:paraId`) — verified unmodified
 * by this task (see `ComposeService.cs` lines ~328-380, task-050/051-era code inherited onto this R4
 * branch). What this task closes is the ONE genuine gap: an unresolvable imported REVISION rendered
 * nothing and was only reflected in a discarded internal count — invisible to the user, which reads as
 * a silent drop despite the underlying data surviving server-side. `renderUnresolvedRevisionPlaceholders`
 * (`./importedRevisions.ts`) closes that gap by reusing the task-021 opaque-atom node
 * (`composeInlineAtom`) as a review marker. Imported COMMENTS already satisfied I-7 before this task —
 * `groupImportedComments` always seeds the FR-23 panel regardless of anchor resolution (proved by
 * `importedComments.test.ts`) — so this file exercises that existing behavior rather than re-building it.
 *
 * WHY A HEADLESS-EDITOR + HOOK SUITE, NOT A FULL `ComposeEditor` REACT MOUNT: `ComposeEditor.tsx`
 * transitively imports `@spaarke/ui-components` (via `ComposeAiToolbar.tsx`) and `@spaarke/auth`,
 * neither of which has a built `dist/` in this worktree — a pre-existing, documented environmental gap
 * (see `ComposeEditor.opaqueAtomTheme.test.ts`'s file header for the same finding, task 024; verified
 * unchanged here via `git stash` A/B — the `tsc --noEmit` error set is byte-identical with and without
 * this task's changes). `usePendingRedline.ts` imports `ComposeEditor` via `import type` only (erased at
 * compile time), so it — and every render primitive this suite needs — loads cleanly without the broken
 * chain. This suite exercises the SAME production code paths `ComposeEditor.tsx` calls
 * (`applyImportedRevisions`, `applyImportedCommentAnchors`, `renderUnresolvedRevisionPlaceholders`,
 * `groupImportedComments`, `usePendingRedline`'s `accept`/`reject`) over a schema assembled from the
 * SAME extensions (`StarterKit` + the R2 marks + the R3 paraId extension + the R4 opaque atoms)
 * `ComposeEditor.tsx` registers — not a reimplementation or a synthetic stand-in.
 *
 * @see ./importedRevisions.ts / ./importedRevisions.test.ts (revision render + unresolved-placeholder unit coverage)
 * @see ./importedComments.ts / ./importedComments.test.ts (comment thread grouping + anchor unit coverage)
 * @see ./hooks/usePendingRedline.ts (the accept/reject engine every redline — imported or AI — rides)
 * @see projects/spaarkeai-compose-r4/spec.md FR-10, invariant I-7
 * @see projects/spaarkeai-compose-r4/design.md §4 F4, §5 (import round-trip)
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { renderHook, act } from '@testing-library/react';
import { InsertionMark } from './marks/InsertionMark';
import { DeletionMark } from './marks/DeletionMark';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { COMPOSE_R4_OPAQUE_ATOMS } from './opaqueAtomNode';
import { stampParaIds } from '../utils/docxBridge';
import {
  applyImportedRevisions,
  renderUnresolvedRevisionPlaceholders,
  IMPORTED_LEDGER_PREFIX,
} from './importedRevisions';
import { applyImportedCommentAnchors, groupImportedComments, IMPORTED_COMMENT_THREAD_PREFIX } from './importedComments';
import { usePendingRedline } from './hooks/usePendingRedline';
import type { ImportedRevision } from '../types/compose-contracts';
import type { ImportedComment } from '../types/compose-contracts';

const DATE = '2026-07-19T09:30:00.000Z';

/** The SAME extension set ComposeEditor.tsx registers for this scenario (StarterKit + R2 marks +
 * R3 paraId + R4 opaque atoms) — no CommentAnchorMark-less / atom-less shortcut. */
function mountDoc(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, InsertionMark, DeletionMark, CommentAnchorMark, ...COMPOSE_R3_PARAID, ...COMPOSE_R4_OPAQUE_ATOMS],
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

function comment(overrides: Partial<ImportedComment>): ImportedComment {
  return {
    id: '1',
    author: 'Jordan Ellis',
    date: DATE,
    commentText: '',
    anchorText: '',
    paragraphHint: 0,
    ...overrides,
  };
}

describe('FR-10 import round trip — a doc redlined externally in Word opens with revisions + comments visible', () => {
  it('renders an imported insertion AND an imported deletion as first-class marks, and groups an imported comment into an FR-23 thread — all anchored by paraId', () => {
    // Mammoth KEEPS an inserted run as plain prose (paragraph 1 already reads with "jumps") but DROPS a
    // deleted run entirely (paragraph 2 reads WITHOUT "lazy" — the reader/applyDeletion re-insert it).
    const editor = mountDoc('<p>The quick brown fox jumps.</p><p>The dog sleeps.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);

    const revisions: ImportedRevision[] = [
      revision({ kind: 'insertion', id: 'r1', text: ' jumps', anchorText: 'The quick brown fox.', paraId: 'AAAA0001' }),
      revision({ kind: 'deletion', id: 'r2', text: 'lazy ', anchorText: 'The dog sleeps.', paragraphHint: 1, paraId: 'BBBB0002' }),
    ];
    const comments: ImportedComment[] = [
      comment({ id: 'c1', commentText: 'Define this term.', anchorText: 'quick brown fox', paraId: 'AAAA0001' }),
    ];

    const revisionResult = applyImportedRevisions(editor, revisions);
    applyImportedCommentAnchors(editor, comments);
    const threads = groupImportedComments(comments);

    expect(revisionResult.applied).toBe(2);
    expect(revisionResult.unresolved).toBe(0);

    const html = editor.getHTML();
    expect(html).toContain('compose-mark-insertion');
    expect(html).toContain('compose-mark-deletion');
    expect(html).toContain('compose-mark-comment-anchor');
    expect(threads).toEqual([
      expect.objectContaining({ id: `${IMPORTED_COMMENT_THREAD_PREFIX}c1`, text: 'Define this term.' }),
    ]);

    editor.destroy();
  });

  it('imported revisions are ACCEPT/REJECT-able through the SAME usePendingRedline engine any redline uses', () => {
    // Mammoth KEEPS an inserted run as plain prose (paragraph 1 already reads with "jumps") but DROPS a
    // deleted run entirely (paragraph 2 reads WITHOUT "lazy" — the reader/applyDeletion re-insert it).
    const editor = mountDoc('<p>The quick brown fox jumps.</p><p>The dog sleeps.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);

    applyImportedRevisions(editor, [
      revision({ kind: 'insertion', id: 'r1', text: ' jumps', anchorText: 'The quick brown fox.', paraId: 'AAAA0001' }),
      revision({ kind: 'deletion', id: 'r2', text: 'lazy ', anchorText: 'The dog sleeps.', paragraphHint: 1, paraId: 'BBBB0002' }),
    ]);

    const { result } = renderHook(() => usePendingRedline(editor));

    // ACCEPT the imported insertion: the mark is removed but the inserted text is KEPT (the insertion
    // becomes permanent, plain text — no residual `compose-mark-insertion`/ledgerRef for r1).
    act(() => {
      result.current.accept(`${IMPORTED_LEDGER_PREFIX}r1`);
    });
    let html = editor.getHTML();
    expect(html).toContain('jumps');
    expect(html).not.toContain(`data-ledger-ref="${IMPORTED_LEDGER_PREFIX}r1"`);

    // REJECT the imported deletion: the struck text is RESTORED as normal text (the user disagrees
    // with the imported deletion and keeps the original wording) — mark removed, text kept.
    act(() => {
      result.current.reject(`${IMPORTED_LEDGER_PREFIX}r2`);
    });
    html = editor.getHTML();
    expect(html).toContain('lazy');
    expect(html).not.toContain(`data-ledger-ref="${IMPORTED_LEDGER_PREFIX}r2"`);

    editor.destroy();
  });

  it('accepting an imported deletion actually removes the struck text (confirms the deletion)', () => {
    // Mammoth already DROPPED the deleted "lazy " run from the flattened prose — the reader recovers it
    // and applyDeletion RE-INSERTS it (struck) for review; only that re-inserted occurrence carries the
    // deletion mark, so accepting it removes exactly that text.
    const editor = mountDoc('<p>The dog sleeps.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'BBBB0002', isMinted: false }]);

    applyImportedRevisions(editor, [
      revision({ kind: 'deletion', id: 'r3', text: 'lazy ', anchorText: 'The dog sleeps.', paraId: 'BBBB0002' }),
    ]);
    expect(editor.getHTML()).toContain('lazy');

    const { result } = renderHook(() => usePendingRedline(editor));
    act(() => {
      result.current.accept(`${IMPORTED_LEDGER_PREFIX}r3`);
    });

    // Converges to the reader's settled (anchorText) reading — the deletion is now final.
    expect(editor.getHTML()).not.toContain('lazy');
    expect(editor.getHTML()).toContain('The dog sleeps.');
    editor.destroy();
  });

  it('NEGATIVE (I-7): an imported revision on an unresolvable paraId surfaces for review — never silently dropped, never text-searched into place', () => {
    const editor = mountDoc('<p>Entirely unrelated content.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const unresolvable = revision({
      kind: 'insertion',
      id: 'r9',
      author: 'Sam Rivera',
      text: 'a clause from a paragraph Word regenerated the id for',
      anchorText: 'A paragraph nowhere in this document.',
      paragraphHint: 9,
      paraId: 'DEADBEEF',
    });

    const { applied, unresolved, unresolvedItems } = applyImportedRevisions(editor, [unresolvable]);
    expect(applied).toBe(0);
    expect(unresolved).toBe(1);

    // Not silently dropped: it is NEVER rendered as a normal insertion/deletion mark (that would be a
    // guess — I-7 bans text-search placement) but it MUST still surface somewhere for review.
    expect(editor.getHTML()).not.toContain('compose-mark-insertion');

    renderUnresolvedRevisionPlaceholders(editor, unresolvedItems);

    const html = editor.getHTML();
    // Surfaced via the reused task-021 opaque-atom node, appended at the END — the original (unrelated)
    // paragraph is untouched, proving this is never a guessed in-place insertion.
    expect(html).toContain('compose-atom-inline');
    expect(html).toContain('Sam Rivera');
    expect(html).toContain('Entirely unrelated content.');

    editor.destroy();
  });

  it('NEGATIVE parity: an imported COMMENT on an unresolvable anchor still surfaces in the FR-23 panel projection (already-compliant behavior, unchanged by this task)', () => {
    const editor = mountDoc('<p>Entirely unrelated content.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const orphanComment = comment({
      id: 'c9',
      commentText: 'This should still be reviewable.',
      anchorText: 'A paragraph nowhere in this document.',
      paragraphHint: 9,
      paraId: 'DEADBEEF',
    });

    const anchorResult = applyImportedCommentAnchors(editor, [orphanComment]);
    expect(anchorResult).toEqual({ applied: 0, unresolved: 1 });
    expect(editor.getHTML()).not.toContain('compose-mark-comment-anchor');

    // groupImportedComments is PURE and independent of anchor resolution — the panel still shows it.
    const threads = groupImportedComments([orphanComment]);
    expect(threads).toHaveLength(1);
    expect(threads[0].text).toBe('This should still be reviewable.');

    editor.destroy();
  });

  it('SURVIVES A SAVE+RELOAD ROUND TRIP: re-mounting from the SAME Load-response revisions/comments (no edit made) re-anchors identically', () => {
    // Simulates "import a redlined doc, make no change, save, reload": since imported revisions/comments
    // are re-derived fresh from the retained/patched bytes on every Load (DocxAnnotationReader re-runs
    // server-side; see ComposeService.LoadAsync), an unedited save leaves the source paragraphs
    // byte-identical (invariant I-4) — so the SAME ImportedRevision/ImportedComment set, applied to a
    // FRESH editor mount built from the SAME content, must reproduce the IDENTICAL applied/unresolved
    // outcome and mark placement as the first mount.
    const content = '<p>The quick brown fox jumps.</p><p>The lazy dog sleeps.</p>';
    const paraIds = [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ];
    const revisions: ImportedRevision[] = [
      revision({ kind: 'insertion', id: 'r1', text: ' jumps', anchorText: 'The quick brown fox.', paraId: 'AAAA0001' }),
    ];
    const comments: ImportedComment[] = [
      comment({ id: 'c1', commentText: 'Define this term.', anchorText: 'quick brown fox', paraId: 'AAAA0001' }),
    ];

    const firstMount = mountDoc(content);
    stampParaIds(firstMount, paraIds);
    const firstRevisionResult = applyImportedRevisions(firstMount, revisions);
    applyImportedCommentAnchors(firstMount, comments);
    const firstHtml = firstMount.getHTML();
    firstMount.destroy();

    // "reload" — a fresh editor instance, same source content + same server-projected imports (the
    // paraIds are unchanged because no edit occurred, per I-4 — the retained bytes are byte-identical).
    const reloadedMount = mountDoc(content);
    stampParaIds(reloadedMount, paraIds);
    const reloadedRevisionResult = applyImportedRevisions(reloadedMount, revisions);
    applyImportedCommentAnchors(reloadedMount, comments);
    const reloadedHtml = reloadedMount.getHTML();
    reloadedMount.destroy();

    expect(reloadedRevisionResult.applied).toBe(firstRevisionResult.applied);
    expect(reloadedRevisionResult.unresolved).toBe(firstRevisionResult.unresolved);
    expect(reloadedHtml).toBe(firstHtml);
    expect(reloadedHtml).toContain(`data-ledger-ref="${IMPORTED_LEDGER_PREFIX}r1"`);
    expect(reloadedHtml).toContain('compose-mark-comment-anchor');
  });
});
