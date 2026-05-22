/**
 * WorkspacePaneMenu.tsx — Dropdown menu rendered in the WorkspacePane PaneHeader
 * rightSlot. Replaces the in-pane tab bar with a Fluent v9 Menu surface that
 * unifies four interaction surfaces in one trigger (FR-12):
 *
 *   1. Open    — currently-open non-Home tabs, newest first, each with a
 *                trailing `DismissRegular` close affordance.
 *   2. Home    — pinned, non-closable LegalWorkspace home tab; selecting it
 *                activates the Home tab. NO close affordance (FR-13).
 *   3. Switch Workspace — list of workspace layouts fetched from the BFF via
 *                `useWorkspaceLayouts` (the SpaarkeAi adaptation of the WORKING
 *                LegalWorkspace hook). Selecting one persists the choice and
 *                dispatches a layout-change signal. A trailing "+ New Workspace"
 *                action launches the WorkspaceLayoutWizard with the SpaarkeAi
 *                6-template filter (FR-14).
 *   4. Edit current workspace — final action that launches the wizard in
 *                edit / saveAs mode for the active layout.
 *
 * The menu is keyboard-navigable and ARIA-labeled (NFR-05). Styling uses
 * Fluent v9 tokens only — no hex or rgba literals (ADR-021).
 *
 * Composition rationale:
 *   - Tab-state callbacks are passed in by `WorkspacePane.tsx` so the menu has
 *     no direct dependency on `WorkspaceTabManager`. The parent owns tab
 *     lifecycle (single source of truth).
 *
 *   - Layout fetching uses `useWorkspaceLayouts` from
 *     `../../hooks/useWorkspaceLayouts.ts` — a faithful SpaarkeAi adaptation
 *     of LegalWorkspace's working `useWorkspaceLayouts` hook (cache-first
 *     hydration, parallel list+default fetch, pinned-id resolution,
 *     setActiveLayoutById + refetch). This file used to inline its own
 *     `useWorkspaceLayoutsList` (task 081); that was a parallel
 *     reimplementation. Round 4 Fix 1 (2026-05-21) replaced it with the
 *     reused working pattern per operator's reuse principle: "when we have
 *     working components reuse them."
 *
 *     Auth still flows through `useAiSession()` (ADR-028, task 081
 *     root-cause fix) — the hook takes `authenticatedFetch` + `bffBaseUrl` +
 *     `isAuthenticated` as args so the effect auto-defers until runtime
 *     config + auth are ready. The hook file's JSDoc cross-references the
 *     LegalWorkspace source so both stay in sync.
 *
 *   - Wizard launch uses `Xrm.Navigation.navigateTo` with `pageType:"webresource"`
 *     and webresourceName `sprk_workspacelayoutwizard`, matching the canonical
 *     pattern in `LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`.
 *     `templateFilter` is passed as a comma-separated query parameter so the
 *     wizard's main.tsx can parse it back into a typed `LayoutTemplateId[]`.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local component (depends on tab + workspace context).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: BFF calls via `authenticatedFetch`; no `accessToken` props.
 *   - FR-12 / FR-13 / FR-14 / NFR-05.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  MenuGroupHeader,
  Button,
  Spinner,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import {
  ChevronDownRegular,
  DismissRegular,
  AddRegular,
  EditRegular,
  HomeRegular,
  CheckmarkRegular,
} from "@fluentui/react-icons";
import { useAiSession, useDispatchPaneEvent } from "@spaarke/ai-widgets";
import type { WorkspaceTab } from "./WorkspaceTabManager";
// useWorkspaceLayouts — SpaarkeAi adaptation of LegalWorkspace's working hook.
// See ../../hooks/useWorkspaceLayouts.ts header for the reuse rationale.
import { useWorkspaceLayouts } from "../../hooks/useWorkspaceLayouts";

// ---------------------------------------------------------------------------
// Constants — FR-14 SpaarkeAi 6-template filter
// ---------------------------------------------------------------------------

/**
 * The 6-template subset surfaced when the SpaarkeAi `WorkspacePaneMenu`
 * launches `WorkspaceLayoutWizard` via "+ New Workspace" (FR-14). Standalone
 * LegalWorkspace still sees all 9 templates because it invokes the wizard
 * without this parameter (FR-25 backwards-compat).
 *
 * Order matches the FR-14 specification. The wizard's `TemplateStep` renders
 * templates in canonical `LAYOUT_TEMPLATES` order regardless of the filter
 * array order, so this list is just a membership set.
 */
const SPAARKEAI_TEMPLATE_FILTER = [
  "2-col-equal",
  "3-row-mixed",
  "hero-2x2",
  "sidebar-main",
  "single-column",
  "single-column-5",
] as const;

/**
 * SessionStorage key used to persist the user's chosen active workspace
 * layout id when the menu's "Switch Workspace" picker is used. Consumed by
 * downstream Home-tab refetch hooks (out of scope for task 032; this task
 * lays the contract).
 */
const ACTIVE_LAYOUT_STORAGE_KEY = "spaarke.workspace.activeLayoutId";

/**
 * Custom DOM event name dispatched on `window` when the active workspace
 * layout is changed via the menu. The Home tab (or any subscriber) can listen
 * for this and refetch. Event detail carries `{ layoutId }`.
 */
const LAYOUT_CHANGED_EVENT = "spaarke:workspace-layout-changed";

// ---------------------------------------------------------------------------
// BFF DTO — imported from ../../hooks/useWorkspaceLayouts (Round 4 Fix 1)
// The local interface declaration was removed when the parallel-implementation
// `useWorkspaceLayoutsList` was replaced with the reused `useWorkspaceLayouts`
// hook. The DTO shape is identical to LegalWorkspace's WorkspaceLayoutDto —
// both consume `GET /api/workspace/layouts` returning the same BFF response.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface WorkspacePaneMenuProps {
  /**
   * Currently-open tabs from `WorkspaceTabManager.getSnapshot().tabs`.
   * Includes the Home tab (kind === "home") at index 0 by convention.
   */
  tabs: readonly WorkspaceTab[];
  /** The currently-active tab id, or `null` if no tab is active. */
  activeTabId: string | null;
  /** Called when a tab is selected from the menu. */
  onTabSelect: (tabId: string) => void;
  /** Called when the user clicks the `×` close affordance on a non-Home tab. */
  onTabClose: (tabId: string) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: "auto",
  },
  tabRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
    gap: tokens.spacingHorizontalS,
  },
  tabLabel: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flex: "1 1 auto",
    minWidth: 0,
  },
  closeButton: {
    flexShrink: 0,
    minWidth: "auto",
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  activeMarker: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  emptyHint: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
  },
  newWorkspace: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  spinnerRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Wizard launch helper — Xrm.Navigation.navigateTo
// ---------------------------------------------------------------------------

/**
 * Walk window → parent → top to locate Xrm. Matches the pattern used by
 * `LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` and the wizard's
 * own `main.tsx#getXrm`. Returns null in dev (Vite) environments where Xrm
 * is not available — callers should fall back to a no-op.
 */
function getXrm(): unknown {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
}

/**
 * Build the `data` query string for the wizard's `Xrm.Navigation.navigateTo`
 * payload. Accepts an optional `templateFilter` which is serialized as a
 * comma-separated list of layout template ids so the wizard's `main.tsx` can
 * parse it back into a typed `LayoutTemplateId[]`.
 *
 * The wizard's main.tsx must be plumbed to read `templateFilter` from the
 * parsed URLSearchParams; that plumbing is added in this task alongside the
 * menu component.
 */
function buildWizardDataParams(
  args: {
    mode: "create" | "edit" | "saveAs";
    bffBaseUrl: string;
    layoutId?: string | null;
    layoutTemplateId?: string | null;
    sectionsJson?: string | null;
    name?: string | null;
    templateFilter?: readonly string[];
  },
): string {
  const parts: string[] = [];
  parts.push(`mode=${encodeURIComponent(args.mode)}`);
  parts.push(`bffBaseUrl=${encodeURIComponent(args.bffBaseUrl)}`);
  if (args.layoutId) parts.push(`layoutId=${encodeURIComponent(args.layoutId)}`);
  if (args.layoutTemplateId) {
    parts.push(`layoutTemplateId=${encodeURIComponent(args.layoutTemplateId)}`);
  }
  if (args.sectionsJson) {
    parts.push(`sectionsJson=${encodeURIComponent(args.sectionsJson)}`);
  }
  if (args.name) parts.push(`name=${encodeURIComponent(args.name)}`);
  if (args.templateFilter && args.templateFilter.length > 0) {
    parts.push(
      `templateFilter=${encodeURIComponent(args.templateFilter.join(","))}`,
    );
  }
  return parts.join("&");
}

/**
 * Launch the WorkspaceLayoutWizard webresource via `Xrm.Navigation.navigateTo`.
 * No-op in dev environments where Xrm is unavailable; emits a console warning
 * so the developer sees the gap (FR-20 host-only navigation guard).
 *
 * Returns a promise that resolves when the wizard dialog closes; callers can
 * use this to re-fetch layouts. Resolves immediately (with `null`) if Xrm is
 * unavailable.
 */
async function launchWizard(args: {
  mode: "create" | "edit" | "saveAs";
  title: string;
  bffBaseUrl: string;
  layoutId?: string | null;
  layoutTemplateId?: string | null;
  sectionsJson?: string | null;
  name?: string | null;
  templateFilter?: readonly string[];
}): Promise<void> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrm() as any;
  if (!xrm?.Navigation?.navigateTo) {
    console.warn(
      "[WorkspacePaneMenu] Xrm.Navigation.navigateTo not available — running outside Dataverse host. Wizard launch is a no-op.",
    );
    return;
  }

  const data = buildWizardDataParams({
    mode: args.mode,
    bffBaseUrl: args.bffBaseUrl ?? "",
    layoutId: args.layoutId,
    layoutTemplateId: args.layoutTemplateId,
    sectionsJson: args.sectionsJson,
    name: args.name,
    templateFilter: args.templateFilter,
  });

  try {
    await xrm.Navigation.navigateTo(
      {
        pageType: "webresource",
        webresourceName: "sprk_workspacelayoutwizard",
        data,
      },
      {
        target: 2,
        width: { value: 60, unit: "%" },
        height: { value: 70, unit: "%" },
        title: args.title,
      },
    );
  } catch (err: unknown) {
    // User cancellation is normal; surface any unexpected error.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const code = (err as any)?.errorCode;
    if (code !== 2) {
      console.warn("[WorkspacePaneMenu] Wizard launch error:", err);
    }
  }
}

// ---------------------------------------------------------------------------
// Active-layout signaling — sessionStorage + window CustomEvent
// ---------------------------------------------------------------------------

/**
 * Persist the chosen active layout id and dispatch a `CustomEvent` on `window`
 * so downstream subscribers (e.g. WorkspaceHomeTab refetch hook) can react.
 * Defensive against SSR / non-DOM contexts by checking `window` first.
 */
function applyActiveLayout(layoutId: string): void {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage?.setItem(ACTIVE_LAYOUT_STORAGE_KEY, layoutId);
  } catch {
    // Storage may be disabled (privacy mode); ignore silently.
  }
  try {
    window.dispatchEvent(
      new CustomEvent(LAYOUT_CHANGED_EVENT, { detail: { layoutId } }),
    );
  } catch {
    // CustomEvent constructor unavailable in very old hosts; ignore.
  }
}

// ---------------------------------------------------------------------------
// WorkspacePaneMenu component
//
// Round 4 Fix 1 (2026-05-21): the inline `useWorkspaceLayoutsList` hook
// (task 081 reimplementation) was removed and replaced with the reused
// `useWorkspaceLayouts` hook in `../../hooks/useWorkspaceLayouts.ts` —
// the SpaarkeAi adaptation of LegalWorkspace's WORKING reference
// implementation. See file header for the reuse rationale + the hook
// file's JSDoc for the surgical adaptations.
// ---------------------------------------------------------------------------

/**
 * `WorkspacePaneMenu` — Fluent v9 Menu rendered in `<PaneHeader rightSlot>`
 * of the WorkspacePane. See file header for full FR / NFR mapping.
 */
export const WorkspacePaneMenu: React.FC<WorkspacePaneMenuProps> = ({
  tabs,
  activeTabId,
  onTabSelect,
  onTabClose,
}) => {
  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState(false);
  // Task 081 fix carried forward in Round 4 Fix 1: auth + bffBaseUrl come
  // from `useAiSession()` (defers until ready) rather than module-level
  // `authenticatedFetch` / `getBffBaseUrl()` which race the runtime-config
  // bootstrap and silently produce an empty menu. The reused
  // `useWorkspaceLayouts` hook accepts these as args and runs the WORKING
  // LegalWorkspace fetch pattern (cache-first hydration, parallel
  // list+default fetch, pinned-id resolution).
  const { authenticatedFetch, bffBaseUrl, isAuthenticated } = useAiSession();
  const { layouts, activeLayout, isLoading, refetch } = useWorkspaceLayouts({
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated,
  });
  // Round 4 Fix 4 (2026-05-21): used by handleLayoutSelect to dispatch a
  // `widget_load` event so the chosen workspace opens as a new tab via the
  // existing WorkspacePane → WorkspaceTabManager → resolveWorkspaceWidget
  // pipeline. The widget type is `'workspace'` and resolves to
  // WorkspaceLayoutWidget (which embeds LegalWorkspaceApp).
  const dispatch = useDispatchPaneEvent();

  // -------------------------------------------------------------------------
  // Derived: split tabs into Home (singular) + Open (newest first, non-Home)
  // -------------------------------------------------------------------------

  const homeTab = React.useMemo(
    () => tabs.find((t) => t.kind === "home") ?? null,
    [tabs],
  );
  /** Non-Home tabs sorted newest first (reverse insertion order). */
  const openTabs = React.useMemo(
    () => tabs.filter((t) => t.kind === "widget").slice().reverse(),
    [tabs],
  );

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

  const handleTabActivate = React.useCallback(
    (tabId: string) => {
      onTabSelect(tabId);
      setMenuOpen(false);
    },
    [onTabSelect],
  );

  const handleTabCloseClick = React.useCallback(
    (e: React.MouseEvent | React.KeyboardEvent, tabId: string) => {
      e.stopPropagation();
      e.preventDefault();
      onTabClose(tabId);
      // Keep the menu open so the user can close multiple tabs in a row.
    },
    [onTabClose],
  );

  const handleLayoutSelect = React.useCallback(
    (layoutId: string) => {
      // Persist the active layout id + dispatch the legacy
      // window-CustomEvent signal (kept for backwards compatibility with any
      // downstream subscribers still listening to it).
      applyActiveLayout(layoutId);

      // Round 4 Fix 4 (2026-05-21): dispatch `widget_load` so the chosen
      // workspace opens as a new tab via the existing tab pipeline. The
      // pane subscriber in `WorkspacePane.tsx` calls
      // `manager.addTab('workspace', { layoutId, layoutName }, displayName)`,
      // then `resolveWorkspaceWidget('workspace')` resolves to
      // `WorkspaceLayoutWidget`, which renders LegalWorkspaceApp with
      // `initialWorkspaceId={layoutId}` and `embedded`.
      //
      // We resolve the friendly layout name from the loaded `layouts` array
      // so the tab title matches what the user clicked. Fall back to
      // "Workspace" if (somehow) the id is unknown.
      const chosen = layouts.find((l) => l.id === layoutId);
      const layoutName = chosen?.name ?? "Workspace";
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
        widgetData: { layoutId, layoutName },
      });

      setMenuOpen(false);
    },
    [dispatch, layouts],
  );

  const handleCreateWorkspace = React.useCallback(async () => {
    setMenuOpen(false);
    await launchWizard({
      mode: "create",
      title: "Create New Workspace",
      bffBaseUrl,
      templateFilter: SPAARKEAI_TEMPLATE_FILTER,
    });
    refetch();
  }, [bffBaseUrl, refetch]);

  const handleEditWorkspace = React.useCallback(async () => {
    setMenuOpen(false);
    if (!activeLayout) {
      console.warn(
        "[WorkspacePaneMenu] No active layout — Edit current workspace is a no-op.",
      );
      return;
    }
    // System layouts use saveAs mode (clone-then-edit); user layouts use edit.
    const mode = activeLayout.isSystem ? "saveAs" : "edit";
    await launchWizard({
      mode,
      title: mode === "saveAs" ? "Save As New Workspace" : "Edit Workspace",
      bffBaseUrl,
      layoutId: activeLayout.id,
      layoutTemplateId:
        mode === "saveAs" ? activeLayout.layoutTemplateId : null,
      sectionsJson: mode === "saveAs" ? activeLayout.sectionsJson : null,
      name: mode === "saveAs" ? activeLayout.name : null,
      // SpaarkeAi consistently surfaces the 6-template subset.
      templateFilter: SPAARKEAI_TEMPLATE_FILTER,
    });
    refetch();
  }, [activeLayout, bffBaseUrl, refetch]);

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <Menu
      open={menuOpen}
      onOpenChange={(_, data) => setMenuOpen(data.open)}
      positioning="below-end"
    >
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Open workspace menu" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronDownRegular />}
            iconPosition="after"
            aria-label="Open workspace menu"
            className={styles.trigger}
            data-testid="workspace-pane-menu-trigger"
          >
            Workspace
          </Button>
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid="workspace-pane-menu-popover">
        <MenuList>
          {/* ───────────────────────────────────────────────────────────────
           * Section 1 — Open tabs (newest first), each with `×` close.
           * Renders an italic empty hint when no non-Home tabs are open.
           * ─────────────────────────────────────────────────────────────── */}
          <MenuGroupHeader>Open</MenuGroupHeader>
          {openTabs.length === 0 ? (
            <Text className={styles.emptyHint} data-testid="open-tabs-empty">
              No open tabs.
            </Text>
          ) : (
            openTabs.map((tab) => {
              const isActive = tab.id === activeTabId;
              return (
                <MenuItem
                  key={tab.id}
                  onClick={() => handleTabActivate(tab.id)}
                  data-testid={`open-tab-${tab.id}`}
                  icon={
                    isActive ? (
                      <CheckmarkRegular className={styles.activeMarker} />
                    ) : undefined
                  }
                >
                  <span className={styles.tabRow}>
                    <span className={styles.tabLabel}>{tab.displayName}</span>
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={<DismissRegular />}
                      className={styles.closeButton}
                      aria-label={`Close ${tab.displayName}`}
                      onClick={(e) => handleTabCloseClick(e, tab.id)}
                      data-testid={`open-tab-close-${tab.id}`}
                    />
                  </span>
                </MenuItem>
              );
            })
          )}

          <MenuDivider />

          {/* ───────────────────────────────────────────────────────────────
           * Section 2 — Home (pinned, non-closable). FR-13: no `×` affordance.
           * Only rendered when a Home tab is present (always true post task 030).
           * ─────────────────────────────────────────────────────────────── */}
          <MenuGroupHeader>Home</MenuGroupHeader>
          {homeTab ? (
            <MenuItem
              onClick={() => handleTabActivate(homeTab.id)}
              data-testid={`home-tab-${homeTab.id}`}
              icon={
                homeTab.id === activeTabId ? (
                  <CheckmarkRegular className={styles.activeMarker} />
                ) : (
                  <HomeRegular />
                )
              }
            >
              <span className={styles.tabLabel}>{homeTab.displayName}</span>
            </MenuItem>
          ) : (
            <Text className={styles.emptyHint} data-testid="home-tab-missing">
              Home is not installed.
            </Text>
          )}

          <MenuDivider />

          {/* ───────────────────────────────────────────────────────────────
           * Section 3 — Switch Workspace (all layouts) + "+ New Workspace".
           * ─────────────────────────────────────────────────────────────── */}
          <MenuGroupHeader>Switch Workspace</MenuGroupHeader>
          {isLoading ? (
            <div className={styles.spinnerRow} data-testid="layouts-loading">
              <Spinner size="tiny" label="Loading workspaces..." />
            </div>
          ) : layouts.length === 0 ? (
            <Text className={styles.emptyHint} data-testid="layouts-empty">
              No workspaces available.
            </Text>
          ) : (
            layouts.map((layout) => {
              const isActive = activeLayout?.id === layout.id;
              return (
                <MenuItem
                  key={layout.id}
                  onClick={() => handleLayoutSelect(layout.id)}
                  data-testid={`switch-workspace-${layout.id}`}
                  icon={
                    isActive ? (
                      <CheckmarkRegular className={styles.activeMarker} />
                    ) : undefined
                  }
                >
                  <span className={styles.tabLabel}>{layout.name}</span>
                </MenuItem>
              );
            })
          )}
          <MenuItem
            onClick={handleCreateWorkspace}
            data-testid="new-workspace-action"
            icon={<AddRegular className={styles.newWorkspace} />}
          >
            <span className={styles.newWorkspace}>+ New Workspace</span>
          </MenuItem>

          <MenuDivider />

          {/* ───────────────────────────────────────────────────────────────
           * Section 4 — Edit current workspace.
           * ─────────────────────────────────────────────────────────────── */}
          <MenuItem
            onClick={handleEditWorkspace}
            disabled={!activeLayout}
            data-testid="edit-current-workspace-action"
            icon={<EditRegular />}
          >
            Edit current workspace
          </MenuItem>
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

WorkspacePaneMenu.displayName = "WorkspacePaneMenu";
