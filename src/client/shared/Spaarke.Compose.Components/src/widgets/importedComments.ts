/**
 * importedComments.ts — project existing Word comments into FR-23 comment threads (FR-25, task 051).
 *
 * Project: spaarkeai-compose-r3, task 051 (import round-trip — existing comments, the in-editor render
 * half). Deps: task 044 (FR-23 `ComposeCommentThread` UI + `useComposeCommentThreads` state) and task 050
 * (the identical mount-contract/anchor-resolution pattern this module mirrors for comments).
 *
 * WHY THIS EXISTS (design §7 — "the reader is not the gap, the editor mount is"): the load-time `.docx`
 * arrives in the editor via mammoth (`docxBridge.docxToTipTapHtml`), which drops native `w:comment`
 * anchors entirely — a Word-commented document opens with its comments silently gone. The BFF already
 * recovers every comment on Load (the EXISTING `DocxAnnotationReader`, reused verbatim — see
 * `ComposeService.LoadAsync`) and projects them onto the Load response as {@link ImportedComment}s
 * carrying the E2 `w14:paraId`. This module is the client render half: it (a) GROUPS same-anchor
 * comments into a {@link ComposeCommentThreadModel} — a legacy (non-modern-comments) `.docx` represents a
 * "reply" as a second `<w:comment>` anchored to the SAME span, in document order (the ONLY shape
 * `DocxAnnotationReader` can recover — see `ComposeCommentThread.types.ts` SHAPE RATIONALE), so grouping
 * by shared `anchorText` (first comment on a span = thread root, the rest = its flat replies) is exact,
 * not a heuristic — and (b) applies the SAME `commentAnchor` mark (task 031, R2) the create-a-thread
 * gesture uses, so an imported thread is visually anchored + reply-able through the FR-23 UI exactly like
 * a thread created in-session.
 *
 * DEPTH CAP (spec Assumptions, binding): `DocxAnnotationReader` reads ONLY `w:comment` +
 * `CommentRangeStart/End` — it carries no `commentsExtended`/`commentsIds`/`commentsExtensible` (the
 * modern-comments 4-part structure Word uses to encode an actual reply TREE). Every thread this module
 * produces is therefore FLAT BY CONSTRUCTION — there is no deeper structure to lose or reconstruct; the
 * cap is satisfied by the reader's own scope, not by a truncation step here.
 *
 * ANCHORING (design §5.2 / project MUST): `w14:paraId` is the PRIMARY anchor, with the SAME `anchorText`
 * + `paragraphHint` fuzzy fallback task 050's `importedRevisions.ts` uses for the cross-Word-session case
 * (Word regenerates paraIds on an external save). This module REUSES that module's exported
 * `collectBlocks`/`resolveBlock`/`blockCharIndex` helpers rather than forking a parallel anchor resolver.
 *
 * TWO SEPARATE CONCERNS, deliberately split:
 *  - {@link groupImportedComments} — PURE (no editor). Produces the `ComposeCommentThreadModel[]` for
 *    `ComposeCommentThread`'s `initialThreads` prop, which seeds its `useComposeCommentThreads` state on
 *    FIRST RENDER — before the docx mount effect (below) has run. Called from a `useMemo` in
 *    `ComposeEditor`, independent of `editor`/mount timing.
 *  - {@link applyImportedCommentAnchors} — IMPERATIVE (needs a live `editor`). Applies the visual
 *    `commentAnchor` mark over each thread's anchored span in the editor document. Called from the SAME
 *    docx-mount effect `applyImportedRevisions` (task 050) runs in, AFTER `stampParaIds` (exact paraId
 *    anchoring) and BEFORE `captureParaIdSnapshot` (so an imported comment's anchor mark is folded into
 *    the load-time reject-state baseline and is never mistaken for a user edit on the next save). Both
 *    functions derive the SAME thread id from the SAME root comment (`threadIdFor`), so a thread seeded
 *    via `initialThreads` and a mark applied via this function refer to the identical thread.
 *
 * Marks are applied with `addToHistory: false` — a load-time render of pre-existing comments is not a
 * user edit and must not be undoable (mirrors `importedRevisions.ts`).
 *
 * Privacy (ADR-015 Tier 3): `comment.commentText` / `comment.anchorText` are document content — carried
 * in-memory to the render only, NEVER logged.
 *
 * @see ./importedRevisions.ts — the FR-24 sibling this module mirrors conventions + anchor-resolution from
 * @see ./ComposeCommentThread.types.ts — `ComposeCommentThreadModel` / `ComposeCommentReply` + SHAPE RATIONALE
 * @see ./hooks/useComposeCommentThreads.ts — `importThreads` (the live-update seam; `initialThreads` is the
 *      first-render seam this module targets)
 * @see ./marks/CommentAnchorMark.ts — the anchoring mark this module applies (reused, not reimplemented)
 * @see projects/spaarkeai-compose-r3/design.md §7 (import round-trip)
 */
import type { Editor } from '@tiptap/core';
import type { ImportedComment } from '../types/compose-contracts';
import { collectBlocks, resolveBlock, blockCharIndex } from './importedRevisions';
import type { ComposeCommentReply, ComposeCommentThreadModel } from './ComposeCommentThread.types';

const COMMENT_ANCHOR_MARK = 'commentAnchor';

/** Stable id prefix for a thread/reply minted from an imported comment — distinguishes it from a
 * thread/reply created in-session via `useComposeCommentThreads.createThread`/`reply` (which mint
 * `thread-{uuid}`/`reply-{uuid}`). Mirrors `importedRevisions.ts`'s `IMPORTED_LEDGER_PREFIX` convention. */
export const IMPORTED_COMMENT_THREAD_PREFIX = 'imported-thread:';
const IMPORTED_COMMENT_REPLY_PREFIX = 'imported-reply:';

/** Outcome of an {@link applyImportedCommentAnchors} pass — how many threads' anchors placed vs could not. */
export interface ApplyImportedCommentAnchorsResult {
  /** Thread anchors placed as a visible `commentAnchor` mark. */
  applied: number;
  /** Thread anchors that could not be located in the editor (no paraId match + no fuzzy match, or empty
   * anchorText) — the thread STILL appears in the panel (via {@link groupImportedComments}); only its
   * in-document highlight is absent. */
  unresolved: number;
}

interface CommentGroup {
  root: ImportedComment;
  replies: ImportedComment[];
}

/** Stable, distinguishing thread id for a group's root comment (its OOXML id, else a paraId+index key). */
function threadIdFor(root: ImportedComment): string {
  const id = root.id && root.id.length > 0 ? root.id : `${root.paraId ?? 'p'}#${root.paragraphHint}`;
  return `${IMPORTED_COMMENT_THREAD_PREFIX}${id}`;
}

/** Stable reply id for a non-root comment in a group. */
function replyIdFor(reply: ImportedComment, index: number): string {
  const id = reply.id && reply.id.length > 0 ? reply.id : `${reply.paraId ?? 'p'}#${reply.paragraphHint}#${index}`;
  return `${IMPORTED_COMMENT_REPLY_PREFIX}${id}`;
}

/**
 * Group recovered comments sharing the SAME `anchorText` into threads — the first comment recovered on a
 * span becomes the root, every subsequent comment on that SAME span becomes an ordered, flat reply (the
 * exact shape a legacy `.docx` multi-comment-on-one-span encodes; see file header SHAPE RATIONALE). A
 * comment with empty/missing `anchorText` (the reader's soft-fail for an unresolvable range — never
 * merged with another empty-anchor comment, each gets its own singleton group keyed by its own id).
 * Preserves the reader's document-order input order within and across groups.
 */
function groupCommentsByAnchor(comments: readonly ImportedComment[]): CommentGroup[] {
  const order: string[] = [];
  const byKey = new Map<string, ImportedComment[]>();

  comments.forEach((comment, index) => {
    const key =
      comment.anchorText && comment.anchorText.trim().length > 0
        ? comment.anchorText
        : `__unanchored__#${comment.id || index}`;
    const existing = byKey.get(key);
    if (existing) {
      existing.push(comment);
    } else {
      byKey.set(key, [comment]);
      order.push(key);
    }
  });

  return order.map(key => {
    const members = byKey.get(key)!;
    const [root, ...replies] = members;
    return { root, replies };
  });
}

/**
 * PURE projection (no editor) — group recovered comments into {@link ComposeCommentThreadModel}s for
 * `ComposeCommentThread`'s `initialThreads` prop (seeds the panel's thread list on first render, before
 * the docx-mount effect that anchors them in the document has run — see file header). `resolved` is
 * always `false` (a legacy `.docx` carries no resolved/done attribute — see
 * `ComposeCommentThread.types.ts` `ComposeCommentThreadModel.resolved` remarks). Every reply list is FLAT
 * (see file header DEPTH CAP). Returns `[]` for an empty/undefined list.
 */
export function groupImportedComments(comments: readonly ImportedComment[] | undefined): ComposeCommentThreadModel[] {
  if (!comments || comments.length === 0) return [];

  return groupCommentsByAnchor(comments).map(
    ({ root, replies }): ComposeCommentThreadModel => ({
      id: threadIdFor(root),
      author: root.author,
      timestamp: root.date,
      text: root.commentText,
      anchorText: root.anchorText && root.anchorText.length > 0 ? root.anchorText : undefined,
      resolved: false,
      replies: replies.map(
        (reply, index): ComposeCommentReply => ({
          id: replyIdFor(reply, index),
          author: reply.author,
          timestamp: reply.date,
          text: reply.commentText,
        })
      ),
    })
  );
}

/** Locate `anchorText` in the block's live prose and apply the `commentAnchor` mark carrying `commentId`. */
function applyCommentAnchorMark(
  editor: Editor,
  block: { from: number; to: number },
  anchorText: string,
  threadId: string
): boolean {
  if (anchorText.length === 0) return false;
  const markType = editor.state.schema.marks[COMMENT_ANCHOR_MARK];
  if (!markType) return false;

  const { text, positions } = blockCharIndex(editor, block.from, block.to);
  const idx = text.indexOf(anchorText);
  if (idx === -1) return false;

  const from = positions[idx];
  const to = positions[idx + anchorText.length - 1] + 1;
  const mark = markType.create({ commentId: threadId });
  const tr = editor.state.tr.addMark(from, to, mark);
  tr.setMeta('addToHistory', false);
  editor.view.dispatch(tr);
  return true;
}

/**
 * IMPERATIVE (needs a live `editor`) — anchor each imported comment thread's root span with the visible
 * `commentAnchor` mark (the SAME mark `useComposeCommentThreads.createThread` applies), keyed by the
 * SAME thread id {@link groupImportedComments} used to seed the panel. Call ONCE per `.docx` mount,
 * AFTER `stampParaIds` (exact paraId anchoring) and BEFORE `captureParaIdSnapshot` (so the anchor mark is
 * folded into the load-time reject-state baseline, not read as a user edit on the next save). No-op when
 * `comments` is empty. A thread whose span cannot be located (no paraId + no fuzzy anchorText match)
 * counts as `unresolved` — it still renders in the FR-23 panel (seeded via `groupImportedComments`), just
 * without an in-document highlight.
 *
 * @param editor   the live TipTap editor (must have the `commentAnchor` mark in schema)
 * @param comments the imported comments from the Load response (`ImportedComment[]`), in document order
 * @returns how many thread anchors placed vs could not (diagnostic only — never throws)
 */
export function applyImportedCommentAnchors(
  editor: Editor | null,
  comments: readonly ImportedComment[] | undefined
): ApplyImportedCommentAnchorsResult {
  if (!editor || !comments || comments.length === 0) {
    return { applied: 0, unresolved: 0 };
  }

  let applied = 0;
  let unresolved = 0;

  for (const { root } of groupCommentsByAnchor(comments)) {
    const blocks = collectBlocks(editor);
    const block = resolveBlock(blocks, {
      paraId: root.paraId,
      paragraphHint: root.paragraphHint,
      anchorText: root.anchorText,
    });
    const ok = block ? applyCommentAnchorMark(editor, block, root.anchorText, threadIdFor(root)) : false;
    if (ok) {
      applied++;
    } else {
      unresolved++;
    }
  }

  return { applied, unresolved };
}

export default groupImportedComments;
