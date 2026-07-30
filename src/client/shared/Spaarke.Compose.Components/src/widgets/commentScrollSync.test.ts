/**
 * commentScrollSync.test.ts — G9 (FR-08, task 031) unit coverage for the Comments-pane ⇄ document
 * scroll-sync helpers. Focuses on the PURE position-picking core (jsdom-safe, no layout needed) plus
 * the editor-scroll action wired over a chainable editor mock.
 */

import type { Editor } from '@tiptap/react';
import {
  pickActiveThreadId,
  scrollEditorToThreadAnchor,
  type ThreadAnchorPosition,
} from './commentScrollSync';

describe('pickActiveThreadId (doc→pane active-tracking core)', () => {
  const positions: ThreadAnchorPosition[] = [
    { threadId: 't-top', pos: 5 },
    { threadId: 't-mid', pos: 40 },
    { threadId: 't-bot', pos: 120 },
  ];

  it('returns null when there are no anchored threads', () => {
    expect(pickActiveThreadId([], 50)).toBeNull();
  });

  it('picks the thread whose anchor is nearest AT/ABOVE the viewport top', () => {
    // viewport top at pos 60 → the mid anchor (40) is the greatest pos <= 60.
    expect(pickActiveThreadId(positions, 60)).toBe('t-mid');
  });

  it('picks the exact thread when the viewport top sits on an anchor', () => {
    expect(pickActiveThreadId(positions, 40)).toBe('t-mid');
  });

  it('picks the last thread once scrolled past the last anchor', () => {
    expect(pickActiveThreadId(positions, 999)).toBe('t-bot');
  });

  it('falls back to the topmost thread when the viewport is above every anchor', () => {
    // top of document (pos 0) — nothing is at/above it, so the first (topmost) thread is active.
    expect(pickActiveThreadId(positions, 0)).toBe('t-top');
  });

  it('is stable for a single-thread pane', () => {
    const one: ThreadAnchorPosition[] = [{ threadId: 'only', pos: 10 }];
    expect(pickActiveThreadId(one, 0)).toBe('only');
    expect(pickActiveThreadId(one, 10)).toBe('only');
    expect(pickActiveThreadId(one, 500)).toBe('only');
  });
});

describe('scrollEditorToThreadAnchor (pane→doc jump)', () => {
  it('returns false when the editor is null', () => {
    expect(scrollEditorToThreadAnchor(null, 't-1')).toBe(false);
  });

  it('scrolls the editor to the anchor span when the mark is present', () => {
    const run = jest.fn().mockReturnValue(true);
    const scrollIntoView = jest.fn().mockReturnValue({ run });
    const setTextSelection = jest.fn().mockReturnValue({ scrollIntoView });
    const chain = jest.fn().mockReturnValue({ setTextSelection });

    // A doc containing a text node carrying the commentAnchor mark for 't-present'.
    const editor = {
      chain,
      state: {
        doc: makeDocWithCommentMark('t-present'),
      },
    } as unknown as Editor;

    const scrolled = scrollEditorToThreadAnchor(editor, 't-present');

    expect(scrolled).toBe(true);
    expect(chain).toHaveBeenCalledTimes(1);
    expect(setTextSelection).toHaveBeenCalledWith(expect.objectContaining({ from: expect.any(Number), to: expect.any(Number) }));
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('is a no-op (returns false) when the thread anchor mark is gone', () => {
    const chain = jest.fn();
    const editor = {
      chain,
      state: { doc: makeDocWithCommentMark('t-other') },
    } as unknown as Editor;

    expect(scrollEditorToThreadAnchor(editor, 't-missing')).toBe(false);
    expect(chain).not.toHaveBeenCalled();
  });
});

/**
 * Minimal ProseMirror-doc stand-in that `findCommentAnchorRange` can walk: it only calls
 * `doc.descendants((node, pos) => …)` and reads `node.isText` + `node.marks` + `node.nodeSize`.
 */
function makeDocWithCommentMark(commentId: string) {
  const textNode = {
    isText: true,
    nodeSize: 10,
    marks: [{ type: { name: 'commentAnchor' }, attrs: { commentId } }],
  };
  return {
    descendants: (fn: (node: unknown, pos: number) => boolean | void) => {
      fn(textNode, 3);
    },
  };
}
