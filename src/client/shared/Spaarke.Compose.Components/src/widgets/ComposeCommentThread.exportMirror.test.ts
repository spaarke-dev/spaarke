/**
 * ComposeCommentThread.exportMirror.test.ts — ai-advanced-capabilities-agreements-r1 task 052
 * (FR-15, Word-comment export mirror).
 *
 * Mapping-level (pure-function) proof that `composeSessionCommentThreadsToAnchoredComments` — the
 * LIVE save path (`ComposeEditor.getAnchoredComments`) — composes the SAME structured
 * "Flagged clause / Assessment says / Standard" text the on-screen gutter renders, instead of the
 * pre-052 raw `thread.text` passthrough. Mirrors `ComposeCommentThread.anchoredComments.test.ts`'s
 * hand-built ProseMirror schema harness (that file stays scoped to the cross-paragraph-clamp bug;
 * this one is scoped to the export-composition fix).
 *
 * Covers the task's acceptance criteria at the mapping level (the layer this task CAN verify
 * in-repo — the Word-open verification is deferred to the deploy-wave UAT, see the task's
 * execution notes):
 *  - structured export from discrete task-002 fields (no string-parsing)
 *  - legacy-thread graceful degrade (no crash, no fabricated structure)
 *  - save→reopen round-trip: the SAME thread shape re-run through the mapping produces the SAME
 *    exported text (proves the mapping is a pure, deterministic function of the thread's fields)
 *  - a recalled-thread fixture (durable-recall parity, tasks 030-032 coordination) exports
 *    identically to an equivalent live thread
 *  - a plain session comment (no advisory metadata) is unaffected
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

/** A single-paragraph doc whose commentAnchor mark (commentId) spans the given text. */
function singleParagraphDoc(
  commentId: string,
  text = 'The receiving party shall retain records indefinitely.'
): PMNode {
  const mark = schema.marks.commentAnchor.create({ commentId });
  const p1 = schema.nodes.paragraph.create({ paraId: '0A000001' }, schema.text(text, [mark]));
  return schema.nodes.doc.create(null, [p1]);
}

function baseThread(overrides: Partial<ComposeCommentThreadModel> & { id: string }): ComposeCommentThreadModel {
  return {
    author: 'AI Advisory Review',
    timestamp: '2026-07-31T00:00:00.000Z',
    text: 'fallback text',
    resolved: false,
    replies: [],
    ...overrides,
  } as ComposeCommentThreadModel;
}

describe('composeSessionCommentThreadsToAnchoredComments — export mirror (task 052, FR-15)', () => {
  it('composes structured commentText from discrete flaggedClause/assessment/standardRef fields (no string-parsing)', () => {
    const doc = singleParagraphDoc('c1');
    const thread = baseThread({
      id: 'c1',
      sectionRef: '4.2',
      riskLevel: 'High',
      flaggedClause: 'The clause allows unilateral termination without notice.',
      assessment: 'This removes the standard 30-day cure period.',
      standardRef: 'B9 - Termination rights',
    });

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread], new Set());

    expect(result).toHaveLength(1);
    expect(result[0].commentText).toBe(
      'Flagged clause: The clause allows unilateral termination without notice.\n\n' +
        'Assessment says: This removes the standard 30-day cure period.\n\n' +
        'Standard: B9 - Termination rights'
    );
    expect(result[0].author).toBe('AI Advisory Review');
  });

  it('legacy thread (pre-002, marker-parsed text only) degrades gracefully — relabels markers, lifts standardRef, never crashes', () => {
    const doc = singleParagraphDoc('c2');
    const thread = baseThread({
      id: 'c2',
      text: 'Grounded fact: A best-efforts standard. Advisory judgment: Needs reasonable care.',
      sectionRef: '3.2',
      standardRef: 'B5',
    });

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread], new Set());

    expect(result).toHaveLength(1);
    expect(result[0].commentText).toBe(
      'Flagged clause: A best-efforts standard.\n\nAssessment says: Needs reasonable care.\n\nStandard: B5'
    );
  });

  it('legacy advisory thread with unstructured text (no markers) exports the raw text, unfabricated, plus Standard', () => {
    const doc = singleParagraphDoc('c3');
    const thread = baseThread({
      id: 'c3',
      text: 'The retention period is unusually long for this industry.',
      riskLevel: 'Medium',
      standardRef: 'B2',
    });

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread], new Set());

    expect(result).toHaveLength(1);
    expect(result[0].commentText).toBe('The retention period is unusually long for this industry.\n\nStandard: B2');
    // No crash, no invented "Flagged clause"/"Assessment says" labels on unstructured legacy text.
    expect(result[0].commentText).not.toContain('Flagged clause');
  });

  it('a plain session comment (no advisory metadata at all) exports its text completely unchanged', () => {
    const doc = singleParagraphDoc('c4', 'Some ordinary document sentence.');
    const thread = baseThread({ id: 'c4', text: 'Please double-check this figure.' });

    const result = composeSessionCommentThreadsToAnchoredComments(doc, [thread], new Set());

    expect(result).toHaveLength(1);
    expect(result[0].commentText).toBe('Please double-check this figure.');
  });

  it('save→reopen round-trip: re-running the SAME thread shape through the mapping produces IDENTICAL export text', () => {
    const doc1 = singleParagraphDoc('c5');
    const thread = baseThread({
      id: 'c5',
      sectionRef: '2.1',
      flaggedClause: 'The confidentiality obligation survives for 10 years post-termination.',
      assessment: 'This exceeds the firm standard of 3 years.',
      standardRef: 'B3 - Survival period',
    });

    const firstSave = composeSessionCommentThreadsToAnchoredComments(doc1, [thread], new Set());

    // "Reopen": a fresh doc instance (same shape) + the identical thread object — the mapping is a
    // pure function of (doc, thread), so re-running it must reproduce the exact same commentText.
    const doc2 = singleParagraphDoc('c5');
    const secondSave = composeSessionCommentThreadsToAnchoredComments(doc2, [thread], new Set());

    expect(secondSave[0].commentText).toBe(firstSave[0].commentText);
  });

  it('durable-recall parity: a recalled thread (same discrete fields, different id/timestamp/provenance) exports identically to a live one', () => {
    const liveDoc = singleParagraphDoc('live-1');
    const liveThread = baseThread({
      id: 'live-1',
      sectionRef: '5.4',
      riskLevel: 'High',
      flaggedClause: 'The indemnification clause has no cap.',
      assessment: 'Uncapped indemnity is materially riskier than the firm standard.',
      standardRef: 'B12 - Indemnification cap',
    });
    const liveResult = composeSessionCommentThreadsToAnchoredComments(liveDoc, [liveThread], new Set());

    // Recalled thread: re-materialized via placeAdvisoryComments on reopen (tasks 030-032) — new id
    // + timestamp (different provenance), but the SAME discrete advisory fields.
    const recalledDoc = singleParagraphDoc('recalled-1');
    const recalledThread = baseThread({
      ...liveThread,
      id: 'recalled-1',
      timestamp: '2026-08-05T12:00:00.000Z',
    });
    const recalledResult = composeSessionCommentThreadsToAnchoredComments(recalledDoc, [recalledThread], new Set());

    expect(recalledResult[0].commentText).toBe(liveResult[0].commentText);
  });
});
