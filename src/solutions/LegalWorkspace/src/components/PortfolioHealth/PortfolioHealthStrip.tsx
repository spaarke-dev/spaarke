import * as React from "react";
import { makeStyles, tokens, Text, MessageBar, MessageBarBody } from "@fluentui/react-components";
import {
  MoneyRegular,
  WarningRegular,
  ClockRegular,
  BriefcaseRegular,
} from "@fluentui/react-icons";

import { IPortfolioHealth } from "../../types/portfolio";
import { MetricCard } from "./MetricCard";
import { SpendUtilizationBar } from "./SpendUtilizationBar";

export interface IPortfolioHealthStripProps {
  /** Portfolio health data from the BFF. Null while loading. */
  health?: IPortfolioHealth | null;
  /** True while BFF fetch is in-flight — renders skeleton cards */
  isLoading: boolean;
  /** Error message from BFF fetch, if any */
  error?: string | null;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Responsive 4-column grid that collapses to 2 columns at < 900 px.
   * Griffel does not support CSS custom properties, so we use explicit
   * repeat() for the two breakpoint states.
   */
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(2, 1fr)",
    gap: tokens.spacingHorizontalM,
    "@media (min-width: 900px)": {
      gridTemplateColumns: "repeat(4, 1fr)",
    },
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Format a dollar amount as a compact string, e.g. 1_300_000 → "$1.3M" */
function formatCurrency(amount: number): string {
  if (amount >= 1_000_000) {
    return `$${(amount / 1_000_000).toFixed(1)}M`;
  }
  if (amount >= 1_000) {
    return `$${(amount / 1_000).toFixed(0)}K`;
  }
  return `$${amount.toLocaleString("en-US")}`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * PortfolioHealthStrip — Block 2 of the Legal Operations Workspace.
 *
 * Renders 4 metric cards in a responsive CSS grid:
 *   1. Portfolio Spend   — with SpendUtilizationBar showing budget consumption
 *   2. Matters at Risk   — red accent when count > 0
 *   3. Overdue Events    — red accent when count > 0
 *   4. Active Matters    — neutral informational metric
 *
 * Loading: All four cards show Skeleton placeholders until BFF data arrives.
 * Error:   A MessageBar surfaces the error above the (still-visible) skeleton grid
 *          so the user is informed without a blank panel.
 *
 * Theming: All colors use Fluent semantic tokens — no hardcoded hex/rgb/hsl.
 */
export const PortfolioHealthStrip: React.FC<IPortfolioHealthStripProps> = ({
  health,
  isLoading,
  error,
}) => {
  const styles = useStyles();

  return (
    <section aria-label="Portfolio Health Summary">
      {/* Error banner — shown alongside skeletons so layout doesn't collapse */}
      {error && !isLoading && (
        <div className={styles.errorBar}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      <div className={styles.grid}>
        {/* ------------------------------------------------------------------ */}
        {/* Card 1 — Portfolio Spend                                           */}
        {/* ------------------------------------------------------------------ */}
        <MetricCard
          label="Portfolio Spend"
          value={health ? formatCurrency(health.totalSpend) : "$—"}
          icon={<MoneyRegular />}
          isLoading={isLoading}
          severity="info"
        >
          {health && (
            <SpendUtilizationBar
              totalSpend={health.totalSpend}
              totalBudget={health.totalBudget}
              utilizationPercent={health.utilizationPercent}
            />
          )}
        </MetricCard>

        {/* ------------------------------------------------------------------ */}
        {/* Card 2 — Matters at Risk                                           */}
        {/* ------------------------------------------------------------------ */}
        <MetricCard
          label="Matters at Risk"
          value={health ? health.mattersAtRisk : "—"}
          icon={<WarningRegular />}
          isLoading={isLoading}
          severity={
            health && health.mattersAtRisk > 0 ? "danger" : undefined
          }
        />

        {/* ------------------------------------------------------------------ */}
        {/* Card 3 — Overdue Events                                            */}
        {/* ------------------------------------------------------------------ */}
        <MetricCard
          label="Overdue Events"
          value={health ? health.overdueEvents : "—"}
          icon={<ClockRegular />}
          isLoading={isLoading}
          severity={
            health && health.overdueEvents > 0 ? "danger" : undefined
          }
        />

        {/* ------------------------------------------------------------------ */}
        {/* Card 4 — Active Matters                                            */}
        {/* ------------------------------------------------------------------ */}
        <MetricCard
          label="Active Matters"
          value={health ? health.activeMatters : "—"}
          icon={<BriefcaseRegular />}
          isLoading={isLoading}
        />
      </div>
    </section>
  );
};
