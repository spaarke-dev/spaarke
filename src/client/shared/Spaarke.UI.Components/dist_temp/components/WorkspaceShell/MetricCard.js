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
import { Text, Badge, Spinner, makeStyles, shorthands, tokens, mergeClasses, } from "@fluentui/react-components";
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    card: {
        /**
         * Square aspect ratio: height equals width.
         * MetricCardRow uses minmax(120px, 160px) columns, so cards stay compact
         * and square across all viewport widths.
         */
        aspectRatio: "1",
        position: "relative",
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
        ":focus-visible": {
            outlineWidth: "2px",
            outlineStyle: "solid",
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: "2px",
        },
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
            ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
            boxShadow: tokens.shadow4,
        },
        ":active": {
            backgroundColor: tokens.colorNeutralBackground1Pressed,
            ...shorthands.borderColor(tokens.colorNeutralStroke1Pressed),
            boxShadow: tokens.shadow2,
        },
    },
    iconWrapper: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "32px",
        height: "32px",
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
        marginBottom: tokens.spacingVerticalXS,
        flexShrink: 0,
    },
    value: {
        color: tokens.colorNeutralForeground1,
        lineHeight: tokens.lineHeightBase600,
        marginBottom: tokens.spacingVerticalXXS,
    },
    label: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center",
        lineHeight: tokens.lineHeightBase200,
    },
    spinnerWrapper: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "24px",
        marginBottom: tokens.spacingVerticalXXS,
    },
    badgeWrapper: {
        position: "absolute",
        top: "8px",
        right: "8px",
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
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
export const MetricCard = ({ label, icon: Icon, ariaLabel, value, isLoading = false, badgeVariant, badgeCount, onClick, className, }) => {
    const styles = useStyles();
    const handleKeyDown = React.useCallback((e) => {
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onClick?.();
        }
    }, [onClick]);
    const showBadge = !isLoading &&
        badgeVariant !== undefined &&
        badgeCount !== undefined &&
        badgeCount > 0;
    return (React.createElement("div", { role: "button", tabIndex: 0, "aria-label": ariaLabel, onClick: onClick, onKeyDown: handleKeyDown, className: mergeClasses(styles.card, className) },
        showBadge && (React.createElement("div", { className: styles.badgeWrapper },
            React.createElement(Badge, { appearance: "filled", color: badgeVariant === "overdue" ? "danger" : "success", size: "small" },
                badgeCount,
                " ",
                badgeVariant === "overdue" ? "Overdue" : "New"))),
        React.createElement("div", { className: styles.iconWrapper, "aria-hidden": "true" },
            React.createElement(Icon, { fontSize: 16 })),
        isLoading ? (React.createElement("div", { className: styles.spinnerWrapper },
            React.createElement(Spinner, { size: "small" }))) : (React.createElement(Text, { size: 600, weight: "semibold", className: styles.value }, value !== undefined ? value : "\u2014")),
        React.createElement(Text, { size: 200, className: styles.label }, label)));
};
MetricCard.displayName = "MetricCard";
//# sourceMappingURL=MetricCard.js.map