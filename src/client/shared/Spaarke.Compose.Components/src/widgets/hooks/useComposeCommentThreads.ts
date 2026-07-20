/**
 * useComposeCommentThreads.ts — FR-23 comment-thread state over `CommentAnchorMark`
 * (spaarkeai-compose-r3 task 044).
 *
 * The R2 `CommentAnchorMark` is a rendering primitive ONLY (see its file header): it carries
 * `data-comment-id` + provenance but does not track threads, replies, or resolution. This hook is
 * the "later, core-gated work" that mark's header flagged — state over the mark, not a
 * reimplementation of it. Three operations, matching the FR-23 SCOPE GUARD exactly:
 *
 *  - `createThread(text, range?)` — applies a NEW `commentAnchor` mark to a non-collapsed selection
 *    (the supplied `range`, or the editor's current selection when omitted) and records the root
 *    comment. Returns `null` (no mutation) for a collapsed/absent selection or empty text — a
 *    thread anchors a SELECTION, never a caret (mirrors Word's own comment-creation gesture).
 *  - `reply(threadId, text)` — appends an ordered reply to an existing thread. Replies are ALWAYS
 *    flat (SCOPE GUARD) — this hook never nests them.
 *  - `resolve(threadId)` — marks a thread resolved (UI-only; see `ComposeCommentThreadModel.resolved`).
 *
 * `importThreads` is the seam FR-25 (task 051) uses to project recovered `RecoveredComment`s into
 * this same state (upsert-by-id) — see `ComposeCommentThread.types.ts` for the shape rationale.
 * `initialThreads` seeds the very first render (e.g. a future mount-time hydration from a prior
 * pull-annotations call) without requiring a second effect.
 *
 * Mutation goes through a raw `editor.state.tr` + `editor.view.dispatch(tr)` (the SAME pattern
 * `usePendingRedline.ts`'s `stripRedlineMarks` uses) rather than a TipTap `.chain()`, because the
 * mark must apply to an EXPLICIT captured range that may differ from the editor's current selection
 * at call time (the caller — `ComposeCommentThread.tsx` — captures the range before the user's focus
 * moves into the comment composer's textbox).
 *
 * @see ../marks/CommentAnchorMark.ts — the mark this hook applies/reads (reused, not reimplemented)
 * @see ../ComposeCommentThread.types.ts — `ComposeCommentThreadModel` / `ComposeCommentReply`
 * @see ../ComposeCommentThread.tsx — the Fluent v9 UI consuming this hook
 * @see ../hooks/usePendingRedline.ts — the sibling hook this mirrors conventions from
 */
import * as React from 'react';
import type { Editor } from '@tiptap/core';
import type { ComposeCommentReply, ComposeCommentThreadModel } from '../ComposeCommentThread.types';

const COMMENT_ANCHOR_MARK = 'commentAnchor';

/** Mint a client-generated id. Prefers `crypto.randomUUID()`; degrades to a timestamp+random id for
 * runtimes without it (mirrors `ComposeWorkspace.mintDocumentSessionId`'s convention). */
function mintCommentId(prefix: 'thread' | 'reply'): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) return `${prefix}-${c.randomUUID()}`;
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

/** A ProseMirror document range, `[from, to)`. */
export interface ComposeCommentRange {
  from: number;
  to: number;
}

export interface UseComposeCommentThreadsResult {
  /** All threads, in creation order. */
  threads: ComposeCommentThreadModel[];
  /**
   * Create a new thread anchored to `range` (or the editor's current selection when `range` is
   * omitted). Applies a fresh `commentAnchor` mark to the resolved span. Returns the new thread id,
   * or `null` when there is no editor, no non-collapsed range to anchor to, or `text` is empty/
   * whitespace-only (no mutation occurs in that case).
   */
  createThread: (text: string, range?: ComposeCommentRange) => string | null;
  /** Append a reply to `threadId`, in order. No-op for empty/whitespace-only text or an unknown id. */
  reply: (threadId: string, text: string) => void;
  /** Mark `threadId` resolved (UI-only — see `ComposeCommentThreadModel.resolved`). No-op if unknown. */
  resolve: (threadId: string) => void;
  /**
   * Upsert-by-id: threads in `imported` REPLACE any existing thread sharing their id, and are added
   * otherwise. The seam FR-25 (task 051) uses to project recovered comment threads into this state.
   */
  importThreads: (imported: readonly ComposeCommentThreadModel[]) => void;
}

/**
 * The hook. `author` is the display name attributed to threads/replies created via this hook
 * instance (the current signed-in user — resolved by the host; this hook makes no network call,
 * per ADR-028).
 */
export function useComposeCommentThreads(
  editor: Editor | null,
  author: string,
  initialThreads?: readonly ComposeCommentThreadModel[]
): UseComposeCommentThreadsResult {
  const [threads, setThreads] = React.useState<ComposeCommentThreadModel[]>(() =>
    initialThreads ? [...initialThreads] : []
  );

  const createThread = React.useCallback(
    (text: string, range?: ComposeCommentRange): string | null => {
      const trimmed = text.trim();
      if (!editor || trimmed.length === 0) return null;

      const target = range ?? { from: editor.state.selection.from, to: editor.state.selection.to };
      // SCOPE: a thread anchors a SELECTION, never a caret — mirrors Word's own comment gesture.
      if (target.to <= target.from) return null;

      const markType = editor.state.schema.marks[COMMENT_ANCHOR_MARK];
      if (!markType) return null; // defensive — CommentAnchorMark not registered on this editor

      const id = mintCommentId('thread');
      const anchorText = editor.state.doc.textBetween(target.from, target.to, ' ');
      const tr = editor.state.tr.addMark(target.from, target.to, markType.create({ commentId: id }));
      editor.view.dispatch(tr);

      const timestamp = new Date().toISOString();
      setThreads(prev => [...prev, { id, author, timestamp, text: trimmed, anchorText, resolved: false, replies: [] }]);
      return id;
    },
    [editor, author]
  );

  const reply = React.useCallback(
    (threadId: string, text: string): void => {
      const trimmed = text.trim();
      if (trimmed.length === 0) return;
      const newReply: ComposeCommentReply = {
        id: mintCommentId('reply'),
        author,
        timestamp: new Date().toISOString(),
        text: trimmed,
      };
      setThreads(prev => prev.map(t => (t.id === threadId ? { ...t, replies: [...t.replies, newReply] } : t)));
    },
    [author]
  );

  const resolve = React.useCallback((threadId: string): void => {
    setThreads(prev => prev.map(t => (t.id === threadId ? { ...t, resolved: true } : t)));
  }, []);

  const importThreads = React.useCallback((imported: readonly ComposeCommentThreadModel[]): void => {
    setThreads(prev => {
      const byId = new Map(prev.map(t => [t.id, t] as const));
      for (const t of imported) byId.set(t.id, t);
      return Array.from(byId.values());
    });
  }, []);

  return { threads, createThread, reply, resolve, importThreads };
}

export default useComposeCommentThreads;
