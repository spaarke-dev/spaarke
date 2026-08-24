/**
 * useAiGenerateBookmark — FR-07 CAPTURE half: drift-proof AI generate-window anchoring
 * (spaarkeai-compose-r4 task 040, design §5 AI paragraph / §4 feature F5).
 *
 * THE PROBLEM (F5, NEW in R4): the user highlights a clause, clicks Generate, then keeps
 * typing while the model runs. When the model returns, the naive path applies the result at
 * the ORIGINAL selection offset — which has since moved. The edit lands in the wrong place
 * (the R4 "generate-window state-drift" defect).
 *
 * THE FIX (this hook, the CAPTURE side): on Generate we drop a request-scoped bookmark at the
 * live selection, tied to the request id, and REBASE it through every concurrent user edit
 * using the SAME ProseMirror `Mapping` primitive task 020/022 established for the rebased
 * operation log (`RebasedOperationLog.recordTransaction` → `tr.mapping.mapResult(pos, assoc)`).
 * The bookmark's paragraph `w14:paraId` (durable, Word-native) is resolved via the D2 anchor
 * resolver `resolveRunAnchor` and handed to the model AS CONTEXT. The model returns JSON
 * operations referencing paraId (NOT free text to search — invariant I-7). On return we resolve
 * the bookmark to its CURRENT position so the operations land at the REBASED selection, never
 * the stale original offset.
 *
 * WHAT THIS IS NOT (binding scope boundary):
 *  - It does NOT fetch or dispatch (ADR-028: no client fetch here). The dispatch already goes
 *    through the shipped `Services/Ai/PublicContracts` facade via `dispatchConsumer` on the
 *    existing `ComposeAiToolbar` — this hook only supplies the paraId context IN and resolves
 *    the bookmark OUT. No new AI endpoint, no AI-catalog row (ADR-039 engine frozen).
 *  - It does NOT apply the operations. The APPLY/VALIDATE half — validate every returned anchor
 *    against the live doc before apply; fuzzy-as-comment last resort — is task 041
 *    (`./useAiApplyValidation.ts`, wired into `ComposeAiToolbar` via its `aiApplyValidation` prop).
 *    This hook hands 041 the resolved anchor + a `review` flag; when the bookmark landed in deleted
 *    content (or no longer resolves) the op is SURFACED for review, never silently placed (never-
 *    silently-drop, mirroring `RebasedOperationLog`'s `deletedContentFlag` discipline).
 *  - It does NOT text-search (I-7). Every rebase is `Mapping.mapResult(pos, assoc)` over a raw
 *    ProseMirror position; the paraId/offset anchor is RE-DERIVED via `resolveRunAnchor`.
 *
 * MECHANISM CHOICE (design offers "`Decoration`/`Selection.getBookmark()`"): we use the native
 * ProseMirror `SelectionBookmark` (`selection.getBookmark()` → `.map(mapping)` → `.resolve(doc)`)
 * PLUS raw tracked points (`mapResult` for the `deleted` signal) — NOT a `Decoration` overlay. A
 * Decoration would add a second plugin + a parallel position system; the bookmark + tracked-point
 * pair reuses the Mapping primitive already in the editor (component-justification §11: extend/
 * reuse, do not introduce a new position system). NFR-03: raw MIT ProseMirror APIs only
 * (`@tiptap/pm/state`, `@tiptap/pm/transform`) — no `@tiptap-pro/*`, zero new dependency.
 *
 * @see ./ ../stepOperationInterceptor.ts (RebasedOperationLog — the Mapping rebasing this reuses; resolveRunAnchor — the D2 anchor)
 * @see ../../types/compose-operations.ts (isComposeOperation — the shape the model must return)
 * @see ../ComposeAiToolbar.tsx (the existing AI toolbar; wires beginGenerate → dispatch → resolveOnReturn)
 * @see projects/spaarkeai-compose-r4/design.md §5 (AI paragraph), §4 F5
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import { TextSelection, type SelectionBookmark } from '@tiptap/pm/state';
import { resolveRunAnchor, STEP_INTERCEPTOR_IGNORE_META } from '../stepOperationInterceptor';
import { isComposeOperation, type ComposeOperation, type ComposeRunPoint } from '../../types/compose-operations';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * The context handed to the model when a bookmark is dropped on Generate. The durable
 * `paraId` is what the model anchors its returned operations to; `anchor` is the fine
 * run-local D2 point at drop time (informational — the model references paraId, and the
 * apply path re-derives run boundaries at patch time).
 */
export interface AiGenerateBookmarkContext {
  /** The request id the bookmark is tied to (caller-supplied or minted). */
  readonly requestId: string;
  /** The durable `w14:paraId` of the paragraph the selection sat in at Generate time; null if the caret was not inside a paraId-bearing block. */
  readonly paraId: string | null;
  /** The run-local D2 anchor of the selection start at Generate time; null when `paraId` is null. */
  readonly anchor: ComposeRunPoint | null;
}

/** The bookmark resolved to its CURRENT position (post concurrent-edit). */
export interface AiGenerateResolvedBookmark {
  /** The durable `w14:paraId` the bookmark's start now resolves into. */
  readonly paraId: string;
  /** The current run-local D2 point of the bookmark's start. */
  readonly point: ComposeRunPoint;
  /** The current absolute ProseMirror position of the bookmark's start (in-session only; never persisted). */
  readonly pos: number;
}

/**
 * Why the apply path (task 041) MUST surface the returned operations for review rather than
 * silently place them. `null` in {@link AiGenerateOperationsResult.review} means clean.
 */
export type AiGenerateReviewReason = 'bookmark-in-deleted-content' | 'bookmark-unresolvable';

/** Why a return payload was refused (all map to status `'refused'`). */
export type AiGenerateRefusalReason = 'free-text' | 'no-operations' | 'not-operations';

/** The model returned a valid JSON-operations payload; hand it to the apply path (041) at the resolved anchor. */
export interface AiGenerateOperationsResult {
  readonly status: 'operations';
  readonly requestId: string;
  /** The returned operations, each already validated to reference a non-empty `paraId` (shape only — deep per-anchor validation is task 041). */
  readonly operations: ComposeOperation[];
  /** The bookmark resolved to its current position; `null` when the anchored paragraph no longer resolves (see `review`). */
  readonly resolved: AiGenerateResolvedBookmark | null;
  /** Non-null ⇒ the apply path (041) MUST surface for review, NOT silently place (never-silently-drop). */
  readonly review: AiGenerateReviewReason | null;
}

/** The model returned free text (or a non-operations shape) — refused + surfaced (I-7 / escalation seam). */
export interface AiGenerateRefusedResult {
  readonly status: 'refused';
  readonly requestId: string;
  readonly reason: AiGenerateRefusalReason;
}

/** `resolveOnReturn` was called with a request id that has no live bookmark (double-return / stale id). */
export interface AiGenerateUnknownResult {
  readonly status: 'unknown-request';
  readonly requestId: string;
}

export type AiGenerateReturnResult = AiGenerateOperationsResult | AiGenerateRefusedResult | AiGenerateUnknownResult;

/** Options for {@link useAiGenerateBookmark} — the escalation/surface seams (root §6/§6.5). */
export interface UseAiGenerateBookmarkOptions {
  /**
   * Escalation seam (POML `<escalation>` / I-7): the model returned free text instead of
   * paraId-anchored operations. The flow REFUSES and surfaces — this callback is how the host
   * surfaces it for review. Never text-search a placement.
   */
  readonly onFreeTextRefused?: (requestId: string, reason: AiGenerateRefusalReason) => void;
  /**
   * The bookmark landed inside content a concurrent edit deleted (or no longer resolves).
   * Surfaced for the apply path (041) to review rather than silently place.
   */
  readonly onReviewRequired?: (requestId: string, reason: AiGenerateReviewReason) => void;
}

/** The stable controller returned by {@link useAiGenerateBookmark}. */
export interface AiGenerateBookmarkController {
  /**
   * On Generate: drop a bookmark at the current selection, tie it to `requestId` (minted if
   * omitted), and return the target paraId to send the model as context. Idempotent per id —
   * a second call with the same id replaces the prior bookmark.
   *
   * `range` (r8 task 051) anchors the bookmark at an EXPLICIT document range instead of the live
   * selection. The review-note tools dispatch a compose edit against a note's clause span
   * (`findCommentAnchorRange(doc, threadId)`), which is generally NOT where the caret is sitting —
   * so without this they had no way to use this controller and fell back to text search, which is
   * the uncovered anchor source task 051's escalation trigger named. Everything downstream is
   * identical: the same `Mapping` rebasing, the same `resolveRunAnchor` paraId, the same
   * `resolveOnReturn`. Supplying an explicit range is the ONLY difference, which is what keeps this
   * one mechanism rather than two (invariant 6).
   */
  beginGenerate: (opts?: { requestId?: string; range?: { from: number; to: number } }) => AiGenerateBookmarkContext;
  /**
   * Peek the current resolution of a live bookmark WITHOUT consuming it (UI / tests). Returns
   * `null` for an unknown id or a bookmark whose anchor no longer resolves.
   */
  resolveBookmark: (requestId: string) => AiGenerateResolvedBookmark | null;
  /**
   * On return: validate `payload` is JSON operations referencing paraId, resolve the bookmark to
   * its CURRENT position, and hand the result to the apply path (041). Consumes (clears) the
   * bookmark. Refuses free text; flags `review` when the bookmark landed in deleted content.
   */
  resolveOnReturn: (requestId: string, payload: unknown) => AiGenerateReturnResult;
  /** Discard a live bookmark (e.g. the dispatch was aborted). No-op for an unknown id. */
  clearBookmark: (requestId: string) => void;
  /** Count of live bookmarks (session-scoped; testing/diagnostics). */
  readonly activeCount: number;
}

// ---------------------------------------------------------------------------
// Internal bookmark entry
// ---------------------------------------------------------------------------

/** A single tracked position + the boundary `assoc` ProseMirror uses at an exact-boundary edit. */
interface TrackedPoint {
  pos: number;
  /** `-1` sticks to content BEFORE an exact-boundary insertion (selection start); `1` sticks AFTER (selection end). */
  assoc: -1 | 1;
}

interface BookmarkEntry {
  /** The native ProseMirror selection bookmark (design's `Selection.getBookmark()`), rebased via `.map(mapping)`. */
  bookmark: SelectionBookmark;
  /** Raw tracked start point — rebased via `mapResult` so we get the `deleted` signal + re-derive the D2 anchor. */
  start: TrackedPoint;
  /** Raw tracked end point (sticks after, so the range grows with content inserted at its trailing edge). */
  end: TrackedPoint;
  /** The durable paraId captured at drop time (sent to the model as context). */
  paraId: string | null;
  /** The run-local D2 anchor captured at drop time. */
  anchor: ComposeRunPoint | null;
  /** Set once the tracked start has landed inside content a concurrent edit deleted (`MapResult.deleted`). */
  deletedContent: boolean;
}

// ---------------------------------------------------------------------------
// Payload extraction (the model's return — JSON operations referencing paraId, NOT free text)
// ---------------------------------------------------------------------------

/**
 * Extract the closed-set operations from a return payload. Returns `null` for anything that is
 * NOT a well-formed operations payload — a string (free text), a mixed/partial array, or an
 * object with no valid `operations` — so the caller REFUSES rather than text-searching a
 * placement (I-7). A valid-but-empty operations envelope returns `[]` (the model produced no
 * change; a legitimate no-op, not a refusal).
 */
export function extractComposeOperations(payload: unknown): ComposeOperation[] | null {
  if (payload === null || payload === undefined) return null;
  if (typeof payload === 'string') return null; // free text — the negative case (I-7)
  if (Array.isArray(payload)) {
    if (payload.length === 0) return [];
    return payload.every(isComposeOperation) ? (payload as ComposeOperation[]) : null;
  }
  if (typeof payload === 'object') {
    const obj = payload as { operations?: unknown };
    if ('operations' in obj) return extractComposeOperations(obj.operations);
    return null;
  }
  return null;
}

/** Classify why extraction refused (for the surfaced reason). */
function refusalReasonFor(payload: unknown): AiGenerateRefusalReason {
  if (typeof payload === 'string') return 'free-text';
  if (payload === null || payload === undefined) return 'no-operations';
  return 'not-operations';
}

// ---------------------------------------------------------------------------
// The hook
// ---------------------------------------------------------------------------

/**
 * FR-07 capture-side controller (see file header). Manages request-scoped generate-window
 * bookmarks over `editor`, rebasing them through concurrent user edits via the ProseMirror
 * `Mapping` primitive (task 020/022 parity), and resolving them on return.
 */
export function useAiGenerateBookmark(
  editor: Editor | null,
  options: UseAiGenerateBookmarkOptions = {}
): AiGenerateBookmarkController {
  const { onFreeTextRefused, onReviewRequired } = options;

  // Live bookmarks keyed by request id. A ref (not state) so the per-transaction rebase mutates
  // in place WITHOUT a re-render on every keystroke (perf + no render storm on the toolbar).
  const entriesRef = React.useRef<Map<string, BookmarkEntry>>(new Map());
  const seqRef = React.useRef(0);
  // Bump only when the set of active ids changes (begin/resolve/clear), so `activeCount` in the
  // returned controller is fresh for a consumer that renders off it — NOT on every rebase.
  const [, forceUpdate] = React.useReducer((x: number) => x + 1, 0);

  // Keep the surface callbacks in a ref so the transaction subscription (below) never re-subscribes
  // when a parent passes fresh inline callbacks — mirrors the stable-listener pattern.
  const cbRef = React.useRef({ onFreeTextRefused, onReviewRequired });
  cbRef.current = { onFreeTextRefused, onReviewRequired };

  // ── Rebase every live bookmark through each concurrent doc-changing transaction ──────────────
  // The SAME primitive `RebasedOperationLog` uses: `tr.mapping.mapResult(pos, assoc)` for the
  // tracked points (gives the `deleted` signal), and `SelectionBookmark.map(tr.mapping)` for the
  // native bookmark. Programmatic (`STEP_INTERCEPTOR_IGNORE_META`) transactions are skipped, as
  // the op-log does — only genuine user edits rebase the selection.
  React.useEffect(() => {
    if (!editor) return undefined;
    const handler = ({ transaction }: { transaction: import('@tiptap/pm/state').Transaction }): void => {
      if (!transaction.docChanged) return;
      if (transaction.getMeta(STEP_INTERCEPTOR_IGNORE_META)) return;
      const entries = entriesRef.current;
      if (entries.size === 0) return;
      const mapping = transaction.mapping;
      for (const entry of entries.values()) {
        const startResult = mapping.mapResult(entry.start.pos, entry.start.assoc);
        const endResult = mapping.mapResult(entry.end.pos, entry.end.assoc);
        entry.start = { pos: startResult.pos, assoc: entry.start.assoc };
        entry.end = { pos: endResult.pos, assoc: entry.end.assoc };
        if (startResult.deleted || endResult.deleted) entry.deletedContent = true;
        entry.bookmark = entry.bookmark.map(mapping);
      }
    };
    editor.on('transaction', handler);
    return () => {
      editor.off('transaction', handler);
    };
  }, [editor]);

  const beginGenerate = React.useCallback(
    (opts?: { requestId?: string; range?: { from: number; to: number } }): AiGenerateBookmarkContext => {
      const requestId = opts?.requestId ?? `ai-generate#${(seqRef.current += 1)}`;
      if (!editor) {
        // No editor mounted — a degenerate bookmark with no context. resolveOnReturn will still
        // refuse free text; a valid ops payload resolves to `review: 'bookmark-unresolvable'`.
        return { requestId, paraId: null, anchor: null };
      }
      const { selection, doc } = editor.state;
      // Task 051: an EXPLICIT range wins over the live selection (the review-note tools anchor on a
      // note's clause span, not on wherever the caret happens to be). The bookmark is taken from a
      // TextSelection over that range so `.map()`/`.resolve()` behave exactly as they do for a real
      // selection — same primitive, no parallel path.
      const from = opts?.range ? opts.range.from : selection.from;
      const to = opts?.range ? opts.range.to : selection.to;
      const bookmark = opts?.range
        ? TextSelection.create(doc, from, to).getBookmark()
        : selection.getBookmark();
      const startAnchor = resolveRunAnchor(doc, from);
      const entry: BookmarkEntry = {
        bookmark,
        start: { pos: from, assoc: -1 },
        end: { pos: to, assoc: 1 },
        paraId: startAnchor ? startAnchor.paraId : null,
        anchor: startAnchor ? { runIndex: startAnchor.runIndex, offset: startAnchor.offset } : null,
        deletedContent: false,
      };
      entriesRef.current.set(requestId, entry);
      forceUpdate();
      return { requestId, paraId: entry.paraId, anchor: entry.anchor };
    },
    [editor]
  );

  const resolveBookmark = React.useCallback(
    (requestId: string): AiGenerateResolvedBookmark | null => {
      const entry = entriesRef.current.get(requestId);
      if (!entry || !editor) return null;
      const a = resolveRunAnchor(editor.state.doc, entry.start.pos);
      if (!a) return null;
      return { paraId: a.paraId, point: { runIndex: a.runIndex, offset: a.offset }, pos: entry.start.pos };
    },
    [editor]
  );

  const clearBookmark = React.useCallback((requestId: string): void => {
    if (entriesRef.current.delete(requestId)) forceUpdate();
  }, []);

  const resolveOnReturn = React.useCallback(
    (requestId: string, payload: unknown): AiGenerateReturnResult => {
      const entry = entriesRef.current.get(requestId);
      if (!entry) {
        return { status: 'unknown-request', requestId };
      }

      // NEGATIVE case (I-7 / escalation): the model returned free text (or a non-operations shape)
      // instead of paraId-anchored operations — REFUSE + surface. Never text-search a placement.
      const operations = extractComposeOperations(payload);
      if (operations === null) {
        const reason = refusalReasonFor(payload);
        entriesRef.current.delete(requestId);
        forceUpdate();
        cbRef.current.onFreeTextRefused?.(requestId, reason);
        return { status: 'refused', requestId, reason };
      }

      // Resolve the bookmark to its CURRENT position (post concurrent-edit) so the operations land
      // at the REBASED selection, not the stale original offset.
      const resolved = editor ? resolveRunAnchor(editor.state.doc, entry.start.pos) : null;
      let review: AiGenerateReviewReason | null = null;
      if (entry.deletedContent) {
        review = 'bookmark-in-deleted-content';
      } else if (!resolved) {
        review = 'bookmark-unresolvable';
      }

      const result: AiGenerateOperationsResult = {
        status: 'operations',
        requestId,
        operations,
        resolved: resolved
          ? {
              paraId: resolved.paraId,
              point: { runIndex: resolved.runIndex, offset: resolved.offset },
              pos: entry.start.pos,
            }
          : null,
        review,
      };

      entriesRef.current.delete(requestId);
      forceUpdate();
      if (review) cbRef.current.onReviewRequired?.(requestId, review);
      return result;
    },
    [editor]
  );

  return React.useMemo<AiGenerateBookmarkController>(
    () => ({
      beginGenerate,
      resolveBookmark,
      resolveOnReturn,
      clearBookmark,
      get activeCount() {
        return entriesRef.current.size;
      },
    }),
    [beginGenerate, resolveBookmark, resolveOnReturn, clearBookmark]
  );
}
