/**
 * ActionCard — interactive card with icon + label for workspace "Get Started" rows.
 *
 * Design requirements:
 *   - Square aspect ratio via CSS `aspect-ratio: 1`
 *   - Cards wrap to additional rows (handled by ActionCardRow grid)
 *   - Hover elevation (shadow4), focus ring, disabled state
 *   - Fluent v9 semantic tokens only — no hard-coded colors
 *   - Dark mode: inherits token values automatically
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */
import * as React from "react";
import type { FluentIcon } from "@fluentui/react-icons";
export interface ActionCardProps {
    /** Fluent v9 icon component rendered above the label. */
    icon: FluentIcon;
    /** Short label displayed below the icon. */
    label: string;
    /** Accessible description for the card button. */
    ariaLabel: string;
    /** Called when the card is clicked or activated via keyboard. */
    onClick?: () => void;
    /** When true the card renders in a non-interactive disabled state. */
    disabled?: boolean;
    /** Additional className applied to the root element. */
    className?: string;
}
/**
 * ActionCard — interactive square card with icon + label.
 *
 * Rendered as a `role="button"` div to allow flexible sizing inside a CSS Grid
 * container. The parent `ActionCardRow` supplies the grid cell width; this
 * component enforces `aspect-ratio: 1` to guarantee square proportions.
 *
 * @example
 * ```tsx
 * <ActionCard
 *   icon={AddSquareRegular}
 *   label="Create New Matter"
 *   ariaLabel="Create a new legal matter"
 *   onClick={handleCreateMatter}
 * />
 * ```
 */
export declare const ActionCard: React.FC<ActionCardProps>;
//# sourceMappingURL=ActionCard.d.ts.map