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
import { Text, makeStyles, shorthands, tokens, mergeClasses, } from "@fluentui/react-components";
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    card: {
        /**
         * Flex-stretch sizing: cards grow equally to fill the row, matching
         * QuickSummaryMetricCard behaviour so both rows render identically.
         */
        minWidth: "120px",
        flex: "1 1 0",
        height: "120px",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingVerticalL,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        cursor: "pointer",
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderWidth(tokens.strokeWidthThin),
        ...shorthands.borderStyle("solid"),
        ...shorthands.borderColor(tokens.colorNeutralStroke2),
        borderRadius: tokens.borderRadiusMedium,
        transitionProperty: "box-shadow, background-color, border-color",
        transitionDuration: tokens.durationNormal,
        transitionTimingFunction: tokens.curveEasyEase,
        // Focus ring (keyboard navigation)
        ":focus-visible": {
            outlineWidth: "2px",
            outlineStyle: "solid",
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: "2px",
        },
        // Hover elevation
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
            ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
            boxShadow: tokens.shadow4,
        },
        // Active / pressed state
        ":active": {
            backgroundColor: tokens.colorNeutralBackground1Pressed,
            ...shorthands.borderColor(tokens.colorNeutralStroke1Pressed),
            boxShadow: tokens.shadow2,
        },
    },
    cardDisabled: {
        cursor: "not-allowed",
        opacity: "0.5",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1,
            ...shorthands.borderColor(tokens.colorNeutralStroke2),
            boxShadow: "none",
        },
        ":active": {
            backgroundColor: tokens.colorNeutralBackground1,
            ...shorthands.borderColor(tokens.colorNeutralStroke2),
            boxShadow: "none",
        },
    },
    iconWrapper: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "40px",
        height: "40px",
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorBrandBackground2,
        marginBottom: tokens.spacingVerticalM,
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    label: {
        textAlign: "center",
        color: tokens.colorNeutralForeground1,
        lineHeight: tokens.lineHeightBase200,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
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
export const ActionCard = ({ icon: Icon, label, ariaLabel, onClick, disabled = false, className, }) => {
    const styles = useStyles();
    const handleClick = React.useCallback(() => {
        if (!disabled && onClick) {
            onClick();
        }
    }, [disabled, onClick]);
    const handleKeyDown = React.useCallback((e) => {
        if (!disabled && (e.key === "Enter" || e.key === " ")) {
            e.preventDefault();
            onClick?.();
        }
    }, [disabled, onClick]);
    return (React.createElement("div", { role: "button", tabIndex: disabled ? -1 : 0, "aria-label": ariaLabel, "aria-disabled": disabled, onClick: handleClick, onKeyDown: handleKeyDown, className: mergeClasses(styles.card, disabled && styles.cardDisabled, className) },
        React.createElement("div", { className: styles.iconWrapper, "aria-hidden": "true" },
            React.createElement(Icon, { fontSize: 20 })),
        React.createElement(Text, { size: 200, weight: "semibold", className: styles.label }, label)));
};
ActionCard.displayName = "ActionCard";
//# sourceMappingURL=ActionCard.js.map