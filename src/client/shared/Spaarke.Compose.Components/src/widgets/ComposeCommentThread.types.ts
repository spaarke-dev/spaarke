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
// task 031: the anchor-range primitive lives in a leaf module now (see the re-export note below).
// Imported here for LOCAL use by composeSessionCommentThreadsToAnchoredComments.
import { findCommentAnchorRange } from './commentAnchorRange';
// task 052 (FR-15, Word-comment export mirror): the ONE shared source for how an advisory
// thread's content composes into its exported `w:comment` text — see that module's file header
// for the full discrete-fields-vs-legacy-marker-parse rationale.
import { composeAdvisoryCommentExportText } from './advisoryNoteFormatting';

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
  /**
   * task 032 (right-gutter comment layout) — optional NDA/agreement-REVIEW advisory metadata,
   * carried through from {@link ../ComposeEditor.AdvisoryCommentInput} via `placeAdvisoryComments`
   * → `useComposeCommentThreads.createThread`'s metadata parameter, so the right-rail gutter card
   * can render a risk badge + section/standard citation without a side lookup.
   *
   * SCOPE LIFT (task 052, FR-15, 2026-07-31 — supersedes the prior "UI-only, never exported"
   * note): `riskLevel`/`sectionRef` stay UI-only (a coarse badge + the gutter's own location-label
   * derivation — neither is part of the on-screen note's labelled body, so neither belongs in the
   * exported comment text). `standardRef` (+ `flaggedClause`/`assessment` below) are DIFFERENT —
   * the on-screen gutter always renders them as part of the note's visible content ("Flagged
   * clause: …" / "Assessment says: …" / "Standard: …"), so silently dropping them at export made
   * the saved-then-reopened-in-Word comment materially incomplete relative to what the reviewer
   * saw (the root cause tracked in
   * `projects/ai-advanced-capabilities-agreements-r1/notes/word-comment-export-gap.md`). Both
   * `composeCommentThreadsToDocxAnnotations` and `composeSessionCommentThreadsToAnchoredComments`
   * below now compose the root comment's exported text via
   * {@link composeAdvisoryCommentExportText} (`./advisoryNoteFormatting`), which reads these
   * fields. Absent for session (non-advisory) Comments panel threads, which never pass metadata to
   * `createThread` — those threads' `text` exports completely unchanged (see
   * `isAdvisoryCommentThread`).
   */
  /** Coarse qualitative risk signal (NEVER a numeric score, per ADR-039). UI-only — not exported. */
  riskLevel?: string;
  /** Section/clause reference from the review Action's output (e.g. "3.2"). UI-only — not exported
   *  as its own line (used for the gutter's location-label derivation only). */
  sectionRef?: string;
  /**
   * Grounded-fact prose — "what the clause does" (ai-advanced-capabilities-agreements-r1 task 002
   * Action-output schema split: `explanation` → discrete `flaggedClause` + `assessment`). Exported
   * as the note's "Flagged clause: …" line when present. Absent on a legacy (pre-002) thread, whose
   * `text` alone may or may not still carry the old embedded "Grounded fact:"/"Advisory judgment:"
   * markers — see {@link getAdvisoryNoteSegments} in `./advisoryNoteFormatting`.
   */
  flaggedClause?: string;
  /** Reasoned-judgment prose — "why it matters" (task 002 discrete field). Exported as the note's
   *  "Assessment says: …" line when present (only meaningful alongside `flaggedClause`). */
  assessment?: string;
  /** Optional standard/playbook reference the flag cites (e.g. "B5 - Use & disclosure
   *  obligations"). Exported as the note's "Standard: …" line (task 052 scope lift — see above). */
  standardRef?: string;
  /**
   * Full resolved standard-clause text, when the thread happens to carry it — task 052's "full
   * clause text when available" criterion. Nothing currently populates this (the gutter's own
   * `StandardRefChip` resolves it on demand via an async BFF call, which the export mapping cannot
   * make — it is a pure, synchronous, `byte[]`-free function per ADR-049/ADR-007); a future durable
   * -recall or prefetch wiring that sets it will export it automatically (payload-driven — no
   * mapping-function change needed). Absent ⇒ export cites `standardRef` alone.
   */
  standardText?: string;
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
      // task 052 (FR-15): the ROOT comment's exported text mirrors the gutter (structured for an
      // advisory thread, verbatim for a plain session comment) — see the shared helper's file
      // header. Replies are never restructured (see composeAdvisoryCommentExportText's own doc).
      commentText: composeAdvisoryCommentExportText(thread),
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

// task 031: `findCommentAnchorRange` + `COMMENT_ANCHOR_MARK_NAME` were EXTRACTED to the leaf module
// `./commentAnchorRange` (so the G9 scroll-sync helpers can import the primitive without dragging this
// file's persistence-vocabulary imports — `useComposeWordShuttle` → `@spaarke/auth`). Re-exported here
// so every existing import site (`ComposeCommentGutter.tsx`, etc.) that imports it FROM this file is
// unaffected. Single implementation.
export { findCommentAnchorRange, COMMENT_ANCHOR_MARK_NAME } from './commentAnchorRange';

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
    // The mark starts in a non-paragraph block (e.g. a heading with no paraId) — cannot anchor a
    // single-paragraph comment there. Do NOT guess a different paragraph (I-7). Skip.
    if (!start) continue;

    // A ComposeAnchoredComment is single-paragraph (paraId + run-local range). Advisory REVIEW comments,
    // though, routinely span a long multi-sentence excerpt that crosses paragraph boundaries — the old
    // `start.paraId !== end.paraId → continue` DROPPED every such comment, so NOTHING reached Word
    // (UAT round-3, 2026-07-27: server received 0 comments on the create-on-save path). Instead, CLAMP the
    // range to the START paragraph the comment demonstrably begins in: anchor from the mark's start to the
    // end of that paragraph's inline content when the mark extends past it. The comment marker still sits
    // on the flagged clause's start; we never guess across a boundary. When the mark already ends inside
    // the start paragraph, the exact span is used unchanged.
    const $from = doc.resolve(span.from);
    const paraEnd = $from.end($from.depth); // end position of the start paragraph's inline content
    const clampedTo = Math.min(span.to, paraEnd);
    const end = resolveRunAnchor(doc, clampedTo > span.from ? clampedTo : paraEnd);
    if (!end || start.paraId !== end.paraId) continue; // defensive: the clamp must land in the same paragraph

    const range = {
      start: { runIndex: start.runIndex, offset: start.offset },
      end: { runIndex: end.runIndex, offset: end.offset },
    };

    result.push({
      paraId: start.paraId,
      range,
      // task 052 (FR-15, Word-comment export mirror): the LIVE save path. Composes the SAME
      // structured "Flagged clause / Assessment says / Standard" text the gutter renders for an
      // advisory thread (discrete task-002 fields when present, legacy marker-parsed `text`
      // otherwise); a plain (non-advisory) session comment's text is unchanged. Payload-driven only
      // — a durable-recalled thread (tasks 030-032) with the same fields composes identically to a
      // live one, regardless of provenance.
      commentText: composeAdvisoryCommentExportText(thread),
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
