/**
 * MetricCardRow — responsive grid row of square MetricCards.
 *
 * Layout requirements:
 *   - Cards maintain square aspect ratio at all viewport widths (768px–2560px)
 *   - Cards WRAP to additional rows instead of stretching
 *   - Same CSS Grid pattern as ActionCardRow
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { MetricCard } from "./MetricCard";
import type { MetricCardConfig } from "./types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface MetricCardRowProps {
  /** Metric card configurations to render. */
  cards: MetricCardConfig[];
  /** Additional className applied to the grid container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Responsive CSS Grid row for MetricCards.
   *
   * `minmax(120px, 160px)` keeps metric cards slightly wider than action cards
   * (to accommodate the numeric value + label) while still wrapping gracefully.
   * `justifyContent: "start"` prevents the last row from stretching.
   */
  grid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fill, minmax(120px, 160px))",
    gap: tokens.spacingHorizontalL,
    // Align left — prevents last row from stretching if fewer cards than columns
    justifyContent: "start",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * MetricCardRow — renders a responsive grid of MetricCards.
 *
 * Cards wrap gracefully at narrow viewports. The `minmax(120px, 160px)` column
 * definition ensures cards stay compact and square without ever stretching to
 * fill the full container width.
 *
 * @example
 * ```tsx
 * <MetricCardRow
 *   cards={[
 *     { id: "matters", label: "My Matters", icon: GavelRegular, ariaLabel: "...", value: 12, isLoading: false },
 *     { id: "projects", label: "My Projects", icon: TaskListSquareLtrRegular, ariaLabel: "...", value: 5 },
 *   ]}
 * />
 * ```
 */
export const MetricCardRow: React.FC<MetricCardRowProps> = ({
  cards,
  className,
}) => {
  const styles = useStyles();

  return (
    <div
      className={`${styles.grid}${className ? ` ${className}` : ""}`}
      role="group"
      aria-label="Summary metrics"
    >
      {cards.map((config: MetricCardConfig) => (
        <MetricCard
          key={config.id}
          label={config.label}
          icon={config.icon}
          ariaLabel={config.ariaLabel}
          value={config.value}
          isLoading={config.isLoading}
          trend={config.trend}
          badgeVariant={config.badgeVariant}
          badgeCount={config.badgeCount}
          onClick={config.onClick}
        />
      ))}
    </div>
  );
};

MetricCardRow.displayName = "MetricCardRow";
