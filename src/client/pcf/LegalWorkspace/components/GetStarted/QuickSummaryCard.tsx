import * as React from "react";
import {
  Text,
  Link,
  Divider,
  Spinner,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import { IQuickSummary } from "../../types";

export interface IQuickSummaryCardProps {
  /** Portfolio metrics. When undefined the card renders a loading skeleton. */
  summary?: IQuickSummary;
  /** True while the BFF briefing endpoint is in-flight. */
  isLoading?: boolean;
  /** Error string from a failed fetch — shown instead of metrics. */
  error?: string;
  /**
   * Called when the user clicks "Full briefing".
   * The briefing dialog (task 021) is wired here.
   */
  onOpenBriefing?: () => void;
}

/**
 * Format a raw number as compact currency: 125000 → "$125K".
 * Used by callers that have raw numeric spend/budget values rather than
 * pre-formatted BFF strings (e.g. fallback rendering or unit tests).
 */
export function formatCompactCurrency(value: number): string {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    notation: "compact",
    maximumFractionDigits: 0,
  }).format(value);
}

const useStyles = makeStyles({
  card: {
    // Fixed width per spec (280-320px)
    width: "300px",
    minWidth: "280px",
    maxWidth: "320px",
    flexShrink: 0,
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth(tokens.strokeWidthThin),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    gap: tokens.spacingVerticalS,
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: tokens.spacingVerticalXS,
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
  metricsGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    rowGap: tokens.spacingVerticalM,
    columnGap: tokens.spacingHorizontalM,
  },
  metric: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  metricLabel: {
    color: tokens.colorNeutralForeground3,
  },
  metricValue: {
    color: tokens.colorNeutralForeground1,
  },
  // At-risk / overdue use the semantic red palette token
  metricValueDanger: {
    color: tokens.colorPaletteRedForeground1,
  },
  divider: {
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
  topPriority: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  topPriorityLabel: {
    color: tokens.colorNeutralForeground3,
  },
  topPriorityValue: {
    color: tokens.colorNeutralForeground1,
    // Clamp to 2 lines to prevent layout expansion
    display: "-webkit-box",
    WebkitLineClamp: "2",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    marginTop: tokens.spacingVerticalXS,
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "120px",
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
});

/**
 * QuickSummaryCard — fixed-width (280-320px) portfolio metrics card.
 *
 * Displays deterministic aggregation data from the BFF briefing endpoint:
 *   - Active matters count
 *   - Spend / Budget (compact currency)
 *   - At-risk count (colorPaletteRedForeground1)
 *   - Overdue count (colorPaletteRedForeground1)
 *   - Top priority matter name
 *   - "Full briefing" link (triggers onOpenBriefing — wired in task 021)
 *
 * All colors use Fluent v9 semantic tokens. Zero hardcoded hex/rgb/hsl values.
 */
export const QuickSummaryCard: React.FC<IQuickSummaryCardProps> = ({
  summary,
  isLoading = false,
  error,
  onOpenBriefing,
}) => {
  const styles = useStyles();

  const renderBody = (): React.ReactNode => {
    if (isLoading) {
      return (
        <div className={styles.loadingContainer}>
          <Spinner size="small" label="Loading summary…" />
        </div>
      );
    }

    if (error) {
      return (
        <Text size={200} className={styles.errorText}>
          {error}
        </Text>
      );
    }

    if (!summary) {
      return (
        <Text size={200} className={styles.errorText}>
          No summary data available.
        </Text>
      );
    }

    // Spend/budget: use pre-formatted strings when provided by BFF,
    // otherwise derive compact currency from raw numeric types via portfolio.
    // IQuickSummary provides spendFormatted / budgetFormatted as strings.
    const spendDisplay = summary.spendFormatted;
    const budgetDisplay = summary.budgetFormatted;

    return (
      <>
        <div className={styles.metricsGrid}>
          {/* Active matters */}
          <div className={styles.metric}>
            <Text size={100} className={styles.metricLabel}>
              Active Matters
            </Text>
            <Text size={400} weight="semibold" className={styles.metricValue}>
              {summary.activeCount}
            </Text>
          </div>

          {/* Spend / Budget */}
          <div className={styles.metric}>
            <Text size={100} className={styles.metricLabel}>
              Spend / Budget
            </Text>
            <Text size={300} weight="semibold" className={styles.metricValue}>
              {spendDisplay} / {budgetDisplay}
            </Text>
          </div>

          {/* At-risk count */}
          <div className={styles.metric}>
            <Text size={100} className={styles.metricLabel}>
              At Risk
            </Text>
            <Text
              size={400}
              weight="semibold"
              className={
                summary.atRiskCount > 0
                  ? styles.metricValueDanger
                  : styles.metricValue
              }
            >
              {summary.atRiskCount}
            </Text>
          </div>

          {/* Overdue count */}
          <div className={styles.metric}>
            <Text size={100} className={styles.metricLabel}>
              Overdue
            </Text>
            <Text
              size={400}
              weight="semibold"
              className={
                summary.overdueCount > 0
                  ? styles.metricValueDanger
                  : styles.metricValue
              }
            >
              {summary.overdueCount}
            </Text>
          </div>
        </div>

        {summary.topPriorityMatter && (
          <>
            <Divider className={styles.divider} />
            <div className={styles.topPriority}>
              <Text size={100} className={styles.topPriorityLabel}>
                Top Priority Matter
              </Text>
              <Text
                size={200}
                weight="semibold"
                className={styles.topPriorityValue}
                title={summary.topPriorityMatter}
              >
                {summary.topPriorityMatter}
              </Text>
            </div>
          </>
        )}
      </>
    );
  };

  return (
    <div className={styles.card} role="region" aria-label="Quick Portfolio Summary">
      {/* Card header */}
      <div className={styles.header}>
        <Text size={300} weight="semibold" className={styles.title}>
          Quick Summary
        </Text>
      </div>

      <Divider className={styles.divider} />

      {/* Body — metrics, loading state, or error */}
      {renderBody()}

      {/* Footer: Full briefing link (wired in task 021) */}
      {!isLoading && !error && summary && (
        <div className={styles.footer}>
          <Link
            onClick={onOpenBriefing}
            aria-label="Open full portfolio briefing"
            as="button"
          >
            Full briefing
          </Link>
        </div>
      )}
    </div>
  );
};

QuickSummaryCard.displayName = "QuickSummaryCard";
