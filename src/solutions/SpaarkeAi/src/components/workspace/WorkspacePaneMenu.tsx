/**
 * WorkspacePaneMenu.tsx — Workspace pane header control (UAT 2026-07-20).
 *
 * # What changed (UAT 2026-07-20)
 *
 * Previously this was a Fluent v9 `<Menu>` ("Workspaces ▾") whose popover listed
 * every layout with per-row pins plus "+ New Workspace" / "Manage workspaces"
 * actions. Operator feedback:
 *
 *   > "For the Workspace drop down replace with the vertical 3-dot standard
 *   > (already updated for the Assistant pane); replace the current ugly list
 *   > and instead open directly the Manage Workspaces right pane."
 *
 * So the trigger is now an icon-only vertical three-dots (⋮) button — the SAME
 * idiom as `AssistantToolMenu` — and clicking it opens the `ManageWorkspacesPane`
 * OverlayDrawer DIRECTLY (no intermediate menu popover). Every job the old
 * popover did now lives in the Manage pane:
 *
 *   - Workspace SELECTION → row-click in the Manage pane opens the tab.
 *   - PIN / reorder / set-default / edit / delete → the Manage pane row menus.
 *   - "+ New Workspace" → the Manage pane footer's "+ New" button (moved there
 *     from the old popover per the same UAT round).
 *
 * The `tabs` prop is forwarded to `ManageWorkspacesPane` so its Save handler can
 * reconcile the open tab set against the pinned list (reactive tab refresh —
 * see ManageWorkspacesPane).
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local component.
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *
 * @see AssistantToolMenu.tsx — the ⋮ trigger idiom this mirrors.
 * @see ManageWorkspacesPane.tsx — the drawer this opens (now owns selection +
 *   "+ New" + management).
 */

import * as React from "react";
import { makeStyles, Button, Tooltip } from "@fluentui/react-components";
import { MoreVerticalRegular } from "@fluentui/react-icons";
import type { WorkspaceTab } from "./WorkspaceTabManager";
import { ManageWorkspacesPane } from "./ManageWorkspacesPane";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface WorkspacePaneMenuProps {
  /**
   * Currently-open tabs from `WorkspaceTabManager.getSnapshot().tabs`. Forwarded
   * to `ManageWorkspacesPane` so its Save handler can open any pinned workspace
   * that isn't already an open tab (reactive tab refresh).
   */
  tabs: readonly WorkspaceTab[];
  /** Retained for API back-compat with `WorkspacePane.tsx`. */
  activeTabId?: string | null;
  /** Retained for API back-compat with `WorkspacePane.tsx`. */
  onTabSelect?: (tabId: string) => void;
  /** Retained for API back-compat with `WorkspacePane.tsx`. */
  onTabClose?: (tabId: string) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: "auto",
  },
});

// ---------------------------------------------------------------------------
// WorkspacePaneMenu component
// ---------------------------------------------------------------------------

/**
 * `WorkspacePaneMenu` — the ⋮ button rendered in the Workspace `<PaneHeader
 * rightSlot>`. Opens `ManageWorkspacesPane`. See file header for rationale.
 */
export const WorkspacePaneMenu: React.FC<WorkspacePaneMenuProps> = ({ tabs }) => {
  const styles = useStyles();
  const [isManageOpen, setIsManageOpen] = React.useState(false);

  const handleOpen = React.useCallback(() => setIsManageOpen(true), []);
  const handleManageOpenChange = React.useCallback(
    (open: boolean) => setIsManageOpen(open),
    [],
  );

  return (
    <>
      <Tooltip content="Workspaces" relationship="label">
        <Button
          appearance="subtle"
          size="small"
          icon={<MoreVerticalRegular />}
          aria-label="Workspaces"
          className={styles.trigger}
          onClick={handleOpen}
          data-testid="workspace-pane-menu-trigger"
        />
      </Tooltip>

      <ManageWorkspacesPane
        open={isManageOpen}
        onOpenChange={handleManageOpenChange}
        tabs={tabs}
      />
    </>
  );
};

WorkspacePaneMenu.displayName = "WorkspacePaneMenu";
