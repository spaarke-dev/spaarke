/**
 * useComposeChangeSummary.ts — the on-demand "summarise the tracked changes" flow (UAT item 8).
 *
 * Project: spaarkeai-compose-r8.
 *
 * Orchestrates the four steps between a user asking for a change summary and the Action running, and
 * returns a CLOSED set of outcomes so the caller renders a deterministic answer for each rather than
 * inferring one from a null:
 *
 *   1. **Save gate.** `pull-annotations` reads the document's bytes AS STORED, not the editor's live
 *      state, so a summary produced over a dirty editor silently omits the user's unsaved edits. For a
 *      panel that is a nuisance; for a memo attached to an email it is a defect. Dirty ⇒ `needs-save`,
 *      and the HOST prompts ("There are unsaved changes — save before generating the summary?"). This
 *      hook never saves on the user's behalf: a save is a user action, and one taken silently to satisfy
 *      a read is exactly the kind of surprise write this codebase refuses elsewhere.
 *   2. **Live pull.** The current native `w:ins`/`w:del`/`w:comment` from the stored document — fresher
 *      than the load-time projection, which is a snapshot from when the document was opened.
 *   3. **Produce the operand.** {@link buildComposeChangesText}, which REFUSES (returns `null`) when
 *      there is no real change data. That refusal surfaces here as `no-changes`.
 *   4. **Dispatch.** The `compose-summarize-word-changes` Binding, chat-session routed (an informational
 *      action — see `ComposeActionEnqueue`'s own note that summarize-changes keeps chat-session dispatch
 *      and Assistant-rendered prose, unlike an editor-materializing edit action).
 *
 * WHY THE REFUSAL IS LOUDER HERE THAN ON A BUTTON. On a passive surface, "no change data" can be
 * expressed by not rendering the control. This action is *asked for*, so the user is owed an answer, and
 * the answer must be "this document has no tracked changes to summarise" — never a generated memo. The
 * Action was pulled from the selection toolbar precisely because, dispatched without change data, the
 * model fabricates a phantom "[Insertion]".
 *
 * PRIVACY (ADR-015 Tier 3): the produced operand is document content. It is passed to the dispatch and
 * never logged, never put on the PaneEventBus, and never included in an error message.
 *
 * @see ../composeChangesText.ts — the producer + its refusal contract
 * @see ../useComposeWordShuttle.ts — `useComposePullAnnotations` (step 2)
 * @see ../../../../../server/api/Sprk.Bff.Api/Services/Compose/ComposeRevisionReportGenerator.cs —
 *      the appendix rendered from the result of this dispatch
 */

import * as React from 'react';

import { buildComposeChangesText } from '../composeChangesText';
import type { PullAnnotationsResult } from '../useComposeWordShuttle';

/** The document this summary is about. */
export interface ComposeChangeSummaryTarget {
  documentSpeId: string;
  driveId: string;
  tenantId: string;
}

/**
 * The closed outcome set. Every branch is a distinct thing to TELL the user — there is no "undefined
 * means nothing happened" case, which is what lets the caller render an answer for each.
 */
export type ComposeChangeSummaryOutcome =
  /** The editor has unsaved edits. The host must offer to save; the summary would otherwise describe stale bytes. */
  | { kind: 'needs-save' }
  /** The stored document carries no tracked changes or comments. The honest answer — never a generated summary. */
  | { kind: 'no-changes' }
  /** Dispatched. `changeCount` is what the operand described, for a confirmation line. */
  | { kind: 'dispatched'; changeCount: number }
  /** The pull or the dispatch failed. `message` is user-safe (ADR-019) and carries no document content. */
  | { kind: 'failed'; message: string };

export interface UseComposeChangeSummaryOptions {
  /** Reads the editor's live dirty state at call time — NOT a snapshot, or the gate races the user's typing. */
  isEditorDirty: () => boolean;
  /** `useComposePullAnnotations().pull` — injected rather than called here so this hook stays host-agnostic and testable. */
  pull: (args: ComposeChangeSummaryTarget) => Promise<PullAnnotationsResult>;
  /** Dispatches the Binding. Returns when the request is accepted; the result renders in the Assistant. */
  dispatch: (changesText: string) => Promise<void>;
}

export interface UseComposeChangeSummaryResult {
  /** True while a request is in flight — for disabling the trigger, never for hiding the outcome. */
  running: boolean;
  /** Runs the flow for `target`. Never throws: every failure is an outcome. */
  requestSummary: (target: ComposeChangeSummaryTarget) => Promise<ComposeChangeSummaryOutcome>;
}

export function useComposeChangeSummary(options: UseComposeChangeSummaryOptions): UseComposeChangeSummaryResult {
  const { isEditorDirty, pull, dispatch } = options;
  const [running, setRunning] = React.useState(false);

  const requestSummary = React.useCallback(
    async (target: ComposeChangeSummaryTarget): Promise<ComposeChangeSummaryOutcome> => {
      // Step 1 — the save gate, BEFORE any network call. Checked first so a dirty editor costs nothing.
      if (isEditorDirty()) {
        return { kind: 'needs-save' };
      }

      setRunning(true);
      try {
        // Step 2 — the stored document's current annotations.
        const pulled = await pull(target);

        // Step 3 — produce the operand, or refuse. The producer owns the definition of "real change
        // data" (empty bodies, no annotations at all, a set too large for the authored cap), so this
        // hook does not second-guess it with its own emptiness check — one rule, one implementation.
        const changesText = buildComposeChangesText({
          revisions: pulled.revisions,
          comments: pulled.comments,
        });

        if (changesText === null) {
          return { kind: 'no-changes' };
        }

        // Step 4 — dispatch. The count is derived from what was PULLED, not re-parsed out of the
        // operand string: the operand is prose for a model, not a data structure to read back.
        await dispatch(changesText);
        return {
          kind: 'dispatched',
          changeCount: (pulled.revisions?.length ?? 0) + (pulled.comments?.length ?? 0),
        };
      } catch {
        // ADR-019: no raw server detail reaches the user, and no document content reaches this string.
        return {
          kind: 'failed',
          message: 'The change summary could not be generated. Please try again.',
        };
      } finally {
        setRunning(false);
      }
    },
    [isEditorDirty, pull, dispatch]
  );

  return { running, requestSummary };
}

export default useComposeChangeSummary;
