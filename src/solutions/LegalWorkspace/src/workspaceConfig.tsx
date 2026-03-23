/**
 * workspaceConfig.tsx — declarative WorkspaceConfig factory for LegalWorkspace.
 *
 * Builds the WorkspaceShell configuration from runtime dependencies (handlers,
 * live counts, refs, webApi, userId). The resulting config object is passed
 * directly to <WorkspaceShell config={config} />.
 *
 * Standards: ADR-012 (consume shared WorkspaceShell), ADR-021 (Fluent v9 tokens)
 */

import * as React from "react";
import { Button } from "@fluentui/react-components";
import {
  OpenRegular,
  ArrowClockwiseRegular,
  AddRegular,
} from "@fluentui/react-icons";
import type { WorkspaceConfig } from "@spaarke/ui-components";
import { ACTION_CARD_CONFIGS } from "./components/GetStarted/getStartedConfig";
import { ActivityFeed } from "./components/ActivityFeed/ActivityFeed";
import { SmartToDo } from "./components/SmartToDo/SmartToDo";
import { DocumentsTab } from "./components/RecordCards/DocumentsTab";
import { QuickSummaryRow } from "./components/QuickSummary/QuickSummaryRow";
import type { IWebApi } from "./types/xrm";
import type { DataverseService } from "./services/DataverseService";

// ---------------------------------------------------------------------------
// Toolbar divider — thin vertical separator between toolbar button groups.
// Matches the original WorkspaceGrid layout using an inline span.
// ---------------------------------------------------------------------------

const ToolbarDivider: React.FC = () => (
  <span
    aria-hidden="true"
    style={{
      width: "1px",
      height: "20px",
      backgroundColor: "var(--colorNeutralStroke2)",
      marginLeft: "2px",
      marginRight: "2px",
      flexShrink: 0,
      display: "inline-block",
    }}
  />
);

// ---------------------------------------------------------------------------
// Config factory parameters
// ---------------------------------------------------------------------------

export interface WorkspaceConfigParams {
  /** Xrm.WebApi reference forwarded to data-bound sections. */
  webApi: IWebApi;
  /** Current user's systemuserid GUID. */
  userId: string;
  /** DataverseService instance for DocumentsTab. */
  service: DataverseService;

  // Live badge counts
  feedCount: number;
  todoCount: number;
  docCount: number;

  // Refetch callbacks
  onTodoRefetch: () => void;
  onDocRefetch: () => void;

  // Refetch-ready registration callbacks
  onTodoRefetchReady: (refetch: () => void) => void;
  onFeedRefetchReady: (refetch: () => void) => void;
  onDocRefetchReady: (refetch: () => void) => void;

  // Count change callbacks
  onFeedCountChange: (n: number) => void;
  onTodoCountChange: (n: number) => void;
  onDocCountChange: (n: number) => void;

  // Action handlers
  onExpandClick: () => void;
  onDashboardOpen: () => void;
  onOpenAllUpdates: () => void;
  onCreateEvent: () => void;
  onOpenTodoWizard: () => void;
  onOpenTodoDialog: () => void;
  onAddDocument: () => void;
  onOpenDocumentsDialog: () => void;

  /** Full card click handler map (all 7 action cards). */
  cardClickHandlers: Partial<Record<string, () => void>>;
}

// ---------------------------------------------------------------------------
// Config factory
// ---------------------------------------------------------------------------

/**
 * buildWorkspaceConfig — constructs the WorkspaceShell declarative config for
 * LegalWorkspace from the runtime handler/state dependencies.
 *
 * Called inside WorkspaceGrid (or a custom hook) so that React state and
 * callbacks are available when constructing toolbar JSX and renderContent fns.
 */
export function buildWorkspaceConfig(p: WorkspaceConfigParams): WorkspaceConfig {
  // Map ACTION_CARD_CONFIGS (local IActionCardConfig) to ActionCardConfig
  // (shared library). The shapes are identical; we cast to satisfy the type.
  const actionCards = ACTION_CARD_CONFIGS.map((c) => ({
    id: c.id,
    label: c.label,
    icon: c.icon,
    ariaLabel: c.ariaLabel,
  }));

  // -------------------------------------------------------------------------
  // Toolbars — built as JSX so they can include callbacks bound to handlers.
  // SectionPanel renders the toolbar prop after an internal spacer div,
  // so all items here appear on the right side of the toolbar row.
  // For sections that need left+right groups we wrap both inside a fragment
  // and use a flex spacer between them (the SectionPanel spacer covers flex-1
  // before our node, so we only need an inner spacer between left and right).
  // -------------------------------------------------------------------------

  // Get Started toolbar: expand/Playbook Library button (right-aligned via SectionPanel spacer)
  const getStartedToolbar = (
    <Button
      appearance="subtle"
      size="small"
      icon={<OpenRegular />}
      onClick={p.onExpandClick}
      aria-label="Open Playbook Library"
    />
  );

  // Quick Summary toolbar: refresh (left) + divider + open dashboard (right)
  const quickSummaryToolbar = (
    <>
      <Button
        appearance="subtle"
        size="small"
        icon={<ArrowClockwiseRegular />}
        aria-label="Refresh Quick Summary"
        style={{ marginRight: "auto" }}
      />
      <ToolbarDivider />
      <Button
        appearance="subtle"
        size="small"
        icon={<OpenRegular />}
        onClick={p.onDashboardOpen}
        aria-label="Open Quick Summary Dashboard"
      />
    </>
  );

  // My To Do List toolbar: refresh (left) + divider + add + open (right)
  const todoToolbar = (
    <>
      <Button
        appearance="subtle"
        size="small"
        icon={<ArrowClockwiseRegular />}
        onClick={p.onTodoRefetch}
        aria-label="Refresh To Do list"
        style={{ marginRight: "auto" }}
      />
      <ToolbarDivider />
      <div style={{ display: "flex", flexDirection: "row", alignItems: "center", gap: "15px" }}>
        <Button
          appearance="subtle"
          size="small"
          icon={<AddRegular />}
          onClick={p.onOpenTodoWizard}
          aria-label="Create new to do"
        />
        <Button
          appearance="subtle"
          size="small"
          icon={<OpenRegular />}
          onClick={p.onOpenTodoDialog}
          aria-label="Open full To Do list"
        />
      </div>
    </>
  );

  // My Documents toolbar: refresh (left) + divider + add + open (right)
  const documentsToolbar = (
    <>
      <Button
        appearance="subtle"
        size="small"
        icon={<ArrowClockwiseRegular />}
        onClick={p.onDocRefetch}
        aria-label="Refresh documents"
        style={{ marginRight: "auto" }}
      />
      <ToolbarDivider />
      <div style={{ display: "flex", flexDirection: "row", alignItems: "center", gap: "15px" }}>
        <Button
          appearance="subtle"
          size="small"
          icon={<AddRegular />}
          onClick={p.onAddDocument}
          aria-label="Add document"
        />
        <Button
          appearance="subtle"
          size="small"
          icon={<OpenRegular />}
          onClick={p.onOpenDocumentsDialog}
          aria-label="Open all documents"
        />
      </div>
    </>
  );

  // -------------------------------------------------------------------------
  // WorkspaceConfig
  // -------------------------------------------------------------------------

  return {
    layout: "rows",
    rows: [
      {
        id: "row1",
        sectionIds: ["get-started", "quick-summary"],
        gridTemplateColumns: "1fr 1fr",
      },
      {
        id: "row2",
        sectionIds: ["latest-updates"],
      },
      {
        id: "row3",
        sectionIds: ["todo", "documents"],
        gridTemplateColumns: "1fr 1fr",
      },
    ],
    sections: [
      // -----------------------------------------------------------------------
      // Get Started — ActionCardRow with 4 visible cards and expand dialog
      // -----------------------------------------------------------------------
      {
        id: "get-started",
        type: "action-cards",
        title: "Get Started",
        toolbar: getStartedToolbar,
        cards: actionCards,
        onCardClick: p.cardClickHandlers,
        maxVisible: 4,
        style: { minHeight: "auto" },
      },

      // -----------------------------------------------------------------------
      // Quick Summary — MetricCardRow rendered via QuickSummaryRow
      // Using "content" type so QuickSummaryRow drives its own data fetching
      // -----------------------------------------------------------------------
      {
        id: "quick-summary",
        type: "content",
        title: "Quick Summary",
        toolbar: quickSummaryToolbar,
        style: { minHeight: "auto" },
        renderContent: () => (
          <div
            style={{
              padding: "8px 12px 12px 12px",
            }}
          >
            <QuickSummaryRow webApi={p.webApi} userId={p.userId} />
          </div>
        ),
      },

      // -----------------------------------------------------------------------
      // Latest Updates — ActivityFeed (full width, min 325px)
      // -----------------------------------------------------------------------
      {
        id: "latest-updates",
        type: "content",
        title: "Latest Updates",
        badgeCount: p.feedCount,
        style: { minHeight: "325px" },
        renderContent: () => (
          <ActivityFeed
            embedded
            webApi={p.webApi}
            userId={p.userId}
            textOnlyFilter
            gridLayout
            hideOverflowMenu
            onCountChange={p.onFeedCountChange}
            onOpenAll={p.onOpenAllUpdates}
            onRefetchReady={p.onFeedRefetchReady}
            onCreateNew={p.onCreateEvent}
          />
        ),
      },

      // -----------------------------------------------------------------------
      // My To Do List — SmartToDo (height: 560px, 50% width)
      // -----------------------------------------------------------------------
      {
        id: "todo",
        type: "content",
        title: "My To Do List",
        badgeCount: p.todoCount,
        toolbar: todoToolbar,
        style: { height: "560px" },
        renderContent: () => (
          <SmartToDo
            embedded
            webApi={p.webApi}
            userId={p.userId}
            disableSidePane
            onCountChange={p.onTodoCountChange}
            onRefetchReady={p.onTodoRefetchReady}
          />
        ),
      },

      // -----------------------------------------------------------------------
      // My Documents — DocumentsTab (50% width, overflow visible)
      // -----------------------------------------------------------------------
      {
        id: "documents",
        type: "content",
        title: "My Documents",
        badgeCount: p.docCount,
        toolbar: documentsToolbar,
        style: { minHeight: "auto", overflow: "visible" },
        renderContent: () => (
          <div style={{ display: "flex", flexDirection: "column", flex: "1 1 0", overflow: "visible" }}>
            <DocumentsTab
              service={p.service}
              userId={p.userId}
              maxVisible={6}
              onCountChange={p.onDocCountChange}
              onRefetchReady={p.onDocRefetchReady}
            />
          </div>
        ),
      },
    ],
  };
}
