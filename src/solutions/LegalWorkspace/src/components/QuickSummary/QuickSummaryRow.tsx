import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { QuickSummaryMetricCard } from "./QuickSummaryMetricCard";
import { QUICK_SUMMARY_CARDS } from "./quickSummaryConfig";
import { useQuickSummaryCounts } from "../../hooks/useQuickSummaryCounts";
import { navigateToEntityList } from "../../utils/navigation";
import type { IWebApi } from "../../types/xrm";

export interface IQuickSummaryRowProps {
  /** Xrm.WebApi reference for Dataverse queries. */
  webApi: IWebApi;
  /** Current user's systemuserid GUID. */
  userId: string;
  /** Record scope: "my" (user only) or "all" (user + BU teams). */
  scope?: "my" | "all";
  /** Business unit ID (required when scope="all"). */
  businessUnitId?: string;
}

const useStyles = makeStyles({
  // Round 8 Wave 3 (task 110, 2026-05-22): switched from a flex-wrap row
  // to a 2-column CSS grid so the 6 cards (My Matters, My Projects,
  // Assign Work, Open Tasks, Communications, Invoices) naturally arrange
  // as a 2x3 layout per the operator's "My Work" system workspace spec.
  // On narrow viewports the grid collapses to 1 column via auto-fit; on
  // wider viewports it stays at 2 columns so the cards remain readable
  // (the Quick Summary section sits in the Workspace pane, which is
  // typically a fixed-width column rather than full-page).
  row: {
    display: "grid",
    gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
    gap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalL,
    width: "100%",
  },
});

/**
 * QuickSummaryRow — renders 6 metric cards in a 2x3 grid.
 *
 * Each card shows a live count, an icon, and a notification badge
 * (green "New" or red "Overdue"). Clicking navigates to the
 * corresponding Dataverse system view.
 *
 * The section title, toolbar, and card wrapper are now provided by
 * WorkspaceGrid (same sectionCard pattern as other sections).
 *
 * Card list (in order):
 *   1. My Matters
 *   2. My Projects
 *   3. Assign Work
 *   4. Open Tasks
 *   5. Communications (added 2026-05-22 — task 110)
 *   6. Invoices       (added 2026-05-22 — task 110)
 */
export const QuickSummaryRow: React.FC<IQuickSummaryRowProps> = ({
  webApi,
  userId,
  scope,
  businessUnitId,
}) => {
  const styles = useStyles();
  const { counts, badgeCounts, isLoading } = useQuickSummaryCounts(webApi, userId, scope, businessUnitId);

  return (
    <div className={styles.row}>
      {QUICK_SUMMARY_CARDS.map((card) => (
        <QuickSummaryMetricCard
          key={card.id}
          title={card.title}
          count={counts[card.id]}
          isLoading={isLoading}
          ariaLabel={card.ariaLabel}
          icon={card.icon}
          badgeType={card.badgeType}
          badgeCount={badgeCounts[card.id]}
          onClick={() => navigateToEntityList(card.entityName, card.viewId)}
        />
      ))}
    </div>
  );
};

QuickSummaryRow.displayName = "QuickSummaryRow";
