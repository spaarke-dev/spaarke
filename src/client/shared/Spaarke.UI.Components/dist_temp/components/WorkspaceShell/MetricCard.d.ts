/**
 * MetricCard — interactive square card displaying a numeric metric with icon
 * and optional notification badge.
 *
 * Design requirements:
 *   - Square aspect ratio via CSS `aspect-ratio: 1`
 *   - Loading state renders a Fluent Spinner
 *   - Badge variants: "new" (success/green), "overdue" (danger/red)
 *   - Fluent v9 semantic tokens only — no hard-coded colors
 *   - Dark mode: inherits token values automatically
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */
import * as React from "react";
import type { FluentIcon } from "@fluentui/react-icons";
import type { MetricBadgeVariant, MetricTrend } from "./types";
export interface MetricCardProps {
    /** Display label shown below the count. */
    label: string;
    /** Fluent v9 icon component for the card. */
    icon: FluentIcon;
    /** Accessible label for the card button. */
    ariaLabel: string;
    /** The numeric value to display. undefined renders an em-dash. */
    value?: number;
    /** When true, shows a Fluent Spinner instead of the value. */
    isLoading?: boolean;
    /** Optional trend direction (currently reserved for future visual indicator). */
    trend?: MetricTrend;
    /** Optional badge variant: "new" → green, "overdue" → red. */
    badgeVariant?: MetricBadgeVariant;
    /** Badge count. Shown only when > 0 and not loading. */
    badgeCount?: number;
    /** Called when the card is clicked or activated via keyboard. */
    onClick?: () => void;
    /** Additional className applied to the root element. */
    className?: string;
}
/**
 * MetricCard — interactive square card displaying a numeric metric.
 *
 * Shows an icon, a large count value (or spinner while loading), and a
 * label below. An optional notification badge (new/overdue) appears in the
 * top-right corner when `badgeCount > 0`.
 *
 * @example
 * ```tsx
 * <MetricCard
 *   label="My Matters"
 *   icon={GavelRegular}
 *   ariaLabel="View my matters"
 *   value={counts.matters}
 *   isLoading={isLoading}
 *   badgeVariant="new"
 *   badgeCount={3}
 *   onClick={() => navigateToMatters()}
 * />
 * ```
 */
export declare const MetricCard: React.FC<MetricCardProps>;
//# sourceMappingURL=MetricCard.d.ts.map