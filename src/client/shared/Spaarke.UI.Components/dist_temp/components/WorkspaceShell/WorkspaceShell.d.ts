/**
 * WorkspaceShell — declarative responsive workspace layout container.
 *
 * Accepts a `WorkspaceConfig` object that defines rows, sections, and cards.
 * Renders the appropriate layout using SectionPanel, ActionCardRow, and
 * MetricCardRow based on the section `type` discriminant.
 *
 * Responsive behaviour:
 *   - Multi-column rows collapse to a single column at ≤767px viewport width
 *   - Card rows always wrap; cards never stretch past their max column width
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9, dark mode)
 */
import * as React from "react";
import type { WorkspaceConfig } from "./types";
export interface WorkspaceShellProps {
    /** Declarative configuration describing the workspace layout. */
    config: WorkspaceConfig;
    /** Additional className applied to the outer shell container. */
    className?: string;
    /** Optional inline style applied to the outer shell container. */
    style?: React.CSSProperties;
}
/**
 * WorkspaceShell — renders a multi-row workspace layout from declarative config.
 *
 * Supports two layout variants:
 *   - `"single-column"`: all sections stacked vertically
 *   - `"rows"`: sections grouped into rows with configurable column layouts
 *
 * @example
 * ```tsx
 * const config: WorkspaceConfig = {
 *   layout: "rows",
 *   rows: [
 *     {
 *       id: "row1",
 *       sectionIds: ["get-started", "quick-summary"],
 *       gridTemplateColumns: "1fr 1fr",
 *     },
 *     { id: "row2", sectionIds: ["latest-updates"] },
 *     {
 *       id: "row3",
 *       sectionIds: ["todo", "documents"],
 *       gridTemplateColumns: "1fr 1fr",
 *     },
 *   ],
 *   sections: [
 *     {
 *       id: "get-started",
 *       type: "action-cards",
 *       title: "Get Started",
 *       cards: ACTION_CARD_CONFIGS,
 *       onCardClick: cardClickHandlers,
 *       maxVisible: 4,
 *     },
 *     {
 *       id: "quick-summary",
 *       type: "metric-cards",
 *       title: "Quick Summary",
 *       cards: metricCards,
 *     },
 *     {
 *       id: "latest-updates",
 *       type: "content",
 *       title: "Latest Updates",
 *       badgeCount: feedCount,
 *       renderContent: () => <ActivityFeed embedded webApi={webApi} userId={userId} />,
 *     },
 *     {
 *       id: "todo",
 *       type: "content",
 *       title: "My To Do List",
 *       badgeCount: todoCount,
 *       style: { height: "560px" },
 *       toolbar: <Button icon={<AddRegular />} onClick={openTodoWizard} />,
 *       renderContent: () => <SmartToDo embedded webApi={webApi} userId={userId} />,
 *     },
 *     {
 *       id: "documents",
 *       type: "content",
 *       title: "My Documents",
 *       badgeCount: docCount,
 *       toolbar: <Button icon={<AddRegular />} onClick={addDocument} />,
 *       renderContent: () => <DocumentsTab service={service} userId={userId} />,
 *     },
 *   ],
 * };
 *
 * <WorkspaceShell config={config} />
 * ```
 */
export declare const WorkspaceShell: React.FC<WorkspaceShellProps>;
//# sourceMappingURL=WorkspaceShell.d.ts.map