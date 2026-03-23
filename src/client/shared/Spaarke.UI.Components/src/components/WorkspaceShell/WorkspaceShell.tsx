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
import { SectionPanel } from "./SectionPanel";
import { ActionCardRow } from "./ActionCardRow";
import { MetricCardRow } from "./MetricCardRow";
import { useWorkspaceShellStyles, useSectionContentPaddingStyles } from "./WorkspaceShell.styles";
import type {
  WorkspaceConfig,
  SectionConfig,
  ActionCardSectionConfig,
  MetricCardSectionConfig,
  ContentSectionConfig,
  WorkspaceRowConfig,
} from "./types";

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Renders the interior of a SectionPanel based on its type. */
const renderSectionBody = (section: SectionConfig): React.ReactNode => {
  switch (section.type) {
    case "action-cards": {
      const s = section as ActionCardSectionConfig;
      return <ActionCardRow cards={s.cards} onCardClick={s.onCardClick} disabledCards={s.disabledCards} maxVisible={s.maxVisible} />;
    }
    case "metric-cards": {
      const s = section as MetricCardSectionConfig;
      return <MetricCardRow cards={s.cards} />;
    }
    case "content": {
      const s = section as ContentSectionConfig;
      return s.renderContent();
    }
    default:
      return null;
  }
};

// ---------------------------------------------------------------------------
// SectionPanelWrapper — thin wrapper that adds standard padding for card rows
// ---------------------------------------------------------------------------

interface SectionPanelWrapperProps {
  section: SectionConfig;
  paddedClassName: string;
}

const SectionPanelWrapper: React.FC<SectionPanelWrapperProps> = ({
  section,
  paddedClassName,
}) => {
  const body = renderSectionBody(section);

  // Action-cards and metric-cards get standard interior padding; content sections
  // manage their own spacing (the consumer-supplied renderContent handles it).
  const needsPadding =
    section.type === "action-cards" || section.type === "metric-cards";

  return (
    <SectionPanel
      title={section.title}
      badgeCount={section.badgeCount}
      toolbar={section.toolbar}
      className={section.className}
      style={section.style}
    >
      {needsPadding ? (
        <div className={paddedClassName}>{body}</div>
      ) : (
        body
      )}
    </SectionPanel>
  );
};

// ---------------------------------------------------------------------------
// WorkspaceShell props
// ---------------------------------------------------------------------------

export interface WorkspaceShellProps {
  /** Declarative configuration describing the workspace layout. */
  config: WorkspaceConfig;
  /** Additional className applied to the outer shell container. */
  className?: string;
  /** Optional inline style applied to the outer shell container. */
  style?: React.CSSProperties;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

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
export const WorkspaceShell: React.FC<WorkspaceShellProps> = ({
  config,
  className,
  style,
}) => {
  const shellStyles = useWorkspaceShellStyles();
  const contentPaddingStyles = useSectionContentPaddingStyles();

  // Build a map of section id → SectionConfig for O(1) lookup
  const sectionMap = React.useMemo(
    () => new Map(config.sections.map((s) => [s.id, s])),
    [config.sections]
  );

  const renderSection = React.useCallback(
    (section: SectionConfig) => (
      <SectionPanelWrapper
        key={section.id}
        section={section}
        paddedClassName={contentPaddingStyles.padded}
      />
    ),
    [contentPaddingStyles.padded]
  );

  // -------------------------------------------------------------------------
  // Single-column layout
  // -------------------------------------------------------------------------

  if (config.layout === "single-column") {
    return (
      <div
        className={`${shellStyles.shell}${className ? ` ${className}` : ""}`}
        style={style}
      >
        {config.sections.map(renderSection)}
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Rows layout
  // -------------------------------------------------------------------------

  const rows = config.rows ?? [];

  return (
    <div
      className={`${shellStyles.shell}${className ? ` ${className}` : ""}`}
      style={style}
    >
      {rows.map((row: WorkspaceRowConfig) => {
        const rowSections = row.sectionIds
          .map((id) => sectionMap.get(id))
          .filter((s): s is SectionConfig => s !== undefined);

        if (rowSections.length === 0) return null;

        // Default grid-template-columns: equal columns for all sections in the row
        const defaultColumns = `repeat(${rowSections.length}, 1fr)`;
        const columns = row.gridTemplateColumns ?? defaultColumns;
        const columnsSmall = row.gridTemplateColumnsSmall ?? "1fr";

        return (
          <div
            key={row.id}
            className={shellStyles.row}
            style={{
              gridTemplateColumns: columns,
              // Inline media-query equivalent via CSS custom properties is not
              // available in inline styles. Consumers who need per-row responsive
              // overrides should pass a className instead. The shell CSS handles
              // the global ≤767px single-column collapse via Griffel makeStyles.
            }}
            data-columns-small={columnsSmall}
          >
            {rowSections.map(renderSection)}
          </div>
        );
      })}
    </div>
  );
};

WorkspaceShell.displayName = "WorkspaceShell";
