import * as React from "react";
import { Text, makeStyles, tokens } from "@fluentui/react-components";
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
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  row: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalL,
    flexWrap: "wrap",
  },
});

/**
 * QuickSummaryRow — renders the "Quick Summary" section title and 4 metric cards.
 *
 * Each card shows a live count fetched via useQuickSummaryCounts and navigates
 * to the corresponding Dataverse system view when clicked.
 *
 * Layout:
 *   - "Quick Summary" heading (size 400, weight semibold)
 *   - Horizontal flex row with spacingHorizontalL gap
 *   - 4 QuickSummaryMetricCard components (equal flex distribution)
 */
export const QuickSummaryRow: React.FC<IQuickSummaryRowProps> = ({
  webApi,
  userId,
}) => {
  const styles = useStyles();
  const { counts, isLoading } = useQuickSummaryCounts(webApi, userId);

  return (
    <section className={styles.section}>
      <Text size={400} weight="semibold">
        Quick Summary
      </Text>
      <div className={styles.row}>
        {QUICK_SUMMARY_CARDS.map((card) => (
          <QuickSummaryMetricCard
            key={card.id}
            title={card.title}
            count={counts[card.id]}
            isLoading={isLoading}
            ariaLabel={card.ariaLabel}
            onClick={() => navigateToEntityList(card.entityName, card.viewId)}
          />
        ))}
      </div>
    </section>
  );
};

QuickSummaryRow.displayName = "QuickSummaryRow";
