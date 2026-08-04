/**
 * useRerunFullAnalysisCard — UAT round-4 item #9 ("agent-B #9",
 * `projects/ai-advanced-capabilities-agreements-r1/notes/uat-round1-2026-08-03.md`).
 *
 * After a QUICK-depth agreement review completes, offers a "Rerun a full analysis (~2–3 min)"
 * CARD — per docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md this is a **persistent act-on item**,
 * not a turn-grounded next-step, so it is a CARD (not a chip). Reuses the SAME `fileId`/
 * `subDomain` the quick run already resolved — no re-classification, no re-upload (the caller's
 * `onRerun` threads straight into `AgreementReviewGateController.rerunThorough`, which dispatches
 * THOROUGH immediately, no ask).
 *
 * Presentation REUSES `SuggestionCard` (the established proactive-card visual,
 * spaarke-notification-spine-r1 task 051) directly — this controller is deliberately NOT
 * `useSuggestionCards`: there is no server outbox row, no expiry, no dismiss endpoint, no BFF
 * re-ground. The trigger is a client-observed dispatch completion
 * (`useConsumerChips`'s `onQuickReviewComplete`), and the card is SESSION-TURN-SCOPED plain React
 * state — a fresh mount / session reset clears it, so "no stacking on reload" falls out for free
 * (there is nothing persisted to go stale).
 *
 * Single-slot by construction (one `pending` value, never a list) — "render the card once per
 * quick run" and "no stacking" both fall out of that shape: a second quick completion (same or a
 * different file) REPLACES the pending card; it never adds a second one.
 *
 * @see ./SuggestionCard.tsx — the reused presentational component.
 * @see ./useAgreementReviewGate.ts — `rerunThorough`, the dispatch this card's action threads into.
 * @see ./useConsumerChips.tsx — `onQuickReviewComplete`, the trigger seam (the `isNdaReview` branch
 *      inside `runBindingDispatch`, the SAME seam task 070's quick-scan caveat reads `reviewDepth` from).
 */
import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { SuggestionCard, type SuggestionCardModel } from "./SuggestionCard";

export interface RerunFullAnalysisCardDeps {
  /** Fires when the user clicks the card's main region — thread straight into `rerunThorough`. */
  onRerun: (fileId: string, subDomainKey?: string) => void;
}

export interface RerunFullAnalysisCardController {
  /** Arms the card for `fileId` (from a just-completed QUICK review). Overwrites any pending card — never stacks. */
  showFor: (info: { fileId: string; subDomain?: string }) => void;
  /** Memoized render node — null when no card is pending. */
  readonly cardSlot: React.ReactNode;
  /** Session-scoped reset (mirrors every other controller's `resetForSession`). */
  resetForSession: () => void;
}

const useStyles = makeStyles({
  // A single-row bordered panel — the SAME visual weight as useSuggestionCards' cardList, sized for
  // exactly ONE row (this controller never stacks more than one card). Tokens only (ADR-021) — no
  // hardcoded colors, correct in both light and dark themes.
  panel: {
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: "hidden",
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalS,
  },
});

interface PendingRerunCard {
  readonly fileId: string;
  readonly subDomain?: string;
}

export function useRerunFullAnalysisCard(
  deps: RerunFullAnalysisCardDeps
): RerunFullAnalysisCardController {
  const { onRerun } = deps;
  const styles = useStyles();
  const [pending, setPending] = React.useState<PendingRerunCard | null>(null);

  const showFor = React.useCallback((info: { fileId: string; subDomain?: string }): void => {
    if (!info.fileId) return;
    setPending({ fileId: info.fileId, subDomain: info.subDomain });
  }, []);

  const handleAction = React.useCallback((): void => {
    if (!pending) return;
    onRerun(pending.fileId, pending.subDomain);
    // Consumed — the card never re-offers itself for this run (a NEW quick run re-arms it).
    setPending(null);
  }, [pending, onRerun]);

  const handleDismiss = React.useCallback((): void => {
    setPending(null);
  }, []);

  const cardSlot = React.useMemo<React.ReactNode>(() => {
    if (!pending) return null;
    const suggestion: SuggestionCardModel = {
      suggestionId: `rerun-full-analysis-${pending.fileId}`,
      title: "Rerun a full analysis",
      snippet: "(~2–3 min)",
      // Presentation only (ADR-039) — the client carries no routing off this value; the click
      // handler below calls the injected `onRerun` directly.
      actionHint: "agreement-review-rerun-thorough",
    };
    return (
      <div className={styles.panel} data-testid="rerun-full-analysis-card">
        <SuggestionCard suggestion={suggestion} onAction={handleAction} onDismiss={handleDismiss} />
      </div>
    );
  }, [pending, styles.panel, handleAction, handleDismiss]);

  const resetForSession = React.useCallback((): void => {
    setPending(null);
  }, []);

  return React.useMemo(
    () => ({ showFor, cardSlot, resetForSession }),
    [showFor, cardSlot, resetForSession]
  );
}

export default useRerunFullAnalysisCard;
