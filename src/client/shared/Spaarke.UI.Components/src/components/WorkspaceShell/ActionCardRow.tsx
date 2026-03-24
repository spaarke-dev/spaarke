/**
 * ActionCardRow — responsive grid row of square ActionCards.
 *
 * Layout requirements:
 *   - Cards maintain square aspect ratio at all viewport widths (768px–2560px)
 *   - Cards WRAP to additional rows instead of stretching
 *   - Uses CSS Grid with `grid-auto-flow: row` and `minmax` for responsive wrapping
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { ActionCard } from "./ActionCard";
import type { ActionCardConfig } from "./types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ActionCardRowProps {
  /** Card configurations to render. */
  cards: ActionCardConfig[];
  /**
   * Map of card id → click handler.
   * Overrides ActionCardConfig.onClick when provided.
   */
  onCardClick?: Partial<Record<string, () => void>>;
  /**
   * Set of card ids to render in a disabled state.
   * Merged with ActionCardConfig.disabled.
   */
  disabledCards?: ReadonlySet<string>;
  /** Maximum number of cards to display. Default: show all. */
  maxVisible?: number;
  /** Additional className applied to the grid container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Responsive CSS Grid row for ActionCards.
   *
   * `minmax(120px, 160px)` matches MetricCardRow exactly so both card types
   * render at the same size in the workspace layout.
   *   - Create as many columns as fit with a minimum width of 120px
   *   - Cards are capped at 160px wide — they do NOT stretch to fill the row
   *   - When viewport narrows, columns wrap to a new row
   *
   * The `aspect-ratio: 1` on each ActionCard ensures square proportions
   * regardless of how wide the column resolves to at any given viewport width.
   */
  row: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalL,
    flexWrap: "wrap",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ActionCardRow — renders a responsive grid of ActionCards.
 *
 * Cards wrap gracefully instead of stretching: the `minmax(120px, 160px)` column
 * definition matches MetricCardRow exactly, so both card types render at the
 * same size. Cards align left rather than growing to fill the container.
 *
 * @example
 * ```tsx
 * <ActionCardRow
 *   cards={ACTION_CARD_CONFIGS}
 *   onCardClick={{ "create-new-matter": handleCreateMatter }}
 *   maxVisible={4}
 * />
 * ```
 */
export const ActionCardRow: React.FC<ActionCardRowProps> = ({
  cards,
  onCardClick = {},
  disabledCards = new Set<string>(),
  maxVisible,
  className,
}) => {
  const styles = useStyles();

  const visibleCards = maxVisible ? cards.slice(0, maxVisible) : cards;

  return (
    <div
      className={`${styles.row}${className ? ` ${className}` : ""}`}
      role="group"
      aria-label="Quick actions"
    >
      {visibleCards.map((config: ActionCardConfig) => (
        <ActionCard
          key={config.id}
          icon={config.icon}
          label={config.label}
          ariaLabel={config.ariaLabel}
          onClick={onCardClick[config.id] ?? config.onClick}
          disabled={config.disabled || disabledCards.has(config.id)}
        />
      ))}
    </div>
  );
};

ActionCardRow.displayName = "ActionCardRow";
