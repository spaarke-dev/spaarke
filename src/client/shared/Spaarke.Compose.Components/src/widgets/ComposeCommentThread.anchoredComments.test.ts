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
  const p2 = schema.nodes.paragraph.create(
    { paraId: '0A000002' },
    schema.text('spilling into the next paragraph', [mark])
  );
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

// UAT-22 (2026-08-18, honest/safe): a comment the user sees in the gutter whose live anchor no longer
// resolves is dropped from the save payload. It MUST be surfaced (counted), never silently lost — but a
// thread legitimately skipped because it already rides the imported baseline is NOT a loss and must NOT
// fire the sink.
describe('composeSessionCommentThreadsToAnchoredComments — onDropped sink surfaces silent drops (UAT-22)', () => {
  it('fires onDropped with anchor-mark-missing for a thread whose anchor is gone', () => {
    const doc = crossParagraphDoc('c1');
    const dropped: Array<{ id: string; reason: string }> = [];
    const result = composeSessionCommentThreadsToAnchoredComments(
      doc,
      [thread('no-such-mark')],
      new Set(),
      (id, reason) => dropped.push({ id, reason })
    );
    expect(result).toHaveLength(0);
    expect(dropped).toEqual([{ id: 'no-such-mark', reason: 'anchor-mark-missing' }]);
  });

  it('does NOT fire onDropped for an imported-thread skip (not a loss — already in the baseline)', () => {
    const doc = crossParagraphDoc('c1');
    const dropped: string[] = [];
    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c1')], new Set(['c1']), id =>
      dropped.push(id)
    );
    expect(result).toHaveLength(0);
    expect(dropped).toHaveLength(0);
  });

  it('does NOT fire onDropped for a thread that anchors successfully', () => {
    const doc = crossParagraphDoc('c1');
    const dropped: string[] = [];
    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c1')], new Set(), id =>
      dropped.push(id)
    );
    expect(result).toHaveLength(1);
    expect(dropped).toHaveLength(0);
  });
});

/**
 * Agreements-r1 UAT round-1 #4 (agent-C, 2026-08-03) — the anchor-drift TRIGGER for the
 * "Save error: A change could not be anchored in the document" 422.
 *
 * `resolveRunAnchor` already threads the ROBUST task-055 `paraOffset` (the paragraph-relative
 * char offset) onto every anchor it returns, and every OP the interceptor emits carries it — so a
 * text edit resolves server-side across the TipTap↔OOXML run-merge boundary. Advisory comment ranges
 * did NOT: the serializer projected only the legacy `(runIndex, offset)`, dropping `paraOffset`, so a
 * comment on a multi-run paragraph (TipTap merges same-format runs, so its runIndex disagrees with
 * OOXML's fine-grained runs) refused with an anchor error on save. Carrying `paraOffset` through makes
 * a comment anchor as robust as a text-edit anchor — still purely numeric (never a text match, I-7).
 */
describe('composeSessionCommentThreadsToAnchoredComments — robust paraOffset anchor (agreements-r1 UAT #4)', () => {
  it('carries the paragraph-relative paraOffset on both range endpoints (parity with text-edit ops)', () => {
    const mark = schema.marks.commentAnchor.create({ commentId: 'c2' });
    const p1 = schema.nodes.paragraph.create({ paraId: '0B000001' }, [
      schema.text('Alpha ', []), // paragraph offsets 0..6
      schema.text('flagged clause', [mark]), // marked span: paragraph offsets 6..20
      schema.text(' omega', []),
    ]);
    const doc = schema.nodes.doc.create(null, [p1]);

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread('c2')], new Set());
    expect(result).toHaveLength(1);
    // The marked span "flagged clause" begins at paragraph char-offset 6 and ends at 20.
    expect(result[0].range.start.paraOffset).toBe(6);
    expect(result[0].range.end.paraOffset).toBe(20);
  });
});
