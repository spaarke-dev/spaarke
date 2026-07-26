/**
 * useNdaReviewAdvisoryCommentsBridge — NDA-REVIEW advisory comments
 * (ai-advanced-capabilities-nda-r1 task 031).
 *
 * Bridges the terminal `dispatched.result` of an INFORMATIONAL Compose action dispatch (already
 * shipped — `dispatchComposeAction`'s `!isEditAction` branch, the SAME raw payload
 * `formatComposeActionResultMarkdown` / `formatEventOutputMarkdown` render as prose) to the
 * `compose_advisory_comments` PaneEventBus workspace-channel event, mirroring
 * `useDocQaCitationBridge`'s bridge-hook shape.
 *
 * `dispatched.result` for an NDA-REVIEW run carries the ledgered Action output VERBATIM
 * (`SessionDispatchOrchestrator.BuildResultChunk` → `AnalysisChunk.CompletedRaw`, per
 * `projects/ai-advanced-capabilities-nda-r1/notes/task-023-whole-doc-review-orchestration.md`):
 * `{ overallRisk, flaggedSections[{ sectionRef, quotedText, riskLevel, explanation, standardRef }] }`.
 * This hook detects that shape STRUCTURALLY (mirrors `composeResultFormat.ts`'s
 * mutually-exclusive-required-fields convention — `flaggedSections` disambiguates from the
 * unrelated `compose-compare-to-playbook` shape, which shares `overallRisk` but carries `matches[]`
 * instead) and, on a match, projects each flagged section into a `ComposeAdvisoryCommentItem` and
 * dispatches ONE `compose_advisory_comments` event.
 *
 * ADR-040: this is a CLIENT-DERIVED projection of the SAME ledgered result the review-summary
 * panel renders elsewhere — no second server disposition, no new model call, no ledger read of its
 * own (the terminal chunk's payload is already in hand). ADR-015: `quotedText`/`explanation` are
 * Tier-3 content already rendered to the user (as the fallback JSON-fence / future summary-panel
 * prose); never logged here beyond the receiver's own `console.warn` failure report.
 *
 * @see @spaarke/compose-components useComposeWorkspaceReceivers — `onAdvisoryComments`
 * @see ./useDocQaCitationBridge.ts — the sibling bridge hook this mirrors conventions from
 * @see ./composeResultFormat.ts — the structural-shape-detection convention this reuses
 * @see ./ConversationPane.tsx (dispatchComposeAction) — the injection seam
 */
import * as React from 'react';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets';

/** One `flaggedSections[]` entry from the NDA-REVIEW Action output contract (task 020). */
interface NdaReviewFlaggedSection {
  quotedText?: unknown;
  explanation?: unknown;
  sectionRef?: unknown;
  riskLevel?: unknown;
  standardRef?: unknown;
}

/** The NDA-REVIEW Action's terminal output shape. */
interface NdaReviewResult {
  overallRisk: string;
  flaggedSections: NdaReviewFlaggedSection[];
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asNonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/**
 * Structural shape guard for the NDA-REVIEW Action's output — `overallRisk` (string) +
 * `flaggedSections` (array). Requiring `flaggedSections` specifically disambiguates from the
 * unrelated `compose-compare-to-playbook` shape (`{ overallRisk, matches[] }` — no
 * `flaggedSections`), the same mutually-exclusive-required-fields convention
 * `composeResultFormat.ts` uses for the 5 shipped Compose action shapes.
 */
export function isNdaReviewResult(payload: unknown): payload is NdaReviewResult {
  const record = asRecord(payload);
  if (record === null) return false;
  return typeof record.overallRisk === 'string' && Array.isArray(record.flaggedSections);
}

/**
 * The client-derived advisory-comment projection of one `flaggedSections[]` entry. Structural
 * mirror of `ComposeAdvisoryCommentItem` (`@spaarke/ai-widgets`).
 */
export interface AdvisoryCommentProjection {
  targetText: string;
  explanation: string;
  sectionRef?: string;
  riskLevel?: string;
  standardRef?: string;
}

/**
 * Projects an NDA-REVIEW result's `flaggedSections[]` into advisory-comment items. Entries
 * missing a usable `quotedText` or `explanation` are skipped (defensive against a malformed/
 * partial LLM payload) — never thrown; the caller sees fewer items rather than a crash.
 * Exported for direct unit testing.
 */
export function projectFlaggedSectionsToAdvisoryComments(
  flaggedSections: readonly NdaReviewFlaggedSection[]
): AdvisoryCommentProjection[] {
  const items: AdvisoryCommentProjection[] = [];
  for (const section of flaggedSections) {
    const targetText = asNonEmptyString(section.quotedText);
    const explanation = asNonEmptyString(section.explanation);
    if (!targetText || !explanation) continue;
    items.push({
      targetText,
      explanation,
      sectionRef: asNonEmptyString(section.sectionRef),
      riskLevel: asNonEmptyString(section.riskLevel),
      standardRef: asNonEmptyString(section.standardRef),
    });
  }
  return items;
}

export interface UseNdaReviewAdvisoryCommentsBridgeOptions {
  /** `PaneEventBus` dispatcher (from `useDispatchPaneEvent()`). */
  dispatch: DispatchPaneEvent;
  /** Getter for the active ChatSession id (stable across renders). */
  getSessionId: () => string | null;
}

export interface UseNdaReviewAdvisoryCommentsBridgeResult {
  /**
   * Emits `compose_advisory_comments` on the `workspace` channel IF `result` matches the
   * NDA-REVIEW output shape and projects at least one usable item. No-op for any other Compose
   * action's result shape (`isNdaReviewResult` returns false), or when every flagged section is
   * missing `quotedText`/`explanation`. Wire directly to `dispatchComposeAction`'s informational
   * result branch (the SAME payload `formatComposeActionResultMarkdown` already renders as prose).
   */
  emitFromResult: (result: unknown) => void;
}

export function useNdaReviewAdvisoryCommentsBridge(
  options: UseNdaReviewAdvisoryCommentsBridgeOptions
): UseNdaReviewAdvisoryCommentsBridgeResult {
  const { dispatch, getSessionId } = options;

  const emitFromResult = React.useCallback(
    (result: unknown): void => {
      if (!isNdaReviewResult(result)) return;

      const advisoryComments = projectFlaggedSectionsToAdvisoryComments(result.flaggedSections);
      if (advisoryComments.length === 0) return;

      dispatch('workspace', {
        type: 'compose_advisory_comments',
        advisoryComments,
        // task 032 (right-gutter comment layout): thread the Action's own server-asserted
        // `overallRisk` across the wire — this field was already typed on `NdaReviewResult` above but
        // previously dropped here, forcing `NdaReviewSummaryPanel` to derive it client-side.
        overallRisk: result.overallRisk,
        sessionId: getSessionId() ?? undefined,
        timestamp: new Date().toISOString(),
      });
    },
    [dispatch, getSessionId]
  );

  return { emitFromResult };
}

export default useNdaReviewAdvisoryCommentsBridge;
