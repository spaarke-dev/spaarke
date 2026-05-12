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
import type { ActionCardConfig } from "./types";
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
export declare const ActionCardRow: React.FC<ActionCardRowProps>;
//# sourceMappingURL=ActionCardRow.d.ts.map