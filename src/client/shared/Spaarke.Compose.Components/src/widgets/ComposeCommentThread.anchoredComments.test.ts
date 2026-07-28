/**
 * ComposeCommentThread.anchoredComments.test.ts — UAT round-3 (2026-07-27) regression for the
 * "advisory comments don't reach Word" bug (#10).
 *
 * Root cause fixed here: {@link composeSessionCommentThreadsToAnchoredComments} required a comment's
 * start and end to resolve to the SAME paragraph (`start.paraId !== end.paraId → continue`). NDA
 * advisory comments anchor to long multi-sentence excerpts that CROSS paragraph boundaries, so every
 * one was silently dropped → `getAnchoredComments()` returned [] → the save sent zero comments → none
 * baked as native `w:comment`. The fix clamps a cross-paragraph comment to its START paragraph instead
 * of dropping it. These tests lock that behavior at the pure-function layer (no editor/DOM mount).
 *
 * Uses a minimal hand-built ProseMirror schema (paragraph carries a `paraId` attr like
 * `paraIdExtension`; a `commentAnchor` mark carries `commentId` like `CommentAnchorMark`) so the
 * mapping is tested in isolation — mirrors `stepOperationInterceptor.test.ts`'s harness convention.
 */
import { Schema } from '@tiptap/pm/model';
import type { Node as PMNode } from '@tiptap/pm/model';
import {
  composeSessionCommentThreadsToAnchoredComments,
  type ComposeCommentThreadModel,
} from './ComposeCommentThread.types';

const schema = new Schema({
  nodes: {
    doc: { content: 'block+' },
    paragraph: {
      group: 'block',
      content: 'inline*',
      attrs: { paraId: { default: null } },
      parseDOM: [{ tag: 'p' }],
      toDOM: () => ['p', 0],
    },
    text: { group: 'inline' },
  },
  marks: {
    commentAnchor: {
      attrs: { commentId: { default: null } },
      toDOM: mark => ['span', { 'data-comment-id': (mark.attrs as { commentId: string }).commentId }, 0],
    },
  },
});

function thread(id: string): ComposeCommentThreadModel {
  return {
    id,
    text: 'Advisory finding text',
    author: 'AI Advisory Review',
    timestamp: '2026-07-27T00:00:00.000Z',
    resolved: false,
    replies: [],
  } as ComposeCommentThreadModel;
}

/** A 2-paragraph doc whose commentAnchor mark (commentId) spans BOTH paragraphs. */
function crossParagraphDoc(commentId: string): PMNode {
  const mark = schema.marks.commentAnchor.create({ commentId });
  const p1 = schema.nodes.paragraph.create({ paraId: '0A000001' }, schema.text('First clause excerpt', [mark]));
  const p2 = schema.nodes.paragraph.create({ paraId: '0A000002' }, schema.text('spilling into the next paragraph', [mark]));
  return schema.nodes.doc.create(null, [p1, p2]);
}

describe('composeSessionCommentThreadsToAnchoredComments — cross-paragraph clamp (#10)', () => {
  it('clamps a cross-paragraph advisory comment to its START paragraph instead of dropping it', () => {
    const doc = crossParagraphDoc('c1');
    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c1')], new Set());

    expect(result).toHaveLength(1); // was 0 before the fix — the whole comment was dropped
    expect(result[0].paraId).toBe('0A000001'); // anchored to the paragraph it STARTS in
    expect(result[0].commentText).toBe('Advisory finding text');
    // The clamped range stays within the start paragraph (a valid single-paragraph w:comment span).
    expect(result[0].range.start.runIndex).toBe(0);
    expect(result[0].range.end.offset).toBeGreaterThan(result[0].range.start.offset);
  });

  it('anchors a single-paragraph comment within that paragraph unchanged', () => {
    const mark = schema.marks.commentAnchor.create({ commentId: 'c2' });
    const p1 = schema.nodes.paragraph.create({ paraId: '0B000001' }, [
      schema.text('Alpha ', []),
      schema.text('flagged clause', [mark]),
      schema.text(' omega', []),
    ]);
    const doc = schema.nodes.doc.create(null, [p1]);

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c2')], new Set());
    expect(result).toHaveLength(1);
    expect(result[0].paraId).toBe('0B000001');
  });

  it('skips a thread whose anchor mark is absent (do-not-guess, I-7)', () => {
    const doc = crossParagraphDoc('c1');
    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('no-such-mark')], new Set());
    expect(result).toHaveLength(0);
  });

  it('skips an imported thread id (handled by the imported round-trip path, not re-baked here)', () => {
    const doc = crossParagraphDoc('c1');
    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c1')], new Set(['c1']));
    expect(result).toHaveLength(0);
  });
});
