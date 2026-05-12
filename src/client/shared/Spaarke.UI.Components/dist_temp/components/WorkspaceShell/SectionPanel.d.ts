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
export interface SectionPanelProps {
    /** Section title used for aria-labels and accessibility. */
    title: string;
    /**
     * Optional React node to render in the title area instead of the title text.
     * When provided, renders in place of `<Text>{title}</Text>`.
     * The `title` string is still used for the collapsible button's aria-label.
     */
    titleContent?: React.ReactNode;
    /** Optional badge count shown beside the title. Renders only when > 0. */
    badgeCount?: number;
    /**
     * Optional toolbar content rendered on the right side of the title bar.
     * Typically contains refresh/open/add buttons from the workspace consumer.
     */
    toolbar?: React.ReactNode;
    /** Section body content. */
    children?: React.ReactNode;
    /**
     * When true, the section supports user-initiated collapse/expand.
     * The expand/collapse button appears in the title bar.
     * Default: false (always expanded).
     */
    collapsible?: boolean;
    /**
     * Controlled open state for the panel.
     * Use together with `onOpenChange` for controlled mode.
     * Defaults to `true` when not provided (uncontrolled, always open).
     */
    open?: boolean;
    /** Called when the user toggles the open state. */
    onOpenChange?: (open: boolean) => void;
    /** Additional className applied to the outer card container. */
    className?: string;
    /** Optional inline style applied to the outer card container. */
    style?: React.CSSProperties;
}
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
export declare const SectionPanel: React.FC<SectionPanelProps>;
//# sourceMappingURL=SectionPanel.d.ts.map