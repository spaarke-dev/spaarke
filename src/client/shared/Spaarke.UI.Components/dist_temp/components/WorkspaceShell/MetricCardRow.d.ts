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
import type { MetricCardConfig } from "./types";
export interface MetricCardRowProps {
    /** Metric card configurations to render. */
    cards: MetricCardConfig[];
    /** Additional className applied to the grid container. */
    className?: string;
}
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
export declare const MetricCardRow: React.FC<MetricCardRowProps>;
//# sourceMappingURL=MetricCardRow.d.ts.map