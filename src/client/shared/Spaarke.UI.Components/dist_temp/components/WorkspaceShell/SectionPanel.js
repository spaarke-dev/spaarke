/**
 * SectionPanel — titled bordered section card with optional toolbar, badge count,
 * and collapsible content area.
 *
 * This is the structural wrapper used by WorkspaceGrid for "Get Started",
 * "Quick Summary", "Latest Updates", "My To Do List", and "My Documents" panels.
 *
 * Design requirements:
 *   - Bordered card with rounded corners using Fluent v9 tokens
 *   - Title bar with optional badge count beside the title
 *   - Optional toolbar row below the title (refresh button, dividers, action buttons)
 *   - Optional collapse/expand toggle
 *   - Fluent v9 semantic tokens only — no hard-coded colors
 *   - Dark mode: inherits token values automatically
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */
import * as React from "react";
import { Text, Badge, Button, makeStyles, shorthands, tokens, mergeClasses, } from "@fluentui/react-components";
import { ChevronDownRegular, ChevronUpRegular } from "@fluentui/react-icons";
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    card: {
        display: "flex",
        flexDirection: "column",
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderWidth("1px"),
        ...shorthands.borderStyle("solid"),
        ...shorthands.borderColor(tokens.colorNeutralStroke2),
        borderRadius: tokens.borderRadiusMedium,
        overflow: "hidden",
    },
    titleBar: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        justifyContent: "space-between",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        flexShrink: 0,
    },
    titleArea: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    toolbarRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderBottomWidth: "1px",
        borderBottomStyle: "solid",
        borderBottomColor: tokens.colorNeutralStroke2,
        backgroundColor: tokens.colorNeutralBackground2,
        flexShrink: 0,
        minHeight: "36px",
    },
    toolbarSpacer: {
        flex: "1 1 0",
    },
    content: {
        display: "flex",
        flexDirection: "column",
        flex: "1 1 auto",
        overflow: "hidden",
    },
    contentCollapsed: {
        display: "none",
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
/**
 * SectionPanel — bordered workspace section with title, optional toolbar, and body.
 *
 * Use this to wrap any workspace section content (action card rows, metric card rows,
 * feed components, lists). The panel handles the structural chrome (border, title bar,
 * toolbar row) so the consumer only needs to supply the title, optional badge count,
 * optional toolbar buttons, and children.
 *
 * @example
 * ```tsx
 * <SectionPanel
 *   title="My To Do List"
 *   badgeCount={todoCount}
 *   toolbar={
 *     <>
 *       <Button appearance="subtle" size="small" icon={<ArrowClockwiseRegular />} onClick={refetch} />
 *       <Button appearance="subtle" size="small" icon={<AddRegular />} onClick={openCreateWizard} />
 *     </>
 *   }
 * >
 *   <SmartToDo embedded webApi={webApi} userId={userId} />
 * </SectionPanel>
 * ```
 */
export const SectionPanel = ({ title, titleContent, badgeCount, toolbar, children, collapsible = false, open: openProp, onOpenChange, className, style, }) => {
    const styles = useStyles();
    // Uncontrolled open state — defaults to true (open)
    const [internalOpen, setInternalOpen] = React.useState(true);
    const isOpen = openProp !== undefined ? openProp : internalOpen;
    const handleToggle = React.useCallback(() => {
        const next = !isOpen;
        if (openProp === undefined) {
            setInternalOpen(next);
        }
        onOpenChange?.(next);
    }, [isOpen, openProp, onOpenChange]);
    const showBadge = badgeCount !== undefined && badgeCount > 0;
    return (React.createElement("div", { className: mergeClasses(styles.card, className), style: style },
        React.createElement("div", { className: styles.titleBar },
            React.createElement("div", { className: styles.titleArea },
                titleContent ?? (React.createElement(Text, { size: 400, weight: "semibold" }, title)),
                showBadge && (React.createElement(Badge, { appearance: "filled", color: "brand", size: "small" }, badgeCount))),
            collapsible && (React.createElement(Button, { appearance: "subtle", size: "small", icon: isOpen ? React.createElement(ChevronUpRegular, null) : React.createElement(ChevronDownRegular, null), onClick: handleToggle, "aria-label": isOpen ? `Collapse ${title}` : `Expand ${title}`, "aria-expanded": isOpen }))),
        toolbar && isOpen && (React.createElement("div", { className: styles.toolbarRow, role: "toolbar", "aria-label": `${title} toolbar` },
            React.createElement("div", { className: styles.toolbarSpacer }),
            toolbar)),
        React.createElement("div", { className: mergeClasses(styles.content, !isOpen && styles.contentCollapsed), "aria-hidden": !isOpen }, children)));
};
SectionPanel.displayName = "SectionPanel";
//# sourceMappingURL=SectionPanel.js.map