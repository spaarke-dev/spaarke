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
import { resolveRunAnchor } from './stepOperationInterceptor';
import type { Node as PMNode } from '@tiptap/pm/model';
import type { ComposeAnchoredComment } from '../types/compose-operations';

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

/**
 * Item 5b (UAT round-4, FR-23): the SAVE-side projection — like
 * {@link composeCommentThreadsToDocxAnnotations} but EXCLUDING threads whose id is in
 * `importedThreadIds` (threads seeded from the retained original's own `w:comment`s). Those already
 * ride the retained baseline on save, so re-emitting them would DUPLICATE the comment in the output.
 * Only session-authored (or otherwise non-imported) threads are persisted as new `w:comment`s. The
 * host (`ComposeEditor.getCommentThreadAnnotations`) supplies the imported id set from the load-time
 * `initialThreads`.
 */
export function composeSessionCommentThreadsToDocxAnnotations(
  threads: readonly ComposeCommentThreadModel[],
  importedThreadIds: ReadonlySet<string>
): DocxAnnotationInput[] {
  return composeCommentThreadsToDocxAnnotations(threads.filter(t => !importedThreadIds.has(t.id)));
}

// ---------------------------------------------------------------------------
// ai-advanced-capabilities-nda-r1 task 040 (comment-export wiring fix)
// ---------------------------------------------------------------------------

/** The `commentAnchor` mark name (mirrors `./marks/CommentAnchorMark.ts` and
 * `./hooks/useComposeCommentThreads.ts`'s `COMMENT_ANCHOR_MARK` — kept as its own local constant to
 * avoid a needless import of the mark module here). */
const COMMENT_ANCHOR_MARK_NAME = 'commentAnchor';

/**
 * Locate the CURRENT ProseMirror range of a `commentAnchor` mark by its `commentId` attribute,
 * spanning every text node carrying it (a mark can render as several adjacent text nodes when other
 * marks split them). Returns `null` when the mark is no longer present anywhere in `doc` — e.g. a
 * later edit deleted the anchored text. The caller treats a `null` result as "this thread's anchor no
 * longer exists," never guessing a replacement span (mirrors the op-log capture path's own
 * never-mis-map discipline in `stepOperationInterceptor.ts`).
 */
function findCommentAnchorRange(doc: PMNode, commentId: string): { from: number; to: number } | null {
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

/**
 * Maps threads to durable `(paraId, run-local range)`-anchored {@link ComposeAnchoredComment}s by
 * resolving each thread's LIVE `commentAnchor` mark span against `doc` via {@link resolveRunAnchor} —
 * the SAME D2 anchor primitive `stepOperationInterceptor.ts`'s op-log capture path uses. No
 * write-path text-search (I-7). REPLACES {@link composeSessionCommentThreadsToDocxAnnotations} for the
 * Save flow: that function's `DocxAnnotationInput`/`targetText` shape rode the now-retired `annotations`
 * save field, which the server's `SaveComposeDocumentBody` never deserialized (every comment sent that
 * way was silently dropped) — `comments` (this shape) is what `ComposeService.SaveAsync` actually reads
 * and `ComposeShadowPatchEngine.ApplyComment` bakes as a native `w:comment` (ADR-049).
 *
 * A thread contributes NO comment (never guessed/mis-anchored) when:
 *  - its id is in `importedThreadIds` (it already rides the retained-original baseline — re-emitting
 *    would duplicate it), OR
 *  - its anchor mark is no longer present in `doc` (a later edit removed the anchored text), OR
 *  - its resolved span crosses a paragraph boundary (`ComposeRunRange` is intra-paragraph only, per D2/
 *    I-4) — mirrors `classifyMarkStep`'s cross-paragraph refusal for the same-shaped op case.
 *
 * A thread's root comment + every reply each become their OWN `ComposeAnchoredComment`, all anchored to
 * the SAME resolved range — the same multi-comment-on-one-span representation
 * {@link composeCommentThreadsToDocxAnnotations} already uses for the legacy shape.
 */
export function composeSessionCommentThreadsToAnchoredComments(
  doc: PMNode,
  threads: readonly ComposeCommentThreadModel[],
  importedThreadIds: ReadonlySet<string>
): ComposeAnchoredComment[] {
  const result: ComposeAnchoredComment[] = [];
  for (const thread of threads) {
    if (importedThreadIds.has(thread.id)) continue;

    const span = findCommentAnchorRange(doc, thread.id);
    if (!span) continue;

    const start = resolveRunAnchor(doc, span.from);
    const end = resolveRunAnchor(doc, span.to);
    if (!start || !end || start.paraId !== end.paraId) continue;

    const range = {
      start: { runIndex: start.runIndex, offset: start.offset },
      end: { runIndex: end.runIndex, offset: end.offset },
    };

    result.push({
      paraId: start.paraId,
      range,
      commentText: thread.text,
      author: thread.author,
      date: thread.timestamp,
    });
    for (const reply of thread.replies) {
      result.push({
        paraId: start.paraId,
        range,
        commentText: reply.text,
        author: reply.author,
        date: reply.timestamp,
      });
    }
  }
  return result;
}
