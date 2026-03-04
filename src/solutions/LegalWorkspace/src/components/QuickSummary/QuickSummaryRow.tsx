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
}

const useStyles = makeStyles({
  row: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalL,
    flexWrap: "wrap",
  },
});

/**
 * QuickSummaryRow — renders 4 metric cards in a horizontal row.
 *
 * Each card shows a live count, an icon, and a notification badge
 * (green "New" or red "Overdue"). Clicking navigates to the
 * corresponding Dataverse system view.
 *
 * The section title, toolbar, and card wrapper are now provided by
 * WorkspaceGrid (same sectionCard pattern as other sections).
 */
export const QuickSummaryRow: React.FC<IQuickSummaryRowProps> = ({
  webApi,
  userId,
}) => {
  const styles = useStyles();
  const { counts, badgeCounts, isLoading } = useQuickSummaryCounts(webApi, userId);

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
