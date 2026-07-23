/**
 * importedComments.test.ts — project existing Word comments into FR-23 comment threads (FR-25, task 051).
 *
 * Exercised through a headless TipTap `Editor` (StarterKit + `CommentAnchorMark` + the R3 paraId
 * extension) — the same schema path ComposeEditor mounts. Protects the FR-25 render invariants:
 *  - comments sharing the SAME anchorText group into ONE thread — first = root, rest = FLAT replies;
 *  - a singleton (unreplied) comment becomes a thread with an empty replies list;
 *  - `groupImportedComments` is PURE — it needs no editor, so it works before any doc mounts;
 *  - `applyImportedCommentAnchors` places the visible `commentAnchor` mark keyed by the SAME thread id
 *    `groupImportedComments` produced — anchoring is paraId-FIRST, with the anchorText/paragraphHint
 *    fuzzy fallback for the cross-Word-session case (a stale/regenerated paraId);
 *  - an un-anchorable thread counts as unresolved but the thread itself is never dropped;
 *  - an empty/undefined comment list is a no-op for both functions.
 *
 * A headless-editor DOM transform — no React render, no mammoth — so these are fast + deterministic.
 */
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CommentAnchorMark } from './marks/CommentAnchorMark';
import { COMPOSE_R3_PARAID } from './paraIdExtension';
import { stampParaIds } from '../utils/docxBridge';
import { groupImportedComments, applyImportedCommentAnchors, IMPORTED_COMMENT_THREAD_PREFIX } from './importedComments';
import type { ImportedComment } from '../types/compose-contracts';

const DATE_1 = '2026-07-19T09:30:00.000Z';
const DATE_2 = '2026-07-19T09:35:00.000Z';

function makeEditor(content: string): Editor {
  return new Editor({
    extensions: [StarterKit, CommentAnchorMark, ...COMPOSE_R3_PARAID],
    content,
  });
}

function comment(overrides: Partial<ImportedComment>): ImportedComment {
  return {
    id: '1',
    author: 'Jordan Ellis',
    date: DATE_1,
    commentText: '',
    anchorText: '',
    paragraphHint: 0,
    ...overrides,
  };
}

describe('groupImportedComments (FR-25, task 051) — pure grouping, no editor', () => {
  it('groups two comments sharing the same anchorText into ONE thread — first root, second a flat reply', () => {
    const threads = groupImportedComments([
      comment({
        id: 'c1',
        anchorText: 'quick brown fox',
        commentText: 'Define this term.',
        author: 'Jordan Ellis',
        date: DATE_1,
      }),
      comment({
        id: 'c2',
        anchorText: 'quick brown fox',
        commentText: 'Agreed — see defined terms.',
        author: 'Sam Rivera',
        date: DATE_2,
      }),
    ]);

    expect(threads).toHaveLength(1);
    expect(threads[0].id).toBe(`${IMPORTED_COMMENT_THREAD_PREFIX}c1`);
    expect(threads[0].author).toBe('Jordan Ellis');
    expect(threads[0].text).toBe('Define this term.');
    expect(threads[0].anchorText).toBe('quick brown fox');
    expect(threads[0].resolved).toBe(false);
    expect(threads[0].replies).toHaveLength(1);
    expect(threads[0].replies[0].author).toBe('Sam Rivera');
    expect(threads[0].replies[0].text).toBe('Agreed — see defined terms.');
    // SCOPE GUARD — the reader carries no modern-comments 4-part structure, so the reply is
    // necessarily flat: it carries no nested-reply structure of its own.
    expect((threads[0].replies[0] as { replies?: unknown }).replies).toBeUndefined();
  });

  it('keeps comments on DIFFERENT anchors as separate threads', () => {
    const threads = groupImportedComments([
      comment({ id: 'c1', anchorText: 'quick brown fox', commentText: 'Define this term.', paragraphHint: 0 }),
      comment({ id: 'c2', anchorText: 'lazy dog', commentText: 'Cut this sentence.', paragraphHint: 1 }),
    ]);

    expect(threads).toHaveLength(2);
    expect(threads.map(t => t.text)).toEqual(['Define this term.', 'Cut this sentence.']);
    expect(threads.every(t => t.replies.length === 0)).toBe(true);
  });

  it('gives an unreplied comment a thread with an empty replies list', () => {
    const threads = groupImportedComments([
      comment({ id: 'c1', anchorText: 'quick brown fox', commentText: 'Solo comment.' }),
    ]);
    expect(threads).toHaveLength(1);
    expect(threads[0].replies).toEqual([]);
  });

  it('keeps comments with empty/missing anchorText as separate (never merged) singleton threads', () => {
    const threads = groupImportedComments([
      comment({ id: 'c1', anchorText: '', commentText: 'First orphan.' }),
      comment({ id: 'c2', anchorText: '', commentText: 'Second orphan.' }),
    ]);
    expect(threads).toHaveLength(2);
    expect(threads[0].anchorText).toBeUndefined();
    expect(threads[1].anchorText).toBeUndefined();
  });

  it('is a no-op for an empty/undefined comment list', () => {
    expect(groupImportedComments([])).toEqual([]);
    expect(groupImportedComments(undefined)).toEqual([]);
  });
});

describe('applyImportedCommentAnchors (FR-25, task 051) — mark placement over a live editor', () => {
  it('anchors a thread over its root comment span (paraId primary)', () => {
    const editor = makeEditor('<p>The quick brown fox jumps.</p><p>The lazy dog sleeps.</p>');
    stampParaIds(editor, [
      { index: 0, paraId: 'AAAA0001', isMinted: false },
      { index: 1, paraId: 'BBBB0002', isMinted: false },
    ]);

    const result = applyImportedCommentAnchors(editor, [
      comment({ id: 'c1', anchorText: 'quick brown fox', paragraphHint: 0, paraId: 'AAAA0001' }),
    ]);

    expect(result).toEqual({ applied: 1, unresolved: 0 });
    const html = editor.getHTML();
    expect(html).toContain('compose-mark-comment-anchor');
    expect(html).toContain(`data-comment-id="${IMPORTED_COMMENT_THREAD_PREFIX}c1"`);
    editor.destroy();
  });

  it('anchors ONE mark per thread (root span only) even when a reply shares the same anchor', () => {
    const editor = makeEditor('<p>The quick brown fox jumps.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const result = applyImportedCommentAnchors(editor, [
      comment({ id: 'c1', anchorText: 'quick brown fox', paragraphHint: 0, paraId: 'AAAA0001' }),
      comment({ id: 'c2', anchorText: 'quick brown fox', paragraphHint: 0, paraId: 'AAAA0001' }),
    ]);

    // Two source comments, but they share one anchor → ONE thread → ONE anchor mark application.
    expect(result).toEqual({ applied: 1, unresolved: 0 });
    editor.destroy();
  });

  it('falls back to fuzzy anchoring (anchorText + hint) when the paraId is stale (cross-Word-session)', () => {
    const editor = makeEditor('<p>The quick brown fox jumps.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const result = applyImportedCommentAnchors(editor, [
      comment({ id: 'c1', anchorText: 'quick brown fox', paragraphHint: 0, paraId: 'DEADBEEF' }),
    ]);

    expect(result).toEqual({ applied: 1, unresolved: 0 });
    expect(editor.getHTML()).toContain('compose-mark-comment-anchor');
    editor.destroy();
  });

  it('counts an un-anchorable thread as unresolved and renders no mark — but the thread is not dropped by the pure projection', () => {
    const editor = makeEditor('<p>Entirely unrelated content.</p>');
    stampParaIds(editor, [{ index: 0, paraId: 'AAAA0001', isMinted: false }]);

    const comments = [
      comment({ id: 'c1', anchorText: 'A different paragraph that is nowhere.', paragraphHint: 9, paraId: 'ZZZZ9999' }),
    ];

    const result = applyImportedCommentAnchors(editor, comments);
    expect(result).toEqual({ applied: 0, unresolved: 1 });
    expect(editor.getHTML()).not.toContain('compose-mark-comment-anchor');

    // The panel-seeding projection still surfaces the thread (author/timestamp/text) even though its
    // in-document highlight could not be placed — comments are never silently dropped.
    expect(groupImportedComments(comments)).toHaveLength(1);
    editor.destroy();
  });

  it('is a no-op for an empty/undefined comment list', () => {
    const editor = makeEditor('<p>Untouched.</p>');
    expect(applyImportedCommentAnchors(editor, [])).toEqual({ applied: 0, unresolved: 0 });
    expect(applyImportedCommentAnchors(editor, undefined)).toEqual({ applied: 0, unresolved: 0 });
    expect(editor.getHTML()).toContain('Untouched.');
    expect(editor.getHTML()).not.toContain('compose-mark-comment-anchor');
    editor.destroy();
  });
});
