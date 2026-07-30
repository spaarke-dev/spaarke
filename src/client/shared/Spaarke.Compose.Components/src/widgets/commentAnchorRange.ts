/**
 * commentAnchorRange.ts — the pure `commentAnchor`-mark range primitive (leaf module).
 *
 * Extracted from `ComposeCommentThread.types.ts` (task 031) so it can be imported by the G9 scroll-sync
 * helpers WITHOUT dragging that file's persistence-vocabulary imports (`useComposeWordShuttle` →
 * `@spaarke/auth`) into a leaf consumer. `ComposeCommentThread.types.ts` re-exports both symbols, so
 * every existing import site is unaffected.
 *
 * Depends only on the ProseMirror model TYPE — no runtime dependency on any Spaarke sibling package.
 */

import type { Node as PMNode } from '@tiptap/pm/model';

/** The `commentAnchor` mark name (mirrors `./marks/CommentAnchorMark.ts` +
 * `./hooks/useComposeCommentThreads.ts`'s `COMMENT_ANCHOR_MARK`). */
export const COMMENT_ANCHOR_MARK_NAME = 'commentAnchor';

/**
 * Locate the CURRENT ProseMirror range of a `commentAnchor` mark by its `commentId` attribute,
 * spanning every text node carrying it (a mark can render as several adjacent text nodes when other
 * marks split them). Returns `null` when the mark is no longer present anywhere in `doc` — e.g. a later
 * edit deleted the anchored text. The caller treats a `null` result as "this thread's anchor no longer
 * exists," never guessing a replacement span (I-7 discipline).
 */
export function findCommentAnchorRange(doc: PMNode, commentId: string): { from: number; to: number } | null {
  let from: number | null = null;
  let to: number | null = null;
  doc.descendants((node, pos) => {
    if (!node.isText) return true;
    const hasMark = node.marks.some(
      m => m.type.name === COMMENT_ANCHOR_MARK_NAME && (m.attrs as { commentId?: string }).commentId === commentId
    );
    if (hasMark) {
      const nodeFrom = pos;
      const nodeTo = pos + node.nodeSize;
      from = from === null ? nodeFrom : Math.min(from, nodeFrom);
      to = to === null ? nodeTo : Math.max(to, nodeTo);
    }
    return true;
  });
  return from === null || to === null ? null : { from, to };
}
