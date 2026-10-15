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

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { MetricCard } from './MetricCard';
import type { MetricCardConfig, MetricCardLayout } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface MetricCardRowProps {
  /** Metric card configurations to render. */
  cards: MetricCardConfig[];
  /** Additional className applied to the grid container. */
  className?: string;
  /** `square` (default: the Quick Summary grid) or `wide` (not square: the worklist count filters, D-24). */
  layout?: MetricCardLayout;
  /** Accessible name of the group. Default "Summary metrics". Give each lane's count filters its own. */
  ariaLabel?: string;
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
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(120px, 160px))',
    gap: tokens.spacingHorizontalL,
    // Align left — prevents last row from stretching if fewer cards than columns
    justifyContent: 'start',
  },
  /** Wide cards: wider columns so the note and progress lines fit; still never stretches to the full width. */
  gridWide: {
    gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 220px))',
    alignItems: 'stretch',
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
  layout = 'square',
  ariaLabel = 'Summary metrics',
}) => {
  const styles = useStyles();

  return (
    <div
      className={mergeClasses(styles.grid, layout === 'wide' && styles.gridWide, className)}
      role="group"
      aria-label={ariaLabel}
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
          selected={config.selected}
          note={config.note}
          progress={config.progress}
          disableWhenEmpty={config.disableWhenEmpty}
          layout={layout}
        />
      ))}
    </div>
  );
};

MetricCardRow.displayName = 'MetricCardRow';
