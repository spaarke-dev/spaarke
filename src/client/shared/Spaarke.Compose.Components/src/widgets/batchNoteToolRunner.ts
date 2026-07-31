/**
 * batchNoteToolRunner — STRICTLY SEQUENTIAL batch execution over the shipped per-note AI-tool
 * dispatch (ai-advanced-capabilities-agreements-r1 task 041, spec FR-11 / design.md Lens 2 #3).
 *
 * WHY A STANDALONE, PURE MODULE (CLAUDE.md §11 reuse-first):
 *   The per-note dispatch itself is SHIPPED — `ComposeCommentGutter.noteTools`/`onRunNoteTool` →
 *   `ComposeEditor.runNoteTool` → `enqueueComposeAction` (bridged to
 *   `ConversationPane.dispatchComposeAction`) → `useSerialActionQueue` (already a FIFO,
 *   at-most-one-in-flight queue — FR-18). Batch = a selection model (gutter) + a sequential LOOP
 *   over that shipped dispatch (`ComposeEditor.tsx` owns the loop, since it is the ONLY place with
 *   the editor state needed to build a per-note request — see that file's `dispatchNoteToolRequest`).
 *   This module is the LOOP MECHANICS ONLY (order, one-in-flight, failure isolation, progress
 *   reporting) — extracted so the ADR-016 sequentiality guarantee is directly unit-testable without
 *   a TipTap editor harness, editor state, or a rendered component. `ComposeEditor.tsx` supplies the
 *   one thing this module does not know how to build: `runOne`, a per-thread dispatch function.
 *
 * ADR-016 (rate limits — sequential batch): this loop NEVER starts note N+1 until note N's `runOne`
 * Promise has settled (resolved OR rejected) — a plain `for...of` with `await`, never `Promise.all`
 * / `Promise.allSettled` / a fire-and-forget kickoff. That is the entire sequentiality guarantee;
 * `batchNoteToolRunner.test.ts` asserts it directly (an in-flight counter that must never exceed 1).
 *
 * Failure isolation: a rejected `runOne` is caught and recorded as a failed outcome; the loop
 * CONTINUES to the next thread id (one bad note never aborts the batch). The returned outcome array
 * always has exactly `threadIds.length` entries, in input order, one per note — good for the
 * end-of-batch summary (ComposeBatchNoteToolProgressModal).
 *
 * ADR-041 (no new outcome shape): this module produces a batch-level ROLLUP only
 * ({@link BatchNoteToolOutcome}, ok/error per thread id) — it never renders anything and never
 * substitutes for the per-note Assistant confirmation, which continues to render via the EXISTING
 * `dispatchComposeAction` → `makeComposeEditControlsMessage` path (unchanged) each time `runOne`
 * resolves.
 *
 * @see ./ComposeCommentGutter.tsx — the selection model + sub-toolbar that triggers a batch run
 * @see ./ComposeEditor.tsx — `dispatchNoteToolRequest` (the per-note request builder, shared with
 *      the single-note `runNoteTool`) + `runBatchNoteToolAsync` (the thin adapter that supplies
 *      `runOne` here and turns `onProgress` into React state for {@link ComposeBatchNoteToolProgressModal})
 */

/** One note's batch outcome — success/failure only; no payload (ADR-041 §MUST NOT: no second outcome store). */
export interface BatchNoteToolOutcome {
  readonly threadId: string;
  readonly ok: boolean;
  /** Present only when `ok` is false — a short, human-readable failure reason for the end-of-batch summary. */
  readonly error?: string;
}

/** Live progress snapshot, reported after every state transition (start-of-note, end-of-note). */
export interface BatchNoteToolProgress {
  /** Total notes in this batch run (fixed for the run's lifetime). */
  readonly total: number;
  /** Count of notes that have FINISHED (success or failure) so far. */
  readonly completed: number;
  /** The thread id currently in flight, or `null` between notes / once the batch has finished. */
  readonly currentThreadId: string | null;
  /** Outcomes recorded so far, in completion order (grows by one each time a note settles). */
  readonly outcomes: readonly BatchNoteToolOutcome[];
}

/**
 * Runs `runOne(threadId)` once per entry in `threadIds`, STRICTLY in order, awaiting each call
 * before starting the next (ADR-016 — never more than one dispatch in flight). A rejected `runOne`
 * is caught and recorded as a failed outcome (failure isolation) — the loop always processes every
 * threadId, even after a failure. `onProgress` fires once before each note starts and once after it
 * settles, so a host can drive a live progress UI without polling.
 *
 * Resolves with exactly `threadIds.length` outcomes, in INPUT order (not completion order — since
 * completion order and input order are identical for a strictly-sequential loop, this is the same
 * thing, but stated explicitly since a future concurrent variant would need to make a choice here).
 *
 * An empty `threadIds` resolves immediately with an empty array — no `onProgress` calls, no-op.
 */
export async function runBatchNoteTool(
  threadIds: readonly string[],
  runOne: (threadId: string) => Promise<void>,
  onProgress?: (progress: BatchNoteToolProgress) => void
): Promise<readonly BatchNoteToolOutcome[]> {
  const total = threadIds.length;
  const outcomes: BatchNoteToolOutcome[] = [];
  if (total === 0) return outcomes;

  const emit = (currentThreadId: string | null): void => {
    onProgress?.({ total, completed: outcomes.length, currentThreadId, outcomes: [...outcomes] });
  };

  for (const threadId of threadIds) {
    // Report BEFORE dispatch — the UI shows "note X of N" WHILE it runs, not only after.
    emit(threadId);
    try {
      // Awaiting this Promise IS the sequentiality gate (mirrors useSerialActionQueue's own
      // ordering proof) — request N+1 is never built/enqueued until request N has fully settled.
      await runOne(threadId);
      outcomes.push({ threadId, ok: true });
    } catch (err) {
      // Failure isolation — record and continue; one bad note never aborts the batch.
      outcomes.push({ threadId, ok: false, error: err instanceof Error ? err.message : String(err) });
    }
    // Report AFTER dispatch settles — `completed` increments, `currentThreadId` clears until the
    // next iteration's pre-dispatch emit (or stays null if this was the last note).
    emit(null);
  }

  return outcomes;
}

export default runBatchNoteTool;
