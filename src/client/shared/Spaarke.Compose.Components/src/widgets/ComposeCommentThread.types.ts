/**
 * ComposeCommentThread.types.ts — comment-thread data model (spaarkeai-compose-r3 task 044, FR-23).
 *
 * R2 shipped `CommentAnchorMark.ts` (the anchoring rendering primitive, `data-comment-id`) and the
 * `w:comment` writer path (`DocxAnnotationWriter`), but no THREAD surface — no author/timestamp/
 * reply/resolve model sat on top of the anchors. This file is that model.
 *
 * SHAPE RATIONALE (binding — FR-23/FR-25, SCOPE GUARD): this is the SOLE thread shape in Compose —
 * no parallel/alternate shape is introduced. A thread is a root comment + an ORDERED, FLAT list of
 * replies; there is no parent/child reply TREE. This intentionally matches what a legacy (non-
 * modern-comments) `.docx` round-trip can represent: one or more `<w:comment>` elements anchored to
 * the SAME span, in document order — see {@link composeCommentThreadsToDocxAnnotations}. FR-25 (task
 * 051) will read recovered `RecoveredComment`s (BFF `DocxAnnotationReader`) — `{ id, author, date,
 * commentText, anchorText, paragraphHint }` — grouped by shared `anchorText` and project them
 * directly into a {@link ComposeCommentThreadModel} (the first recovered comment on a span becomes
 * the root, the rest become `replies`) — this shape is deliberately author/timestamp/text/anchorText-
 * compatible with that projection, so 051 needs no adapter shape of its own.
 *
 * SCOPE GUARD (binding, FR-23 Assumptions): view/create/reply/resolve ONLY — NOT full Word
 * comment-feature parity. Nested reply chains beyond one level render FLAT when the source lacks the
 * modern-comments 4-part structure (`w:commentEx` / `w:done` / `paraId` / `paraIdParent`) — see
 * {@link ComposeCommentReply.parentReplyId}.
 *
 * @see ./marks/CommentAnchorMark.ts — the anchoring mark threads render over (reused, not reimplemented)
 * @see ./hooks/useComposeCommentThreads.ts — the state hook building threads over the mark
 * @see ./ComposeCommentThread.tsx — the Fluent v9 thread UI
 * @see ./useComposeWordShuttle.ts — `DocxAnnotationInput` / `DocxTrackChangeKind` (the existing R2
 *      wire vocabulary this model maps onto for persistence — reused, not forked)
 * @see projects/spaarkeai-compose-r3/spec.md FR-23, FR-25, FR-26
 */
import { DocxTrackChangeKind, type DocxAnnotationInput } from './useComposeWordShuttle';

/** Author + timestamp stamp shared by a thread's root comment and every reply. */
export interface ComposeCommentAuthorStamp {
  /** Display name of the person (or e.g. "Spaarke Assistant") who authored this comment/reply. */
  author: string;
  /** ISO-8601 timestamp. */
  timestamp: string;
}

/**
 * One reply in a thread's flat reply list.
 *
 * `parentReplyId` is OPTIONAL provenance only — carried through from an imported modern-comments
 * `w:commentEx`/`paraIdParent` chain when a future import step supplies it. SCOPE GUARD (binding):
 * the UI NEVER builds a tree from it. Every reply always renders as a flat list entry, in thread
 * `replies` array order, regardless of `parentReplyId` — see `ComposeCommentThread.tsx`.
 */
export interface ComposeCommentReply extends ComposeCommentAuthorStamp {
  /** Stable reply id. */
  id: string;
  /** Reply body. */
  text: string;
  /** Optional imported-provenance parent reply id. Never used to build a render tree — see above. */
  parentReplyId?: string;
}

/** A comment thread: the root comment + its flat, ordered replies. */
export interface ComposeCommentThreadModel extends ComposeCommentAuthorStamp {
  /** Stable thread id — matches the anchoring `CommentAnchorMark`'s `data-comment-id`. */
  id: string;
  /** Root comment body. */
  text: string;
  /**
   * The anchored document span's text, captured at creation time (or `AnchorText` on import) — the
   * native `w:comment` `targetText` this thread's `<w:comment>` element(s) attach to on save. Absent
   * only for a thread that could not resolve an anchor (never produced by
   * {@link ../hooks/useComposeCommentThreads.useComposeCommentThreads}'s `createThread`, which
   * requires a non-collapsed selection).
   */
  anchorText?: string;
  /**
   * UI-only resolved flag. NOT written to native `w:comment` on save — legacy Word comments carry no
   * resolved/done attribute (that requires the modern-comments `commentsExtended.xml` structure, out
   * of SCOPE GUARD). A resolved thread renders collapsed/muted; re-opening a saved document with
   * recovered comments therefore always starts unresolved.
   */
  resolved: boolean;
  /** Ordered replies — always rendered FLAT (see {@link ComposeCommentReply.parentReplyId}). */
  replies: ComposeCommentReply[];
}

/**
 * Maps threads to the {@link DocxAnnotationInput}s the EXISTING R2 `w:comment` writer path
 * (`DocxAnnotationWriter`, reached via the push-annotations shuttle) renders as native comments
 * (FR-23/FR-24). A thread's root comment + every reply each become their own `Comment`-kind
 * annotation, ALL anchored to the SAME `targetText` — the flat, multi-comment-on-one-span
 * representation a legacy `.docx` can hold (SCOPE GUARD: no modern-comments reply-chain authoring).
 * Threads without a captured `anchorText` are skipped (the server's `DocxAnnotation.Validate()`
 * requires a non-empty `targetText` for a Comment-kind annotation — same rule
 * `anchoredAnnotationsToDocxAnnotations` in `useComposeWordShuttle.ts` already applies).
 *
 * Exported so a save-flow caller (a follow-on wiring task) can append this to the annotations list
 * alongside {@link ../ComposeEditor.ComposeEditorHandle.getRedlineAnnotations} — the two paths NEVER
 * duplicate a target: redlines write `w:ins`/`w:del`, this writes `w:comment`.
 */
export function composeCommentThreadsToDocxAnnotations(
  threads: readonly ComposeCommentThreadModel[]
): DocxAnnotationInput[] {
  const result: DocxAnnotationInput[] = [];
  for (const thread of threads) {
    if (!thread.anchorText) continue;
    result.push({
      kind: DocxTrackChangeKind.Comment,
      targetText: thread.anchorText,
      commentText: thread.text,
      author: thread.author,
      date: thread.timestamp,
    });
    for (const reply of thread.replies) {
      result.push({
        kind: DocxTrackChangeKind.Comment,
        targetText: thread.anchorText,
        commentText: reply.text,
        author: reply.author,
        date: reply.timestamp,
      });
    }
  }
  return result;
}
